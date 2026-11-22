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
    private readonly Action? navigateToSpareParts;
    private readonly Action? notifySparePartsChanged;
    private readonly Data.MaintenanceRepository? repository;
    private readonly Action? onOrdersSaved;
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
            ReopenReason = "";
            ExtraDepositAmountText = "";
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
    public ActionCommand GoToSparePartsCommand { get; }
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
        Action? onOrdersSaved = null,
        Action? navigateToSpareParts = null)
    {
        this.currentUserName = currentUserName;
        this.sharedOrders = sharedOrders;
        this.sharedSpareParts = sharedSpareParts;
        this.navigateToIntake = navigateToIntake;
        this.navigateToSpareParts = navigateToSpareParts;
        this.notifySparePartsChanged = notifySparePartsChanged;
        this.repository = repository;
        this.onOrdersSaved = onOrdersSaved;

        ReloadTechnicianDirectory();

        this.sharedOrders.CollectionChanged += OnOrdersCollectionChanged;
        this.sharedSpareParts.CollectionChanged += OnSparePartsCollectionChanged;

        GoToIntakeCommand = new ActionCommand(() => this.navigateToIntake?.Invoke());
        GoToSparePartsCommand = new ActionCommand(() => this.navigateToSpareParts?.Invoke());
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
        SaveWorkshopUpdatesCommand = new ActionCommand(() => SaveWorkshopUpdates());
        SavePricingOnlyCommand = new ActionCommand(() => SaveWorkshopUpdates());

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
                ? "وضع عرض فقط: لا تُعتمد إجراءات الورشة أو الدفعات دون اتصال بقاعدة البيانات."
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
        RefreshFilteredCatalogParts(allowClearSelection: true);

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

    private void RefreshAfterPosting(MaintenanceOrderDesignItem saved)
    {
        if (onOrdersSaved is not null) onOrdersSaved();
        else if (!NotifyExternalDataChanged()) throw new Data.MaintenanceDataException("تعذّر تحديث الورشة.");
    }

    private bool Post(MaintenanceOrderDesignItem order, Func<Data.MaintenanceRepository, byte[]> write, string success) =>
        MaintenancePosting.Save(repository, order, () => write(repository!), RefreshAfterPosting, SetStatus, success);

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
    {
        // A Clear/Add reload or a delete can leave the issue form pointing at an instance that is
        // no longer in the shared collection. Drop that selection before rebuilding the list.
        if (selectedCatalogPart is not null && !sharedSpareParts.Contains(selectedCatalogPart))
        {
            selectedCatalogPart = null;
            Changed(nameof(SelectedCatalogPart));
            Changed(nameof(HasSelectedCatalogPart));
        }

        RefreshFilteredCatalogParts();
    }

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

    private bool TryReadPricing(out decimal labor, out decimal discount)
    {
        discount = 0m;
        if (PosFormatting.TryParseAmount(EditLaborCostText, out labor) && labor >= 0 &&
            PosFormatting.TryParseAmount(EditDiscountText, out discount) && discount >= 0) return true;
        SetStatus("أدخل قيمًا صحيحة للمصنعية والخصم.", true);
        return false;
    }

    private void SaveWorkshopUpdates(MaintenanceOrderStatus? next = null)
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (!order.IsActiveOrder) { SetStatus("الأمر مغلق؛ لا يمكن تعديل بيانات الورشة.", true); return; }
        if (!TryReadPricing(out var labor, out var discount)) return;
        if (ValidateWorkshopFieldLengths() is { } error) { SetStatus(error, true); return; }
        var tech = string.IsNullOrWhiteSpace(EditAssignedTechnician) ? order.AssignedTechnician : EditAssignedTechnician.Trim();
        Post(order, r => r.SaveWorkshop(order, next ?? order.Status, EditPriority, tech, EditExpectedDeliveryDate,
            EditExpectedDeliveryLabel, EditDiagnosisNotes.Trim(), labor, discount), $"تم حفظ حالة وتسعير الأمر {order.OrderNumber}.");
    }

    private void AdvanceToNextStage()
    {
        if (SelectedOrder?.NextRecommendedStatus is not { } nextStatus) return;

        if (nextStatus == MaintenanceOrderStatus.Delivered)
        {
            OpenDeliveryDialog();
            return;
        }

        SaveWorkshopUpdates(nextStatus);
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

        SaveWorkshopUpdates(target);
    }

    private string reopenReason = "";
    public string ReopenReason
    {
        get => reopenReason;
        set { reopenReason = value; Changed(); }
    }

    private void ReopenOrder()
    {
        var order = SelectedOrder;
        if (order is null || order.IsActiveOrder) return;
        if (!order.CanReopen) { SetStatus("إعادة الفتح تتطلب اتصال مشرف مخوّل.", true); return; }
        var reason = ReopenReason.Trim();
        if (reason.Length is 0 or > 1500) { SetStatus("اكتب سبب إعادة الفتح في حدود 1500 حرف.", true); return; }
        Post(order, r => r.Reopen(Data.MaintenanceTicketKey.Open(order), reason), $"أعيد فتح {order.OrderNumber} مع حفظ نسخة الإغلاق.");
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

        // Do not silently save workshop edits when merely opening (or cancelling) delivery.
        if (EditPriority != order.Priority || (EditAssignedTechnician ?? "").Trim() != order.AssignedTechnician ||
            EditExpectedDeliveryDate != order.ExpectedDeliveryAt || EditExpectedDeliveryLabel != order.ExpectedDeliveryLabel ||
            EditDiagnosisNotes.Trim() != order.DiagnosisNotes.Trim())
        {
            SetStatus("احفظ تعديلات الفحص والفني قبل فتح نافذة التسليم.", true);
            return;
        }
        var dialog = new MaintenanceDeliveryViewModel(order) { LaborCostText = EditLaborCostText, DiscountText = EditDiscountText };
        MaintenancePosting.Deliver(dialog, repository, OpenDeliveryDialogRequested, RefreshAfterPosting,
            OpenReceiptPreviewRequested, currentUserName, SetStatus);
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

        var key = Data.MaintenanceTicketKey.Open(order);
        var vm = new MaintenanceCancelOrderViewModel(order);
        if (OpenCancelDialogRequested?.Invoke(vm) != true || !vm.IsConfirmed) return;
        Post(order, r => r.Cancel(key, vm.ConfirmedRefundAmount, vm.ShouldReturnParts, vm.FinalReasonText),
            $"تم إلغاء الأمر {order.OrderNumber} وتسوية الدفعات والقطع.");
    }

    private void OpenReceiptPreview()
    {
        if (SelectedOrder is { } order) MaintenancePosting.Preview(order, repository, currentUserName, OpenReceiptPreviewRequested, SetStatus);
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

        if (!TryReadPricing(out var labor, out var discount)) return;
        if (Post(order, r => r.IssuePart(Data.MaintenanceTicketKey.Open(order), sparePart.Id, qty, labor, discount),
                $"تم صرف {qty} × {sparePart.Name} على الأمر {order.OrderNumber}.")) CatalogPartQuantityText = "1";
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

        if (!TryReadPricing(out var labor, out var discount)) return;
        if (Post(order, r => r.AddManualPart(Data.MaintenanceTicketKey.Open(order), ManualPartName.Trim(), qty, cost, sellPrice, labor, discount),
                $"أضيف البند اليدوي إلى {order.OrderNumber}."))
        {
            ManualPartName = ""; ManualPartCostText = "0"; ManualPartPriceText = "0"; ManualPartQtyText = "1";
        }
    }

    private void RemoveUsedPart(MaintenancePartUsageItem? part)
    {
        var order = SelectedOrder;
        if (order is null || part is null || !order.IsActiveOrder || !order.UsedParts.Contains(part)) return;
        if (!TryReadPricing(out var labor, out var discount)) return;
        Post(order, r => r.RemovePart(Data.MaintenanceTicketKey.Open(order), part.LineId, labor, discount),
            $"تم حذف بند {part.ProductName} من {order.OrderNumber} وتسوية المخزون.");
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

        var account = extraDepositMode switch
        {
            MaintenanceDepositMode.VodafoneCashOnly => FinancialAccountKind.VodafoneCash,
            MaintenanceDepositMode.InstaPayOnly => FinancialAccountKind.InstaPay,
            _ => FinancialAccountKind.Cash
        };
        if (Post(order, r => r.CollectDeposit(Data.MaintenanceTicketKey.Open(order), account, amount),
                $"تم تحصيل {PosFormatting.FormatCurrency(amount)} على الأمر {order.OrderNumber}.")) ExtraDepositAmountText = "";
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

    private void RefreshFilteredCatalogParts(bool allowClearSelection = false)
    {
        var query = PartSearchText.Trim();
        var selectedId = selectedCatalogPart?.Id;
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

        // Reloading the spare-parts screen replaces its item instances. Keep the issue form
        // pointing at the new instance with the same Id instead of a stale quantity/cost. During
        // the Clear/Add sequence the target row may not exist yet, so only a full refresh may
        // clear a selection that was really deleted or deactivated.
        if (selectedId.HasValue)
        {
            var replacement = sharedSpareParts.FirstOrDefault(p => p.Id == selectedId.Value);
            if (replacement is not null)
                selectedCatalogPart = replacement;
            else if (allowClearSelection)
                selectedCatalogPart = null;

            Changed(nameof(SelectedCatalogPart));
            Changed(nameof(HasSelectedCatalogPart));
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
