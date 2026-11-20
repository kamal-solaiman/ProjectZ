using static ProjectZ.Models.OverallMeasure;

namespace ProjectZ.Models;

/// <summary>One projection for the screen, A4 and XLSX; even a one-day, one-sale period has no transaction row.</summary>
public static class OverallReportProjection
{
    public const string Bridge = "تكوين النتيجة", Daily = "النتيجة اليومية", Liquidity = "السيولة", Review = "نواقص الإثبات";
    private static readonly string[] Comparison = ["البند","الحالية","السابقة","الفرق","التغير ٪","المقارنة"];
    private static ReportRow Row(params object?[] cells) => new([],cells) { CanOpenInvoice=false };
    private static ReportRow Compare(string label, decimal? current, decimal? previous)
    {
        var delta = current-previous;
        decimal? percent = previous is null or 0 || delta is null ? null : Math.Round(delta.Value/Math.Abs(previous.Value)*100m,2);
        var status = (current,previous) switch {
            (null,null)=>"الفترتان غير مكتملتين", (null,_)=>"الحالية غير مكتملة", (_,null)=>"السابقة غير مكتملة",
            (not 0,0)=>"جديد", _ when delta==0=>"بلا تغيّر", _ when delta>0=>"زيادة", _=>"انخفاض" };
        return Row(label,current,previous,delta,percent,status);
    }

    public static ReportPage Build(OverallReportData data, string currency)
    {
        var period = data.Period;
        var from = DateOnly.FromDateTime(period.From); int days = (period.Through-period.From).Days+1;
        var priorStart = from.AddDays(-days);
        var current = new OperatingTotals(data.Figures.Where(f=>f.Day>=from));
        var prior = new OperatingTotals(data.Figures.Where(f=>f.Day<from));
        var measures = new Dictionary<OverallMeasure,string>();
        ReportRow Calc(string title, Func<OperatingTotals,decimal?> calc) => Compare(title,calc(current),calc(prior));
        ReportRow Measure(string title, OverallMeasure measure)
        {
            measures[measure]=title;
            return Compare(title,current[measure],prior[measure]);
        }
        ReportTable Table(string title, params ReportRow[] rows) => new(title,Comparison,rows);
        var tables = new List<ReportTable> {
            Table(Bridge,
                Calc("هامش المبيعات",s=>s.GrossProfit), Calc("مساهمة الصيانة",s=>s.Service), Calc("عمولة التحويل الصافية",s=>s.Fees),
                Calc("أثر المصروفات (−)",s=>-s[ExpenseWritten]), Calc("أثر الهالك بعد الاسترداد (−)",s=>-s.NetDamage),
                Calc("النتيجة التشغيلية",s=>s.Profit)),
            Table("المبيعات",
                Calc("فواتير صدرت (عدد)",s=>s[SalesIssued]), Measure("قيمة البيع عند الإصدار بعد الخصم",SaleIssue),
                Measure("بضاعة بديلة صادرة",SaleReplacement), Measure("مبيعات مرتجعة ومستبدلة",SaleReturn), Measure("قيمة المبيعات الملغاة",SaleCancel),
                Calc("صافي المبيعات المؤرّخة",s=>s.NetSales), Measure("صافي تكلفة البضاعة المباعة",SalesCost), Calc("هامش المبيعات",s=>s.GrossProfit),
                Measure("تحصيل مبيعات مسجّل",SaleReceipts), Measure("رد نقدي لعملاء البيع",SaleRefunds)),
            Table("المشتريات",
                Calc("مستندات أُنشئت (عدد)",s=>s.Count(PurchaseCreate)), Measure("مشتريات عند الإنشاء",PurchaseCreate),
                Measure("تعديلات وحذف بالقيمة الموقّعة",PurchaseEdit), Measure("تخفيض مرتجعات وإلغاءات",PurchaseReturn),
                Calc("صافي حركة مستندات الشراء",s=>s[PurchaseCreate]+s[PurchaseEdit]-s[PurchaseReturn]), Measure("مدفوع للمورد",SupplierPaid),
                Measure("مقبوض من المورد بسند مطابق",SupplierReceived), Measure("وارد مورد يحتاج مطابقة",UnverifiedSupplierReceipt),
                Measure("خفض دين بمستند مرتجع",DebtReduced), Measure("رصيد دائن نشأ للمحل",CreditIssued), Measure("رصيد استُخدم في المقاصة",CreditUsed)),
            Table("الصيانة",
                Measure("أجهزة استُلمت (عدد)",ServiceIntake), Measure("تسليمات منها إعادة التسليم (عدد)",Deliveries),
                Measure("عربون ودفعات تحت الحساب",ServiceAdvance), Measure("مقبوضات مرحلة التسليم",ServiceAtDelivery), Measure("مبالغ رُدّت للعميل",ServiceRefunds),
                Calc("صافي حركة دفعات العملاء",s=>s[ServiceAdvance]+s[ServiceAtDelivery]-s[ServiceRefunds]),
                Measure("صافي المقبوض المعترف به بعد التسليم",ServiceEarned), Measure("تكلفة القطع المعترف بها وتغيراتها",ServiceParts), Calc("مساهمة الصيانة",s=>s.Service)),
            Table("المالية والعمولات",
                Measure("عمولات بتاريخ التسجيل",FeeIssued), Measure("عمولات بتاريخ العكس",FeeReversed), Calc("دخل العمولات الصافي",s=>s.Fees),
                Calc("إجمالي الوارد للحسابات",s=>s.Inflow), Calc("إجمالي المنصرف من الحسابات",s=>s.Outflow), Calc("صافي حركة الحسابات (ليس ربحًا)",s=>s.CashChange)),
            Table("المصروفات",
                Calc("مستندات مصروف (عدد)",s=>s.Count(ExpenseWritten)), Measure("مصاريف التشغيل بتاريخ المستند",ExpenseWritten),
                Measure("خصم من الحسابات بتاريخ القيد",ExpenseDebit), Measure("وارد مرتبط بمصروف بتاريخ القيد",ExpenseCredit),
                Calc("صافي الخصم الفعلي (ليس مصروفًا ثانيًا)",s=>s[ExpenseDebit]-s[ExpenseCredit])),
            new("تصنيفات المصروفات",Comparison,data.ExpenseGroups.OrderByDescending(g=>g.Current).ThenBy(g=>g.Category)
                .Select(g=>Compare(g.Category,g.Current,g.Previous)).ToArray()),
            Table("الهالك والاسترداد",
                Measure("هالك بتكلفته عند التسجيل",Damage), Measure("قيمة وحدات أُعيدت للمتاح",Restored),
                Measure("تعويض مورد مثبت على المرتجع",Compensation), Calc("صافي خسارة المخزون",s=>s.NetDamage)),
            Table(Liquidity,
                Measure("الخزنة • وارد",CashIn), Measure("الخزنة • منصرف",CashOut), Calc("صافي الخزنة",s=>s[CashIn]-s[CashOut]),
                Measure("فودافون كاش • وارد",VodafoneIn), Measure("فودافون كاش • منصرف",VodafoneOut), Calc("صافي فودافون كاش",s=>s[VodafoneIn]-s[VodafoneOut]),
                Measure("إنستاباي • وارد",InstaIn), Measure("إنستاباي • منصرف",InstaOut), Calc("صافي إنستاباي",s=>s[InstaIn]-s[InstaOut]),
                Calc("إجمالي الوارد",s=>s.Inflow), Calc("إجمالي المنصرف",s=>s.Outflow), Calc("صافي حركة المحل",s=>s.CashChange)),
            new("أرصدة وقت القراءة",["القناة","الحسابات (عدد)","الرصيد الحالي وليس إقفال الفترة"],
                data.Balances.Select(b=>Row(b.Kind switch {0=>"الخزنة",1=>"فودافون كاش",2=>"إنستاباي",_=>"أخرى"},b.Accounts,b.Amount))
                .Append(Row("كل الحسابات",data.Balances.Sum(b=>b.Accounts),data.Balances.Sum(b=>b.Amount))).ToArray())
        };
        var byDay = data.Figures.ToLookup(f=>f.Day);
        tables.Add(new(Daily,["اليوم","نتيجة اليوم","اليوم المقارن","نتيجة المقارن","فرق النتيجة","صافي حركة الحسابات اليوم","قراءة اليوم"],
            Enumerable.Range(0,days).Select(i=>{
                var day=from.AddDays(i); var before=priorStart.AddDays(i);
                var a=new OperatingTotals(byDay[day]); var b=new OperatingTotals(byDay[before]);
                return Row(day,a.Profit,before,b.Profit,a.Profit-b.Profit,a.CashChange,a.Verdict);
            }).ToArray()));
        var gaps = data.Figures.Where(f=>f.Missing>0).GroupBy(f=>f.Measure).OrderBy(g=>g.Key).Select(g=>Row(
            measures[g.Key],g.Count(f=>f.Day>=from),g.Count(f=>f.Day<from))).ToArray();
        tables.Add(new(Review,["بند غير مكتمل","أيام متأثرة حاليًا","أيام متأثرة سابقًا"],gaps));
        var context=$"الفترة: {period.Label} • المقارنة: {priorStart:yyyy-MM-dd} — {from.AddDays(-1):yyyy-MM-dd} ({days} يوم) • المبالغ: {currency} • قراءة {ReportPeriod.Local(data.ReadAtUtc):yyyy-MM-dd HH:mm} بتوقيت مصر.";
        var warning=gaps.Length>0?"توجد قيم غير مكتملة؛ راجع «نواقص الإثبات». لا تمثل الخانة الفارغة صفرًا ولا يُعرض مجموع جزئي بوصفه نتيجة مؤكدة.":"";
        return new("نتيجة المحل • "+current.Verdict,context,
            [new(current.Verdict,current.Profit,currency),new("نتيجة الفترة السابقة",prior.Profit,currency),new("فرق النتيجة",current.Profit-prior.Profit,currency),new("صافي حركة الحسابات",current.CashChange,currency)],
            tables,warning,"إجمالي التقارير");
    }
}
