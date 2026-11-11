using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceInvoicesViewModel : ObservableObject
{
    private readonly string currentUserName;
    private readonly ObservableCollection<MaintenanceOrderDesignItem> sharedOrders;
    private readonly Action? navigateToIntake;
    private readonly Action<MaintenanceOrderDesignItem>? openInMaintenanceActions;
    private readonly Action? notifyMaintenanceOrdersChanged;

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

    public IReadOnlyList<string> TechnicianFilterOptions { get; }

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

    // --- 5 Dynamic KPI Summaries computed on FilteredOrders ---
    public string InvoicesCountDisplay => $"{FilteredOrders.Count} فاتورة";

    public string InvoicesBreakdownSummary
    {
        get
        {
            var active = FilteredOrders.Count(o => o.IsActiveOrder);
            var delivered = FilteredOrders.Count(o => o.IsDelivered);
            var cancelled = FilteredOrders.Count(o => o.IsCancelled);
            return $"نشط: {active} · مسلّم: {delivered} · ملغى: {cancelled}";
        }
    }

    public string TotalServiceValueDisplay
    {
        get
        {
            var sum = FilteredOrders.Where(o => !o.IsCancelled).Sum(o => o.CurrentEffectiveCost);
            return PosFormatting.FormatCurrency(sum);
        }
    }

    public string ServiceBreakdownSubtext
    {
        get
        {
            var nonCancelled = FilteredOrders.Where(o => !o.IsCancelled).ToList();
            var partsSum = nonCancelled.Sum(o => o.PartsTotal);
            var laborSum = nonCancelled.Sum(o => o.LaborCost);
            var discountSum = nonCancelled.Sum(o => o.DiscountAmount);
            return $"قطع: {PosFormatting.FormatCurrency(partsSum)} · مصنعية: {PosFormatting.FormatCurrency(laborSum)} · خصم: {PosFormatting.FormatCurrency(discountSum)}";
        }
    }

    public string TotalNetProfitDisplay
    {
        get
        {
            var profit = FilteredOrders.Where(o => !o.IsCancelled).Sum(o => o.EstimatedProfit);
            return PosFormatting.FormatCurrency(profit);
        }
    }

    public string PartsCostAndMarginSubtext
    {
        get
        {
            var nonCancelled = FilteredOrders.Where(o => !o.IsCancelled).ToList();
            var revenue = nonCancelled.Sum(o => o.CurrentEffectiveCost);
            var profit = nonCancelled.Sum(o => o.EstimatedProfit);
            var partsCost = nonCancelled.Sum(o => o.PartsCostTotal);
            var margin = revenue > 0m ? Math.Round((profit / revenue) * 100m, 1) : 0m;
            return $"تكلفة القطع: {PosFormatting.FormatCurrency(partsCost)} · الهامش: {margin:0.#}%";
        }
    }

    public string TotalCollectedAllDisplay
    {
        get
        {
            var netCollected = FilteredOrders.Sum(o => o.TotalCollectedAll);
            return PosFormatting.FormatCurrency(netCollected);
        }
    }

    public string ChannelsBreakdownDisplay
    {
        get
        {
            var cash = FilteredOrders.Sum(o => o.TotalCashCollected);
            var vf = FilteredOrders.Sum(o => o.TotalVodafoneCollected);
            var ip = FilteredOrders.Sum(o => o.TotalInstaPayCollected);
            return $"كاش: {PosFormatting.FormatPlainNumber(cash)} · فودافون: {PosFormatting.FormatPlainNumber(vf)} · إنستاباي: {PosFormatting.FormatPlainNumber(ip)}";
        }
    }

    public string TotalRemainingDisplay
    {
        get
        {
            var remaining = FilteredOrders.Where(o => !o.IsCancelled).Sum(o => o.EstimatedRemaining);
            return PosFormatting.FormatCurrency(remaining);
        }
    }

    public string RemainingCountSubtext
    {
        get
        {
            var withRemaining = FilteredOrders.Count(o => !o.IsCancelled && o.HasEstimatedRemaining);
            var refunded = FilteredOrders.Sum(o => o.RefundedDepositAmount);
            return $"{withRemaining} فاتورة بها متبقي · مرتجع عربون: {PosFormatting.FormatCurrency(refunded)}";
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

    // Commands
    public ActionCommand GoToIntakeCommand { get; }
    public ActionCommand RefreshDataCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public ActionCommand FilterRemainingOnlyCommand { get; }
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
        Action? notifyMaintenanceOrdersChanged = null)
    {
        this.currentUserName = currentUserName;
        this.sharedOrders = sharedOrders;
        this.navigateToIntake = navigateToIntake;
        this.openInMaintenanceActions = openInMaintenanceActions;
        this.notifyMaintenanceOrdersChanged = notifyMaintenanceOrdersChanged;

        TechnicianFilterOptions = new[] { "كل الفنيين" }.Concat(MaintenanceDesignSeedData.Technicians).ToArray();
        this.sharedOrders.CollectionChanged += OnOrdersCollectionChanged;

        GoToIntakeCommand = new ActionCommand(() => this.navigateToIntake?.Invoke());
        RefreshDataCommand = new ActionCommand(() =>
        {
            NotifyExternalDataChanged();
            SetStatus($"تم تحديث سجل فواتير الصيانة ({FilteredOrders.Count} فاتورة مطابقة).", false);
        });
        ClearFiltersCommand = new ActionCommand(ClearAllFilters);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        FilterRemainingOnlyCommand = new ActionCommand(() =>
        {
            SelectedStatusFilter = "كل الحالات";
            SelectedSettlementFilter = SelectedSettlementFilter == "يوجد متبقي للتحصيل"
                ? "كل الحالات المالية"
                : "يوجد متبقي للتحصيل";
        });

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

    public void NotifyExternalDataChanged()
    {
        RefreshFilteredOrders();
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFilteredOrders();

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

        var detailsVm = new MaintenanceInvoiceDetailsViewModel(order, currentUserName)
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
            RefreshFilteredOrders();
            notifyMaintenanceOrdersChanged?.Invoke();
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
                order.ActivityLog.Add(new MaintenanceActivityEntry
                {
                    Timestamp = DateTime.Now,
                    ActionTitle = $"طباعة: {modeLabel}",
                    Details = $"معاينة وطباعة {modeLabel} من فواتير الصيانة {order.OrderNumber}.",
                    ActorName = currentUserName
                });
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

        var matches = sharedOrders.Where(order =>
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
                          || order.DepositVodafoneReference.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.DepositInstaPayReference.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.FinalPaymentVodafoneReference.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || order.FinalPaymentInstaPayReference.Contains(query, StringComparison.OrdinalIgnoreCase)
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
        NotifyKpis();
    }

    private void NotifyKpis()
    {
        Changed(nameof(InvoicesCountDisplay));
        Changed(nameof(InvoicesBreakdownSummary));
        Changed(nameof(TotalServiceValueDisplay));
        Changed(nameof(ServiceBreakdownSubtext));
        Changed(nameof(TotalNetProfitDisplay));
        Changed(nameof(PartsCostAndMarginSubtext));
        Changed(nameof(TotalCollectedAllDisplay));
        Changed(nameof(ChannelsBreakdownDisplay));
        Changed(nameof(TotalRemainingDisplay));
        Changed(nameof(RemainingCountSubtext));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
