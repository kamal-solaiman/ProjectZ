using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class ProductRegistrationsViewModel : ObservableObject
{
    private readonly ObservableCollection<InventoryUnitRegistrationItem> allRegistrations;
    private readonly Action<InventoryUnitRegistrationItem, string> markUnitDamaged;
    private readonly Action<InventoryUnitRegistrationItem> restoreUnitToAvailable;

    private InventoryUnitStatus? statusFilter;
    private string searchText = "";
    private string quickDamageReason = "تالف عند الفحص";
    private string statusMessage = "";

    public PosProductDesignItem Product { get; }
    public ObservableCollection<InventoryUnitRegistrationItem> FilteredRegistrations { get; } = [];
    public ObservableCollection<InventorySupplierSummary> SupplierSummaries { get; } = [];
    public IReadOnlyList<string> QuickDamageReasons => InventoryDesignSeedData.DamageReasons;

    public InventoryUnitStatus? StatusFilter
    {
        get => statusFilter;
        private set
        {
            if (statusFilter == value) return;
            statusFilter = value;
            Changed();
            Changed(nameof(IsAllSelected));
            Changed(nameof(IsAvailableSelected));
            Changed(nameof(IsSoldSelected));
            Changed(nameof(IsDamagedSelected));
            Refresh();
        }
    }

    public bool IsAllSelected => StatusFilter is null;
    public bool IsAvailableSelected => StatusFilter == InventoryUnitStatus.Available;
    public bool IsSoldSelected => StatusFilter == InventoryUnitStatus.Sold;
    public bool IsDamagedSelected => StatusFilter == InventoryUnitStatus.Damaged;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            Refresh();
        }
    }

    public string QuickDamageReason
    {
        get => quickDamageReason;
        set
        {
            if (quickDamageReason == value) return;
            quickDamageReason = value;
            Changed();
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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public int TotalCount { get; private set; }
    public int AvailableCount { get; private set; }
    public int SoldCount { get; private set; }
    public int DamagedCount { get; private set; }
    public bool HasNoResults => FilteredRegistrations.Count == 0;

    public string AllFilterLabel => $"الكل ({TotalCount})";
    public string AvailableFilterLabel => $"متاح ({AvailableCount})";
    public string SoldFilterLabel => $"مباع ({SoldCount})";
    public string DamagedFilterLabel => $"هالك ({DamagedCount})";

    public Action? RequestClose { get; set; }
    public ActionCommand SelectAllFilterCommand { get; }
    public ActionCommand SelectAvailableFilterCommand { get; }
    public ActionCommand SelectSoldFilterCommand { get; }
    public ActionCommand SelectDamagedFilterCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public RelayCommand<string> PickDamageReasonCommand { get; }
    public RelayCommand<InventoryUnitRegistrationItem> MarkUnitDamagedCommand { get; }
    public RelayCommand<InventoryUnitRegistrationItem> RestoreUnitCommand { get; }
    public ActionCommand CloseCommand { get; }

    public ProductRegistrationsViewModel(
        PosProductDesignItem product,
        ObservableCollection<InventoryUnitRegistrationItem> allRegistrations,
        Action<InventoryUnitRegistrationItem, string> markUnitDamaged,
        Action<InventoryUnitRegistrationItem> restoreUnitToAvailable)
    {
        Product = product;
        this.allRegistrations = allRegistrations;
        this.markUnitDamaged = markUnitDamaged;
        this.restoreUnitToAvailable = restoreUnitToAvailable;

        SelectAllFilterCommand = new ActionCommand(() => StatusFilter = null);
        SelectAvailableFilterCommand = new ActionCommand(() => StatusFilter = InventoryUnitStatus.Available);
        SelectSoldFilterCommand = new ActionCommand(() => StatusFilter = InventoryUnitStatus.Sold);
        SelectDamagedFilterCommand = new ActionCommand(() => StatusFilter = InventoryUnitStatus.Damaged);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        PickDamageReasonCommand = new RelayCommand<string>(r =>
        {
            if (!string.IsNullOrWhiteSpace(r))
                QuickDamageReason = r;
        });
        MarkUnitDamagedCommand = new RelayCommand<InventoryUnitRegistrationItem>(unit =>
        {
            if (unit is null || !unit.IsAvailable) return;
            var reason = string.IsNullOrWhiteSpace(QuickDamageReason) ? "تالف عند الفحص" : QuickDamageReason.Trim();
            this.markUnitDamaged(unit, reason);
            StatusMessage = $"تم تحويل التسجيل {unit.IdDisplay} (المورد: {unit.SupplierName}) إلى مخزون الهالك.";
            Refresh();
        });
        RestoreUnitCommand = new RelayCommand<InventoryUnitRegistrationItem>(unit =>
        {
            if (unit is null || !unit.IsDamaged) return;
            this.restoreUnitToAvailable(unit);
            StatusMessage = $"تم استرجاع التسجيل {unit.IdDisplay} (المورد: {unit.SupplierName}) إلى المخزون المتاح.";
            Refresh();
        });
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());

        Refresh();
    }

    public void Refresh()
    {
        var productUnits = allRegistrations
            .Where(r => r.ProductId == Product.Id)
            .OrderByDescending(r => r.RegisteredAt)
            .ThenByDescending(r => r.Id)
            .ToList();

        TotalCount = productUnits.Count;
        AvailableCount = productUnits.Count(r => r.IsAvailable);
        SoldCount = productUnits.Count(r => r.IsSold);
        DamagedCount = productUnits.Count(r => r.IsDamaged);

        Changed(nameof(TotalCount));
        Changed(nameof(AvailableCount));
        Changed(nameof(SoldCount));
        Changed(nameof(DamagedCount));
        Changed(nameof(AllFilterLabel));
        Changed(nameof(AvailableFilterLabel));
        Changed(nameof(SoldFilterLabel));
        Changed(nameof(DamagedFilterLabel));

        SupplierSummaries.Clear();
        foreach (var group in productUnits.GroupBy(u => string.IsNullOrWhiteSpace(u.SupplierName) ? "غير محدد" : u.SupplierName))
        {
            SupplierSummaries.Add(new InventorySupplierSummary(
                group.Key,
                group.Count(x => x.IsAvailable),
                group.Count(x => x.IsSold),
                group.Count(x => x.IsDamaged)));
        }

        var query = searchText.Trim();
        var filtered = productUnits.Where(u =>
            (StatusFilter is null || u.Status == StatusFilter.Value)
            && (string.IsNullOrEmpty(query)
                || u.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)
                || u.IdDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                || u.SupplierName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || u.PurchaseInvoiceId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || u.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase)));

        FilteredRegistrations.Clear();
        foreach (var item in filtered)
            FilteredRegistrations.Add(item);

        Changed(nameof(HasNoResults));
    }
}

public sealed record SupplyStockSubmission(int Quantity, string SupplierName, decimal PurchasePrice, string InvoiceId);

public sealed class SupplyStockViewModel : ObservableObject
{
    private string quantityText = "1";
    private string supplierName = "";
    private string purchasePriceText;
    private string invoiceId;
    private string error = "";

    public PosProductDesignItem Product { get; }
    public IReadOnlyList<string> SuggestedSuppliers { get; }

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (quantityText == value) return;
            quantityText = value;
            Changed();
            Changed(nameof(PreviewSummary));
            Error = "";
        }
    }

    public string SupplierName
    {
        get => supplierName;
        set
        {
            if (supplierName == value) return;
            supplierName = value;
            Changed();
            Changed(nameof(PreviewSummary));
            Error = "";
        }
    }

    public string PurchasePriceText
    {
        get => purchasePriceText;
        set
        {
            if (purchasePriceText == value) return;
            purchasePriceText = value;
            Changed();
            Error = "";
        }
    }

    public string InvoiceId
    {
        get => invoiceId;
        set
        {
            if (invoiceId == value) return;
            invoiceId = value;
            Changed();
            Error = "";
        }
    }

    public string Error
    {
        get => error;
        private set
        {
            if (error == value) return;
            error = value;
            Changed();
        }
    }

    public string PreviewSummary
    {
        get
        {
            if (!int.TryParse(QuantityText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
                return "أدخل كمية صحيحة أكبر من صفر لمعاينة التوريد.";

            var supplierDisplay = string.IsNullOrWhiteSpace(SupplierName) ? "المورد" : SupplierName.Trim();
            return $"سيتم تسجيل {qty} قطعة جديدة بحالة «متاح» من «{supplierDisplay}» — ليصبح إجمالي المتاح: {Product.AvailableStock + qty}";
        }
    }

    public Action<bool>? RequestCloseWithResult { get; set; }
    public RelayCommand<string> PickSupplierCommand { get; }
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public SupplyStockViewModel(
        PosProductDesignItem product,
        IReadOnlyList<string> knownSuppliers,
        string suggestedInvoiceId,
        Action<SupplyStockSubmission> onConfirm)
    {
        Product = product;
        SuggestedSuppliers = knownSuppliers;
        purchasePriceText = product.CostPrice > 0m ? PosFormatting.FormatPlainNumber(product.CostPrice) : "0";
        invoiceId = suggestedInvoiceId;
        supplierName = knownSuppliers.FirstOrDefault() ?? "أحمد علي";

        PickSupplierCommand = new RelayCommand<string>(s =>
        {
            if (!string.IsNullOrWhiteSpace(s))
                SupplierName = s;
        });

        ConfirmCommand = new ActionCommand(() =>
        {
            if (!int.TryParse(QuantityText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                Error = "يرجى إدخال كمية صحيحة أكبر من صفر.";
                return;
            }

            var trimmedSupplier = SupplierName.Trim();
            if (string.IsNullOrWhiteSpace(trimmedSupplier))
            {
                Error = "يرجى إدخال اسم المورد أو اختياره.";
                return;
            }

            if (!PosFormatting.TryParseAmount(PurchasePriceText, out var price) || price < 0m)
            {
                Error = "يرجى إدخال سعر شراء صحيح.";
                return;
            }

            var trimmedInvoice = string.IsNullOrWhiteSpace(InvoiceId) ? suggestedInvoiceId : InvoiceId.Trim();
            onConfirm(new SupplyStockSubmission(qty, trimmedSupplier, price, trimmedInvoice));
            RequestCloseWithResult?.Invoke(true);
        });

        CancelCommand = new ActionCommand(() => RequestCloseWithResult?.Invoke(false));
    }
}

public sealed record EditProductSubmission(
    string Name,
    string Barcode,
    string Category,
    string Unit,
    decimal CostPrice,
    decimal RetailPrice,
    decimal WholesalePrice,
    decimal SpecialPrice,
    int MinStockAlert,
    bool IsActiveForSale,
    bool TrackSerialNumber,
    string Notes);

public sealed class EditProductViewModel : ObservableObject
{
    private readonly PosProductDesignItem product;
    private readonly IReadOnlyCollection<PosProductDesignItem> allProducts;

    private string productName;
    private string barcode;
    private string selectedCategory;
    private string selectedUnit;
    private string costPriceText;
    private string retailPriceText;
    private string wholesalePriceText;
    private string specialPriceText;
    private string minStockAlertText;
    private bool isActiveForSale;
    private bool trackSerialNumber;
    private string notes;
    private string error = "";

    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<string> Units => PosDesignSeedData.ProductUnits;

    public string ProductName
    {
        get => productName;
        set { productName = value; Changed(); Error = ""; }
    }

    public string Barcode
    {
        get => barcode;
        set { barcode = value; Changed(); Error = ""; }
    }

    public string SelectedCategory
    {
        get => selectedCategory;
        set { selectedCategory = value; Changed(); Error = ""; }
    }

    public string SelectedUnit
    {
        get => selectedUnit;
        set { selectedUnit = value; Changed(); Error = ""; }
    }

    public string CostPriceText
    {
        get => costPriceText;
        set { costPriceText = value; Changed(); NotifyMargins(); Error = ""; }
    }

    public string RetailPriceText
    {
        get => retailPriceText;
        set { retailPriceText = value; Changed(); NotifyMargins(); Error = ""; }
    }

    public string WholesalePriceText
    {
        get => wholesalePriceText;
        set { wholesalePriceText = value; Changed(); NotifyMargins(); Error = ""; }
    }

    public string SpecialPriceText
    {
        get => specialPriceText;
        set { specialPriceText = value; Changed(); NotifyMargins(); Error = ""; }
    }

    public string MinStockAlertText
    {
        get => minStockAlertText;
        set { minStockAlertText = value; Changed(); Error = ""; }
    }

    public bool IsActiveForSale
    {
        get => isActiveForSale;
        set { isActiveForSale = value; Changed(); }
    }

    public bool TrackSerialNumber
    {
        get => trackSerialNumber;
        set { trackSerialNumber = value; Changed(); }
    }

    public string Notes
    {
        get => notes;
        set { notes = value; Changed(); }
    }

    public string Error
    {
        get => error;
        private set { error = value; Changed(); }
    }

    private decimal ParsedCost => PosFormatting.TryParseAmount(CostPriceText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedRetail => PosFormatting.TryParseAmount(RetailPriceText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedWholesale => PosFormatting.TryParseAmount(WholesalePriceText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedSpecial => PosFormatting.TryParseAmount(SpecialPriceText, out var v) ? Math.Max(0m, v) : 0m;

    public bool HasCostPrice => ParsedCost > 0m;
    public string RetailMarginText => FormatMargin(ParsedRetail, ParsedCost);
    public string WholesaleMarginText => FormatMargin(ParsedWholesale, ParsedCost);
    public string SpecialMarginText => FormatMargin(ParsedSpecial, ParsedCost);
    public bool IsRetailMarginNegative => ParsedCost > 0m && ParsedRetail > 0m && ParsedRetail < ParsedCost;
    public bool IsWholesaleMarginNegative => ParsedCost > 0m && ParsedWholesale > 0m && ParsedWholesale < ParsedCost;
    public bool IsSpecialMarginNegative => ParsedCost > 0m && ParsedSpecial > 0m && ParsedSpecial < ParsedCost;

    public bool HasPriceOrderWarning
    {
        get
        {
            if (ParsedRetail <= 0m || (ParsedWholesale <= 0m && ParsedSpecial <= 0m))
                return false;

            return (ParsedWholesale > 0m && ParsedWholesale > ParsedRetail)
                   || (ParsedSpecial > 0m && ParsedSpecial > ParsedRetail)
                   || (ParsedWholesale > 0m && ParsedSpecial > 0m && ParsedSpecial >= ParsedWholesale);
        }
    }

    public Action<bool>? RequestCloseWithResult { get; set; }
    public ActionCommand GenerateBarcodeCommand { get; }
    public ActionCommand EstimatePricesCommand { get; }
    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    public EditProductViewModel(
        PosProductDesignItem product,
        IReadOnlyCollection<PosProductDesignItem> allProducts,
        IReadOnlyList<string> categories,
        Action<EditProductSubmission> onSave)
    {
        this.product = product;
        this.allProducts = allProducts;
        Categories = categories.Contains(product.Category) || string.IsNullOrWhiteSpace(product.Category)
            ? categories
            : [.. categories, product.Category];

        productName = product.Name;
        barcode = product.Barcode;
        selectedCategory = product.Category;
        selectedUnit = product.UnitDisplay;
        costPriceText = product.CostPrice > 0m ? PosFormatting.FormatPlainNumber(product.CostPrice) : "0";
        retailPriceText = PosFormatting.FormatPlainNumber(product.RetailPrice);
        wholesalePriceText = PosFormatting.FormatPlainNumber(product.WholesalePrice);
        specialPriceText = PosFormatting.FormatPlainNumber(product.SpecialPrice);
        minStockAlertText = product.MinStockAlert.ToString(CultureInfo.InvariantCulture);
        isActiveForSale = product.IsActiveForSale;
        trackSerialNumber = product.TrackSerialNumber;
        notes = product.Notes;

        GenerateBarcodeCommand = new ActionCommand(() =>
        {
            long candidate = 6221001011;
            while (this.allProducts.Any(p => p.Id != this.product.Id && p.Barcode.Equals(candidate.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                   || candidate.ToString(CultureInfo.InvariantCulture) == Barcode.Trim())
            {
                candidate++;
            }

            Barcode = candidate.ToString(CultureInfo.InvariantCulture);
        });

        EstimatePricesCommand = new ActionCommand(() =>
        {
            if (!PosFormatting.TryParseAmount(RetailPriceText, out var retail) || retail <= 0m)
            {
                Error = "أدخل سعر القطاعي أولًا ليتم تقدير سعر الجملة والسعر المميز.";
                return;
            }

            WholesalePriceText = PosFormatting.FormatPlainNumber(Math.Round(retail * 0.90m, 2));
            SpecialPriceText = PosFormatting.FormatPlainNumber(Math.Round(retail * 0.85m, 2));
        });

        SaveCommand = new ActionCommand(() =>
        {
            var trimmedName = ProductName.Trim();
            var trimmedBarcode = Barcode.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                Error = "يرجى إدخال اسم الصنف / المنتج.";
                return;
            }

            if (string.IsNullOrWhiteSpace(trimmedBarcode))
            {
                Error = "يرجى إدخال الباركود.";
                return;
            }

            if (this.allProducts.Any(p => p.Id != this.product.Id && p.Barcode.Equals(trimmedBarcode, StringComparison.OrdinalIgnoreCase)))
            {
                Error = $"الباركود «{trimmedBarcode}» مستخدم لصنف آخر.";
                return;
            }

            if (!PosFormatting.TryParseAmount(CostPriceText, out var cost) || cost < 0m)
            {
                Error = "سعر التكلفة غير صحيح.";
                return;
            }

            if (!PosFormatting.TryParseAmount(RetailPriceText, out var retail) || retail <= 0m)
            {
                Error = "يرجى إدخال سعر قطاعي صحيح أكبر من صفر.";
                return;
            }

            if (!PosFormatting.TryParseAmount(WholesalePriceText, out var wholesale) || wholesale <= 0m)
            {
                Error = "يرجى إدخال سعر جملة صحيح أكبر من صفر.";
                return;
            }

            if (!PosFormatting.TryParseAmount(SpecialPriceText, out var special) || special <= 0m)
            {
                Error = "يرجى إدخال سعر مميز صحيح أكبر من صفر.";
                return;
            }

            if (!PosFormatting.TryParseNonNegativeInt(MinStockAlertText, out var minAlert))
            {
                Error = "حد التنبيه للنقص يجب أن يكون رقمًا صحيحًا غير سالب.";
                return;
            }

            onSave(new EditProductSubmission(
                trimmedName,
                trimmedBarcode,
                string.IsNullOrWhiteSpace(SelectedCategory) ? product.Category : SelectedCategory.Trim(),
                string.IsNullOrWhiteSpace(SelectedUnit) ? "قطعة" : SelectedUnit,
                cost,
                retail,
                wholesale,
                special,
                minAlert,
                IsActiveForSale,
                TrackSerialNumber,
                Notes.Trim()));
            RequestCloseWithResult?.Invoke(true);
        });

        CancelCommand = new ActionCommand(() => RequestCloseWithResult?.Invoke(false));
    }

    private void NotifyMargins()
    {
        Changed(nameof(HasCostPrice));
        Changed(nameof(RetailMarginText));
        Changed(nameof(WholesaleMarginText));
        Changed(nameof(SpecialMarginText));
        Changed(nameof(IsRetailMarginNegative));
        Changed(nameof(IsWholesaleMarginNegative));
        Changed(nameof(IsSpecialMarginNegative));
        Changed(nameof(HasPriceOrderWarning));
    }

    private static string FormatMargin(decimal sellPrice, decimal costPrice)
    {
        if (costPrice <= 0m || sellPrice <= 0m) return "—";
        var diff = sellPrice - costPrice;
        var pct = Math.Round((diff / costPrice) * 100m, 1);
        var sign = diff >= 0m ? "+" : "";
        return $"{sign}{PosFormatting.FormatPlainNumber(diff)} ج.م ({sign}{pct.ToString("0.#", CultureInfo.InvariantCulture)}%)";
    }
}

public sealed record MarkDamagedOption(string Label, string? SupplierFilter, int? SpecificUnitId);

public sealed record MarkDamagedSubmission(int Quantity, string? SupplierFilter, int? SpecificUnitId, string Reason);

public sealed class MarkDamagedViewModel : ObservableObject
{
    private readonly List<InventoryUnitRegistrationItem> availableUnits;
    private MarkDamagedOption selectedSourceOption;
    private string quantityText = "1";
    private string reasonText = "تالف عند الفحص";
    private string error = "";

    public PosProductDesignItem Product { get; }
    public IReadOnlyList<MarkDamagedOption> SourceOptions { get; }
    public IReadOnlyList<string> QuickReasons => InventoryDesignSeedData.DamageReasons;

    public MarkDamagedOption SelectedSourceOption
    {
        get => selectedSourceOption;
        set
        {
            if (Equals(selectedSourceOption, value)) return;
            selectedSourceOption = value;
            Changed();
            Changed(nameof(CanEditQuantity));
            Changed(nameof(MaxSelectableQuantity));
            if (!CanEditQuantity)
                QuantityText = "1";
            Error = "";
        }
    }

    public bool CanEditQuantity => SelectedSourceOption.SpecificUnitId is null;

    public int MaxSelectableQuantity
    {
        get
        {
            if (SelectedSourceOption.SpecificUnitId is not null)
                return 1;
            if (SelectedSourceOption.SupplierFilter is { } sup)
                return availableUnits.Count(u => u.SupplierName.Equals(sup, StringComparison.OrdinalIgnoreCase));
            return availableUnits.Count > 0 ? availableUnits.Count : Product.AvailableStock;
        }
    }

    public string QuantityText
    {
        get => quantityText;
        set { quantityText = value; Changed(); Error = ""; }
    }

    public string ReasonText
    {
        get => reasonText;
        set { reasonText = value; Changed(); Error = ""; }
    }

    public string Error
    {
        get => error;
        private set { error = value; Changed(); }
    }

    public Action<bool>? RequestCloseWithResult { get; set; }
    public RelayCommand<string> PickReasonCommand { get; }
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public MarkDamagedViewModel(
        PosProductDesignItem product,
        IEnumerable<InventoryUnitRegistrationItem> productRegistrations,
        Action<MarkDamagedSubmission> onConfirm)
    {
        Product = product;
        availableUnits = productRegistrations
            .Where(r => r.ProductId == product.Id && r.IsAvailable)
            .OrderByDescending(r => r.Id)
            .ToList();

        var options = new List<MarkDamagedOption>
        {
            new($"تحديد كمية من المتاح ({Math.Max(availableUnits.Count, product.AvailableStock)} متاح)", null, null)
        };

        foreach (var supGroup in availableUnits.GroupBy(u => u.SupplierName))
            options.Add(new($"من المورد: {supGroup.Key} ({supGroup.Count()} متاح)", supGroup.Key, null));

        foreach (var unit in availableUnits.Take(25))
            options.Add(new($"تسجيل محدد {unit.IdDisplay} — {unit.SupplierName} ({unit.PurchaseInvoiceId})", unit.SupplierName, unit.Id));

        SourceOptions = options;
        selectedSourceOption = options[0];

        PickReasonCommand = new RelayCommand<string>(r =>
        {
            if (!string.IsNullOrWhiteSpace(r))
                ReasonText = r;
        });

        ConfirmCommand = new ActionCommand(() =>
        {
            if (!int.TryParse(QuantityText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                Error = "يرجى إدخال كمية صحيحة أكبر من صفر.";
                return;
            }

            if (qty > MaxSelectableQuantity)
            {
                Error = $"الكمية المطلوبة ({qty}) أكبر من المتاح ({MaxSelectableQuantity}).";
                return;
            }

            var trimmedReason = ReasonText.Trim();
            if (string.IsNullOrWhiteSpace(trimmedReason))
            {
                Error = "يرجى إدخال سبب الإهلاك.";
                return;
            }

            onConfirm(new MarkDamagedSubmission(qty, SelectedSourceOption.SupplierFilter, SelectedSourceOption.SpecificUnitId, trimmedReason));
            RequestCloseWithResult?.Invoke(true);
        });

        CancelCommand = new ActionCommand(() => RequestCloseWithResult?.Invoke(false));
    }
}

public sealed class BarcodePrintPreviewViewModel : ObservableObject
{
    private string copiesText = "1";
    private string statusMessage = "";

    public string PreviewName { get; }
    public string PreviewBarcode { get; }

    public string CopiesText
    {
        get => copiesText;
        set { copiesText = value; Changed(); StatusMessage = ""; }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set { statusMessage = value; Changed(); Changed(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public Action? RequestClose { get; set; }
    public ActionCommand PrintPreviewCommand { get; }
    public ActionCommand CloseCommand { get; }

    public BarcodePrintPreviewViewModel(PosProductDesignItem product, Action<string> onPrintPreview)
    {
        PreviewName = product.Name;
        PreviewBarcode = product.Barcode;

        PrintPreviewCommand = new ActionCommand(() =>
        {
            if (!int.TryParse(CopiesText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies) || copies <= 0)
            {
                StatusMessage = "أدخل عدد ملصقات صحيح أكبر من صفر.";
                return;
            }

            var msg = $"وضع التصميم (بدون طابعة Backend): تمت معاينة طباعة {copies} ملصق باركود (50 × 25 مم) للصنف «{PreviewName}» ({PreviewBarcode}).";
            StatusMessage = msg;
            onPrintPreview(msg);
        });

        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }
}

public sealed class ConfirmActionViewModel
{
    public string Title { get; }
    public string Subtitle { get; }
    public string Message { get; }
    public string ConfirmButtonText { get; }
    public Action<bool>? RequestCloseWithResult { get; set; }
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public ConfirmActionViewModel(
        string title,
        string subtitle,
        string message,
        string confirmButtonText)
    {
        Title = title;
        Subtitle = subtitle;
        Message = message;
        ConfirmButtonText = confirmButtonText;
        ConfirmCommand = new ActionCommand(() => RequestCloseWithResult?.Invoke(true));
        CancelCommand = new ActionCommand(() => RequestCloseWithResult?.Invoke(false));
    }
}
