using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum ReturnExchangeType
{
    Return = 0,
    Exchange = 1
}

public sealed class ReturnExchangeOperationItem
{
    public int Id { get; init; }
    public string OperationNumber { get; init; } = "";
    public ReturnExchangeType OperationType { get; init; }
    public DateTime CreatedAt { get; init; }
    public string ActorName { get; init; } = "";
    public int InvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string CustomerPhone { get; init; } = "";
    public string Reason { get; init; } = "";

    public int GoodReturnedUnitsCount { get; init; }
    public int DamagedReturnedUnitsCount { get; init; }
    public int ReplacementUnitsCount { get; init; }

    public decimal ReturnedValue { get; init; }
    public decimal ReplacementValue { get; init; }
    public decimal NetDifference => ReplacementValue - ReturnedValue;

    public string ReturnedItemsSummary { get; init; } = "";
    public string ReplacementItemsSummary { get; init; } = "—";
    public string SettlementSummary { get; init; } = "";

    public bool IsExchange => OperationType == ReturnExchangeType.Exchange;
    public bool IsReturn => OperationType == ReturnExchangeType.Return;
    public string OperationTypeTitle => IsExchange ? "استبدال" : "مرتجع";
    public string CreatedDateDisplay => CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string CreatedTimeDisplay => CreatedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string CreatedFullDisplay => CreatedAt.ToString("yyyy-MM-dd · HH:mm", CultureInfo.InvariantCulture);
    public string CustomerPhoneDisplay => string.IsNullOrWhiteSpace(CustomerPhone) ? "—" : CustomerPhone;
    public string GoodUnitsSummary => $"{GoodReturnedUnitsCount} سليم";
    public string DamagedUnitsSummary => $"{DamagedReturnedUnitsCount} هالك";
    public bool HasDamagedUnits => DamagedReturnedUnitsCount > 0;
    public string ReturnedValueDisplay => PosFormatting.FormatCurrency(ReturnedValue);
    public string ReplacementValueDisplay => IsExchange ? PosFormatting.FormatCurrency(ReplacementValue) : "—";

    public string NetDifferenceDisplay
    {
        get
        {
            if (!IsExchange)
                return $"رد {PosFormatting.FormatCurrency(ReturnedValue)}";

            if (NetDifference > 0m)
                return $"فرق على العميل +{PosFormatting.FormatCurrency(NetDifference)}";

            if (NetDifference < 0m)
                return $"فرق للعميل -{PosFormatting.FormatCurrency(Math.Abs(NetDifference))}";

            return "متساوي (0.00 ج.م)";
        }
    }
}

public static class ReturnExchangeDesignSeedData
{
    public static IReadOnlyList<string> OperationReasons { get; } =
    [
        "تبديل موديل / سعة",
        "عيب صناعة",
        "تلف عند الفحص",
        "عدم ملاءمة الجهاز",
        "تراجع العميل",
        "عطل خلال فترة الضمان"
    ];

    public static ObservableCollection<ReturnExchangeOperationItem> CreateSharedSampleOperations(
        IReadOnlyList<SalesInvoiceDesignItem> invoices)
    {
        var list = new ObservableCollection<ReturnExchangeOperationItem>();
        var inv2 = invoices.FirstOrDefault(i => i.InvoiceNumber == "INV-2026-0002");
        if (inv2 is not null)
        {
            list.Add(new ReturnExchangeOperationItem
            {
                Id = 1,
                OperationNumber = "RET-2026-0001",
                OperationType = ReturnExchangeType.Return,
                CreatedAt = inv2.IssuedAt.AddDays(1).AddHours(2),
                ActorName = "المشرف",
                InvoiceId = inv2.Id,
                InvoiceNumber = inv2.InvoiceNumber,
                CustomerName = inv2.CustomerName,
                CustomerPhone = inv2.CustomerPhone,
                Reason = "عدم ملاءمة لوحة السيارة",
                GoodReturnedUnitsCount = 1,
                DamagedReturnedUnitsCount = 0,
                ReplacementUnitsCount = 0,
                ReturnedValue = 290m,
                ReplacementValue = 0m,
                ReturnedItemsSummary = "1× حامل موبايل مغناطيسي للسيارة (سليم ← المخزون)",
                ReplacementItemsSummary = "—",
                SettlementSummary = "رد للعميل: 290.00 ج.م (نقدي كاش)"
            });
        }

        return list;
    }
}
