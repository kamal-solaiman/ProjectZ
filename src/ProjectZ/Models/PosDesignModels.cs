using System.Globalization;

namespace ProjectZ.Models;

public enum PriceTier
{
    Retail = 0,
    Special = 1,
    Wholesale = 2
}

public enum PaymentPresetMode
{
    CashOnly = 0,
    VodafoneCashOnly = 1,
    InstaPayOnly = 2,
    Split = 3
}

public sealed record PriceTierOption(PriceTier Tier, string Title, string ShortTitle);

public sealed class PosProductDesignItem
{
    public int Id { get; init; }
    public string Barcode { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Sku { get; init; } = "";
    public string Unit { get; init; } = "قطعة";
    public decimal CostPrice { get; init; }
    public int AvailableStock { get; init; }
    public int MinStockAlert { get; init; } = 5;
    public string ShelfLocation { get; init; } = "";
    public decimal RetailPrice { get; init; }
    public decimal SpecialPrice { get; init; }
    public decimal WholesalePrice { get; init; }
    public bool TrackSerialNumber { get; init; }
    public bool IsTaxable { get; init; }
    public bool IsActiveForSale { get; init; } = true;
    public string Notes { get; init; } = "";
    public bool IsNewlyAdded { get; init; }

    public decimal GetPrice(PriceTier tier) => tier switch
    {
        PriceTier.Special => SpecialPrice,
        PriceTier.Wholesale => WholesalePrice,
        _ => RetailPrice
    };

    public string CostPriceDisplay => PosFormatting.FormatCurrency(CostPrice);
    public string RetailPriceDisplay => PosFormatting.FormatCurrency(RetailPrice);
    public string SpecialPriceDisplay => PosFormatting.FormatCurrency(SpecialPrice);
    public string WholesalePriceDisplay => PosFormatting.FormatCurrency(WholesalePrice);
    public string RetailBadgeText => $"قطاعي: {RetailPriceDisplay}";
    public string SpecialBadgeText => $"مميز: {SpecialPriceDisplay}";
    public string WholesaleBadgeText => $"جملة: {WholesalePriceDisplay}";
    public string StockDisplay => $"متاح: {AvailableStock}";
    public string UnitDisplay => string.IsNullOrWhiteSpace(Unit) ? "قطعة" : Unit;
    public string BrandAndSkuDisplay => string.IsNullOrWhiteSpace(Brand) && string.IsNullOrWhiteSpace(Sku)
        ? "غير محدد"
        : string.Join(" · ", new[] { Brand, Sku }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed class PosCustomerDesignItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Phone { get; init; } = "";
    public string SecondaryPhone { get; init; } = "";
    public string Address { get; init; } = "";
    public string Notes { get; init; } = "";
    public PriceTier DefaultPriceTier { get; init; } = PriceTier.Retail;
    public bool IsWalkIn { get; init; }

    public string DefaultPriceTierTitle => PosFormatting.GetPriceTierTitle(DefaultPriceTier);
    public string DisplayPhone => string.IsNullOrWhiteSpace(Phone) ? "بدون رقم هاتف" : Phone;
}

public static class PosFormatting
{
    public static IReadOnlyList<PriceTierOption> PriceTierOptions { get; } =
    [
        new PriceTierOption(PriceTier.Retail, "سعر قطاعي", "قطاعي"),
        new PriceTierOption(PriceTier.Special, "سعر مميز", "مميز"),
        new PriceTierOption(PriceTier.Wholesale, "سعر جملة", "جملة")
    ];

    public static string GetPriceTierTitle(PriceTier tier) => tier switch
    {
        PriceTier.Special => "سعر مميز",
        PriceTier.Wholesale => "سعر جملة",
        _ => "سعر قطاعي"
    };

    public static string GetPriceTierShortTitle(PriceTier tier) => tier switch
    {
        PriceTier.Special => "مميز",
        PriceTier.Wholesale => "جملة",
        _ => "قطاعي"
    };

    public static string FormatCurrency(decimal amount)
        => $"{amount.ToString("#,##0.00", CultureInfo.InvariantCulture)} ج.م";

    public static string FormatNumber(decimal amount)
        => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    public static string FormatPlainNumber(decimal amount)
        => amount == Math.Truncate(amount)
            ? amount.ToString("0", CultureInfo.InvariantCulture)
            : amount.ToString("0.##", CultureInfo.InvariantCulture);

    public static bool TryParseAmount(string? text, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0m;
            return true;
        }

        var normalized = text.Trim().Replace("٬", "").Replace(",", "");
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                   CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    public static bool TryParseNonNegativeInt(string? text, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
               && value >= 0;
    }
}

public static class PosDesignSeedData
{
    public static IReadOnlyList<string> CategoryFilters { get; } =
    [
        "الكل",
        "هواتف",
        "شواحن وكابلات",
        "سماعات",
        "حماية وإكسسوار"
    ];

    public static IReadOnlyList<string> ProductCategories { get; } =
    [
        "هواتف",
        "شواحن وكابلات",
        "سماعات",
        "حماية وإكسسوار",
        "صيانة وقطع غيار",
        "أجهزة لوحية وإكسسوارات",
        "أخرى"
    ];

    public static IReadOnlyList<string> ProductUnits { get; } =
    [
        "قطعة",
        "علبة",
        "كابل",
        "طقم",
        "دستة"
    ];

    public static System.Collections.ObjectModel.ObservableCollection<PosProductDesignItem> CreateSharedSampleCatalog()
        => new(CreateSampleCatalog());

    public static IReadOnlyList<PosProductDesignItem> CreateSampleCatalog() =>
    [
        new PosProductDesignItem
        {
            Id = 1,
            Barcode = "6221001001",
            Name = "شاحن أصلي سريع 25 واط Type-C",
            Category = "شواحن وكابلات",
            Brand = "Samsung",
            Sku = "CHG-25W-C",
            CostPrice = 310m,
            AvailableStock = 24,
            ShelfLocation = "رف A-1",
            RetailPrice = 450m,
            SpecialPrice = 410m,
            WholesalePrice = 370m
        },
        new PosProductDesignItem
        {
            Id = 2,
            Barcode = "6221001002",
            Name = "كابل شحن سريع مضفر 1.5 متر",
            Category = "شواحن وكابلات",
            Brand = "Anker",
            Sku = "CBL-USB-15",
            CostPrice = 95m,
            AvailableStock = 40,
            ShelfLocation = "رف A-2",
            RetailPrice = 180m,
            SpecialPrice = 155m,
            WholesalePrice = 130m
        },
        new PosProductDesignItem
        {
            Id = 3,
            Barcode = "6221001003",
            Name = "سماعة بلوتوث لاسلكية Pro",
            Category = "سماعات",
            Brand = "Soundcore",
            Sku = "AUD-PRO-03",
            CostPrice = 670m,
            AvailableStock = 14,
            ShelfLocation = "رف B-1",
            RetailPrice = 950m,
            SpecialPrice = 875m,
            WholesalePrice = 790m
        },
        new PosProductDesignItem
        {
            Id = 4,
            Barcode = "6221001004",
            Name = "اسكرينة حماية زجاج كامل 9H",
            Category = "حماية وإكسسوار",
            Brand = "Generic",
            Sku = "SCR-9H-04",
            CostPrice = 40m,
            AvailableStock = 65,
            ShelfLocation = "درج C-1",
            RetailPrice = 120m,
            SpecialPrice = 95m,
            WholesalePrice = 70m
        },
        new PosProductDesignItem
        {
            Id = 5,
            Barcode = "6221001005",
            Name = "جراب سيليكون مبطن مقاوم للصدمات",
            Category = "حماية وإكسسوار",
            Brand = "Silicone Pro",
            Sku = "CASE-05",
            CostPrice = 75m,
            AvailableStock = 38,
            ShelfLocation = "حامل D-2",
            RetailPrice = 160m,
            SpecialPrice = 135m,
            WholesalePrice = 105m
        },
        new PosProductDesignItem
        {
            Id = 6,
            Barcode = "6221001006",
            Name = "باور بانك 20000 مللي أمبير شحن سريع",
            Category = "شواحن وكابلات",
            Brand = "Anker",
            Sku = "PB-20K-06",
            CostPrice = 880m,
            AvailableStock = 11,
            ShelfLocation = "رف A-4",
            RetailPrice = 1250m,
            SpecialPrice = 1150m,
            WholesalePrice = 1020m
        },
        new PosProductDesignItem
        {
            Id = 7,
            Barcode = "6221001007",
            Name = "هاتف سامسونج Galaxy A55 5G 256GB",
            Category = "هواتف",
            Brand = "Samsung",
            Sku = "PHN-A55-256",
            CostPrice = 16800m,
            AvailableStock = 5,
            MinStockAlert = 2,
            ShelfLocation = "خزنة E-1",
            TrackSerialNumber = true,
            RetailPrice = 18500m,
            SpecialPrice = 18100m,
            WholesalePrice = 17650m
        },
        new PosProductDesignItem
        {
            Id = 8,
            Barcode = "6221001008",
            Name = "هاتف ريدمي Note 13 Pro 256GB",
            Category = "هواتف",
            Brand = "Xiaomi",
            Sku = "PHN-RN13P",
            CostPrice = 11100m,
            AvailableStock = 7,
            MinStockAlert = 2,
            ShelfLocation = "خزنة E-2",
            TrackSerialNumber = true,
            RetailPrice = 12400m,
            SpecialPrice = 12050m,
            WholesalePrice = 11700m
        },
        new PosProductDesignItem
        {
            Id = 9,
            Barcode = "6221001009",
            Name = "سماعة سلكية أصلية بمنفذ Type-C",
            Category = "سماعات",
            Brand = "Samsung",
            Sku = "AUD-TC-09",
            CostPrice = 145m,
            AvailableStock = 29,
            ShelfLocation = "رف B-3",
            RetailPrice = 260m,
            SpecialPrice = 225m,
            WholesalePrice = 190m
        },
        new PosProductDesignItem
        {
            Id = 10,
            Barcode = "6221001010",
            Name = "حامل موبايل مغناطيسي للسيارة",
            Category = "حماية وإكسسوار",
            Brand = "Baseus",
            Sku = "HLD-MAG-10",
            CostPrice = 160m,
            AvailableStock = 19,
            ShelfLocation = "رف D-4",
            RetailPrice = 290m,
            SpecialPrice = 250m,
            WholesalePrice = 210m
        }
    ];

    public static List<PosCustomerDesignItem> CreateSampleCustomers() =>
    [
        new PosCustomerDesignItem
        {
            Id = 0,
            Name = "عميل نقدي (عام)",
            Phone = "",
            DefaultPriceTier = PriceTier.Retail,
            IsWalkIn = true
        },
        new PosCustomerDesignItem
        {
            Id = 1,
            Name = "أحمد محمود علي",
            Phone = "01012345678",
            Address = "كفر الشيخ - شارع الخليفة المأمون",
            DefaultPriceTier = PriceTier.Retail
        },
        new PosCustomerDesignItem
        {
            Id = 2,
            Name = "محمد سامي إبراهيم",
            Phone = "01122334455",
            Address = "كفر الشيخ - تقسيم 2",
            DefaultPriceTier = PriceTier.Special
        },
        new PosCustomerDesignItem
        {
            Id = 3,
            Name = "محل النور للإكسسوارات",
            Phone = "01298765432",
            Address = "وسط البلد - برج السلام",
            DefaultPriceTier = PriceTier.Wholesale
        },
        new PosCustomerDesignItem
        {
            Id = 4,
            Name = "كريم حسن عبد الله",
            Phone = "01555443322",
            Address = "دسوق - شارع الشركات",
            DefaultPriceTier = PriceTier.Special
        }
    ];
}
