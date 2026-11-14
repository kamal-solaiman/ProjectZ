using System.Collections.ObjectModel;
using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Data;

namespace ProjectZ.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly UserSession user;
    private readonly SalesRepository? salesRepository;
    private readonly PurchaseRepository? purchaseRepository;
    private readonly CategoryRepository? categoryRepository;
    private readonly UserRepository? userRepository;
    private readonly MaintenanceRepository? maintenanceRepository;
    private readonly BackupRepository? backupRepository;
    private readonly ObservableCollection<PosCustomerDesignItem> sharedCustomers;
    private NavigationMenuItemViewModel? selectedItem;
    private bool isSidebarCollapsed;

    public IReadOnlyList<NavigationMenuCategoryViewModel> Categories { get; }
    public PosViewModel Pos { get; }
    public AddProductViewModel AddProduct { get; }
    public InventoryViewModel Inventory { get; }
    public CategoriesViewModel ProductCategories { get; }
    public InvoicesViewModel Invoices { get; }
    public ReturnExchangeViewModel ReturnExchange { get; }
    public PurchaseEntryViewModel PurchaseEntry { get; }
    public PurchaseInvoicesViewModel PurchaseInvoices { get; }
    public MaintenanceIntakeViewModel MaintenanceIntake { get; }
    public MaintenanceInvoicesViewModel MaintenanceInvoices { get; }
    // Assigned during construction: the spare-parts screen can notify actions before that line runs.
    public MaintenanceActionsViewModel MaintenanceActions { get; } = null!;
    public MaintenanceSparePartsViewModel MaintenanceSpareParts { get; }
    public CustomersViewModel Customers { get; }
    public SuppliersViewModel Suppliers { get; }
    public UsersViewModel Users { get; }
    public BackupViewModel Backup { get; }
    public MyAccountsViewModel MyAccounts { get; }
    public VodafoneTransferViewModel VodafoneTransfer { get; }
    public InstapayTransferViewModel InstapayTransfer { get; }
    public VodafoneRecordsViewModel VodafoneRecords { get; }
    public InstapayRecordsViewModel InstapayRecords { get; }
    public string Welcome => $"مرحبًا، {user.DisplayName}";
    public string Username => user.Username;
    public bool IsPosSelected => ReferenceEquals(selectedItem, Categories[0].Items[0]);
    public bool IsAddProductSelected => ReferenceEquals(selectedItem, Categories[0].Items[1]);
    public bool IsInventorySelected => ReferenceEquals(selectedItem, Categories[0].Items[2]);
    public bool IsCategoriesSelected => ReferenceEquals(selectedItem, Categories[0].Items[3]);
    public bool IsInvoicesSelected => ReferenceEquals(selectedItem, Categories[0].Items[4]);
    public bool IsReturnExchangeSelected => ReferenceEquals(selectedItem, Categories[0].Items[5]);
    public bool IsPurchaseEntrySelected => ReferenceEquals(selectedItem, Categories[1].Items[0]);
    public bool IsPurchaseInvoicesSelected => ReferenceEquals(selectedItem, Categories[1].Items[1]);
    public bool IsMaintenanceIntakeSelected => ReferenceEquals(selectedItem, Categories[2].Items[0]);
    public bool IsMaintenanceInvoicesSelected => ReferenceEquals(selectedItem, Categories[2].Items[1]);
    public bool IsMaintenanceActionsSelected => ReferenceEquals(selectedItem, Categories[2].Items[2]);
    public bool IsMaintenanceSparePartsSelected => ReferenceEquals(selectedItem, Categories[2].Items[3]);
    public bool IsCustomersSelected => ReferenceEquals(selectedItem, Categories[4].Items[0]);
    public bool IsSuppliersSelected => ReferenceEquals(selectedItem, Categories[4].Items[1]);
    public bool IsUsersSelected => ReferenceEquals(selectedItem, Categories[5].Items[0]);
    public bool IsMyAccountsSelected => ReferenceEquals(selectedItem, Categories[3].Items[0]);
    public bool IsVodafoneTransferSelected => ReferenceEquals(selectedItem, Categories[3].Items[1]);
    public bool IsInstapayTransferSelected => ReferenceEquals(selectedItem, Categories[3].Items[2]);
    public bool IsVodafoneRecordsSelected => ReferenceEquals(selectedItem, Categories[3].Items[3]);
    public bool IsInstapayRecordsSelected => ReferenceEquals(selectedItem, Categories[3].Items[4]);
    public bool IsBackupSelected => ReferenceEquals(selectedItem, Categories[6].Items[2]);
    public string SelectedCategoryTitle => selectedItem?.Category.Title ?? "مساحة العمل";
    public string SelectedPageTitle => selectedItem?.Title ?? "مرحبًا بك في ProjectZ";
    public string SelectedPageDescription => selectedItem is null
        ? "اختر إحدى الوظائف من القائمة الجانبية."
        : "هذا العنصر للتحديد فقط حاليًا؛ لم تُنفذ شاشته بعد.";
    public RelayCommand LogoutCommand { get; }

    // Collapsed = narrow icon rail anchored to the right edge; expanded = full menu.
    public bool IsSidebarCollapsed
    {
        get => isSidebarCollapsed;
        private set
        {
            if (isSidebarCollapsed == value) return;

            isSidebarCollapsed = value;
            Changed();
        }
    }
    public RelayCommand ToggleSidebarCommand { get; }

    public MainViewModel(
        UserSession user,
        Action logout,
        InventoryRepository? inventoryRepository = null,
        SalesRepository? salesRepository = null,
        CategoryRepository? categoryRepository = null,
        PurchaseRepository? purchaseRepository = null,
        FinancialRepository? financialRepository = null,
        MaintenanceRepository? maintenanceRepository = null,
        UserRepository? userRepository = null,
        BackupRepository? backupRepository = null)
    {
        this.user = user;
        this.salesRepository = salesRepository;
        this.categoryRepository = categoryRepository;
        this.purchaseRepository = purchaseRepository;
        this.maintenanceRepository = maintenanceRepository;
        this.userRepository = userRepository;
        this.backupRepository = backupRepository;
        LogoutCommand = new RelayCommand(logout);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        Categories = new[]
        {
            new NavigationMenuCategoryViewModel("المبيعات", "\U0001F6D2",
                "نقطة البيع", "اضافة منتج", "المخزون", "التصنيفات", "الفواتير", "المرتجع والاستبدال"),
            new NavigationMenuCategoryViewModel("المشتريات", "\U0001F4E6",
                "شراء فاتورة", "فواتير المشتريات"),
            new NavigationMenuCategoryViewModel("الصيانة", "\U0001F527",
                "امر صيانة", "فواتير الصيانة", "اجراءات الصيانة", "قطع الغيار"),
            new NavigationMenuCategoryViewModel("المالية", "\U0001F4B0",
                "حساباتي", "تحويل فودافون كاش", "تحويل انستاباي", "سجلات فودافون كاش", "سجلات انستاباي"),
            new NavigationMenuCategoryViewModel("العملاء والموردين", "\U0001F465",
                "العملاء", "الموردين"),
            new NavigationMenuCategoryViewModel("المستخدمين", "\U0001F464",
                "المستخدمين", "الصلاحيات"),
            new NavigationMenuCategoryViewModel("الاعدادات", "\u2699",
                "اعدادات عامة", "المظهر", "النسخ الاحتياطي")
        };

        var sharedCatalog = inventoryRepository is null
            ? PosDesignSeedData.CreateSharedSampleCatalog()
            : new ObservableCollection<PosProductDesignItem>();
        var sharedCategories = inventoryRepository is null
            ? PosDesignSeedData.CreateSharedCategories()
            : new ObservableCollection<string>();
        // With SQL the directory comes from dbo.Customers; design mode keeps the sample customers.
        sharedCustomers = salesRepository is null
            ? new ObservableCollection<PosCustomerDesignItem>(PosDesignSeedData.CreateSampleCustomers())
            : new ObservableCollection<PosCustomerDesignItem>();
        var sharedRegistrations = inventoryRepository is null
            ? InventoryDesignSeedData.CreateSharedSampleRegistrations(sharedCatalog)
            : new ObservableCollection<InventoryUnitRegistrationItem>();
        // With SQL these collections start empty and are filled by SalesRepository.
        var sharedSalesInvoices = salesRepository is null
            ? InvoiceDesignSeedData.CreateSharedSampleInvoices(sharedCatalog, sharedRegistrations)
            : new ObservableCollection<SalesInvoiceDesignItem>();
        var sharedReturnOperations = ReturnExchangeDesignSeedData.CreateSharedSampleOperations(sharedSalesInvoices);
        // With SQL the supplier directory and the purchase ledger come from dbo.Suppliers / dbo.PurchaseInvoices.
        var sharedSuppliers = purchaseRepository is null
            ? PurchaseDesignSeedData.CreateSharedSampleSuppliers()
            : new ObservableCollection<SupplierDesignItem>();
        var sharedPurchaseInvoices = purchaseRepository is null
            ? PurchaseDesignSeedData.CreateSharedSamplePurchaseInvoices(sharedCatalog, sharedRegistrations, sharedSuppliers)
            : new ObservableCollection<PurchaseInvoiceDesignItem>();
        // With SQL the maintenance screens are a separate future task: no sample orders, so the
        // customer-screen maintenance tab shows an explicit "not in the database yet" message.
        var sharedMaintenanceOrders = salesRepository is null
            ? MaintenanceDesignSeedData.CreateSharedSampleOrders(sharedCustomers)
            : new ObservableCollection<MaintenanceOrderDesignItem>();
        var sharedMaintenanceSpareParts = maintenanceRepository is null
            ? MaintenanceDesignSeedData.CreateSharedSampleSpareParts()
            : new ObservableCollection<MaintenanceSparePartItem>();

        Inventory = new InventoryViewModel(
            sharedCatalog,
            sharedRegistrations,
            () => SelectItem(Categories[0].Items[1]),
            sharedCategories,
            inventoryRepository,
            categoryRepository);
        Inventory.InventoryChanged += OnInventoryChanged;
        Invoices = new InvoicesViewModel(
            sharedSalesInvoices,
            sharedCatalog,
            sharedRegistrations,
            user.DisplayName,
            () => SelectItem(Categories[0].Items[0]),
            ReloadAfterSale,
            sharedReturnOperations,
            null,
            salesRepository);
        ReturnExchange = new ReturnExchangeViewModel(
            sharedSalesInvoices,
            sharedCatalog,
            sharedRegistrations,
            sharedReturnOperations,
            user.DisplayName,
            () => SelectItem(Categories[0].Items[4]),
            Inventory.NotifyExternalInventoryChanged,
            Invoices.NotifyExternalInvoicesChanged,
            salesRepository);
        Invoices.SetReturnExchangeNotifier(ReturnExchange.NotifyExternalDataChanged);
        // Invoice writes (sale / return / cancel) move stock in SQL; the catalog and units are re-read from the same rows.
        Invoices.StockReloadRequested = ReloadInventoryOnly;
        PurchaseEntry = new PurchaseEntryViewModel(
            user.DisplayName,
            sharedCatalog,
            sharedSuppliers,
            sharedRegistrations,
            sharedPurchaseInvoices,
            sharedCategories,
            () => SelectItem(Categories[0].Items[1]),
            Inventory.NotifyExternalInventoryChanged,
            ReturnExchange.NotifyExternalDataChanged,
            purchaseRepository,
            SuppliesChanged);
        PurchaseInvoices = new PurchaseInvoicesViewModel(
            sharedPurchaseInvoices,
            sharedSuppliers,
            sharedCatalog,
            sharedRegistrations,
            user.DisplayName,
            () => SelectItem(Categories[1].Items[0]),
            Inventory.NotifyExternalInventoryChanged,
            ReturnExchange.NotifyExternalDataChanged,
            PurchaseEntry.NotifyExternalCatalogChanged,
            purchaseRepository);
        MaintenanceIntake = new MaintenanceIntakeViewModel(
            user.DisplayName,
            sharedCustomers,
            sharedMaintenanceOrders,
            maintenanceRepository,
            ReloadMaintenanceData,
            CreateCustomerFromPos,
            ValidateCustomerPhone,
            ReloadCustomerDirectory);
        // Spare parts and actions notify each other, so the spare-parts screen gets a late-bound callback
        // instead of dereferencing MaintenanceActions before it has been assigned.
        MaintenanceSpareParts = new MaintenanceSparePartsViewModel(
            sharedMaintenanceSpareParts,
            () => SelectItem(Categories[2].Items[2]),
            NotifyMaintenanceActionsChanged,
            maintenanceRepository);
        MaintenanceActions = new MaintenanceActionsViewModel(
            user.DisplayName,
            sharedMaintenanceOrders,
            sharedMaintenanceSpareParts,
            () => SelectItem(Categories[2].Items[0]),
            MaintenanceSpareParts.NotifyExternalDataChanged,
            maintenanceRepository,
            ReloadMaintenanceData);
        MaintenanceInvoices = new MaintenanceInvoicesViewModel(
            user.DisplayName,
            sharedMaintenanceOrders,
            () => SelectItem(Categories[2].Items[0]),
            order =>
            {
                MaintenanceActions.SelectOrderFromExternal(order);
                SelectItem(Categories[2].Items[2]);
            },
            ReloadMaintenanceData,
            maintenanceRepository,
            ReloadMaintenanceData);
        Pos = new PosViewModel(
            user.DisplayName,
            sharedCatalog,
            posInvoice => Invoices.RecordCompletedPosSale(posInvoice).InvoiceNumber,
            sharedCustomers,
            useDesignSamples: inventoryRepository is null,
            salesRepository,
            ReloadPosData,
            CreateCustomerFromPos);
        // Created first: the add-product dialog saves new categories through this screen's repository.
        ProductCategories = new CategoriesViewModel(
            sharedCategories,
            () => SelectItem(Categories[0].Items[1]),
            categoryRepository,
            user.DisplayName);
        ProductCategories.CategoriesChanged = OnCategoriesChanged;
        AddProduct = new AddProductViewModel(
            sharedCatalog,
            () => SelectItem(Categories[0].Items[0]),
            Inventory.RegisterInitialStockForNewProduct,
            sharedCategories,
            () => Inventory.StatusMessage,
            inventoryRepository,
            Inventory.RefreshFromDatabase,
            name => ProductCategories.EnsureCategory(name));
        MyAccounts = new MyAccountsViewModel(user.DisplayName, financialRepository);
        VodafoneTransfer = new VodafoneTransferViewModel(MyAccounts, user.DisplayName);
        InstapayTransfer = new InstapayTransferViewModel(MyAccounts, user.DisplayName);
        VodafoneRecords = new VodafoneRecordsViewModel(VodafoneTransfer, MyAccounts, user.DisplayName);
        InstapayRecords = new InstapayRecordsViewModel(InstapayTransfer, MyAccounts, user.DisplayName);
        Suppliers = new SuppliersViewModel(
            sharedSuppliers,
            sharedPurchaseInvoices,
            PurchaseInvoices,
            supplier =>
            {
                PurchaseEntry.StartNewPurchaseForSupplier(supplier);
                SelectItem(Categories[1].Items[0]);
            },
            () =>
            {
                SuppliesChanged();
            },
            purchaseRepository);
        Customers = new CustomersViewModel(
            user.DisplayName,
            sharedCustomers,
            sharedSalesInvoices,
            sharedMaintenanceOrders,
            customer =>
            {
                Pos.StartNewInvoiceForCustomer(customer);
                SelectItem(Categories[0].Items[0]);
            },
            customer =>
            {
                MaintenanceIntake.StartNewIntakeForCustomer(customer);
                SelectItem(Categories[2].Items[0]);
            },
            order =>
            {
                MaintenanceActions.SelectOrderFromExternal(order);
                SelectItem(Categories[2].Items[2]);
            },
            () =>
            {
                Invoices.NotifyExternalInvoicesChanged();
                ReturnExchange.NotifyExternalDataChanged();
                MaintenanceIntake.NotifyExternalDataChanged();
                MaintenanceInvoices.NotifyExternalDataChanged();
                MaintenanceActions.NotifyExternalDataChanged();
            },
            salesRepository);

        Users = new UsersViewModel(user.DisplayName, userRepository, user.Id, user.Username);
        Backup = new BackupViewModel(user.DisplayName, backupRepository);

        Inventory.RefreshFromDatabase();
        AddProduct.NotifyInventoryLoaded();

        if (maintenanceRepository is not null)
        {
            // MaintenanceIntake is the source for the shared orders collection; loading it here
            // makes the orders visible to the actions and invoices screens immediately.
            try
            {
                MaintenanceIntake.NotifyExternalDataChanged();
            }
            catch (MaintenanceDataException)
            {
                // Each maintenance screen reports the failure on its own status strip and retries on refresh.
            }
            MaintenanceActions.NotifyExternalDataChanged();
            MaintenanceInvoices.NotifyExternalDataChanged();
            MaintenanceSpareParts.NotifyExternalDataChanged();
        }

        if (purchaseRepository is not null)
        {
            // The purchase screens and the supplier directory both read these two collections.
            try
            {
                purchaseRepository.LoadSuppliersInto(sharedSuppliers);
                purchaseRepository.LoadInvoicesInto(sharedPurchaseInvoices);
            }
            catch (PurchaseDataException)
            {
                // Each screen reports the failure on its own status strip and retries on refresh.
            }

            PurchaseEntry.RefreshSuppliersFromDatabase();
            PurchaseInvoices.RefreshFromDatabase();
            Suppliers.RefreshFromDatabase();
        }

        if (categoryRepository is not null)
            ProductCategories.RefreshFromDatabase();

        // Login accounts live in dbo.Users; the screen loads them on construction too.
        if (userRepository is not null)
            Users.RefreshFromDatabase();

        if (salesRepository is not null)
        {
            // Customer directory and the saved sales invoices both live in SQL Server.
            try { salesRepository.LoadCustomersInto(sharedCustomers); }
            catch (SalesDataException) { /* The POS status strip explains the failure on first sale. */ }

            Invoices.RefreshInvoicesFromDatabase();
            Customers.RefreshFromDatabase();
        }

        // Start at the first available destination; all other categories stay closed.
        SelectItem(Categories[0].Items[0]);
    }

    public bool IsImplementedScreenSelected =>
        IsPosSelected || IsAddProductSelected || IsInventorySelected || IsCategoriesSelected
        || IsInvoicesSelected || IsReturnExchangeSelected || IsPurchaseEntrySelected || IsPurchaseInvoicesSelected
        || IsMaintenanceIntakeSelected || IsMaintenanceInvoicesSelected || IsMaintenanceActionsSelected
        || IsMaintenanceSparePartsSelected || IsCustomersSelected || IsSuppliersSelected || IsMyAccountsSelected
        || IsVodafoneTransferSelected || IsInstapayTransferSelected || IsVodafoneRecordsSelected || IsInstapayRecordsSelected
        || IsUsersSelected || IsBackupSelected;

    /// <summary>
    /// A category was added, renamed or deleted in SQL. The combo box list was already updated in place;
    /// this keeps the inventory and add-product screens consistent with the same rows.
    /// </summary>
    /// <summary>
    /// Inventory wrote stock to SQL (supply, edit, damage, restore, delete...). POS, add-product and
    /// the invoice screens hold the same catalog instances, so they only need a refresh; maintenance
    /// and suppliers reload their own lists.
    /// </summary>
    private void OnInventoryChanged()
    {
        Pos.NotifyExternalInventoryChanged();
        AddProduct.NotifyInventoryLoaded();
        Invoices.NotifyExternalInvoicesChanged();
        Suppliers.NotifyExternalDataChanged();
    }

    private void OnCategoriesChanged()
    {
        // A rename changes the category name shown on every product row, so the catalog itself is reloaded.
        Inventory.RefreshFromDatabase();
        AddProduct.NotifyInventoryLoaded();
    }

    /// <summary>
    /// A supplier was added, renamed or archived in SQL. The purchase entry screen reloaded the shared
    /// supplier collection already, so the ledger and the directory only need to re-read their rows.
    /// </summary>
    private void SuppliesChanged()
    {
        PurchaseInvoices.RefreshFromDatabase();
        Suppliers.RefreshFromDatabase();
    }

    /// <summary>
    /// A customer was added, renamed or archived in SQL. The customer screen reloaded the shared
    /// customer collection already, so the other screens only need to re-read their rows.
    /// </summary>
    private void CustomersChanged()
    {
        Customers.RefreshFromDatabase();
    }

    /// <summary>Late-bound notifier: the spare-parts screen may call it before MaintenanceActions exists.</summary>
    private void NotifyMaintenanceActionsChanged()
        => MaintenanceActions?.NotifyExternalDataChanged();

    /// <summary>Re-reads the maintenance data from the database and refreshes every maintenance screen.</summary>
    private void ReloadMaintenanceData()
    {
        MaintenanceIntake.NotifyExternalDataChanged();
        MaintenanceInvoices.NotifyExternalDataChanged();
        MaintenanceActions.NotifyExternalDataChanged();
        MaintenanceSpareParts.NotifyExternalDataChanged();
    }

    /// <summary>Reloads the SQL catalog, units, categories and customers after a sale or a return.</summary>
    private bool ReloadPosData()
    {
        var loaded = Inventory.RefreshFromDatabase();
        if (!loaded) return false;

        if (salesRepository is not null)
        {
            try
            {
                salesRepository.LoadCustomersInto(sharedCustomers);
            }
            catch (SalesDataException)
            {
                return false;
            }
        }

        Pos.NotifyExternalInventoryChanged();
        AddProduct.NotifyInventoryLoaded();
        return true;
    }

    /// <summary>Re-reads products and units from SQL and pushes them to every screen that shows stock.</summary>
    private void ReloadInventoryOnly()
    {
        Inventory.RefreshFromDatabase();
        Pos.NotifyExternalInventoryChanged();
        AddProduct.NotifyInventoryLoaded();
    }

    /// <summary>Called by the invoice screen after it changed stock or money in SQL.</summary>
    private void ReloadAfterSale()
    {
        var loaded = Inventory.RefreshFromDatabase();
        if (loaded && salesRepository is not null)
        {
            try { salesRepository.LoadCustomersInto(sharedCustomers); }
            catch (SalesDataException) { /* Keep the loaded list; the status strip reports the failure. */ }
        }

        Pos.NotifyExternalInventoryChanged();
    }

    /// <summary>
    /// Duplicate-phone check used by the «عميل جديد» dialogs opened from the POS and intake screens.
    /// It asks SQL Server, so a customer created on another client is caught before the insert.
    /// </summary>
    private string? ValidateCustomerPhone(string phone, int? editingCustomerId)
        => salesRepository is not null && salesRepository.PhoneExists(phone, editingCustomerId)
            ? "رقم الهاتف مسجل بالفعل لعميل آخر. ابحث عن العميل الحالي أو أدخل رقمًا مختلفًا."
            : null;

    /// <summary>Re-reads dbo.Customers into the directory shared by POS, customers and maintenance intake.</summary>
    private void ReloadCustomerDirectory()
    {
        if (salesRepository is null) return;
        salesRepository.LoadCustomersInto(sharedCustomers);
    }

    /// <summary>Persists a customer created from the POS dialog; the POS view model appends it to the directory.</summary>
    private PosCustomerDesignItem CreateCustomerFromPos(PosCustomerDesignItem customer)
    {
        if (salesRepository is null) return customer;

        var saved = salesRepository.CreateCustomer(
            customer.Name,
            customer.Phone,
            customer.SecondaryPhone,
            customer.Address,
            customer.Notes,
            customer.DefaultPriceTier);

        CustomersChanged();
        return saved;
    }

    public void ToggleCategory(NavigationMenuCategoryViewModel category)
    {
        if (!Categories.Contains(category)) return;

        var expand = !category.IsExpanded;
        // A category header only controls disclosure; navigation changes on item selection.
        foreach (var itemCategory in Categories)
            itemCategory.SetExpanded(expand && ReferenceEquals(itemCategory, category));
    }

    // From the collapsed rail a category icon expands the sidebar and opens that category.
    public void OpenCategoryFromRail(NavigationMenuCategoryViewModel category)
    {
        if (!Categories.Contains(category)) return;

        IsSidebarCollapsed = false;
        foreach (var other in Categories)
            other.SetExpanded(ReferenceEquals(other, category));
    }

    public void SelectItem(NavigationMenuItemViewModel item)
    {
        if (!Categories.Contains(item.Category) || !item.Category.Items.Contains(item)) return;

        foreach (var category in Categories)
        {
            category.SetExpanded(ReferenceEquals(category, item.Category));
            foreach (var categoryItem in category.Items)
                categoryItem.SetSelected(ReferenceEquals(categoryItem, item));
        }

        selectedItem = item;
        if (ReferenceEquals(item, Categories[3].Items[1]))
            VodafoneTransfer.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[3].Items[2]))
            InstapayTransfer.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[3].Items[3]))
            VodafoneRecords.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[3].Items[4]))
            InstapayRecords.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[0].Items[5]))
        {
            if (!ReturnExchange.RefreshFromDatabase())
                ReturnExchange.NotifyExternalDataChanged();
        }
        else if (ReferenceEquals(item, Categories[0].Items[4]))
        {
            if (!Invoices.RefreshInvoicesFromDatabase())
                Invoices.NotifyExternalInvoicesChanged();
        }
        else if (ReferenceEquals(item, Categories[0].Items[1]))
            AddProduct.RefreshFromDatabase();
        else if (ReferenceEquals(item, Categories[0].Items[2]))
        {
            if (categoryRepository is not null)
                ProductCategories.RefreshFromDatabase();
            Inventory.NotifyExternalInventoryChanged();
        }
        else if (ReferenceEquals(item, Categories[1].Items[0]))
        {
            PurchaseEntry.RefreshSuppliersFromDatabase();
            PurchaseEntry.NotifyExternalCatalogChanged();
        }
        else if (ReferenceEquals(item, Categories[1].Items[1]))
            PurchaseInvoices.RefreshFromDatabase();
        else if (ReferenceEquals(item, Categories[2].Items[0]))
            MaintenanceIntake.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[2].Items[1]))
            MaintenanceInvoices.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[2].Items[2]))
            MaintenanceActions.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[2].Items[3]))
            MaintenanceSpareParts.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[4].Items[0]))
            Customers.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[4].Items[1]))
            Suppliers.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[5].Items[0]))
            Users.RefreshFromDatabase();
        else if (ReferenceEquals(item, Categories[6].Items[2]))
            Backup.RefreshFromDatabase();

        Changed(nameof(IsPosSelected));
        Changed(nameof(IsAddProductSelected));
        Changed(nameof(IsInventorySelected));
        Changed(nameof(IsCategoriesSelected));
        Changed(nameof(IsInvoicesSelected));
        Changed(nameof(IsReturnExchangeSelected));
        Changed(nameof(IsPurchaseEntrySelected));
        Changed(nameof(IsPurchaseInvoicesSelected));
        Changed(nameof(IsMaintenanceIntakeSelected));
        Changed(nameof(IsMaintenanceInvoicesSelected));
        Changed(nameof(IsMaintenanceActionsSelected));
        Changed(nameof(IsMaintenanceSparePartsSelected));
        Changed(nameof(IsCustomersSelected));
        Changed(nameof(IsSuppliersSelected));
        Changed(nameof(IsUsersSelected));
        Changed(nameof(IsMyAccountsSelected));
        Changed(nameof(IsVodafoneTransferSelected));
        Changed(nameof(IsInstapayTransferSelected));
        Changed(nameof(IsVodafoneRecordsSelected));
        Changed(nameof(IsInstapayRecordsSelected));
        Changed(nameof(IsBackupSelected));
        Changed(nameof(IsImplementedScreenSelected));
        Changed(nameof(SelectedCategoryTitle));
        Changed(nameof(SelectedPageTitle));
        Changed(nameof(SelectedPageDescription));
    }
}

public sealed class NavigationMenuCategoryViewModel : ObservableObject
{
    private bool isExpanded;

    public string Title { get; }
    public string Icon { get; }
    public IReadOnlyList<NavigationMenuItemViewModel> Items { get; }
    public bool IsExpanded => isExpanded;
    // Physical glyphs (the chevron TextBlock is LTR): closed arrow points left, toward the RTL content.
    public string Chevron => IsExpanded ? "⌄" : "‹";

    public NavigationMenuCategoryViewModel(string title, string icon, params string[] itemTitles)
    {
        Title = title;
        Icon = icon;
        Items = itemTitles.Select(itemTitle => new NavigationMenuItemViewModel(this, itemTitle)).ToArray();
    }

    internal void SetExpanded(bool value)
    {
        if (isExpanded == value) return;

        isExpanded = value;
        Changed(nameof(IsExpanded));
        Changed(nameof(Chevron));
    }
}

public sealed class NavigationMenuItemViewModel : ObservableObject
{
    private bool isSelected;

    public NavigationMenuCategoryViewModel Category { get; }
    public string Title { get; }
    public bool IsSelected => isSelected;

    internal NavigationMenuItemViewModel(NavigationMenuCategoryViewModel category, string title)
    {
        Category = category;
        Title = title;
    }

    internal void SetSelected(bool value)
    {
        if (isSelected == value) return;

        isSelected = value;
        Changed(nameof(IsSelected));
    }
}
