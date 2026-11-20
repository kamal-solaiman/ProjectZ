namespace ProjectZ.Models;

/// <summary>Inclusive Egypt business dates, half-open UTC query. Do not use the workstation's timezone.</summary>
public sealed class SalesReportPeriod
{
    private static readonly TimeZoneInfo Egypt = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    public DateTime From { get; }
    public DateTime Through { get; }
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }
    public static DateTime Today => Local(DateTime.UtcNow).Date;
    public string Label => $"{From:yyyy-MM-dd} — {Through:yyyy-MM-dd} (مصر)";

    public SalesReportPeriod(DateTime? from, DateTime? through)
    {
        if (from is null || through is null || through.Value.Date < from.Value.Date || through.Value.Date == DateTime.MaxValue.Date
            || (through.Value.Date - from.Value.Date).Days >= 366)
            throw new ArgumentException("اختر تاريخ بداية ونهاية بالترتيب، بحد أقصى 366 يومًا لكل تقرير.");
        From = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Unspecified);
        Through = DateTime.SpecifyKind(through.Value.Date, DateTimeKind.Unspecified);
        StartUtc = Boundary(From); EndUtc = Boundary(Through.AddDays(1));
    }
    private static DateTime Boundary(DateTime midnight)
    {
        while (Egypt.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(1);
        if (Egypt.IsAmbiguousTime(midnight)) return new DateTimeOffset(midnight, Egypt.GetAmbiguousTimeOffsets(midnight).Max()).UtcDateTime;
        return TimeZoneInfo.ConvertTimeToUtc(midnight, Egypt);
    }
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Egypt);
    public bool Contains(DateTime utc) => utc >= StartUtc && utc < EndUtc;
}

public sealed record ReportDocument(int Id, string Number, int? CustomerId, string Customer, int CashierId, string Cashier);
public sealed record ReportInvoice(ReportDocument Doc, DateTime IssuedUtc, byte Status, decimal Gross, decimal Discount,
    decimal Net, decimal Cost, decimal Received, decimal Refunded, decimal Due)
{
    public string StatusLabel => StatusText(Status);
    public static string StatusText(byte status) => status switch { 0 => "مكتملة", 1 => "آجل / متبقي", 2 => "مرتجع جزئي", 3 => "مرتجع كلي", 4 => "ملغاة", 5 => "مستبدلة", _ => "غير معروف" };
}
public sealed record ReportLine(int InvoiceId, int Number, int ProductId, string Product, string Category, string Barcode,
    string Unit, bool Replacement, int Quantity, int Returned, decimal UnitPrice, decimal UnitCost, decimal Gross,
    decimal BeforeReturn, decimal ReturnedValue, decimal Net, decimal Cost);

public enum SalesEventKind { Issue, Return, ExchangeReturn, ExchangeIssue, Cancel, Collection, Refund }
public readonly record struct SalesEventKey(SalesEventKind Kind, int SourceId, int Position);
public sealed record SalesReportEvent(ReportDocument Doc, SalesEventKind Kind, int SourceId, DateTime AtUtc,
    int? ActorId, string Actor, string Reference, string Detail, long? Quantity, decimal? SalesDelta, decimal? CostDelta,
    int? AccountId, byte? AccountKind, string? Account, decimal Received, decimal Refunded, int Position = 0)
{
    public SalesEventKey Key => new(Kind, SourceId, Position);
    public bool IsMoney => Kind is SalesEventKind.Collection or SalesEventKind.Refund;
    public bool NeedsReview => !IsMoney && (SalesDelta is null || CostDelta is null);
    public string KindLabel => KindText(Kind);
    public static string KindText(SalesEventKind kind) => kind switch { SalesEventKind.Issue => "إصدار بيع", SalesEventKind.Return => "مرتجع",
        SalesEventKind.ExchangeReturn => "استبدال • راجع", SalesEventKind.ExchangeIssue => "استبدال • بديل",
        SalesEventKind.Cancel => "إلغاء", SalesEventKind.Collection => "تحصيل", SalesEventKind.Refund => "رد مالي", _ => "غير معروف" };
    public string Channel => AccountKind switch { 0 => "نقدي", 1 => "فودافون كاش", 2 => "إنستاباي", _ => "" };
    public string AccountLabel => AccountId is null ? "" : $"{Channel} · {Account} (#{AccountId})";
}
public sealed record SalesReportData(SalesReportPeriod Period, DateTime ReadAtUtc, IReadOnlyList<ReportInvoice> Invoices,
    IReadOnlyList<ReportLine> Lines, IReadOnlyList<SalesReportEvent> Events);

public interface ISalesReportSource
{
    Task<SalesReportData> LoadAsync(SalesReportPeriod period, CancellationToken token);
}
