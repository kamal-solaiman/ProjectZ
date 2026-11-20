using System.Globalization;

namespace ProjectZ.Models;

/// <summary>Inclusive Egypt business dates, half-open UTC query. Do not use the workstation's timezone.</summary>
public sealed class ReportPeriod
{
    private static readonly TimeZoneInfo Egypt = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    public DateTime From { get; }
    public DateTime Through { get; }
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }
    public static DateTime Today => Local(DateTime.UtcNow).Date;
    public string Label => $"{From:yyyy-MM-dd} — {Through:yyyy-MM-dd} (مصر)";

    public ReportPeriod(DateTime? from, DateTime? through)
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

public interface IReportSource<T>
{
    Task<T> LoadAsync(ReportPeriod period, CancellationToken token);
}
public readonly record struct ReportEventKey(int Kind, int SourceId, int Position = 0);

// A single typed projection feeds the grid, A4 and Excel. Never recalculate financial figures in a printer/exporter.
public sealed class ReportRow(int[] invoiceIds, object?[] cells)
{
    public int[] InvoiceIds { get; } = invoiceIds;
    public object?[] Cells { get; } = cells;
    public int? SupplierId { get; init; }
    public int? RelatedInvoiceId { get; init; }
    public bool CanOpenInvoice { get; init; } = true;
    public ReportEventKey[]? EventKeys { get; init; }
    private string[]? display;
    public string[] Display => display ??= Array.ConvertAll(Cells, Format);
    public static string Format(object? value) => value switch {
        null => "—", decimal n => n.ToString("N2", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), _ => value.ToString() ?? "" };
}
public sealed record ReportTable(string Title, string[] Columns, IReadOnlyList<ReportRow> Rows, bool Grouped = false);
public sealed record ReportMetric(string Label, decimal? Value, string Unit, string Missing = "غير مكتمل")
{
    public string Display => Value is null ? Missing : $"{ReportRow.Format(Value)} {Unit}";
}
public sealed record ReportPage(string Title, string Context, IReadOnlyList<ReportMetric> Metrics,
    IReadOnlyList<ReportTable> Tables, string Warning, string Section);
public sealed record ReportFilter(string Search = "", int? IssuerId = null, string Detail = "", IReadOnlySet<int>? InvoiceIds = null, string DrillLabel = "",
    int? StatusOrKind = null, int? AccountId = null, IReadOnlySet<ReportEventKey>? EventKeys = null, int? SupplierId = null);
