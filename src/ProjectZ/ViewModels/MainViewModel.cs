using System.Collections.ObjectModel;
using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Data;

namespace ProjectZ.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly UserSession user;
    private readonly SalesRepository? salesRepository;
    private readonly CategoryRepository? categoryRepository;
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
    public bool IsMyAccountsSelected => ReferenceEquals(selectedItem, Categories[3].Items[0]);
    public bool IsVodafoneTransferSelected => ReferenceEquals(selectedItem, Categories[3].Items[1]);
    public bool IsInstapayTransferSelected => ReferenceEquals(selectedItem, Categories[3].Items[2]);
    public bool IsVodafoneRecordsSelected => ReferenceEquals(selectedItem, Categories[3].Items[3]);
    public bool IsInstapayRecordsSelected => ReferenceEquals(selectedItem, Categories[3].Items[4]);
    public bool IsImplementedScreenSelected =>
        IsPosSelected || IsAddProductSelected || IsInventorySelected || IsCategoriesSelected
        || IsInvoicesSelected || IsReturnExchangeSelected || IsPurchaseEntrySelected || IsPurchaseInvoicesSelected
        || IsMaintenanceIntakeSelected || IsMaintenanceInvoicesSelected || IsMaintenanceActionsSelected
        || IsMaintenanceSparePartsSelected || IsCustomersSelected || IsSuppliersSelected || IsMyAccountsSelected
        || IsVodafoneTransferSelected || IsInstapayTransferSelected || IsVodafoneRecordsSelected || IsInstapayRecordsSelected;
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
        CategoryRepository? categoryRepository = null)
    {
        this.user = user;
        this.salesRepository = salesRepository;
        this.categoryRepository = categoryRepository;
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
        var sharedSuppliers = PurchaseDesignSeedData.CreateSharedSampleSuppliers();
        var sharedPurchaseInvoices = PurchaseDesignSeedData.CreateSharedSamplePurchaseInvoices(
            sharedCatalog,
            sharedRegistrations,
            sharedSuppliers);
        var sharedMaintenanceOrders = MaintenanceDesignSeedData.CreateSharedSampleOrders(sharedCustomers);
        var sharedMaintenanceSpareParts = MaintenanceDesignSeedData.CreateSharedSampleSpareParts();

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
            ReturnExchange.NotifyExternalDataChanged);
        PurchaseInvoices = new PurchaseInvoicesViewModel(
            sharedPurchaseInvoices,
            sharedSuppliers,
            sharedCatalog,
            sharedRegistrations,
            user.DisplayName,
            () => SelectItem(Categories[1].Items[0]),
            Inventory.NotifyExternalInventoryChanged,
            ReturnExchange.NotifyExternalDataChanged,
            PurchaseEntry.NotifyExternalCatalogChanged);
        MaintenanceIntake = new MaintenanceIntakeViewModel(
            user.DisplayName,
            sharedCustomers,
            sharedMaintenanceOrders);
        // Spare parts and actions notify each other, so the spare-parts screen gets a late-bound callback
        // instead of dereferencing MaintenanceActions before it has been assigned.
        MaintenanceSpareParts = new MaintenanceSparePartsViewModel(
            sharedMaintenanceSpareParts,
            () => SelectItem(Categories[2].Items[2]),
            NotifyMaintenanceActionsChanged);
        MaintenanceActions = new MaintenanceActionsViewModel(
            user.DisplayName,
            sharedMaintenanceOrders,
            sharedMaintenanceSpareParts,
            () => SelectItem(Categories[2].Items[0]),
            MaintenanceSpareParts.NotifyExternalDataChanged);
        MaintenanceInvoices = new MaintenanceInvoicesViewModel(
            user.DisplayName,
            sharedMaintenanceOrders,
            () => SelectItem(Categories[2].Items[0]),
            order =>
            {
                MaintenanceActions.SelectOrderFromExternal(order);
                SelectItem(Categories[2].Items[2]);
            },
            MaintenanceActions.NotifyExternalDataChanged);
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
        MyAccounts = new MyAccountsViewModel(user.DisplayName);
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
                PurchaseEntry.NotifyExternalCatalogChanged();
                PurchaseInvoices.NotifyExternalPurchasesChanged();
                Inventory.NotifyExternalInventoryChanged();
            });
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
            });

        Inventory.RefreshFromDatabase();
        AddProduct.NotifyInventoryLoaded();

        if (categoryRepository is not null)
            ProductCategories.RefreshFromDatabase();

        if (salesRepository is not null)
        {
            // Customer directory and the saved sales invoices both live in SQL Server.
            try { salesRepository.LoadCustomersInto(sharedCustomers); }
            catch (SalesDataException) { /* The POS status strip explains the failure on first sale. */ }

            Invoices.RefreshInvoicesFromDatabase();
        }

        // Start at the first available destination; all other categories stay closed.
        SelectItem(Categories[0].Items[0]);
    }

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

    /// <summary>Late-bound notifier: the spare-parts screen may call it before MaintenanceActions exists.</summary>
    private void NotifyMaintenanceActionsChanged()
        => MaintenanceActions?.NotifyExternalDataChanged();

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

        Customers.NotifyExternalDataChanged();
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
            PurchaseEntry.NotifyExternalCatalogChanged();
        else if (ReferenceEquals(item, Categories[1].Items[1]))
            PurchaseInvoices.NotifyExternalPurchasesChanged();
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
        Changed(nameof(IsMyAccountsSelected));
        Changed(nameof(IsVodafoneTransferSelected));
        Changed(nameof(IsInstapayTransferSelected));
        Changed(nameof(IsVodafoneRecordsSelected));
        Changed(nameof(IsInstapayRecordsSelected));
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
