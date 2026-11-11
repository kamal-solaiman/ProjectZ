using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class SelectableIntakeTagViewModel : ObservableObject
{
    private readonly Action onChanged;
    private bool isSelected;

    public string Title { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            Changed();
            onChanged();
        }
    }

    public ActionCommand ToggleCommand { get; }

    public SelectableIntakeTagViewModel(string title, Action onChanged)
    {
        Title = title;
        this.onChanged = onChanged;
        ToggleCommand = new ActionCommand(() => IsSelected = !IsSelected);
    }
}

public sealed class MaintenanceIntakeViewModel : ObservableObject
{
    private readonly ObservableCollection<PosCustomerDesignItem> sharedCustomers;
    private readonly ObservableCollection<MaintenanceOrderDesignItem> sharedMaintenanceOrders;
    private readonly string currentUserName;
    private int nextOrderId;

    // Customer selection & quick contact
    private PosCustomerDesignItem? selectedCustomer;
    private string customerSearchText = "";
    private bool isCustomerPickerOpen = true;
    private string contactPhone = "";

    // Device specification
    private string deviceType = "هاتف ذكي";
    private string deviceBrand = "Apple";
    private string deviceModel = "";
    private string deviceColor = "";
    private string serialOrImei = "";
    private MaintenancePasscodeType passcodeType = MaintenancePasscodeType.None;
    private string passcodeValue = "";

    // Fault, condition & accessories
    private string reportedFault = "";
    private string customConditionNote = "";
    private string intakeNotes = "";

    // Priority, technician & expected delivery
    private MaintenancePriority priority = MaintenancePriority.Normal;
    private string assignedTechnician = "ورشة الصيانة العامة";
    private DateTime? expectedDeliveryDate = DateTime.Today.AddDays(1);
    private string expectedDeliveryLabel = "غدًا";

    // Financial estimate & optional deposit
    private bool isEstimatePendingInspection;
    private string estimatedCostText = "";
    private MaintenanceDepositMode depositMode = MaintenanceDepositMode.NoDeposit;
    private string depositCashText = "";
    private string depositVodafoneText = "";
    private string depositVodafoneRef = "";
    private string depositInstaPayText = "";
    private string depositInstaPayRef = "";

    // Recent orders sidebar
    private MaintenanceOrderDesignItem? lastSavedOrder;
    private string recentSearchText = "";
    private string recentFilterMode = "الكل";

    private string statusMessage = "مرحلة التصميم (بدون Backend): اختر العميل وسجّل بيانات الجهاز والعطل والملحقات، وأضف عربونًا اختياريًا ثم احفظ أو اطبع إيصال الاستلام.";
    private bool isErrorStatus;

    public ObservableCollection<PosCustomerDesignItem> Customers => sharedCustomers;
    public ObservableCollection<PosCustomerDesignItem> FilteredCustomers { get; } = [];
    public ObservableCollection<MaintenanceOrderDesignItem> FilteredRecentOrders { get; } = [];

    public ObservableCollection<SelectableIntakeTagViewModel> DeviceTypeOptions { get; } = [];
    public ObservableCollection<SelectableIntakeTagViewModel> ConditionChecklist { get; } = [];
    public ObservableCollection<SelectableIntakeTagViewModel> AccessoryChecklist { get; } = [];

    public IReadOnlyList<string> DeviceBrands => MaintenanceDesignSeedData.DeviceBrands;
    public IReadOnlyList<string> CommonFaults => MaintenanceDesignSeedData.CommonFaults;
    public IReadOnlyList<string> DeliverySlotOptions => MaintenanceDesignSeedData.DeliverySlotOptions;
    public IReadOnlyList<string> Technicians => MaintenanceDesignSeedData.Technicians;

    public Action<string?, Action<PosCustomerDesignItem>>? OpenAddCustomerRequested { get; set; }
    public Action<MaintenanceReceiptPreviewViewModel>? OpenReceiptPreviewRequested { get; set; }
    public Action? FocusModelInputRequested { get; set; }

    public string NextOrderNumberDisplay => $"رقم الأمر القادم: MNT-2026-{GetNextOrderIdPreview():0000}";

    // Customer properties
    public PosCustomerDesignItem? SelectedCustomer
    {
        get => selectedCustomer;
        private set
        {
            if (ReferenceEquals(selectedCustomer, value)) return;
            selectedCustomer = value;
            Changed();
            Changed(nameof(HasSelectedCustomer));
            Changed(nameof(NeedsCustomerSelection));
            Changed(nameof(IsWalkInCustomerSelected));
            Changed(nameof(IsCustomerPickerVisible));
        }
    }

    public bool HasSelectedCustomer => SelectedCustomer is not null;
    public bool NeedsCustomerSelection => SelectedCustomer is null;
    public bool IsWalkInCustomerSelected => SelectedCustomer?.IsWalkIn == true;

    public string CustomerSearchText
    {
        get => customerSearchText;
        set
        {
            if (customerSearchText == value) return;
            customerSearchText = value;
            Changed();
            if (!isCustomerPickerOpen && !string.IsNullOrWhiteSpace(value))
                isCustomerPickerOpen = true;
            RefreshFilteredCustomers();
            Changed(nameof(IsCustomerPickerVisible));
        }
    }

    public bool IsCustomerPickerOpen
    {
        get => isCustomerPickerOpen;
        set
        {
            if (isCustomerPickerOpen == value) return;
            isCustomerPickerOpen = value;
            Changed();
            Changed(nameof(IsCustomerPickerVisible));
        }
    }

    public bool IsCustomerPickerVisible => (SelectedCustomer is null || isCustomerPickerOpen) && FilteredCustomers.Count > 0;
    public bool HasNoFilteredCustomers => FilteredCustomers.Count == 0;

    public string ContactPhone
    {
        get => contactPhone;
        set
        {
            if (contactPhone == value) return;
            contactPhone = value;
            Changed();
        }
    }

    // Device specification properties
    public string DeviceType
    {
        get => deviceType;
        set
        {
            if (deviceType == value) return;
            deviceType = value;
            foreach (var opt in DeviceTypeOptions)
                opt.IsSelected = opt.Title == deviceType;
            Changed();
        }
    }

    public string DeviceBrand
    {
        get => deviceBrand;
        set
        {
            if (deviceBrand == value) return;
            deviceBrand = value;
            Changed();
        }
    }

    public string DeviceModel
    {
        get => deviceModel;
        set
        {
            if (deviceModel == value) return;
            deviceModel = value;
            Changed();
        }
    }

    public string DeviceColor
    {
        get => deviceColor;
        set
        {
            if (deviceColor == value) return;
            deviceColor = value;
            Changed();
        }
    }

    public string SerialOrImei
    {
        get => serialOrImei;
        set
        {
            if (serialOrImei == value) return;
            serialOrImei = value;
            Changed();
        }
    }

    public MaintenancePasscodeType PasscodeType
    {
        get => passcodeType;
        set
        {
            if (passcodeType == value) return;
            passcodeType = value;
            if (passcodeType == MaintenancePasscodeType.None)
                PasscodeValue = "";
            Changed();
            Changed(nameof(IsPasscodeNone));
            Changed(nameof(IsPasscodePin));
            Changed(nameof(IsPasscodePattern));
            Changed(nameof(NeedsPasscodeInput));
            Changed(nameof(PasscodeInputLabel));
            Changed(nameof(PasscodeInputHint));
        }
    }

    public bool IsPasscodeNone => PasscodeType == MaintenancePasscodeType.None;
    public bool IsPasscodePin => PasscodeType == MaintenancePasscodeType.PinOrPassword;
    public bool IsPasscodePattern => PasscodeType == MaintenancePasscodeType.Pattern;
    public bool NeedsPasscodeInput => PasscodeType != MaintenancePasscodeType.None;
    public string PasscodeInputLabel => IsPasscodePattern ? "أرقام نقاط النمط (1 إلى 9)" : "رمز فتح الجهاز / كلمة المرور";
    public string PasscodeInputHint => IsPasscodePattern ? "مثال: 1-2-5-8-9 (من أعلى اليسار 1 إلى أسفل اليمين 9)" : "مثال: 2580 أو كلمة السر الخاصة بالجهاز";

    public string PasscodeValue
    {
        get => passcodeValue;
        set
        {
            if (passcodeValue == value) return;
            passcodeValue = value;
            Changed();
        }
    }

    public string ReportedFault
    {
        get => reportedFault;
        set
        {
            if (reportedFault == value) return;
            reportedFault = value;
            Changed();
        }
    }

    public string CustomConditionNote
    {
        get => customConditionNote;
        set
        {
            if (customConditionNote == value) return;
            customConditionNote = value;
            Changed();
            Changed(nameof(ConditionSummaryPreview));
        }
    }

    public string ConditionSummaryPreview
    {
        get
        {
            var items = ConditionChecklist.Where(t => t.IsSelected).Select(t => t.Title).ToList();
            if (!string.IsNullOrWhiteSpace(CustomConditionNote))
                items.Add(CustomConditionNote.Trim());
            return items.Count == 0 ? "حالة ظاهرية سليمة (أو اختر من الملاحظات السريعة)" : string.Join("، ", items);
        }
    }

    public string AccessoriesSummaryPreview
    {
        get
        {
            var items = AccessoryChecklist.Where(t => t.IsSelected).Select(t => t.Title).ToList();
            return items.Count == 0 ? "الجهاز فقط (بدون ملحقات)" : string.Join("، ", items);
        }
    }

    public string IntakeNotes
    {
        get => intakeNotes;
        set
        {
            if (intakeNotes == value) return;
            intakeNotes = value;
            Changed();
        }
    }

    // Priority & schedule
    public MaintenancePriority Priority
    {
        get => priority;
        set
        {
            if (priority == value) return;
            priority = value;
            Changed();
            Changed(nameof(IsNormalPriority));
            Changed(nameof(IsUrgentPriority));
            Changed(nameof(IsWarrantyPriority));
        }
    }

    public bool IsNormalPriority => Priority == MaintenancePriority.Normal;
    public bool IsUrgentPriority => Priority == MaintenancePriority.Urgent;
    public bool IsWarrantyPriority => Priority == MaintenancePriority.WarrantyRecheck;

    public string AssignedTechnician
    {
        get => assignedTechnician;
        set
        {
            if (assignedTechnician == value || value is null) return;
            assignedTechnician = value;
            Changed();
        }
    }

    public DateTime? ExpectedDeliveryDate
    {
        get => expectedDeliveryDate;
        set
        {
            if (expectedDeliveryDate == value) return;
            expectedDeliveryDate = value;
            Changed();
        }
    }

    public string ExpectedDeliveryLabel
    {
        get => expectedDeliveryLabel;
        set
        {
            if (expectedDeliveryLabel == value) return;
            expectedDeliveryLabel = value;
            Changed();
        }
    }

    // Financial estimate & deposit
    public bool IsEstimatePendingInspection
    {
        get => isEstimatePendingInspection;
        set
        {
            if (isEstimatePendingInspection == value) return;
            isEstimatePendingInspection = value;
            if (isEstimatePendingInspection)
            {
                estimatedCostText = "";
                Changed(nameof(EstimatedCostText));
            }
            Changed();
            Changed(nameof(CanEditEstimatedCost));
            NotifyFinancialSummary();
        }
    }

    public bool CanEditEstimatedCost => !IsEstimatePendingInspection;

    public string EstimatedCostText
    {
        get => estimatedCostText;
        set
        {
            if (estimatedCostText == value) return;
            estimatedCostText = value;
            Changed();
            NotifyFinancialSummary();
        }
    }

    public MaintenanceDepositMode DepositMode
    {
        get => depositMode;
        private set
        {
            if (depositMode == value) return;
            depositMode = value;
            Changed();
            Changed(nameof(IsNoDepositMode));
            Changed(nameof(IsDepositCashMode));
            Changed(nameof(IsDepositVodafoneMode));
            Changed(nameof(IsDepositInstaPayMode));
            Changed(nameof(IsDepositSplitMode));
            Changed(nameof(ShowDepositInputs));
            Changed(nameof(ShowCashDepositInput));
            Changed(nameof(ShowVodafoneDepositInput));
            Changed(nameof(ShowInstaPayDepositInput));
        }
    }

    public bool IsNoDepositMode => DepositMode == MaintenanceDepositMode.NoDeposit;
    public bool IsDepositCashMode => DepositMode == MaintenanceDepositMode.CashOnly;
    public bool IsDepositVodafoneMode => DepositMode == MaintenanceDepositMode.VodafoneCashOnly;
    public bool IsDepositInstaPayMode => DepositMode == MaintenanceDepositMode.InstaPayOnly;
    public bool IsDepositSplitMode => DepositMode == MaintenanceDepositMode.Split;
    public bool ShowDepositInputs => DepositMode != MaintenanceDepositMode.NoDeposit;
    public bool ShowCashDepositInput => DepositMode is MaintenanceDepositMode.CashOnly or MaintenanceDepositMode.Split;
    public bool ShowVodafoneDepositInput => DepositMode is MaintenanceDepositMode.VodafoneCashOnly or MaintenanceDepositMode.Split;
    public bool ShowInstaPayDepositInput => DepositMode is MaintenanceDepositMode.InstaPayOnly or MaintenanceDepositMode.Split;

    public string DepositCashText
    {
        get => depositCashText;
        set
        {
            if (depositCashText == value) return;
            depositCashText = value;
            Changed();
            NotifyFinancialSummary();
        }
    }

    public string DepositVodafoneText
    {
        get => depositVodafoneText;
        set
        {
            if (depositVodafoneText == value) return;
            depositVodafoneText = value;
            Changed();
            NotifyFinancialSummary();
        }
    }

    public string DepositVodafoneRef
    {
        get => depositVodafoneRef;
        set
        {
            if (depositVodafoneRef == value) return;
            depositVodafoneRef = value;
            Changed();
        }
    }

    public string DepositInstaPayText
    {
        get => depositInstaPayText;
        set
        {
            if (depositInstaPayText == value) return;
            depositInstaPayText = value;
            Changed();
            NotifyFinancialSummary();
        }
    }

    public string DepositInstaPayRef
    {
        get => depositInstaPayRef;
        set
        {
            if (depositInstaPayRef == value) return;
            depositInstaPayRef = value;
            Changed();
        }
    }

    public decimal EstimatedCost => IsEstimatePendingInspection
        ? 0m
        : (PosFormatting.TryParseAmount(EstimatedCostText, out var val) && val >= 0m ? val : 0m);

    public decimal DepositCashAmount => ShowCashDepositInput && PosFormatting.TryParseAmount(DepositCashText, out var c) && c >= 0m ? c : 0m;
    public decimal DepositVodafoneAmount => ShowVodafoneDepositInput && PosFormatting.TryParseAmount(DepositVodafoneText, out var v) && v >= 0m ? v : 0m;
    public decimal DepositInstaPayAmount => ShowInstaPayDepositInput && PosFormatting.TryParseAmount(DepositInstaPayText, out var i) && i >= 0m ? i : 0m;
    public decimal TotalDepositPaid => DepositCashAmount + DepositVodafoneAmount + DepositInstaPayAmount;

    public bool HasDepositOverEstimate => !IsEstimatePendingInspection && EstimatedCost > 0m && TotalDepositPaid > EstimatedCost;
    public decimal EstimatedRemaining => IsEstimatePendingInspection || EstimatedCost <= 0m
        ? 0m
        : Math.Max(0m, EstimatedCost - TotalDepositPaid);

    public string EstimatedCostSummaryDisplay => IsEstimatePendingInspection
        ? "يُحدد بعد الفحص"
        : EstimatedCost > 0m
            ? PosFormatting.FormatCurrency(EstimatedCost)
            : "غير محدد بعد";

    public string TotalDepositPaidDisplay => PosFormatting.FormatCurrency(TotalDepositPaid);
    public string EstimatedRemainingDisplay => IsEstimatePendingInspection || EstimatedCost <= 0m
        ? "يُحدد بعد الفحص"
        : PosFormatting.FormatCurrency(EstimatedRemaining);

    // Recent orders sidebar
    public MaintenanceOrderDesignItem? LastSavedOrder
    {
        get => lastSavedOrder;
        private set
        {
            if (ReferenceEquals(lastSavedOrder, value)) return;
            lastSavedOrder = value;
            Changed();
            Changed(nameof(HasLastSavedOrder));
            Changed(nameof(LastSavedOrderButtonLabel));
        }
    }

    public bool HasLastSavedOrder => LastSavedOrder is not null;
    public string LastSavedOrderButtonLabel => LastSavedOrder is null
        ? "معاينة آخر إيصال"
        : $"معاينة آخر إيصال ({LastSavedOrder.OrderNumber})";

    public string RecentSearchText
    {
        get => recentSearchText;
        set
        {
            if (recentSearchText == value) return;
            recentSearchText = value;
            Changed();
            RefreshFilteredRecentOrders();
        }
    }

    public string RecentFilterMode
    {
        get => recentFilterMode;
        set
        {
            if (recentFilterMode == value) return;
            recentFilterMode = value;
            Changed();
            Changed(nameof(IsRecentAllFilter));
            Changed(nameof(IsRecentTodayFilter));
            Changed(nameof(IsRecentUrgentFilter));
            RefreshFilteredRecentOrders();
        }
    }

    public bool IsRecentAllFilter => RecentFilterMode == "الكل";
    public bool IsRecentTodayFilter => RecentFilterMode == "اليوم";
    public bool IsRecentUrgentFilter => RecentFilterMode == "مستعجل";
    public bool HasNoFilteredRecentOrders => FilteredRecentOrders.Count == 0;

    public string TodayCountDisplay => $"{sharedMaintenanceOrders.Count(o => o.ReceivedAt.Date == DateTime.Today)} أجهزة";
    public string ActiveOrdersCountDisplay => $"{sharedMaintenanceOrders.Count(o => o.IsActiveOrder)} نشطة";
    public string TodayDepositsDisplay => PosFormatting.FormatCurrency(
        sharedMaintenanceOrders.Where(o => o.ReceivedAt.Date == DateTime.Today).Sum(o => o.TotalDepositPaid));

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
    public ActionCommand ToggleCustomerPickerCommand { get; }
    public ActionCommand ClearCustomerSelectionCommand { get; }
    public RelayCommand<PosCustomerDesignItem> SelectCustomerCommand { get; }
    public ActionCommand OpenAddCustomerWindowCommand { get; }

    public RelayCommand<SelectableIntakeTagViewModel> SelectDeviceTypeCommand { get; }
    public ActionCommand SetPasscodeNoneCommand { get; }
    public ActionCommand SetPasscodePinCommand { get; }
    public ActionCommand SetPasscodePatternCommand { get; }

    public RelayCommand<string> AppendCommonFaultCommand { get; }
    public ActionCommand SetNormalPriorityCommand { get; }
    public ActionCommand SetUrgentPriorityCommand { get; }
    public ActionCommand SetWarrantyPriorityCommand { get; }
    public RelayCommand<string> SelectDeliverySlotCommand { get; }

    public ActionCommand SetNoDepositModeCommand { get; }
    public ActionCommand SetDepositCashModeCommand { get; }
    public ActionCommand SetDepositVodafoneModeCommand { get; }
    public ActionCommand SetDepositInstaPayModeCommand { get; }
    public ActionCommand SetDepositSplitModeCommand { get; }

    public ActionCommand SaveOrderCommand { get; }
    public ActionCommand SaveAndPrintCommand { get; }
    public ActionCommand ResetFormCommand { get; }
    public ActionCommand PreviewLastSavedCommand { get; }

    public RelayCommand<string> SetRecentFilterCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> PreviewOrderReceiptCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> CopyCustomerForNewOrderCommand { get; }

    public MaintenanceIntakeViewModel()
        : this("المشرف", null, null)
    {
    }

    public MaintenanceIntakeViewModel(
        string currentUserName,
        ObservableCollection<PosCustomerDesignItem>? sharedCustomers = null,
        ObservableCollection<MaintenanceOrderDesignItem>? sharedMaintenanceOrders = null)
    {
        this.currentUserName = currentUserName;
        this.sharedCustomers = sharedCustomers
            ?? new ObservableCollection<PosCustomerDesignItem>(PosDesignSeedData.CreateSampleCustomers());
        this.sharedMaintenanceOrders = sharedMaintenanceOrders ?? MaintenanceDesignSeedData.CreateSharedSampleOrders(this.sharedCustomers);

        nextOrderId = this.sharedMaintenanceOrders.Count > 0 ? this.sharedMaintenanceOrders.Max(o => o.Id) + 1 : 1;

        foreach (var dt in MaintenanceDesignSeedData.DeviceTypes)
        {
            var tag = new SelectableIntakeTagViewModel(dt, () => { });
            tag.IsSelected = dt == deviceType;
            DeviceTypeOptions.Add(tag);
        }

        foreach (var cond in MaintenanceDesignSeedData.ConditionChecklistOptions)
            ConditionChecklist.Add(new SelectableIntakeTagViewModel(cond, () => Changed(nameof(ConditionSummaryPreview))));

        foreach (var acc in MaintenanceDesignSeedData.AccessoryChecklistOptions)
            AccessoryChecklist.Add(new SelectableIntakeTagViewModel(acc, () => Changed(nameof(AccessoriesSummaryPreview))));

        this.sharedCustomers.CollectionChanged += OnCustomersCollectionChanged;
        this.sharedMaintenanceOrders.CollectionChanged += OnOrdersCollectionChanged;

        ToggleCustomerPickerCommand = new ActionCommand(() =>
        {
            IsCustomerPickerOpen = !IsCustomerPickerOpen;
            RefreshFilteredCustomers();
        });
        ClearCustomerSelectionCommand = new ActionCommand(() =>
        {
            SelectedCustomer = null;
            IsCustomerPickerOpen = true;
            RefreshFilteredCustomers();
        });
        SelectCustomerCommand = new RelayCommand<PosCustomerDesignItem>(SelectCustomer);
        OpenAddCustomerWindowCommand = new ActionCommand(RequestOpenAddCustomer);

        SelectDeviceTypeCommand = new RelayCommand<SelectableIntakeTagViewModel>(tag =>
        {
            if (tag is not null)
                DeviceType = tag.Title;
        });

        SetPasscodeNoneCommand = new ActionCommand(() => PasscodeType = MaintenancePasscodeType.None);
        SetPasscodePinCommand = new ActionCommand(() => PasscodeType = MaintenancePasscodeType.PinOrPassword);
        SetPasscodePatternCommand = new ActionCommand(() => PasscodeType = MaintenancePasscodeType.Pattern);

        AppendCommonFaultCommand = new RelayCommand<string>(AppendCommonFault);
        SetNormalPriorityCommand = new ActionCommand(() => Priority = MaintenancePriority.Normal);
        SetUrgentPriorityCommand = new ActionCommand(() => Priority = MaintenancePriority.Urgent);
        SetWarrantyPriorityCommand = new ActionCommand(() => Priority = MaintenancePriority.WarrantyRecheck);
        SelectDeliverySlotCommand = new RelayCommand<string>(ApplyDeliverySlot);

        SetNoDepositModeCommand = new ActionCommand(() => ApplyDepositMode(MaintenanceDepositMode.NoDeposit));
        SetDepositCashModeCommand = new ActionCommand(() => ApplyDepositMode(MaintenanceDepositMode.CashOnly));
        SetDepositVodafoneModeCommand = new ActionCommand(() => ApplyDepositMode(MaintenanceDepositMode.VodafoneCashOnly));
        SetDepositInstaPayModeCommand = new ActionCommand(() => ApplyDepositMode(MaintenanceDepositMode.InstaPayOnly));
        SetDepositSplitModeCommand = new ActionCommand(() => ApplyDepositMode(MaintenanceDepositMode.Split));

        SaveOrderCommand = new ActionCommand(() => SaveIntakeOrder(openReceiptAfter: false));
        SaveAndPrintCommand = new ActionCommand(() => SaveIntakeOrder(openReceiptAfter: true));
        ResetFormCommand = new ActionCommand(() =>
        {
            ResetForm();
            SetStatus("تم تفريغ نموذج الاستلام لبدء أمر صيانة جديد.", false);
        });
        PreviewLastSavedCommand = new ActionCommand(() =>
        {
            if (LastSavedOrder is not null)
                OpenReceiptPreview(LastSavedOrder);
        });

        SetRecentFilterCommand = new RelayCommand<string>(mode =>
        {
            if (!string.IsNullOrEmpty(mode))
                RecentFilterMode = mode;
        });
        PreviewOrderReceiptCommand = new RelayCommand<MaintenanceOrderDesignItem>(ord =>
        {
            if (ord is not null)
                OpenReceiptPreview(ord);
        });
        CopyCustomerForNewOrderCommand = new RelayCommand<MaintenanceOrderDesignItem>(CopyCustomerToNewIntake);

        LastSavedOrder = this.sharedMaintenanceOrders.FirstOrDefault();
        RefreshFilteredCustomers();
        RefreshFilteredRecentOrders();
    }

    public void NotifyExternalDataChanged()
    {
        RefreshFilteredCustomers();
        RefreshFilteredRecentOrders();
        NotifyRecentKpis();
        Changed(nameof(NextOrderNumberDisplay));
    }

    public void StartNewIntakeForCustomer(PosCustomerDesignItem customer)
    {
        ResetForm();
        SelectCustomer(customer);
        FocusModelInputRequested?.Invoke();
        SetStatus($"جاهز لاستلام أمر صيانة جديد للعميل «{customer.Name}» ({customer.DisplayPhone}).", false);
    }

    private void OnCustomersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFilteredCustomers();

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFilteredRecentOrders();
        NotifyRecentKpis();
        Changed(nameof(NextOrderNumberDisplay));
    }

    private int GetNextOrderIdPreview()
    {
        var maxId = sharedMaintenanceOrders.Count > 0 ? sharedMaintenanceOrders.Max(o => o.Id) : 0;
        return Math.Max(nextOrderId, maxId + 1);
    }

    private void SelectCustomer(PosCustomerDesignItem? customer)
    {
        if (customer is null) return;
        SelectedCustomer = customer;
        CustomerSearchText = "";
        IsCustomerPickerOpen = false;
        ContactPhone = customer.Phone;
        SetStatus(
            customer.IsWalkIn
                ? "تم اختيار «عميل عابر» — يرجى إدخال رقم هاتف للتواصل عند انتهاء الصيانة أو تسجيل عميل جديد."
                : $"تم ربط الأمر بالعميل «{customer.Name}» ({customer.DisplayPhone}).",
            false);
    }

    private void RequestOpenAddCustomer()
    {
        OpenAddCustomerRequested?.Invoke(CustomerSearchText, created =>
        {
            var nextCustId = sharedCustomers.Count > 0 ? sharedCustomers.Max(c => c.Id) + 1 : 100;
            var assigned = new PosCustomerDesignItem
            {
                Id = nextCustId,
                Name = created.Name,
                Phone = created.Phone,
                SecondaryPhone = created.SecondaryPhone,
                Address = created.Address,
                Notes = created.Notes,
                DefaultPriceTier = created.DefaultPriceTier
            };
            sharedCustomers.Add(assigned);
            SelectCustomer(assigned);
            SetStatus($"تمت إضافة العميل «{assigned.Name}» إلى النظام وربطه بأمر الصيانة.", false);
        });
    }

    private void AppendCommonFault(string? fault)
    {
        if (string.IsNullOrWhiteSpace(fault)) return;
        if (string.IsNullOrWhiteSpace(ReportedFault))
        {
            ReportedFault = fault;
            return;
        }

        if (ReportedFault.Contains(fault, StringComparison.OrdinalIgnoreCase))
            return;

        ReportedFault = $"{ReportedFault.TrimEnd(' ', '،', '-')} + {fault}";
    }

    private void ApplyDeliverySlot(string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot)) return;
        ExpectedDeliveryLabel = slot;
        ExpectedDeliveryDate = slot switch
        {
            "اليوم مساءً" => DateTime.Today,
            "غدًا" => DateTime.Today.AddDays(1),
            "خلال 48 ساعة" => DateTime.Today.AddDays(2),
            _ => null
        };
    }

    private void ApplyDepositMode(MaintenanceDepositMode mode)
    {
        DepositMode = mode;
        if (mode == MaintenanceDepositMode.NoDeposit)
        {
            DepositCashText = "";
            DepositVodafoneText = "";
            DepositVodafoneRef = "";
            DepositInstaPayText = "";
            DepositInstaPayRef = "";
        }
        NotifyFinancialSummary();
    }

    private void CopyCustomerToNewIntake(MaintenanceOrderDesignItem? order)
    {
        if (order is null) return;
        var matched = sharedCustomers.FirstOrDefault(c => c.Id == order.CustomerId || c.Name == order.CustomerName);
        if (matched is not null)
        {
            SelectCustomer(matched);
            if (!string.IsNullOrWhiteSpace(order.CustomerPhone))
                ContactPhone = order.CustomerPhone;
        }
        else
        {
            ContactPhone = order.CustomerPhone;
        }

        FocusModelInputRequested?.Invoke();
        SetStatus($"جاهز لاستلام جهاز جديد للعميل «{order.CustomerName}».", false);
    }

    private void SaveIntakeOrder(bool openReceiptAfter)
    {
        if (SelectedCustomer is null)
        {
            IsCustomerPickerOpen = true;
            SetStatus("يرجى اختيار العميل من القائمة أو إضافة عميل جديد قبل حفظ أمر الصيانة.", true);
            return;
        }

        var trimmedPhone = ContactPhone.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPhone))
        {
            SetStatus("يرجى إدخال رقم هاتف للتواصل مع العميل عند انتهاء الصيانة أو الفحص.", true);
            return;
        }

        var trimmedModel = DeviceModel.Trim();
        if (string.IsNullOrWhiteSpace(trimmedModel))
        {
            SetStatus("يرجى إدخال موديل الجهاز المستلم (مثال: iPhone 13 Pro أو Galaxy A55).", true);
            FocusModelInputRequested?.Invoke();
            return;
        }

        var trimmedFault = ReportedFault.Trim();
        if (string.IsNullOrWhiteSpace(trimmedFault))
        {
            SetStatus("يرجى وصف العطل المطلوب صيانته أو اختيار أحد الأعطال الشائعة.", true);
            return;
        }

        if (!IsEstimatePendingInspection && !string.IsNullOrWhiteSpace(EstimatedCostText))
        {
            if (!PosFormatting.TryParseAmount(EstimatedCostText, out var parsedEst) || parsedEst < 0m)
            {
                SetStatus("يرجى إدخال قيمة صحيحة للتكلفة المبدئية المتوقعة أو تفعيل خيار «يُحدد بعد الفحص».", true);
                return;
            }
        }

        if (HasDepositOverEstimate)
        {
            SetStatus(
                $"العربون المدفوع ({TotalDepositPaidDisplay}) أكبر من التكلفة التقديرية ({EstimatedCostSummaryDisplay}).",
                true);
            return;
        }

        if (ShowDepositInputs && TotalDepositPaid <= 0m)
        {
            SetStatus("أدخل مبلغ العربون / رسوم الفحص أو اختر «بدون مقدم».", true);
            return;
        }

        var orderId = GetNextOrderIdPreview();
        nextOrderId = orderId + 1;
        var orderNumber = $"MNT-2026-{orderId:0000}";

        var selectedConditions = ConditionChecklist.Where(c => c.IsSelected).Select(c => c.Title).ToList();
        if (!string.IsNullOrWhiteSpace(CustomConditionNote))
            selectedConditions.Add(CustomConditionNote.Trim());

        var selectedAccessories = AccessoryChecklist.Where(a => a.IsSelected).Select(a => a.Title).ToList();

        var order = new MaintenanceOrderDesignItem
        {
            Id = orderId,
            OrderNumber = orderNumber,
            ReceivedAt = DateTime.Now,
            ReceivedByName = currentUserName,
            ExpectedDeliveryAt = ExpectedDeliveryDate,
            ExpectedDeliveryLabel = ExpectedDeliveryLabel.Trim(),
            CustomerId = SelectedCustomer.Id,
            CustomerName = SelectedCustomer.Name,
            CustomerPhone = trimmedPhone,
            CustomerSecondaryPhone = SelectedCustomer.SecondaryPhone,
            CustomerAddress = SelectedCustomer.Address,
            IsWalkInCustomer = SelectedCustomer.IsWalkIn,
            DeviceType = DeviceType,
            DeviceBrand = string.IsNullOrWhiteSpace(DeviceBrand) ? "أخرى" : DeviceBrand.Trim(),
            DeviceModel = trimmedModel,
            DeviceColor = DeviceColor.Trim(),
            SerialOrImei = SerialOrImei.Trim(),
            PasscodeType = PasscodeType,
            PasscodeValue = PasscodeType == MaintenancePasscodeType.None ? "" : PasscodeValue.Trim(),
            ReportedFault = trimmedFault,
            PhysicalCondition = selectedConditions.Count == 0 ? "سليم ظاهريًا" : string.Join("، ", selectedConditions),
            AccessoriesReceived = selectedAccessories.Count == 0 ? "الجهاز فقط (بدون ملحقات)" : string.Join("، ", selectedAccessories),
            IntakeNotes = IntakeNotes.Trim(),
            IsEstimatePendingInspection = IsEstimatePendingInspection,
            EstimatedCost = IsEstimatePendingInspection ? 0m : EstimatedCost,
            Status = MaintenanceOrderStatus.Received,
            Priority = Priority,
            AssignedTechnician = AssignedTechnician,
            DepositCashPaid = DepositCashAmount,
            DepositVodafonePaid = DepositVodafoneAmount,
            DepositVodafoneReference = DepositVodafoneRef.Trim(),
            DepositInstaPayPaid = DepositInstaPayAmount,
            DepositInstaPayReference = DepositInstaPayRef.Trim()
        };

        var depositDetail = order.HasDeposit ? $" · تحصيل مقدم: {order.DepositSummaryText}" : " · بدون مقدم";
        order.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = order.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة",
            Details = $"استلام {order.DeviceFullTitle} للعميل «{order.CustomerName}» ({order.PriorityTitle}){depositDetail}.",
            ActorName = currentUserName,
            Amount = order.HasDeposit ? order.TotalDepositPaid : null
        });

        sharedMaintenanceOrders.Insert(0, order);
        LastSavedOrder = order;

        ResetForm();
        SetStatus(
            $"تم تسجيل أمر الصيانة {orderNumber} بنجاح للعميل «{order.CustomerName}» ({order.DeviceFullTitle}){depositDetail}.",
            false);

        if (openReceiptAfter)
            OpenReceiptPreview(order);
    }

    private void OpenReceiptPreview(MaintenanceOrderDesignItem order)
    {
        var vm = new MaintenanceReceiptPreviewViewModel(order, modeLabel =>
        {
            order.ActivityLog.Add(new MaintenanceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "معاينة / طباعة تذكرة صيانة",
                Details = $"معاينة بصيغة «{modeLabel}».",
                ActorName = currentUserName
            });
            SetStatus($"تمت معاينة طباعة {order.OrderNumber} ({modeLabel}).", false);
        });
        OpenReceiptPreviewRequested?.Invoke(vm);
    }

    private void ResetForm()
    {
        SelectedCustomer = null;
        CustomerSearchText = "";
        IsCustomerPickerOpen = true;
        ContactPhone = "";

        DeviceType = "هاتف ذكي";
        DeviceBrand = "Apple";
        DeviceModel = "";
        DeviceColor = "";
        SerialOrImei = "";
        PasscodeType = MaintenancePasscodeType.None;
        PasscodeValue = "";

        ReportedFault = "";
        foreach (var cond in ConditionChecklist)
            cond.IsSelected = false;
        CustomConditionNote = "";
        foreach (var acc in AccessoryChecklist)
            acc.IsSelected = false;
        IntakeNotes = "";

        Priority = MaintenancePriority.Normal;
        AssignedTechnician = Technicians[0];
        ExpectedDeliveryLabel = "غدًا";
        ExpectedDeliveryDate = DateTime.Today.AddDays(1);

        IsEstimatePendingInspection = false;
        EstimatedCostText = "";
        ApplyDepositMode(MaintenanceDepositMode.NoDeposit);
        Changed(nameof(NextOrderNumberDisplay));
    }

    private void RefreshFilteredCustomers()
    {
        var q = CustomerSearchText.Trim();
        var matches = sharedCustomers.Where(c =>
            string.IsNullOrEmpty(q)
            || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || c.Phone.Contains(q, StringComparison.OrdinalIgnoreCase)
            || c.Address.Contains(q, StringComparison.OrdinalIgnoreCase));

        FilteredCustomers.Clear();
        foreach (var c in matches)
            FilteredCustomers.Add(c);

        Changed(nameof(HasNoFilteredCustomers));
        Changed(nameof(IsCustomerPickerVisible));
    }

    private void RefreshFilteredRecentOrders()
    {
        var q = RecentSearchText.Trim();
        var today = DateTime.Today;

        var matches = sharedMaintenanceOrders.Where(o =>
        {
            if (IsRecentTodayFilter && o.ReceivedAt.Date != today)
                return false;
            if (IsRecentUrgentFilter && !o.IsUrgent)
                return false;

            if (!string.IsNullOrEmpty(q))
            {
                return o.OrderNumber.Contains(q, StringComparison.OrdinalIgnoreCase)
                       || o.CustomerName.Contains(q, StringComparison.OrdinalIgnoreCase)
                       || o.CustomerPhone.Contains(q, StringComparison.OrdinalIgnoreCase)
                       || o.DeviceModel.Contains(q, StringComparison.OrdinalIgnoreCase)
                       || o.DeviceBrand.Contains(q, StringComparison.OrdinalIgnoreCase)
                       || o.SerialOrImei.Contains(q, StringComparison.OrdinalIgnoreCase)
                       || o.ReportedFault.Contains(q, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        });

        FilteredRecentOrders.Clear();
        foreach (var o in matches)
            FilteredRecentOrders.Add(o);

        Changed(nameof(HasNoFilteredRecentOrders));
    }

    private void NotifyRecentKpis()
    {
        Changed(nameof(TodayCountDisplay));
        Changed(nameof(ActiveOrdersCountDisplay));
        Changed(nameof(TodayDepositsDisplay));
    }

    private void NotifyFinancialSummary()
    {
        Changed(nameof(EstimatedCost));
        Changed(nameof(TotalDepositPaid));
        Changed(nameof(HasDepositOverEstimate));
        Changed(nameof(EstimatedRemaining));
        Changed(nameof(EstimatedCostSummaryDisplay));
        Changed(nameof(TotalDepositPaidDisplay));
        Changed(nameof(EstimatedRemainingDisplay));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
