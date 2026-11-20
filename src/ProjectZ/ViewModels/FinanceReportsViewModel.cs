using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class FinanceReportsViewModel : ReportWorkspaceViewModel<FinanceReportData>
{
    private readonly Action<FinanceLink> navigate;
    private HashSet<int> walletIds=[];
    private ReportChoice direction=All;
    public FinanceReportsViewModel(IFinanceReportSource? source,Action<FinanceLink> navigate) : base(source,null,initialMode:(int)FinanceReportMode.Ledger)
    {
        this.navigate=navigate;
        OpenAccountCommand=new ActionCommand(()=>{if(SelectedRow?.FinancialAccount is {} link)navigate(link);},()=>CanOpenAccount);
    }
    private FinanceReportMode Basis=>(FinanceReportMode)ModeId;
    public override IReadOnlyList<ReportChoice> Modes {get;}=[new(1,"دفتر حركة المال"),new(0,"كشف الأرصدة والمطابقة"),new(2,"عمولات حسب يوم الحركة"),new(3,"تحويلات حسب تاريخ الإصدار"),new(4,"حدود واستخدام المحافظ")];
    public override string SectionTitle=>FinanceReportProjection.Section;
    public override string ExportPrefix=>"Finance";
    public override string ExportMode=>Basis.ToString();
    public override object ExtraTools=>this;
    protected override bool ClearOnModeChange=>true;
    public override bool ShowIssuerFilter=>Basis is FinanceReportMode.Ledger or FinanceReportMode.FeeEvents or FinanceReportMode.IssuedTransfers;
    public override string IssuerLabel=>"الموظف المسجّل للعملية";
    public override string SearchLabel=>Basis switch {FinanceReportMode.Ledger=>"الحساب، رقم القيد أو مرجع المستند",FinanceReportMode.FeeEvents or FinanceReportMode.IssuedTransfers=>"مرجع التحويل، المحفظة أو آخر أرقام العميل",_=>"الحساب أو آخر أرقام المعرّف"};
    public override string DetailLabel=>Basis switch {FinanceReportMode.Ledger=>"المصدر، نوع القيد أو الموظف",FinanceReportMode.FeeEvents or FinanceReportMode.IssuedTransfers=>"الموظف، إيداع / سحب أو تصحيح",_=>"نشط، موقوف أو افتراضي"};
    public override string KindLabel=>Basis switch {FinanceReportMode.Ledger=>"مصدر القيد",FinanceReportMode.FeeEvents=>"تسجيل أو عكس",FinanceReportMode.IssuedTransfers=>"حالة التحويل الآن",_=>"قناة الحساب"};
    private bool WalletOnly=>Basis is FinanceReportMode.FeeEvents or FinanceReportMode.IssuedTransfers or FinanceReportMode.Limits;
    public override string AccountLabel=>WalletOnly?"المحفظة الفعلية":"الحساب الفعلي";
    public override string Explanation=>Basis switch {
        FinanceReportMode.Statement=>"الافتتاح وأرصدة الإنشاء والحركة والإقفال لكل حساب؛ الرصيد الآن للمطابقة وليس رصيد إقفال تاريخيًا.",
        FinanceReportMode.FeeEvents=>"تُسجل العمولة مرة يوم التحويل، ويظهر عكسها بالسالب يوم العكس. أصل التحويل ليس ربحًا.",
        FinanceReportMode.IssuedTransfers=>"عمليات بتاريخ الإصدار المحفوظ وحالتها الآن؛ ليست عمولات حركة الفترة. التصحيح له مرجع مستقل.",
        FinanceReportMode.Limits=>"الحدود المضبوطة واستخدام اليوم والشهر الآن، واستخدام الفترة المختارة في جداول مستقلة بتوقيت مصر.",
        _=>"كل صف أثر فعلي على حساب واحد. افتح المجموعة لتفصيلها أو القيد للانتقال إلى مستنده. الأرقام الشخصية محجوبة."};
    public override string TableCaption=>SelectedTable is not {} t?"":$"{t.Rows.Count:N0} صف • "+(t.Grouped?"افتح المجموعة لعرض صفوفها فقط":SelectedRow?.FinancialSource!=null?"فتح السجل الأصلي":"افتح الحساب إن كان متاحًا");
    public bool CanOpenAccount=>SelectedRow?.FinancialAccount!=null;
    public ActionCommand OpenAccountCommand {get;}
    public IReadOnlyList<ReportChoice> Directions {get;}=[All,new(1,"وارد"),new(0,"منصرف")];
    public ReportChoice Direction {get=>direction;set{if(value==null || direction==value)return;direction=value;Changed();ResetDrill();}}
    protected override void ClearExtraFilters(){direction=All;Changed(nameof(Direction));}
    protected override void NotifyRowActions(){Changed(nameof(CanOpenAccount));OpenAccountCommand.NotifyCanExecuteChanged();}
    protected override bool CanOpenDetail(ReportRow r)=>r.FinancialSource!=null || r.FinancialAccount!=null;
    protected override void OpenDetail(ReportRow r){if((r.FinancialSource??r.FinancialAccount) is {} link)navigate(link);}
    protected override IReadOnlyList<ReportChoice> EventChoices {get;}=Enum.GetValues<FinanceSource>().Select(s=>new ReportChoice((int)s,FinanceReportProjection.SourceText(s))).Prepend(All).ToArray();
    protected override IReadOnlyList<ReportChoice> StatusChoices {get;}=[All,new(0,"الخزنة"),new(1,"فودافون كاش"),new(2,"إنستاباي")];
    public override IReadOnlyList<ReportChoice> KindOptions=>Basis switch {FinanceReportMode.Ledger=>EventChoices,FinanceReportMode.Limits=>StatusChoices.Where(c=>c.Id!=0).ToArray(),
        FinanceReportMode.FeeEvents or FinanceReportMode.IssuedTransfers=>[All,new(0,FinanceReportProjection.FilterText(Basis,0)),new(1,FinanceReportProjection.FilterText(Basis,1))],_=>StatusChoices};
    protected override IEnumerable<ReportChoice> GetIssuers(FinanceReportData d)=>d.Actors.Keys.Select(id=>new ReportChoice(id,d.Actor(id)));
    protected override IEnumerable<ReportChoice> GetAccounts(FinanceReportData d)=>d.Accounts.Values.Select(a=>new ReportChoice(a.Id,a.Label));
    protected override void SetExtraChoices(FinanceReportData d)=>walletIds=d.Accounts.Values.Where(a=>a.Provider!=0).Select(a=>a.Id).ToHashSet();
    public override IReadOnlyList<ReportChoice> AccountChoices=>WalletOnly?Accounts.Where(a=>!a.Id.HasValue || walletIds.Contains(a.Id.Value)).ToArray():Accounts;
    protected override ReportPage Project(FinanceReportData d,bool movement,ReportFilter f,string currency)=>FinanceReportProjection.Build(d,Basis,f,currency,direction.Id is null?null:direction.Id==1);
}
