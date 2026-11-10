using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PosViewModel : ObservableObject
{
    private readonly Func<PosInvoiceViewModel, string>? onInvoiceCompleted;
    private int nextInvoiceNumber = 1;
    private int nextCustomerId = 100;
    private PosInvoiceViewModel activeInvoice = null!;
    private string productSearchText = "";
    private string selectedCategoryFilter = "الكل";
    private string statusMessage = "مرحلة التصميم (بدون Backend): بيانات الفواتير والمنتجات والعملاء تجريبية لمعاينة الواجهة.";
    private bool isErrorStatus;

    public string CashierName { get; }
    public IReadOnlyList<PriceTierOption> PriceTierOptions => PosFormatting.PriceTierOptions;
    public ObservableCollection<PosCategoryFilterViewModel> CategoryFilters { get; } = [];
    public ObservableCollection<PosProductDesignItem> Catalog { get; }
    public ObservableCollection<PosProductDesignItem> FilteredCatalog { get; } = [];
    public ObservableCollection<PosCustomerDesignItem> Customers { get; } = [];
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
    public ActionCommand CompleteInvoicePreviewCommand { get; }
    public ActionCommand FocusBarcodeCommand { get; }

    public PosViewModel() : this("المشرف", null, null)
    {
    }

    public PosViewModel(
        string cashierName,
        ObservableCollection<PosProductDesignItem>? sharedCatalog = null,
        Func<PosInvoiceViewModel, string>? onInvoiceCompleted = null)
    {
        this.onInvoiceCompleted = onInvoiceCompleted;
        CashierName = cashierName;
        Catalog = sharedCatalog ?? PosDesignSeedData.CreateSharedSampleCatalog();
        foreach (var title in PosDesignSeedData.CategoryFilters)
            CategoryFilters.Add(new PosCategoryFilterViewModel(title, title == "الكل"));
        SyncCategoryFiltersFromCatalog();
        Catalog.CollectionChanged += OnCatalogCollectionChanged;

        foreach (var customer in PosDesignSeedData.CreateSampleCustomers())
            Customers.Add(customer);

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
            ActiveInvoice.AddProduct(product);
            SetStatus($"تمت إضافة «{product.Name}» إلى {ActiveInvoice.TabTitle}.", false);
        });

        OpenAddCustomerWindowCommand = new ActionCommand(RequestOpenAddCustomerWindow);
        CompleteInvoicePreviewCommand = new ActionCommand(CompleteInvoicePreview);
        FocusBarcodeCommand = new ActionCommand(() => FocusBarcodeInputRequested?.Invoke());

        InitializeDesignSampleInvoices();
        RefreshFilteredCatalog();
        SetStatus("مرحلة التصميم (بدون Backend): بيانات الفواتير والمنتجات والعملاء تجريبية لمعاينة الواجهة.", false);
    }

    private void InitializeDesignSampleInvoices()
    {
        // Sample Invoice #1: demonstrates split payment + per-item price override
        var invoice1 = new PosInvoiceViewModel(nextInvoiceNumber++, Customers, SetStatus);
        invoice1.SelectCustomer(Customers[1], applyDefaultTier: false);
        invoice1.AddProduct(Catalog[0], quantity: 1, explicitTier: PriceTier.Retail);   // 450
        invoice1.AddProduct(Catalog[2], quantity: 1, explicitTier: PriceTier.Special);  // 775 (custom override)
        invoice1.AddProduct(Catalog[3], quantity: 2, explicitTier: PriceTier.Retail);   // 240
        invoice1.SetSplitAmounts(cash: 465m, vodafoneCash: 500m, instaPay: 500m);

        // Sample Invoice #2: demonstrates simultaneous open invoice in wholesale pricing
        var invoice2 = new PosInvoiceViewModel(nextInvoiceNumber++, Customers, SetStatus);
        invoice2.SelectCustomer(Customers[3], applyDefaultTier: true); // Wholesale customer
        invoice2.AddProduct(Catalog[1], quantity: 5); // 5 x 145 = 725

        Invoices.Add(invoice1);
        Invoices.Add(invoice2);
        ActiveInvoice = invoice1;
    }

    private void CreateAndSelectNewInvoice()
    {
        var invoice = new PosInvoiceViewModel(nextInvoiceNumber++, Customers, SetStatus);
        Invoices.Add(invoice);
        ActiveInvoice = invoice;
        SetStatus($"تم فتح {invoice.TabTitle} جديدة.", false);
        FocusBarcodeInputRequested?.Invoke();
    }

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
            ActiveInvoice.AddProduct(exactBarcode);
            ProductSearchText = "";
            SetStatus($"تم مسح الباركود ({exactBarcode.Barcode}) وإضافة «{exactBarcode.Name}».", false);
            return;
        }

        if (FilteredCatalog.Count == 1)
        {
            var single = FilteredCatalog[0];
            ActiveInvoice.AddProduct(single);
            ProductSearchText = "";
            SetStatus($"تمت إضافة «{single.Name}» إلى {ActiveInvoice.TabTitle}.", false);
            return;
        }

        if (FilteredCatalog.Count > 1)
        {
            SetStatus($"وُجد {FilteredCatalog.Count} منتجات مطابقة — اختر المنتج المطلوب من القائمة.", false);
            return;
        }

        SetStatus($"لم يتم العثور على منتج بالباركود أو الاسم «{query}».", true);
    }

    private void RequestOpenAddCustomerWindow()
    {
        var initialSearch = ActiveInvoice.CustomerSearchText.Trim();
        OpenAddCustomerRequested?.Invoke(
            string.IsNullOrWhiteSpace(initialSearch) ? null : initialSearch,
            createdCustomer =>
            {
                var customerWithId = new PosCustomerDesignItem
                {
                    Id = nextCustomerId++,
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
        if (ActiveInvoice.IsEmpty)
        {
            SetStatus("أضف منتجًا واحدًا على الأقل قبل إتمام الفاتورة.", true);
            return;
        }

        if (ActiveInvoice.HasRemaining)
        {
            SetStatus(
                $"المبلغ المدفوع أقل من المستحق — المتبقي على العميل: {ActiveInvoice.RemainingAmountDisplay}.",
                true);
            return;
        }

        var changeNote = ActiveInvoice.HasChange
            ? $" · الباقي للعميل: {ActiveInvoice.ChangeAmountDisplay}"
            : "";

        if (onInvoiceCompleted is not null)
        {
            var customerName = ActiveInvoice.SelectedCustomer.Name;
            var paymentMix = ActiveInvoice.PaymentMixSummary;
            var netTotal = ActiveInvoice.NetTotalDisplay;
            var savedNo = onInvoiceCompleted(ActiveInvoice);

            CloseInvoice(ActiveInvoice);
            SetStatus(
                $"تم حفظ الفاتورة {savedNo} للعميل «{customerName}» ({paymentMix} | الإجمالي: {netTotal}{changeNote}) وإضافتها لقسم الفواتير وتحديث المخزون.",
                false);
            return;
        }

        SetStatus(
            $"وضع التصميم (بدون Backend): الفاتورة #{ActiveInvoice.InvoiceNumber} للعميل «{ActiveInvoice.SelectedCustomer.Name}» جاهزة ({ActiveInvoice.PaymentMixSummary} | الإجمالي: {ActiveInvoice.NetTotalDisplay}{changeNote}).",
            false);
    }

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
