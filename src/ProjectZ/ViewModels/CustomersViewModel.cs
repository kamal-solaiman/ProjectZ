using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class CustomerDirectoryEntryViewModel : ObservableObject
{
    private int salesInvoicesCount;
    private int maintenanceOrdersCount;
    private decimal totalSalesNet;
    private decimal totalSalesPaid;
    private decimal salesRemaining;
    private decimal totalMaintenanceValue;
    private decimal totalMaintenancePaid;
    private decimal maintenanceRemaining;
    private DateTime? lastActivityAt;
    private bool isSelected;

    public PosCustomerDesignItem Customer { get; }
    public List<SalesInvoiceDesignItem> SalesInvoices { get; } = [];
    public List<MaintenanceOrderDesignItem> MaintenanceOrders { get; } = [];

    public CustomerDirectoryEntryViewModel(PosCustomerDesignItem customer)
    {
        Customer = customer;
    }

    public int SalesInvoicesCount => salesInvoicesCount;
    public int MaintenanceOrdersCount => maintenanceOrdersCount;
    public int TotalTransactionsCount => salesInvoicesCount + maintenanceOrdersCount;
    public bool HasSalesInvoices => salesInvoicesCount > 0;
    public bool HasMaintenanceOrders => maintenanceOrdersCount > 0;
    public bool HasTransactions => TotalTransactionsCount > 0;

    public decimal TotalSalesNet => totalSalesNet;
    public decimal TotalSalesPaid => totalSalesPaid;
    public decimal SalesRemaining => salesRemaining;

    public decimal TotalMaintenanceValue => totalMaintenanceValue;
    public decimal TotalMaintenancePaid => totalMaintenancePaid;
    public decimal MaintenanceRemaining => maintenanceRemaining;

    public decimal TotalBusinessValue => totalSalesNet + totalMaintenanceValue;
    public decimal TotalPaid => totalSalesPaid + totalMaintenancePaid;
    public decimal TotalRemaining => salesRemaining + maintenanceRemaining;
    public bool HasRemaining => TotalRemaining > 0m;
    public bool HasSalesRemaining => salesRemaining > 0m;
    public bool HasMaintenanceRemaining => maintenanceRemaining > 0m;

    public DateTime? LastActivityAt => lastActivityAt;

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected == value) return;
            isSelected = value;
            Changed();
        }
    }

    public string TotalSalesNetDisplay => PosFormatting.FormatCurrency(TotalSalesNet);
    public string TotalSalesPaidDisplay => PosFormatting.FormatCurrency(TotalSalesPaid);
    public string SalesRemainingDisplay => PosFormatting.FormatCurrency(SalesRemaining);

    public string TotalMaintenanceValueDisplay => PosFormatting.FormatCurrency(TotalMaintenanceValue);
    public string TotalMaintenancePaidDisplay => PosFormatting.FormatCurrency(TotalMaintenancePaid);
    public string MaintenanceRemainingDisplay => PosFormatting.FormatCurrency(MaintenanceRemaining);

    public string TotalBusinessValueDisplay => PosFormatting.FormatCurrency(TotalBusinessValue);
    public string TotalPaidDisplay => PosFormatting.FormatCurrency(TotalPaid);
    public string TotalRemainingDisplay => PosFormatting.FormatCurrency(TotalRemaining);

    public string BalanceStatusTitle => HasRemaining ? $"متبقي: {TotalRemainingDisplay}" : (HasTransactions ? "خالص / مسدد" : "لا توجد معاملات");
    public string TransactionsSummaryText => HasTransactions
        ? $"مبيعات: {SalesInvoicesCount} · صيانة: {MaintenanceOrdersCount}"
        : "بدون معاملات مسجلة";
    public string LastActivityDisplay => LastActivityAt.HasValue
        ? LastActivityAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : "—";

    public void Recalculate(
        IEnumerable<SalesInvoiceDesignItem> matchedSales,
        IEnumerable<MaintenanceOrderDesignItem> matchedOrders)
    {
        SalesInvoices.Clear();
        SalesInvoices.AddRange(matchedSales.OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id));

        MaintenanceOrders.Clear();
        MaintenanceOrders.AddRange(matchedOrders.OrderByDescending(o => o.ReceivedAt).ThenByDescending(o => o.Id));

        salesInvoicesCount = SalesInvoices.Count;
        maintenanceOrdersCount = MaintenanceOrders.Count;

        var activeSales = SalesInvoices.Where(i => !i.IsCancelled && !i.IsFullyReturned).ToList();
        totalSalesNet = activeSales.Sum(i => i.EffectiveNetSales);
        totalSalesPaid = activeSales.Sum(i => Math.Min(i.EffectiveNetSales, i.NetCollected));
        salesRemaining = activeSales.Sum(i => i.RemainingAmount);

        var activeOrders = MaintenanceOrders.Where(o => !o.IsCancelled).ToList();
        totalMaintenanceValue = activeOrders.Where(o => !o.IsPriceStillPending).Sum(o => o.CurrentEffectiveCost);
        totalMaintenancePaid = activeOrders.Sum(o => o.TotalCollectedAll);
        maintenanceRemaining = activeOrders.Where(o => !o.IsPriceStillPending).Sum(o => o.EstimatedRemaining);

        DateTime? latestSale = SalesInvoices.Count > 0 ? SalesInvoices.Max(i => i.IssuedAt) : null;
        DateTime? latestMaint = MaintenanceOrders.Count > 0 ? MaintenanceOrders.Max(o => o.DeliveredAt ?? o.ReceivedAt) : null;
        lastActivityAt = (latestSale, latestMaint) switch
        {
            (not null, not null) => latestSale >= latestMaint ? latestSale : latestMaint,
            (not null, null) => latestSale,
            (null, not null) => latestMaint,
            _ => null
        };

        Changed(nameof(SalesInvoicesCount));
        Changed(nameof(MaintenanceOrdersCount));
        Changed(nameof(TotalTransactionsCount));
        Changed(nameof(HasSalesInvoices));
        Changed(nameof(HasMaintenanceOrders));
        Changed(nameof(HasTransactions));
        Changed(nameof(TotalSalesNet));
        Changed(nameof(TotalSalesPaid));
        Changed(nameof(SalesRemaining));
        Changed(nameof(TotalMaintenanceValue));
        Changed(nameof(TotalMaintenancePaid));
        Changed(nameof(MaintenanceRemaining));
        Changed(nameof(TotalBusinessValue));
        Changed(nameof(TotalPaid));
        Changed(nameof(TotalRemaining));
        Changed(nameof(HasRemaining));
        Changed(nameof(HasSalesRemaining));
        Changed(nameof(HasMaintenanceRemaining));
        Changed(nameof(LastActivityAt));
        Changed(nameof(TotalSalesNetDisplay));
        Changed(nameof(TotalSalesPaidDisplay));
        Changed(nameof(SalesRemainingDisplay));
        Changed(nameof(TotalMaintenanceValueDisplay));
        Changed(nameof(TotalMaintenancePaidDisplay));
        Changed(nameof(MaintenanceRemainingDisplay));
        Changed(nameof(TotalBusinessValueDisplay));
        Changed(nameof(TotalPaidDisplay));
        Changed(nameof(TotalRemainingDisplay));
        Changed(nameof(BalanceStatusTitle));
        Changed(nameof(TransactionsSummaryText));
        Changed(nameof(LastActivityDisplay));
    }
}

public sealed class CustomersViewModel : ObservableObject
{
    private readonly string currentUserName;
    private readonly ObservableCollection<PosCustomerDesignItem> sharedCustomers;
    private readonly ObservableCollection<SalesInvoiceDesignItem> sharedSalesInvoices;
    private readonly ObservableCollection<MaintenanceOrderDesignItem> sharedMaintenanceOrders;
    private readonly Action<PosCustomerDesignItem>? startPosSaleForCustomer;
    private readonly Action<PosCustomerDesignItem>? startMaintenanceForCustomer;
    private readonly Action<MaintenanceOrderDesignItem>? openOrderInMaintenanceActions;
    private readonly Action? notifyExternalFinancialDataChanged;

    private readonly List<CustomerDirectoryEntryViewModel> allEntries = [];
    private CustomerDirectoryEntryViewModel? selectedCustomerEntry;
    private string searchText = "";
    private string selectedTierFilter = "كل الفئات";
    private string selectedBalanceFilter = "جميع العملاء";
    private string selectedSortOption = "الأحدث نشاطًا";
    private int activeDetailTabIndex;
    private string statusMessage = "شاشة العملاء الموحدة: إدارة ملفات العملاء، متابعة فواتير البيع وتذاكر الصيانة، وتحصيل المستحقات المتبقية.";
    private bool isErrorStatus;

    public ObservableCollection<CustomerDirectoryEntryViewModel> FilteredCustomers { get; } = [];
    public ObservableCollection<SalesInvoiceDesignItem> SelectedCustomerSalesInvoices { get; } = [];
    public ObservableCollection<MaintenanceOrderDesignItem> SelectedCustomerMaintenanceOrders { get; } = [];

    public IReadOnlyList<string> TierFilterOptions { get; } =
    [
        "كل الفئات",
        "سعر قطاعي",
        "سعر مميز",
        "سعر جملة"
    ];

    public IReadOnlyList<string> BalanceFilterOptions { get; } =
    [
        "جميع العملاء",
        "لديهم مستحقات متبقية",
        "مسددون / بدون مديونية",
        "لديهم فواتير مبيعات",
        "لديهم تذاكر صيانة",
        "بدون معاملات بعد"
    ];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "الأحدث نشاطًا",
        "الأعلى تعاملًا",
        "الأعلى مديونية",
        "الأكثر معاملات",
        "الاسم أبجديًا"
    ];

    // Window/Dialog hooks wired by CustomersView.xaml.cs
    public Action<string?, Func<string, int?, string?>?, Action<PosCustomerDesignItem>>? OpenAddCustomerDialogRequested { get; set; }
    public Action<PosCustomerDesignItem, Func<string, int?, string?>?, Action<PosCustomerDesignItem>>? OpenEditCustomerDialogRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }
    public Action<CollectInvoicePaymentViewModel>? OpenCollectSalesPaymentRequested { get; set; }
    public Action<InvoicePrintPreviewViewModel>? OpenSalesPrintPreviewRequested { get; set; }
    public Func<MaintenanceDeliveryViewModel, bool?>? OpenMaintenanceDeliveryRequested { get; set; }
    public Action<MaintenanceInvoiceDetailsViewModel>? OpenMaintenanceDetailsRequested { get; set; }
    public Action<MaintenanceReceiptPreviewViewModel>? OpenMaintenanceReceiptPreviewRequested { get; set; }

    public CustomerDirectoryEntryViewModel? SelectedCustomerEntry
    {
        get => selectedCustomerEntry;
        set
        {
            if (ReferenceEquals(selectedCustomerEntry, value)) return;
            if (selectedCustomerEntry is not null)
                selectedCustomerEntry.IsSelected = false;

            selectedCustomerEntry = value;

            if (selectedCustomerEntry is not null)
                selectedCustomerEntry.IsSelected = true;

            RefreshSelectedCustomerTransactions();
            Changed();
            Changed(nameof(HasSelectedCustomer));
            Changed(nameof(SelectedSalesTabTitle));
            Changed(nameof(SelectedMaintenanceTabTitle));
            Changed(nameof(HasNoSelectedCustomerSales));
            Changed(nameof(HasNoSelectedCustomerMaintenance));
        }
    }

    public bool HasSelectedCustomer => SelectedCustomerEntry is not null;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            RefreshFilteredCustomers();
        }
    }

    public string SelectedTierFilter
    {
        get => selectedTierFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedTierFilter == value) return;
            selectedTierFilter = value;
            Changed();
            RefreshFilteredCustomers();
        }
    }

    public string SelectedBalanceFilter
    {
        get => selectedBalanceFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedBalanceFilter == value) return;
            selectedBalanceFilter = value;
            Changed();
            RefreshFilteredCustomers();
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
            RefreshFilteredCustomers();
        }
    }

    public int ActiveDetailTabIndex
    {
        get => activeDetailTabIndex;
        set
        {
            if (activeDetailTabIndex == value) return;
            activeDetailTabIndex = value;
            Changed();
            Changed(nameof(IsSalesTabSelected));
            Changed(nameof(IsMaintenanceTabSelected));
        }
    }

    public bool IsSalesTabSelected => ActiveDetailTabIndex == 0;
    public bool IsMaintenanceTabSelected => ActiveDetailTabIndex == 1;

    public string SelectedSalesTabTitle => SelectedCustomerEntry is null
        ? "فواتير المبيعات (0)"
        : $"فواتير المبيعات ({SelectedCustomerEntry.SalesInvoicesCount})";

    public string SelectedMaintenanceTabTitle => SelectedCustomerEntry is null
        ? "تذاكر الصيانة (0)"
        : $"تذاكر الصيانة ({SelectedCustomerEntry.MaintenanceOrdersCount})";

    public bool HasNoFilteredCustomers => FilteredCustomers.Count == 0;
    public bool HasNoSelectedCustomerSales => SelectedCustomerSalesInvoices.Count == 0;
    public bool HasNoSelectedCustomerMaintenance => SelectedCustomerMaintenanceOrders.Count == 0;

    // Top KPI Cards
    public string TotalCustomersCountDisplay
    {
        get
        {
            var registeredCount = sharedCustomers.Count(c => !c.IsWalkIn);
            return $"{registeredCount} عميل";
        }
    }

    public string TierBreakdownSubtext
    {
        get
        {
            var registered = sharedCustomers.Where(c => !c.IsWalkIn).ToList();
            var retail = registered.Count(c => c.DefaultPriceTier == PriceTier.Retail);
            var special = registered.Count(c => c.DefaultPriceTier == PriceTier.Special);
            var wholesale = registered.Count(c => c.DefaultPriceTier == PriceTier.Wholesale);
            return $"قطاعي: {retail} · مميز: {special} · جملة: {wholesale}";
        }
    }

    public string TotalBusinessValueDisplay => PosFormatting.FormatCurrency(allEntries.Sum(e => e.TotalBusinessValue));

    public string BusinessBreakdownSubtext
    {
        get
        {
            var sales = allEntries.Sum(e => e.TotalSalesNet);
            var maint = allEntries.Sum(e => e.TotalMaintenanceValue);
            return $"مبيعات: {PosFormatting.FormatCurrency(sales)} · صيانة: {PosFormatting.FormatCurrency(maint)}";
        }
    }

    public string TotalCollectedAllDisplay => PosFormatting.FormatCurrency(allEntries.Sum(e => e.TotalPaid));

    public string TransactionsCountSubtext
    {
        get
        {
            var salesCount = allEntries.Sum(e => e.SalesInvoicesCount);
            var maintCount = allEntries.Sum(e => e.MaintenanceOrdersCount);
            return $"{salesCount} فاتورة بيع · {maintCount} تذكرة صيانة";
        }
    }

    public string TotalRemainingAllDisplay => PosFormatting.FormatCurrency(allEntries.Sum(e => e.TotalRemaining));

    public string DebtorsCountSubtext
    {
        get
        {
            var debtors = allEntries.Count(e => e.HasRemaining);
            return debtors == 0
                ? "لا توجد مستحقات متبقية"
                : $"{debtors} عميل عليه مستحقات";
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

    public ActionCommand AddCustomerCommand { get; }
    public ActionCommand EditSelectedCustomerCommand { get; }
    public ActionCommand DeleteSelectedCustomerCommand { get; }
    public ActionCommand StartSaleForCustomerCommand { get; }
    public ActionCommand StartMaintenanceForCustomerCommand { get; }
    public ActionCommand RefreshDataCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public RelayCommand<CustomerDirectoryEntryViewModel> SelectCustomerCommand { get; }
    public RelayCommand<CustomerDirectoryEntryViewModel> EditCustomerRowCommand { get; }
    public RelayCommand<string> SelectDetailTabCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> CollectSalesInvoiceCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> PrintSalesInvoiceCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> OpenMaintenanceDetailsCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> DeliverAndCollectMaintenanceCommand { get; }
    public RelayCommand<MaintenanceOrderDesignItem> OpenMaintenanceInActionsCommand { get; }

    public CustomersViewModel()
        : this(
            "المشرف",
            new ObservableCollection<PosCustomerDesignItem>(PosDesignSeedData.CreateSampleCustomers()),
            CreateSampleInvoicesForDesign(),
            MaintenanceDesignSeedData.CreateSharedSampleOrders(PosDesignSeedData.CreateSampleCustomers()),
            null,
            null,
            null,
            null)
    {
    }

    private static ObservableCollection<SalesInvoiceDesignItem> CreateSampleInvoicesForDesign()
    {
        var catalog = PosDesignSeedData.CreateSharedSampleCatalog();
        var registrations = InventoryDesignSeedData.CreateSharedSampleRegistrations(catalog);
        return InvoiceDesignSeedData.CreateSharedSampleInvoices(catalog, registrations);
    }

    public CustomersViewModel(
        string currentUserName,
        ObservableCollection<PosCustomerDesignItem> sharedCustomers,
        ObservableCollection<SalesInvoiceDesignItem> sharedSalesInvoices,
        ObservableCollection<MaintenanceOrderDesignItem> sharedMaintenanceOrders,
        Action<PosCustomerDesignItem>? startPosSaleForCustomer = null,
        Action<PosCustomerDesignItem>? startMaintenanceForCustomer = null,
        Action<MaintenanceOrderDesignItem>? openOrderInMaintenanceActions = null,
        Action? notifyExternalFinancialDataChanged = null)
    {
        this.currentUserName = currentUserName;
        this.sharedCustomers = sharedCustomers;
        this.sharedSalesInvoices = sharedSalesInvoices;
        this.sharedMaintenanceOrders = sharedMaintenanceOrders;
        this.startPosSaleForCustomer = startPosSaleForCustomer;
        this.startMaintenanceForCustomer = startMaintenanceForCustomer;
        this.openOrderInMaintenanceActions = openOrderInMaintenanceActions;
        this.notifyExternalFinancialDataChanged = notifyExternalFinancialDataChanged;

        this.sharedCustomers.CollectionChanged += OnExternalCollectionChanged;
        this.sharedSalesInvoices.CollectionChanged += OnExternalCollectionChanged;
        this.sharedMaintenanceOrders.CollectionChanged += OnExternalCollectionChanged;

        AddCustomerCommand = new ActionCommand(OpenAddCustomer);
        EditSelectedCustomerCommand = new ActionCommand(() => EditCustomer(SelectedCustomerEntry));
        DeleteSelectedCustomerCommand = new ActionCommand(() => DeleteCustomer(SelectedCustomerEntry));
        StartSaleForCustomerCommand = new ActionCommand(StartSaleForSelected);
        StartMaintenanceForCustomerCommand = new ActionCommand(StartMaintenanceForSelected);
        RefreshDataCommand = new ActionCommand(() =>
        {
            NotifyExternalDataChanged();
            SetStatus("تم تحديث دليل العملاء وجميع أرصدة المبيعات والصيانة.", false);
        });
        ClearFiltersCommand = new ActionCommand(ClearAllFilters);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        SelectCustomerCommand = new RelayCommand<CustomerDirectoryEntryViewModel>(entry =>
        {
            if (entry is not null)
                SelectedCustomerEntry = entry;
        });
        EditCustomerRowCommand = new RelayCommand<CustomerDirectoryEntryViewModel>(entry =>
        {
            if (entry is not null)
            {
                SelectedCustomerEntry = entry;
                EditCustomer(entry);
            }
        });
        SelectDetailTabCommand = new RelayCommand<string>(idxStr =>
        {
            if (int.TryParse(idxStr, out var idx))
                ActiveDetailTabIndex = idx;
        });
        CollectSalesInvoiceCommand = new RelayCommand<SalesInvoiceDesignItem>(OpenCollectSalesPayment);
        PrintSalesInvoiceCommand = new RelayCommand<SalesInvoiceDesignItem>(OpenSalesPrintPreview);
        OpenMaintenanceDetailsCommand = new RelayCommand<MaintenanceOrderDesignItem>(OpenMaintenanceDetails);
        DeliverAndCollectMaintenanceCommand = new RelayCommand<MaintenanceOrderDesignItem>(OpenMaintenanceDelivery);
        OpenMaintenanceInActionsCommand = new RelayCommand<MaintenanceOrderDesignItem>(order =>
        {
            if (order is not null)
                this.openOrderInMaintenanceActions?.Invoke(order);
        });

        RebuildEntriesAndRefresh();
    }

    public void NotifyExternalDataChanged()
    {
        RebuildEntriesAndRefresh();
    }

    private void OnExternalCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildEntriesAndRefresh();

    private void ClearAllFilters()
    {
        searchText = "";
        selectedTierFilter = "كل الفئات";
        selectedBalanceFilter = "جميع العملاء";
        selectedSortOption = "الأحدث نشاطًا";
        Changed(nameof(SearchText));
        Changed(nameof(SelectedTierFilter));
        Changed(nameof(SelectedBalanceFilter));
        Changed(nameof(SelectedSortOption));
        RefreshFilteredCustomers();
        SetStatus("تمت إعادة ضبط الفلاتر لعرض جميع العملاء.", false);
    }

    private string? ValidateDuplicatePhone(string phone, int? editingCustomerId)
    {
        var normalized = NormalizePhone(phone);
        if (string.IsNullOrEmpty(normalized)) return null;

        var duplicate = sharedCustomers.FirstOrDefault(c =>
            !c.IsWalkIn
            && c.Id != (editingCustomerId ?? -1)
            && (NormalizePhone(c.Phone) == normalized || NormalizePhone(c.SecondaryPhone) == normalized));

        return duplicate is not null
            ? $"رقم الهاتف مسجل بالفعل للعميل «{duplicate.Name}» ({duplicate.CustomerCodeDisplay})."
            : null;
    }

    private static string NormalizePhone(string? phone)
        => string.IsNullOrWhiteSpace(phone)
            ? ""
            : new string(phone.Where(char.IsDigit).ToArray());

    private void OpenAddCustomer()
    {
        OpenAddCustomerDialogRequested?.Invoke(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            ValidateDuplicatePhone,
            created =>
            {
                var nextId = sharedCustomers.Select(c => c.Id).DefaultIfEmpty(0).Max() + 1;
                var newCustomer = new PosCustomerDesignItem
                {
                    Id = nextId,
                    Name = created.Name,
                    Phone = created.Phone,
                    SecondaryPhone = created.SecondaryPhone,
                    Address = created.Address,
                    Notes = created.Notes,
                    DefaultPriceTier = created.DefaultPriceTier,
                    IsWalkIn = false
                };

                sharedCustomers.Add(newCustomer);
                RebuildEntriesAndRefresh(selectCustomerId: newCustomer.Id);
                notifyExternalFinancialDataChanged?.Invoke();
                SetStatus($"تم تسجيل العميل «{newCustomer.Name}» ({newCustomer.CustomerCodeDisplay}) بنجاح ومشاركته مع المبيعات والصيانة.", false);
            });
    }

    private void EditCustomer(CustomerDirectoryEntryViewModel? entry)
    {
        if (entry is null) return;
        if (entry.Customer.IsWalkIn)
        {
            SetStatus("العميل النقدي (العام) هو حساب افتراضي ثابت ولا يمكن تعديله أو حذفه.", true);
            return;
        }

        OpenEditCustomerDialogRequested?.Invoke(
            entry.Customer,
            ValidateDuplicatePhone,
            updated =>
            {
                // Sync snapshot info on linked sales invoices & maintenance orders for this customer
                foreach (var inv in sharedSalesInvoices.Where(i => !i.IsWalkInCustomer && i.CustomerId == updated.Id))
                {
                    inv.CustomerName = updated.Name;
                    inv.CustomerPhone = updated.Phone;
                    inv.CustomerAddress = updated.Address;
                    inv.DefaultPriceTier = updated.DefaultPriceTier;
                }

                foreach (var ord in sharedMaintenanceOrders.Where(o => !o.IsWalkInCustomer && o.CustomerId == updated.Id))
                {
                    ord.CustomerName = updated.Name;
                    ord.CustomerPhone = updated.Phone;
                    ord.CustomerSecondaryPhone = updated.SecondaryPhone;
                    ord.CustomerAddress = updated.Address;
                }

                RebuildEntriesAndRefresh(selectCustomerId: updated.Id);
                notifyExternalFinancialDataChanged?.Invoke();
                SetStatus($"تم تحديث بيانات العميل «{updated.Name}» ({updated.CustomerCodeDisplay}) ومزامنتها عبر النظام.", false);
            });
    }

    private void DeleteCustomer(CustomerDirectoryEntryViewModel? entry)
    {
        if (entry is null) return;
        var customer = entry.Customer;
        if (customer.IsWalkIn)
        {
            SetStatus("العميل النقدي (العام) هو حساب أساسي في نقطة البيع ولا يمكن حذفه.", true);
            return;
        }

        if (entry.HasRemaining)
        {
            SetStatus(
                $"لا يمكن حذف العميل «{customer.Name}» لوجود مستحقات متبقية عليه بقيمة {entry.TotalRemainingDisplay}؛ يجب تحصيلها أولًا.",
                true);
            return;
        }

        var detailsNote = entry.HasTransactions
            ? $"هل تريد حذف العميل «{customer.Name}» من الدليل؟ لديه {entry.TotalTransactionsCount} معاملات سابقة مسددة ستبقى محفوظة في أرشيف الفواتير للرجوع إليها."
            : $"هل تريد حذف العميل «{customer.Name}» ({customer.CustomerCodeDisplay}) نهائيًا من قائمة العملاء؟";

        var confirmVm = new ConfirmActionViewModel(
            "تأكيد حذف العميل",
            $"{customer.Name} — {customer.DisplayPhone}",
            detailsNote,
            "حذف العميل");

        if (OpenConfirmDialogRequested is not null)
            OpenConfirmDialogRequested(confirmVm, () => ConfirmDeleteCustomer(customer));
        else
            ConfirmDeleteCustomer(customer);
    }

    private void ConfirmDeleteCustomer(PosCustomerDesignItem customer)
    {
        if (!sharedCustomers.Remove(customer)) return;

        RebuildEntriesAndRefresh();
        notifyExternalFinancialDataChanged?.Invoke();
        SetStatus($"تم حذف العميل «{customer.Name}» من دليل العملاء.", false);
    }

    private void StartSaleForSelected()
    {
        if (SelectedCustomerEntry is null) return;
        startPosSaleForCustomer?.Invoke(SelectedCustomerEntry.Customer);
    }

    private void StartMaintenanceForSelected()
    {
        if (SelectedCustomerEntry is null) return;
        startMaintenanceForCustomer?.Invoke(SelectedCustomerEntry.Customer);
    }

    private void OpenCollectSalesPayment(SalesInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        if (!invoice.CanCollectRemaining)
        {
            SetStatus($"الفاتورة {invoice.InvoiceNumber} مسددة بالكامل ولا يوجد عليها مبلغ متبقٍ.", true);
            return;
        }

        var vm = new CollectInvoicePaymentViewModel(invoice, submission =>
        {
            var paidNow = submission.CashAmount + submission.VodafoneAmount + submission.InstaPayAmount;
            invoice.CashPaid += submission.CashAmount;
            invoice.VodafoneCashPaid += submission.VodafoneAmount;
            if (!string.IsNullOrWhiteSpace(submission.VodafoneReference))
            {
                invoice.VodafoneCashReference = string.IsNullOrWhiteSpace(invoice.VodafoneCashReference)
                    ? submission.VodafoneReference
                    : $"{invoice.VodafoneCashReference} / {submission.VodafoneReference}";
            }

            invoice.InstaPayPaid += submission.InstaPayAmount;
            if (!string.IsNullOrWhiteSpace(submission.InstaPayReference))
            {
                invoice.InstaPayReference = string.IsNullOrWhiteSpace(invoice.InstaPayReference)
                    ? submission.InstaPayReference
                    : $"{invoice.InstaPayReference} / {submission.InstaPayReference}";
            }

            if (invoice.RemainingAmount <= 0m && invoice.IsPartialCredit)
                invoice.Status = SalesInvoiceStatus.Completed;

            invoice.NotifyFinancialsChanged();

            var parts = new List<string>(3);
            if (submission.CashAmount > 0m) parts.Add($"كاش {PosFormatting.FormatCurrency(submission.CashAmount)}");
            if (submission.VodafoneAmount > 0m) parts.Add($"فودافون {PosFormatting.FormatCurrency(submission.VodafoneAmount)}");
            if (submission.InstaPayAmount > 0m) parts.Add($"إنستاباي {PosFormatting.FormatCurrency(submission.InstaPayAmount)}");
            var extraNote = string.IsNullOrWhiteSpace(submission.Note) ? "" : $" — {submission.Note}";

            invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "تحصيل دفعة من ملف العميل",
                Details = $"{string.Join(" + ", parts)}{extraNote} | المتبقي: {invoice.RemainingAmountDisplay}.",
                ActorName = currentUserName,
                Amount = paidNow
            });

            var keepCustomerId = SelectedCustomerEntry?.Customer.Id;
            RebuildEntriesAndRefresh(selectCustomerId: keepCustomerId);
            notifyExternalFinancialDataChanged?.Invoke();
            SetStatus($"تم تحصيل {PosFormatting.FormatCurrency(paidNow)} على الفاتورة {invoice.InvoiceNumber} وتحديث رصيد العميل.", false);
        });

        OpenCollectSalesPaymentRequested?.Invoke(vm);
    }

    private void OpenSalesPrintPreview(SalesInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;

        var vm = new InvoicePrintPreviewViewModel(invoice, modeLabel =>
        {
            invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "معاينة / طباعة الفاتورة",
                Details = $"طباعة ({modeLabel}) من ملف العميل.",
                ActorName = currentUserName
            });
            SetStatus($"تمت معاينة طباعة الفاتورة {invoice.InvoiceNumber} ({modeLabel}).", false);
        });

        OpenSalesPrintPreviewRequested?.Invoke(vm);
    }

    private void OpenMaintenanceDetails(MaintenanceOrderDesignItem? order)
    {
        if (order is null) return;

        var detailsVm = new MaintenanceInvoiceDetailsViewModel(order, currentUserName)
        {
            OpenDeliveryDialogRequested = vm => OpenMaintenanceDeliveryRequested?.Invoke(vm),
            OpenReceiptPreviewRequested = vm => OpenMaintenanceReceiptPreviewRequested?.Invoke(vm),
            OpenInActionsRequested = target => openOrderInMaintenanceActions?.Invoke(target),
            NotifyOrdersChanged = () =>
            {
                var keepId = SelectedCustomerEntry?.Customer.Id;
                RebuildEntriesAndRefresh(selectCustomerId: keepId);
                notifyExternalFinancialDataChanged?.Invoke();
            }
        };

        OpenMaintenanceDetailsRequested?.Invoke(detailsVm);
        RebuildEntriesAndRefresh(selectCustomerId: SelectedCustomerEntry?.Customer.Id);
    }

    private void OpenMaintenanceDelivery(MaintenanceOrderDesignItem? order)
    {
        if (order is null) return;

        if (order.IsCancelled)
        {
            SetStatus($"التذكرة {order.OrderNumber} ملغاة ولا يمكن تحصيلها.", true);
            return;
        }

        if (order.IsDelivered)
        {
            OpenMaintenanceDetails(order);
            return;
        }

        var vm = new MaintenanceDeliveryViewModel(order, currentUserName);
        var result = OpenMaintenanceDeliveryRequested?.Invoke(vm);
        if (result == true || vm.IsConfirmed)
        {
            var keepId = SelectedCustomerEntry?.Customer.Id;
            RebuildEntriesAndRefresh(selectCustomerId: keepId);
            notifyExternalFinancialDataChanged?.Invoke();
            SetStatus($"تم تسليم جهاز الصيانة {order.OrderNumber} وتحديث رصيد العميل.", false);

            if (vm.ShouldOpenPrintAfterClose)
            {
                var printVm = new MaintenanceReceiptPreviewViewModel(
                    order,
                    modeLabel => SetStatus($"تمت طباعة {modeLabel} للتذكرة {order.OrderNumber}.", false),
                    MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt);
                OpenMaintenanceReceiptPreviewRequested?.Invoke(printVm);
            }
        }
    }

    private void RebuildEntriesAndRefresh(int? selectCustomerId = null)
    {
        var previousSelectedId = selectCustomerId ?? SelectedCustomerEntry?.Customer.Id;

        var previousEntries = allEntries.ToList();
        allEntries.Clear();

        foreach (var customer in sharedCustomers)
        {
            var entry = previousEntries.FirstOrDefault(e => ReferenceEquals(e.Customer, customer))
                ?? new CustomerDirectoryEntryViewModel(customer);

            var salesMatches = customer.IsWalkIn
                ? sharedSalesInvoices.Where(i => i.IsWalkInCustomer || i.CustomerId == customer.Id)
                : sharedSalesInvoices.Where(i => !i.IsWalkInCustomer && i.CustomerId == customer.Id);

            var maintMatches = customer.IsWalkIn
                ? sharedMaintenanceOrders.Where(o => o.IsWalkInCustomer || o.CustomerId == customer.Id)
                : sharedMaintenanceOrders.Where(o => !o.IsWalkInCustomer && o.CustomerId == customer.Id);

            entry.Recalculate(salesMatches, maintMatches);
            allEntries.Add(entry);
        }

        NotifyKpis();
        RefreshFilteredCustomers(previousSelectedId);
    }

    private void RefreshFilteredCustomers(int? preferredSelectId = null)
    {
        var targetId = preferredSelectId ?? SelectedCustomerEntry?.Customer.Id;
        var query = SearchText.Trim();

        IEnumerable<CustomerDirectoryEntryViewModel> filtered = allEntries;

        if (SelectedTierFilter != "كل الفئات")
        {
            filtered = SelectedTierFilter switch
            {
                "سعر مميز" => filtered.Where(e => e.Customer.DefaultPriceTier == PriceTier.Special),
                "سعر جملة" => filtered.Where(e => e.Customer.DefaultPriceTier == PriceTier.Wholesale),
                _ => filtered.Where(e => e.Customer.DefaultPriceTier == PriceTier.Retail)
            };
        }

        filtered = SelectedBalanceFilter switch
        {
            "لديهم مستحقات متبقية" => filtered.Where(e => e.HasRemaining),
            "مسددون / بدون مديونية" => filtered.Where(e => e.HasTransactions && !e.HasRemaining),
            "لديهم فواتير مبيعات" => filtered.Where(e => e.HasSalesInvoices),
            "لديهم تذاكر صيانة" => filtered.Where(e => e.HasMaintenanceOrders),
            "بدون معاملات بعد" => filtered.Where(e => !e.HasTransactions),
            _ => filtered
        };

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(e =>
                e.Customer.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Customer.Phone.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Customer.SecondaryPhone.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Customer.Address.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Customer.CustomerCodeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Customer.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.SalesInvoices.Any(i => i.InvoiceNumber.Contains(query, StringComparison.OrdinalIgnoreCase))
                || e.MaintenanceOrders.Any(o =>
                    o.OrderNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || o.DeviceFullTitle.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        filtered = SelectedSortOption switch
        {
            "الأعلى تعاملًا" => filtered.OrderByDescending(e => e.TotalBusinessValue).ThenBy(e => e.Customer.Name),
            "الأعلى مديونية" => filtered.OrderByDescending(e => e.TotalRemaining).ThenByDescending(e => e.TotalBusinessValue),
            "الأكثر معاملات" => filtered.OrderByDescending(e => e.TotalTransactionsCount).ThenByDescending(e => e.TotalBusinessValue),
            "الاسم أبجديًا" => filtered.OrderBy(e => e.Customer.IsWalkIn ? 1 : 0).ThenBy(e => e.Customer.Name, StringComparer.CurrentCulture),
            _ => filtered
                .OrderBy(e => e.Customer.IsWalkIn ? 1 : 0)
                .ThenByDescending(e => e.LastActivityAt ?? DateTime.MinValue)
                .ThenByDescending(e => e.Customer.Id)
        };

        var orderedList = filtered.ToList();
        FilteredCustomers.Clear();
        foreach (var item in orderedList)
            FilteredCustomers.Add(item);

        var matchedSelected = targetId.HasValue
            ? FilteredCustomers.FirstOrDefault(e => e.Customer.Id == targetId.Value)
            : null;
        SelectedCustomerEntry = matchedSelected ?? FilteredCustomers.FirstOrDefault();

        Changed(nameof(HasNoFilteredCustomers));
    }

    private void RefreshSelectedCustomerTransactions()
    {
        SelectedCustomerSalesInvoices.Clear();
        SelectedCustomerMaintenanceOrders.Clear();

        if (SelectedCustomerEntry is not null)
        {
            foreach (var inv in SelectedCustomerEntry.SalesInvoices)
                SelectedCustomerSalesInvoices.Add(inv);

            foreach (var ord in SelectedCustomerEntry.MaintenanceOrders)
                SelectedCustomerMaintenanceOrders.Add(ord);
        }

        Changed(nameof(SelectedSalesTabTitle));
        Changed(nameof(SelectedMaintenanceTabTitle));
        Changed(nameof(HasNoSelectedCustomerSales));
        Changed(nameof(HasNoSelectedCustomerMaintenance));
    }

    private void NotifyKpis()
    {
        Changed(nameof(TotalCustomersCountDisplay));
        Changed(nameof(TierBreakdownSubtext));
        Changed(nameof(TotalBusinessValueDisplay));
        Changed(nameof(BusinessBreakdownSubtext));
        Changed(nameof(TotalCollectedAllDisplay));
        Changed(nameof(TransactionsCountSubtext));
        Changed(nameof(TotalRemainingAllDisplay));
        Changed(nameof(DebtorsCountSubtext));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
