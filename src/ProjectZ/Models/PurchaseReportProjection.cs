using static ProjectZ.Models.PurchaseFactKind;

namespace ProjectZ.Models;

/// <summary>Project one consistent SQL read. Neither the WPF view, A4 nor Excel recalculates settlement.</summary>
public static class PurchaseReportProjection
{
    public const string Section = "تقارير المشتريات";
    private static DateTime Local(DateTime utc) => ReportPeriod.Local(utc);
    private static bool Has(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);
    private static ReportTable Table(string title, string headers, IEnumerable<ReportRow> rows, bool grouped = false) =>
        new(title, headers.Split('|'), rows.ToArray(), grouped);
    private static ReportRow Row(PurchaseReportDocument doc, object?[] cells, int? related = null) => new([doc.Id], cells) {
        CanOpenInvoice = doc.CanOpen, SupplierId = doc.CanOpenSupplier ? doc.SupplierId : null, RelatedInvoiceId = related };
    private static ReportRow Group(PurchaseReportData data, IEnumerable<int> invoiceIds, object?[] cells, ReportEventKey[]? keys = null)
    {
        var ids = invoiceIds.Distinct().ToArray();
        var doc = data.Documents[ids[0]];
        return new(ids, cells) { EventKeys = keys, SupplierId = doc.CanOpenSupplier && ids.All(id => data.Documents[id].SupplierId == doc.SupplierId) ? doc.SupplierId : null };
    }

    public static ReportPage Build(PurchaseReportData data, bool movement, ReportFilter f, string currency)
    {
        string search = f.Search.Trim(), detail = f.Detail.Trim();
        bool Search(PurchaseReportDocument d) => Has(d.Number, search) || Has(d.Supplier, search) || Has(d.SupplierReference, search);
        bool Scope(PurchaseReportDocument d) => (!f.IssuerId.HasValue || d.BuyerId == f.IssuerId)
            && (!f.SupplierId.HasValue || d.SupplierId == f.SupplierId) && (f.InvoiceIds is null || f.InvoiceIds.Contains(d.Id));
        string context = $"{data.Period.Label} | قراءة {Local(data.ReadAtUtc):yyyy-MM-dd HH:mm} | {currency}"
            + (search.Length == 0 ? "" : " | رقم/مورد/مرجع: " + search)
            + (detail.Length == 0 ? "" : (movement ? " | بيان: " : " | فواتير تتضمن: ") + detail)
            + (f.SupplierId.HasValue ? $" | المورد #{f.SupplierId}" : "") + (f.IssuerId.HasValue ? $" | مسجّل الفاتورة #{f.IssuerId}" : "")
            + (f.AccountId.HasValue ? $" | {(movement ? "حركات الحساب" : "فواتير تعاملت مع الحساب")} #{f.AccountId}" : "")
            + (f.StatusOrKind is int k ? " | " + (movement ? PurchaseFact.KindText((PurchaseFactKind)k) : PurchaseReportInvoice.StatusText(k)) : "")
            + (f.InvoiceIds is null ? "" : " | المجموعة: " + f.DrillLabel);
        if (movement)
        {
            var events = data.Events.Where(e => data.Period.Contains(e.AtUtc) && Scope(data.Documents[e.InvoiceId])
                && (Search(data.Documents[e.InvoiceId]) || (e.RelatedId is int source && Search(data.Documents[source])))
                && (f.EventKeys is null || f.EventKeys.Contains(e.Key)) && (f.AccountId is null || e.AccountId == f.AccountId)
                && (f.StatusOrKind is null || (int)e.Kind == f.StatusOrKind) && (detail.Length == 0 || Has(e.Detail, detail)
                    || Has(PurchaseFact.KindText(e.Kind), detail) || Has(e.Actor, detail) || Has(e.Reference, detail) || Has(e.AccountLabel, detail))).ToArray();
            return Movement(data, events, context, currency);
        }
        var items = detail.Length == 0 ? null : data.Lines.Where(l => Has(l.Product, detail) || Has(l.Category, detail) || Has(l.Barcode, detail)).Select(l => l.InvoiceId).ToHashSet();
        var accounts = f.AccountId is null ? null : data.Events.Where(e => e.IsMoney && e.AccountId == f.AccountId).Select(e => e.InvoiceId).ToHashSet();
        var invoices = data.Invoices.Where(i => Scope(data.Documents[i.Id]) && Search(data.Documents[i.Id])
            && (f.StatusOrKind is null || i.Status == f.StatusOrKind) && (items is null || items.Contains(i.Id)) && (accounts is null || accounts.Contains(i.Id))).ToArray();
        var ids = invoices.Select(i => i.Id).ToHashSet();
        // An application belongs to BOTH parties to the settlement. List it once, even when both invoices match.
        var history = data.Events.Where(e => ids.Contains(e.InvoiceId) || (e.RelatedId is int source && ids.Contains(source))).ToArray();
        var lines = data.Lines.Where(l => ids.Contains(l.InvoiceId)).ToArray();
        ReportTable People(string title, Func<PurchaseReportDocument,int?> key, Func<PurchaseReportDocument,string> label) => Table(title,
            "البيان|فواتير|إجمالي البنود|قيمة الباقي قبل الخصم|الخصم الفعلي|صافي المشتريات|سداد|قبض مسجّل|إشعار صادر|مقاصة على الدين|مستحق للمورد|متاح للمحل",
            invoices.GroupBy(i => key(data.Documents[i.Id])).OrderByDescending(g => g.Sum(i => i.Net)).Select(g => Group(data, g.Select(i => i.Id),
                [label(data.Documents[g.First().Id]),g.Count(),g.Sum(i=>i.Gross),g.Sum(i=>i.ActiveGross),g.Sum(i=>i.EffectiveDiscount),g.Sum(i=>i.Net),
                 g.Sum(i=>i.Paid),g.Sum(i=>i.Received),g.Sum(i=>i.IssuedCredit),g.Sum(i=>i.AppliedToDebt),g.Sum(i=>i.Due),g.Sum(i=>i.Available)])), true);
        ReportTable Items<TKey>(string title, Func<PurchaseReportLine,TKey> key, Func<PurchaseReportLine,string> label) => Table(title,
            "البيان|فواتير|كمية مدرجة|كمية مرتجعة مسجلة|كمية باقية|إجمالي البنود|الباقي قبل الخصم|حصة الخصم التحليلية|الصافي التحليلي",
            lines.GroupBy(key).OrderByDescending(g => g.Sum(l => l.Net)).Select(g => Group(data, g.Select(l => l.InvoiceId),
                [label(g.First()),g.Select(l=>l.InvoiceId).Distinct().Count(),g.Sum(l=>(long)l.Quantity),g.Sum(l=>(long)l.Returned),g.Sum(l=>(long)l.Kept),
                 g.Sum(l=>l.Quantity*l.UnitCost),g.Sum(l=>l.ActiveGross),g.Sum(l=>l.Discount),g.Sum(l=>l.Net)])), true);
        var tables = new List<ReportTable> {
            Table("الفواتير", "الفاتورة|تاريخ المستند (مصر)|وقت التسجيل (مصر)|مرجع المورد|المورد|مسجّل الفاتورة|الحالة|إجمالي البنود|خصم الرأس|الباقي قبل الخصم|الخصم الفعلي|صافي المشتريات|سداد|قبض مسجّل|صافي المدفوع الدفتري|رصيد صادر|مقاصة على الدين|مستحق للمورد|متاح للمحل|المطابقة",
                invoices.Select(i => { var d=data.Documents[i.Id]; return Row(d,[d.Number,Local(i.IssuedUtc),Local(i.CreatedUtc),d.SupplierReference,d.SupplierLabel,d.BuyerLabel,
                    PurchaseReportInvoice.StatusText(i.Status),i.Gross,i.HeaderDiscount,i.ActiveGross,i.EffectiveDiscount,i.Net,i.Paid,i.Received,i.Paid-i.Received,
                    i.IssuedCredit,i.AppliedToDebt,i.Due,i.Available,i.NeedsReview?"تحتاج مطابقة":""]); })),
            Table("بنود الفواتير", "الفاتورة|رقم البند|الصنف|التصنيف|الباركود|الوحدة|كمية مدرجة|مرتجعة مسجلة|باقية|تكلفة شراء الوحدة|إجمالي البند|الباقي قبل الخصم|حصة الخصم التحليلية|الصافي التحليلي",
                lines.Select(l => { var d=data.Documents[l.InvoiceId]; return Row(d,[d.Number,l.Position,l.Product,l.Category,l.Barcode,l.Unit,l.Quantity,l.Returned,
                    l.Kept,l.UnitCost,l.UnitCost*l.Quantity,l.ActiveGross,l.Discount,l.Net]); })),
            Items("الأصناف",l=>l.ProductId,l=>$"{l.Product} (#{l.ProductId})"), Items("التصنيفات",l=>l.Category,l=>l.Category),
            People("الموردون",d=>d.SupplierId,d=>d.SupplierLabel), People("مسجّلو الشراء",d=>d.BuyerId,d=>d.BuyerLabel),
            Accounts(data,history), Ledger("السداد والقبض",data,history.Where(e=>e.IsMoney)),
            Ledger("المرتجع والإلغاء",data,history.Where(e=>e.Kind is Return or Cancel)),
            ReturnLines(data, data.ReturnLines.Where(l=>ids.Contains(l.InvoiceId))),
            Table("إشعارات المورد", "إشعار|فاتورة المصدر|مستند المرتجع|تاريخ الإصدار (مصر)|المورد|الصادر|استُخدم بالمقاصة|قُبض من الإشعار|المتاح الآن",
                data.Notes.Where(n=>ids.Contains(n.InvoiceId)).Select(n=> { var d=data.Documents[n.InvoiceId]; return Row(d,[n.Id,d.Number,n.ReturnNumber,Local(n.AtUtc),
                    d.SupplierLabel,n.Amount,n.Applied,n.Received,n.Available]); })),
            Ledger("المقاصات",data,history.Where(e=>e.Kind==CreditApplication)), Ledger("تاريخ المستندات",data,history.Where(e=>e.ChangesPurchases))
        };
        return new("فواتير الشراء • الحالة وقت القراءة",context,[new("عدد الفواتير",invoices.Length,"فاتورة"),new("صافي المشتريات",invoices.Sum(i=>i.Net),currency),
            new("السداد للمورد",invoices.Sum(i=>i.Paid),currency),new("قبض موثّق",history.Where(e=>e.Kind==Receipt).Sum(e=>e.Amount)??0,currency),
            new("مستحق للمورد",invoices.Sum(i=>i.Due),currency),new("رصيد متاح للمحل",invoices.Sum(i=>i.Available),currency)],
            tables,Warning(invoices.Count(i=>i.NeedsReview), history),Section);
    }

    private static ReportTable Ledger(string title, PurchaseReportData data, IEnumerable<PurchaseFact> events) => Table(title,
        "الفاتورة|التاريخ (مصر)|نوع الحركة|المرجع أو الإثبات|المورد|المنفّذ|البيان|أثر المستند ±|خفض الدين بالمرتجع|إشعار صادر|مقاصة|سداد|قبض موثّق|قبض غير مطابق|الحساب الفعلي|رقم الإشعار|فاتورة مصدر المقاصة|قيد القبض|المطابقة",
        events.Select(e => { var d=data.Documents[e.InvoiceId]; return Row(d,[d.NumberLabel,Local(e.AtUtc),PurchaseFact.KindText(e.Kind),e.Reference,d.SupplierLabel,e.Actor,
            e.Detail,e.DocumentDelta,e.DebtReduction,e.Kind==CreditIssue?e.Amount:null,e.Kind==CreditApplication?e.Amount:null,e.Kind==Payment?e.Amount:null,
            e.Kind==Receipt?e.Amount:null,e.Kind==UnverifiedReceipt?e.Amount:null,e.AccountLabel,e.CreditId,e.RelatedId is int id?data.Documents[id].NumberLabel:null,
            e.LedgerId,e.NeedsReview?"تحتاج مطابقة":""],e.RelatedId); }));
    private static ReportTable Accounts(PurchaseReportData data,IEnumerable<PurchaseFact> events) => Table("حسابات الدفع والقبض",
        "الحساب الفعلي|القناة|عدد الدفعات|مدفوع|قبض موثّق|قبض غير مطابق|صافي المدفوع الدفتري",
        events.Where(e=>e.IsMoney).GroupBy(e=>e.AccountId).OrderBy(g=>g.Key).Select(g=> {
            var s=Totals.Of(g); return Group(data,g.Select(e=>e.InvoiceId),[g.First().AccountLabel,g.First().Channel,g.Count(),s.Paid,s.Received,s.Unverified,s.Paid-s.Received-s.Unverified],g.Select(e=>e.Key).ToArray()); }),true);
    private static ReportTable ReturnLines(PurchaseReportData data, IEnumerable<PurchaseReportReturnLine> lines) => Table("بنود المرتجع",
        "مستند المرتجع|الفاتورة|تاريخ السحب (مصر)|الصنف|التصنيف|الباركود|الكمية|تكلفة الوحدة وقت السحب|القيمة قبل الخصم|وحدات موثقة|منها هالك",
        lines.Select(l=> { var d=data.Documents[l.InvoiceId]; return Row(d,[l.Number,d.Number,Local(l.AtUtc),l.Product,l.Category,l.Barcode,l.Quantity,l.UnitCost,
            l.Quantity*l.UnitCost,l.LinkedUnits,l.DamagedUnits]); }));
    private static string Warning(int invoices,IEnumerable<PurchaseFact> events)
    {
        int n=events.Count(e=>e.NeedsReview);
        return invoices+n==0 ? "" : $"للمطابقة: {invoices} فاتورة و{n} حركة. القيمة التاريخية غير الموثقة ليست صفرًا. القبض غير المطابق منفصل عن القبض الموثّق.";
    }
    private sealed class Totals
    {
        public decimal? Delta=0m, DebtReduction=0m;
        public decimal Paid, Received, Unverified, Issued, Applied;
        public static Totals Of(IEnumerable<PurchaseFact> events)
        {
            var s=new Totals();
            foreach (var e in events)
            {
                if(e.ChangesPurchases)s.Delta+=e.DocumentDelta; // null contaminates only the affected document-value total
                if(e.Kind is Return or Cancel)s.DebtReduction+=e.DebtReduction;
                switch(e.Kind) { case Payment:s.Paid+=e.Amount!.Value;break; case Receipt:s.Received+=e.Amount!.Value;break;
                    case UnverifiedReceipt:s.Unverified+=e.Amount!.Value;break; case CreditIssue:s.Issued+=e.Amount!.Value;break;
                    case CreditApplication:s.Applied+=e.Amount!.Value;break; }
            }
            return s;
        }
    }
    private static ReportPage Movement(PurchaseReportData data,PurchaseFact[] events,string context,string currency)
    {
        var sum=Totals.Of(events);
        ReportTable By<TKey>(string title,Func<PurchaseFact,TKey> key,Func<PurchaseFact,object> label) => Table(title,
            "البيان|أسطر حركة|أثر المستند ±|خفض الدين بالمرتجع|إشعار صادر|مقاصة|سداد|قبض موثّق|قبض غير مطابق|صافي المدفوع الدفتري",
            events.GroupBy(key).Select(g=> {var s=Totals.Of(g); return Group(data,g.Select(e=>e.InvoiceId),[label(g.First()),g.Count(),s.Delta,s.DebtReduction,
                s.Issued,s.Applied,s.Paid,s.Received,s.Unverified,s.Paid-s.Received-s.Unverified],g.Select(e=>e.Key).ToArray()); }),true);
        var returns=events.Where(e=>e.Origin==0 && e.Kind is Return or Cancel).Select(e=>e.SourceId).ToHashSet();
        return new("حركة المشتريات • حسب وقت تسجيل العملية",context,[new("صافي حركة الشراء",sum.Delta,currency),new("سداد للمورد",sum.Paid,currency),
            new("قبض موثّق",sum.Received,currency),new("قبض غير مطابق",sum.Unverified,currency),new("إشعارات صادرة",sum.Issued,currency),new("مقاصة غير نقدية",sum.Applied,currency)],
            [Ledger("دفتر الحركات",data,events),By("الحركة اليومية",e=>Local(e.AtUtc).Date,e=>DateOnly.FromDateTime(Local(e.AtUtc))),
             By("أنواع الحركات",e=>e.Kind,e=>PurchaseFact.KindText(e.Kind)),By("حركة الموردين",e=>data.Documents[e.InvoiceId].SupplierId,e=>data.Documents[e.InvoiceId].SupplierLabel),
             By("منفّذو الحركات",e=>e.ActorId,e=>e.ActorId.HasValue?$"{e.Actor} (#{e.ActorId})":e.Actor),Accounts(data,events),
             ReturnLines(data,data.ReturnLines.Where(l=>returns.Contains(l.ReturnId)))],Warning(0,events),Section);
    }
}
