using System.Globalization;

namespace ProjectZ.Models;

// A single typed projection feeds the grid, A4 and Excel. Never recalculate financial figures in a printer/exporter.
public sealed record SalesReportRow(int[] InvoiceIds, object?[] Cells)
{
    public SalesEventKey[]? EventKeys { get; init; }
    private string[]? display;
    public string[] Display => display ??= Array.ConvertAll(Cells, Format);
    public static string Format(object? value) => value switch {
        null => "—", decimal n => n.ToString("N2", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), _ => value.ToString() ?? "" };
}
public sealed record SalesReportTable(string Title, string[] Columns, IReadOnlyList<SalesReportRow> Rows, bool Grouped = false);
public sealed record SalesReportMetric(string Label, decimal? Value, string Unit)
{
    public string Display => Value is null ? "غير مكتمل" : $"{SalesReportRow.Format(Value)} {Unit}";
}
public sealed record SalesReportPage(string Title, string Context, IReadOnlyList<SalesReportMetric> Metrics,
    IReadOnlyList<SalesReportTable> Tables, string Warning);
public sealed record SalesReportFilter(string Search = "", int? CashierId = null, string Detail = "", IReadOnlySet<int>? InvoiceIds = null, string DrillLabel = "",
    int? StatusOrKind = null, int? AccountId = null, IReadOnlySet<SalesEventKey>? EventKeys = null);

public static class SalesReportProjection
{
    private static SalesReportRow Row(int doc, params object?[] cells) => new([doc], cells);
    private static SalesReportRow Group(IEnumerable<int> ids, params object?[] cells) => new(ids.Distinct().ToArray(), cells);
    private static SalesReportRow EventGroup(IEnumerable<SalesReportEvent> events, params object?[] cells) =>
        new(events.Select(e=>e.Doc.Id).Distinct().ToArray(), cells) { EventKeys = events.Select(e=>e.Key).ToArray() };
    private static SalesReportTable Table(string title, string headers, IEnumerable<SalesReportRow> rows, bool grouped = false) =>
        new(title, headers.Split('|'), rows.ToList(), grouped);
    private static decimal? Margin(decimal net, decimal profit) => net == 0 ? null : decimal.Round(100 * profit / net, 2);
    private static bool Has(string text, string search) => text.Contains(search, StringComparison.OrdinalIgnoreCase);
    private static string Customer(ReportDocument d) => d.CustomerId is null ? $"{d.Customer} (نقدي عام)" : $"{d.Customer} (#{d.CustomerId})";

    public static SalesReportPage Build(SalesReportData data, bool movement, SalesReportFilter filter, string currency)
    {
        var search = filter.Search.Trim(); var detail = filter.Detail.Trim();
        bool Match(ReportDocument d) => (filter.CashierId is null || d.CashierId == filter.CashierId)
            && (filter.InvoiceIds is null || filter.InvoiceIds.Contains(d.Id)) && (Has(d.Number, search) || Has(d.Customer, search));
        var context = $"{data.Period.Label} | قراءة {SalesReportPeriod.Local(data.ReadAtUtc):yyyy-MM-dd HH:mm} | {currency}"
            + (search.Length == 0 ? "" : $" | رقم/عميل: {search}") + (filter.CashierId.HasValue ? $" | كاشير الفاتورة #{filter.CashierId}" : "")
            + (detail.Length == 0 ? "" : (movement ? " | بيان حركة: " : " | فواتير تتضمن: ") + detail)
            + (filter.StatusOrKind is not int kind ? "" : " | " + (movement ? SalesReportEvent.KindText((SalesEventKind)kind) : ReportInvoice.StatusText((byte)kind)))
            + (filter.AccountId is null ? "" : $" | {(movement ? "حركة الحساب" : "فواتير تعاملت مع الحساب")} #{filter.AccountId}")
            + (filter.InvoiceIds is null ? "" : $" | {(movement ? "حركات المجموعة" : "فواتير المجموعة")}: {filter.DrillLabel}");
        if (movement)
        {
            var events = data.Events.Where(e => data.Period.Contains(e.AtUtc) && Match(e.Doc)
                && (filter.EventKeys is null || filter.EventKeys.Contains(e.Key))
                && (filter.StatusOrKind is null || (int)e.Kind == filter.StatusOrKind) && (filter.AccountId is null || e.AccountId == filter.AccountId)
                && (detail.Length == 0 || Has(e.KindLabel, detail) || Has(e.Detail, detail) || Has(e.AccountLabel, detail) || Has(e.Actor, detail) || Has(e.Reference, detail))).ToArray();
            return Activity(events, context, currency);
        }
        var itemMatches = detail.Length == 0 ? null : data.Lines.Where(l => Has(l.Product, detail) || Has(l.Category, detail) || Has(l.Barcode, detail))
            .Select(l => l.InvoiceId).ToHashSet();
        var accountDocs = filter.AccountId is null ? null : data.Events.Where(e => e.IsMoney && e.AccountId == filter.AccountId).Select(e => e.Doc.Id).ToHashSet();
        var docs = data.Invoices.Where(i => Match(i.Doc) && (filter.StatusOrKind is null || i.Status == filter.StatusOrKind)
            && (accountDocs is null || accountDocs.Contains(i.Doc.Id)) && (itemMatches is null || itemMatches.Contains(i.Doc.Id))).ToArray();
        var byId = docs.ToDictionary(i => i.Doc.Id);
        var lines = data.Lines.Where(l => byId.ContainsKey(l.InvoiceId)).ToArray();
        var history = data.Events.Where(e => byId.ContainsKey(e.Doc.Id)).ToArray();
        var net = docs.Sum(i => i.Net); var cost = docs.Sum(i => i.Cost);
        var received = docs.Sum(i => i.Received); var refunded = docs.Sum(i => i.Refunded);
        SalesReportTable People(string title, Func<ReportInvoice,int?> key, Func<ReportInvoice,string> label) => Table(title,
            "البيان|فواتير|إجمالي البنود|الخصم|صافي البيع|تكلفة المباع|مجمل الربح|الهامش %|محصل|مردود مالي|المتبقي",
            docs.GroupBy(key).OrderByDescending(g => g.Sum(i => i.Net)).Select(g => {
                var value = g.Sum(i => i.Net); var c = g.Sum(i => i.Cost);
                return Group(g.Select(i => i.Doc.Id), label(g.First()), g.Count(), g.Sum(i => i.Gross), g.Sum(i => i.Discount), value, c, value-c,
                    Margin(value,value-c), g.Sum(i => i.Received), g.Sum(i => i.Refunded), g.Sum(i => i.Due)); }), true);
        long Kept(ReportLine l) => byId[l.InvoiceId].Status is 3 or 4 ? 0L : l.Quantity-l.Returned;
        SalesReportTable Items<TKey>(string title, Func<ReportLine,TKey> key, Func<ReportLine,string> label) => Table(title,
            "البيان|فواتير|كمية مدرجة|كمية باقية|إجمالي البنود|خصم موزع|صافي البيع|تكلفة المباع|مجمل الربح|الهامش %",
            lines.GroupBy(key).OrderByDescending(g => g.Sum(l => l.Net)).Select(g => {
                var ids = g.Select(l => l.InvoiceId).Distinct().ToArray(); var v = g.Sum(l => l.Net); var c = g.Sum(l => l.Cost);
                return Group(ids, label(g.First()), ids.Length, g.Sum(l => (long)l.Quantity), g.Sum(Kept),g.Sum(l => l.Gross),g.Sum(l => l.Gross-l.BeforeReturn),v,c,v-c,Margin(v,v-c)); }), true);
        var tables = new List<SalesReportTable> {
            Table("الفواتير", "الفاتورة|الإصدار (مصر)|العميل|الكاشير|الحالة|إجمالي البنود|الخصم|صافي البيع|تكلفة المباع|مجمل الربح|الهامش %|محصل|مردود مالي|صافي المحصل|المتبقي",
                docs.Select(i => Row(i.Doc.Id, i.Doc.Number,SalesReportPeriod.Local(i.IssuedUtc),Customer(i.Doc),i.Doc.Cashier,i.StatusLabel,i.Gross,i.Discount,i.Net,i.Cost,
                    i.Net-i.Cost,Margin(i.Net,i.Net-i.Cost),i.Received,i.Refunded,i.Received-i.Refunded,i.Due))),
            Table("بنود الفواتير", "الفاتورة|الصنف|التصنيف|الباركود|الوحدة|نوع البند|الكمية|مسحوب أو ملغى|الباقي|سعر الوحدة|تكلفة الوحدة|الإجمالي|خصم موزع|صافي قبل السحب|قيمة مسحوبة|صافي البيع|تكلفة المباع|مجمل الربح",
                lines.Select(l => Row(l.InvoiceId,byId[l.InvoiceId].Doc.Number,l.Product,l.Category,l.Barcode,l.Unit,l.Replacement?"بديل":"أصلي",l.Quantity,l.Returned,Kept(l),l.UnitPrice,l.UnitCost,
                    l.Gross,l.Gross-l.BeforeReturn,l.BeforeReturn,l.ReturnedValue,l.Net,l.Cost,l.Net-l.Cost))),
            Items("أداء الأصناف", l => l.ProductId, l => $"{l.Product} (#{l.ProductId})"),
            Items("أداء التصنيفات", l => l.Category, l => l.Category),
            People("العملاء", i => i.Doc.CustomerId, i => Customer(i.Doc)),
            People("الكاشير", i => i.Doc.CashierId, i => $"{i.Doc.Cashier} (#{i.Doc.CashierId})"),
            Accounts(history), EventTable("الدفعات الفعلية", history.Where(e => e.IsMoney)),
            EventTable("المرتجعات والاستبدال والإلغاء", history.Where(e => !e.IsMoney && e.Kind!=SalesEventKind.Issue))
        };
        return new("فواتير الفترة • الحالة الحالية", context,
            [new("عدد الفواتير",docs.Length,"فاتورة"),new("صافي المبيعات",net,currency),new("تكلفة المباع",cost,currency),new("مجمل الربح",net-cost,currency),
             new("صافي المحصل",received-refunded,currency),new("المتبقي",docs.Sum(i=>i.Due),currency)], tables, Warning(history));
    }

    private static SalesReportTable Accounts(IEnumerable<SalesReportEvent> events) => Table("حسابات السداد", "الحساب الفعلي|القناة|دفعات وردود|تحصيل|رد مالي|صافي الحركة",
        events.Where(e => e.IsMoney).GroupBy(e => e.AccountId).OrderBy(g => g.Key).Select(g => EventGroup(g,g.First().AccountLabel,g.First().Channel,g.Count(),
            g.Sum(e=>e.Received),g.Sum(e=>e.Refunded),g.Sum(e=>e.Received-e.Refunded))),true);
    private static SalesReportTable EventTable(string title, IEnumerable<SalesReportEvent> events) => Table(title,
        "الفاتورة|التاريخ (مصر)|الحركة|المستند أو السند|البيان|العميل|منفذ الحركة|الكمية ±|أثر البيع ±|أثر التكلفة ±|أثر الربح ±|الحساب الفعلي|تحصيل|رد مالي|المطابقة",
        events.Select(e=>Row(e.Doc.Id,e.Doc.Number,SalesReportPeriod.Local(e.AtUtc),e.KindLabel,e.Reference,e.Detail,Customer(e.Doc),e.Actor,e.Quantity,
            e.SalesDelta,e.CostDelta,e.SalesDelta-e.CostDelta,e.AccountLabel,e.Received,e.Refunded,e.NeedsReview?"قيمة تاريخية غير موثقة":"")));
    private static string Warning(IEnumerable<SalesReportEvent> events)
    {
        var n=events.Count(e=>e.NeedsReview);
        return n==0 ? "" : $"{n} بند حركة بلا قيمة تاريخية موثقة. علامة — ليست صفرًا، ولا يُقدَّر المبلغ من دفعة أو سعر اليوم.";
    }
    private sealed record EventTotals(decimal? Net,decimal? Cost,decimal Received,decimal Refunded)
    {
        public static EventTotals For(IEnumerable<SalesReportEvent> rows)
        {
            decimal? net=0m,cost=0m; decimal received=0,refunded=0;
            foreach (var e in rows) { if (!e.IsMoney) { net+=e.SalesDelta; cost+=e.CostDelta; } received+=e.Received; refunded+=e.Refunded; }
            return new(net,cost,received,refunded);
        }
    }
    private static SalesReportPage Activity(SalesReportEvent[] events,string context,string currency)
    {
        var sum=EventTotals.For(events);
        SalesReportTable By<TKey>(string title,Func<SalesReportEvent,TKey> key,Func<SalesReportEvent,string> label) => Table(title,
            "البيان|فواتير متأثرة|أسطر حركة|أثر البيع ±|أثر التكلفة ±|أثر الربح ±|تحصيل فعلي|رد مالي|صافي حركة المال",
            events.GroupBy(key).Select(g=>{ var ids=g.Select(e=>e.Doc.Id).Distinct().ToArray(); var s=EventTotals.For(g);
                return EventGroup(g,label(g.First()),ids.Length,g.Count(),s.Net,s.Cost,s.Net-s.Cost,s.Received,s.Refunded,s.Received-s.Refunded); }),true);
        return new("حركة الفترة • بحسب تاريخ الحركة",context,
            [new("صافي حركة البيع",sum.Net,currency),new("حركة التكلفة",sum.Cost,currency),new("حركة مجمل الربح",sum.Net-sum.Cost,currency),
             new("تحصيل فعلي",sum.Received,currency),new("رد مالي فعلي",sum.Refunded,currency),new("صافي حركة المال",sum.Received-sum.Refunded,currency)],
            [EventTable("دفتر الحركات",events),By("حركة يومية",e=>SalesReportPeriod.Local(e.AtUtc).Date,e=>SalesReportPeriod.Local(e.AtUtc).ToString("yyyy-MM-dd")),
             By("أنواع الحركات",e=>e.Kind,e=>e.KindLabel),By("منفذو الحركات",e=>e.ActorId,e=>$"{e.Actor} (#{e.ActorId})"),
             By("حركة العملاء",e=>e.Doc.CustomerId,e=>Customer(e.Doc)),Accounts(events)], Warning(events));
    }
}
