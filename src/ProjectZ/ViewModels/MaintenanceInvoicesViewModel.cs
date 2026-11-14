using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceInvoicesViewModel : ObservableObject
{
    private readonly string currentUserName;
    // The invoices screen owns its own orders collection and loads it directly from the
    // database, so it has a self-contained backend independent of the intake screen.
    private readonly ObservableCollection<MaintenanceOrderDesignItem> seedOrders;
    private readonly Action? navigateToIntake;
    private readonly Action<MaintenanceOrderDesignItem>? openInMaintenanceActions;
    private readonly Action? notifyMaintenanceOrdersChanged;
    private readonly Data.MaintenanceRepository? repository;
    private readonly Action? onOrdersSaved;
    // Entries accumulated since the last DB save, grouped by order id so a failed save for one
    // invoice can never flush its activities or payments onto another invoice.
    private readonly List<(int OrderId, MaintenanceActivityEntry Entry)> pendingActivities = new();
    private readonly List<(int OrderId, Data.MaintenancePaymentEntry Entry)> pendingPayments = new();

    private MaintenanceOrderDesignItem? selectedOrder;
    private string searchText = "";
    private string selectedDateBasis = "تاريخ الاستلام";
    private string selectedDatePreset = "الكل";
    private DateTime? customFromDate = DateTime.Today.AddDays(-30);
    private DateTime? customToDate = DateTime.Today;
    private string selectedStatusFilter = "كل الحالات";
    private string selectedPriorityFilter = "كل الأولويات";
    private string selectedTechnicianFilter = "كل الفنيين";
    private string selectedPaymentFilter = "كل طرق الدفع";
    private string selectedSettlementFilter = "كل الحالات المالية";
    private string selectedSortOption = "الأحدث استلامًا";

    private string statusMessage = "فواتير الصيانة الشاملة: اضغط على أي فاتورة لعرض كافة تفاصيلها والبنود والسجل، أو استخدم الفلاتر المتقدمة للفرز والتحليل.";
    private bool isErrorStatus;
    private string[] technicianFilterOptions = ["كل الفنيين"];
    private DateTime? lastRefreshedAt;
    private bool lastLoadFailed;
    // A database reload clears and refills Orders item by item; filtering each time would be
    // O(n^2) work and would flash the summary strip. One refresh runs after the bulk load.
    private bool isBulkLoading;

    public ObservableCollection<MaintenanceOrderDesignItem> Orders { get; } = [];
    public ObservableCollection<MaintenanceOrderDesignItem> FilteredOrders { get; } = [];

    public IReadOnlyList<string> DatePresets { get; } =
        ["الكل", "اليوم", "آخر 7 أيام", "هذا الشهر", "فترة مخصصة"];

    public IReadOnlyList<string> StatusFilterOptions { get; } =
    [
        "كل الحالات",
        "النشطة بالورشة",
        "تم الاستلام",
        "قيد الفحص",
        "بانتظار قطع غيار",
        "قيد الصيانة",
        "جاهز للتسليم",
        "تم التسليم",
        "ملغى"
    ];

    public IReadOnlyList<string> PriorityFilterOptions { get; } =
        ["كل الأولويات", "مستعجل", "عادي", "ضمان / إعادة فحص"];

    public IReadOnlyList<string> TechnicianFilterOptions => technicianFilterOptions;

    public IReadOnlyList<string> PaymentFilterOptions { get; } =
    [
        "كل طرق الدفع",
        "كاش",
        "فودافون كاش",
        "إنستاباي",
        "مختلط (أكثر من وسيلة)",
        "بدون مدفوعات"
    ];

    public IReadOnlyList<string> SettlementFilterOptions { get; } =
    [
        "كل الحالات المالية",
        "يوجد متبقي للتحصيل",
        "مسدد بالكامل",
        "لم يُحدد السعر بعد",
        "يوجد عربون مرتجع / زائد"
    ];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "الأحدث استلامًا",
        "الأقدم استلامًا",
        "الأحدث تسليمًا",
        "الأعلى قيمة",
        "الأعلى متبقي للتحصيل",
        "الأعلى ربحًا"
    ];

    // Window callbacks wired by MaintenanceInvoicesView.xaml.cs
    public Action<MaintenanceInvoiceDetailsViewModel>? OpenDetailsDialogRequested { get; set; }
    public Func<MaintenanceDeliveryViewModel, bool?>? OpenDeliveryDialogRequested { get; set; }
    public Action<MaintenanceReceiptPreviewViewModel>? OpenReceiptPreviewRequested { get; set; }

    public MaintenanceOrderDesignItem? SelectedOrder
    {
        get => selectedOrder;
        set
        {
            if (ReferenceEquals(selectedOrder, value)) return;
            if (selectedOrder is not null)
                selectedOrder.IsInvoiceRowSelected = false;

            selectedOrder = value;
            if (selectedOrder is not null)
                selectedOrder.IsInvoiceRowSelected = true;

            Changed();
            Changed(nameof(HasSelectedOrder));
        }
    }

    public bool HasSelectedOrder => SelectedOrder is not null;
    public bool HasNoFilteredOrders => FilteredOrders.Count == 0;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public string SelectedDateBasis
    {
        get => selectedDateBasis;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedDateBasis == value) return;
            selectedDateBasis = value;
            Changed();
            Changed(nameof(IsReceivedDateBasis));
            Changed(nameof(IsDeliveryDateBasis));
            RefreshFilteredOrders();
        }
    }

    public bool IsReceivedDateBasis => SelectedDateBasis == "تاريخ الاستلام";
    public bool IsDeliveryDateBasis => SelectedDateBasis == "تاريخ التسليم";

    public string SelectedDatePreset
    {
        get => selectedDatePreset;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedDatePreset == value) return;
            selectedDatePreset = value;
            Changed();
            Changed(nameof(IsAllDatePreset));
            Changed(nameof(IsTodayPreset));
            Changed(nameof(IsLast7DaysPreset));
            Changed(nameof(IsThisMonthPreset));
            Changed(nameof(IsCustomDatePreset));
            RefreshFilteredOrders();
        }
    }

    public bool IsAllDatePreset => SelectedDatePreset == "الكل";
    public bool IsTodayPreset => SelectedDatePreset == "اليوم";
    public bool IsLast7DaysPreset => SelectedDatePreset == "آخر 7 أيام";
    public bool IsThisMonthPreset => SelectedDatePreset == "هذا الشهر";
    public bool IsCustomDatePreset => SelectedDatePreset == "فترة مخصصة";

    public DateTime? CustomFromDate
    {
        get => customFromDate;
        set
        {
            if (customFromDate == value) return;
            customFromDate = value;
            Changed();
            if (IsCustomDatePreset) RefreshFilteredOrders();
        }
    }

    public DateTime? CustomToDate
    {
        get => customToDate;
        set
        {
            if (customToDate == value) return;
            customToDate = value;
            Changed();
            if (IsCustomDatePreset) RefreshFilteredOrders();
        }
    }

    public string SelectedStatusFilter
    {
        get => selectedStatusFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedStatusFilter == value) return;
            selectedStatusFilter = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public string SelectedPriorityFilter
    {
        get => selectedPriorityFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedPriorityFilter == value) return;
            selectedPriorityFilter = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public string SelectedTechnicianFilter
    {
        get => selectedTechnicianFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedTechnicianFilter == value) return;
            selectedTechnicianFilter = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public string SelectedPaymentFilter
    {
        get => selectedPaymentFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedPaymentFilter == value) return;
            selectedPaymentFilter = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public string SelectedSettlementFilter
    {
        get => selectedSettlementFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedSettlementFilter == value) return;
            selectedSettlementFilter = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public string SelectedSortOption
    {
        get => selectedSortOption;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedSortOption == value) return;
            selectedSortOption = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value) return;
            statusMessage = value;
            Changed();
            Changed(nameof(HasStatusMessage));
        }
    }

    public bool IsErrorStatus
    {
        get => isErrorStatus;
        private set
        {
            if (isErrorStatus == value) return;
            isErrorStatus = value;
            Changed();
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    // ---------------------------------------------------------------- backend status

    /// <summary>True when the screen is bound to SQL Server (MainViewModel supplies the repository).</summary>
    public bool IsDatabaseConnected => repository is not null;

    /// <summary>True only for the design/sample mode used when no SQL connection is configured.</summary>
    public bool IsDesignMode => !IsDatabaseConnected;

    public string BackendStatusText => IsDatabaseConnected
        ? "متصل بقاعدة البيانات — SQL Server"
        : "معاينة تصميم — بدون قاعدة بيانات";

    public string BackendStatusDetail => IsDatabaseConnected
        ? "الفواتير تُقرأ من dbo.MaintenanceOrders، والتحصيل والتسليم وسجل الطباعة وقطع الغيار تُكتب في قاعدة البيانات."
        : "البيانات المعروضة تجريبية داخل الجلسة فقط ولا تُحفظ في قاعدة البيانات.";

    public string LastRefreshDisplay => lastRefreshedAt.HasValue
        ? $"آخر تحميل: {lastRefreshedAt.Value:yyyy-MM-dd HH:mm}"
        : (IsDatabaseConnected ? "لم يتم التحميل بعد" : "بيانات نموذجية");

    public string LoadedInvoicesCountDisplay => $"{Orders.Count}";

    public bool HasNoOrdersAtAll => Orders.Count == 0;

    /// <summary>Connected to SQL but the workshop has no saved orders yet.</summary>
    public bool IsDatabaseEmpty => IsDatabaseConnected && Orders.Count == 0;

    /// <summary>Orders exist but the current filters hide all of them (excludes the empty-database case).</summary>
    public bool ShowNoMatchEmptyState => FilteredOrders.Count == 0 && !IsDatabaseEmpty;

    // ---------------------------------------------------------------- register summary (visible list)

    // The screen deliberately shows a single compact summary line instead of the KPI cards that
    // were removed earlier from the maintenance screens (see tools/static_checks.py).
    public int ShownInvoicesCount => FilteredOrders.Count;
    public int ActiveCasesCount => FilteredOrders.Count(o => o.IsActiveOrder);
    public int DeliveredCasesCount => FilteredOrders.Count(o => o.IsDelivered);
    public int CancelledCasesCount => FilteredOrders.Count(o => o.IsCancelled);
    public int DueCollectionCasesCount => FilteredOrders.Count(o => !o.IsCancelled && o.HasEstimatedRemaining);

    public decimal ShownCollectedTotal => FilteredOrders.Where(o => !o.IsCancelled).Sum(o => o.TotalCollectedAll);
    public decimal ShownRemainingTotal => FilteredOrders.Where(o => !o.IsCancelled).Sum(o => o.EstimatedRemaining);
    public decimal ShownPartsCostTotal => FilteredOrders.Sum(o => o.PartsCostTotal);
    public decimal ShownProfitTotal => FilteredOrders.Where(o => !o.IsCancelled).Sum(o => o.EstimatedProfit);

    public string RegisterCountSummaryText
    {
        get
        {
            var all = Orders.Count;
            var scope = ShownInvoicesCount == all
                ? $"كل الفواتير ({all})"
                : $"{ShownInvoicesCount} من {all} فاتورة";
            return $"{scope} · نشطة بالورشة {ActiveCasesCount} · تم التسليم {DeliveredCasesCount}"
                   + (CancelledCasesCount > 0 ? $" · ملغاة {CancelledCasesCount}" : "")
                   + (DueCollectionCasesCount > 0 ? $" · بانتظار تحصيل {DueCollectionCasesCount}" : "");
        }
    }

    public string RegisterMoneySummaryText =>
        $"محصّل {PosFormatting.FormatCurrency(ShownCollectedTotal)}"
        + $" · متبقي {PosFormatting.FormatCurrency(ShownRemainingTotal)}"
        + $" · تكلفة القطع {PosFormatting.FormatCurrency(ShownPartsCostTotal)}"
        + $" · أرباح تقديرية {PosFormatting.FormatCurrency(ShownProfitTotal)}";

    // Commands
    public ActionCommand GoToIntakeCommand { get; }
    public ActionCommand RefreshDataCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public RelayCommand<string> SelectDateBasisCommand { get; }
    public RelayCommand<string> SelectDatePresetCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> OpenInvoiceDetailsCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> OpenPrintPreviewCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> OpenDeliverDialogCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> OpenInMaintenanceActionsCommand { get; }

    public MaintenanceInvoicesViewModel()
        : this(
            "المشرف",
            MaintenanceDesignSeedData.CreateSharedSampleOrders(PosDesignSeedData.CreateSampleCustomers()),
            null,
            null,
            null)
    {
    }

    public MaintenanceInvoicesViewModel(
        string currentUserName,
        ObservableCollection<MaintenanceOrderDesignItem> sharedOrders,
        Action? navigateToIntake = null,
        Action<MaintenanceOrderDesignItem>? openInMaintenanceActions = null,
        Action? notifyMaintenanceOrdersChanged = null,
        Data.MaintenanceRepository? repository = null,
        Action? onOrdersSaved = null)
    {
        this.currentUserName = currentUserName;
        this.seedOrders = sharedOrders;
        this.navigateToIntake = navigateToIntake;
        this.openInMaintenanceActions = openInMaintenanceActions;
        this.notifyMaintenanceOrdersChanged = notifyMaintenanceOrdersChanged;
        this.repository = repository;
        this.onOrdersSaved = onOrdersSaved;

        // Design mode (no repository): seed the screen with the sample orders. With a
        // repository the collection stays empty until NotifyExternalDataChanged loads DB.
        if (repository is null)
        {
            foreach (var order in seedOrders)
                Orders.Add(order);
        }

        technicianFilterOptions = BuildTechnicianFilterOptions();
        this.Orders.CollectionChanged += OnOrdersCollectionChanged;

        GoToIntakeCommand = new ActionCommand(() => this.navigateToIntake?.Invoke());
        RefreshDataCommand = new ActionCommand(() =>
        {
            NotifyExternalDataChanged();
            if (lastLoadFailed) return; // the load failure is already on the status strip.
            SetStatus($"تم تحديث سجل فواتير الصيانة ({FilteredOrders.Count} فاتورة مطابقة من {Orders.Count}).", false);
        });
        ClearFiltersCommand = new ActionCommand(ClearAllFilters);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");

        SelectDateBasisCommand = new RelayCommand<string>(basis =>
        {
            if (!string.IsNullOrWhiteSpace(basis))
                SelectedDateBasis = basis;
        });
        SelectDatePresetCommand = new RelayCommand<string>(preset =>
        {
            if (!string.IsNullOrWhiteSpace(preset))
                SelectedDatePreset = preset;
        });

        OpenInvoiceDetailsCommand = new RelayCommand<MaintenanceOrderDesignItem>(OpenInvoiceDetails);
        OpenPrintPreviewCommand = new RelayCommand<MaintenanceOrderDesignItem>(order => OpenPrintPreview(order, null));
        OpenDeliverDialogCommand = new RelayCommand<MaintenanceOrderDesignItem>(OpenDeliverDialog);
        OpenInMaintenanceActionsCommand = new RelayCommand<MaintenanceOrderDesignItem>(order =>
        {
            if (order is null) return;
            SelectedOrder = order;
            this.openInMaintenanceActions?.Invoke(order);
        });

        RefreshFilteredOrders();
    }

    /// <summary>
    /// Loads the invoice orders directly from the database into the screen's own collection and
    /// rebuilds the technician filter from the names that actually exist in SQL.
    /// </summary>
    public void NotifyExternalDataChanged()
    {
        var loadedOk = false;
        if (repository is not null)
        {
            isBulkLoading = true;
            try
            {
                repository.LoadMaintenanceOrdersInto(Orders);
                pendingActivities.Clear();
                pendingPayments.Clear();
                lastRefreshedAt = DateTime.Now;
                lastLoadFailed = false;
                Changed(nameof(LastRefreshDisplay));
                RefreshTechnicianFilterOptions();
                loadedOk = true;
            }
            catch (Exception ex)
            {
                lastLoadFailed = true;
                SetStatus($"تعذّر تحميل فواتير الصيانة من قاعدة البيانات: {ex.Message}", true);
            }
            finally
            {
                isBulkLoading = false;
            }
        }

        if (repository is null) lastLoadFailed = false;
        RefreshFilteredOrders();

        if (loadedOk)
        {
            SetStatus(
                Orders.Count == 0
                    ? "لا توجد أوامر صيانة محفوظة في قاعدة البيانات بعد. ابدأ بـ «أمر صيانة جديد» ثم ستظهر الفاتورة هنا تلقائيًا."
                    : $"تم تحميل {Orders.Count} فاتورة صيانة من قاعدة البيانات ({FilteredOrders.Count} مطابقة للفلاتر الحالية).",
                false);
        }
    }

    /// <summary>Builds the technician filter list from the sample names plus the names already loaded.</summary>
    private string[] BuildTechnicianFilterOptions()
    {
        var names = new List<string> { "كل الفنيين" };
        foreach (var name in MaintenanceDesignSeedData.Technicians)
            if (!names.Contains(name)) names.Add(name);
        foreach (var name in Orders.Select(o => o.AssignedTechnician))
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
        return names.ToArray();
    }

    /// <summary>
    /// Rebuilds the technician filter from SQL (distinct technicians on orders + active users)
    /// so the list never offers a name that cannot exist in the database.
    /// </summary>
    private void RefreshTechnicianFilterOptions()
    {
        var names = new List<string> { "كل الفنيين" };
        if (repository is not null)
        {
            try
            {
                foreach (var name in repository.LoadTechnicianDirectory())
                    if (!names.Contains(name)) names.Add(name);
            }
            catch (Data.MaintenanceDataException)
            {
                // The filter keeps the names already loaded; the status strip reports the failure.
            }
        }

        foreach (var name in MaintenanceDesignSeedData.Technicians)
            if (!names.Contains(name)) names.Add(name);
        foreach (var name in Orders.Select(o => o.AssignedTechnician).Where(n => !string.IsNullOrWhiteSpace(n)))
            if (!names.Contains(name)) names.Add(name);

        if (names.SequenceEqual(technicianFilterOptions)) return;

        technicianFilterOptions = names.ToArray();
        Changed(nameof(TechnicianFilterOptions));

        if (!technicianFilterOptions.Contains(selectedTechnicianFilter))
        {
            selectedTechnicianFilter = "كل الفنيين";
            Changed(nameof(SelectedTechnicianFilter));
        }

        RefreshFilteredOrders();
    }

    /// <summary>Flushes accumulated activities/payments and the order state to the database.</summary>
    /// <returns>True when the save succeeded (or no repository is connected).</returns>
    private bool TryPersistOrder(MaintenanceOrderDesignItem order, string successMessage, string errorPrefix)
    {
        if (repository is null)
            return true;

        // Only this invoice's pending rows are flushed; a failed save of another invoice must not
        // write its activities or payments onto this order.
        var activities = pendingActivities.Where(p => p.OrderId == order.Id).Select(p => p.Entry).ToList();
        var payments = pendingPayments.Where(p => p.OrderId == order.Id).Select(p => p.Entry).ToList();

        try
        {
            repository.UpdateMaintenanceOrder(order, activities, payments);
            pendingActivities.RemoveAll(p => p.OrderId == order.Id);
            pendingPayments.RemoveAll(p => p.OrderId == order.Id);
            onOrdersSaved?.Invoke();
            SetStatus(successMessage, false);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"{errorPrefix}: {ex.Message}", true);
            return false;
        }
    }

    /// <summary>Records an activity both in the in-memory log and the pending DB batch.</summary>
    private void RecordActivity(MaintenanceOrderDesignItem order, MaintenanceActivityEntry entry)
    {
        order.ActivityLog.Add(entry);
        pendingActivities.Add((order.Id, entry));
    }

    /// <summary>
    /// Queues a payment for the database and mirrors it into the invoice's payment history so the
    /// open details window shows the row immediately; the next reload reads the same row back.
    /// </summary>
    private void AddPendingPayment(
        MaintenanceOrderDesignItem order, FinancialAccountKind account, int stage, int kind, decimal amount, string note)
    {
        if (amount <= 0m) return;

        var paidAt = DateTime.Now;
        pendingPayments.Add((order.Id, new Data.MaintenancePaymentEntry(account, stage, kind, amount, paidAt, note)));
        order.PaymentHistory.Insert(0, new MaintenancePaymentHistoryItem
        {
            PaidAt = paidAt,
            Stage = stage,
            Kind = kind,
            Account = account,
            Amount = amount,
            Note = note,
            ActorName = currentUserName
        });
        order.NotifyFinancialsChanged();
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (isBulkLoading) return;
        RefreshFilteredOrders();
    }

    private void ClearAllFilters()
    {
        searchText = "";
        selectedDateBasis = "تاريخ الاستلام";
        selectedDatePreset = "الكل";
        customFromDate = DateTime.Today.AddDays(-30);
        customToDate = DateTime.Today;
        selectedStatusFilter = "كل الحالات";
        selectedPriorityFilter = "كل الأولويات";
        selectedTechnicianFilter = "كل الفنيين";
        selectedPaymentFilter = "كل طرق الدفع";
        selectedSettlementFilter = "كل الحالات المالية";
        selectedSortOption = "الأحدث استلامًا";

        Changed(nameof(SearchText));
        Changed(nameof(SelectedDateBasis));
        Changed(nameof(IsReceivedDateBasis));
        Changed(nameof(IsDeliveryDateBasis));
        Changed(nameof(SelectedDatePreset));
        Changed(nameof(IsAllDatePreset));
        Changed(nameof(IsTodayPreset));
        Changed(nameof(IsLast7DaysPreset));
        Changed(nameof(IsThisMonthPreset));
        Changed(nameof(IsCustomDatePreset));
        Changed(nameof(CustomFromDate));
        Changed(nameof(CustomToDate));
        Changed(nameof(SelectedStatusFilter));
        Changed(nameof(SelectedPriorityFilter));
        Changed(nameof(SelectedTechnicianFilter));
        Changed(nameof(SelectedPaymentFilter));
        Changed(nameof(SelectedSettlementFilter));
        Changed(nameof(SelectedSortOption));

        RefreshFilteredOrders();
        SetStatus("تمت إعادة ضبط جميع فلاتر فواتير الصيانة.", false);
    }

    private void OpenInvoiceDetails(MaintenanceOrderDesignItem? order)
    {
        if (order is null) return;
        SelectedOrder = order;

        var detailsVm = new MaintenanceInvoiceDetailsViewModel(order, currentUserName, repository)
        {
            OpenDeliveryDialogRequested = vm => OpenDeliveryDialogRequested?.Invoke(vm),
            OpenReceiptPreviewRequested = vm => OpenReceiptPreviewRequested?.Invoke(vm),
            OpenInActionsRequested = target => openInMaintenanceActions?.Invoke(target),
            NotifyOrdersChanged = () =>
            {
                RefreshFilteredOrders();
                notifyMaintenanceOrdersChanged?.Invoke();
            }
        };

        OpenDetailsDialogRequested?.Invoke(detailsVm);
        RefreshFilteredOrders();
    }

    private void OpenDeliverDialog(MaintenanceOrderDesignItem? order)
    {
        if (order is null) return;
        SelectedOrder = order;

        if (order.IsCancelled)
        {
            SetStatus($"الفاتورة {order.OrderNumber} ملغاة؛ أعد فتحها من «إجراءات الصيانة» أولًا.", true);
            return;
        }

        if (order.IsDelivered)
        {
            OpenPrintPreview(order, MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt);
            return;
        }

        var vm = new MaintenanceDeliveryViewModel(order, currentUserName);
        var result = OpenDeliveryDialogRequested?.Invoke(vm);
        if (result == true || vm.IsConfirmed)
        {
            var cashBefore = order.FinalPaymentCashPaid;
            var vodBefore = order.FinalPaymentVodafonePaid;
            var instaBefore = order.FinalPaymentInstaPayPaid;
            var refundBefore = order.RefundedDepositAmount;

            RefreshFilteredOrders();
            notifyMaintenanceOrdersChanged?.Invoke();

            var cashNew = order.FinalPaymentCashPaid - cashBefore;
            var vodNew = order.FinalPaymentVodafonePaid - vodBefore;
            var instaNew = order.FinalPaymentInstaPayPaid - instaBefore;
            var refundNew = order.RefundedDepositAmount - refundBefore;
            var totalNewCollected = cashNew + vodNew + instaNew;

            // stage 2 = final settlement, kind 1 = collected, kind 2 = refunded.
            AddPendingPayment(order, FinancialAccountKind.Cash, 2, 1, cashNew, "دفعة تسليم - كاش");
            AddPendingPayment(order, FinancialAccountKind.VodafoneCash, 2, 1, vodNew, "دفعة تسليم - فودافون كاش");
            AddPendingPayment(order, FinancialAccountKind.InstaPay, 2, 1, instaNew, "دفعة تسليم - إنستاباي");
            AddPendingPayment(order, FinancialAccountKind.Cash, 2, 2, refundNew, "رد فرق عربون عند التسليم");

            var summaryParts = new List<string>
            {
                $"التكلفة النهائية: {order.EstimatedCostDisplay}",
                vm.WarrantyDays > 0 ? $"ضمان {vm.WarrantyDays} يومًا" : "بدون ضمان"
            };
            if (totalNewCollected > 0m)
                summaryParts.Add($"تحصيل عند التسليم: {PosFormatting.FormatCurrency(totalNewCollected)}");
            if (refundNew > 0m)
                summaryParts.Add($"رد فرق عربون: {PosFormatting.FormatCurrency(refundNew)}");
            if (!string.IsNullOrWhiteSpace(order.DeliveryNotes))
                summaryParts.Add(order.DeliveryNotes);

            RecordActivity(order, new MaintenanceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "تسليم الجهاز وإغلاق أمر الصيانة",
                Details = string.Join(" · ", summaryParts),
                ActorName = currentUserName,
                Amount = totalNewCollected > 0m ? totalNewCollected : order.CurrentEffectiveCost
            });

            if (repository is not null)
                TryPersistOrder(
                    order,
                    $"تم تسليم الجهاز وإغلاق الفاتورة {order.OrderNumber} ({order.WarrantySummaryDisplay}).",
                    "تعذّر حفظ التسليم");
            else
                SetStatus(
                    $"تم تسليم الجهاز وإغلاق الفاتورة {order.OrderNumber} ({order.WarrantySummaryDisplay}).",
                    false);

            if (vm.ShouldOpenPrintAfterClose)
                OpenPrintPreview(order, MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt);
        }
    }

    private void OpenPrintPreview(MaintenanceOrderDesignItem? order, MaintenanceReceiptPrintMode? explicitMode)
    {
        if (order is null) return;
        SelectedOrder = order;

        var initialMode = explicitMode ?? (order.IsDelivered || order.IsReadyForPickup
            ? MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt
            : MaintenanceReceiptPrintMode.CustomerIntakeReceipt);

        var vm = new MaintenanceReceiptPreviewViewModel(
            order,
            modeLabel =>
            {
                RecordActivity(order, new MaintenanceActivityEntry
                {
                    Timestamp = DateTime.Now,
                    ActionTitle = $"طباعة: {modeLabel}",
                    Details = $"معاينة وطباعة {modeLabel} من فواتير الصيانة {order.OrderNumber}.",
                    ActorName = currentUserName
                });

                if (repository is not null && order.Id > 0)
                    TryPersistOrder(
                        order,
                        $"تمت معاينة طباعة الفاتورة {order.OrderNumber} ({modeLabel}).",
                        "تعذّر حفظ سجل الطباعة");
                else
                    SetStatus($"تمت معاينة طباعة الفاتورة {order.OrderNumber} ({modeLabel}).", false);
            },
            initialMode);

        OpenReceiptPreviewRequested?.Invoke(vm);
    }

    private void RefreshFilteredOrders()
    {
        var query = SearchText.Trim();
        var today = DateTime.Today;
        var currentSelected = SelectedOrder;

        DateTime? normalizedFrom = CustomFromDate?.Date;
        DateTime? normalizedTo = CustomToDate?.Date;
        if (normalizedFrom.HasValue && normalizedTo.HasValue && normalizedFrom.Value > normalizedTo.Value)
            (normalizedFrom, normalizedTo) = (normalizedTo, normalizedFrom);

        var matches = Orders.Where(order =>
        {
            // Date basis + date preset filter
            DateTime? targetDate = IsDeliveryDateBasis ? order.DeliveredAt?.Date : order.ReceivedAt.Date;
            if (IsDeliveryDateBasis && !targetDate.HasValue)
                return false;

            if (targetDate.HasValue)
            {
                var d = targetDate.Value;
                if (IsTodayPreset && d != today) return false;
                if (IsLast7DaysPreset && (d < today.AddDays(-6) || d > today)) return false;
                if (IsThisMonthPreset && (d.Year != today.Year || d.Month != today.Month)) return false;
                if (IsCustomDatePreset)
                {
                    if (normalizedFrom.HasValue && d < normalizedFrom.Value) return false;
                    if (normalizedTo.HasValue && d > normalizedTo.Value) return false;
                }
            }

            // Stage / Status filter
            var stageMatch = SelectedStatusFilter switch
            {
                "النشطة بالورشة" => order.IsActiveOrder,
                "تم الاستلام" => order.IsReceived,
                "قيد الفحص" => order.IsDiagnosing,
                "بانتظار قطع غيار" => order.IsWaitingParts,
                "قيد الصيانة" => order.IsInRepair,
                "جاهز للتسليم" => order.IsReadyForPickup,
                "تم التسليم" => order.IsDelivered,
                "ملغى" => order.IsCancelled,
                _ => true
            };
            if (!stageMatch) return false;

            // Priority filter
            var priorityMatch = SelectedPriorityFilter switch
            {
                "مستعجل" => order.IsUrgent,
                "عادي" => order.Priority == MaintenancePriority.Normal,
                "ضمان / إعادة فحص" => order.IsWarranty,
                _ => true
            };
            if (!priorityMatch) return false;

            // Technician filter
            if (SelectedTechnicianFilter != "كل الفنيين"
                && !string.Equals(order.AssignedTechnician, SelectedTechnicianFilter, StringComparison.Ordinal))
                return false;

            // Payment channel filter
            var paymentMatch = SelectedPaymentFilter switch
            {
                "كاش" => order.HasAnyCashCollected && !order.HasAnyVodafoneCollected && !order.HasAnyInstaPayCollected,
                "فودافون كاش" => order.HasAnyVodafoneCollected && !order.HasAnyCashCollected && !order.HasAnyInstaPayCollected,
                "إنستاباي" => order.HasAnyInstaPayCollected && !order.HasAnyCashCollected && !order.HasAnyVodafoneCollected,
                "مختلط (أكثر من وسيلة)" => order.IsMultiChannelPayment,
                "بدون مدفوعات" => !order.HasAnyCashCollected && !order.HasAnyVodafoneCollected && !order.HasAnyInstaPayCollected,
                _ => true
            };
            if (!paymentMatch) return false;

            // Financial settlement filter
            var settlementMatch = SelectedSettlementFilter switch
            {
                "يوجد متبقي للتحصيل" => !order.IsCancelled && order.HasEstimatedRemaining,
                "مسدد بالكامل" => !order.IsCancelled && !order.IsPriceStillPending && order.EstimatedRemaining == 0m,
                "لم يُحدد السعر بعد" => !order.IsCancelled && order.IsPriceStillPending,
                "يوجد عربون مرتجع / زائد" => order.RefundedDepositAmount > 0m || order.OverpaidDepositAmount > 0m,
                _ => true
            };
            if (!settlementMatch) return false;

            // Comprehensive Search
            if (!string.IsNullOrEmpty(query))
            {
                var hit = order.OrderNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.CustomerPhone.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.DeviceFullTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.DeviceType.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.SerialOrImei.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.ReportedFault.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.DiagnosisNotes.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.AssignedTechnician.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.UsedParts.Any(p =>
                              p.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase)
                              || p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase));

                if (!hit) return false;
            }

            return true;
        });

        IEnumerable<MaintenanceOrderDesignItem> ordered = SelectedSortOption switch
        {
            "الأقدم استلامًا" => matches.OrderBy(o => o.ReceivedAt).ThenBy(o => o.Id),
            "الأحدث تسليمًا" => matches.OrderByDescending(o => o.DeliveredAt ?? DateTime.MinValue).ThenByDescending(o => o.ReceivedAt),
            "الأعلى قيمة" => matches.OrderByDescending(o => o.IsCancelled ? -1m : o.CurrentEffectiveCost).ThenByDescending(o => o.ReceivedAt),
            "الأعلى متبقي للتحصيل" => matches.OrderByDescending(o => o.IsCancelled ? -1m : o.EstimatedRemaining).ThenByDescending(o => o.ReceivedAt),
            "الأعلى ربحًا" => matches.OrderByDescending(o => o.IsCancelled ? decimal.MinValue : o.EstimatedProfit).ThenByDescending(o => o.ReceivedAt),
            _ => matches.OrderByDescending(o => o.ReceivedAt).ThenByDescending(o => o.Id)
        };

        var orderedList = ordered.ToList();
        FilteredOrders.Clear();
        foreach (var item in orderedList)
            FilteredOrders.Add(item);

        if (currentSelected is not null && !FilteredOrders.Contains(currentSelected))
            SelectedOrder = null;

        Changed(nameof(HasNoFilteredOrders));
        NotifySummaryChanged();
    }

    /// <summary>Raises change notifications for the summary strip above the register.</summary>
    private void NotifySummaryChanged()
    {
        Changed(nameof(ShownInvoicesCount));
        Changed(nameof(ActiveCasesCount));
        Changed(nameof(DeliveredCasesCount));
        Changed(nameof(CancelledCasesCount));
        Changed(nameof(DueCollectionCasesCount));
        Changed(nameof(ShownCollectedTotal));
        Changed(nameof(ShownRemainingTotal));
        Changed(nameof(ShownPartsCostTotal));
        Changed(nameof(ShownProfitTotal));
        Changed(nameof(RegisterCountSummaryText));
        Changed(nameof(RegisterMoneySummaryText));
        Changed(nameof(LoadedInvoicesCountDisplay));
        Changed(nameof(HasNoOrdersAtAll));
        Changed(nameof(IsDatabaseEmpty));
        Changed(nameof(ShowNoMatchEmptyState));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
