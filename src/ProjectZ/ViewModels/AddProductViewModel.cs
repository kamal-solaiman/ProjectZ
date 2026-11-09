using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class AddProductViewModel : ObservableObject
{
    private readonly ObservableCollection<PosProductDesignItem> sharedCatalog;
    private readonly Action? navigateToPos;
    private int nextProductId = 100;

    private string productName = "";
    private string barcode = "";
    private string selectedCategory = "";
    private bool isAddingCustomCategory;
    private string newCategoryName = "";
    private string brand = "";
    private string sku = "";
    private string selectedUnit = "قطعة";

    private string costPriceText = "";
    private string retailPriceText = "";
    private string specialPriceText = "";
    private string wholesalePriceText = "";

    private string initialStockText = "10";
    private string minStockAlertText = "5";
    private string shelfLocation = "";
    private bool isActiveForSale = true;
    private bool trackSerialNumber;
    private bool isTaxable;
    private string notes = "";

    private string catalogSearchText = "";
    private string statusMessage = "مرحلة التصميم (بدون Backend): سجّل صنفًا جديدًا مع أسعار القطاعي والمميز والجملة وسيظهر فورًا في المعاينة ونقطة البيع.";
    private bool isErrorStatus;

    public ObservableCollection<string> Categories { get; } = [];
    public IReadOnlyList<string> Units => PosDesignSeedData.ProductUnits;
    public ObservableCollection<PosProductDesignItem> FilteredProducts { get; } = [];

    public Action? FocusProductNameRequested { get; set; }

    public string ProductName
    {
        get => productName;
        set
        {
            if (productName == value) return;
            productName = value;
            Changed();
            Changed(nameof(PreviewName));
        }
    }

    public string Barcode
    {
        get => barcode;
        set
        {
            if (barcode == value) return;
            barcode = value;
            Changed();
            Changed(nameof(PreviewBarcode));
        }
    }

    public string SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (selectedCategory == value) return;
            selectedCategory = value;
            Changed();
            Changed(nameof(PreviewCategory));
        }
    }

    public bool IsAddingCustomCategory
    {
        get => isAddingCustomCategory;
        private set
        {
            if (isAddingCustomCategory == value) return;
            isAddingCustomCategory = value;
            Changed();
        }
    }

    public string NewCategoryName
    {
        get => newCategoryName;
        set
        {
            if (newCategoryName == value) return;
            newCategoryName = value;
            Changed();
        }
    }

    public string Brand
    {
        get => brand;
        set
        {
            if (brand == value) return;
            brand = value;
            Changed();
            Changed(nameof(PreviewBrandAndSku));
        }
    }

    public string Sku
    {
        get => sku;
        set
        {
            if (sku == value) return;
            sku = value;
            Changed();
            Changed(nameof(PreviewBrandAndSku));
        }
    }

    public string SelectedUnit
    {
        get => selectedUnit;
        set
        {
            if (selectedUnit == value) return;
            selectedUnit = value;
            Changed();
            Changed(nameof(PreviewStockSummary));
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
            NotifyPricingChanged();
        }
    }

    public string RetailPriceText
    {
        get => retailPriceText;
        set
        {
            if (retailPriceText == value) return;
            retailPriceText = value;
            Changed();
            NotifyPricingChanged();
        }
    }

    public string SpecialPriceText
    {
        get => specialPriceText;
        set
        {
            if (specialPriceText == value) return;
            specialPriceText = value;
            Changed();
            NotifyPricingChanged();
        }
    }

    public string WholesalePriceText
    {
        get => wholesalePriceText;
        set
        {
            if (wholesalePriceText == value) return;
            wholesalePriceText = value;
            Changed();
            NotifyPricingChanged();
        }
    }

    public string InitialStockText
    {
        get => initialStockText;
        set
        {
            if (initialStockText == value) return;
            initialStockText = value;
            Changed();
            Changed(nameof(PreviewStockSummary));
        }
    }

    public string MinStockAlertText
    {
        get => minStockAlertText;
        set
        {
            if (minStockAlertText == value) return;
            minStockAlertText = value;
            Changed();
            Changed(nameof(PreviewStockSummary));
        }
    }

    public string ShelfLocation
    {
        get => shelfLocation;
        set
        {
            if (shelfLocation == value) return;
            shelfLocation = value;
            Changed();
            Changed(nameof(PreviewLocationDisplay));
        }
    }

    public bool IsActiveForSale
    {
        get => isActiveForSale;
        set
        {
            if (isActiveForSale == value) return;
            isActiveForSale = value;
            Changed();
            Changed(nameof(PreviewStatusText));
        }
    }

    public bool TrackSerialNumber
    {
        get => trackSerialNumber;
        set
        {
            if (trackSerialNumber == value) return;
            trackSerialNumber = value;
            Changed();
        }
    }

    public bool IsTaxable
    {
        get => isTaxable;
        set
        {
            if (isTaxable == value) return;
            isTaxable = value;
            Changed();
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (notes == value) return;
            notes = value;
            Changed();
        }
    }

    public string CatalogSearchText
    {
        get => catalogSearchText;
        set
        {
            if (catalogSearchText == value) return;
            catalogSearchText = value;
            Changed();
            RefreshFilteredProducts();
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

    // Parsed live pricing & margins
    private decimal ParsedCost => PosFormatting.TryParseAmount(CostPriceText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedRetail => PosFormatting.TryParseAmount(RetailPriceText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedSpecial => PosFormatting.TryParseAmount(SpecialPriceText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedWholesale => PosFormatting.TryParseAmount(WholesalePriceText, out var v) ? Math.Max(0m, v) : 0m;

    public bool HasCostPrice => ParsedCost > 0m;
    public string RetailMarginText => FormatMargin(ParsedRetail, ParsedCost);
    public string SpecialMarginText => FormatMargin(ParsedSpecial, ParsedCost);
    public string WholesaleMarginText => FormatMargin(ParsedWholesale, ParsedCost);
    public bool IsRetailMarginNegative => ParsedCost > 0m && ParsedRetail > 0m && ParsedRetail < ParsedCost;
    public bool IsSpecialMarginNegative => ParsedCost > 0m && ParsedSpecial > 0m && ParsedSpecial < ParsedCost;
    public bool IsWholesaleMarginNegative => ParsedCost > 0m && ParsedWholesale > 0m && ParsedWholesale < ParsedCost;

    public bool HasPriceOrderWarning
    {
        get
        {
            if (ParsedRetail <= 0m || (ParsedSpecial <= 0m && ParsedWholesale <= 0m))
                return false;

            return (ParsedSpecial > 0m && ParsedSpecial > ParsedRetail)
                   || (ParsedWholesale > 0m && ParsedWholesale > ParsedRetail)
                   || (ParsedSpecial > 0m && ParsedWholesale > 0m && ParsedWholesale > ParsedSpecial);
        }
    }

    // Live Card Preview Properties
    public string PreviewName => string.IsNullOrWhiteSpace(ProductName) ? "اسم الصنف الجديد سيظهر هنا" : ProductName.Trim();
    public string PreviewBarcode => string.IsNullOrWhiteSpace(Barcode) ? "62210010XX" : Barcode.Trim();
    public string PreviewCategory => string.IsNullOrWhiteSpace(SelectedCategory) ? "بدون تصنيف" : SelectedCategory;
    public string PreviewBrandAndSku => string.IsNullOrWhiteSpace(Brand) && string.IsNullOrWhiteSpace(Sku)
        ? "الماركة / الكود: غير محدد"
        : string.Join(" · ", new[] { Brand.Trim(), Sku.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
    public string PreviewCostDisplay => PosFormatting.FormatCurrency(ParsedCost);
    public string PreviewRetailDisplay => PosFormatting.FormatCurrency(ParsedRetail);
    public string PreviewSpecialDisplay => PosFormatting.FormatCurrency(ParsedSpecial);
    public string PreviewWholesaleDisplay => PosFormatting.FormatCurrency(ParsedWholesale);
    public string PreviewStockSummary
    {
        get
        {
            var stock = PosFormatting.TryParseNonNegativeInt(InitialStockText, out var s) ? s : 0;
            var min = PosFormatting.TryParseNonNegativeInt(MinStockAlertText, out var m) ? m : 0;
            var unit = string.IsNullOrWhiteSpace(SelectedUnit) ? "قطعة" : SelectedUnit;
            return $"{stock} {unit} · حد التنبيه: {min}";
        }
    }
    public string PreviewLocationDisplay => string.IsNullOrWhiteSpace(ShelfLocation) ? "الموقع: غير محدد" : $"الموقع: {ShelfLocation.Trim()}";
    public string PreviewStatusText => IsActiveForSale ? "متاح للبيع في نقطة البيع" : "موقوف عن البيع مؤقتًا";

    public string CatalogCountDisplay => $"{sharedCatalog.Count} صنف في الدليل";
    public bool HasNoCatalogResults => FilteredProducts.Count == 0;

    public ActionCommand GenerateBarcodeCommand { get; }
    public ActionCommand ToggleNewCategoryCommand { get; }
    public ActionCommand ConfirmNewCategoryCommand { get; }
    public ActionCommand CancelNewCategoryCommand { get; }
    public ActionCommand EstimatePricesFromRetailCommand { get; }
    public ActionCommand SaveProductCommand { get; }
    public ActionCommand SaveAndKeepCategoryCommand { get; }
    public ActionCommand ResetFormCommand { get; }
    public ActionCommand GoToPosCommand { get; }
    public ActionCommand ClearCatalogSearchCommand { get; }
    public RelayCommand<PosProductDesignItem> CloneProductCommand { get; }
    public RelayCommand<PosProductDesignItem> RemoveProductCommand { get; }

    public AddProductViewModel()
        : this(PosDesignSeedData.CreateSharedSampleCatalog(), null)
    {
    }

    public AddProductViewModel(
        ObservableCollection<PosProductDesignItem> sharedCatalog,
        Action? navigateToPos = null)
    {
        this.sharedCatalog = sharedCatalog;
        this.navigateToPos = navigateToPos;

        foreach (var category in PosDesignSeedData.ProductCategories)
            Categories.Add(category);
        foreach (var item in sharedCatalog)
        {
            if (!string.IsNullOrWhiteSpace(item.Category) && !Categories.Contains(item.Category))
                Categories.Add(item.Category);
        }

        selectedCategory = Categories.FirstOrDefault() ?? "هواتف";
        barcode = GenerateNextAvailableBarcode();

        sharedCatalog.CollectionChanged += OnCatalogCollectionChanged;

        GenerateBarcodeCommand = new ActionCommand(() =>
        {
            Barcode = GenerateNextAvailableBarcode();
            SetStatus($"تم توليد باركود جديد: {Barcode}", false);
        });
        ToggleNewCategoryCommand = new ActionCommand(() =>
        {
            IsAddingCustomCategory = !IsAddingCustomCategory;
            if (!IsAddingCustomCategory)
                NewCategoryName = "";
        });
        ConfirmNewCategoryCommand = new ActionCommand(AddCustomCategory);
        CancelNewCategoryCommand = new ActionCommand(() =>
        {
            NewCategoryName = "";
            IsAddingCustomCategory = false;
        });
        EstimatePricesFromRetailCommand = new ActionCommand(EstimateSpecialAndWholesaleFromRetail);
        SaveProductCommand = new ActionCommand(() => SaveProduct(keepCategoryAndUnit: false));
        SaveAndKeepCategoryCommand = new ActionCommand(() => SaveProduct(keepCategoryAndUnit: true));
        ResetFormCommand = new ActionCommand(() => ResetForm(keepCategoryAndUnit: false));
        GoToPosCommand = new ActionCommand(() => this.navigateToPos?.Invoke());
        ClearCatalogSearchCommand = new ActionCommand(() => CatalogSearchText = "");
        CloneProductCommand = new RelayCommand<PosProductDesignItem>(CloneProductIntoForm);
        RemoveProductCommand = new RelayCommand<PosProductDesignItem>(RemoveProduct);

        RefreshFilteredProducts();
    }

    private void AddCustomCategory()
    {
        var trimmed = NewCategoryName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("أدخل اسم التصنيف الجديد أولًا.", true);
            return;
        }

        var existing = Categories.FirstOrDefault(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedCategory = existing;
        }
        else
        {
            Categories.Add(trimmed);
            SelectedCategory = trimmed;
        }

        NewCategoryName = "";
        IsAddingCustomCategory = false;
        SetStatus($"تم اختيار التصنيف «{SelectedCategory}».", false);
    }

    private void EstimateSpecialAndWholesaleFromRetail()
    {
        if (!PosFormatting.TryParseAmount(RetailPriceText, out var retail) || retail <= 0m)
        {
            SetStatus("أدخل سعر القطاعي أولًا ليتم تقدير السعر المميز وسعر الجملة تلقائيًا.", true);
            return;
        }

        var special = Math.Round(retail * 0.92m, 2);
        var wholesale = Math.Round(retail * 0.85m, 2);

        SpecialPriceText = PosFormatting.FormatPlainNumber(special);
        WholesalePriceText = PosFormatting.FormatPlainNumber(wholesale);
        SetStatus("تم تقدير السعر المميز (-8%) وسعر الجملة (-15%) من السعر القطاعي؛ يمكنك تعديلها يدويًا.", false);
    }

    private void SaveProduct(bool keepCategoryAndUnit)
    {
        var trimmedName = ProductName.Trim();
        var trimmedBarcode = Barcode.Trim();
        var trimmedCategory = SelectedCategory.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            SetStatus("يرجى إدخال اسم الصنف / المنتج.", true);
            FocusProductNameRequested?.Invoke();
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmedBarcode))
        {
            SetStatus("يرجى إدخال الباركود أو الضغط على «توليد تلقائي».", true);
            return;
        }

        if (sharedCatalog.Any(p => p.Barcode.Equals(trimmedBarcode, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"الباركود «{trimmedBarcode}» مسجل بالفعل لصنف آخر. استخدم باركودًا مختلفًا أو اضغط توليد تلقائي.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmedCategory))
        {
            SetStatus("يرجى اختيار التصنيف.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(CostPriceText, out var costPrice) || costPrice < 0m)
        {
            SetStatus("سعر التكلفة غير صحيح.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(RetailPriceText, out var retailPrice) || retailPrice <= 0m)
        {
            SetStatus("يرجى إدخال سعر قطاعي صحيح أكبر من صفر.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(SpecialPriceText, out var specialPrice) || specialPrice <= 0m)
        {
            SetStatus("يرجى إدخال سعر مميز صحيح أكبر من صفر.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(WholesalePriceText, out var wholesalePrice) || wholesalePrice <= 0m)
        {
            SetStatus("يرجى إدخال سعر جملة صحيح أكبر من صفر.", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(InitialStockText, out var initialStock))
        {
            SetStatus("الكمية الافتتاحية يجب أن تكون رقمًا صحيحًا غير سالب.", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(MinStockAlertText, out var minStock))
        {
            SetStatus("حد التنبيه يجب أن يكون رقمًا صحيحًا غير سالب.", true);
            return;
        }

        var created = new PosProductDesignItem
        {
            Id = nextProductId++,
            Barcode = trimmedBarcode,
            Name = trimmedName,
            Category = trimmedCategory,
            Brand = Brand.Trim(),
            Sku = Sku.Trim(),
            Unit = string.IsNullOrWhiteSpace(SelectedUnit) ? "قطعة" : SelectedUnit,
            CostPrice = costPrice,
            RetailPrice = retailPrice,
            SpecialPrice = specialPrice,
            WholesalePrice = wholesalePrice,
            AvailableStock = initialStock,
            MinStockAlert = minStock,
            ShelfLocation = ShelfLocation.Trim(),
            IsActiveForSale = IsActiveForSale,
            TrackSerialNumber = TrackSerialNumber,
            IsTaxable = IsTaxable,
            Notes = Notes.Trim(),
            IsNewlyAdded = true
        };

        sharedCatalog.Add(created);
        ResetForm(keepCategoryAndUnit, clearStatus: false);
        SetStatus(
            $"وضع التصميم (بدون Backend): تم تسجيل «{created.Name}» ({created.Barcode}) — قطاعي {created.RetailPriceDisplay} | مميز {created.SpecialPriceDisplay} | جملة {created.WholesalePriceDisplay} وإضافته لنقطة البيع.",
            false);
        FocusProductNameRequested?.Invoke();
    }

    private void CloneProductIntoForm(PosProductDesignItem? source)
    {
        if (source is null) return;

        if (!Categories.Contains(source.Category) && !string.IsNullOrWhiteSpace(source.Category))
            Categories.Add(source.Category);

        ProductName = $"{source.Name} (نسخة)";
        Barcode = GenerateNextAvailableBarcode();
        SelectedCategory = source.Category;
        Brand = source.Brand;
        Sku = source.Sku;
        SelectedUnit = source.UnitDisplay;
        CostPriceText = source.CostPrice > 0m ? PosFormatting.FormatPlainNumber(source.CostPrice) : "";
        RetailPriceText = PosFormatting.FormatPlainNumber(source.RetailPrice);
        SpecialPriceText = PosFormatting.FormatPlainNumber(source.SpecialPrice);
        WholesalePriceText = PosFormatting.FormatPlainNumber(source.WholesalePrice);
        InitialStockText = source.AvailableStock.ToString(CultureInfo.InvariantCulture);
        MinStockAlertText = source.MinStockAlert.ToString(CultureInfo.InvariantCulture);
        ShelfLocation = source.ShelfLocation;
        IsActiveForSale = source.IsActiveForSale;
        TrackSerialNumber = source.TrackSerialNumber;
        IsTaxable = source.IsTaxable;
        Notes = source.Notes;

        SetStatus($"تم تحميل بيانات «{source.Name}» في النموذج مع توليد باركود جديد للتعديل أو الحفظ.", false);
        FocusProductNameRequested?.Invoke();
    }

    private void RemoveProduct(PosProductDesignItem? item)
    {
        if (item is null || !sharedCatalog.Contains(item)) return;

        sharedCatalog.Remove(item);
        SetStatus($"تمت إزالة الصنف «{item.Name}» من دليل التصميم المؤقت.", false);
    }

    private void ResetForm(bool keepCategoryAndUnit, bool clearStatus = true)
    {
        ProductName = "";
        Barcode = GenerateNextAvailableBarcode();
        if (!keepCategoryAndUnit)
        {
            SelectedCategory = Categories.FirstOrDefault() ?? "هواتف";
            SelectedUnit = Units.FirstOrDefault() ?? "قطعة";
        }
        IsAddingCustomCategory = false;
        NewCategoryName = "";
        Brand = "";
        Sku = "";
        CostPriceText = "";
        RetailPriceText = "";
        SpecialPriceText = "";
        WholesalePriceText = "";
        InitialStockText = "10";
        MinStockAlertText = "5";
        ShelfLocation = "";
        IsActiveForSale = true;
        TrackSerialNumber = false;
        IsTaxable = false;
        Notes = "";

        if (clearStatus)
            SetStatus("تم تفريغ الحقول وتوليد باركود جديد.", false);
    }

    private string GenerateNextAvailableBarcode()
    {
        long candidate = 6221001011;
        while (sharedCatalog.Any(p => p.Barcode.Equals(candidate.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
               || candidate.ToString(CultureInfo.InvariantCulture) == Barcode.Trim())
        {
            candidate++;
        }

        return candidate.ToString(CultureInfo.InvariantCulture);
    }

    private void NotifyPricingChanged()
    {
        Changed(nameof(HasCostPrice));
        Changed(nameof(RetailMarginText));
        Changed(nameof(SpecialMarginText));
        Changed(nameof(WholesaleMarginText));
        Changed(nameof(IsRetailMarginNegative));
        Changed(nameof(IsSpecialMarginNegative));
        Changed(nameof(IsWholesaleMarginNegative));
        Changed(nameof(HasPriceOrderWarning));
        Changed(nameof(PreviewCostDisplay));
        Changed(nameof(PreviewRetailDisplay));
        Changed(nameof(PreviewSpecialDisplay));
        Changed(nameof(PreviewWholesaleDisplay));
    }

    private static string FormatMargin(decimal sellPrice, decimal costPrice)
    {
        if (costPrice <= 0m || sellPrice <= 0m) return "—";
        var diff = sellPrice - costPrice;
        var pct = Math.Round((diff / costPrice) * 100m, 1);
        var sign = diff >= 0m ? "+" : "";
        return $"{sign}{PosFormatting.FormatPlainNumber(diff)} ج.م ({sign}{pct.ToString("0.#", CultureInfo.InvariantCulture)}%)";
    }

    private void OnCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Changed(nameof(CatalogCountDisplay));
        RefreshFilteredProducts();
    }

    private void RefreshFilteredProducts()
    {
        var query = catalogSearchText.Trim();
        var matches = sharedCatalog
            .Where(p => string.IsNullOrEmpty(query)
                        || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || p.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || p.Brand.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.IsNewlyAdded)
            .ThenByDescending(p => p.Id);

        FilteredProducts.Clear();
        foreach (var item in matches)
            FilteredProducts.Add(item);

        Changed(nameof(HasNoCatalogResults));
        Changed(nameof(CatalogCountDisplay));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
