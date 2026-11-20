namespace ProjectZ.Models;

/// <summary>A ledger leg, a transfer commission and an opening balance are never added together.</summary>
public static class FinanceReportProjection
{
    public const string Section="تقارير المالية";
    public static string SourceText(FinanceSource source)=>source switch {
        FinanceSource.Independent=>"قيد مستقل / يدوي",FinanceSource.Sale=>"مبيعات",FinanceSource.ReturnExchange=>"مرتجع / استبدال بيع",
        FinanceSource.Purchase=>"مشتريات",FinanceSource.Service=>"صيانة",FinanceSource.Expense=>"مصروفات",FinanceSource.CustomerTransfer=>"تحويل عميل",
        FinanceSource.TransferGroup=>"مجموعة تحويل مرتبطة",_=>"مصدر آخر" };
    private static DateTime Local(DateTime t)=>ReportPeriod.Local(t);
    private static DateOnly Day(DateTime t)=>DateOnly.FromDateTime(Local(t));
    private static bool Match(string query,params string[] terms)=>query.Length==0 || terms.Any(t=>t.Contains(query,StringComparison.OrdinalIgnoreCase));
    private static ReportTable Table(string title,string columns,IEnumerable<ReportRow> rows,bool group=false)=>new(title,columns.Split('|'),rows.ToArray(),group);
    private static ReportRow Detail(object?[] values,FinanceLink? account,FinanceLink? source=null)=>new([],values){CanOpenInvoice=false,FinancialAccount=account,FinancialSource=source};
    private static ReportRow Group(IEnumerable<int> keys,object?[] values,FinanceLink? account=null)=>new(keys.ToArray(),values){CanOpenInvoice=false,FinancialAccount=account};
    // If one surviving item cannot be valued, zero would be a materially wrong total.
    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        decimal sum=0; foreach(var n in values) {if(n is null)return null;sum+=n.Value;} return sum;
    }
    public static ReportPage Build(FinanceReportData d,FinanceReportMode mode,ReportFilter f,string currency,bool? incoming=null)
    {
        var context=$"{d.Period.Label} | قراءة {Local(d.ReadAtUtc):yyyy-MM-dd HH:mm} | {currency}"
            +(f.AccountId is int a?$" | الحساب #{a}":"")+(f.IssuerId is int u?$" | الموظف #{u}":"")
            +(f.StatusOrKind is int k?" | "+FilterText(mode,k):"")+(incoming.HasValue && mode==FinanceReportMode.Ledger?(incoming.Value?" | الوارد فقط":" | المنصرف فقط"):"")
            // Do not echo arbitrary search text into paper, XLSX or the screen caption: someone might paste an unmasked number.
            +(f.Search.Trim().Length>0?" | بحث أساسي مفعّل":"")+(f.Detail.Trim().Length>0?" | بحث تفصيلي مفعّل":"")+(f.InvoiceIds is null?"":" | المجموعة: "+f.DrillLabel);
        return mode switch {
            FinanceReportMode.Statement=>Statement(d,f,context,currency),FinanceReportMode.Ledger=>Ledger(d,f,context,currency,incoming),
            FinanceReportMode.Limits=>Limits(d,f,context),_=>Commissions(d,f,context,currency,mode==FinanceReportMode.IssuedTransfers) };
    }
    public static string FilterText(FinanceReportMode mode,int key)=>mode switch {
        FinanceReportMode.Ledger=>SourceText((FinanceSource)key),FinanceReportMode.FeeEvents=>key==0?"تسجيل عمولة":"عكس عمولة",
        FinanceReportMode.IssuedTransfers=>key==0?"سارية الآن":"معكوسة الآن",_=>key switch {0=>"الخزنة",1=>"فودافون كاش",_=>"إنستاباي"} };
    private static bool AccountMatches(BookAccount a,ReportFilter f)=> (f.AccountId is null || f.AccountId==a.Id) && (f.StatusOrKind is null || f.StatusOrKind==a.Provider)
        && Match(f.Search.Trim(),a.Label,a.MaskedIdentifier) && Match(f.Detail.Trim(),a.Active?"نشط":"موقوف",a.IsDefault?"افتراضي":"غير افتراضي");

    private sealed record Balance(BookAccount Account,decimal? Open,decimal? Created,decimal In,decimal Out,decimal? Close,int Legs,int Breaks,string Basis)
    {
        public bool Review=>Breaks>0 || Open is null || Created is null || Close is null || (Account.LatestPostedBalance is decimal last && last!=Account.Current);
    }
    private static Balance ReadBalance(FinanceReportData d,BookAccount a,IEnumerable<BookLeg> source)
    {
        var legs=source.OrderBy(l=>l.AtUtc).ThenBy(l=>l.Id).ToArray();
        var created=a.CreatedUtc>=d.Period.StartUtc && a.CreatedUtc<d.Period.EndUtc;
        decimal? seed=a.PreviousClose??legs.FirstOrDefault()?.Before??a.NextOpen;
        var basis=a.PreviousClose.HasValue?"آخر قيد قبل الفترة":legs.Length>0?"قبل أول قيد في الفترة":a.NextOpen.HasValue?"قبل أول قيد لاحق":"لا يوجد قيد تاريخي";
        // An account may be created with capital and no movement. Today's value is not proof of a past close if it changed since.
        if(seed is null && a.UpdatedUtc<d.Period.EndUtc && (created || a.UpdatedUtc<d.Period.StartUtc)) {seed=a.Current;basis="رصيد محفوظ بلا قيود أو تحديث لاحق للفترة";}
        decimal? running=seed;int breaks=0;
        foreach(var l in legs){if(running.HasValue && running!=l.Before)breaks++;running=l.After;}
        if(a.NextOpen.HasValue && running.HasValue && a.NextOpen!=running)breaks++;
        if(created && a.PreviousClose.HasValue || legs.Any(l=>l.AtUtc<a.CreatedUtc))breaks++;
        return new(a,created?0m:seed,created?seed:0m,legs.Where(l=>l.Incoming).Sum(l=>l.Amount),legs.Where(l=>!l.Incoming).Sum(l=>l.Amount),running,legs.Length,breaks,basis);
    }
    private static ReportPage Statement(FinanceReportData d,ReportFilter f,string context,string currency)
    {
        var byAccount=d.Legs.ToLookup(l=>l.AccountId);
        var rows=d.Accounts.Values.Where(a=>AccountMatches(a,f) && (a.CreatedUtc<d.Period.EndUtc || byAccount.Contains(a.Id) || a.PreviousClose.HasValue))
            .OrderBy(a=>a.Provider).ThenBy(a=>a.Id).Select(a=>ReadBalance(d,a,byAccount[a.Id])).ToArray();
        const string cols="الحساب|المعرّف المحجوب|الحالة الآن|رصيد أول الفترة|رصيد إنشاء أثناء الفترة|وارد القيود|منصرف القيود|صافي الحركة|رصيد آخر الفترة|الرصيد الآن|أسطر الدفتر|أساس الافتتاح|فواصل غير مطابقة|آخر قيد مقابل الرصيد الآن";
        ReportRow Row(Balance b)=>Detail([b.Account.Label,b.Account.MaskedIdentifier,b.Account.Active?"نشط":"موقوف",b.Open,b.Created,b.In,b.Out,b.In-b.Out,b.Close,b.Account.Current,b.Legs,b.Basis,b.Breaks,
            b.Account.LatestPostedBalance is null?"بلا قيود":b.Account.LatestPostedBalance==b.Account.Current?"مطابق":"فرق يحتاج مراجعة"],b.Account.Link);
        return new("كشف الأرصدة • أول وآخر الفترة",context,
            [new("أول الفترة",SumKnown(rows.Select(b=>b.Open)),currency),new("أرصدة إنشاء",SumKnown(rows.Select(b=>b.Created)),currency),new("وارد القيود",rows.Sum(b=>b.In),currency),
             new("منصرف القيود",rows.Sum(b=>b.Out),currency),new("آخر الفترة",SumKnown(rows.Select(b=>b.Close)),currency),new("تحتاج مراجعة",rows.Count(b=>b.Review),"حساب")],
            [Table("كشف الأرصدة",cols,rows.Select(Row)),Table("مطابقة الأرصدة",cols,rows.Where(b=>b.Review).Select(Row)),
             Table("حسب القناة","القناة|حسابات|أول الفترة|أرصدة إنشاء|وارد|منصرف|صافي الحركة|آخر الفترة",rows.GroupBy(b=>b.Account.Provider).Select(g=>Detail([g.First().Account.ProviderLabel,g.Count(),SumKnown(g.Select(b=>b.Open)),SumKnown(g.Select(b=>b.Created)),g.Sum(b=>b.In),g.Sum(b=>b.Out),g.Sum(b=>b.In-b.Out),SumKnown(g.Select(b=>b.Close))],null)))],
            rows.Any(b=>b.Review)?"توجد أرصدة غير مكتملة أو غير متصلة بالدفتر. لا تُملأ الفجوة بتسوية آلية.":"",Section);
    }
    private static ReportPage Ledger(FinanceReportData d,ReportFilter f,string context,string currency,bool? incoming)
    {
        var legs=d.Legs.Where(l=>d.Period.Contains(l.AtUtc) && (f.InvoiceIds is null || f.InvoiceIds.Contains(l.Id)) && (f.StatusOrKind is null || f.StatusOrKind==(int)l.Source)
            && (f.IssuerId is null || f.IssuerId==l.ActorId) && (f.AccountId is null || f.AccountId==l.AccountId) && (incoming is null || incoming==l.Incoming)
            && Match(f.Search.Trim(),d.Accounts[l.AccountId].Label,d.Accounts[l.AccountId].MaskedIdentifier,l.Reference,$"#{l.Id}")
            && Match(f.Detail.Trim(),SourceText(l.Source),l.Purpose,d.Actor(l.ActorId))).OrderByDescending(l=>l.AtUtc).ThenByDescending(l=>l.Id).ToArray();
        decimal In(IEnumerable<BookLeg> list)=>list.Where(l=>l.Incoming).Sum(l=>l.Amount);
        decimal Out(IEnumerable<BookLeg> list)=>list.Where(l=>!l.Incoming).Sum(l=>l.Amount);
        ReportRow Row(BookLeg l){var a=d.Accounts[l.AccountId];return Detail([l.Id,Local(l.AtUtc),a.Label,a.MaskedIdentifier,SourceText(l.Source),l.Reference,l.Purpose,
            l.Before,l.Incoming?l.Amount:null,l.Incoming?null:l.Amount,l.After,d.Actor(l.ActorId),a.Provider==0?"لا ينطبق":l.Counts?"نعم":"لا",l.SourceId.HasValue && !l.HasDocument?"بلا مستند متاح":""],a.Link,d.SourceLink(l));}
        ReportTable By<T>(string title,Func<BookLeg,T> key,Func<BookLeg,object> label)=>Table(title,"التجميع|أسطر|وارد|منصرف|صافي أثر الحسابات",legs.GroupBy(key).Select(g=>Group(g.Select(l=>l.Id),
            [label(g.First()),g.Count(),In(g),Out(g),In(g)-Out(g)],g.Select(l=>l.AccountId).Distinct().Count()==1?d.Accounts[g.First().AccountId].Link:null)),true);
        const string cols="رقم القيد|التوقيت (مصر)|الحساب|المعرّف المحجوب|المصدر|مرجع المصدر|نوع القيد|الرصيد قبل|وارد|منصرف|الرصيد بعد|الموظف|ضمن استخدام المحفظة|الرابط";
        return new("دفتر حركة المال • بتاريخ القيد",context,[new("وارد الحسابات",In(legs),currency),new("منصرف الحسابات",Out(legs),currency),new("صافي أثر القيود",In(legs)-Out(legs),currency),new("عدد القيود",legs.Length,"قيد")],
            [Table("قيود الفترة",cols,legs.Select(Row)),By("الحركة اليومية",l=>Day(l.AtUtc),l=>Day(l.AtUtc)),By("حسب المصدر",l=>l.Source,l=>SourceText(l.Source)),
             By("حركة الحسابات",l=>l.AccountId,l=>d.Accounts[l.AccountId].Label),By("حركة القنوات",l=>d.Accounts[l.AccountId].Provider,l=>d.Accounts[l.AccountId].ProviderLabel),By("حسب الموظف",l=>l.ActorId,l=>d.Actor(l.ActorId))],
            "الوارد والمنصرف حركة كل حساب؛ ليسا المبيعات أو أرباح المحل. لا يُضاف أصل تحويل العميل أو عمولته مرة أخرى إلى الدفتر.",Section);
    }
    private sealed record Fee(FeeTransfer Transfer,bool Undo,DateTime AtUtc,int ActorId,decimal? Signed)
    {
        public int Key=>Undo?-Transfer.Id:Transfer.Id;
    }
    private static ReportPage Commissions(FinanceReportData d,ReportFilter f,string context,string currency,bool cohort)
    {
        bool MatchTransfer(FeeTransfer t)=> (f.AccountId is null || f.AccountId==t.WalletId) && Match(f.Search.Trim(),$"#{t.Id}",t.MaskedCustomer,d.Accounts[t.WalletId].Label,d.Accounts[t.WalletId].MaskedIdentifier);
        bool Extra(FeeTransfer t,int actor)=>Match(f.Detail.Trim(),d.Actor(actor),t.Direction,t.Predecessor.HasValue?"تصحيح":"أصل");
        var transfers=d.Transfers.Values.Where(MatchTransfer);
        if(cohort)
        {
            var items=transfers.Where(t=>d.Period.Contains(t.IssuedUtc) && (f.InvoiceIds is null||f.InvoiceIds.Contains(t.Id)) && (f.StatusOrKind is null || f.StatusOrKind==(t.Reversed?1:0))
                && (f.IssuerId is null || f.IssuerId==t.ActorId) && Extra(t,t.ActorId)).OrderByDescending(t=>t.IssuedUtc).ThenByDescending(t=>t.Id).ToArray();
            var table=Table("تحويلات تاريخ الإصدار","التحويل|تاريخ التسجيل|المحفظة|العميل المحجوب|الاتجاه|أصل المبلغ المسجّل|العمولة المسجلة|الحالة الآن|تاريخ العكس|العمولة السارية المثبتة|المسجل|عكسه|تصحيح للتحويل|استبدله التحويل|المطابقة",items.Select(t=>
                Detail([t.Id,Local(t.IssuedUtc),d.Accounts[t.WalletId].Label,t.MaskedCustomer,t.Direction,t.Principal,t.Commission,t.Reversed?"معكوسة":"سارية",t.ReversedUtc is DateTime r?Local(r):null,t.CurrentFee,
                    d.Actor(t.ActorId),t.ReverserId is int u?d.Actor(u):null,t.Predecessor,t.Replacement,t.CurrentFee.HasValue?"مطابق":"تحتاج مراجعة"],d.Accounts[t.WalletId].Link,d.TransferLink(t))));
            ReportTable ByCohort<T>(string title,Func<FeeTransfer,T> key,Func<FeeTransfer,string> label)=>Table(title,
                "البيان|عمليات صدرت|معكوسة الآن|أصل التحويلات السارية المثبتة|العمولة السارية المثبتة",items.GroupBy(key).Select(g=>Group(g.Select(t=>t.Id),
                    [label(g.First()),g.Count(),g.Count(t=>t.Reversed),SumKnown(g.Where(t=>!t.Reversed).Select(t=>t.ApplicationConfirmed?(decimal?)t.Principal:null)),SumKnown(g.Select(t=>t.CurrentFee))],
                    g.Select(t=>t.WalletId).Distinct().Count()==1?d.Accounts[g.First().WalletId].Link:null)),true);
            return new("تحويلات الفترة • حالتها وقت القراءة",context,[new("تحويلات صدرت",items.Length,"عملية"),new("معكوسة الآن",items.Count(t=>t.Reversed),"عملية"),new("العمولة السارية المثبتة",SumKnown(items.Select(t=>t.CurrentFee)),currency)],
                [table,ByCohort("التحويلات حسب المحفظة",t=>t.WalletId,t=>d.Accounts[t.WalletId].Label),ByCohort("أنواع الخدمة",t=>t.Kind,t=>t.Direction),ByCohort("مسجلو التحويلات",t=>t.ActorId,t=>d.Actor(t.ActorId))],FeeWarning(items.Count(t=>!t.CurrentFee.HasValue)),Section);
        }
        IEnumerable<Fee> Events(FeeTransfer t)
        {
            yield return new(t,false,t.IssuedUtc,t.ActorId,t.ApplicationConfirmed?t.Commission:null);
            if(t.Reversed && t.ReversedUtc is DateTime at && t.ReverserId is int u)yield return new(t,true,at,u,t.UndoConfirmed?-t.Commission:null);
        }
        var events=transfers.SelectMany(Events).Where(e=>d.Period.Contains(e.AtUtc) && (f.InvoiceIds is null || f.InvoiceIds.Contains(e.Key)) && (f.StatusOrKind is null || f.StatusOrKind==(e.Undo?1:0))
            && (f.IssuerId is null || f.IssuerId==e.ActorId) && Extra(e.Transfer,e.ActorId)).OrderByDescending(e=>e.AtUtc).ThenByDescending(e=>e.Transfer.Id).ToArray();
        // A transfer leg in the selected dates need not have a usable dated header (e.g. a pre-23 in-place edit).
        // Surface the ledger evidence separately. Do not turn each wallet/cash leg into a second fee event.
        var unassigned=d.Legs.Where(l=>l.Source==FinanceSource.CustomerTransfer && d.Period.Contains(l.AtUtc) && f.InvoiceIds==null
            && (f.IssuerId is null || f.IssuerId==l.ActorId)).Where(l=>{
                var a=d.Accounts[l.AccountId];var h=l.SourceId is int id?d.Transfers.GetValueOrDefault(id):null;
                bool undo=l.Purpose is "عكس تحويل" or "عكس تعديل";
                if(h!=null && (undo?h.UndoConfirmed:h.ApplicationConfirmed))return false;
                if(f.StatusOrKind is int k && k!=(undo?1:0))return false;
                if(f.AccountId is int acc && acc!=l.AccountId && !(a.Provider==0 && h?.WalletId==acc))return false;
                return Match(f.Search.Trim(),l.Reference,a.Label,a.MaskedIdentifier,h?.MaskedCustomer??"",h!=null?d.Accounts[h.WalletId].Label:"")
                    && Match(f.Detail.Trim(),d.Actor(l.ActorId),l.Purpose,h?.Direction??"",h?.Predecessor!=null?"تصحيح":"أصل");
            }).ToArray();
        var review=Table("قيود عمولة تحتاج مطابقة","القيد|التوقيت|الحساب الفعلي|مرجع التحويل|أثر القيد وليس العمولة|الموظف",unassigned.Select(l=>Detail(
            [l.Id,Local(l.AtUtc),d.Accounts[l.AccountId].Label,l.SourceId,l.Delta,d.Actor(l.ActorId)],d.Accounts[l.AccountId].Link,d.SourceLink(l))));
        decimal? Applied(IEnumerable<Fee> list)=>SumKnown(list.Where(e=>!e.Undo).Select(e=>e.Signed));
        decimal? Undone(IEnumerable<Fee> list)=>-SumKnown(list.Where(e=>e.Undo).Select(e=>e.Signed));
        ReportTable By<T>(string title,Func<Fee,T> key,Func<Fee,object> label)=>Table(title,"البيان|حركات عمولة|عمولة مسجلة|عمولة معكوسة|صافي العمولات",events.GroupBy(key).Select(g=>Group(g.Select(e=>e.Key),
            [label(g.First()),g.Count(),Applied(g),Undone(g),SumKnown(g.Select(e=>e.Signed))])),true);
        return new("عمولات التحويل • بتاريخ التسجيل والعكس",context,[new("عمولات التسجيل",unassigned.Any(l=>l.Purpose is not ("عكس تحويل" or "عكس تعديل"))?null:Applied(events),currency),new("عمولات عُكست",unassigned.Any(l=>l.Purpose is "عكس تحويل" or "عكس تعديل")?null:Undone(events),currency),new("صافي عمولة الفترة",unassigned.Length>0?null:SumKnown(events.Select(e=>e.Signed)),currency),new("أسطر العمولة",events.Length,"حركة")],
            [Table("دفتر العمولات","التحويل|وقت العملية|الحدث|المحفظة|العميل المحجوب|أصل التحويل المثبت|أثر المحفظة|أثر الخزنة|أثر العمولة|الموظف|تصحيح للتحويل|المطابقة",events.Select(e=>{var t=e.Transfer;int sign=e.Undo?-1:1;
                return Detail([t.Id,Local(e.AtUtc),e.Undo?"عكس":t.Predecessor.HasValue?"تسجيل تصحيح":"تسجيل",d.Accounts[t.WalletId].Label,t.MaskedCustomer,e.Signed.HasValue?t.Principal:null,
                    e.Signed.HasValue?sign*(t.Kind==1?-t.Principal:t.Principal):null,e.Signed.HasValue?sign*t.CashDelta:null,e.Signed,d.Actor(e.ActorId),t.Predecessor,e.Signed.HasValue?"مطابق":"تفاصيل تاريخية غير مؤكدة"],d.Accounts[t.WalletId].Link,d.TransferLink(t));})),
             By("العمولات اليومية",e=>Day(e.AtUtc),e=>Day(e.AtUtc)),By("عمولات المحافظ",e=>e.Transfer.WalletId,e=>d.Accounts[e.Transfer.WalletId].Label),By("منفذو العمليات",e=>e.ActorId,e=>d.Actor(e.ActorId)),review],
             FeeWarning(events.Count(e=>!e.Signed.HasValue))+(unassigned.Length>0?$" يوجد {unassigned.Length} قيد تحويل يحتاج مطابقة. جداول التجميع تخص الأحداث المؤرخة فقط.":""),Section);
    }
    private static string FeeWarning(int count)=>count>0?$"توجد {count} عملية غير مكتملة الإثبات (تاريخ قديم أو قيود غير مطابقة). لا تُستنتج عمولة من النص أو الرصيد الحالي.":"";

    private static ReportPage Limits(FinanceReportData d,ReportFilter f,string context)
    {
        var wallets=d.Accounts.Values.Where(a=>a.Provider!=0 && AccountMatches(a,f)).OrderBy(a=>a.Provider).ThenBy(a=>a.Id).ToArray();
        var ids=wallets.Select(a=>a.Id).ToHashSet();var usage=d.Usage.ToDictionary(u=>(u.AccountId,u.Incoming));
        var legs=d.Legs.Where(l=>ids.Contains(l.AccountId) && l.Counts && d.Period.Contains(l.AtUtc)).ToArray();
        var now=new List<ReportRow>();int warnings=0;
        foreach(var a in wallets)foreach(bool inc in new[]{true,false})foreach(bool month in new[]{false,true})
        {
            usage.TryGetValue((a.Id,inc),out var u);
            decimal cap=month?(inc?a.MonthInLimit:a.MonthOutLimit):(inc?a.DayInLimit:a.DayOutLimit);
            decimal used=(month?u?.Month:u?.Today)??0m;
            if(a.Active && cap>0 && used>=cap)warnings++;
            now.Add(Detail([a.Label,a.MaskedIdentifier,a.Active?"نشط":"موقوف",month?"هذا الشهر":"اليوم",inc?"إيداع":"سحب",cap>0?cap:null,used,cap>0?Math.Max(0,cap-used):null,
                cap==0?"بلا حد مضبوط":used>cap?"تجاوز الحد":used==cap?"بلغ الحد":"دون الحد"],a.Link));
        }
        ReportTable PeriodUse(string name,Func<BookLeg,DateOnly> key)=>Table(name,"المحفظة|الفترة|قيود محتسبة|استخدام الإيداع|استخدام السحب",legs.GroupBy(l=>(l.AccountId,Date:key(l))).OrderByDescending(g=>g.Key.Date).Select(g=>Detail([d.Accounts[g.Key.AccountId].Label,g.Key.Date,g.Count(),g.Where(l=>l.Incoming).Sum(l=>l.Amount),g.Where(l=>!l.Incoming).Sum(l=>l.Amount)],d.Accounts[g.Key.AccountId].Link)));
        return new("حدود المحافظ • الآن واستخدام الفترة",context,[new("المحافظ",wallets.Length,"محفظة"),new("حدود نشطة بلغت الحد",warnings,"حد"),new("قيود محتسبة في الفترة",legs.Length,"قيد")],
            [Table("حدود اليوم والشهر الآن","المحفظة|المعرّف المحجوب|الحالة|نافذة الاستخدام|الاتجاه|الحد الحالي|المستخدم|المتاح|التنبيه",now),PeriodUse("استخدام الفترة باليوم",l=>Day(l.AtUtc)),PeriodUse("استخدام الفترة بالشهر",l=>new DateOnly(Local(l.AtUtc).Year,Local(l.AtUtc).Month,1))],
            "المستخدم حسب علامة احتساب الحد في القيد، لا حسب صافي النقد. الحدود الحالية لا تمثل حدودًا تاريخية ولا كشف مقدم الخدمة.",Section);
    }
}
