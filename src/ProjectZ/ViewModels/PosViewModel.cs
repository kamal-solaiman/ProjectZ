using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PosViewModel : ObservableObject
{
    private readonly Func<PosInvoiceViewModel, string>? onInvoiceCompleted;
    private readonly SalesRepository? repository;
    private readonly Func<bool>? reloadCatalog;
    private readonly Func<PosCustomerDesignItem, PosCustomerDesignItem>? createCustomer;
    private int nextInvoiceNumber = 1;
    private PosInvoiceViewModel activeInvoice = null!;
    private string productSearchText = "";
    private string selectedCategoryFilter = "الكل";
    private string statusMessage =
        "سجّل البيع بعد اختيار العميل وتأكيد الدفع، وستُحفظ الفاتورة والوحدات والتحصيل في قاعدة البيانات.";
    private bool isErrorStatus;

    public string CashierName { get; }

    /// <summary>True when the screen saves to SQL Server; the views use it to label the customer dialog correctly.</summary>
    public bool IsDatabaseBacked => repository is not null;
    public IReadOnlyList<PriceTierOption> PriceTierOptions => PosFormatting.PriceTierOptions;
    public ObservableCollection<PosCategoryFilterViewModel> CategoryFilters { get; } = [];
    public ObservableCollection<PosProductDesignItem> Catalog { get; }
    public ObservableCollection<PosProductDesignItem> FilteredCatalog { get; } = [];
    public ObservableCollection<PosCustomerDesignItem> Customers { get; }
    public ObservableCollection<PosInvoiceViewModel> Invoices { get; } = [];

    public PosInvoiceViewModel ActiveInvoice
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

    public Action<string?, Action<PosCustomerDesignItem>>? OpenAddCustomerRequested { get; set; }
    public Action? FocusBarcodeInputRequested { get; set; }

    public ActionCommand NewInvoiceCommand { get; }
    public RelayCommand<PosInvoiceViewModel> SelectInvoiceCommand { get; }
    public RelayCommand<PosInvoiceViewModel> CloseInvoiceCommand { get; }

    public ActionCommand ScanOrSearchEnterCommand { get; }
    public ActionCommand ClearProductSearchCommand { get; }
    public RelayCommand<PosCategoryFilterViewModel> SelectCategoryFilterCommand { get; }
    public RelayCommand<PosProductDesignItem> AddProductCommand { get; }

    public ActionCommand OpenAddCustomerWindowCommand { get; }
    public ActionCommand RefreshCatalogCommand { get; }
    public ActionCommand CompleteInvoicePreviewCommand { get; }
    public ActionCommand FocusBarcodeCommand { get; }

    public PosViewModel() : this("المشرف", null, null, null)
    {
    }

    public PosViewModel(
        string cashierName,
        ObservableCollection<PosProductDesignItem>? sharedCatalog = null,
        Func<PosInvoiceViewModel, string>? onInvoiceCompleted = null,
        ObservableCollection<PosCustomerDesignItem>? sharedCustomers = null,
        bool useDesignSamples = true,
        SalesRepository? repository = null,
        Func<bool>? reloadCatalog = null,
        Func<PosCustomerDesignItem, PosCustomerDesignItem>? createCustomer = null)
    {
        this.onInvoiceCompleted = onInvoiceCompleted;
        this.repository = repository;
        this.reloadCatalog = reloadCatalog;
        this.createCustomer = createCustomer;
        CashierName = cashierName;
        Catalog = sharedCatalog ?? PosDesignSeedData.CreateSharedSampleCatalog();
        Customers = sharedCustomers ?? CreateDefaultCustomers();
        foreach (var title in PosDesignSeedData.CategoryFilters)
            CategoryFilters.Add(new PosCategoryFilterViewModel(title, title == "الكل"));
        SyncCategoryFiltersFromCatalog();
        Catalog.CollectionChanged += OnCatalogCollectionChanged;

        NewInvoiceCommand = new ActionCommand(CreateAndSelectNewInvoice);
        SelectInvoiceCommand = new RelayCommand<PosInvoiceViewModel>(invoice =>
        {
            if (invoice is not null && Invoices.Contains(invoice))
            {
                ActiveInvoice = invoice;
                SetStatus($"تم التبديل إلى {invoice.TabTitle}.", false);
            }
        });
        CloseInvoiceCommand = new RelayCommand<PosInvoiceViewModel>(CloseInvoice);

        ScanOrSearchEnterCommand = new ActionCommand(HandleBarcodeOrSearchEnter);
        ClearProductSearchCommand = new ActionCommand(() => ProductSearchText = "");
        SelectCategoryFilterCommand = new RelayCommand<PosCategoryFilterViewModel>(filter =>
        {
            if (filter is not null)
                SelectedCategoryFilter = filter.Title;
        });
        AddProductCommand = new RelayCommand<PosProductDesignItem>(product =>
        {
            if (product is null) return;
            if (ActiveInvoice.AddProduct(product))
                SetStatus($"تمت إضافة «{product.Name}» إلى {ActiveInvoice.TabTitle}.", false);
        });

        OpenAddCustomerWindowCommand = new ActionCommand(RequestOpenAddCustomerWindow);
        RefreshCatalogCommand = new ActionCommand(() => RefreshFromDatabase());
        CompleteInvoicePreviewCommand = new ActionCommand(CompleteInvoicePreview);
        FocusBarcodeCommand = new ActionCommand(() => FocusBarcodeInputRequested?.Invoke());

        if (useDesignSamples)
            InitializeDesignSampleInvoices();
        else
            CreateAndSelectNewInvoice();
        RefreshFilteredCatalog();
        SetStatus(useDesignSamples
            ? "وضع العرض التجريبي: بيانات الفواتير والمنتجات والعملاء تجريبية لمعاينة الواجهة."
            : "تم فتح فاتورة بيع جديدة. اختر المنتجات من كتالوج المخزون.", false);
    }

    private void InitializeDesignSampleInvoices()
    {
        // Design samples require the complete seed catalog; never index an empty/SQL catalog.
        if (Catalog.Count < 4 || Customers.Count < 4)
        {
            CreateAndSelectNewInvoice();
            return;
        }

        // Sample Invoice #1: demonstrates split payment + per-item price override
        var invoice1 = CreateInvoice();
        invoice1.SelectCustomer(Customers[1], applyDefaultTier: false);
        invoice1.AddProduct(Catalog[0], quantity: 1, explicitTier: PriceTier.Retail);   // 450
        invoice1.AddProduct(Catalog[2], quantity: 1, explicitTier: PriceTier.Special);  // 775 (custom override)
        invoice1.AddProduct(Catalog[3], quantity: 2, explicitTier: PriceTier.Retail);   // 240
        invoice1.SetSplitAmounts(cash: 465m, vodafoneCash: 500m, instaPay: 500m);

        // Sample Invoice #2: demonstrates simultaneous open invoice in wholesale pricing
        var invoice2 = CreateInvoice();
        invoice2.SelectCustomer(Customers[3], applyDefaultTier: true); // Wholesale customer
        invoice2.AddProduct(Catalog[1], quantity: 5); // 5 x 145 = 725

        Invoices.Add(invoice1);
        Invoices.Add(invoice2);
        ActiveInvoice = invoice1;
    }

    /// <summary>Opens a fresh empty invoice; the tab number is local to the session, the saved number comes from SQL.</summary>
    private PosInvoiceViewModel CreateInvoice()
        => new(nextInvoiceNumber++, Customers, SetStatus, CanSetQuantity);

    private void CreateAndSelectNewInvoice()
    {
        var invoice = CreateInvoice();
        Invoices.Add(invoice);
        ActiveInvoice = invoice;
        SetStatus($"تم فتح {invoice.TabTitle} جديدة.", false);
        FocusBarcodeInputRequested?.Invoke();
    }

    public void StartNewInvoiceForCustomer(PosCustomerDesignItem customer)
    {
        var invoice = CreateInvoice();
        Invoices.Add(invoice);
        ActiveInvoice = invoice;
        ActiveInvoice.SelectCustomer(customer, applyDefaultTier: true);
        SetStatus($"تم فتح {invoice.TabTitle} جديدة للعميل «{customer.Name}» ({customer.DefaultPriceTierTitle}).", false);
        FocusBarcodeInputRequested?.Invoke();
    }

    /// <summary>
    /// Stock guard for the cart: a line may never ask for more units than SQL reports as available.
    /// The picker, the barcode entry and the stepper all pass through this check.
    /// </summary>
    private bool CanSetQuantity(PosProductDesignItem product, int requestedQuantity)
        => repository is null || requestedQuantity <= Math.Max(0, product.AvailableStock);

    private void CloseInvoice(PosInvoiceViewModel? invoice)
    {
        if (invoice is null || !Invoices.Contains(invoice)) return;

        if (Invoices.Count == 1)
        {
            invoice.ClearInvoice();
            SetStatus($"تم تفريغ {invoice.TabTitle} للبدء من جديد.", false);
            return;
        }

        var index = Invoices.IndexOf(invoice);
        var wasActive = ReferenceEquals(ActiveInvoice, invoice);
        invoice.Detach();
        Invoices.Remove(invoice);

        if (wasActive)
        {
            var nextIndex = Math.Clamp(index, 0, Invoices.Count - 1);
            ActiveInvoice = Invoices[nextIndex];
        }

        SetStatus($"تم إغلاق {invoice.TabTitle}.", false);
    }

    private void HandleBarcodeOrSearchEnter()
    {
        var query = ProductSearchText.Trim();
        if (string.IsNullOrEmpty(query))
        {
            SetStatus("أدخل رقم الباركود أو اسم المنتج للبحث والإضافة.", true);
            return;
        }

        var exactBarcode = Catalog.FirstOrDefault(p =>
            p.IsActiveForSale && p.Barcode.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (exactBarcode is not null)
        {
            if (ActiveInvoice.AddProduct(exactBarcode))
            {
                ProductSearchText = "";
                SetStatus($"تم مسح الباركود ({exactBarcode.Barcode}) وإضافة «{exactBarcode.Name}».", false);
            }

            return;
        }

        if (FilteredCatalog.Count == 1)
        {
            var single = FilteredCatalog[0];
            if (ActiveInvoice.AddProduct(single))
            {
                ProductSearchText = "";
                SetStatus($"تمت إضافة «{single.Name}» إلى {ActiveInvoice.TabTitle}.", false);
            }

            return;
        }

        if (FilteredCatalog.Count > 1)
        {
            SetStatus($"وُجد {FilteredCatalog.Count} منتجات مطابقة — اختر المنتج المطلوب من القائمة.", false);
            return;
        }

        SetStatus($"لم يتم العثور على منتج بالباركود أو الاسم «{query}».", true);
    }

    private static ObservableCollection<PosCustomerDesignItem> CreateDefaultCustomers()
    {
        var list = new ObservableCollection<PosCustomerDesignItem>();
        foreach (var customer in PosDesignSeedData.CreateSampleCustomers())
            list.Add(customer);
        return list;
    }

    private void RequestOpenAddCustomerWindow()
    {
        var initialSearch = ActiveInvoice.CustomerSearchText.Trim();
        OpenAddCustomerRequested?.Invoke(
            string.IsNullOrWhiteSpace(initialSearch) ? null : initialSearch,
            createdCustomer =>
            {
                if (createCustomer is not null)
                {
                    try
                    {
                        var saved = createCustomer(createdCustomer);
                        Customers.Add(saved);
                        ActiveInvoice.SelectCustomer(saved, applyDefaultTier: true);
                        SetStatus(
                            $"تم حفظ العميل «{saved.Name}» في قاعدة البيانات وربطه بالفاتورة #{ActiveInvoice.InvoiceNumber}.",
                            false);
                    }
                    catch (Exception ex)
                    {
                        SetStatus(DescribeError(ex, "تعذر حفظ العميل في قاعدة البيانات."), true);
                    }

                    return;
                }

                var nextId = Customers.Select(c => c.Id).DefaultIfEmpty(99).Max() + 1;
                var customerWithId = new PosCustomerDesignItem
                {
                    Id = nextId,
                    Name = createdCustomer.Name,
                    Phone = createdCustomer.Phone,
                    SecondaryPhone = createdCustomer.SecondaryPhone,
                    Address = createdCustomer.Address,
                    Notes = createdCustomer.Notes,
                    DefaultPriceTier = createdCustomer.DefaultPriceTier,
                    IsWalkIn = false
                };

                Customers.Add(customerWithId);
                ActiveInvoice.SelectCustomer(customerWithId, applyDefaultTier: true);
                SetStatus(
                    $"تمت إضافة العميل «{customerWithId.Name}» وربطه بالفاتورة #{ActiveInvoice.InvoiceNumber} (وضع التصميم).",
                    false);
            });
    }

    private void CompleteInvoicePreview()
    {
        var invoice = ActiveInvoice;
        if (invoice.IsEmpty)
        {
            SetStatus("أضف منتجًا واحدًا على الأقل قبل إتمام الفاتورة.", true);
            return;
        }

        if (invoice.HasStockShortage)
        {
            SetStatus(
                "الكمية المطلوبة تتجاوز المتاح في المخزون: " + string.Join(" · ", invoice.StockShortageSummaries) +
                ". حدّث الأصناف من قاعدة البيانات وعدّل الكميات.",
                true);
            return;
        }

        if (invoice.HasRemaining && !invoice.CanLeaveUnpaid)
        {
            SetStatus(
                $"المبلغ المدفوع أقل من المستحق — المتبقي على العميل: {invoice.RemainingAmountDisplay}. " +
                "البيع الآجل يحتاج اختيار عميل مسجل بدل العميل النقدي العام.",
                true);
            return;
        }

        var changeNote = invoice.HasChange
            ? $" · الباقي للعميل: {invoice.ChangeAmountDisplay}"
            : "";

        if (onInvoiceCompleted is not null)
        {
            var customerName = invoice.SelectedCustomer.Name;
            var paymentMix = invoice.PaymentMixSummary;
            var netTotal = invoice.NetTotalDisplay;

            string savedNo;
            try
            {
                savedNo = onInvoiceCompleted(invoice);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر حفظ الفاتورة في قاعدة البيانات."), true);
                return;
            }

            // Closing the invoice is safe now: stock, units and customers were reloaded from SQL already.
            CloseInvoice(invoice);

            SetStatus(
                $"تم حفظ الفاتورة {savedNo} للعميل «{customerName}» ({paymentMix} | الإجمالي: {netTotal}{changeNote}) في قاعدة البيانات.",
                false);
            FocusBarcodeInputRequested?.Invoke();
            return;
        }

        SetStatus(
            $"وضع العرض التجريبي: الفاتورة #{invoice.InvoiceNumber} للعميل «{invoice.SelectedCustomer.Name}» جاهزة ({invoice.PaymentMixSummary} | الإجمالي: {invoice.NetTotalDisplay}{changeNote}).",
            false);
    }

    /// <summary>Re-reads products, units, categories and customers after a sale so stock can never be stale.</summary>
    public void NotifyExternalInventoryChanged()
    {
        // The catalog instance is rebuilt on every SQL load, so open invoices must point at the new products.
        foreach (var invoice in Invoices)
            invoice.RebindCatalog(Catalog);

        SyncCategoryFiltersFromCatalog();
        RefreshFilteredCatalog();
    }

    /// <summary>Reloads the SQL catalog and customer directory, then reports the outcome in the status strip.</summary>
    public bool RefreshFromDatabase()
    {
        if (reloadCatalog is null) return false;

        try
        {
            if (!reloadCatalog())
                return false;
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر تحديث الأصناف والعملاء من قاعدة البيانات."), true);
            return false;
        }

        SyncCategoryFiltersFromCatalog();
        RefreshFilteredCatalog();
        SetStatus("تم تحديث الأصناف والعملاء من قاعدة البيانات.", false);
        return true;
    }

    private static string DescribeError(Exception exception, string fallback)
        => exception is SalesDataException data ? data.Message : fallback;

    private void OnCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncCategoryFiltersFromCatalog();
        RefreshFilteredCatalog();
    }

    private void SyncCategoryFiltersFromCatalog()
    {
        foreach (var item in Catalog)
        {
            if (string.IsNullOrWhiteSpace(item.Category)) continue;
            if (CategoryFilters.All(f => !f.Title.Equals(item.Category, StringComparison.OrdinalIgnoreCase)))
                CategoryFilters.Add(new PosCategoryFilterViewModel(item.Category, item.Category == selectedCategoryFilter));
        }
    }

    private void RefreshFilteredCatalog()
    {
        var query = productSearchText.Trim();
        var category = selectedCategoryFilter;

        var matches = Catalog.Where(p =>
            p.IsActiveForSale
            && (category == "الكل" || p.Category == category)
            && (string.IsNullOrEmpty(query)
                || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Category.Contains(query, StringComparison.OrdinalIgnoreCase)));

        FilteredCatalog.Clear();
        foreach (var item in matches)
            FilteredCatalog.Add(item);

        Changed(nameof(HasNoFilteredProducts));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}

public sealed class PosCategoryFilterViewModel : ObservableObject
{
    private bool isSelected;

    public string Title { get; }

    public bool IsSelected
    {
        get => isSelected;
        private set
        {
            if (isSelected == value) return;
            isSelected = value;
            Changed();
        }
    }

    public PosCategoryFilterViewModel(string title, bool isSelected)
    {
        Title = title;
        this.isSelected = isSelected;
    }

    internal void SetSelected(bool value) => IsSelected = value;
}
