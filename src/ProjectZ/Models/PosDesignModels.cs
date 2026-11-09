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
    public string Unit { get; init; } = "قطعة";
    public decimal CostPrice { get; init; }
    public int AvailableStock { get; init; }
    public int MinStockAlert { get; init; } = 5;
    public decimal RetailPrice { get; init; }
    public decimal SpecialPrice { get; init; }
    public decimal WholesalePrice { get; init; }
    public bool TrackSerialNumber { get; init; }
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
        new PriceTierOption(PriceTier.Wholesale, "سعر جملة", "جملة"),
        new PriceTierOption(PriceTier.Special, "سعر مميز", "مميز")
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
            CostPrice = 310m,
            AvailableStock = 24,
            RetailPrice = 450m,
            WholesalePrice = 390m,
            SpecialPrice = 360m
        },
        new PosProductDesignItem
        {
            Id = 2,
            Barcode = "6221001002",
            Name = "كابل شحن سريع مضفر 1.5 متر",
            Category = "شواحن وكابلات",
            CostPrice = 95m,
            AvailableStock = 40,
            RetailPrice = 180m,
            WholesalePrice = 145m,
            SpecialPrice = 125m
        },
        new PosProductDesignItem
        {
            Id = 3,
            Barcode = "6221001003",
            Name = "سماعة بلوتوث لاسلكية Pro",
            Category = "سماعات",
            CostPrice = 670m,
            AvailableStock = 14,
            RetailPrice = 950m,
            WholesalePrice = 840m,
            SpecialPrice = 775m
        },
        new PosProductDesignItem
        {
            Id = 4,
            Barcode = "6221001004",
            Name = "اسكرينة حماية زجاج كامل 9H",
            Category = "حماية وإكسسوار",
            CostPrice = 40m,
            AvailableStock = 65,
            RetailPrice = 120m,
            WholesalePrice = 85m,
            SpecialPrice = 65m
        },
        new PosProductDesignItem
        {
            Id = 5,
            Barcode = "6221001005",
            Name = "جراب سيليكون مبطن مقاوم للصدمات",
            Category = "حماية وإكسسوار",
            CostPrice = 75m,
            AvailableStock = 38,
            RetailPrice = 160m,
            WholesalePrice = 125m,
            SpecialPrice = 100m
        },
        new PosProductDesignItem
        {
            Id = 6,
            Barcode = "6221001006",
            Name = "باور بانك 20000 مللي أمبير شحن سريع",
            Category = "شواحن وكابلات",
            CostPrice = 880m,
            AvailableStock = 11,
            RetailPrice = 1250m,
            WholesalePrice = 1090m,
            SpecialPrice = 990m
        },
        new PosProductDesignItem
        {
            Id = 7,
            Barcode = "6221001007",
            Name = "هاتف سامسونج Galaxy A55 5G 256GB",
            Category = "هواتف",
            CostPrice = 16800m,
            AvailableStock = 5,
            MinStockAlert = 2,
            TrackSerialNumber = true,
            RetailPrice = 18500m,
            WholesalePrice = 17900m,
            SpecialPrice = 17500m
        },
        new PosProductDesignItem
        {
            Id = 8,
            Barcode = "6221001008",
            Name = "هاتف ريدمي Note 13 Pro 256GB",
            Category = "هواتف",
            CostPrice = 11100m,
            AvailableStock = 7,
            MinStockAlert = 2,
            TrackSerialNumber = true,
            RetailPrice = 12400m,
            WholesalePrice = 11900m,
            SpecialPrice = 11550m
        },
        new PosProductDesignItem
        {
            Id = 9,
            Barcode = "6221001009",
            Name = "سماعة سلكية أصلية بمنفذ Type-C",
            Category = "سماعات",
            CostPrice = 145m,
            AvailableStock = 29,
            RetailPrice = 260m,
            WholesalePrice = 210m,
            SpecialPrice = 185m
        },
        new PosProductDesignItem
        {
            Id = 10,
            Barcode = "6221001010",
            Name = "حامل موبايل مغناطيسي للسيارة",
            Category = "حماية وإكسسوار",
            CostPrice = 160m,
            AvailableStock = 19,
            RetailPrice = 290m,
            WholesalePrice = 235m,
            SpecialPrice = 205m
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
