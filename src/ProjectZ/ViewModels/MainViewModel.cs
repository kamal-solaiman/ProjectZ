using System.Collections.ObjectModel;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly UserSession user;
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
    public MaintenanceActionsViewModel MaintenanceActions { get; }
    public MaintenanceSparePartsViewModel MaintenanceSpareParts { get; }
    public CustomersViewModel Customers { get; }
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
    public bool IsImplementedScreenSelected =>
        IsPosSelected || IsAddProductSelected || IsInventorySelected || IsCategoriesSelected
        || IsInvoicesSelected || IsReturnExchangeSelected || IsPurchaseEntrySelected || IsPurchaseInvoicesSelected
        || IsMaintenanceIntakeSelected || IsMaintenanceInvoicesSelected || IsMaintenanceActionsSelected
        || IsMaintenanceSparePartsSelected || IsCustomersSelected;
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

    public MainViewModel(UserSession user, Action logout)
    {
        this.user = user;
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
                "تحويل فودافون كاش", "تحويل انستاباي", "سجلات فودافون كاش", "سجلات انستاباي", "حساباتي"),
            new NavigationMenuCategoryViewModel("العملاء والموردين", "\U0001F465",
                "العملاء", "الموردين"),
            new NavigationMenuCategoryViewModel("المستخدمين", "\U0001F464",
                "المستخدمين", "الصلاحيات"),
            new NavigationMenuCategoryViewModel("الاعدادات", "\u2699",
                "اعدادات عامة", "المظهر", "النسخ الاحتياطي")
        };

        var sharedCatalog = PosDesignSeedData.CreateSharedSampleCatalog();
        var sharedCategories = PosDesignSeedData.CreateSharedCategories();
        var sharedCustomers = new ObservableCollection<PosCustomerDesignItem>(
            PosDesignSeedData.CreateSampleCustomers());
        var sharedRegistrations = InventoryDesignSeedData.CreateSharedSampleRegistrations(sharedCatalog);
        var sharedSalesInvoices = InvoiceDesignSeedData.CreateSharedSampleInvoices(sharedCatalog, sharedRegistrations);
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
            sharedCategories);
        Invoices = new InvoicesViewModel(
            sharedSalesInvoices,
            sharedCatalog,
            sharedRegistrations,
            user.DisplayName,
            () => SelectItem(Categories[0].Items[0]),
            Inventory.NotifyExternalInventoryChanged,
            sharedReturnOperations);
        ReturnExchange = new ReturnExchangeViewModel(
            sharedSalesInvoices,
            sharedCatalog,
            sharedRegistrations,
            sharedReturnOperations,
            user.DisplayName,
            () => SelectItem(Categories[0].Items[4]),
            Inventory.NotifyExternalInventoryChanged,
            Invoices.NotifyExternalInvoicesChanged);
        Invoices.SetReturnExchangeNotifier(ReturnExchange.NotifyExternalDataChanged);
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
        MaintenanceSpareParts = new MaintenanceSparePartsViewModel(
            sharedMaintenanceSpareParts,
            () => SelectItem(Categories[2].Items[2]),
            () => MaintenanceActions.NotifyExternalDataChanged());
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
            sharedCustomers);
        AddProduct = new AddProductViewModel(
            sharedCatalog,
            () => SelectItem(Categories[0].Items[0]),
            Inventory.RegisterInitialStockForNewProduct,
            sharedCategories);
        ProductCategories = new CategoriesViewModel(
            sharedCategories,
            () => SelectItem(Categories[0].Items[1]));
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

        // Start at the first available destination; all other categories stay closed.
        SelectItem(Categories[0].Items[0]);
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
        if (ReferenceEquals(item, Categories[0].Items[5]))
            ReturnExchange.NotifyExternalDataChanged();
        else if (ReferenceEquals(item, Categories[0].Items[4]))
            Invoices.NotifyExternalInvoicesChanged();
        else if (ReferenceEquals(item, Categories[0].Items[2]))
            Inventory.NotifyExternalInventoryChanged();
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
