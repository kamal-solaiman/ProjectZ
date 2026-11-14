using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

/// <summary>
/// The maintenance spare-parts inventory screen. It owns the SQL-backed CRUD, the supplier links,
/// the restock workflow and the movement ledger, and it keeps the shared collection used by
/// «إجراءات الصيانة» in sync. Design mode (repository is null) still works with sample data.
/// </summary>
public sealed class MaintenanceSparePartsViewModel : ObservableObject
{
    private readonly ObservableCollection<MaintenanceSparePartItem> sharedSpareParts;
    private readonly Action? navigateToActions;
    private readonly Action? notifySparePartsChanged;
    private readonly MaintenanceRepository? repository;
    private readonly List<MaintenanceSparePartMovementItem> allMovements = [];

    private MaintenanceSparePartItem? editingPart;
    private string partName = "";
    private string selectedCategory = "شاشات";
    private string customCategoryName = "";
    private string barcodeText = "";
    private string unitText = "قطعة";
    private SparePartSupplierOption? selectedSupplier;
    private string quantityText = "1";
    private string minimumStockText = "3";
    private string reorderQuantityText = "0";
    private string storageLocationText = "";
    private string notesText = "";
    private string costPriceText = "0";
    private string sellPriceText = "0";

    private string searchText = "";
    private string movementSearchText = "";
    private string selectedCategoryFilter = "كل الفئات";
    private string selectedSupplierFilter = "كل الموردين";
    private string selectedStockFilter = "كل حالات الرصيد";
    private string selectedMovementKindFilter = "كل الحركات";
    private int activeWorkspaceTabIndex;
    private string statusMessage = "شاشة قطع الغيار: مخزون صيانة مستقل مربوط بالموردين وأوامر الصيانة والعملاء.";
    private bool isErrorStatus;
    private DateTime? lastRefreshedAt;
    private bool lastLoadFailed;

    public ObservableCollection<MaintenanceSparePartItem> FilteredSpareParts { get; } = [];
    public ObservableCollection<MaintenanceSparePartMovementItem> FilteredMovements { get; } = [];
    public ObservableCollection<string> CategoryOptions { get; } = [];
    public ObservableCollection<string> CategoryFilterOptions { get; } = [];
    public ObservableCollection<SparePartSupplierOption> SupplierOptions { get; } = [];
    public ObservableCollection<string> SupplierFilterOptions { get; } = [];

    public IReadOnlyList<string> StockFilterOptions { get; } =
        ["كل حالات الرصيد", "متاح", "رصيد منخفض", "نفد"];

    public IReadOnlyList<string> MovementKindFilterOptions { get; } =
        ["كل الحركات", "توريد", "صرف", "إرجاع", "تسويات", "رصيد افتتاحي"];

    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }
    public Func<MaintenanceSparePartRestockViewModel, bool?>? OpenRestockDialogRequested { get; set; }
    public Action<int>? OpenSupplierRequested { get; set; }
    public Action<int>? OpenCustomerRequested { get; set; }
    public Action<int>? OpenOrderRequested { get; set; }

    public bool IsDatabaseConnected => repository is not null;
    public string ConnectionBadgeText => IsDatabaseConnected
        ? (lastLoadFailed ? "تعذّر التحميل — راجع الحالة" : "متصل بقاعدة البيانات — SQL Server")
        : "معاينة تصميم — بدون قاعدة بيانات";
    public string ConnectionBadgeDetail => IsDatabaseConnected
        ? (lastLoadFailed
            ? "تعذّر الاتصال أو القراءة من SQL Server. تحقق من الخادم والصلاحيات ثم اضغط «تحديث من SQL»."
            : "dbo.MaintenanceSpareParts · dbo.MaintenanceSparePartMovements · dbo.Suppliers · dbo.MaintenanceOrders")
        : "تُعرض بيانات نموذجية حتى يتوفر اتصال SQL Server.";

    public string LastRefreshDisplay => lastLoadFailed
        ? "فشل آخر تحديث"
        : lastRefreshedAt.HasValue
            ? lastRefreshedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            : (IsDatabaseConnected ? "لم يتم التحميل بعد" : "بيانات نموذجية");

    public string InventorySummaryText
    {
        get
        {
            var count = sharedSpareParts.Count;
            var units = sharedSpareParts.Sum(p => p.Quantity);
            var lowOrOut = sharedSpareParts.Count(p => p.IsLowStock || p.IsOutOfStock);
            var value = sharedSpareParts.Sum(p => p.TotalCostValue);
            return $"{count} صنف · {units} وحدة متاحة · {lowOrOut} بحاجة توريد · قيمة المخزون بالتكلفة {PosFormatting.FormatCurrency(value)}";
        }
    }

    public string MovementsSummaryText
    {
        get
        {
            var incoming = allMovements.Count(m => m.IsIncoming);
            var outgoing = allMovements.Count - incoming;
            return $"{allMovements.Count} حركة محمّلة · {incoming} إضافة · {outgoing} خصم";
        }
    }

    public bool IsEditMode => editingPart is not null;
    public bool IsAddMode => editingPart is null;
    public string FormHeaderTitle => IsEditMode
        ? $"تعديل قطعة الغيار ({editingPart!.CodeDisplay})"
        : "تسجيل قطعة غيار جديدة للصيانة";
    public string SaveButtonText => IsEditMode ? "حفظ التعديلات" : "+ تسجيل قطعة الغيار";

    public int ActiveWorkspaceTabIndex
    {
        get => activeWorkspaceTabIndex;
        set
        {
            if (activeWorkspaceTabIndex == value) return;
            activeWorkspaceTabIndex = value;
            Changed();
            Changed(nameof(IsInventoryTabSelected));
            Changed(nameof(IsMovementsTabSelected));
        }
    }

    public bool IsInventoryTabSelected => ActiveWorkspaceTabIndex == 0;
    public bool IsMovementsTabSelected => ActiveWorkspaceTabIndex == 1;
    public string InventoryTabTitle => $"مخزون قطع الغيار ({sharedSpareParts.Count})";
    public string MovementsTabTitle => $"سجل الحركات ({allMovements.Count})";

    public string PartName
    {
        get => partName;
        set
        {
            if (partName == value) return;
            partName = value;
            Changed();
        }
    }

    public string SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedCategory == value) return;
            selectedCategory = value;
            Changed();
            Changed(nameof(IsCustomCategorySelected));
        }
    }

    public bool IsCustomCategorySelected => SelectedCategory == "أخرى";

    public string CustomCategoryName
    {
        get => customCategoryName;
        set
        {
            if (customCategoryName == value) return;
            customCategoryName = value;
            Changed();
        }
    }

    public string BarcodeText
    {
        get => barcodeText;
        set
        {
            if (barcodeText == value) return;
            barcodeText = value;
            Changed();
        }
    }

    public string UnitText
    {
        get => unitText;
        set
        {
            if (unitText == value) return;
            unitText = value;
            Changed();
        }
    }

    public SparePartSupplierOption? SelectedSupplier
    {
        get => selectedSupplier;
        set
        {
            if (ReferenceEquals(selectedSupplier, value)) return;
            selectedSupplier = value;
            Changed();
            Changed(nameof(SelectedSupplierDisplay));
        }
    }

    public string SelectedSupplierDisplay => SelectedSupplier?.DisplayName ?? "بدون مورد";

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (quantityText == value) return;
            quantityText = value;
            Changed();
        }
    }

    public string CostPriceText
    {
        get => costPriceText;
        set
        {
            if (costPriceText == value) return;
            costPriceText = value;
            Changed();
            Changed(nameof(PreviewProfitDisplay));
            Changed(nameof(IsPreviewProfitNegative));
        }
    }

    public string SellPriceText
    {
        get => sellPriceText;
        set
        {
            if (sellPriceText == value) return;
            sellPriceText = value;
            Changed();
            Changed(nameof(PreviewProfitDisplay));
            Changed(nameof(IsPreviewProfitNegative));
        }
    }

    public bool IsPreviewProfitNegative => PreviewProfitValue < 0m;
    private decimal PreviewProfitValue
    {
        get
        {
            var cost = PosFormatting.TryParseAmount(CostPriceText, out var c) && c >= 0m ? c : 0m;
            var sell = PosFormatting.TryParseAmount(SellPriceText, out var s) && s >= 0m ? s : 0m;
            return sell - cost;
        }
    }

    public string MinimumStockText
    {
        get => minimumStockText;
        set
        {
            if (minimumStockText == value) return;
            minimumStockText = value;
            Changed();
        }
    }

    public string ReorderQuantityText
    {
        get => reorderQuantityText;
        set
        {
            if (reorderQuantityText == value) return;
            reorderQuantityText = value;
            Changed();
        }
    }

    public string StorageLocationText
    {
        get => storageLocationText;
        set
        {
            if (storageLocationText == value) return;
            storageLocationText = value;
            Changed();
        }
    }

    public string NotesText
    {
        get => notesText;
        set
        {
            if (notesText == value) return;
            notesText = value;
            Changed();
        }
    }

    public string PreviewProfitDisplay => PosFormatting.FormatCurrency(PreviewProfitValue);

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            RefreshFilteredSpareParts();
        }
    }

    public string MovementSearchText
    {
        get => movementSearchText;
        set
        {
            if (movementSearchText == value) return;
            movementSearchText = value;
            Changed();
            RefreshFilteredMovements();
        }
    }

    public string SelectedCategoryFilter
    {
        get => selectedCategoryFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedCategoryFilter == value) return;
            selectedCategoryFilter = value;
            Changed();
            RefreshFilteredSpareParts();
        }
    }

    public string SelectedSupplierFilter
    {
        get => selectedSupplierFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedSupplierFilter == value) return;
            selectedSupplierFilter = value;
            Changed();
            RefreshFilteredSpareParts();
        }
    }

    public string SelectedStockFilter
    {
        get => selectedStockFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedStockFilter == value) return;
            selectedStockFilter = value;
            Changed();
            RefreshFilteredSpareParts();
        }
    }

    public string SelectedMovementKindFilter
    {
        get => selectedMovementKindFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedMovementKindFilter == value) return;
            selectedMovementKindFilter = value;
            Changed();
            RefreshFilteredMovements();
        }
    }

    public bool HasNoFilteredSpareParts => FilteredSpareParts.Count == 0;
    public bool HasNoFilteredMovements => FilteredMovements.Count == 0;

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

    public ActionCommand GoToActionsCommand { get; }
    public ActionCommand SavePartCommand { get; }
    public ActionCommand CancelEditCommand { get; }
    public ActionCommand ResetFormCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public ActionCommand ClearMovementSearchCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand RefreshDataCommand { get; }
    public ActionCommand RefreshMovementsCommand { get; }
    public RelayCommand<MaintenanceSparePartItem> BeginEditCommand { get; }
    public RelayCommand<MaintenanceSparePartItem> DeletePartCommand { get; }
    public RelayCommand<MaintenanceSparePartItem> RestockPartCommand { get; }
    public RelayCommand<string> SelectWorkspaceTabCommand { get; }
    public RelayCommand<MaintenanceSparePartMovementItem> OpenSupplierCommand { get; }
    public RelayCommand<MaintenanceSparePartMovementItem> OpenCustomerCommand { get; }
    public RelayCommand<MaintenanceSparePartMovementItem> OpenOrderCommand { get; }

    public MaintenanceSparePartsViewModel()
        : this(MaintenanceDesignSeedData.CreateSharedSampleSpareParts(), null, null, null)
    {
    }

    public MaintenanceSparePartsViewModel(
        ObservableCollection<MaintenanceSparePartItem> sharedSpareParts,
        Action? navigateToActions = null,
        Action? notifySparePartsChanged = null,
        MaintenanceRepository? repository = null)
    {
        this.sharedSpareParts = sharedSpareParts;
        this.navigateToActions = navigateToActions;
        this.notifySparePartsChanged = notifySparePartsChanged;
        this.repository = repository;

        foreach (var cat in MaintenanceDesignSeedData.SparePartCategories)
            CategoryOptions.Add(cat);

        if (repository is null)
        {
            // Design/preview mode: make the supplier picker usable with the same sample suppliers
            // used by the suppliers screen. SQL mode fills SupplierOptions from dbo.Suppliers.
            foreach (var supplier in PurchaseDesignSeedData.CreateSharedSampleSuppliers())
                SupplierOptions.Add(new SparePartSupplierOption(supplier.Id, supplier.Name, supplier.CompanyName, supplier.Phone));
        }

        this.sharedSpareParts.CollectionChanged += OnSparePartsChanged;

        GoToActionsCommand = new ActionCommand(() => this.navigateToActions?.Invoke());
        SavePartCommand = new ActionCommand(SavePart);
        CancelEditCommand = new ActionCommand(CancelEdit);
        ResetFormCommand = new ActionCommand(() =>
        {
            ClearFormFields();
            SetStatus("تم تفريغ الحقول لبدء تسجيل قطعة غيار جديدة.", false);
        });
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        ClearMovementSearchCommand = new ActionCommand(() => MovementSearchText = "");
        ClearFiltersCommand = new ActionCommand(() =>
        {
            searchText = "";
            selectedCategoryFilter = "كل الفئات";
            selectedSupplierFilter = "كل الموردين";
            selectedStockFilter = "كل حالات الرصيد";
            Changed(nameof(SearchText));
            Changed(nameof(SelectedCategoryFilter));
            Changed(nameof(SelectedSupplierFilter));
            Changed(nameof(SelectedStockFilter));
            RefreshFilteredSpareParts();
            SetStatus("تمت إعادة ضبط فلاتر المخزون.", false);
        });
        RefreshDataCommand = new ActionCommand(() =>
        {
            NotifyExternalDataChanged();
            if (!lastLoadFailed)
            {
                // The actions screen keeps its own catalog projection; refresh it after a manual reload.
                notifySparePartsChanged?.Invoke();
                SetStatus(repository is null
                    ? "تم تحديث بيانات المعاينة (لا يوجد اتصال بقاعدة البيانات)."
                    : "تم تحديث مخزون قطع الغيار وسجل الحركات من قاعدة البيانات.", false);
            }
        });
        RefreshMovementsCommand = new ActionCommand(() =>
        {
            LoadMovements();
            RefreshFilteredMovements();
            if (!lastLoadFailed)
            {
                if (repository is not null)
                {
                    lastRefreshedAt = DateTime.Now;
                    Changed(nameof(LastRefreshDisplay));
                }
                SetStatus(repository is null
                    ? "تم تحديث حركات المعاينة (لا يوجد اتصال بقاعدة البيانات)."
                    : "تم تحديث سجل حركات قطع الغيار من قاعدة البيانات.", false);
            }
        });
        BeginEditCommand = new RelayCommand<MaintenanceSparePartItem>(BeginEdit);
        DeletePartCommand = new RelayCommand<MaintenanceSparePartItem>(RequestDelete);
        RestockPartCommand = new RelayCommand<MaintenanceSparePartItem>(RequestRestock);
        SelectWorkspaceTabCommand = new RelayCommand<string>(key =>
        {
            if (int.TryParse(key, out var index))
                ActiveWorkspaceTabIndex = index;
        });
        OpenSupplierCommand = new RelayCommand<MaintenanceSparePartMovementItem>(movement =>
        {
            if (movement?.SupplierId is int supplierId)
                OpenSupplierRequested?.Invoke(supplierId);
        });
        OpenCustomerCommand = new RelayCommand<MaintenanceSparePartMovementItem>(movement =>
        {
            if (movement?.CustomerId is int customerId)
                OpenCustomerRequested?.Invoke(customerId);
        });
        OpenOrderCommand = new RelayCommand<MaintenanceSparePartMovementItem>(movement =>
        {
            if (movement?.MaintenanceOrderId is int orderId)
                OpenOrderRequested?.Invoke(orderId);
        });

        NotifyExternalDataChanged();
    }

    /// <summary>
    /// Reloads the inventory, supplier directory and movement ledger. With SQL the rows are the
    /// source of truth; without a repository the shared design collection stays as-is.
    /// </summary>
    public void NotifyExternalDataChanged()
    {
        var editingId = editingPart?.Id;
        lastLoadFailed = false;
        Changed(nameof(ConnectionBadgeText));
        Changed(nameof(ConnectionBadgeDetail));

        if (repository is not null)
        {
            try
            {
                repository.LoadSparePartsInto(sharedSpareParts);
                LoadSupplierOptionsFromDatabase();
                LoadMovementsFromDatabase();
                if (!lastLoadFailed)
                    lastRefreshedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                lastLoadFailed = true;
                Changed(nameof(ConnectionBadgeText));
                Changed(nameof(ConnectionBadgeDetail));
                SetStatus($"تعذّر إعادة تحميل مخزون قطع الغيار: {ex.Message}", true);
            }
        }
        else
        {
            lastRefreshedAt = DateTime.Now;
        }

        if (editingId.HasValue)
        {
            editingPart = sharedSpareParts.FirstOrDefault(p => p.Id == editingId.Value);
            if (editingPart is null)
            {
                ClearFormFields();
            }
            else
            {
                foreach (var part in sharedSpareParts)
                    part.IsSelected = ReferenceEquals(part, editingPart);
            }
        }

        RebuildCategoryFilterOptions();
        RebuildSupplierFilterOptions();
        RefreshFilteredSpareParts();
        RefreshFilteredMovements();
        Changed(nameof(InventorySummaryText));
        Changed(nameof(MovementsSummaryText));
        Changed(nameof(LastRefreshDisplay));
        Changed(nameof(ConnectionBadgeText));
        Changed(nameof(ConnectionBadgeDetail));
        Changed(nameof(IsDatabaseConnected));
    }

    private void LoadSupplierOptionsFromDatabase()
    {
        if (repository is null) return;

        var currentId = SelectedSupplier?.Id;
        var suppliers = repository.LoadSparePartSuppliers();
        SupplierOptions.Clear();
        foreach (var supplier in suppliers)
            SupplierOptions.Add(supplier);

        selectedSupplier = currentId.HasValue
            ? SupplierOptions.FirstOrDefault(s => s.Id == currentId.Value)
            : null;
        Changed(nameof(SelectedSupplier));
        Changed(nameof(SelectedSupplierDisplay));
    }

    private void LoadMovements()
    {
        if (repository is null) return;
        try
        {
            var loaded = new ObservableCollection<MaintenanceSparePartMovementItem>();
            repository.LoadSparePartMovementsInto(loaded);
            allMovements.Clear();
            allMovements.AddRange(loaded);
        }
        catch (Exception ex)
        {
            lastLoadFailed = true;
            Changed(nameof(ConnectionBadgeText));
            Changed(nameof(ConnectionBadgeDetail));
            Changed(nameof(LastRefreshDisplay));
            SetStatus($"تعذّر تحميل سجل حركات قطع الغيار: {ex.Message}", true);
        }
    }

    private void LoadMovementsFromDatabase() => LoadMovements();

    private void OnSparePartsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCategoryFilterOptions();
        RebuildSupplierFilterOptions();
        RefreshFilteredSpareParts();
        Changed(nameof(InventorySummaryText));
        Changed(nameof(InventoryTabTitle));
    }

    private void SavePart()
    {
        var trimmedName = PartName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            SetStatus("أدخل اسم قطعة الغيار أولًا.", true);
            return;
        }

        if (trimmedName.Length > 250)
        {
            SetStatus("اسم قطعة الغيار أطول من 250 حرفًا.", true);
            return;
        }

        var category = IsCustomCategorySelected ? CustomCategoryName.Trim() : SelectedCategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            SetStatus("اختر الفئة أو أدخل اسم الفئة المخصصة.", true);
            return;
        }

        if (category.Length > 100)
        {
            SetStatus("اسم الفئة أطول من 100 حرف.", true);
            return;
        }

        if (!CategoryOptions.Contains(category))
        {
            var otherIndex = CategoryOptions.IndexOf("أخرى");
            if (otherIndex >= 0)
                CategoryOptions.Insert(otherIndex, category);
            else
                CategoryOptions.Add(category);
        }

        if (!PosFormatting.TryParseNonNegativeInt(QuantityText, out var qty))
        {
            SetStatus("أدخل كمية صحيحة (صفر أو أكثر).", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(CostPriceText, out var cost) || cost < 0m)
        {
            SetStatus("أدخل سعر تكلفة صحيحًا.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(SellPriceText, out var sell) || sell < 0m)
        {
            SetStatus("أدخل سعر تركيب صحيحًا للعميل.", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(MinimumStockText, out var minStock))
        {
            SetStatus("أدخل مؤشر رصيد منخفض صحيحًا (صفر أو أكثر).", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(ReorderQuantityText, out var reorderQuantity))
        {
            SetStatus("أدخل حد إعادة طلب صحيحًا (صفر أو أكثر).", true);
            return;
        }

        var barcode = BarcodeText.Trim();
        if (barcode.Length > 100)
        {
            SetStatus("الباركود أطول من 100 حرف.", true);
            return;
        }

        var unit = string.IsNullOrWhiteSpace(UnitText) ? "قطعة" : UnitText.Trim();
        var storageLocation = StorageLocationText.Trim();
        var notes = NotesText.Trim();
        if (unit.Length > 30)
        {
            SetStatus("وحدة القياس أطول من 30 حرفًا.", true);
            return;
        }
        if (storageLocation.Length > 120)
        {
            SetStatus("موقع التخزين أطول من 120 حرفًا.", true);
            return;
        }
        if (notes.Length > 1000)
        {
            SetStatus("الملاحظات أطول من 1000 حرف.", true);
            return;
        }

        var duplicateName = sharedSpareParts.FirstOrDefault(p =>
            p.Id != (editingPart?.Id ?? 0)
            && p.Name.Equals(trimmedName, StringComparison.CurrentCultureIgnoreCase));
        if (duplicateName is not null)
        {
            SetStatus($"توجد قطعة غيار أخرى مسجلة بنفس الاسم «{duplicateName.Name}» ({duplicateName.CodeDisplay}).", true);
            return;
        }

        if (barcode.Length > 0 && sharedSpareParts.Any(p =>
                p.Id != (editingPart?.Id ?? 0)
                && p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("الباركود مستخدم بالفعل لقطعة غيار أخرى.", true);
            return;
        }

        if (repository is not null)
        {
            var wasEdit = editingPart is not null;
            try
            {
                var saved = repository.SaveSparePart(
                    editingPart?.Id, trimmedName, category, qty, minStock, cost, sell,
                    barcode, unit, SelectedSupplier?.Id, reorderQuantity, storageLocation, notes);
                ClearFormFields();
                NotifyExternalDataChanged();
                notifySparePartsChanged?.Invoke();
                if (lastLoadFailed)
                {
                    SetStatus($"تم حفظ «{saved.Name}» في قاعدة البيانات، لكن تعذّر تحديث القائمة المعروضة. راجع شريط الحالة ثم اضغط «تحديث من SQL».", true);
                    return;
                }
                SetStatus(wasEdit
                    ? $"تم حفظ تعديلات قطعة الغيار «{saved.Name}» (SPR-{saved.Id:000}) ومزامنتها مع قاعدة البيانات."
                    : $"تم تسجيل قطعة الغيار «{saved.Name}» (SPR-{saved.Id:000}) بنجاح في قاعدة البيانات.", false);
                return;
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر حفظ قطعة الغيار: {ex.Message}", true);
                return;
            }
        }

        if (!CategoryOptions.Contains(category))
        {
            var otherIdx = CategoryOptions.IndexOf("أخرى");
            if (otherIdx >= 0)
                CategoryOptions.Insert(otherIdx, category);
            else
                CategoryOptions.Add(category);
        }

        var supplierName = SelectedSupplier?.Name ?? "";
        if (editingPart is not null)
        {
            var target = editingPart;
            var duplicate = sharedSpareParts.FirstOrDefault(p =>
                !ReferenceEquals(p, target)
                && p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                SetStatus($"توجد قطعة غيار أخرى مسجلة بنفس الاسم «{duplicate.Name}».", true);
                return;
            }

            if (barcode.Length > 0 && sharedSpareParts.Any(p =>
                    !ReferenceEquals(p, target)
                    && p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase)))
            {
                SetStatus("الباركود مستخدم بالفعل لقطعة غيار أخرى.", true);
                return;
            }

            var before = target.Quantity;
            target.Name = trimmedName;
            target.Category = category;
            target.Barcode = barcode;
            target.Unit = unit;
            target.SupplierId = SelectedSupplier?.Id;
            target.SupplierName = supplierName;
            target.Quantity = qty;
            target.MinimumStockAlert = minStock;
            target.ReorderQuantity = reorderQuantity;
            target.StorageLocation = storageLocation;
            target.Notes = notes;
            target.CostPrice = cost;
            target.SellPrice = sell;

            if (before != qty)
            {
                AddLocalMovement(target,
                    qty > before ? MaintenanceSparePartMovementKind.AdjustmentIncrease : MaintenanceSparePartMovementKind.AdjustmentDecrease,
                    Math.Abs(qty - before), before, qty, cost, "تعديل يدوي للرصيد من شاشة قطع الغيار.");
            }

            ClearFormFields();
            RebuildCategoryFilterOptions();
            RebuildSupplierFilterOptions();
            RefreshFilteredSpareParts();
            Changed(nameof(InventorySummaryText));
            Changed(nameof(InventoryTabTitle));
            notifySparePartsChanged?.Invoke();

            SetStatus($"تم حفظ تعديلات قطعة الغيار «{trimmedName}» ({target.CodeDisplay}) ومزامنتها مع إجراءات الصيانة.", false);
            return;
        }

        var existing = sharedSpareParts.FirstOrDefault(p =>
            p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetStatus($"قطعة الغيار «{existing.Name}» مسجلة بالفعل ({existing.CodeDisplay})؛ يمكنك الضغط على «تعديل» لتحديث كميتها أو سعرها.", true);
            return;
        }

        if (barcode.Length > 0 && sharedSpareParts.Any(p =>
                p.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("الباركود مستخدم بالفعل لقطعة غيار أخرى.", true);
            return;
        }

        var nextId = sharedSpareParts.Count == 0 ? 1 : sharedSpareParts.Max(p => p.Id) + 1;
        var newPart = new MaintenanceSparePartItem
        {
            Id = nextId,
            MinimumStockAlert = minStock,
            ReorderQuantity = reorderQuantity,
            Name = trimmedName,
            Category = category,
            Barcode = barcode,
            Unit = unit,
            SupplierId = SelectedSupplier?.Id,
            SupplierName = supplierName,
            Quantity = qty,
            StorageLocation = storageLocation,
            Notes = notes,
            CostPrice = cost,
            SellPrice = sell
        };

        sharedSpareParts.Insert(0, newPart);
        if (qty > 0)
        {
            AddLocalMovement(newPart, MaintenanceSparePartMovementKind.OpeningBalance,
                qty, 0, qty, cost, "الرصيد الافتتاحي عند تسجيل قطعة الغيار.");
        }

        ClearFormFields();
        notifySparePartsChanged?.Invoke();

        SetStatus($"تم تسجيل قطعة الغيار «{newPart.Name}» ({newPart.CodeDisplay}) بنجاح وستظهر فورًا في صرف قطع الغيار بإجراءات الصيانة.", false);
    }

    private void BeginEdit(MaintenanceSparePartItem? item)
    {
        if (item is null) return;

        foreach (var part in sharedSpareParts)
            part.IsSelected = ReferenceEquals(part, item);

        editingPart = item;
        partName = item.Name;
        if (CategoryOptions.Contains(item.Category))
        {
            selectedCategory = item.Category;
            customCategoryName = "";
        }
        else
        {
            selectedCategory = "أخرى";
            customCategoryName = item.Category;
        }
        barcodeText = item.Barcode;
        unitText = item.Unit;
        selectedSupplier = item.SupplierId is int supplierId
            ? SupplierOptions.FirstOrDefault(s => s.Id == supplierId)
            : null;
        quantityText = item.Quantity.ToString();
        minimumStockText = item.MinimumStockAlert.ToString();
        reorderQuantityText = item.ReorderQuantity.ToString();
        storageLocationText = item.StorageLocation;
        notesText = item.Notes;
        costPriceText = PosFormatting.FormatPlainNumber(item.CostPrice);
        sellPriceText = PosFormatting.FormatPlainNumber(item.SellPrice);
        ActiveWorkspaceTabIndex = 0;

        NotifyFormChanged();
        SetStatus($"وضع التعديل: جاري تعديل «{item.Name}» ({item.CodeDisplay}).", false);
    }

    private void CancelEdit()
    {
        ClearFormFields();
        SetStatus("تم إلغاء التعديل والعودة إلى نموذج قطعة غيار جديدة.", false);
    }

    private void ClearFormFields()
    {
        foreach (var part in sharedSpareParts)
            part.IsSelected = false;

        editingPart = null;
        partName = "";
        selectedCategory = CategoryOptions.FirstOrDefault() ?? "شاشات";
        customCategoryName = "";
        barcodeText = "";
        unitText = "قطعة";
        selectedSupplier = null;
        quantityText = "1";
        minimumStockText = "3";
        reorderQuantityText = "0";
        storageLocationText = "";
        notesText = "";
        costPriceText = "0";
        sellPriceText = "0";

        NotifyFormChanged();
    }

    private void NotifyFormChanged()
    {
        Changed(nameof(IsEditMode));
        Changed(nameof(IsAddMode));
        Changed(nameof(FormHeaderTitle));
        Changed(nameof(SaveButtonText));
        Changed(nameof(PartName));
        Changed(nameof(SelectedCategory));
        Changed(nameof(IsCustomCategorySelected));
        Changed(nameof(CustomCategoryName));
        Changed(nameof(BarcodeText));
        Changed(nameof(UnitText));
        Changed(nameof(SelectedSupplier));
        Changed(nameof(SelectedSupplierDisplay));
        Changed(nameof(QuantityText));
        Changed(nameof(MinimumStockText));
        Changed(nameof(ReorderQuantityText));
        Changed(nameof(StorageLocationText));
        Changed(nameof(NotesText));
        Changed(nameof(CostPriceText));
        Changed(nameof(SellPriceText));
        Changed(nameof(PreviewProfitDisplay));
        Changed(nameof(IsPreviewProfitNegative));
    }

    private void RequestDelete(MaintenanceSparePartItem? item)
    {
        if (item is null) return;

        var confirmVm = new ConfirmActionViewModel(
            "تأكيد حذف قطعة الغيار",
            $"{item.Name} ({item.CodeDisplay})",
            $"هل تريد حذف قطعة الغيار «{item.Name}» من مخزون قطع غيار الصيانة؟ إذا كانت مستخدمة في أمر صيانة أو لها حركات مخزون أو ما زال لها رصيد فسيتم إيقافها (إخفاؤها) بدل حذف السجل.",
            "حذف القطعة");

        if (OpenConfirmDialogRequested is not null)
            OpenConfirmDialogRequested(confirmVm, () => DeleteConfirmed(item));
        else
            DeleteConfirmed(item);
    }

    private void DeleteConfirmed(MaintenanceSparePartItem item)
    {
        if (ReferenceEquals(editingPart, item))
            ClearFormFields();

        if (repository is not null)
        {
            try
            {
                var (_, message) = repository.DeleteSparePart(item.Id, item.Name);
                NotifyExternalDataChanged();
                notifySparePartsChanged?.Invoke();
                SetStatus(lastLoadFailed
                    ? $"{message} لكن تعذّر تحديث القائمة المعروضة؛ اضغط «تحديث من SQL»."
                    : message, lastLoadFailed);
                return;
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر حذف قطعة الغيار: {ex.Message}", true);
                return;
            }
        }

        if (!sharedSpareParts.Remove(item))
            return;

        allMovements.RemoveAll(m => m.SparePartId == item.Id);
        notifySparePartsChanged?.Invoke();
        RefreshFilteredMovements();
        Changed(nameof(MovementsSummaryText));
        SetStatus($"تم حذف قطعة الغيار «{item.Name}» من مخزون الصيانة.", false);
    }

    private void RequestRestock(MaintenanceSparePartItem? item)
    {
        if (item is null) return;

        var dialog = new MaintenanceSparePartRestockViewModel(item, SupplierOptions);
        var result = OpenRestockDialogRequested?.Invoke(dialog);
        if (result != true && !dialog.IsConfirmed)
        {
            if (result is null && OpenRestockDialogRequested is null)
                SetStatus("نافذة التوريد غير متاحة في هذه البيئة.", true);
            return;
        }

        if (repository is not null)
        {
            try
            {
                var saved = repository.RestockSparePart(
                    item.Id, dialog.Quantity, dialog.UnitCost, dialog.SupplierId,
                    dialog.ReferenceText, dialog.NoteText);
                NotifyExternalDataChanged();
                notifySparePartsChanged?.Invoke();
                if (lastLoadFailed)
                {
                    SetStatus($"تم توريد {dialog.Quantity} {saved.Unit} من «{saved.Name}» في قاعدة البيانات، لكن تعذّر تحديث القائمة. اضغط «تحديث من SQL».", true);
                    return;
                }
                SetStatus($"تم توريد {dialog.Quantity} {saved.Unit} من «{saved.Name}» وأصبح الرصيد {saved.Quantity}، وسُجلت الحركة في سجل التوريد.", false);
                return;
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر توريد قطعة الغيار: {ex.Message}", true);
                return;
            }
        }

        var before = item.Quantity;
        item.Quantity = before + dialog.Quantity;
        if (dialog.UnitCost > 0m)
            item.CostPrice = dialog.UnitCost;
        if (dialog.SupplierId is int supplierId)
        {
            item.SupplierId = supplierId;
            item.SupplierName = SupplierOptions.FirstOrDefault(s => s.Id == supplierId)?.Name ?? item.SupplierName;
        }
        item.LastMovementAt = DateTime.Now;

        AddLocalMovement(item, MaintenanceSparePartMovementKind.Purchase,
            dialog.Quantity, before, item.Quantity, dialog.UnitCost,
            string.IsNullOrWhiteSpace(dialog.NoteText) ? "توريد قطعة غيار من المورد." : dialog.NoteText,
            dialog.ReferenceText);

        RefreshFilteredSpareParts();
        RefreshFilteredMovements();
        Changed(nameof(InventorySummaryText));
        notifySparePartsChanged?.Invoke();
        SetStatus($"تم توريد {dialog.Quantity} {item.Unit} من «{item.Name}» وأصبح الرصيد {item.Quantity}.", false);
    }

    private void AddLocalMovement(
        MaintenanceSparePartItem part,
        MaintenanceSparePartMovementKind kind,
        int quantity,
        int before,
        int after,
        decimal unitCost,
        string note,
        string reference = "")
    {
        var supplierId = part.SupplierId;
        var supplierName = part.SupplierName;
        var id = allMovements.Count == 0 ? 1 : allMovements.Max(m => m.Id) + 1;
        allMovements.Insert(0, new MaintenanceSparePartMovementItem
        {
            Id = id,
            SparePartId = part.Id,
            SparePartName = part.Name,
            SparePartCode = part.CodeDisplay,
            Kind = kind,
            Quantity = quantity,
            QuantityBefore = before,
            QuantityAfter = after,
            UnitCost = unitCost,
            SupplierId = supplierId,
            SupplierName = supplierName,
            Reference = reference,
            Note = note,
            ActorName = "المستخدم الحالي",
            OccurredAt = DateTime.Now
        });

        part.LastMovementAt = DateTime.Now;
        Changed(nameof(MovementsSummaryText));
        Changed(nameof(MovementsTabTitle));
        RefreshFilteredMovements();
    }

    private void RebuildCategoryFilterOptions()
    {
        var previous = SelectedCategoryFilter;
        var categories = CategoryOptions
            .Where(c => c != "أخرى")
            .Concat(sharedSpareParts.Select(p => p.Category))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var cat in categories)
        {
            if (CategoryOptions.Contains(cat)) continue;
            var otherIndex = CategoryOptions.IndexOf("أخرى");
            if (otherIndex >= 0)
                CategoryOptions.Insert(otherIndex, cat);
            else
                CategoryOptions.Add(cat);
        }

        CategoryFilterOptions.Clear();
        CategoryFilterOptions.Add("كل الفئات");
        foreach (var cat in categories)
            CategoryFilterOptions.Add(cat);

        selectedCategoryFilter = CategoryFilterOptions.Contains(previous) ? previous : "كل الفئات";
        Changed(nameof(SelectedCategoryFilter));
    }

    private void RebuildSupplierFilterOptions()
    {
        var previous = SelectedSupplierFilter;
        var suppliers = SupplierOptions.Select(s => s.Name)
            .Concat(sharedSpareParts.Select(p => p.SupplierName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        SupplierFilterOptions.Clear();
        SupplierFilterOptions.Add("كل الموردين");
        SupplierFilterOptions.Add("بدون مورد");
        foreach (var supplier in suppliers)
            SupplierFilterOptions.Add(supplier);

        selectedSupplierFilter = SupplierFilterOptions.Contains(previous) ? previous : "كل الموردين";
        Changed(nameof(SelectedSupplierFilter));
    }

    private void RefreshFilteredSpareParts()
    {
        var query = SearchText.Trim();
        FilteredSpareParts.Clear();

        foreach (var part in sharedSpareParts)
        {
            if (SelectedCategoryFilter != "كل الفئات"
                && !string.Equals(part.Category, SelectedCategoryFilter, StringComparison.Ordinal))
            {
                continue;
            }

            if (SelectedSupplierFilter == "بدون مورد")
            {
                if (part.HasSupplier) continue;
            }
            else if (SelectedSupplierFilter != "كل الموردين"
                && !string.Equals(part.SupplierName, SelectedSupplierFilter, StringComparison.CurrentCulture))
            {
                continue;
            }

            if (SelectedStockFilter == "متاح" && (part.IsOutOfStock || part.IsLowStock)) continue;
            if (SelectedStockFilter == "رصيد منخفض" && !part.IsLowStock) continue;
            if (SelectedStockFilter == "نفد" && !part.IsOutOfStock) continue;

            if (!string.IsNullOrEmpty(query))
            {
                var hit = part.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.CodeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.SupplierName.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.StorageLocation.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.Notes.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            FilteredSpareParts.Add(part);
        }

        Changed(nameof(HasNoFilteredSpareParts));
        Changed(nameof(InventoryTabTitle));
    }

    private void RefreshFilteredMovements()
    {
        var query = MovementSearchText.Trim();
        FilteredMovements.Clear();

        foreach (var movement in allMovements)
        {
            if (!MatchesMovementKindFilter(movement)) continue;
            if (!movement.Matches(query)) continue;
            FilteredMovements.Add(movement);
        }

        Changed(nameof(HasNoFilteredMovements));
        Changed(nameof(MovementsTabTitle));
        Changed(nameof(MovementsSummaryText));
    }

    private bool MatchesMovementKindFilter(MaintenanceSparePartMovementItem movement) =>
        SelectedMovementKindFilter switch
        {
            "توريد" => movement.Kind == MaintenanceSparePartMovementKind.Purchase,
            "صرف" => movement.Kind == MaintenanceSparePartMovementKind.Issue,
            "إرجاع" => movement.Kind == MaintenanceSparePartMovementKind.Return,
            "تسويات" => movement.Kind is MaintenanceSparePartMovementKind.AdjustmentIncrease
                or MaintenanceSparePartMovementKind.AdjustmentDecrease,
            "رصيد افتتاحي" => movement.Kind == MaintenanceSparePartMovementKind.OpeningBalance,
            _ => true
        };

    /// <summary>
    /// Opens a part that was selected on another screen (for example the supplier profile) and
    /// switches back to the inventory tab so the form is visible immediately.
    /// </summary>
    public void SelectPartFromExternal(int partId)
    {
        searchText = "";
        selectedCategoryFilter = "كل الفئات";
        selectedSupplierFilter = "كل الموردين";
        selectedStockFilter = "كل حالات الرصيد";
        Changed(nameof(SearchText));
        Changed(nameof(SelectedCategoryFilter));
        Changed(nameof(SelectedSupplierFilter));
        Changed(nameof(SelectedStockFilter));
        ActiveWorkspaceTabIndex = 0;
        RefreshFilteredSpareParts();

        var part = sharedSpareParts.FirstOrDefault(p => p.Id == partId);
        if (part is null)
        {
            SetStatus($"لم يتم العثور على قطعة الغيار رقم {partId} في المخزون النشط.", true);
            return;
        }

        BeginEdit(part);
        SetStatus($"تم فتح قطعة الغيار «{part.Name}» ({part.CodeDisplay}) من الشاشة المرتبطة.", false);
    }

    /// <summary>Shows feedback when a cross-screen link cannot be resolved (for example a deleted order).</summary>
    public void ReportExternalLinkFailure(string message) => SetStatus(message, true);

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
