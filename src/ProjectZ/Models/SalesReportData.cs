namespace ProjectZ.Models;

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
public sealed record SalesReportEvent(ReportDocument Doc, SalesEventKind Kind, int SourceId, DateTime AtUtc,
    int? ActorId, string Actor, string Reference, string Detail, long? Quantity, decimal? SalesDelta, decimal? CostDelta,
    int? AccountId, byte? AccountKind, string? Account, decimal Received, decimal Refunded, int Position = 0)
{
    public ReportEventKey Key => new((int)Kind, SourceId, Position);
    public bool IsMoney => Kind is SalesEventKind.Collection or SalesEventKind.Refund;
    public bool NeedsReview => !IsMoney && (SalesDelta is null || CostDelta is null);
    public string KindLabel => KindText(Kind);
    public static string KindText(SalesEventKind kind) => kind switch { SalesEventKind.Issue => "إصدار بيع", SalesEventKind.Return => "مرتجع",
        SalesEventKind.ExchangeReturn => "استبدال • راجع", SalesEventKind.ExchangeIssue => "استبدال • بديل",
        SalesEventKind.Cancel => "إلغاء", SalesEventKind.Collection => "تحصيل", SalesEventKind.Refund => "رد مالي", _ => "غير معروف" };
    public string Channel => AccountKind switch { 0 => "نقدي", 1 => "فودافون كاش", 2 => "إنستاباي", _ => "" };
    public string AccountLabel => AccountId is null ? "" : $"{Channel} · {Account} (#{AccountId})";
}
public sealed record SalesReportData(ReportPeriod Period, DateTime ReadAtUtc, IReadOnlyList<ReportInvoice> Invoices,
    IReadOnlyList<ReportLine> Lines, IReadOnlyList<SalesReportEvent> Events);

public interface ISalesReportSource : IReportSource<SalesReportData> { }
