using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PurchaseEntryViewModel : ObservableObject
{
    private readonly ObservableCollection<string>? sharedCategories;
    private readonly ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations;
    private readonly ObservableCollection<PurchaseInvoiceDesignItem> sharedPurchaseInvoices;
    private readonly Action? navigateToAddProduct;
    private readonly Action? notifyInventoryChanged;
    private readonly Action? notifyReturnExchangeChanged;
    /// <summary>Raised when the SQL supplier directory changed so the supplier screen reloads it.</summary>
    private readonly Action? notifySuppliersChanged;
    private readonly PurchaseRepository? repository;

    private int nextDraftNumber = 1;
    private int nextSupplierId = 100;
    private int nextPurchaseInvoiceId;

    private PurchaseDraftInvoiceViewModel activeInvoice = null!;
    private PurchaseInvoiceDesignItem? lastSavedInvoice;
    private string productSearchText = "";
    private string selectedCategoryFilter = "الكل";
    private string statusMessage = "اختر المورد وأضف الأصناف مع تحديد سعر الشراء والكمية، وقسّم الدفع بين كاش وفودافون كاش وإنستاباي أو الآجل.";
    private bool isErrorStatus;

    public string BuyerName { get; }
    public ObservableCollection<PosCategoryFilterViewModel> CategoryFilters { get; } = [];
    public ObservableCollection<PosProductDesignItem> Catalog { get; }
    public ObservableCollection<PosProductDesignItem> FilteredCatalog { get; } = [];
    public ObservableCollection<SupplierDesignItem> Suppliers { get; }
    public ObservableCollection<PurchaseDraftInvoiceViewModel> Invoices { get; } = [];

    public PurchaseDraftInvoiceViewModel ActiveInvoice
    {
        get => activeInvoice;
        private set
        {
            if (ReferenceEquals(activeInvoice, value)) return;
            if (activeInvoice is not null)
                activeInvoice.IsActive = false;
            activeInvoice = value;
            activeInvoice.IsActive = true;
            Changed();
        }
    }

    public PurchaseInvoiceDesignItem? LastSavedInvoice
    {
        get => lastSavedInvoice;
        private set
        {
            if (ReferenceEquals(lastSavedInvoice, value)) return;
            lastSavedInvoice = value;
            Changed();
            Changed(nameof(HasLastSavedInvoice));
            Changed(nameof(LastSavedInvoiceButtonLabel));
        }
    }

    public bool HasLastSavedInvoice => LastSavedInvoice is not null;
    public string LastSavedInvoiceButtonLabel => LastSavedInvoice is null
        ? "معاينة آخر فاتورة"
        : $"معاينة آخر فاتورة ({LastSavedInvoice.InvoiceNumber})";

    public string ProductSearchText
    {
        get => productSearchText;
        set
        {
            if (productSearchText == value) return;
            productSearchText = value;
            Changed();
            RefreshFilteredCatalog();
        }
    }

    public string SelectedCategoryFilter
    {
        get => selectedCategoryFilter;
        set
        {
            if (selectedCategoryFilter == value) return;
            selectedCategoryFilter = value;
            foreach (var filter in CategoryFilters)
                filter.SetSelected(filter.Title == selectedCategoryFilter);
            Changed();
            RefreshFilteredCatalog();
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
    public bool HasNoFilteredProducts => FilteredCatalog.Count == 0;

    public Action<string?, Func<string, bool>, Action<SupplierDesignItem>>? OpenAddSupplierRequested { get; set; }
    public Action<PurchaseInvoicePrintPreviewViewModel>? OpenPrintPreviewRequested { get; set; }
    public Action? FocusBarcodeInputRequested { get; set; }

    public ActionCommand NewInvoiceCommand { get; }
    public RelayCommand<PurchaseDraftInvoiceViewModel> SelectInvoiceCommand { get; }
    public RelayCommand<PurchaseDraftInvoiceViewModel> CloseInvoiceCommand { get; }

    public ActionCommand ScanOrSearchEnterCommand { get; }
    public ActionCommand ClearProductSearchCommand { get; }
    public RelayCommand<PosCategoryFilterViewModel> SelectCategoryFilterCommand { get; }
    public RelayCommand<PosProductDesignItem> AddProductCommand { get; }
    public ActionCommand GoToAddProductCommand { get; }

    public ActionCommand OpenAddSupplierWindowCommand { get; }
    public ActionCommand CompletePurchaseInvoiceCommand { get; }
    public ActionCommand CompleteAndPrintPurchaseInvoiceCommand { get; }
    public ActionCommand PreviewDraftCommand { get; }
    public ActionCommand PreviewLastSavedInvoiceCommand { get; }
    public ActionCommand FocusBarcodeCommand { get; }

    public PurchaseEntryViewModel()
        : this(
            "المشرف",
            PosDesignSeedData.CreateSharedSampleCatalog(),
            PurchaseDesignSeedData.CreateSharedSampleSuppliers(),
            null,
            null,
            null,
            null,
            null,
            null)
    {
    }

    public PurchaseEntryViewModel(
        string buyerName,
        ObservableCollection<PosProductDesignItem> sharedCatalog,
        ObservableCollection<SupplierDesignItem> sharedSuppliers,
        ObservableCollection<InventoryUnitRegistrationItem>? sharedRegistrations = null,
        ObservableCollection<PurchaseInvoiceDesignItem>? sharedPurchaseInvoices = null,
        ObservableCollection<string>? sharedCategories = null,
        Action? navigateToAddProduct = null,
        Action? notifyInventoryChanged = null,
        Action? notifyReturnExchangeChanged = null,
        PurchaseRepository? repository = null,
        Action? notifySuppliersChanged = null)
    {
        BuyerName = buyerName;
        Catalog = sharedCatalog;
        Suppliers = sharedSuppliers;
        this.sharedRegistrations = sharedRegistrations ?? InventoryDesignSeedData.CreateSharedSampleRegistrations(sharedCatalog);
        this.sharedPurchaseInvoices = sharedPurchaseInvoices ?? PurchaseDesignSeedData.CreateSharedSamplePurchaseInvoices(sharedCatalog, this.sharedRegistrations, sharedSuppliers);
        this.sharedCategories = sharedCategories;
        this.navigateToAddProduct = navigateToAddProduct;
        this.notifyInventoryChanged = notifyInventoryChanged;
        this.notifyReturnExchangeChanged = notifyReturnExchangeChanged;
        this.repository = repository;
        this.notifySuppliersChanged = notifySuppliersChanged;

        nextSupplierId = Suppliers.Count > 0 ? Suppliers.Max(s => s.Id) + 1 : 100;
        nextPurchaseInvoiceId = this.sharedPurchaseInvoices.Count > 0 ? this.sharedPurchaseInvoices.Max(i => i.Id) + 1 : 1;

        RebuildCategoryFilters();
        Catalog.CollectionChanged += OnCatalogCollectionChanged;
        if (this.sharedCategories is not null)
            this.sharedCategories.CollectionChanged += OnCategoriesCollectionChanged;

        NewInvoiceCommand = new ActionCommand(CreateAndSelectNewDraft);
        SelectInvoiceCommand = new RelayCommand<PurchaseDraftInvoiceViewModel>(inv =>
        {
            if (inv is not null && Invoices.Contains(inv))
                ActiveInvoice = inv;
        });
        CloseInvoiceCommand = new RelayCommand<PurchaseDraftInvoiceViewModel>(CloseDraft);

        ScanOrSearchEnterCommand = new ActionCommand(HandleScanOrSearchEnter);
        ClearProductSearchCommand = new ActionCommand(() => ProductSearchText = "");
        SelectCategoryFilterCommand = new RelayCommand<PosCategoryFilterViewModel>(filter =>
        {
            if (filter is not null)
                SelectedCategoryFilter = filter.Title;
        });
        AddProductCommand = new RelayCommand<PosProductDesignItem>(AddProductToActiveDraft);
        GoToAddProductCommand = new ActionCommand(() => this.navigateToAddProduct?.Invoke());

        OpenAddSupplierWindowCommand = new ActionCommand(RequestOpenAddSupplier);
        CompletePurchaseInvoiceCommand = new ActionCommand(() => SavePurchaseInvoice(openPrintAfter: false));
        CompleteAndPrintPurchaseInvoiceCommand = new ActionCommand(() => SavePurchaseInvoice(openPrintAfter: true));
        PreviewDraftCommand = new ActionCommand(PreviewActiveDraft);
        PreviewLastSavedInvoiceCommand = new ActionCommand(PreviewLastSaved);
        FocusBarcodeCommand = new ActionCommand(() => FocusBarcodeInputRequested?.Invoke());

        RefreshFilteredCatalog();
        CreateAndSelectNewDraft();
    }

    /// <summary>True when the screen reads and writes SQL Server instead of the design sample data.</summary>
    public bool IsConnectedToDatabase => repository is not null;

    public string DataSourceLabel => repository is null
        ? "معاينة تصميم — بدون قاعدة بيانات"
        : "متصل بقاعدة البيانات — SQL Server";

    public string SupplierSourceLabel => repository is null
        ? "الموردون التجريبيون (بدون قاعدة بيانات)"
        : $"الموردون من dbo.Suppliers ({Suppliers.Count.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>Reloads the supplier directory from SQL so the picker never shows a stale row.</summary>
    public bool RefreshSuppliersFromDatabase()
    {
        if (repository is null) return false;

        try
        {
            var selectedId = ActiveInvoice.SelectedSupplier?.Id ?? 0;
            repository.LoadSuppliersInto(Suppliers);

            if (selectedId > 0)
            {
                var restored = Suppliers.FirstOrDefault(s => s.Id == selectedId);
                if (restored is not null)
                {
                    ActiveInvoice.SelectSupplier(restored);
                }
                else
                {
                    ActiveInvoice.ClearSupplierSelection();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر تحميل دليل الموردين من قاعدة البيانات.", true);
            return false;
        }
    }

    public void NotifyExternalCatalogChanged()
    {
        RebuildCategoryFilters();
        RefreshFilteredCatalog();
        if (LastSavedInvoice is not null && !sharedPurchaseInvoices.Contains(LastSavedInvoice))
            LastSavedInvoice = sharedPurchaseInvoices.FirstOrDefault();
    }

    public void StartNewPurchaseForSupplier(SupplierDesignItem supplier)
    {
        // A SQL reload replaces the supplier instances, so the id is the stable identity.
        var tracked = Suppliers.FirstOrDefault(s => ReferenceEquals(s, supplier))
                      ?? Suppliers.FirstOrDefault(s => s.Id == supplier.Id);
        if (tracked is null) return;

        CreateAndSelectNewDraft();
        ActiveInvoice.SelectSupplier(tracked);
        SetStatus($"تم فتح فاتورة شراء جديدة للمورد «{tracked.Name}». أضف الأصناف ثم حدد طريقة السداد.", false);
        FocusBarcodeInputRequested?.Invoke();
    }

    private void OnCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCategoryFilters();
        RefreshFilteredCatalog();
    }

    private void OnCategoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildCategoryFilters();

    private void RebuildCategoryFilters()
    {
        var activeTitle = SelectedCategoryFilter;
        IEnumerable<string> categorySource = sharedCategories is not null
            ? sharedCategories
            : PosDesignSeedData.CategoryFilters.Where(c => c != "الكل");

        var titles = new List<string> { "الكل" };
        foreach (var cat in categorySource.Concat(Catalog.Select(p => p.Category)))
        {
            if (!string.IsNullOrWhiteSpace(cat) && !titles.Contains(cat))
                titles.Add(cat);
        }

        CategoryFilters.Clear();
        foreach (var title in titles)
            CategoryFilters.Add(new PosCategoryFilterViewModel(title, title == activeTitle));

        if (!titles.Contains(activeTitle))
            SelectedCategoryFilter = "الكل";
    }

    private void CreateAndSelectNewDraft()
    {
        var draft = new PurchaseDraftInvoiceViewModel(nextDraftNumber++, Suppliers, SetStatus);
        Invoices.Add(draft);
        ActiveInvoice = draft;
        SetStatus($"تم فتح مسودة فاتورة شراء جديدة (#{draft.DraftNumber}).", false);
    }

    private void CloseDraft(PurchaseDraftInvoiceViewModel? draft)
    {
        if (draft is null) return;

        if (Invoices.Count == 1)
        {
            draft.ClearInvoice();
            return;
        }

        var idx = Invoices.IndexOf(draft);
        if (idx < 0) return;

        var wasActive = ReferenceEquals(ActiveInvoice, draft);
        draft.Detach();
        Invoices.RemoveAt(idx);

        if (wasActive && Invoices.Count > 0)
            ActiveInvoice = Invoices[Math.Min(idx, Invoices.Count - 1)];

        SetStatus($"تم إغلاق {draft.TabTitle}.", false);
    }

    private void HandleScanOrSearchEnter()
    {
        var query = ProductSearchText.Trim();
        if (string.IsNullOrEmpty(query)) return;

        var exactBarcode = Catalog.FirstOrDefault(p =>
            p.Barcode.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (exactBarcode is not null)
        {
            AddProductToActiveDraft(exactBarcode);
            ProductSearchText = "";
            FocusBarcodeInputRequested?.Invoke();
            return;
        }

        if (FilteredCatalog.Count == 1)
        {
            AddProductToActiveDraft(FilteredCatalog[0]);
            ProductSearchText = "";
            FocusBarcodeInputRequested?.Invoke();
            return;
        }

        if (FilteredCatalog.Count == 0)
            SetStatus($"لا يوجد منتج مسجل بالباركود أو الاسم «{query}». يمكنك إضافته أولاً من شاشة إضافة منتج.", true);
    }

    private void AddProductToActiveDraft(PosProductDesignItem? product)
    {
        if (product is null) return;

        ActiveInvoice.AddProduct(product, 1);
        SetStatus($"تمت إضافة «{product.Name}» إلى {ActiveInvoice.TabTitle}.", false);
    }

    private void RequestOpenAddSupplier()
    {
        var initialQuery = ActiveInvoice.SupplierSearchText;
        OpenAddSupplierRequested?.Invoke(
            initialQuery,
            name => repository is not null
                ? repository.LoadSupplierNames().Contains(name)
                : Suppliers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)),
            newSupplier =>
            {
                if (repository is not null)
                {
                    SupplierDesignItem createdFromSql;
                    try
                    {
                        createdFromSql = repository.CreateSupplier(
                            newSupplier.Name,
                            newSupplier.Phone,
                            newSupplier.SecondaryPhone,
                            newSupplier.CompanyName,
                            newSupplier.Specialization,
                            newSupplier.Address,
                            newSupplier.VodafoneCashNumber,
                            newSupplier.InstaPayAddress,
                            newSupplier.Notes);
                    }
                    catch (Exception ex)
                    {
                        SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر حفظ المورد في قاعدة البيانات.", true);
                        return;
                    }

                    RefreshSuppliersFromDatabase();
                    var tracked = Suppliers.FirstOrDefault(s => s.Id == createdFromSql.Id) ?? createdFromSql;
                    ActiveInvoice.SelectSupplier(tracked);
                    notifySuppliersChanged?.Invoke();
                    SetStatus($"تم حفظ المورد «{tracked.Name}» ({tracked.SupplierCodeDisplay}) في قاعدة البيانات وربطه بفاتورة الشراء الحالية.", false);
                    return;
                }

                var assigned = new SupplierDesignItem
                {
                    Id = nextSupplierId++,
                    Name = newSupplier.Name,
                    Phone = newSupplier.Phone,
                    SecondaryPhone = newSupplier.SecondaryPhone,
                    CompanyName = newSupplier.CompanyName,
                    Specialization = newSupplier.Specialization,
                    Address = newSupplier.Address,
                    VodafoneCashNumber = newSupplier.VodafoneCashNumber,
                    InstaPayAddress = newSupplier.InstaPayAddress,
                    Notes = newSupplier.Notes
                };

                Suppliers.Add(assigned);
                ActiveInvoice.SelectSupplier(assigned);
                SetStatus($"تمت إضافة المورد «{assigned.Name}» وربطه بفاتورة الشراء الحالية.", false);
            });
    }

    private void PreviewActiveDraft()
    {
        if (ActiveInvoice.IsEmpty)
        {
            SetStatus("أضف صنفًا واحدًا على الأقل لمعاينة فاتورة الشراء.", true);
            return;
        }

        var snapshot = ActiveInvoice.BuildPreviewSnapshot(BuyerName, $"مسودة #{ActiveInvoice.DraftNumber}");
        OpenPrintPreviewRequested?.Invoke(new PurchaseInvoicePrintPreviewViewModel(snapshot));
    }

    private void PreviewLastSaved()
    {
        if (LastSavedInvoice is null) return;
        OpenPrintPreviewRequested?.Invoke(new PurchaseInvoicePrintPreviewViewModel(LastSavedInvoice));
    }

    private void SavePurchaseInvoice(bool openPrintAfter)
    {
        if (ActiveInvoice.SelectedSupplier is null)
        {
            ActiveInvoice.IsSupplierPickerOpen = true;
            SetStatus("يرجى اختيار المورد أو إضافة مورد جديد لربط فاتورة الشراء والمنتجات به.", true);
            return;
        }

        if (ActiveInvoice.IsEmpty)
        {
            SetStatus("لا يمكن حفظ فاتورة شراء فارغة — أضف صنفًا واحدًا على الأقل.", true);
            return;
        }

        foreach (var line in ActiveInvoice.Items)
        {
            if (line.Quantity <= 0)
            {
                SetStatus($"حدد كمية صحيحة لصنف «{line.Product.Name}».", true);
                return;
            }

            if (line.UnitPurchaseCost <= 0m)
            {
                SetStatus($"يرجى إدخال سعر الشراء للقطعة لصنف «{line.Product.Name}».", true);
                return;
            }
        }

        if (ActiveInvoice.HasOverpayment)
        {
            SetStatus($"إجمالي المدفوع ({ActiveInvoice.TotalPaidDisplay}) أكبر من صافي الفاتورة ({ActiveInvoice.NetTotalDisplay}) بزيادة {ActiveInvoice.OverpaidAmountDisplay}.", true);
            return;
        }

        var supplier = ActiveInvoice.SelectedSupplier;

        if (repository is not null)
        {
            SavePurchaseInvoiceToDatabase(openPrintAfter);
            return;
        }

        var maxExistingId = sharedPurchaseInvoices.Count > 0 ? sharedPurchaseInvoices.Max(i => i.Id) : 0;
        var invoiceId = Math.Max(nextPurchaseInvoiceId, maxExistingId + 1);
        nextPurchaseInvoiceId = invoiceId + 1;
        var invoiceNo = $"PUR-2026-{invoiceId:0000}";
        var issuedAt = ActiveInvoice.PurchaseDate.Date.Add(DateTime.Now.TimeOfDay);
        var nextRegId = sharedRegistrations.Count > 0 ? sharedRegistrations.Max(r => r.Id) + 1 : 6001;

        var status = ActiveInvoice.TotalPaid <= 0m && ActiveInvoice.NetTotal > 0m
            ? PurchaseInvoiceStatus.FullCredit
            : ActiveInvoice.RemainingPayable > 0m
                ? PurchaseInvoiceStatus.PartialCredit
                : PurchaseInvoiceStatus.Paid;

        var savedInvoice = new PurchaseInvoiceDesignItem
        {
            Id = invoiceId,
            InvoiceNumber = invoiceNo,
            SupplierInvoiceNumber = ActiveInvoice.SupplierInvoiceNumber.Trim(),
            IssuedAt = issuedAt,
            BuyerName = BuyerName,
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            SupplierPhone = supplier.Phone,
            SupplierCompany = supplier.CompanyName,
            SupplierAddress = supplier.Address,
            Status = status,
            DiscountAmount = ActiveInvoice.EffectiveDiscount,
            CashPaid = ActiveInvoice.CashAmount,
            VodafoneCashPaid = ActiveInvoice.VodafoneCashAmount,
            InstaPayPaid = ActiveInvoice.InstaPayAmount,
            Notes = ActiveInvoice.Notes.Trim()
        };

        var totalUnitsImported = 0;
        var lineIdx = 1;
        foreach (var draftLine in ActiveInvoice.Items)
        {
            var claimedIds = new List<int>(draftLine.Quantity);
            var serials = new List<string>();

            for (var i = 0; i < draftLine.Quantity; i++)
            {
                var regId = nextRegId++;
                var serial = draftLine.Product.TrackSerialNumber
                    ? $"SN-{invoiceId:000}-{regId}"
                    : "";

                sharedRegistrations.Add(new InventoryUnitRegistrationItem
                {
                    Id = regId,
                    ProductId = draftLine.Product.Id,
                    ProductName = draftLine.Product.Name,
                    ProductBarcode = draftLine.Product.Barcode,
                    PurchasePrice = draftLine.UnitPurchaseCost,
                    SupplierName = supplier.Name,
                    RegisteredAt = issuedAt.Date,
                    Status = InventoryUnitStatus.Available,
                    PurchaseInvoiceId = invoiceNo,
                    SerialNumber = serial
                });

                claimedIds.Add(regId);
                if (!string.IsNullOrEmpty(serial))
                    serials.Add(serial);
            }

            draftLine.Product.AvailableStock = sharedRegistrations.Count(r =>
                r.ProductId == draftLine.Product.Id && r.IsAvailable);
            draftLine.Product.CostPrice = draftLine.UnitPurchaseCost;

            savedInvoice.Items.Add(new PurchaseInvoiceLineDesignItem
            {
                LineNumber = lineIdx++,
                ProductId = draftLine.Product.Id,
                Barcode = draftLine.Product.Barcode,
                ProductName = draftLine.Product.Name,
                Category = draftLine.Product.Category,
                Unit = draftLine.Product.UnitDisplay,
                TrackSerialNumber = draftLine.Product.TrackSerialNumber,
                UnitRegistrationIds = claimedIds,
                SerialNumbers = serials,
                Quantity = draftLine.Quantity,
                UnitPurchaseCost = draftLine.UnitPurchaseCost
            });

            totalUnitsImported += draftLine.Quantity;
        }

        supplier.TotalPurchasesCount++;
        supplier.OutstandingPayable += ActiveInvoice.RemainingPayable;

        savedInvoice.ActivityLog.Add(new PurchaseInvoiceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "اعتماد فاتورة شراء وتوريد للمخزون",
            Details = $"توريد {totalUnitsImported} قطعة من المورد «{supplier.Name}» — الصافي {savedInvoice.NetTotalDisplay} · {savedInvoice.PaymentMethodSummary}.",
            ActorName = BuyerName,
            Amount = savedInvoice.TotalPaid
        });

        sharedPurchaseInvoices.Insert(0, savedInvoice);
        LastSavedInvoice = savedInvoice;

        notifyInventoryChanged?.Invoke();
        notifyReturnExchangeChanged?.Invoke();

        var creditSuffix = savedInvoice.HasRemainingPayable
            ? $" | متبقي آجل للمورد: {savedInvoice.RemainingPayableDisplay}"
            : "";

        ActiveInvoice.ClearInvoice();
        SetStatus(
            $"تم حفظ فاتورة الشراء {invoiceNo} للمورد «{supplier.Name}» وتوريد {totalUnitsImported} قطعة للمخزون ({savedInvoice.PaymentMethodSummary}{creditSuffix}).",
            false);

        if (openPrintAfter)
            OpenPrintPreviewRequested?.Invoke(new PurchaseInvoicePrintPreviewViewModel(savedInvoice));
    }

    private void RefreshFilteredCatalog()
    {
        var query = ProductSearchText.Trim();
        var matches = Catalog.Where(p =>
        {
            if (SelectedCategoryFilter != "الكل" && p.Category != SelectedCategoryFilter)
                return false;

            if (string.IsNullOrEmpty(query))
                return true;

            return p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || p.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
        });

        FilteredCatalog.Clear();
        foreach (var item in matches)
            FilteredCatalog.Add(item);

        Changed(nameof(HasNoFilteredProducts));
    }

    /// <summary>
    /// Persists the active draft through PurchaseRepository: header, lines, stock units, payments and
    /// the activity trail are written in one transaction, then every screen is re-read from SQL.
    /// </summary>
    private void SavePurchaseInvoiceToDatabase(bool openPrintAfter)
    {
        if (repository is null) return;

        var supplier = ActiveInvoice.SelectedSupplier!;
        var request = new PurchaseInvoiceRequest(
            supplier.Id,
            ActiveInvoice.SupplierInvoiceNumber.Trim(),
            ActiveInvoice.PurchaseDate.Date.Add(DateTime.Now.TimeOfDay),
            ActiveInvoice.EffectiveDiscount,
            ActiveInvoice.Items.Select(item => new PurchaseLineRequest(
                item.Product.Id,
                item.Product.Barcode,
                item.Product.Name,
                item.Product.Category,
                item.Product.UnitDisplay,
                item.Product.TrackSerialNumber,
                item.Quantity,
                item.UnitPurchaseCost)).ToList(),
            ActiveInvoice.CashAmount,
            ActiveInvoice.VodafoneCashAmount,
            ActiveInvoice.InstaPayAmount,
            ActiveInvoice.Notes.Trim());

        SavedPurchase saved;
        try
        {
            saved = repository.CreateInvoice(request);
        }
        catch (Exception ex)
        {
            SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر حفظ فاتورة الشراء في قاعدة البيانات.", true);
            return;
        }

        // Re-read from SQL: the invoice rows, the stock units and the cash balances all changed.
        repository.LoadSuppliersInto(Suppliers);
        repository.LoadInvoicesInto(sharedPurchaseInvoices);
        notifySuppliersChanged?.Invoke();
        notifyInventoryChanged?.Invoke();
        notifyReturnExchangeChanged?.Invoke();
        NotifyExternalCatalogChanged();

        LastSavedInvoice = sharedPurchaseInvoices.FirstOrDefault(i => i.Id == saved.InvoiceId)
                           ?? sharedPurchaseInvoices.FirstOrDefault();

        var creditSuffix = saved.RemainingPayable > 0m
            ? $" | متبقي آجل للمورد: {PosFormatting.FormatCurrency(saved.RemainingPayable)}"
            : "";

        ActiveInvoice.ClearInvoice();
        var keepSupplier = Suppliers.FirstOrDefault(s => s.Id == supplier.Id) ?? Suppliers.FirstOrDefault();
        if (keepSupplier is not null)
            ActiveInvoice.SelectSupplier(keepSupplier);

        SetStatus(
            $"تم حفظ فاتورة الشراء {saved.InvoiceNumber} للمورد «{supplier.Name}» في قاعدة البيانات وتوريد الأصناف للمخزون (الصافي {PosFormatting.FormatCurrency(saved.NetTotal)}{creditSuffix}).",
            false);

        if (openPrintAfter && LastSavedInvoice is not null)
            OpenPrintPreviewRequested?.Invoke(new PurchaseInvoicePrintPreviewViewModel(LastSavedInvoice));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
