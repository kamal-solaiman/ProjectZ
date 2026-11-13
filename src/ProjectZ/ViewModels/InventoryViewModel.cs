using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Data;

namespace ProjectZ.ViewModels;

public sealed class InventoryViewModel : ObservableObject
{
    private readonly ObservableCollection<PosProductDesignItem> sharedCatalog;
    private readonly ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations;
    private readonly ObservableCollection<string>? sharedCategories;
    private readonly Action? navigateToAddProduct;
    private readonly InventoryRepository? repository;
    private readonly CategoryRepository? categoryRepository;

    private List<string> databaseSuppliers = [];
    private bool isLoadingSuppliers;

    private int nextRegistrationId = 2001;
    private int nextSupplyInvoiceNumber = 130;

    private bool isDamagedTabSelected;
    private string searchText = "";
    private string selectedCategoryFilter = "الكل";
    private string selectedSupplierFilter = "كل الموردين";
    private bool onlyLowStock;

    private string damagedSearchText = "";
    private string selectedSettlementFilter = "الكل";

    private string movementScopeLabel = "سجل حركات المخزون";
    private string statusMessage = "اضغط على أي صنف أو زر «التسجيلات» لعرض الوحدات والموردين، وتحكّم بالتوريد والتعديل والسيريال والباركود والهالك والحذف.";
    private bool isErrorStatus;

    public ObservableCollection<PosProductDesignItem> FilteredProducts { get; } = [];
    public ObservableCollection<InventoryUnitRegistrationItem> FilteredDamagedUnits { get; } = [];
    public ObservableCollection<string> CategoryFilters { get; } = ["الكل"];
    public ObservableCollection<string> SupplierFilters { get; } = ["كل الموردين"];
    public ObservableCollection<StockMovementRow> RecentMovements { get; } = [];
    public IReadOnlyList<string> SettlementFilters { get; } = ["الكل", "قيد المراجعة", "مرتجع للمورد"];

    /// <summary>Suppliers currently known to dbo.Suppliers (used by the supply dialog).</summary>
    public IReadOnlyList<string> KnownSuppliers => databaseSuppliers;

    public bool HasKnownSuppliers => databaseSuppliers.Count > 0;

    public string SuppliersSourceLabel => repository is null
        ? "الموردون: غير متصل بقاعدة البيانات"
        : databaseSuppliers.Count == 0
            ? "لا يوجد موردون مسجلون في dbo.Suppliers — سيُضاف المورد تلقائيًا عند التوريد."
            : $"الموردون المسجلون: {databaseSuppliers.Count.ToString(CultureInfo.InvariantCulture)} من dbo.Suppliers";

    public string MovementsHeader => movementScopeLabel;

    public bool HasMovements => RecentMovements.Count > 0;

    public string MovementSummary => RecentMovements.Count == 0
        ? "لا توجد حركات مخزون مسجلة بعد."
        : $"آخر {RecentMovements.Count.ToString(CultureInfo.InvariantCulture)} حركة مخزون";

    public Action<ProductRegistrationsViewModel>? OpenRegistrationsDialogRequested { get; set; }
    public Action<SupplyStockViewModel>? OpenSupplyDialogRequested { get; set; }
    public Action<EditProductViewModel>? OpenEditDialogRequested { get; set; }
    public Action<BarcodePrintPreviewViewModel>? OpenBarcodePreviewRequested { get; set; }
    public Action<MarkDamagedViewModel>? OpenMarkDamagedDialogRequested { get; set; }
    public Action<DamageEditViewModel>? OpenDamageEditDialogRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    public bool IsCatalogTabSelected
    {
        get => !isDamagedTabSelected;
        set
        {
            if (isDamagedTabSelected == !value) return;
            isDamagedTabSelected = !value;
            Changed();
            Changed(nameof(IsDamagedTabSelected));
        }
    }

    public bool IsDamagedTabSelected
    {
        get => isDamagedTabSelected;
        set
        {
            if (isDamagedTabSelected == value) return;
            isDamagedTabSelected = value;
            Changed();
            Changed(nameof(IsCatalogTabSelected));
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            RefreshCatalogFilter();
        }
    }

    public string SelectedCategoryFilter
    {
        get => selectedCategoryFilter;
        set
        {
            if (selectedCategoryFilter == value || value is null) return;
            selectedCategoryFilter = value;
            Changed();
            RefreshCatalogFilter();
        }
    }

    public string SelectedSupplierFilter
    {
        get => selectedSupplierFilter;
        set
        {
            if (selectedSupplierFilter == value || value is null) return;
            selectedSupplierFilter = value;
            Changed();
            RefreshCatalogFilter();
        }
    }

    public bool OnlyLowStock
    {
        get => onlyLowStock;
        set
        {
            if (onlyLowStock == value) return;
            onlyLowStock = value;
            Changed();
            RefreshCatalogFilter();
        }
    }

    public string DamagedSearchText
    {
        get => damagedSearchText;
        set
        {
            if (damagedSearchText == value) return;
            damagedSearchText = value;
            Changed();
            RefreshDamagedFilter();
        }
    }

    public string SelectedSettlementFilter
    {
        get => selectedSettlementFilter;
        set
        {
            if (selectedSettlementFilter == value || value is null) return;
            selectedSettlementFilter = value;
            Changed();
            RefreshDamagedFilter();
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

    public int TotalProductsCount => sharedCatalog.Count;
    public int TotalAvailableStock => sharedCatalog.Sum(p => p.AvailableStock);
    public int LowStockCount => sharedCatalog.Count(p => p.IsLowStock);
    public int TotalDamagedCount => sharedCatalog.Sum(p => p.DamagedCount);
    public string CatalogTabLabel => $"قائمة البضاعة المسجلة ({TotalProductsCount})";
    public string DamagedTabLabel => $"قائمة مخزون الهالك ({TotalDamagedCount})";
    public bool HasNoCatalogResults => FilteredProducts.Count == 0;
    public bool HasNoDamagedResults => FilteredDamagedUnits.Count == 0;

    public ActionCommand SelectCatalogTabCommand { get; }
    public ActionCommand SelectDamagedTabCommand { get; }
    public ActionCommand ToggleLowStockCommand { get; }
    public ActionCommand ClearCatalogFiltersCommand { get; }
    public ActionCommand ClearDamagedFiltersCommand { get; }
    public ActionCommand GoToAddProductCommand { get; }
    public ActionCommand RefreshInventoryCommand { get; }
    public ActionCommand ReloadSuppliersCommand { get; }
    public RelayCommand<PosProductDesignItem> ShowProductMovementsCommand { get; }
    public RelayCommand<InventoryUnitRegistrationItem> ShowUnitMovementsCommand { get; }
    public RelayCommand<InventoryUnitRegistrationItem> EditDamagedUnitCommand { get; }
    public ActionCommand MarkAllDamagedReturnedCommand { get; }

    public RelayCommand<PosProductDesignItem> OpenRegistrationsCommand { get; }
    public RelayCommand<PosProductDesignItem> OpenSupplyCommand { get; }
    public RelayCommand<PosProductDesignItem> OpenEditCommand { get; }
    public RelayCommand<PosProductDesignItem> OpenBarcodePrintCommand { get; }
    public RelayCommand<PosProductDesignItem> OpenMarkDamagedCommand { get; }
    public RelayCommand<PosProductDesignItem> DeleteProductCommand { get; }

    public RelayCommand<InventoryUnitRegistrationItem> RestoreDamagedUnitCommand { get; }
    public RelayCommand<InventoryUnitRegistrationItem> MarkReturnedToSupplierCommand { get; }
    public RelayCommand<InventoryUnitRegistrationItem> DeleteDamagedUnitCommand { get; }

    public InventoryViewModel()
        : this(CreateDefaultCatalog(out var defaultRegs), defaultRegs, null)
    {
    }

    private static ObservableCollection<PosProductDesignItem> CreateDefaultCatalog(
        out ObservableCollection<InventoryUnitRegistrationItem> regs)
    {
        var catalog = PosDesignSeedData.CreateSharedSampleCatalog();
        regs = InventoryDesignSeedData.CreateSharedSampleRegistrations(catalog);
        return catalog;
    }

    public InventoryViewModel(
        ObservableCollection<PosProductDesignItem> sharedCatalog,
        ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations,
        Action? navigateToAddProduct = null,
        ObservableCollection<string>? sharedCategories = null,
        InventoryRepository? repository = null,
        CategoryRepository? categoryRepository = null)
    {
        this.repository = repository;
        this.categoryRepository = categoryRepository;
        this.sharedCatalog = sharedCatalog;
        this.sharedRegistrations = sharedRegistrations;
        this.sharedCategories = sharedCategories;
        this.navigateToAddProduct = navigateToAddProduct;
        nextRegistrationId = sharedRegistrations.Count > 0 ? sharedRegistrations.Max(r => r.Id) + 1 : 1001;

        SelectCatalogTabCommand = new ActionCommand(() => IsCatalogTabSelected = true);
        SelectDamagedTabCommand = new ActionCommand(() => IsDamagedTabSelected = true);
        ToggleLowStockCommand = new ActionCommand(() =>
        {
            IsCatalogTabSelected = true;
            OnlyLowStock = !OnlyLowStock;
        });
        ClearCatalogFiltersCommand = new ActionCommand(() =>
        {
            SearchText = "";
            SelectedCategoryFilter = "الكل";
            SelectedSupplierFilter = "كل الموردين";
            OnlyLowStock = false;
        });
        ClearDamagedFiltersCommand = new ActionCommand(() =>
        {
            DamagedSearchText = "";
            SelectedSettlementFilter = "الكل";
        });
        GoToAddProductCommand = new ActionCommand(() => this.navigateToAddProduct?.Invoke());
        RefreshInventoryCommand = new ActionCommand(() => RefreshFromDatabase());
        ReloadSuppliersCommand = new ActionCommand(ReloadSuppliers);
        ShowProductMovementsCommand = new RelayCommand<PosProductDesignItem>(product =>
        {
            if (product is null) return;
            LoadMovementsForProduct(product);
            SetStatus($"تم عرض {RecentMovements.Count.ToString(CultureInfo.InvariantCulture)} حركة مخزون للصنف «{product.Name}» من dbo.StockMovements.", false);
        });
        ShowUnitMovementsCommand = new RelayCommand<InventoryUnitRegistrationItem>(unit =>
        {
            if (unit is null) return;
            LoadMovementsForUnit(unit);
            SetStatus($"تم عرض {RecentMovements.Count.ToString(CultureInfo.InvariantCulture)} حركة للوحدة #{unit.Id.ToString(CultureInfo.InvariantCulture)} من dbo.StockMovements.", false);
        });

        OpenRegistrationsCommand = new RelayCommand<PosProductDesignItem>(OpenRegistrationsDialog);
        OpenSupplyCommand = new RelayCommand<PosProductDesignItem>(OpenSupplyDialog);
        OpenEditCommand = new RelayCommand<PosProductDesignItem>(OpenEditDialog);
        OpenBarcodePrintCommand = new RelayCommand<PosProductDesignItem>(OpenBarcodeDialog);
        OpenMarkDamagedCommand = new RelayCommand<PosProductDesignItem>(OpenMarkDamagedDialog);
        DeleteProductCommand = new RelayCommand<PosProductDesignItem>(RequestDeleteProduct);

        RestoreDamagedUnitCommand = new RelayCommand<InventoryUnitRegistrationItem>(unit => RestoreDamagedUnit(unit));
        MarkReturnedToSupplierCommand = new RelayCommand<InventoryUnitRegistrationItem>(MarkReturnedToSupplier);
        DeleteDamagedUnitCommand = new RelayCommand<InventoryUnitRegistrationItem>(RequestDeleteDamagedUnit);
        EditDamagedUnitCommand = new RelayCommand<InventoryUnitRegistrationItem>(OpenDamageEditDialog);
        MarkAllDamagedReturnedCommand = new ActionCommand(() =>
        {
            var damaged = sharedRegistrations.Where(r => r.IsDamaged).ToList();
            if (damaged.Count == 0)
            {
                SetStatus("لا يوجد هالك مسجل لإرجاعه إلى المورد.", true);
                return;
            }

            if (repository is null)
            {
                foreach (var unit in damaged) MarkReturnedToSupplier(unit);
                return;
            }

            var failures = 0;
            foreach (var unit in damaged)
            {
                try { repository.MarkReturnedToSupplier(unit); }
                catch (Exception ex) { failures++; SetStatus(DatabaseError(ex, "تعذر إرجاع أحد سجلات الهالك للمورد."), true); }
            }

            ReloadAfterWrite();
            if (failures == 0)
                SetStatus($"تم تسجيل {damaged.Count.ToString(CultureInfo.InvariantCulture)} وحدة هالكة كمرتجع للمورد في قاعدة البيانات.", false);
        });

        sharedCatalog.CollectionChanged += OnCatalogCollectionChanged;
        sharedRegistrations.CollectionChanged += OnRegistrationsCollectionChanged;

        RebuildFilterLookups();
        RefreshCatalogFilter();
        RefreshDamagedFilter();
        NotifyKpis();
    }

    public bool RefreshFromDatabase()
    {
        if (repository is null) return true;
        try
        {
            var categories = sharedCategories ?? new ObservableCollection<string>();
            // Categories come from dbo.Categories (the same source the categories screen writes to),
            // so a rename/add/delete there is reflected in the inventory filters immediately.
            ReloadCategoryNames(categories);
            repository.LoadInto(sharedCatalog, sharedRegistrations, categories);
            ReloadSuppliers();
            LoadRecentMovements();
            NotifyExternalInventoryChanged();
            SetStatus($"تم تحميل {sharedCatalog.Count.ToString(CultureInfo.InvariantCulture)} صنف و{sharedRegistrations.Count.ToString(CultureInfo.InvariantCulture)} وحدة من قاعدة البيانات.", false);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(DatabaseError(ex, "تعذر تحميل المخزون من قاعدة البيانات. تحقق من تنفيذ مخطط قاعدة البيانات والاتصال."), true);
            return false;
        }
    }

    /// <summary>Reloads from SQL after a successful write and tells the other screens that stock moved.</summary>
    private void ReloadAfterWrite()
    {
        RefreshFromDatabase();
        InventoryChanged?.Invoke();
    }

    public bool RegisterInitialStockForNewProduct(PosProductDesignItem product)
    {
        if (repository is not null)
        {
            try
            {
                repository.SaveNewProduct(product, "رصيد افتتاحي", $"OPEN-{Guid.NewGuid():N}");
                ReloadAfterWrite();
                SetStatus($"تم حفظ الصنف «{product.Name}» ورصيده الافتتاحي في قاعدة البيانات.", false);
                return true;
            }
            catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر حفظ الصنف الجديد في قاعدة البيانات."), true); return false; }
        }
        if (product.AvailableStock <= 0) return true;

        var invoiceId = $"OPEN-{product.Id}";
        var now = DateTime.Today;
        for (var i = 0; i < product.AvailableStock; i++)
        {
            sharedRegistrations.Add(new InventoryUnitRegistrationItem
            {
                Id = nextRegistrationId++,
                ProductId = product.Id,
                ProductName = product.Name,
                ProductBarcode = product.Barcode,
                PurchasePrice = product.CostPrice,
                SupplierName = "رصيد افتتاحي",
                RegisteredAt = now,
                Status = InventoryUnitStatus.Available,
                PurchaseInvoiceId = invoiceId
            });
        }

        RebuildFilterLookups();
        RefreshCatalogFilter();
        NotifyKpis();
        return true;
    }

    /// <summary>
    /// Raised after the inventory screen wrote to SQL, so POS / add-product / invoices / maintenance /
    /// suppliers reload their own lists. RefreshFromDatabase (the screen's own reload) does not raise it.
    /// </summary>
    public event Action? InventoryChanged;

    public void NotifyExternalInventoryChanged()
    {
        RebuildFilterLookups();
        RefreshCatalogFilter();
        RefreshDamagedFilter();
        NotifyKpis();
    }

    private void OpenRegistrationsDialog(PosProductDesignItem? product)
    {
        if (product is null) return;

        var vm = new ProductRegistrationsViewModel(
            product,
            sharedRegistrations,
            MarkSingleUnitDamaged,
            RestoreDamagedUnit,
            SaveSerialNumber,
            () => StatusMessage,
            unit => repository is null
                ? []
                : repository.LoadStockMovements(unitId: unit.Id, limit: 50));

        OpenRegistrationsDialogRequested?.Invoke(vm);
    }

    private void OpenSupplyDialog(PosProductDesignItem? product)
    {
        if (product is null) return;

        var knownSuppliers = GetKnownSuppliers();
        var suggestedInvoice = repository is null
            ? $"PUR-{nextSupplyInvoiceNumber}"
            : $"PUR-{DateTime.Now:yyyyMMdd-HHmmss}-{product.Id}";

        var vm = new SupplyStockViewModel(
            product,
            knownSuppliers,
            suggestedInvoice,
            submission =>
            {
                if (repository is not null)
                {
                    try
                    {
                        repository.Supply(product, submission.Quantity, submission.SupplierName, submission.PurchasePrice, submission.InvoiceId);
                        ReloadAfterWrite();
                        SetStatus($"تم حفظ توريد {submission.Quantity} قطعة من «{product.Name}» في قاعدة البيانات.", false);
                        return true;
                    }
                    catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر حفظ التوريد في قاعدة البيانات."), true); return false; }
                }
                nextSupplyInvoiceNumber++;
                var now = DateTime.Today;
                for (var i = 0; i < submission.Quantity; i++)
                {
                    sharedRegistrations.Add(new InventoryUnitRegistrationItem
                    {
                        Id = nextRegistrationId++,
                        ProductId = product.Id,
                        ProductName = product.Name,
                        ProductBarcode = product.Barcode,
                        PurchasePrice = submission.PurchasePrice,
                        SupplierName = submission.SupplierName,
                        RegisteredAt = now,
                        Status = InventoryUnitStatus.Available,
                        PurchaseInvoiceId = submission.InvoiceId
                    });
                }

                if (submission.PurchasePrice > 0m)
                    product.CostPrice = submission.PurchasePrice;

                SyncProductStockFromRegistrations(product);
                RebuildFilterLookups();
                RefreshCatalogFilter();
                NotifyKpis();
                SetStatus($"تم توريد {submission.Quantity} قطعة من «{product.Name}» بواسطة المورد «{submission.SupplierName}» (فاتورة {submission.InvoiceId}).", false);
                return true;
            },
            () => StatusMessage);

        OpenSupplyDialogRequested?.Invoke(vm);
    }

    private void OpenEditDialog(PosProductDesignItem? product)
    {
        if (product is null) return;

        // Category choices are exactly dbo.Categories; the catalog is only a fallback when
        // the screen runs without a database connection (design mode).
        var baseCategories = sharedCategories is { Count: > 0 }
            ? sharedCategories.AsEnumerable()
            : sharedCatalog.Select(p => p.Category);
        var categoryList = baseCategories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var vm = new EditProductViewModel(
            product,
            sharedCatalog,
            categoryList,
            submission =>
            {
                if (repository is not null)
                {
                    try
                    {
                        var updated = new PosProductDesignItem
                        {
                            Id = product.Id, DatabaseRowVersion = product.DatabaseRowVersion,
                            Name = submission.Name, Barcode = submission.Barcode, Category = submission.Category,
                            Unit = submission.Unit, CostPrice = submission.CostPrice, RetailPrice = submission.RetailPrice,
                            WholesalePrice = submission.WholesalePrice, SpecialPrice = submission.SpecialPrice,
                            MinStockAlert = submission.MinStockAlert, IsActiveForSale = submission.IsActiveForSale,
                            TrackSerialNumber = submission.TrackSerialNumber, Notes = submission.Notes
                        };
                        repository.UpdateProduct(updated);
                        ReloadAfterWrite();
                        SetStatus($"تم تحديث الصنف «{product.Name}» في قاعدة البيانات.", false);
                        return true;
                    }
                    catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر تحديث الصنف في قاعدة البيانات."), true); return false; }
                }
                product.Name = submission.Name;
                product.Barcode = submission.Barcode;
                product.Category = submission.Category;
                product.Unit = submission.Unit;
                product.CostPrice = submission.CostPrice;
                product.RetailPrice = submission.RetailPrice;
                product.WholesalePrice = submission.WholesalePrice;
                product.SpecialPrice = submission.SpecialPrice;
                product.MinStockAlert = submission.MinStockAlert;
                product.IsActiveForSale = submission.IsActiveForSale;
                product.TrackSerialNumber = submission.TrackSerialNumber;
                product.Notes = submission.Notes;

                foreach (var reg in sharedRegistrations.Where(r => r.ProductId == product.Id))
                {
                    reg.ProductName = submission.Name;
                    reg.ProductBarcode = submission.Barcode;
                }

                RebuildFilterLookups();
                RefreshCatalogFilter();
                RefreshDamagedFilter();
                NotifyKpis();
                SetStatus($"تم تحديث بيانات الصنف «{product.Name}» بنجاح.", false);
                return true;
            },
            () => StatusMessage);

        OpenEditDialogRequested?.Invoke(vm);
    }

    private void OpenBarcodeDialog(PosProductDesignItem? product)
    {
        if (product is null) return;

        var vm = new BarcodePrintPreviewViewModel(
            product,
            msg => SetStatus(msg, false));

        OpenBarcodePreviewRequested?.Invoke(vm);
    }

    private void OpenMarkDamagedDialog(PosProductDesignItem? product)
    {
        if (product is null) return;

        if (product.AvailableStock <= 0)
        {
            SetStatus($"لا توجد كمية متاحة للصنف «{product.Name}» لتسجيل هالك.", true);
            return;
        }

        var vm = new MarkDamagedViewModel(
            product,
            sharedRegistrations,
            submission =>
            {
                if (repository is not null)
                {
                    try
                    {
                        repository.MarkDamaged(product, submission.Quantity, submission.SupplierFilter, submission.SpecificUnitId, submission.Reason);
                        ReloadAfterWrite();
                        SetStatus($"تم تسجيل {submission.Quantity} قطعة هالك من «{product.Name}» وحفظ حركات المخزون.", false);
                        return true;
                    }
                    catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر تسجيل الهالك في قاعدة البيانات."), true); return false; }
                }
                var candidates = sharedRegistrations
                    .Where(r => r.ProductId == product.Id && r.IsAvailable)
                    .AsEnumerable();

                if (submission.SpecificUnitId is { } unitId)
                    candidates = candidates.Where(r => r.Id == unitId);
                else if (!string.IsNullOrWhiteSpace(submission.SupplierFilter))
                    candidates = candidates.Where(r => r.SupplierName.Equals(submission.SupplierFilter, StringComparison.OrdinalIgnoreCase));

                var toMark = candidates
                    .OrderBy(r => r.RegisteredAt)
                    .ThenBy(r => r.Id)
                    .Take(submission.Quantity)
                    .ToList();

                if (toMark.Count > 0)
                {
                    foreach (var unit in toMark)
                        MarkUnitAsDamagedInternal(unit, submission.Reason);

                    SyncProductStockFromRegistrations(product);
                }
                else
                {
                    product.AvailableStock = Math.Max(0, product.AvailableStock - submission.Quantity);
                    product.DamagedCount += submission.Quantity;
                }

                RefreshCatalogFilter();
                RefreshDamagedFilter();
                NotifyKpis();
                SetStatus($"تم تسجيل {submission.Quantity} قطعة هالك من «{product.Name}» ({submission.Reason}) وإدراجها في قائمة مخزون الهالك.", false);
                return true;
            },
            () => StatusMessage);

        OpenMarkDamagedDialogRequested?.Invoke(vm);
    }

    private void RequestDeleteProduct(PosProductDesignItem? product)
    {
        if (product is null) return;

        var vm = new ConfirmActionViewModel(
            "تأكيد حذف المنتج",
            product.Name,
            $"هل تريد حذف الصنف «{product.Name}» ({product.Barcode}) من المخزون ونقطة البيع؟\nإذا كان له تاريخ فواتير أو حركات فستتم أرشفته بدل حذف التاريخ.",
            "نعم، حذف المنتج");

        OpenConfirmDialogRequested?.Invoke(vm, () =>
        {
            if (repository is not null)
            {
                try
                {
                    var deleted = repository.DeleteOrArchiveProduct(product);
                    ReloadAfterWrite();
                    SetStatus(deleted
                        ? $"تم حذف الصنف «{product.Name}» من قاعدة البيانات."
                        : $"للصنف «{product.Name}» حركات أو فواتير؛ تمت أرشفته بدل حذف تاريخه.", false);
                }
                catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر حذف أو أرشفة الصنف."), true); }
                return;
            }
            var toRemoveRegs = sharedRegistrations.Where(r => r.ProductId == product.Id).ToList();
            foreach (var reg in toRemoveRegs)
                sharedRegistrations.Remove(reg);

            sharedCatalog.Remove(product);
            RebuildFilterLookups();
            RefreshCatalogFilter();
            RefreshDamagedFilter();
            NotifyKpis();
            SetStatus($"تم حذف الصنف «{product.Name}» من المخزون.", false);
        });
    }

    private bool MarkSingleUnitDamaged(InventoryUnitRegistrationItem unit, string reason)
    {
        if (!unit.IsAvailable) return false;

        if (repository is not null)
        {
            try
            {
                repository.MarkSingleDamaged(unit, reason);
                ReloadAfterWrite();
                SetStatus($"تم حفظ هالك التسجيل {unit.IdDisplay} وحركة المخزون في قاعدة البيانات.", false);
            }
            catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر تسجيل هالك الوحدة."), true); return false; }
            return true;
        }
        MarkUnitAsDamagedInternal(unit, reason);
        var product = sharedCatalog.FirstOrDefault(p => p.Id == unit.ProductId);
        if (product is not null)
            SyncProductStockFromRegistrations(product);

        RefreshCatalogFilter();
        RefreshDamagedFilter();
        NotifyKpis();
        SetStatus($"تم تحويل التسجيل {unit.IdDisplay} من «{unit.ProductName}» (المورد: {unit.SupplierName}) إلى مخزون الهالك.", false);
        return true;
    }

    private void OpenDamageEditDialog(InventoryUnitRegistrationItem? unit)
    {
        if (unit is null) return;

        var vm = new DamageEditViewModel(
            unit,
            (settlement, reason) =>
            {
                if (repository is null)
                {
                    unit.DamageSettlementStatus = settlement;
                    unit.DamageReason = reason;
                    return true;
                }

                try
                {
                    repository.UpdateDamageReason(unit, reason);
                    if (!unit.DamageSettlementStatus.Equals(settlement, StringComparison.OrdinalIgnoreCase))
                        repository.UpdateDamageSettlement(unit, settlement);
                    ReloadAfterWrite();
                    SetStatus($"تم حفظ بيانات هالك الوحدة {unit.IdDisplay} (التسوية: {settlement}).", false);
                    return true;
                }
                catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر حفظ تعديل الهالك."), true); return false; }
            },
            () => StatusMessage);

        OpenDamageEditDialogRequested?.Invoke(vm);
    }

    private bool SaveSerialNumber(InventoryUnitRegistrationItem unit, string serial)
    {
        if (repository is null) { unit.SerialNumber = serial; return true; }
        try
        {
            repository.RegisterSerial(unit, serial);
            ReloadAfterWrite();
            SetStatus($"تم حفظ السيريال للتسجيل {unit.IdDisplay} في قاعدة البيانات.", false);
        }
        catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر حفظ السيريال. تأكد من عدم تكراره."), true); return false; }
        return true;
    }

    private bool RestoreDamagedUnit(InventoryUnitRegistrationItem? unit)
    {
        if (unit is null || !unit.IsDamaged) return false;

        if (repository is not null)
        {
            try
            {
                repository.RestoreDamaged(unit);
                ReloadAfterWrite();
                SetStatus($"تم استرجاع {unit.IdDisplay} وحفظ حركة المخزون في قاعدة البيانات.", false);
            }
            catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر استرجاع الوحدة إلى المخزون."), true); return false; }
            return true;
        }
        unit.Status = InventoryUnitStatus.Available;
        unit.DamageReason = "";
        unit.DamagedAt = null;
        unit.DamageSettlementStatus = "";

        var product = sharedCatalog.FirstOrDefault(p => p.Id == unit.ProductId);
        if (product is not null)
            SyncProductStockFromRegistrations(product);

        RefreshCatalogFilter();
        RefreshDamagedFilter();
        NotifyKpis();
        SetStatus($"تم استرجاع {unit.IdDisplay} ({unit.ProductName}) إلى المخزون المتاح.", false);
        return true;
    }

    private void MarkReturnedToSupplier(InventoryUnitRegistrationItem? unit)
    {
        if (unit is null || !unit.IsDamaged) return;

        if (repository is not null)
        {
            try
            {
                repository.MarkReturnedToSupplier(unit);
                ReloadAfterWrite();
                SetStatus($"تم حفظ تسوية الهالك {unit.IdDisplay} مع المورد في قاعدة البيانات.", false);
            }
            catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر حفظ إرجاع الهالك للمورد."), true); }
            return;
        }
        unit.DamageSettlementStatus = "مرتجع للمورد";
        RefreshDamagedFilter();
        SetStatus($"تم تحديث حالة التسجيل الهالك {unit.IdDisplay} إلى «مرتجع للمورد: {unit.SupplierName}».", false);
    }

    private void RequestDeleteDamagedUnit(InventoryUnitRegistrationItem? unit)
    {
        if (unit is null || !unit.IsDamaged) return;

        var vm = new ConfirmActionViewModel(
            "تأكيد إعدام / حذف الهالك",
            $"{unit.ProductName} ({unit.IdDisplay})",
            $"هل تريد حذف سجل الهالك {unit.IdDisplay} للصنف «{unit.ProductName}» (المورد: {unit.SupplierName}) نهائيًا من قائمة الهالك؟",
            "نعم، حذف نهائي");

        OpenConfirmDialogRequested?.Invoke(vm, () =>
        {
            if (repository is not null)
            {
                try
                {
                    repository.DeleteDamaged(unit);
                    ReloadAfterWrite();
                    SetStatus($"تم حذف سجل الهالك {unit.IdDisplay} وحركاته غير المرتبطة بفاتورة.", false);
                }
                catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر حذف سجل الهالك."), true); }
                return;
            }
            sharedRegistrations.Remove(unit);
            var product = sharedCatalog.FirstOrDefault(p => p.Id == unit.ProductId);
            if (product is not null)
            {
                var remainingRegs = sharedRegistrations.Where(r => r.ProductId == product.Id).ToList();
                if (remainingRegs.Count > 0)
                    SyncProductStockFromRegistrations(product);
                else
                    product.DamagedCount = Math.Max(0, product.DamagedCount - 1);
            }

            RefreshCatalogFilter();
            RefreshDamagedFilter();
            NotifyKpis();
            SetStatus($"تم إعدام وحذف سجل الهالك {unit.IdDisplay} نهائيًا.", false);
        });
    }

    private static void MarkUnitAsDamagedInternal(InventoryUnitRegistrationItem unit, string reason)
    {
        unit.Status = InventoryUnitStatus.Damaged;
        unit.DamageReason = reason;
        unit.DamagedAt = DateTime.Today;
        unit.DamageSettlementStatus = "قيد المراجعة";
    }

    private void SyncProductStockFromRegistrations(PosProductDesignItem product)
    {
        var regs = sharedRegistrations.Where(r => r.ProductId == product.Id).ToList();
        if (regs.Count == 0) return;

        product.AvailableStock = regs.Count(r => r.IsAvailable);
        product.DamagedCount = regs.Count(r => r.IsDamaged);
    }

    private List<string> GetKnownSuppliers()
    {
        var known = new List<string>(databaseSuppliers);
        known.AddRange(sharedRegistrations.Select(r => r.SupplierName));
        return known.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
    }

    /// <summary>
    /// Reloads dbo.Categories into the shared name list so the inventory category filter and the
    /// edit-product category combo match the categories screen exactly.
    /// </summary>
    private void ReloadCategoryNames(ObservableCollection<string> target)
    {
        if (categoryRepository is null) return;
        try { categoryRepository.LoadNamesInto(target); }
        catch (Exception ex)
        {
            SetStatus(ex is CategoryDataException ? ex.Message : "تعذر قراءة التصنيفات من قاعدة البيانات؛ أُعيد استخدام آخر قائمة محمّلة.", true);
        }
    }

    /// <summary>Reads dbo.Suppliers so the supply dialog suggests real suppliers.</summary>
    private void ReloadSuppliers()
    {
        if (repository is null || isLoadingSuppliers) return;
        isLoadingSuppliers = true;
        try
        {
            databaseSuppliers = repository.LoadSupplierNames();
            Changed(nameof(KnownSuppliers));
            Changed(nameof(HasKnownSuppliers));
            Changed(nameof(SuppliersSourceLabel));
            RebuildFilterLookups();
        }
        catch (Exception ex)
        {
            databaseSuppliers = [];
            SetStatus(DatabaseError(ex, "تعذر قراءة الموردين من قاعدة البيانات."), true);
        }
        finally { isLoadingSuppliers = false; }
    }

    /// <summary>Shows the newest movements of the whole warehouse in the ledger panel.</summary>
    private void LoadRecentMovements()
    {
        RecentMovements.Clear();
        movementScopeLabel = "سجل حركات المخزون — آخر الحركات على مستوى المخزن";
        if (repository is null)
        {
            Changed(nameof(MovementsHeader));
            Changed(nameof(HasMovements));
            Changed(nameof(MovementSummary));
            return;
        }

        try
        {
            foreach (var row in repository.LoadStockMovements(limit: 100)) RecentMovements.Add(row);
        }
        catch (Exception ex)
        {
            SetStatus(DatabaseError(ex, "تعذر قراءة سجل حركات المخزون من قاعدة البيانات."), true);
        }

        Changed(nameof(MovementsHeader));
        Changed(nameof(HasMovements));
        Changed(nameof(MovementSummary));
    }

    /// <summary>Filters the ledger panel to one product (stock moves for that item only).</summary>
    private void LoadMovementsForProduct(PosProductDesignItem product)
    {
        if (repository is null) return;
        try
        {
            var rows = repository.LoadStockMovements(productId: product.Id, limit: 100);
            RecentMovements.Clear();
            foreach (var row in rows) RecentMovements.Add(row);
            movementScopeLabel = $"سجل حركات «{product.Name}»";
            Changed(nameof(MovementsHeader));
            Changed(nameof(HasMovements));
            Changed(nameof(MovementSummary));
        }
        catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر قراءة حركات الصنف."), true); }
    }

    /// <summary>Filters the ledger panel to one physical unit (registrations window).</summary>
    private void LoadMovementsForUnit(InventoryUnitRegistrationItem unit)
    {
        if (repository is null) return;
        try
        {
            var rows = repository.LoadStockMovements(unitId: unit.Id, limit: 50);
            RecentMovements.Clear();
            foreach (var row in rows) RecentMovements.Add(row);
            movementScopeLabel = $"سجل حركات الوحدة #{unit.Id.ToString(CultureInfo.InvariantCulture)}";
            Changed(nameof(MovementsHeader));
            Changed(nameof(HasMovements));
            Changed(nameof(MovementSummary));
        }
        catch (Exception ex) { SetStatus(DatabaseError(ex, "تعذر قراءة حركات الوحدة."), true); }
    }

    private void RebuildFilterLookups()
    {
        var currentCat = selectedCategoryFilter;
        CategoryFilters.Clear();
        CategoryFilters.Add("الكل");
        var categorySource = sharedCategories is { Count: > 0 }
            ? sharedCategories.Where(c => !string.IsNullOrWhiteSpace(c))
            : sharedCatalog.Select(p => p.Category).Where(c => !string.IsNullOrWhiteSpace(c));
        foreach (var cat in categorySource.Distinct(StringComparer.CurrentCultureIgnoreCase))
            CategoryFilters.Add(cat);
        selectedCategoryFilter = CategoryFilters.Contains(currentCat) ? currentCat : "الكل";
        Changed(nameof(SelectedCategoryFilter));

        var currentSup = selectedSupplierFilter;
        SupplierFilters.Clear();
        SupplierFilters.Add("كل الموردين");
        var supplierSource = databaseSuppliers.Concat(sharedRegistrations.Select(r => r.SupplierName));
        foreach (var sup in supplierSource.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.CurrentCultureIgnoreCase))
            SupplierFilters.Add(sup);
        selectedSupplierFilter = SupplierFilters.Contains(currentSup) ? currentSup : "كل الموردين";
        Changed(nameof(SelectedSupplierFilter));
    }

    private void OnCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFilterLookups();
        RefreshCatalogFilter();
        NotifyKpis();
    }

    private void OnRegistrationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDamagedFilter();
        NotifyKpis();
    }

    private void RefreshCatalogFilter()
    {
        var query = searchText.Trim();
        var matches = sharedCatalog.Where(p =>
        {
            if (!string.IsNullOrEmpty(query)
                && !p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !p.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (selectedCategoryFilter != "الكل" && !p.Category.Equals(selectedCategoryFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (selectedSupplierFilter != "كل الموردين")
            {
                var hasSupplier = sharedRegistrations.Any(r =>
                    r.ProductId == p.Id
                    && r.SupplierName.Equals(selectedSupplierFilter, StringComparison.OrdinalIgnoreCase));
                if (!hasSupplier)
                    return false;
            }

            if (onlyLowStock && !p.IsLowStock)
                return false;

            return true;
        });

        FilteredProducts.Clear();
        foreach (var item in matches)
            FilteredProducts.Add(item);

        Changed(nameof(HasNoCatalogResults));
        NotifyKpis();
    }

    private void RefreshDamagedFilter()
    {
        var query = damagedSearchText.Trim();
        var matches = sharedRegistrations
            .Where(r => r.IsDamaged)
            .Where(r =>
            {
                if (selectedSettlementFilter != "الكل"
                    && !r.DamageSettlementStatus.Equals(selectedSettlementFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(query)
                    && !r.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !r.ProductBarcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !r.SupplierName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !r.PurchaseInvoiceId.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !r.DamageReason.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !r.IdDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            })
            .OrderByDescending(r => r.DamagedAt)
            .ThenByDescending(r => r.Id);

        FilteredDamagedUnits.Clear();
        foreach (var item in matches)
            FilteredDamagedUnits.Add(item);

        Changed(nameof(HasNoDamagedResults));
        NotifyKpis();
    }

    private void NotifyKpis()
    {
        Changed(nameof(TotalProductsCount));
        Changed(nameof(TotalAvailableStock));
        Changed(nameof(LowStockCount));
        Changed(nameof(TotalDamagedCount));
        Changed(nameof(CatalogTabLabel));
        Changed(nameof(DamagedTabLabel));
    }

    private static string DatabaseError(Exception exception, string fallback)
    {
        if (exception is InventoryDataException) return exception.Message;
        if (exception is Microsoft.Data.SqlClient.SqlException sql)
            return sql.Number switch
            {
                2601 or 2627 => "الباركود أو السيريال مستخدم بالفعل. أدخل قيمة مختلفة.",
                547 => "السجل مرتبط بفاتورة أو حركة أخرى ولا يمكن حذفه.",
                1205 => "تعارضت العملية مع تحديث آخر. أعد المحاولة بعد تحديث الشاشة.",
                >= 50000 => sql.Message,
                _ => fallback
            };
        return fallback;
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
