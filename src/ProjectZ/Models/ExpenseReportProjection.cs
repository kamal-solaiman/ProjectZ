namespace ProjectZ.Models;

/// <summary>The same typed rows feed the grid, paper and workbook. Never add a document to its allocation or its ledger.</summary>
public static class ExpenseReportProjection
{
    public const string Section = "تقارير المصروفات";
    public const string DocumentsTitle = "مصروفات بتاريخ المستند", PostingsTitle = "حركة بتاريخ الخصم";
    public const string CompareTotal = "مقارنة الفترة", CompareCategory = "مقارنة التصنيفات", CompareAccount = "مقارنة الحسابات";
    private const int DocumentKey = 0, ShareKey = 1, PostingKey = 2;
    private sealed record Fact(ReportEventKey Key, int SourceId, ExpenseDocument? Doc, DateOnly Day, bool Previous,
        int ActorId, int? AccountId, decimal Out, decimal In, ExpensePosting? Leg = null)
    {
        public decimal Cost => Out - In;
        public string Reference => Doc?.Reference ?? $"مصدر #{SourceId}";
    }

    private static ReportTable Table(string title, string columns, IEnumerable<ReportRow> rows, bool grouped = false) =>
        new(title, columns.Split('|'), rows.ToArray(), grouped);
    private static bool Match(string q, params string[] values) => q.Length == 0 || values.Any(v => v.Contains(q, StringComparison.OrdinalIgnoreCase));
    private static decimal? Ratio(decimal value, decimal basis) => basis <= 0 ? null : Math.Round(value / basis * 100m, 2, MidpointRounding.AwayFromZero);
    private static int DocumentCount(IEnumerable<Fact> facts) => facts.Where(f => f.Doc != null).Select(f => f.SourceId).Distinct().Count();
    private static ReportRow Group(IEnumerable<Fact> facts, object?[] cells, FinanceLink? account = null) =>
        new(facts.Select(f => f.SourceId).Distinct().ToArray(), cells) { EventKeys = facts.Select(f => f.Key).ToArray(), CanOpenInvoice = false, FinancialAccount = account };
    private static ReportRow Row(ExpenseReportData d, Fact f, object?[] cells, int? accountId = null) => new([f.SourceId], cells) {
        CanOpenInvoice = false, FinancialSource = f.Doc is null ? null : new(FinanceDestination.Expense, f.SourceId), FinancialAccount = d.AccountLink(accountId ?? f.AccountId) };

    public static string KindName(bool movement, int kind) => movement ? (kind == 1 ? "منصرف" : "وارد للمراجعة")
        : kind switch { 1 => "حساب واحد", 2 => "دفع مقسّم", _ => "بلا توزيع" };

    public static ReportPage Build(ExpenseReportData d, bool movement, ReportFilter f, string currency, int? categoryId = null, bool? matched = null)
    {
        var q = f.Search.Trim(); var detail = f.Detail.Trim();
        string basis = movement ? PostingsTitle : DocumentsTitle;
        var context = $"{basis}: {d.Period.Label} | المقارنة السابقة: {d.Previous.Label} | قراءة {ReportPeriod.Local(d.ReadAtUtc):yyyy-MM-dd HH:mm} | {currency}"
            + (categoryId is int c ? " | التصنيف: " + d.CategoryName(c) : "") + (f.IssuerId is int u ? " | الموظف: " + d.ActorName(u) : "")
            + (f.AccountId is int a ? " | " + (movement ? "قيود الحساب: " : "مستندات شارك فيها: ") + d.AccountName(a) : "")
            + (f.StatusOrKind is int k ? " | " + KindName(movement,k) : "") + (matched.HasValue ? (matched.Value ? " | مطابق فقط" : " | للمراجعة فقط") : "")
            + (q.Length > 0 ? " | بحث في المرجع" : "") + (detail.Length > 0 ? " | بحث في التصنيف أو الحساب أو الموظف" : "")
            + (f.InvoiceIds != null ? " | المجموعة: " + f.DrillLabel : ""); // Never echo free text (it might be a phone or payroll note).

        var primary = new List<Fact>(); var accounts = new List<Fact>();
        var first = DateOnly.FromDateTime(d.Period.From); var last = DateOnly.FromDateTime(d.Period.Through);
        var previousFirst = DateOnly.FromDateTime(d.Previous.From);
        bool Includes(int id, int? category, int actor, bool verified) => (f.InvoiceIds == null || f.InvoiceIds.Contains(id))
            && (categoryId == null || categoryId == category) && (f.IssuerId == null || f.IssuerId == actor) && (matched == null || matched == verified);
        if (!movement)
        {
            foreach (var doc in d.Documents.Values.OrderByDescending(i => i.Day).ThenByDescending(i => i.Id))
            {
                var proof = d.Proofs[doc.Id];
                if (doc.Day < previousFirst || doc.Day > last || !Includes(doc.Id, doc.CategoryId, doc.IssuerId, proof.OK)) continue;
                if (f.StatusOrKind is int kind && kind != Math.Min(2, proof.Shares.Count)) continue;
                if (f.AccountId is int account && !proof.Shares.Any(s => s.AccountId == account)) continue;
                if (!Match(q, doc.Reference, $"#{doc.Id}") || !Match(detail, d.CategoryName(doc.CategoryId), d.ActorName(doc.IssuerId))
                    && !proof.Shares.Any(s => Match(detail, d.AccountName(s.AccountId)))) continue;
                var item = new Fact(new(DocumentKey,doc.Id), doc.Id, doc, doc.Day, doc.Day < first, doc.IssuerId, null, doc.Amount, 0);
                primary.Add(item);
                foreach (var s in proof.Shares.Where(s => f.AccountId == null || s.AccountId == f.AccountId))
                    accounts.Add(item with { Key = new(ShareKey,s.Id), AccountId = s.AccountId, Out = s.Amount });
            }
        }
        else
        {
            foreach (var leg in d.Postings.OrderByDescending(l => l.AtUtc).ThenByDescending(l => l.Id))
            {
                bool previous = d.Previous.Contains(leg.AtUtc);
                if (!previous && !d.Period.Contains(leg.AtUtc)) continue; // lifetime proof is not dated spending
                var doc = d.Documents.GetValueOrDefault(leg.SourceId);
                if (!Includes(leg.SourceId, doc?.CategoryId, leg.ActorId, d.Proofs.GetValueOrDefault(leg.SourceId)?.OK == true)
                    || (f.StatusOrKind is int kind && kind != (leg.Incoming ? 2 : 1)) || (f.AccountId != null && f.AccountId != leg.AccountId)) continue;
                if (!Match(q, doc?.Reference ?? "", $"#{leg.SourceId}", $"#{leg.Id}")
                    || !Match(detail, d.CategoryName(doc?.CategoryId), d.ActorName(leg.ActorId), d.AccountName(leg.AccountId))) continue;
                primary.Add(new(new(PostingKey,leg.Id), leg.SourceId, doc, DateOnly.FromDateTime(ReportPeriod.Local(leg.AtUtc)), previous,
                    leg.ActorId, leg.AccountId, leg.Incoming ? 0 : leg.Amount, leg.Incoming ? leg.Amount : 0, leg));
            }
            accounts = primary;
        }

        if (f.EventKeys is { } keys)
        {
            // Account comparisons in document mode drill to PAYMENT keys, never the other shares of that invoice.
            var pool = !movement && keys.Any(key => key.Kind == ShareKey) ? accounts : primary;
            var exact = pool.Where(i => keys.Contains(i.Key)).ToArray();
            return Drill(d, exact, context, basis, currency, f.DrillTable is CompareTotal or CompareCategory or CompareAccount);
        }

        var current = primary.Where(i => !i.Previous).ToArray();
        var currentAccounts = accounts.Where(i => !i.Previous).ToArray();
        var total = current.Sum(i => i.Cost);
        bool Review(Fact i) => d.Proofs.GetValueOrDefault(i.SourceId)?.OK != true;
        int reviews = current.Count(Review);
        var tables = new List<ReportTable>();
        List<ReportMetric> metrics;
        if (!movement)
        {
            metrics = [new("مستندات",current.Length,"مستند"), new("إجمالي المستندات",total,currency),
                new("متوسط المصروف",current.Length == 0 ? null : total / current.Length,currency,"لا مستندات"),
                new("حصص الحسابات المعروضة",currentAccounts.Sum(i => i.Cost),currency), new("مستندات للمراجعة",reviews,"مستند")];
            tables.Add(Table("مستندات الفترة", "رقم المصروف|تاريخ المصروف|وقت التسجيل بمصر|التصنيف|الموظف المسجّل|مبلغ المستند|وسائل الدفع|مجموع التوزيع|صافي الخصم المسجّل لكل الحسابات|مطابقة المستند", current.Select(i => {
                var p = d.Proofs[i.SourceId];
                return Row(d,i,[i.Reference,i.Day,ReportPeriod.Local(i.Doc!.RegisteredUtc),d.CategoryName(i.Doc.CategoryId),d.ActorName(i.ActorId),i.Cost,p.Shares.Count,p.Allocated,p.BookCost,p.Status]);
            })));
            tables.Add(Table("توزيع الدفع", "رقم المصروف|تاريخ المستند|سطر التوزيع|التصنيف|الحساب الموزّع عليه|الحصة المكتوبة|صافي القيد لكل التواريخ|عدد القيود|مطابقة الحساب", currentAccounts.Select(i => {
                var proof = d.Proofs[i.SourceId].Accounts.Single(a => a.AccountId == i.AccountId);
                return Row(d,i,[i.Reference,i.Day,i.Key.SourceId,d.CategoryName(i.Doc!.CategoryId),d.AccountName(i.AccountId!.Value),i.Cost,proof.BookCost,proof.Legs.Count,proof.Result]);
            })));
            // Documentary evidence, not an extra posting total. A backdated invoice can have proof
            // outside BOTH displayed periods; show its timing and ledger key instead of guessing.
            tables.Add(Table("قيود إثبات المستند", "رقم المصروف|تاريخ المستند|رقم القيد|وقت القيد بمصر|موقع القيد من الفترة|الحساب الفعلي|المنفّذ|منصرف|وارد|مطابقة الحساب",
                from i in current from evidence in d.Proofs[i.SourceId].Accounts where f.AccountId == null || evidence.AccountId == f.AccountId
                from l in evidence.Legs select Row(d,i,[i.Reference,i.Day,l.Id,ReportPeriod.Local(l.AtUtc),d.Period.Contains(l.AtUtc) ? "داخل الفترة" : "خارج الفترة",
                    d.AccountName(evidence.AccountId),d.ActorName(l.ActorId),l.Incoming ? 0m : l.Amount,l.Incoming ? l.Amount : 0m,evidence.Result],evidence.AccountId)));
            tables.Add(Table("فروق المطابقة", "رقم المصروف|تاريخ المستند|سبب المراجعة|الحساب|المتوقّع|المسجّل|المسجّل ناقص المتوقع|عدد القيود", Differences(d,current.Where(Review))));
        }
        else
        {
            metrics = [new("قيود فعلية",current.Length,"قيد"), new("منصرف المصروفات",current.Sum(i => i.Out),currency),
                new("وارد مرتبط بمصروف",current.Sum(i => i.In),currency), new("صافي الخصم",total,currency), new("قيود تحتاج مراجعة",reviews,"قيد")];
            const string cols = "رقم القيد|وقت القيد بمصر|مرجع المصروف|تاريخ المستند|التصنيف|الحساب الفعلي|منفّذ القيد|منصرف|وارد|صافي الخصم|الرصيد قبل|الرصيد بعد|حالة المطابقة";
            ReportRow Posting(Fact i) => Row(d,i,[$"#{i.Key.SourceId}",ReportPeriod.Local(i.Leg!.AtUtc),i.Reference,i.Doc?.Day,d.CategoryName(i.Doc?.CategoryId),
                d.AccountName(i.AccountId!.Value),d.ActorName(i.ActorId),i.Out,i.In,i.Cost,i.Leg.BalanceAfter+i.Cost,i.Leg.BalanceAfter,Status(d,i)]);
            tables.Add(Table("قيود الخصم والوارد",cols,current.Select(Posting)));
            tables.Add(Table("قيود تحتاج مراجعة",cols,current.Where(Review).Select(Posting)));
        }

        string valueLabel = movement ? "صافي الخصم" : "مبلغ المستندات";
        tables.Add(Summary("حسب التصنيف",current,i => i.Doc?.CategoryId,i => d.CategoryName(i.Doc?.CategoryId),valueLabel));
        tables.Add(Summary("حسب الحساب",currentAccounts,i => i.AccountId,i => d.AccountName(i.AccountId!.Value),movement ? valueLabel : "حصة الحساب", d));
        tables.Add(Summary("حسب الموظف",current,i => i.ActorId,i => d.ActorName(i.ActorId),valueLabel));
        tables.Add(Summary("حسب اليوم",current,i => i.Day,i => i.Day,valueLabel));
        tables.Add(Summary("حسب الشهر",current,i => (i.Day.Year,i.Day.Month),i => i.Day.ToString("yyyy-MM",System.Globalization.CultureInfo.InvariantCulture),valueLabel));
        tables.Add(Compare(CompareTotal,primary,_ => 0,_ => "كل الفترة",valueLabel));
        tables.Add(Compare(CompareCategory,primary,i => i.Doc?.CategoryId,i => d.CategoryName(i.Doc?.CategoryId),valueLabel));
        tables.Add(Compare(CompareAccount,accounts,i => i.AccountId,i => d.AccountName(i.AccountId!.Value),movement ? valueLabel : "الحصص الموزّعة",d));
        string warning = reviews == 0 ? "" : movement ? "بعض القيود تحتاج مراجعة؛ أُدرج أثرها الفعلي دون افتراض مستند أو توزيع." : "راجع «فروق المطابقة». مبلغ المستند وتوزيعه ليسا إثباتًا لخصم الحساب.";
        return new(basis,context,metrics,tables,warning,Section);
    }

    private static string Status(ExpenseReportData d, Fact i)
    {
        if (i.Doc == null) return "قيد بلا مستند";
        var proof = d.Proofs[i.SourceId];
        if (i.AccountId is int id && proof.Accounts.FirstOrDefault(a => a.AccountId == id) is { } match && match.Result != ExpenseProof.Matched) return match.Result;
        return proof.OK ? ExpenseProof.Matched : !proof.AllocationOK ? "توزيع المستند لا يطابق مبلغه" : i.AccountId == null ? "فرق بين الحسابات الموزّعة والقيود" : "فرق في حساب آخر للمستند";
    }

    private static IEnumerable<ReportRow> Differences(ExpenseReportData d, IEnumerable<Fact> facts)
    {
        foreach (var i in facts)
        {
            var proof = d.Proofs[i.SourceId];
            if (!proof.AllocationOK) yield return Row(d,i,[i.Reference,i.Day,proof.Shares.Count == 0 ? "مستند بلا توزيع" : "مجموع التوزيع لا يساوي المستند",null,i.Cost,proof.Allocated,proof.Allocated-i.Cost,null]);
            foreach (var a in proof.Accounts.Where(a => a.Result != ExpenseProof.Matched))
                yield return Row(d,i,[i.Reference,i.Day,a.Result,d.AccountName(a.AccountId),a.Share?.Amount,a.BookCost,a.BookCost-a.Share?.Amount,a.Legs.Count],a.AccountId);
        }
    }

    private static ReportTable Summary<T>(string title, Fact[] facts, Func<Fact,T> key, Func<Fact,object> label, string valueLabel, ExpenseReportData? accountData = null)
    {
        decimal total = facts.Sum(i => i.Cost);
        return Table(title,$"المجموعة|مستندات متاحة|عدد الأسطر|{valueLabel}|النسبة من الإجمالي %",facts.GroupBy(key).Select(g =>
            Group(g,[label(g.First()),DocumentCount(g),g.Count(),g.Sum(i => i.Cost),Ratio(g.Sum(i => i.Cost),total)],accountData?.AccountLink(g.First().AccountId))),true);
    }

    private static ReportTable Compare<T>(string title, List<Fact> both, Func<Fact,T> key, Func<Fact,object> label, string valueLabel, ExpenseReportData? accountData = null) =>
        Table(title,$"المجموعة|أسطر الفترة|{valueLabel} الآن|أسطر السابقة|{valueLabel} سابقًا|الفرق|التغير %|النتيجة",both.GroupBy(key).Select(g => {
            var items = g.ToArray(); int oldCount = items.Count(i => i.Previous); int newCount = items.Length - oldCount;
            decimal previous = items.Where(i => i.Previous).Sum(i => i.Cost), current = items.Where(i => !i.Previous).Sum(i => i.Cost), delta = current - previous;
            string trend = oldCount == 0 ? "جديد" : newCount == 0 ? "لا نشاط حالي" : previous <= 0 ? "أساس غير موجب" : delta == 0 ? "ثابت" : delta > 0 ? "زيادة" : "انخفاض";
            return Group(items,[label(items[0]),newCount,current,oldCount,previous,delta,Ratio(delta,previous),trend],accountData?.AccountLink(items[0].AccountId));
        }),true);

    private static ReportPage Drill(ExpenseReportData d, Fact[] items, string context, string basis, string currency, bool comparison)
    {
        decimal current = items.Where(i => !i.Previous).Sum(i => i.Cost), prior = items.Where(i => i.Previous).Sum(i => i.Cost);
        var metrics = new List<ReportMetric> { new("أسطر الفترة",items.Count(i => !i.Previous),"سطر"), new("مبلغ المجموعة للفترة",current,currency) };
        if (comparison) metrics.AddRange([new("مبلغ المجموعة سابقًا",prior,currency),new("الفرق",current-prior,currency),new("التغير",Ratio(current-prior,prior),"%","لا أساس موجب")]);
        var table = Table(comparison ? "أدلة المقارنة" : "تفاصيل المجموعة", "المرجع|الفترة|اليوم|نوع السطر|رقم السطر|التصنيف|الحساب|الموظف|المبلغ الموقّع|المطابقة",
            items.OrderBy(i => i.Previous).Select(i => Row(d,i,[i.Reference,i.Previous ? "السابقة" : "المختارة",i.Day,
                i.Key.Kind switch { ShareKey => "حصة موزّعة", PostingKey => i.Leg!.Incoming ? "قيد وارد" : "قيد منصرف", _ => "مستند" },i.Key.SourceId,
                d.CategoryName(i.Doc?.CategoryId),i.AccountId is int a ? d.AccountName(a) : null,d.ActorName(i.ActorId),i.Cost,Status(d,i)])));
        return new(basis,context,metrics,[table],items.Any(i => d.Proofs.GetValueOrDefault(i.SourceId)?.OK != true) ? "تحتوي المجموعة على أسطر تحتاج مراجعة." : "",Section);
    }
}
