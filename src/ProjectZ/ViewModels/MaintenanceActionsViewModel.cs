using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceActionsViewModel : ObservableObject
{
    private readonly string currentUserName;
    private readonly ObservableCollection<MaintenanceOrderDesignItem> sharedOrders;
    private readonly ObservableCollection<PosProductDesignItem> sharedCatalog;
    private readonly ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations;
    private readonly Action? navigateToIntake;
    private readonly Action? notifyInventoryChanged;
    private readonly Action? notifyReturnExchangeChanged;
    private readonly Action? notifyPurchaseEntryCatalogChanged;

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

    // Spare parts management (catalog + quick manual item)
    private bool isCatalogPartInputMode = true;
    private string partSearchText = "";
    private PosProductDesignItem? selectedCatalogPart;
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

    public ObservableCollection<MaintenanceOrderDesignItem> FilteredOrders { get; } = [];
    public ObservableCollection<PosProductDesignItem> FilteredCatalogParts { get; } = [];

    public IReadOnlyList<string> Technicians => MaintenanceDesignSeedData.Technicians;
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
    public IReadOnlyList<string> TechnicianFilterOptions { get; }

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
            if (selectedTechnicianFilter == value) return;
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

    public PosProductDesignItem? SelectedCatalogPart
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
    public RelayCommand<PosProductDesignItem> AddCatalogPartDirectCommand { get; }
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
            PosDesignSeedData.CreateSharedSampleCatalog(),
            null,
            null,
            null,
            null,
            null)
    {
    }

    public MaintenanceActionsViewModel(
        string currentUserName,
        ObservableCollection<MaintenanceOrderDesignItem> sharedOrders,
        ObservableCollection<PosProductDesignItem> sharedCatalog,
        ObservableCollection<InventoryUnitRegistrationItem>? sharedRegistrations = null,
        Action? navigateToIntake = null,
        Action? notifyInventoryChanged = null,
        Action? notifyReturnExchangeChanged = null,
        Action? notifyPurchaseEntryCatalogChanged = null)
    {
        this.currentUserName = currentUserName;
        this.sharedOrders = sharedOrders;
        this.sharedCatalog = sharedCatalog;
        this.sharedRegistrations = sharedRegistrations
            ?? InventoryDesignSeedData.CreateSharedSampleRegistrations(this.sharedCatalog);
        this.navigateToIntake = navigateToIntake;
        this.notifyInventoryChanged = notifyInventoryChanged;
        this.notifyReturnExchangeChanged = notifyReturnExchangeChanged;
        this.notifyPurchaseEntryCatalogChanged = notifyPurchaseEntryCatalogChanged;

        TechnicianFilterOptions = new[] { "كل الفنيين" }.Concat(MaintenanceDesignSeedData.Technicians).ToArray();

        this.sharedOrders.CollectionChanged += OnOrdersCollectionChanged;
        this.sharedCatalog.CollectionChanged += OnCatalogCollectionChanged;

        GoToIntakeCommand = new ActionCommand(() => this.navigateToIntake?.Invoke());
        RefreshDataCommand = new ActionCommand(() =>
        {
            NotifyExternalDataChanged();
            SetStatus("تم تحديث قائمة أجهزة الصيانة وبيانات المخزون.", false);
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
        AddCatalogPartDirectCommand = new RelayCommand<PosProductDesignItem>(AddCatalogPart);
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
        SetStatus("شاشة إجراءات الصيانة: تحكم كامل بحالات الأجهزة، تقارير الفحص، قطع الغيار والمخزون، والتسليم والضمان.", false);
    }

    public void NotifyExternalDataChanged()
    {
        NotifyStageAndKpiCounts();
        RefreshFilteredOrders();
        RefreshFilteredCatalogParts();
        if (SelectedOrder is null && FilteredOrders.Count > 0)
            SelectedOrder = FilteredOrders[0];
        else
            LoadEditableFieldsFromSelected();
    }

    public void SelectOrderFromExternal(MaintenanceOrderDesignItem order)
    {
        if (!sharedOrders.Contains(order)) return;

        if (!FilteredOrders.Contains(order))
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

        SelectedOrder = order;
        SetStatus($"تم فتح الأمر {order.OrderNumber} في إجراءات الصيانة للمتابعة والتعديل.", false);
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyStageAndKpiCounts();
        RefreshFilteredOrders();
    }

    private void OnCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFilteredCatalogParts();

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

        var changes = new List<string>();

        if (order.Priority != EditPriority)
        {
            order.Priority = EditPriority;
            changes.Add($"الأولوية: {order.PriorityTitle}");
        }

        if (order.AssignedTechnician != EditAssignedTechnician)
        {
            order.AssignedTechnician = EditAssignedTechnician;
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
            order.ActivityLog.Add(new MaintenanceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "تحديث بيانات الورشة والفحص",
                Details = string.Join(" · ", changes),
                ActorName = currentUserName,
                Amount = pricingChanged ? order.CurrentEffectiveCost : null
            });

            NotifyStageAndKpiCounts();
            RefreshFilteredOrders();
            if (showSuccessMessage)
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

        SaveWorkshopUpdates(showSuccessMessage: false);

        var prevTitle = order.StatusTitle;
        order.Status = nextStatus;

        order.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = $"تغيير الحالة إلى: {order.StatusTitle}",
            Details = $"تم نقل الجهاز من «{prevTitle}» إلى «{order.StatusTitle}».",
            ActorName = currentUserName
        });

        NotifyStageAndKpiCounts();
        RefreshFilteredOrders();
        Changed(nameof(CanEditSelectedWorkshopFields));
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

        order.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "إعادة فتح أمر الصيانة",
            Details = $"تمت إعادة فتح الأمر من «{prevTitle}» إلى «قيد الفحص» للمتابعة والمراجعة.",
            ActorName = currentUserName
        });

        NotifyStageAndKpiCounts();
        RefreshFilteredOrders();
        LoadEditableFieldsFromSelected();
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
            NotifyStageAndKpiCounts();
            RefreshFilteredOrders();
            LoadEditableFieldsFromSelected();
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
                    RestorePartStockToInventory(part);
                    order.UsedParts.Remove(part);
                    returnedCount += part.Quantity;
                }
                order.RecalculateWorkshopTotal();
                notifyInventoryChanged?.Invoke();
                notifyReturnExchangeChanged?.Invoke();
                notifyPurchaseEntryCatalogChanged?.Invoke();
            }

            if (vm.ConfirmedRefundAmount > 0m)
                order.RefundedDepositAmount += vm.ConfirmedRefundAmount;

            order.CancellationReason = vm.FinalReasonText;
            order.Status = MaintenanceOrderStatus.Cancelled;

            var details = $"السبب: {vm.FinalReasonText}";
            if (returnedCount > 0)
                details += $" · تم إرجاع {returnedCount} قطعة غيار للمخزون";
            if (vm.ConfirmedRefundAmount > 0m)
                details += $" · استرداد عربون: {PosFormatting.FormatCurrency(vm.ConfirmedRefundAmount)}";

            order.ActivityLog.Add(new MaintenanceActivityEntry
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
        order.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "معاينة / طباعة مستند صيانة",
            Details = $"صيغة الطباعة: {modeLabel}.",
            ActorName = currentUserName
        });
        SetStatus($"تمت معاينة طباعة {order.OrderNumber} بصيغة «{modeLabel}».", false);
    }

    // --- Spare Parts Stock Allocation & Release ---
    private void AddCatalogPart(PosProductDesignItem? product)
    {
        var order = SelectedOrder;
        if (order is null) return;
        if (!order.IsActiveOrder)
        {
            SetStatus("لا يمكن صرف قطع غيار لتذكرة مغلقة.", true);
            return;
        }

        if (product is null)
        {
            SetStatus("اختر صنفًا من قائمة قطع الغيار والمخزون أولًا.", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(CatalogPartQuantityText, out var qty) || qty <= 0)
        {
            SetStatus("أدخل كمية صحيحة أكبر من صفر.", true);
            return;
        }

        if (product.AvailableStock < qty)
        {
            SetStatus($"الكمية المطلوبة ({qty}) تتجاوز الرصيد المتاح في المخزون للصنف «{product.Name}» ({product.AvailableStock}).", true);
            return;
        }

        var availableUnits = sharedRegistrations
            .Where(r => r.ProductId == product.Id && r.IsAvailable)
            .OrderBy(r => r.Id)
            .Take(qty)
            .ToList();

        var claimedIds = new List<int>(availableUnits.Count);
        foreach (var unit in availableUnits)
        {
            unit.Status = InventoryUnitStatus.Sold;
            unit.SaleInvoiceNumber = $"صيانة {order.OrderNumber}";
            claimedIds.Add(unit.Id);
        }

        if (availableUnits.Count > 0)
            product.AvailableStock = sharedRegistrations.Count(r => r.ProductId == product.Id && r.IsAvailable);
        else
            product.AvailableStock = Math.Max(0, product.AvailableStock - qty);

        var unitCost = availableUnits.Count > 0
            ? Math.Round(availableUnits.Average(u => u.PurchasePrice), 2)
            : product.CostPrice;

        var partItem = new MaintenancePartUsageItem
        {
            ProductId = product.Id,
            Barcode = product.Barcode,
            ProductName = product.Name,
            Quantity = qty,
            UnitCost = unitCost,
            UnitSellPrice = product.RetailPrice,
            IsManualPart = false,
            UnitRegistrationIds = claimedIds
        };

        order.UsedParts.Add(partItem);
        SyncLaborBeforeWorkshopRecalc(order);
        order.RecalculateWorkshopTotal();
        Changed(nameof(LiveComputedTotalDisplay));

        order.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "صرف قطعة غيار من المخزون",
            Details = $"صرف {qty} × {product.Name} ({partItem.LineTotalDisplay}) وخصمها من المخزون.",
            ActorName = currentUserName,
            Amount = partItem.LineTotal
        });

        CatalogPartQuantityText = "1";
        RefreshFilteredCatalogParts();
        NotifyStageAndKpiCounts();
        notifyInventoryChanged?.Invoke();
        notifyReturnExchangeChanged?.Invoke();
        notifyPurchaseEntryCatalogChanged?.Invoke();

        SetStatus($"تم صرف «{product.Name}» ({qty} قطعة) للأمر {order.OrderNumber} وتحديث المخزون.", false);
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

        order.ActivityLog.Add(new MaintenanceActivityEntry
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

        RestorePartStockToInventory(part);
        order.UsedParts.Remove(part);
        SyncLaborBeforeWorkshopRecalc(order);
        order.RecalculateWorkshopTotal();
        Changed(nameof(LiveComputedTotalDisplay));

        order.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = part.IsManualPart ? "حذف بند قطعة غيار" : "إرجاع قطعة غيار للمخزون",
            Details = $"حذف {part.Quantity} × {part.ProductName} ({part.LineTotalDisplay}).",
            ActorName = currentUserName
        });

        RefreshFilteredCatalogParts();
        NotifyStageAndKpiCounts();
        if (!part.IsManualPart)
        {
            notifyInventoryChanged?.Invoke();
            notifyReturnExchangeChanged?.Invoke();
            notifyPurchaseEntryCatalogChanged?.Invoke();
        }

        SetStatus(
            part.IsManualPart
                ? $"تم حذف البند «{part.ProductName}» من التذكرة."
                : $"تمت إزالة «{part.ProductName}» وإرجاع الكمية للمخزون بنجاح.",
            false);
    }

    private void SyncLaborBeforeWorkshopRecalc(MaintenanceOrderDesignItem order)
    {
        if (PosFormatting.TryParseAmount(EditLaborCostText, out var labor) && labor >= 0m)
            order.LaborCost = labor;
        if (PosFormatting.TryParseAmount(EditDiscountText, out var discount) && discount >= 0m)
            order.DiscountAmount = discount;
    }

    private void RestorePartStockToInventory(MaintenancePartUsageItem part)
    {
        if (part.IsManualPart || part.ProductId <= 0) return;

        var product = sharedCatalog.FirstOrDefault(p => p.Id == part.ProductId);
        if (part.UnitRegistrationIds.Count > 0)
        {
            foreach (var regId in part.UnitRegistrationIds)
            {
                var unit = sharedRegistrations.FirstOrDefault(r => r.Id == regId);
                if (unit is not null)
                {
                    unit.Status = InventoryUnitStatus.Available;
                    unit.SaleInvoiceNumber = "";
                }
            }
            if (product is not null)
                product.AvailableStock = sharedRegistrations.Count(r => r.ProductId == product.Id && r.IsAvailable);
        }
        else if (product is not null)
        {
            product.AvailableStock += part.Quantity;
        }
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

        string channelTitle;
        if (extraDepositMode == MaintenanceDepositMode.VodafoneCashOnly)
        {
            order.DepositVodafonePaid += amount;
            channelTitle = "فودافون كاش";
        }
        else if (extraDepositMode == MaintenanceDepositMode.InstaPayOnly)
        {
            order.DepositInstaPayPaid += amount;
            channelTitle = "إنستاباي";
        }
        else
        {
            order.DepositCashPaid += amount;
            channelTitle = "كاش";
        }

        order.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "تحصيل دفعة تحت الحساب",
            Details = $"تحصيل {PosFormatting.FormatCurrency(amount)} عبر {channelTitle}.",
            ActorName = currentUserName,
            Amount = amount
        });

        ExtraDepositAmountText = "";
        NotifyStageAndKpiCounts();
        SetStatus($"تم تحصيل دفعة إضافية {PosFormatting.FormatCurrency(amount)} على الأمر {order.OrderNumber} ({channelTitle}).", false);
    }

    private void RefreshFilteredOrders()
    {
        var query = SearchText.Trim();
        var previouslySelected = SelectedOrder;
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
            SelectedOrder = previouslySelected;
        else if (FilteredOrders.Count > 0)
            SelectedOrder = FilteredOrders[0];
        else
            SelectedOrder = null;

        Changed(nameof(HasNoFilteredOrders));
    }

    private void RefreshFilteredCatalogParts()
    {
        var query = PartSearchText.Trim();
        FilteredCatalogParts.Clear();

        foreach (var item in sharedCatalog)
        {
            if (string.IsNullOrWhiteSpace(query)
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
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

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
