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
    private readonly Data.MaintenanceRepository? repository;
    private readonly Action? onOrdersSaved;
    private readonly Func<PosCustomerDesignItem, PosCustomerDesignItem>? createCustomer;
    private readonly Func<string, int?, string?>? validateCustomerPhone;
    private readonly Action? reloadCustomers;
    private int nextOrderId;

    // SQL-backed header data: the real next order number and the last successful sync.
    private string nextOrderNumberPreview = "";
    private bool isNextOrderNumberPreviewExact;
    private DateTime? lastSyncedAt;

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
    private string depositInstaPayText = "";

    // Recent orders sidebar
    private MaintenanceOrderDesignItem? lastSavedOrder;
    private string recentSearchText = "";
    private string recentFilterMode = "الكل";

    private string statusMessage = "";
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

    /// <summary>True when this screen reads and writes SQL Server; the view swaps the design badge accordingly.</summary>
    public bool IsDatabaseBacked => repository is not null;

    public string DataConnectionBadgeText => IsDatabaseBacked
        ? "متصل بقاعدة البيانات — SQL Server"
        : "وضع عرض — بدون قاعدة بيانات";

    public string LastSyncedDisplay => lastSyncedAt is { } at
        ? $"آخر مزامنة: {at:yyyy-MM-dd HH:mm:ss}"
        : IsDatabaseBacked
            ? "لم تتم المزامنة بعد"
            : "بيانات تجريبية في الذاكرة";

    /// <summary>
    /// The next order number as dbo.DocumentSequences will hand it out. When the sequence cannot be
    /// read the badge falls back to a hint computed from the loaded rows and says «تقريبي».
    /// </summary>
    public string NextOrderNumberDisplay => string.IsNullOrWhiteSpace(nextOrderNumberPreview)
        ? "رقم الأمر القادم: —"
        : $"رقم الأمر القادم: {nextOrderNumberPreview}{(isNextOrderNumberPreviewExact ? "" : " (تقريبي)")}";

    /// <summary>True when device passcodes are encrypted with a configured AES key.</summary>
    public bool IsPasscodeProtectionEnabled => repository?.IsPasscodeProtectionEnabled ?? false;

    public string PasscodeProtectionHint => !IsDatabaseBacked
        ? "وضع عرض: لا يُحفظ رمز الجهاز في أي مكان."
        : IsPasscodeProtectionEnabled
            ? "يُحفظ رمز الجهاز مشفّرًا (AES-GCM) بمفتاح خارج قاعدة البيانات."
            : "تنبيه: مفتاح تشفير الرموز غير مُهيّأ، لذا يُحفظ الرمز كنص عادي. اضبط PROJECTZ_PASSCODE_KEY أو PasscodeKey في connectionsettings.json.");

    /// <summary>Duplicate-phone check used by the «عميل جديد» dialog when the directory lives in SQL.</summary>
    public string? ValidateNewCustomerPhone(string? phone, int? editingCustomerId)
        => validateCustomerPhone?.Invoke(phone ?? "", editingCustomerId);

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
    public ActionCommand RefreshFromDatabaseCommand { get; }

    public RelayCommand<string> SetRecentFilterCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> PreviewOrderReceiptCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> CopyCustomerForNewOrderCommand { get; }

    public MaintenanceIntakeViewModel()
        : this("المشرف", null, null, null)
    {
    }

    public MaintenanceIntakeViewModel(
        string currentUserName,
        ObservableCollection<PosCustomerDesignItem>? sharedCustomers = null,
        ObservableCollection<MaintenanceOrderDesignItem>? sharedMaintenanceOrders = null,
        Data.MaintenanceRepository? repository = null,
        Action? onOrdersSaved = null,
        Func<PosCustomerDesignItem, PosCustomerDesignItem>? createCustomer = null,
        Func<string, int?, string?>? validateCustomerPhone = null,
        Action? reloadCustomers = null)
    {
        this.currentUserName = currentUserName;
        this.repository = repository;
        this.onOrdersSaved = onOrdersSaved;
        this.createCustomer = createCustomer;
        this.validateCustomerPhone = validateCustomerPhone;
        this.reloadCustomers = reloadCustomers;
        statusMessage = repository is null
            ? "وضع عرض بدون قاعدة بيانات: تُحفظ الأوامر في الذاكرة فقط حتى تُضبط الاتصال."
            : "جاهز للاستلام: يُحفظ أمر الصيانة والعربون وسجل الأنشطة مباشرة في SQL Server.";
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
        RefreshFromDatabaseCommand = new ActionCommand(RefreshFromDatabase);

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
        RefreshNextOrderNumberPreview();
        if (repository is not null)
            lastSyncedAt = DateTime.Now;
    }

    public void NotifyExternalDataChanged()
    {
        if (repository is not null)
        {
            try
            {
                repository.LoadMaintenanceOrdersInto(sharedMaintenanceOrders);
                lastSyncedAt = DateTime.Now;
                Changed(nameof(LastSyncedDisplay));
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر تحميل أوامر الصيانة: {ex.Message}", true);
            }
        }
        RefreshFilteredCustomers();
        RefreshFilteredRecentOrders();
        NotifyRecentKpis();
        RefreshNextOrderNumberPreview();
    }

    /// <summary>
    /// Reloads the two lists this screen depends on — the maintenance orders and the customer
    /// directory — from SQL Server and reports the result on the status strip.
    /// </summary>
    public void RefreshFromDatabase()
    {
        if (repository is null)
        {
            SetStatus("وضع العرض بدون قاعدة بيانات: لا يوجد ما يُحدَّث من SQL Server.", true);
            return;
        }

        var ordersError = "";
        var customersError = "";

        try
        {
            repository.LoadMaintenanceOrdersInto(sharedMaintenanceOrders);
        }
        catch (Exception ex)
        {
            ordersError = ex.Message;
        }

        try
        {
            reloadCustomers?.Invoke();
        }
        catch (Exception ex)
        {
            customersError = ex.Message;
        }

        RefreshFilteredRecentOrders();
        RefreshFilteredCustomers();
        NotifyRecentKpis();
        RefreshNextOrderNumberPreview();

        if (ordersError.Length > 0 || customersError.Length > 0)
        {
            var details = string.Join(" · ", new[] { ordersError, customersError }.Where(m => m.Length > 0));
            SetStatus($"تعذّر تحديث بيانات الشاشة من قاعدة البيانات: {details}", true);
            return;
        }

        lastSyncedAt = DateTime.Now;
        Changed(nameof(LastSyncedDisplay));
        if (sharedMaintenanceOrders.Count > 0)
            LastSavedOrder = sharedMaintenanceOrders[0];

        SetStatus(
            $"تم تحديث أوامر الصيانة ({sharedMaintenanceOrders.Count}) ودليل العملاء ({sharedCustomers.Count}) من قاعدة البيانات.",
            false);
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
        // A reload adds row by row; the sequence badge is refreshed once by the caller instead.
        RefreshFilteredRecentOrders();
        NotifyRecentKpis();
    }

    private int GetNextOrderIdPreview()
    {
        var maxId = sharedMaintenanceOrders.Count > 0 ? sharedMaintenanceOrders.Max(o => o.Id) : 0;
        return Math.Max(nextOrderId, maxId + 1);
    }

    /// <summary>
    /// Reads the real next order number from dbo.DocumentSequences. The badge is only a hint, so a
    /// failed read never blocks the form: it degrades to the local estimate and says «تقريبي».
    /// </summary>
    private void RefreshNextOrderNumberPreview()
    {
        if (repository is not null)
        {
            try
            {
                nextOrderNumberPreview = repository.PeekNextOrderNumber();
                isNextOrderNumberPreviewExact = true;
                Changed(nameof(NextOrderNumberDisplay));
                return;
            }
            catch (Exception)
            {
                // Fall through to the local estimate; the save path reports real failures.
            }
        }

        nextOrderNumberPreview = $"MNT-{DateTime.Now.Year:0000}-{GetNextOrderIdPreview():0000}";
        isNextOrderNumberPreviewExact = false;
        Changed(nameof(NextOrderNumberDisplay));
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
        OpenAddCustomerRequested?.Invoke(CustomerSearchText, CreateCustomerFromDialog);
    }

    /// <summary>
    /// Called when the «عميل جديد» dialog returns (or when a customer is created from the directory).
    /// With SQL Server behind the screen the row must exist in dbo.Customers first: a fabricated
    /// in-memory id would break the FK_MaintenanceOrders_Customer constraint on save.
    /// </summary>
    private void CreateCustomerFromDialog(PosCustomerDesignItem created)
    {
        if (created is null) return;

        if (repository is null || createCustomer is null)
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
            SetStatus($"تمت إضافة العميل «{assigned.Name}» إلى الجلسة الحالية (بدون قاعدة بيانات) وربطه بأمر الصيانة.", false);
            return;
        }

        var duplicate = ValidateNewCustomerPhone(created.Phone, null);
        if (!string.IsNullOrWhiteSpace(duplicate))
        {
            SetStatus(duplicate, true);
            return;
        }

        PosCustomerDesignItem persisted;
        try
        {
            persisted = createCustomer(created);
        }
        catch (Exception ex)
        {
            SetStatus($"تعذّر حفظ العميل «{created.Name}» في قاعدة البيانات: {ex.Message}", true);
            return;
        }

        // The directory is reloaded from SQL after the insert, so prefer the instance the other
        // screens already hold instead of keeping a second copy of the same customer.
        var linked = sharedCustomers.FirstOrDefault(c => c.Id == persisted.Id);
        if (linked is null)
        {
            sharedCustomers.Add(persisted);
            linked = persisted;
        }

        SelectCustomer(linked);
        SetStatus($"تم حفظ العميل «{linked.Name}» في قاعدة البيانات ({linked.CustomerCodeDisplay}) وربطه بأمر الصيانة.", false);
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
            DepositInstaPayText = "";
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

        if (!IsPlausiblePhone(trimmedPhone))
        {
            SetStatus("رقم هاتف التواصل يجب أن يحتوي على 7 أرقام على الأقل (مثال: 01012345678).", true);
            return;
        }

        if (repository is not null && !SelectedCustomer.IsWalkIn && SelectedCustomer.Id <= 0)
        {
            SetStatus(
                "العميل المختار غير محفوظ في قاعدة البيانات. أضفه من زر «+ عميل جديد» ليُربط به أمر الصيانة.",
                true);
            return;
        }

        if (ValidateFieldLengths(trimmedPhone) is { } lengthProblem)
        {
            SetStatus(lengthProblem, true);
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

        var trimmedSerial = SerialOrImei.Trim();
        if (repository is not null && trimmedSerial.Length > 0)
        {
            try
            {
                if (repository.HasOpenOrderWithSerial(trimmedSerial))
                {
                    SetStatus(
                        $"يوجد أمر صيانة مفتوح آخر بنفس الرقم التسلسلي «{trimmedSerial}». راجع الأوامر النشطة أو أفرغ خانة السيريال ثم أعد الحفظ.",
                        true);
                    return;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر التحقق من الرقم التسلسلي في قاعدة البيانات: {ex.Message}", true);
                return;
            }
        }

        int orderId = 0;
        string orderNumber = "";
        if (repository is null)
        {
            orderId = GetNextOrderIdPreview();
            nextOrderId = orderId + 1;
            orderNumber = $"MNT-{DateTime.Now.Year:0000}-{orderId:0000}";
        }

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
            SerialOrImei = trimmedSerial,
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
            DepositInstaPayPaid = DepositInstaPayAmount,
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

        if (repository is not null)
        {
            try
            {
                var saved = repository.CreateMaintenanceOrder(order);
                order.Id = saved.OrderId;
                order.OrderNumber = saved.OrderNumber;
                lastSyncedAt = DateTime.Now;
                Changed(nameof(LastSyncedDisplay));
                RefreshNextOrderNumberPreview();
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر تسجيل أمر الصيانة: {ex.Message}", true);
                return;
            }
        }

        sharedMaintenanceOrders.Insert(0, order);
        LastSavedOrder = order;

        // Let the other maintenance screens (which own their own collections) reload.
        onOrdersSaved?.Invoke();

        ResetForm();
        SetStatus(
            $"تم تسجيل أمر الصيانة {order.OrderNumber} بنجاح للعميل «{order.CustomerName}» ({order.DeviceFullTitle}){depositDetail}" +
            (repository is null ? " في الذاكرة فقط." : " في قاعدة البيانات."),
            false);

        if (openReceiptAfter)
            OpenReceiptPreview(order);
    }

    private void OpenReceiptPreview(MaintenanceOrderDesignItem order)
    {
        var vm = new MaintenanceReceiptPreviewViewModel(order, modeLabel =>
        {
            var entry = new MaintenanceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "معاينة / طباعة تذكرة صيانة",
                Details = $"معاينة بصيغة «{modeLabel}».",
                ActorName = currentUserName
            };
            order.ActivityLog.Add(entry);
            PersistActivity(order, entry);
            SetStatus($"تمت معاينة طباعة {order.OrderNumber} ({modeLabel}).", false);
        });
        OpenReceiptPreviewRequested?.Invoke(vm);
    }

    /// <summary>Persists a single activity entry for an already-saved order.</summary>
    private void PersistActivity(MaintenanceOrderDesignItem order, MaintenanceActivityEntry entry)
    {
        if (repository is null || order.Id <= 0) return;

        try
        {
            repository.AppendMaintenanceActivity(order.Id, entry);
        }
        catch (Exception ex)
        {
            SetStatus($"تعذّر حفظ سجل الطباعة في القاعدة: {ex.Message}", true);
        }
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
        RefreshNextOrderNumberPreview();
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

    private static bool IsPlausiblePhone(string phone) => phone.Count(char.IsDigit) >= 7;

    private static string? TooLong(string value, int limit, string label)
        => value.Length > limit
            ? $"{label} أطول من الحد المسموح ({limit} حرفًا). اختصره ثم أعد الحفظ."
            : null;

    /// <summary>
    /// Mirrors the column widths in sql/06 so an over-long value produces an Arabic message here
    /// instead of SQL Server error 8152 ("String or binary data would be truncated").
    /// </summary>
    private string? ValidateFieldLengths(string phone)
    {
        var problems = new[]
        {
            TooLong(phone, 30, "رقم هاتف التواصل"),
            TooLong(SerialOrImei.Trim(), 150, "الرقم التسلسلي / IMEI"),
            TooLong(DeviceModel.Trim(), 150, "موديل الجهاز"),
            TooLong(DeviceColor.Trim(), 80, "لون الجهاز"),
            TooLong(string.IsNullOrWhiteSpace(DeviceBrand) ? "أخرى" : DeviceBrand.Trim(), 100, "ماركة الجهاز"),
            TooLong(DeviceType, 100, "نوع الجهاز"),
            TooLong(ReportedFault.Trim(), 2000, "وصف العطل"),
            TooLong(IntakeNotes.Trim(), 1500, "ملاحظات الاستلام"),
            TooLong(ConditionSummaryPreview, 1500, "الحالة الظاهرية"),
            TooLong(AccessoriesSummaryPreview, 1000, "الملحقات المستلمة"),
            TooLong(AssignedTechnician, 150, "الفني المسؤول"),
            TooLong(ExpectedDeliveryLabel.Trim(), 200, "موعد التسليم المتوقع"),
            TooLong(PasscodeValue.Trim(), Services.PasscodeProtector.MaxPlainLength, "رمز فتح الجهاز"),
        };

        return problems.FirstOrDefault(problem => problem is not null);
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
