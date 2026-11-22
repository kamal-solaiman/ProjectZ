using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum InventoryUnitStatus
{
    Available = 0,
    Sold = 1,
    Damaged = 2
}

/// <summary>One row of dbo.StockMovements rendered in the inventory ledger panel.</summary>
public sealed class StockMovementRow
{
    public DateTime OccurredAt { get; init; }
    public string MovementType { get; init; } = "";
    public int QuantityDelta { get; init; }
    public decimal UnitCost { get; init; }
    public string ActorName { get; init; } = "";
    public string Reason { get; init; } = "";
    public int? InventoryUnitId { get; init; }

    public string OccurredAtDisplay => OccurredAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Arabic label for the ledger movement type.</summary>
    public string MovementTypeDisplay => MovementType switch
    {
        "Supply" => "توريد",
        "Sale" => "بيع",
        "Damage" => "هالك",
        "DamageRestore" => "استرجاع هالك",
        "Opening" => "رصيد افتتاحي",
        "Return" => "مرتجع",
        _ => MovementType
    };

    public string QuantityDisplay => (QuantityDelta > 0 ? "+" : "") + QuantityDelta.ToString(CultureInfo.InvariantCulture);

    public string UnitCostDisplay => UnitCost.ToString("N2", CultureInfo.InvariantCulture);

    public string ReasonDisplay => string.IsNullOrWhiteSpace(Reason) ? "—" : Reason;

    public string ActorDisplay => string.IsNullOrWhiteSpace(ActorName) ? "غير محدد" : ActorName;

    public bool IsIncrease => QuantityDelta > 0;
}

public sealed record InventorySupplierSummary(string SupplierName, int AvailableCount, int SoldCount, int DamagedCount)
{
    public string SummaryText => $"{SupplierName}: {AvailableCount} متاح"
        + (SoldCount > 0 ? $" · {SoldCount} مباع" : "")
        + (DamagedCount > 0 ? $" · {DamagedCount} هالك" : "");
}

public sealed class InventoryUnitRegistrationItem : ObservableObject
{
    private string productName = "";
    private string productBarcode = "";
    private decimal purchasePrice;
    private string supplierName = "";
    private InventoryUnitStatus status = InventoryUnitStatus.Available;
    private string purchaseInvoiceId = "";
    private string saleInvoiceNumber = "";
    private string serialNumber = "";
    private string damageReason = "";
    private DateTime? damagedAt;
    private string damageSettlementStatus = "";

    public int Id { get; set; }
    public int ProductId { get; init; }
    public DateTime RegisteredAt { get; init; }
    public byte[]? DatabaseRowVersion { get; set; }

    public string ProductName
    {
        get => productName;
        set
        {
            if (productName == value) return;
            productName = value;
            Changed();
        }
    }

    public string ProductBarcode
    {
        get => productBarcode;
        set
        {
            if (productBarcode == value) return;
            productBarcode = value;
            Changed();
        }
    }

    public decimal PurchasePrice
    {
        get => purchasePrice;
        set
        {
            if (purchasePrice == value) return;
            purchasePrice = value;
            Changed();
            Changed(nameof(PurchasePriceDisplay));
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
        }
    }

    public InventoryUnitStatus Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            Changed();
            Changed(nameof(StatusTitle));
            Changed(nameof(IsAvailable));
            Changed(nameof(IsSold));
            Changed(nameof(IsDamaged));
        }
    }

    public string PurchaseInvoiceId
    {
        get => purchaseInvoiceId;
        set
        {
            if (purchaseInvoiceId == value) return;
            purchaseInvoiceId = value;
            Changed();
        }
    }

    public string SaleInvoiceNumber
    {
        get => saleInvoiceNumber;
        set
        {
            if (saleInvoiceNumber == value) return;
            saleInvoiceNumber = value;
            Changed();
        }
    }

    public string SerialNumber
    {
        get => serialNumber;
        set
        {
            if (serialNumber == value) return;
            serialNumber = value;
            Changed();
        }
    }

    public string DamageReason
    {
        get => damageReason;
        set
        {
            if (damageReason == value) return;
            damageReason = value;
            Changed();
        }
    }

    public DateTime? DamagedAt
    {
        get => damagedAt;
        set
        {
            if (damagedAt == value) return;
            damagedAt = value;
            Changed();
            Changed(nameof(DamagedAtDisplay));
        }
    }

    public string DamageSettlementStatus
    {
        get => damageSettlementStatus;
        set
        {
            if (damageSettlementStatus == value) return;
            damageSettlementStatus = value;
            Changed();
            Changed(nameof(IsReturnedToSupplier));
            Changed(nameof(CanMarkReturnedToSupplier));
        }
    }

    public string IdDisplay => $"#{Id.ToString(CultureInfo.InvariantCulture)}";
    public string PurchasePriceDisplay => PosFormatting.FormatCurrency(PurchasePrice);
    public string RegisteredAtDisplay => RegisteredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string DamagedAtDisplay => DamagedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    public string StatusTitle => Status switch
    {
        InventoryUnitStatus.Sold => "مباع",
        InventoryUnitStatus.Damaged => "هالك",
        _ => "متاح"
    };

    public bool IsAvailable => Status == InventoryUnitStatus.Available;
    public bool IsSold => Status == InventoryUnitStatus.Sold;
    public bool IsDamaged => Status == InventoryUnitStatus.Damaged;
    public bool IsReturnedToSupplier => string.Equals(DamageSettlementStatus, "مرتجع للمورد", StringComparison.Ordinal);
    public bool CanMarkReturnedToSupplier => IsDamaged && !IsReturnedToSupplier;
}

public static class InventoryDesignSeedData
{
    public static IReadOnlyList<string> DefaultSuppliers { get; } =
    [
        "أحمد علي",
        "محمود كامل",
        "شركة النور للتوزيع",
        "الفتح للإلكترونيات",
        "الرواد موبايل"
    ];

    public static IReadOnlyList<string> DamageReasons { get; } =
    [
        "تلف شحن",
        "عطل مصنعي",
        "كسر بالمحل",
        "تالف عند الفحص",
        "عطل بطارية / دائرة"
    ];

    public static ObservableCollection<InventoryUnitRegistrationItem> CreateSharedSampleRegistrations(
        IEnumerable<PosProductDesignItem> catalog)
    {
        var items = new ObservableCollection<InventoryUnitRegistrationItem>();
        var nextId = 1001;

        foreach (var product in catalog)
        {
            if (product.Id == 1)
            {
                // Matches user's example: existing stock from محمود كامل + 5 Type-C chargers bought from أحمد علي
                for (var i = 0; i < 19; i++)
                    items.Add(CreateUnit(nextId++, product, 300m, "محمود كامل", new DateTime(2026, 10, 15), InventoryUnitStatus.Available, "INV-101"));
                for (var i = 0; i < 2; i++)
                    items.Add(CreateUnit(nextId++, product, 300m, "محمود كامل", new DateTime(2026, 10, 15), InventoryUnitStatus.Sold, "INV-101"));
                items.Add(CreateDamagedUnit(nextId++, product, 300m, "محمود كامل", new DateTime(2026, 10, 15), "INV-101", "تلف منفذ الشحن أثناء الفحص", new DateTime(2026, 10, 29), "قيد المراجعة"));

                for (var i = 0; i < 5; i++)
                    items.Add(CreateUnit(nextId++, product, 310m, "أحمد علي", new DateTime(2026, 11, 5), InventoryUnitStatus.Available, "INV-118"));

                continue;
            }

            var primarySupplier = DefaultSuppliers[(product.Id) % DefaultSuppliers.Count];
            var secondarySupplier = DefaultSuppliers[(product.Id + 2) % DefaultSuppliers.Count];
            var primaryInvoice = $"INV-{100 + product.Id}";
            var secondaryInvoice = $"INV-{110 + product.Id}";
            var primaryDate = new DateTime(2026, 10, Math.Min(28, 10 + product.Id));
            var secondaryDate = new DateTime(2026, 11, Math.Min(9, 1 + (product.Id % 8)));

            var firstBatchAvailable = Math.Max(0, product.AvailableStock - Math.Min(4, product.AvailableStock / 3));
            var secondBatchAvailable = Math.Max(0, product.AvailableStock - firstBatchAvailable);

            for (var i = 0; i < firstBatchAvailable; i++)
                items.Add(CreateUnit(nextId++, product, product.CostPrice, primarySupplier, primaryDate, InventoryUnitStatus.Available, primaryInvoice));

            for (var i = 0; i < secondBatchAvailable; i++)
                items.Add(CreateUnit(nextId++, product, product.CostPrice, secondarySupplier, secondaryDate, InventoryUnitStatus.Available, secondaryInvoice));

            // Add 1 sample sold record for realistic registration history
            items.Add(CreateUnit(nextId++, product, product.CostPrice, primarySupplier, primaryDate, InventoryUnitStatus.Sold, primaryInvoice));

            // Add exact matching damaged records if seeded on product
            for (var d = 0; d < product.DamagedCount; d++)
            {
                var reason = product.Id == 4 ? "كسر بالمحل أثناء التركيب" : "تالف عند الفحص";
                var settlement = d == 1 ? "مرتجع للمورد" : "قيد المراجعة";
                items.Add(CreateDamagedUnit(
                    nextId++,
                    product,
                    product.CostPrice,
                    primarySupplier,
                    primaryDate,
                    primaryInvoice,
                    reason,
                    new DateTime(2026, 11, 6),
                    settlement));
            }
        }

        return items;
    }

    private static InventoryUnitRegistrationItem CreateUnit(
        int id,
        PosProductDesignItem product,
        decimal purchasePrice,
        string supplier,
        DateTime registeredAt,
        InventoryUnitStatus status,
        string invoiceId) => new()
    {
        Id = id,
        ProductId = product.Id,
        ProductName = product.Name,
        ProductBarcode = product.Barcode,
        PurchasePrice = purchasePrice,
        SupplierName = supplier,
        RegisteredAt = registeredAt,
        Status = status,
        PurchaseInvoiceId = invoiceId,
        SerialNumber = product.TrackSerialNumber ? $"3589{product.Id:00}{id:0000}12" : ""
    };

    private static InventoryUnitRegistrationItem CreateDamagedUnit(
        int id,
        PosProductDesignItem product,
        decimal purchasePrice,
        string supplier,
        DateTime registeredAt,
        string invoiceId,
        string reason,
        DateTime damagedAt,
        string settlementStatus) => new()
    {
        Id = id,
        ProductId = product.Id,
        ProductName = product.Name,
        ProductBarcode = product.Barcode,
        PurchasePrice = purchasePrice,
        SupplierName = supplier,
        RegisteredAt = registeredAt,
        Status = InventoryUnitStatus.Damaged,
        PurchaseInvoiceId = invoiceId,
        DamageReason = reason,
        DamagedAt = damagedAt,
        DamageSettlementStatus = settlementStatus
    };
}
