using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceActionsViewModel : ObservableObject
{
    private readonly string currentUserName;
    private readonly ObservableCollection<MaintenanceOrderDesignItem> sharedOrders;
    private readonly ObservableCollection<MaintenanceSparePartItem> sharedSpareParts;
    private readonly Action? navigateToIntake;
    private readonly Action? notifySparePartsChanged;
    private readonly Data.MaintenanceRepository? repository;
    private readonly Action? onOrdersSaved;
    // Entries accumulated since the last DB save; flushed together with the order update.
    private readonly List<MaintenanceActivityEntry> pendingActivities = new();
    private readonly List<Data.MaintenancePaymentEntry> pendingPayments = new();

    private MaintenanceOrderDesignItem? selectedOrder;
    private string searchText = "";
    private string selectedStageFilter = "كل الحالات";
    private string selectedPriorityFilter = "كل الأولويات";
    private string selectedTechnicianFilter = "كل الفنيين";
    private int activeControlTabIndex;

    // Workshop editable fields for the currently selected order
    private MaintenancePriority editPriority;
    private string editAssignedTechnician = "";
    private DateTime? editExpectedDeliveryDate;
    private string editExpectedDeliveryLabel = "";
    private string editDiagnosisNotes = "";
    private string editLaborCostText = "0";
    private string editDiscountText = "0";

    // Spare parts management (maintenance-only spare parts stock + quick manual item)
    private bool isCatalogPartInputMode = true;
    private string partSearchText = "";
    private MaintenanceSparePartItem? selectedCatalogPart;
    private string catalogPartQuantityText = "1";
    private string manualPartName = "";
    private string manualPartCostText = "0";
    private string manualPartPriceText = "0";
    private string manualPartQtyText = "1";

    // Additional mid-repair deposit collection
    private MaintenanceDepositMode extraDepositMode = MaintenanceDepositMode.CashOnly;
    private string extraDepositAmountText = "";

    private string statusMessage = "";
    private bool isErrorStatus;
    private bool isBulkReload;
    private DateTime? lastSyncedAt;

    public ObservableCollection<MaintenanceOrderDesignItem> FilteredOrders { get; } = [];
    public ObservableCollection<MaintenanceSparePartItem> FilteredCatalogParts { get; } = [];

    /// <summary>
    /// Technician names offered by the workshop editor. With SQL Server behind the screen the list is
    /// read from dbo.MaintenanceOrders and dbo.Users (see <see cref="Data.MaintenanceRepository.LoadTechnicianDirectory"/>),
    /// so the drop-down can never offer a name that has no row behind it. It falls back to the design
    /// samples only when no database is connected.
    /// </summary>
    public ObservableCollection<string> Technicians { get; } = [];

    public IReadOnlyList<string> DeliverySlotOptions => MaintenanceDesignSeedData.DeliverySlotOptions;
    public IReadOnlyList<string> DiagnosisSnippets => MaintenanceDesignSeedData.DiagnosisSnippets;
    public IReadOnlyList<string> StageFilterOptions { get; } =
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
    public ObservableCollection<string> TechnicianFilterOptions { get; } = [];

    public MaintenanceOrderDesignItem? SelectedOrder
    {
        get => selectedOrder;
        set
        {
            if (ReferenceEquals(selectedOrder, value)) return;
            selectedOrder = value;
            LoadEditableFieldsFromSelected();
            Changed();
            Changed(nameof(HasSelectedOrder));
            Changed(nameof(HasNoSelectedOrder));
            Changed(nameof(CanEditSelectedWorkshopFields));
            Changed(nameof(SelectedOrderSummaryTitle));
        }
    }

    /// <summary>True when this screen reads and writes SQL Server; the header badge follows it.</summary>
    public bool IsDatabaseBacked => repository is not null;

    public string DataConnectionBadgeText => IsDatabaseBacked
        ? "متصل بقاعدة البيانات — SQL Server"
        : "وضع عرض — بدون قاعدة بيانات";

    public string LastSyncedDisplay => lastSyncedAt is { } at
        ? $"آخر مزامنة: {at:yyyy-MM-dd HH:mm:ss}"
        : IsDatabaseBacked
            ? "لم تتم المزامنة بعد"
            : "بيانات تجريبية في الذاكرة";

    public bool HasSelectedOrder => SelectedOrder is not null;
    public bool HasNoSelectedOrder => SelectedOrder is null;
    public bool CanEditSelectedWorkshopFields => SelectedOrder is not null && SelectedOrder.IsActiveOrder;

    public string SelectedOrderSummaryTitle => SelectedOrder is null
        ? "اختر جهازًا من القائمة للتحكم"
        : $"{SelectedOrder.OrderNumber} — {SelectedOrder.DeviceFullTitle}";

    public int ActiveControlTabIndex
    {
        get => activeControlTabIndex;
        set
        {
            if (activeControlTabIndex == value) return;
            activeControlTabIndex = value;
            Changed();
            Changed(nameof(IsDiagnosisTabSelected));
            Changed(nameof(IsPartsAndPricingTabSelected));
            Changed(nameof(IsFinancialsTabSelected));
            Changed(nameof(IsActivityTabSelected));
        }
    }

    public bool IsDiagnosisTabSelected => ActiveControlTabIndex == 0;
    public bool IsPartsAndPricingTabSelected => ActiveControlTabIndex == 1;
    public bool IsFinancialsTabSelected => ActiveControlTabIndex == 2;
    public bool IsActivityTabSelected => ActiveControlTabIndex == 3;

    // --- Pipeline Stage Counts & Workshop KPIs ---
    public int AllCount => sharedOrders.Count;
    public int ActiveOrdersCount => sharedOrders.Count(o => o.IsActiveOrder);
    public int ReceivedCount => sharedOrders.Count(o => o.IsReceived);
    public int DiagnosingCount => sharedOrders.Count(o => o.IsDiagnosing);
    public int WaitingPartsCount => sharedOrders.Count(o => o.IsWaitingParts);
    public int InRepairCount => sharedOrders.Count(o => o.IsInRepair);
    public int ReadyForPickupCount => sharedOrders.Count(o => o.IsReadyForPickup);
    public int DeliveredCount => sharedOrders.Count(o => o.IsDelivered);
    public int CancelledCount => sharedOrders.Count(o => o.IsCancelled);
    public int UrgentActiveCount => sharedOrders.Count(o => o.IsActiveOrder && o.IsUrgent);

    public string ExpectedCollectionDisplay => PosFormatting.FormatCurrency(
        sharedOrders.Where(o => o.IsActiveOrder).Sum(o => o.EstimatedRemaining));

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

    public string SelectedStageFilter
    {
        get => selectedStageFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedStageFilter == value) return;
            selectedStageFilter = value;
            Changed();
            NotifyStageFilterSelection();
            RefreshFilteredOrders();
        }
    }

    public bool IsFilterAll => SelectedStageFilter is "الكل" or "كل الحالات";
    public bool IsFilterActive => SelectedStageFilter is "النشطة" or "النشطة بالورشة";
    public bool IsFilterReceived => SelectedStageFilter == "تم الاستلام";
    public bool IsFilterDiagnosing => SelectedStageFilter == "قيد الفحص";
    public bool IsFilterWaitingParts => SelectedStageFilter == "بانتظار قطع غيار";
    public bool IsFilterInRepair => SelectedStageFilter == "قيد الصيانة";
    public bool IsFilterReady => SelectedStageFilter == "جاهز للتسليم";
    public bool IsFilterDelivered => SelectedStageFilter == "تم التسليم";
    public bool IsFilterCancelled => SelectedStageFilter == "ملغى";

    public string SelectedPriorityFilter
    {
        get => selectedPriorityFilter;
        set
        {
            if (selectedPriorityFilter == value) return;
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
            // Rebuilding the ComboBox items (a SQL directory reload) clears SelectedItem and would push
            // null/empty back here; an empty filter must never turn into "match nobody".
            if (string.IsNullOrWhiteSpace(value) || selectedTechnicianFilter == value) return;
            selectedTechnicianFilter = value;
            Changed();
            RefreshFilteredOrders();
        }
    }

    public bool HasNoFilteredOrders => FilteredOrders.Count == 0;

    // --- Selected Order Editable Workshop Properties ---
    public MaintenancePriority EditPriority
    {
        get => editPriority;
        set
        {
            if (editPriority == value) return;
            editPriority = value;
            Changed();
            Changed(nameof(IsEditNormalPriority));
            Changed(nameof(IsEditUrgentPriority));
            Changed(nameof(IsEditWarrantyPriority));
        }
    }

    public bool IsEditNormalPriority => EditPriority == MaintenancePriority.Normal;
    public bool IsEditUrgentPriority => EditPriority == MaintenancePriority.Urgent;
    public bool IsEditWarrantyPriority => EditPriority == MaintenancePriority.WarrantyRecheck;

    public string EditAssignedTechnician
    {
        get => editAssignedTechnician;
        set
        {
            if (editAssignedTechnician == value) return;
            editAssignedTechnician = value;
            Changed();
        }
    }

    public DateTime? EditExpectedDeliveryDate
    {
        get => editExpectedDeliveryDate;
        set
        {
            if (editExpectedDeliveryDate == value) return;
            editExpectedDeliveryDate = value;
            Changed();
        }
    }

    public string EditExpectedDeliveryLabel
    {
        get => editExpectedDeliveryLabel;
        set
        {
            if (editExpectedDeliveryLabel == value) return;
            editExpectedDeliveryLabel = value;
            Changed();
        }
    }

    public string EditDiagnosisNotes
    {
        get => editDiagnosisNotes;
        set
        {
            if (editDiagnosisNotes == value) return;
            editDiagnosisNotes = value;
            Changed();
        }
    }

    public string EditLaborCostText
    {
        get => editLaborCostText;
        set
        {
            if (editLaborCostText == value) return;
            editLaborCostText = value;
            Changed();
            Changed(nameof(LiveComputedTotalDisplay));
        }
    }

    public string EditDiscountText
    {
        get => editDiscountText;
        set
        {
            if (editDiscountText == value) return;
            editDiscountText = value;
            Changed();
            Changed(nameof(LiveComputedTotalDisplay));
        }
    }

    public string LiveComputedTotalDisplay
    {
        get
        {
            if (SelectedOrder is null) return PosFormatting.FormatCurrency(0m);
            var labor = PosFormatting.TryParseAmount(EditLaborCostText, out var l) && l >= 0m ? l : 0m;
            var disc = PosFormatting.TryParseAmount(EditDiscountText, out var d) && d >= 0m ? d : 0m;
            return PosFormatting.FormatCurrency(Math.Max(0m, SelectedOrder.PartsTotal + labor - disc));
        }
    }

    // --- Spare Parts Controls ---
    public bool IsCatalogPartInputMode
    {
        get => isCatalogPartInputMode;
        private set
        {
            if (isCatalogPartInputMode == value) return;
            isCatalogPartInputMode = value;
            Changed();
            Changed(nameof(IsManualPartInputMode));
        }
    }

    public bool IsManualPartInputMode => !IsCatalogPartInputMode;

    public string PartSearchText
    {
        get => partSearchText;
        set
        {
            if (partSearchText == value) return;
            partSearchText = value;
            Changed();
            RefreshFilteredCatalogParts();
        }
    }

    public MaintenanceSparePartItem? SelectedCatalogPart
    {
        get => selectedCatalogPart;
        set
        {
            if (ReferenceEquals(selectedCatalogPart, value)) return;
            selectedCatalogPart = value;
            Changed();
            Changed(nameof(HasSelectedCatalogPart));
        }
    }

    public bool HasSelectedCatalogPart => SelectedCatalogPart is not null;

    public string CatalogPartQuantityText
    {
        get => catalogPartQuantityText;
        set
        {
            if (catalogPartQuantityText == value) return;
            catalogPartQuantityText = value;
            Changed();
        }
    }

    public string ManualPartName
    {
        get => manualPartName;
        set
        {
            if (manualPartName == value) return;
            manualPartName = value;
            Changed();
        }
    }

    public string ManualPartCostText
    {
        get => manualPartCostText;
        set
        {
            if (manualPartCostText == value) return;
            manualPartCostText = value;
            Changed();
        }
    }

    public string ManualPartPriceText
    {
        get => manualPartPriceText;
        set
        {
            if (manualPartPriceText == value) return;
            manualPartPriceText = value;
            Changed();
        }
    }

    public string ManualPartQtyText
    {
        get => manualPartQtyText;
        set
        {
            if (manualPartQtyText == value) return;
            manualPartQtyText = value;
            Changed();
        }
    }

    // --- Extra Deposit Inputs ---
    public bool IsExtraDepositCash => extraDepositMode == MaintenanceDepositMode.CashOnly;
    public bool IsExtraDepositVodafone => extraDepositMode == MaintenanceDepositMode.VodafoneCashOnly;
    public bool IsExtraDepositInstaPay => extraDepositMode == MaintenanceDepositMode.InstaPayOnly;

    public string ExtraDepositAmountText
    {
        get => extraDepositAmountText;
        set
        {
            if (extraDepositAmountText == value) return;
            extraDepositAmountText = value;
            Changed();
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

    // Window callbacks wired by MaintenanceActionsView.xaml.cs
    public Func<MaintenanceDeliveryViewModel, bool?>? OpenDeliveryDialogRequested { get; set; }
    public Func<MaintenanceCancelOrderViewModel, bool?>? OpenCancelDialogRequested { get; set; }
    public Action<MaintenanceReceiptPreviewViewModel>? OpenReceiptPreviewRequested { get; set; }

    // Commands
    public ActionCommand GoToIntakeCommand { get; }
    public ActionCommand RefreshDataCommand { get; }
    public RelayCommand<string> SetStageFilterCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> SelectOrderCommand { get; }
    public RelayCommand<string> SelectTabCommand { get; }

    public ActionCommand AdvanceToNextStageCommand { get; }
    public RelayCommand<string> SetDirectStatusCommand { get; }
    public ActionCommand ReopenOrderCommand { get; }
    public ActionCommand OpenDeliveryDialogCommand { get; }
    public ActionCommand OpenCancelDialogCommand { get; }
    public ActionCommand OpenReceiptPreviewCommand { get; }

    public ActionCommand SetEditNormalPriorityCommand { get; }
    public ActionCommand SetEditUrgentPriorityCommand { get; }
    public ActionCommand SetEditWarrantyPriorityCommand { get; }
    public RelayCommand<string> ApplyEditDeliverySlotCommand { get; }
    public RelayCommand<string> AppendDiagnosisSnippetCommand { get; }
    public ActionCommand SaveWorkshopUpdatesCommand { get; }
    public ActionCommand SavePricingOnlyCommand { get; }

    public ActionCommand SwitchToCatalogPartModeCommand { get; }
    public ActionCommand SwitchToManualPartModeCommand { get; }
    public RelayCommand<MaintenanceSparePartItem> AddCatalogPartDirectCommand { get; }
    public ActionCommand AddSelectedCatalogPartCommand { get; }
    public ActionCommand AddManualPartCommand { get; }
    public RelayCommand<MaintenancePartUsageItem> RemoveUsedPartCommand { get; }

    public ActionCommand SetExtraDepositCashCommand { get; }
    public ActionCommand SetExtraDepositVodafoneCommand { get; }
    public ActionCommand SetExtraDepositInstaPayCommand { get; }
    public ActionCommand CollectExtraDepositCommand { get; }

    public MaintenanceActionsViewModel()
        : this(
            "المشرف",
            MaintenanceDesignSeedData.CreateSharedSampleOrders(PosDesignSeedData.CreateSampleCustomers()),
            MaintenanceDesignSeedData.CreateSharedSampleSpareParts(),
            null,
            null)
    {
    }

    public MaintenanceActionsViewModel(
        string currentUserName,
        ObservableCollection<MaintenanceOrderDesignItem> sharedOrders,
        ObservableCollection<MaintenanceSparePartItem> sharedSpareParts,
        Action? navigateToIntake = null,
        Action? notifySparePartsChanged = null,
        Data.MaintenanceRepository? repository = null,
        Action? onOrdersSaved = null)
    {
        this.currentUserName = currentUserName;
        this.sharedOrders = sharedOrders;
        this.sharedSpareParts = sharedSpareParts;
        this.navigateToIntake = navigateToIntake;
        this.notifySparePartsChanged = notifySparePartsChanged;
        this.repository = repository;
        this.onOrdersSaved = onOrdersSaved;

        ReloadTechnicianDirectory();

        this.sharedOrders.CollectionChanged += OnOrdersCollectionChanged;
        this.sharedSpareParts.CollectionChanged += OnSparePartsCollectionChanged;

        GoToIntakeCommand = new ActionCommand(() => this.navigateToIntake?.Invoke());
        RefreshDataCommand = new ActionCommand(() =>
        {
            if (!NotifyExternalDataChanged()) return;

            try
            {
                notifySparePartsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر تحديث مخزون قطع الغيار: {ex.Message}", true);
                return;
            }

            RefreshFilteredCatalogParts();
            SetStatus(
                $"تم تحديث أوامر الصيانة ({sharedOrders.Count}) ومخزون قطع الغيار ({sharedSpareParts.Count}) وقائمة الفنيين من قاعدة البيانات.",
                false);
        });
        SetStageFilterCommand = new RelayCommand<string>(stage =>
        {
            if (!string.IsNullOrWhiteSpace(stage))
                SelectedStageFilter = stage;
        });
        SelectOrderCommand = new RelayCommand<MaintenanceOrderDesignItem>(order =>
        {
            if (order is not null)
                SelectedOrder = order;
        });
        SelectTabCommand = new RelayCommand<string>(idxStr =>
        {
            if (int.TryParse(idxStr, out var idx))
                ActiveControlTabIndex = idx;
        });

        AdvanceToNextStageCommand = new ActionCommand(AdvanceToNextStage);
        SetDirectStatusCommand = new RelayCommand<string>(SetDirectStatusByKey);
        ReopenOrderCommand = new ActionCommand(ReopenOrder);
        OpenDeliveryDialogCommand = new ActionCommand(OpenDeliveryDialog);
        OpenCancelDialogCommand = new ActionCommand(OpenCancelDialog);
        OpenReceiptPreviewCommand = new ActionCommand(OpenReceiptPreview);

        SetEditNormalPriorityCommand = new ActionCommand(() => EditPriority = MaintenancePriority.Normal);
        SetEditUrgentPriorityCommand = new ActionCommand(() => EditPriority = MaintenancePriority.Urgent);
        SetEditWarrantyPriorityCommand = new ActionCommand(() => EditPriority = MaintenancePriority.WarrantyRecheck);
        ApplyEditDeliverySlotCommand = new RelayCommand<string>(ApplyDeliverySlot);
        AppendDiagnosisSnippetCommand = new RelayCommand<string>(AppendDiagnosisSnippet);
        SaveWorkshopUpdatesCommand = new ActionCommand(() => SaveWorkshopUpdates(showSuccessMessage: true));
        SavePricingOnlyCommand = new ActionCommand(SavePricingOnly);

        SwitchToCatalogPartModeCommand = new ActionCommand(() => IsCatalogPartInputMode = true);
        SwitchToManualPartModeCommand = new ActionCommand(() => IsCatalogPartInputMode = false);
        AddCatalogPartDirectCommand = new RelayCommand<MaintenanceSparePartItem>(AddCatalogPart);
        AddSelectedCatalogPartCommand = new ActionCommand(() => AddCatalogPart(SelectedCatalogPart));
        AddManualPartCommand = new ActionCommand(AddManualPart);
        RemoveUsedPartCommand = new RelayCommand<MaintenancePartUsageItem>(RemoveUsedPart);

        SetExtraDepositCashCommand = new ActionCommand(() => SetExtraDepositMode(MaintenanceDepositMode.CashOnly));
        SetExtraDepositVodafoneCommand = new ActionCommand(() => SetExtraDepositMode(MaintenanceDepositMode.VodafoneCashOnly));
        SetExtraDepositInstaPayCommand = new ActionCommand(() => SetExtraDepositMode(MaintenanceDepositMode.InstaPayOnly));
        CollectExtraDepositCommand = new ActionCommand(CollectExtraDeposit);

        RefreshFilteredOrders();
        RefreshFilteredCatalogParts();
        SelectedOrder = FilteredOrders.FirstOrDefault() ?? sharedOrders.FirstOrDefault();
        SetStatus(
            repository is null
                ? "وضع عرض بدون قاعدة بيانات: تُحفظ التغييرات في الذاكرة فقط حتى يُضبط الاتصال."
                : "جاهز للتحكم: تُحفظ الحالات وتقارير الفحص وقطع الغيار والتحصيل والتسليم مباشرة في SQL Server.",
            false);
        if (repository is not null)
            lastSyncedAt = DateTime.Now;
    }

    /// <summary>
    /// Reloads the orders (and the technician directory) from SQL Server while keeping the ticket the
    /// user is working on selected. Without the id-based restore the panel jumped to the newest order
    /// after every save, because a reload replaces every item instance in the shared collection.
    /// </summary>
    /// <returns>False when the reload failed; the status strip then carries the reason.</returns>
    public bool NotifyExternalDataChanged()
    {
        var selectedId = SelectedOrder?.Id ?? 0;
        var failure = "";

        if (repository is not null)
        {
            isBulkReload = true;
            try
            {
                repository.LoadMaintenanceOrdersInto(sharedOrders);
                pendingActivities.Clear();
                pendingPayments.Clear();
                lastSyncedAt = DateTime.Now;
                Changed(nameof(LastSyncedDisplay));
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            finally
            {
                isBulkReload = false;
            }
        }

        ReloadTechnicianDirectory();
        NotifyStageAndKpiCounts();
        RefreshFilteredOrders(selectedId);
        RefreshFilteredCatalogParts();

        if (failure.Length > 0)
        {
            SetStatus($"تعذّر تحميل أوامر الصيانة من قاعدة البيانات: {failure}", true);
            return false;
        }

        if (SelectedOrder is null && FilteredOrders.Count > 0)
            SelectedOrder = FilteredOrders[0];
        else
            LoadEditableFieldsFromSelected();

        return true;
    }

    /// <summary>Flushes accumulated activities/payments and the order state to the database.</summary>
    /// <returns>True when the save succeeded (or no repository is connected).</returns>
    private bool TryPersistOrder(MaintenanceOrderDesignItem order, string successMessage, string errorPrefix)
    {
        if (repository is null)
            return true;

        try
        {
            repository.UpdateMaintenanceOrder(order, pendingActivities, pendingPayments);
            pendingActivities.Clear();
            pendingPayments.Clear();
            onOrdersSaved?.Invoke();
            SetStatus(successMessage, false);
            return true;
        }
        catch (Exception ex)
        {
            // The in-memory ticket now holds changes SQL Server never accepted (a lost update from
            // another client, a stock conflict, a dropped connection...). Reload the truth so the
            // screen never shows a state the database does not have, and say so in the message.
            var message = $"{errorPrefix}: {ex.Message}";
            if (NotifyExternalDataChanged())
                message += " — أُعيد تحميل الأمر من قاعدة البيانات بحالته المحفوظة فعليًا.";

            SetStatus(message, true);
            return false;
        }
    }

    /// <summary>Records an activity both in the in-memory log and the pending DB batch.</summary>
    private void RecordActivity(MaintenanceOrderDesignItem order, MaintenanceActivityEntry entry)
    {
        order.ActivityLog.Add(entry);
        pendingActivities.Add(entry);
    }

    public void SelectOrderFromExternal(MaintenanceOrderDesignItem order)
    {
        // Match by Id so an order coming from another screen's own collection (e.g. invoices)
        // still resolves to the actions screen's copy.
        var target = sharedOrders.FirstOrDefault(o => o.Id == order.Id) ?? order;
        if (target.Id <= 0) return;

        if (!FilteredOrders.Contains(target))
        {
            searchText = "";
            selectedStageFilter = "كل الحالات";
            selectedPriorityFilter = "كل الأولويات";
            selectedTechnicianFilter = "كل الفنيين";
            Changed(nameof(SearchText));
            Changed(nameof(SelectedStageFilter));
            Changed(nameof(SelectedPriorityFilter));
            Changed(nameof(SelectedTechnicianFilter));
            NotifyStageFilterSelection();
            RefreshFilteredOrders();
        }

        SelectedOrder = target;
        SetStatus($"تم فتح الأمر {target.OrderNumber} في إجراءات الصيانة للمتابعة والتعديل.", false);
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A SQL reload adds row by row; refreshing the queue for every row is wasted work and would
        // reset the selection, so the caller refreshes once when the bulk load finishes.
        if (isBulkReload) return;

        NotifyStageAndKpiCounts();
        RefreshFilteredOrders();
    }

    private void OnSparePartsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFilteredCatalogParts();

    /// <summary>
    /// Rebuilds the technician drop-down from SQL Server (or the design samples when no database is
    /// connected) and keeps the currently assigned/selected names so a stale list never clears them.
    /// </summary>
    private void ReloadTechnicianDirectory()
    {
        var names = new List<string>();
        if (repository is not null)
        {
            try
            {
                names = repository.LoadTechnicianDirectory();
            }
            catch (Exception)
            {
                // Fall back to the design list; the status strip reports real load failures elsewhere.
                names.Clear();
            }
        }

        if (names.Count == 0)
            names.AddRange(MaintenanceDesignSeedData.Technicians);

        foreach (var extra in new[] { SelectedOrder?.AssignedTechnician, EditAssignedTechnician })
        {
            var trimmed = extra?.Trim() ?? "";
            if (trimmed.Length > 0 && !names.Contains(trimmed, StringComparer.Ordinal))
                names.Add(trimmed);
        }

        names.Sort(StringComparer.Ordinal);

        Technicians.Clear();
        foreach (var name in names)
            Technicians.Add(name);

        var previousFilter = SelectedTechnicianFilter;
        TechnicianFilterOptions.Clear();
        TechnicianFilterOptions.Add("كل الفنيين");
        foreach (var name in names)
            TechnicianFilterOptions.Add(name);

        if (!TechnicianFilterOptions.Contains(previousFilter))
        {
            selectedTechnicianFilter = "كل الفنيين";
            Changed(nameof(SelectedTechnicianFilter));
            NotifyStageFilterSelection();
            RefreshFilteredOrders();
        }
    }

    private void LoadEditableFieldsFromSelected()
    {
        if (SelectedOrder is null)
        {
            editPriority = MaintenancePriority.Normal;
            editAssignedTechnician = "";
            editExpectedDeliveryDate = null;
            editExpectedDeliveryLabel = "";
            editDiagnosisNotes = "";
            editLaborCostText = "0";
            editDiscountText = "0";
        }
        else
        {
            editPriority = SelectedOrder.Priority;
            editAssignedTechnician = SelectedOrder.AssignedTechnician;
            editExpectedDeliveryDate = SelectedOrder.ExpectedDeliveryAt;
            editExpectedDeliveryLabel = SelectedOrder.ExpectedDeliveryLabel;
            editDiagnosisNotes = SelectedOrder.DiagnosisNotes;

            var laborVal = SelectedOrder.HasWorkshopPricing
                ? SelectedOrder.LaborCost
                : (SelectedOrder.IsEstimatePendingInspection ? 0m : Math.Max(0m, SelectedOrder.EstimatedCost - SelectedOrder.PartsTotal));
            editLaborCostText = PosFormatting.FormatPlainNumber(laborVal);
            editDiscountText = PosFormatting.FormatPlainNumber(SelectedOrder.DiscountAmount);
        }

        // A technician saved on the ticket (from intake or another client) may be missing from the
        // last directory load; keep it selectable so the ComboBox never clears the field.
        var assigned = SelectedOrder?.AssignedTechnician?.Trim() ?? "";
        if (assigned.Length > 0 && !Technicians.Contains(assigned))
        {
            Technicians.Add(assigned);
            if (!TechnicianFilterOptions.Contains(assigned))
                TechnicianFilterOptions.Add(assigned);
        }

        Changed(nameof(EditPriority));
        Changed(nameof(IsEditNormalPriority));
        Changed(nameof(IsEditUrgentPriority));
        Changed(nameof(IsEditWarrantyPriority));
        Changed(nameof(EditAssignedTechnician));
        Changed(nameof(EditExpectedDeliveryDate));
        Changed(nameof(EditExpectedDeliveryLabel));
        Changed(nameof(EditDiagnosisNotes));
        Changed(nameof(EditLaborCostText));
        Changed(nameof(EditDiscountText));
        Changed(nameof(LiveComputedTotalDisplay));
        Changed(nameof(CanEditSelectedWorkshopFields));
    }

    private void ApplyDeliverySlot(string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot)) return;
        EditExpectedDeliveryLabel = slot;
        EditExpectedDeliveryDate = slot switch
        {
            "اليوم مساءً" => DateTime.Today,
            "غدًا" => DateTime.Today.AddDays(1),
            "خلال 48 ساعة" => DateTime.Today.AddDays(2),
            _ => EditExpectedDeliveryDate
        };
    }

    private void AppendDiagnosisSnippet(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return;
        EditDiagnosisNotes = string.IsNullOrWhiteSpace(EditDiagnosisNotes)
            ? snippet
            : $"{EditDiagnosisNotes.TrimEnd()}\n• {snippet}";
    }

    private bool SaveWorkshopUpdates(bool showSuccessMessage)
    {
        var order = SelectedOrder;
        if (order is null) return false;
        if (!order.IsActiveOrder)
        {
            SetStatus("هذا الأمر مغلق حاليًا؛ أعد فتحه للمراجعة أولًا إذا أردت التعديل.", true);
            return false;
        }

        if (!PosFormatting.TryParseAmount(EditLaborCostText, out var labor) || labor < 0m)
        {
            SetStatus("أدخل قيمة رقمية صحيحة لأجرة الصيانة والمصنعية.", true);
            return false;
        }

        if (!PosFormatting.TryParseAmount(EditDiscountText, out var discount) || discount < 0m)
        {
            SetStatus("أدخل قيمة رقمية صحيحة للخصم.", true);
            return false;
        }

        if (discount > order.PartsTotal + labor)
        {
            SetStatus("الخصم لا يمكن أن يتجاوز مجموع قطع الغيار والمصنعية.", true);
            return false;
        }

        if (ValidateWorkshopFieldLengths() is { } lengthProblem)
        {
            SetStatus(lengthProblem, true);
            return false;
        }

        var changes = new List<string>();

        if (order.Priority != EditPriority)
        {
            order.Priority = EditPriority;
            changes.Add($"الأولوية: {order.PriorityTitle}");
        }

        var technician = (EditAssignedTechnician ?? "").Trim();
        if (technician.Length == 0)
            technician = order.AssignedTechnician;

        if (order.AssignedTechnician != technician)
        {
            order.AssignedTechnician = technician;
            changes.Add($"الفني: {order.AssignedTechnicianDisplay}");
        }

        if (order.ExpectedDeliveryAt != EditExpectedDeliveryDate
            || order.ExpectedDeliveryLabel != EditExpectedDeliveryLabel)
        {
            order.ExpectedDeliveryAt = EditExpectedDeliveryDate;
            order.ExpectedDeliveryLabel = EditExpectedDeliveryLabel;
            changes.Add($"موعد التسليم: {order.ExpectedDeliveryDisplay}");
        }

        var trimmedDiag = EditDiagnosisNotes.Trim();
        if (order.DiagnosisNotes != trimmedDiag)
        {
            order.DiagnosisNotes = trimmedDiag;
            changes.Add("تحديث تقرير الفحص الفني");
        }

        var pricingChanged = order.LaborCost != labor
            || order.DiscountAmount != discount
            || !order.HasWorkshopPricing;

        if (pricingChanged)
        {
            order.LaborCost = labor;
            order.DiscountAmount = discount;
            order.RecalculateWorkshopTotal();
            changes.Add($"اعتماد التكلفة: {order.EstimatedCostDisplay}");
        }

        if (changes.Count > 0)
        {
            RecordActivity(order, new MaintenanceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "تحديث بيانات الورشة والفحص",
                Details = string.Join(" · ", changes),
                ActorName = currentUserName,
                Amount = pricingChanged ? order.CurrentEffectiveCost : null
            });

            NotifyStageAndKpiCounts();
            RefreshFilteredOrders();
            if (repository is not null)
                TryPersistOrder(
                    order,
                    $"تم حفظ تحديثات الأمر {order.OrderNumber} ({string.Join("، ", changes)}).",
                    "تعذّر حفظ تحديثات الأمر");
            else if (showSuccessMessage)
                SetStatus($"تم حفظ تحديثات الأمر {order.OrderNumber} ({string.Join("، ", changes)}).", false);
        }
        else if (showSuccessMessage)
        {
            SetStatus($"لا توجد تغييرات جديدة لحفظها في الأمر {order.OrderNumber}.", false);
        }

        return true;
    }

    private void SavePricingOnly()
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (SaveWorkshopUpdates(showSuccessMessage: false))
        {
            SetStatus(
                $"تم اعتماد التسعير النهائي للأمر {order.OrderNumber}: {order.EstimatedCostDisplay} (المتبقي: {order.EstimatedRemainingDisplay}).",
                false);
        }
    }

    private void AdvanceToNextStage()
    {
        if (SelectedOrder?.NextRecommendedStatus is not { } nextStatus) return;

        if (nextStatus == MaintenanceOrderStatus.Delivered)
        {
            OpenDeliveryDialog();
            return;
        }

        ApplyStatusTransition(SelectedOrder, nextStatus);
    }

    private void SetDirectStatusByKey(string? statusKey)
    {
        if (SelectedOrder is null || string.IsNullOrWhiteSpace(statusKey)) return;

        if (!Enum.TryParse<MaintenanceOrderStatus>(statusKey, out var target))
            return;

        if (target == MaintenanceOrderStatus.Delivered)
        {
            OpenDeliveryDialog();
            return;
        }

        if (target == MaintenanceOrderStatus.Cancelled)
        {
            OpenCancelDialog();
            return;
        }

        if (!SelectedOrder.IsActiveOrder)
        {
            SetStatus("استخدم زر «إعادة فتح للمراجعة» أولًا لتنشيط التذكرة المغلقة.", true);
            return;
        }

        ApplyStatusTransition(SelectedOrder, target);
    }

    private void ApplyStatusTransition(MaintenanceOrderDesignItem order, MaintenanceOrderStatus nextStatus)
    {
        if (order.Status == nextStatus) return;

        // Persist whatever the workshop form holds first; if that fails (invalid amount, conflict with
        // another client) the stage must not change, otherwise the UI and the database disagree.
        if (!SaveWorkshopUpdates(showSuccessMessage: false))
            return;

        var prevTitle = order.StatusTitle;
        order.Status = nextStatus;

        RecordActivity(order, new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = $"تغيير الحالة إلى: {order.StatusTitle}",
            Details = $"تم نقل الجهاز من «{prevTitle}» إلى «{order.StatusTitle}».",
            ActorName = currentUserName
        });

        NotifyStageAndKpiCounts();
        RefreshFilteredOrders();
        Changed(nameof(CanEditSelectedWorkshopFields));
        if (repository is not null)
            TryPersistOrder(order, $"تم تغيير حالة الأمر {order.OrderNumber} إلى «{order.StatusTitle}».", "تعذّر حفظ تغيير الحالة");
        else
            SetStatus($"تم تغيير حالة الأمر {order.OrderNumber} إلى «{order.StatusTitle}».", false);
    }

    private void ReopenOrder()
    {
        var order = SelectedOrder;
        if (order is null || order.IsActiveOrder) return;

        var prevTitle = order.StatusTitle;
        order.Status = MaintenanceOrderStatus.Diagnosing;
        order.DeliveredAt = null;
        order.CancellationReason = "";

        RecordActivity(order, new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "إعادة فتح أمر الصيانة",
            Details = $"تمت إعادة فتح الأمر من «{prevTitle}» إلى «قيد الفحص» للمتابعة والمراجعة.",
            ActorName = currentUserName
        });

        NotifyStageAndKpiCounts();
        RefreshFilteredOrders();
        LoadEditableFieldsFromSelected();
        if (repository is not null)
            TryPersistOrder(order, $"تمت إعادة فتح الأمر {order.OrderNumber} للمراجعة بحالة «قيد الفحص».", "تعذّر حفظ إعادة الفتح");
        else
            SetStatus($"تمت إعادة فتح الأمر {order.OrderNumber} للمراجعة بحالة «قيد الفحص».", false);
    }

    private void OpenDeliveryDialog()
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (order.IsCancelled)
        {
            SetStatus("لا يمكن تسليم أمر ملغى؛ أعد فتحه للمراجعة أولًا.", true);
            return;
        }

        if (order.IsDelivered)
        {
            OpenReceiptPreview();
            return;
        }

        SaveWorkshopUpdates(showSuccessMessage: false);

        var vm = new MaintenanceDeliveryViewModel(order, currentUserName);
        var result = OpenDeliveryDialogRequested?.Invoke(vm);
        if (result == true || vm.IsConfirmed)
        {
            // Snapshot before delivery so only the newly collected amounts are recorded.
            var cashBefore = order.FinalPaymentCashPaid;
            var vodBefore = order.FinalPaymentVodafonePaid;
            var instaBefore = order.FinalPaymentInstaPayPaid;
            var refundBefore = order.RefundedDepositAmount;

            NotifyStageAndKpiCounts();
            RefreshFilteredOrders();
            LoadEditableFieldsFromSelected();

            var cashNew = order.FinalPaymentCashPaid - cashBefore;
            var vodNew = order.FinalPaymentVodafonePaid - vodBefore;
            var instaNew = order.FinalPaymentInstaPayPaid - instaBefore;
            var refundNew = order.RefundedDepositAmount - refundBefore;
            var totalNewCollected = cashNew + vodNew + instaNew;

            if (cashNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.Cash, 2, 1, cashNew, DateTime.Now, "دفعة تسليم - كاش"));
            if (vodNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.VodafoneCash, 2, 1, vodNew, DateTime.Now, "دفعة تسليم - فودافون كاش"));
            if (instaNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.InstaPay, 2, 1, instaNew, DateTime.Now, "دفعة تسليم - إنستاباي"));
            if (refundNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.Cash, 2, 2, refundNew, DateTime.Now, "رد فرق عربون عند التسليم"));

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
                    $"تم تسليم الجهاز {order.OrderNumber} وإغلاق التذكرة ({order.WarrantySummaryDisplay}).",
                    "تعذّر حفظ التسليم");
            else
                SetStatus(
                    $"تم تسليم الجهاز {order.OrderNumber} وإغلاق التذكرة ({order.WarrantySummaryDisplay}).",
                    false);

            if (vm.ShouldOpenPrintAfterClose)
            {
                var previewVm = new MaintenanceReceiptPreviewViewModel(
                    order,
                    modeLabel => RecordPrintActivity(order, modeLabel),
                    MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt);
                OpenReceiptPreviewRequested?.Invoke(previewVm);
            }
        }
    }

    private void OpenCancelDialog()
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (order.IsDelivered || order.IsCancelled)
        {
            SetStatus("هذا الأمر مغلق بالفعل.", true);
            return;
        }

        var vm = new MaintenanceCancelOrderViewModel(order);
        var result = OpenCancelDialogRequested?.Invoke(vm);
        if (result == true || vm.IsConfirmed)
        {
            var returnedCount = 0;
            if (vm.ShouldReturnParts && order.UsedParts.Count > 0)
            {
                var partsCopy = order.UsedParts.ToList();
                foreach (var part in partsCopy)
                {
                    if (!RestorePartStockToInventory(part))
                        return;   // Keep the ticket open so the returned stock and the lines stay in sync.

                    order.UsedParts.Remove(part);
                    returnedCount += part.Quantity;
                }
                order.RecalculateWorkshopTotal();
                RefreshFilteredCatalogParts();
                notifySparePartsChanged?.Invoke();
            }

            if (vm.ConfirmedRefundAmount > 0m)
                order.RefundedDepositAmount += vm.ConfirmedRefundAmount;

            order.CancellationReason = vm.FinalReasonText;
            order.Status = MaintenanceOrderStatus.Cancelled;

            var details = $"السبب: {vm.FinalReasonText}";
            if (returnedCount > 0)
                details += $" · تم إرجاع {returnedCount} قطعة غيار لمخزون الصيانة";
            if (vm.ConfirmedRefundAmount > 0m)
                details += $" · استرداد عربون: {PosFormatting.FormatCurrency(vm.ConfirmedRefundAmount)}";

            if (vm.ConfirmedRefundAmount > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(
                    FinancialAccountKind.Cash, 1, 2, vm.ConfirmedRefundAmount, DateTime.Now, "رد عربون عند الإلغاء"));

            RecordActivity(order, new MaintenanceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "إلغاء أمر الصيانة",
                Details = details,
                ActorName = currentUserName,
                Amount = vm.ConfirmedRefundAmount > 0m ? vm.ConfirmedRefundAmount : null
            });

            NotifyStageAndKpiCounts();
            RefreshFilteredOrders();
            LoadEditableFieldsFromSelected();
            if (repository is not null)
                TryPersistOrder(order, $"تم إلغاء الأمر {order.OrderNumber} ({vm.FinalReasonText}).", "تعذّر حفظ الإلغاء");
            else
                SetStatus($"تم إلغاء الأمر {order.OrderNumber} ({vm.FinalReasonText}).", false);
        }
    }

    private void OpenReceiptPreview()
    {
        var order = SelectedOrder;
        if (order is null) return;

        var initialMode = order.IsDelivered || order.IsReadyForPickup
            ? MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt
            : MaintenanceReceiptPrintMode.CustomerIntakeReceipt;

        var vm = new MaintenanceReceiptPreviewViewModel(
            order,
            modeLabel => RecordPrintActivity(order, modeLabel),
            initialMode);

        OpenReceiptPreviewRequested?.Invoke(vm);
    }

    private void RecordPrintActivity(MaintenanceOrderDesignItem order, string modeLabel)
    {
        var entry = new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "معاينة / طباعة مستند صيانة",
            Details = $"صيغة الطباعة: {modeLabel}.",
            ActorName = currentUserName
        };

        // Printing is a read-only action on the ticket: append the log row on its own so the order
        // columns, its part lines and its RowVersion are not rewritten.
        order.ActivityLog.Add(entry);

        if (repository is null || order.Id <= 0)
        {
            SetStatus($"تمت معاينة طباعة {order.OrderNumber} بصيغة «{modeLabel}».", false);
            return;
        }

        try
        {
            repository.AppendMaintenanceActivity(order.Id, entry);
            SetStatus($"تمت معاينة طباعة {order.OrderNumber} بصيغة «{modeLabel}» وحفظ السجل في قاعدة البيانات.", false);
        }
        catch (Exception ex)
        {
            SetStatus($"تعذّر حفظ سجل الطباعة في قاعدة البيانات: {ex.Message}", true);
        }
    }

    // --- Spare Parts Stock Allocation & Release (Maintenance Spare Parts Only) ---
    private void AddCatalogPart(MaintenanceSparePartItem? sparePart)
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (!order.IsActiveOrder)
        {
            SetStatus("لا يمكن صرف قطع غيار لتذكرة مغلقة.", true);
            return;
        }

        if (sparePart is null)
        {
            SetStatus("اختر قطعة غيار من قائمة قطع غيار الصيانة أولًا.", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(CatalogPartQuantityText, out var qty) || qty <= 0)
        {
            SetStatus("أدخل كمية صحيحة أكبر من صفر.", true);
            return;
        }

        if (sparePart.Quantity < qty)
        {
            SetStatus($"الكمية المطلوبة ({qty}) تتجاوز الرصيد المتاح للقطعة «{sparePart.Name}» ({sparePart.Quantity}).", true);
            return;
        }

        if (repository is not null)
        {
            try
            {
                var lineNumber = order.UsedParts.Count + 1;
                repository.DeductSparePartStock(
                    sparePart.Id, qty, order.Id, sparePart.Name, sparePart.CodeDisplay,
                    sparePart.CostPrice, sparePart.SellPrice, lineNumber);
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر صرف قطعة الغيار: {ex.Message}", true);
                return;
            }
        }

        sparePart.Quantity -= qty;

        var partItem = new MaintenancePartUsageItem
        {
            // Spare-part lines link dbo.MaintenanceSpareParts through SparePartId; ProductId is
            // reserved for POS catalog products and stays 0 here.
            SparePartId = sparePart.Id,
            Barcode = sparePart.CodeDisplay,
            ProductName = sparePart.Name,
            Quantity = qty,
            UnitCost = sparePart.CostPrice,
            UnitSellPrice = sparePart.SellPrice,
            IsManualPart = false
        };

        order.UsedParts.Add(partItem);
        SyncLaborBeforeWorkshopRecalc(order);
        order.RecalculateWorkshopTotal();
        Changed(nameof(LiveComputedTotalDisplay));

        RecordActivity(order, new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "صرف قطعة غيار من مخزون الصيانة",
            Details = $"صرف {qty} × {sparePart.Name} ({partItem.LineTotalDisplay}) وخصمها من مخزون قطع الغيار.",
            ActorName = currentUserName,
            Amount = partItem.LineTotal
        });

        CatalogPartQuantityText = "1";
        RefreshFilteredCatalogParts();
        NotifyStageAndKpiCounts();
        notifySparePartsChanged?.Invoke();

        if (order.Id > 0)
            TryPersistOrder(
                order,
                $"تم صرف «{sparePart.Name}» ({qty} قطعة) للأمر {order.OrderNumber} وتحديث رصيد قطع الغيار.",
                "تعذّر حفظ قطعة الغيار المصروفة");
        else
            SetStatus($"تم صرف «{sparePart.Name}» ({qty} قطعة) للأمر {order.OrderNumber} وتحديث رصيد قطع الغيار.", false);
    }

    private void AddManualPart()
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (!order.IsActiveOrder)
        {
            SetStatus("لا يمكن إضافة قطع غيار لتذكرة مغلقة.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(ManualPartName))
        {
            SetStatus("أدخل اسم قطعة الغيار أو البند الإضافي.", true);
            return;
        }

        if (ManualPartName.Trim().Length > 250)
        {
            SetStatus("اسم البند اليدوي أطول من الحد المسموح (250 حرفًا). اختصره ثم أعد الإضافة.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(ManualPartCostText, out var cost) || cost < 0m)
        {
            SetStatus("أدخل تكلفة شراء صحيحة للبند اليدوي.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(ManualPartPriceText, out var sellPrice) || sellPrice < 0m)
        {
            SetStatus("أدخل سعر بيع صحيح للعميل للبند اليدوي.", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(ManualPartQtyText, out var qty) || qty <= 0)
        {
            SetStatus("أدخل كمية صحيحة أكبر من صفر.", true);
            return;
        }

        var partItem = new MaintenancePartUsageItem
        {
            ProductId = 0,
            Barcode = "MANUAL",
            ProductName = ManualPartName.Trim(),
            Quantity = qty,
            UnitCost = cost,
            UnitSellPrice = sellPrice,
            IsManualPart = true
        };

        order.UsedParts.Add(partItem);
        SyncLaborBeforeWorkshopRecalc(order);
        order.RecalculateWorkshopTotal();
        Changed(nameof(LiveComputedTotalDisplay));

        RecordActivity(order, new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "إضافة قطعة غيار يدوية",
            Details = $"إضافة {qty} × {partItem.ProductName} بقيمة {partItem.LineTotalDisplay}.",
            ActorName = currentUserName,
            Amount = partItem.LineTotal
        });

        ManualPartName = "";
        ManualPartCostText = "0";
        ManualPartPriceText = "0";
        ManualPartQtyText = "1";
        NotifyStageAndKpiCounts();
        if (order.Id > 0)
            TryPersistOrder(
                order,
                $"تمت إضافة البند اليدوي «{partItem.ProductName}» للأمر {order.OrderNumber}.",
                "تعذّر حفظ البند اليدوي");
        else
            SetStatus($"تمت إضافة البند اليدوي «{partItem.ProductName}» للأمر {order.OrderNumber}.", false);
    }

    private void RemoveUsedPart(MaintenancePartUsageItem? part)
    {
        var order = SelectedOrder;
        if (order is null || part is null) return;
        if (!order.IsActiveOrder)
        {
            SetStatus("لا يمكن إزالة قطع غيار من تذكرة مغلقة.", true);
            return;
        }

        if (!RestorePartStockToInventory(part))
            return;

        order.UsedParts.Remove(part);
        SyncLaborBeforeWorkshopRecalc(order);
        order.RecalculateWorkshopTotal();
        Changed(nameof(LiveComputedTotalDisplay));

        RecordActivity(order, new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = part.IsManualPart ? "حذف بند قطعة غيار" : "إرجاع قطعة غيار لمخزون الصيانة",
            Details = $"حذف {part.Quantity} × {part.ProductName} ({part.LineTotalDisplay}).",
            ActorName = currentUserName
        });

        RefreshFilteredCatalogParts();
        NotifyStageAndKpiCounts();
        if (!part.IsManualPart)
            notifySparePartsChanged?.Invoke();

        if (order.Id > 0)
            TryPersistOrder(
                order,
                part.IsManualPart
                    ? $"تم حذف البند «{part.ProductName}» من التذكرة."
                    : $"تمت إزالة «{part.ProductName}» وإرجاع الكمية لمخزون قطع الغيار بنجاح.",
                "تعذّر حفظ حذف القطعة");
        else
            SetStatus(
                part.IsManualPart
                    ? $"تم حذف البند «{part.ProductName}» من التذكرة."
                    : $"تمت إزالة «{part.ProductName}» وإرجاع الكمية لمخزون قطع الغيار بنجاح.",
                false);
    }

    private void SyncLaborBeforeWorkshopRecalc(MaintenanceOrderDesignItem order)
    {
        if (PosFormatting.TryParseAmount(EditLaborCostText, out var labor) && labor >= 0m)
            order.LaborCost = labor;
        if (PosFormatting.TryParseAmount(EditDiscountText, out var discount) && discount >= 0m)
            order.DiscountAmount = discount;
    }

    /// <summary>
    /// Returns a spare-part line to dbo.MaintenanceSpareParts before the line is dropped from the
    /// ticket. The id comes from the line itself, so a part that is no longer in the loaded catalog
    /// is still returned instead of being lost. Returns false when the stock write failed, so the
    /// caller must keep the line on the ticket.
    /// </summary>
    private bool RestorePartStockToInventory(MaintenancePartUsageItem part)
    {
        if (!part.IsSparePartLine) return true;

        var sparePartId = part.EffectiveSparePartId;
        if (sparePartId <= 0) return true;

        if (repository is not null)
        {
            try
            {
                repository.RestoreSparePartStock(sparePartId, part.Quantity);
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر استرجاع رصيد قطعة الغيار: {ex.Message}", true);
                return false;
            }
        }

        var sparePart = sharedSpareParts.FirstOrDefault(p => p.Id == sparePartId);
        if (sparePart is not null)
            sparePart.Quantity += part.Quantity;

        return true;
    }

    private void SetExtraDepositMode(MaintenanceDepositMode mode)
    {
        extraDepositMode = mode;
        Changed(nameof(IsExtraDepositCash));
        Changed(nameof(IsExtraDepositVodafone));
        Changed(nameof(IsExtraDepositInstaPay));
    }

    private void CollectExtraDeposit()
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (!order.IsActiveOrder)
        {
            SetStatus("لا يمكن تسجيل دفعة على تذكرة مغلقة.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(ExtraDepositAmountText, out var amount) || amount <= 0m)
        {
            SetStatus("أدخل مبلغًا أكبر من صفر لتحصيل الدفعة الإضافية.", true);
            return;
        }

        FinancialAccountKind account;
        string channelTitle;
        if (extraDepositMode == MaintenanceDepositMode.VodafoneCashOnly)
        {
            order.DepositVodafonePaid += amount;
            account = FinancialAccountKind.VodafoneCash;
            channelTitle = "فودافون كاش";
        }
        else if (extraDepositMode == MaintenanceDepositMode.InstaPayOnly)
        {
            order.DepositInstaPayPaid += amount;
            account = FinancialAccountKind.InstaPay;
            channelTitle = "إنستاباي";
        }
        else
        {
            order.DepositCashPaid += amount;
            account = FinancialAccountKind.Cash;
            channelTitle = "كاش";
        }

        pendingPayments.Add(new Data.MaintenancePaymentEntry(account, 1, 1, amount, DateTime.Now, $"دفعة إضافية - {channelTitle}"));

        RecordActivity(order, new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "تحصيل دفعة تحت الحساب",
            Details = $"تحصيل {PosFormatting.FormatCurrency(amount)} عبر {channelTitle}.",
            ActorName = currentUserName,
            Amount = amount
        });

        ExtraDepositAmountText = "";
        NotifyStageAndKpiCounts();
        if (repository is not null)
            TryPersistOrder(
                order,
                $"تم تحصيل دفعة إضافية {PosFormatting.FormatCurrency(amount)} على الأمر {order.OrderNumber} ({channelTitle}).",
                "تعذّر حفظ الدفعة الإضافية");
        else
            SetStatus($"تم تحصيل دفعة إضافية {PosFormatting.FormatCurrency(amount)} على الأمر {order.OrderNumber} ({channelTitle}).", false);
    }

    private void RefreshFilteredOrders(int preferredOrderId = 0)
    {
        var query = SearchText.Trim();
        var previouslySelected = SelectedOrder;
        var targetId = preferredOrderId > 0 ? preferredOrderId : (previouslySelected?.Id ?? 0);
        FilteredOrders.Clear();

        foreach (var order in sharedOrders)
        {
            var stageMatches = SelectedStageFilter switch
            {
                "النشطة" or "النشطة بالورشة" => order.IsActiveOrder,
                "تم الاستلام" => order.IsReceived,
                "قيد الفحص" => order.IsDiagnosing,
                "بانتظار قطع غيار" => order.IsWaitingParts,
                "قيد الصيانة" => order.IsInRepair,
                "جاهز للتسليم" => order.IsReadyForPickup,
                "تم التسليم" => order.IsDelivered,
                "ملغى" => order.IsCancelled,
                _ => true
            };

            if (!stageMatches) continue;

            if (SelectedPriorityFilter == "مستعجل" && !order.IsUrgent) continue;
            if (SelectedPriorityFilter == "عادي" && order.Priority != MaintenancePriority.Normal) continue;
            if (SelectedPriorityFilter == "ضمان / إعادة فحص" && !order.IsWarranty) continue;

            if (SelectedTechnicianFilter != "كل الفنيين"
                && !string.Equals(order.AssignedTechnician, SelectedTechnicianFilter, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(query))
            {
                var matched = order.OrderNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || order.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || order.CustomerPhone.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || order.DeviceFullTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || order.SerialOrImei.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || order.ReportedFault.Contains(query, StringComparison.OrdinalIgnoreCase);

                if (!matched) continue;
            }

            FilteredOrders.Add(order);
        }

        if (previouslySelected is not null && FilteredOrders.Contains(previouslySelected))
        {
            SelectedOrder = previouslySelected;
        }
        else if (targetId > 0 && FilteredOrders.FirstOrDefault(o => o.Id == targetId) is { } sameOrder)
        {
            SelectedOrder = sameOrder;
        }
        else if (FilteredOrders.Count > 0)
        {
            SelectedOrder = FilteredOrders[0];
        }
        else
        {
            SelectedOrder = null;
        }

        Changed(nameof(HasNoFilteredOrders));
    }

    private void RefreshFilteredCatalogParts()
    {
        var query = PartSearchText.Trim();
        FilteredCatalogParts.Clear();

        foreach (var item in sharedSpareParts)
        {
            if (string.IsNullOrWhiteSpace(query)
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.CodeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCatalogParts.Add(item);
            }
        }
    }

    private void NotifyStageFilterSelection()
    {
        Changed(nameof(IsFilterAll));
        Changed(nameof(IsFilterActive));
        Changed(nameof(IsFilterReceived));
        Changed(nameof(IsFilterDiagnosing));
        Changed(nameof(IsFilterWaitingParts));
        Changed(nameof(IsFilterInRepair));
        Changed(nameof(IsFilterReady));
        Changed(nameof(IsFilterDelivered));
        Changed(nameof(IsFilterCancelled));
    }

    private void NotifyStageAndKpiCounts()
    {
        Changed(nameof(AllCount));
        Changed(nameof(ActiveOrdersCount));
        Changed(nameof(ReceivedCount));
        Changed(nameof(DiagnosingCount));
        Changed(nameof(WaitingPartsCount));
        Changed(nameof(InRepairCount));
        Changed(nameof(ReadyForPickupCount));
        Changed(nameof(DeliveredCount));
        Changed(nameof(CancelledCount));
        Changed(nameof(UrgentActiveCount));
        Changed(nameof(ExpectedCollectionDisplay));
    }

    /// <summary>
    /// Mirrors the column widths in sql/06 for the fields this screen writes, so an over-long value
    /// produces an Arabic message here instead of SQL Server error 8152 on save.
    /// </summary>
    private string? ValidateWorkshopFieldLengths()
    {
        if (EditDiagnosisNotes.Trim().Length > 2000)
            return "تقرير الفحص أطول من الحد المسموح (2000 حرف). اختصره ثم أعد الحفظ.";

        if ((EditAssignedTechnician ?? "").Trim().Length > 150)
            return "اسم الفني أطول من الحد المسموح (150 حرفًا).";

        if (EditExpectedDeliveryLabel.Trim().Length > 200)
            return "وصف موعد التسليم أطول من الحد المسموح (200 حرف).";

        return null;
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
