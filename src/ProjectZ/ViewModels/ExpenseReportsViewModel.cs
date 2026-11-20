using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class ExpenseReportsViewModel : ReportWorkspaceViewModel<ExpenseReportData>
{
    private readonly Action<FinanceLink> navigate;
    private ReportChoice category = All, verification = All;
    public ExpenseReportsViewModel(IExpenseReportSource? source, Action<FinanceLink> navigate) : base(source,null)
    {
        this.navigate = navigate;
        OpenAccountCommand = new ActionCommand(() => { if (SelectedRow?.FinancialAccount is {} account) navigate(account); }, () => CanOpenAccount);
    }
    public override IReadOnlyList<ReportChoice> Modes { get; } = [new(0,ExpenseReportProjection.DocumentsTitle),new(1,ExpenseReportProjection.PostingsTitle)];
    public override string SectionTitle => ExpenseReportProjection.Section;
    public override string ExportPrefix => "Expenses";
    public override string ExportMode => IsMovement ? "Postings" : "Documents";
    public override string IssuerLabel => IsMovement ? "منفّذ القيد" : "مسجّل المصروف";
    public override string SearchLabel => IsMovement ? "رقم المصروف أو القيد" : "رقم المصروف أو مرجعه الداخلي";
    public override string DetailLabel => "التصنيف، الحساب أو الموظف (دون البيان)";
    public override string KindLabel => IsMovement ? "اتجاه القيد الفعلي" : "توزيع المستند";
    public override string AccountLabel => IsMovement ? "قيود الحساب الفعلي فقط" : "مستندات شارك فيها الحساب (المستند كاملًا)";
    public override string Explanation => IsMovement
        ? "كل صف قيد فعلي بتاريخ الخصم أو الوارد بتوقيت مصر. فلتر الحساب يعرض حصته فقط. المقارنة بصافي الخصم في الفترة السابقة المساوية."
        : "يُحسب المستند مرة بتاريخ المصروف. توزيع الحسابات ومطابقته بالدفتر مستقلان عن هذا المبلغ. اختر «مقارنة التصنيفات» أو «مقارنة الحسابات» لتفسير التغيّر.";
    public override string TableCaption => SelectedTable is not {} t ? "" : $"{t.Rows.Count:N0} صف • " + (t.Grouped ? "افتح المجموعة للاطّلاع على الأسطر المكوِّنة للمبلغ"
        : SelectedRow?.FinancialSource != null ? "فتح فاتورة المصروف الأصلية للاطّلاع على البيان" : "البيان متاح فقط من الفاتورة الأصلية");
    protected override IReadOnlyList<ReportChoice> StatusChoices { get; } = [All,new(1,"حساب واحد"),new(2,"دفع مقسّم"),new(0,"بلا توزيع")];
    protected override IReadOnlyList<ReportChoice> EventChoices { get; } = [All,new(1,"منصرف"),new(2,"وارد للمراجعة")];
    protected override bool ClearOnModeChange => true;
    public override object ExtraTools => this;
    public bool CanOpenAccount => SelectedRow?.FinancialAccount != null;
    public ActionCommand OpenAccountCommand { get; }
    public IReadOnlyList<ReportChoice> Categories { get; private set; } = [All];
    public IReadOnlyList<ReportChoice> VerificationChoices { get; } = [All,new(1,"مطابق"),new(0,"تحتاج مراجعة")];
    public ReportChoice Category { get => category; set { if (value == null || category == value) return; category = value; Changed(); ResetDrill(); } }
    public ReportChoice Verification { get => verification; set { if (value == null || verification == value) return; verification = value; Changed(); ResetDrill(); } }
    protected override void ClearExtraFilters() { category = verification = All; Changed(nameof(Category)); Changed(nameof(Verification)); }
    protected override void SetExtraChoices(ExpenseReportData d)
    {
        Categories = d.Categories.Keys.Select(id => new ReportChoice(id,d.CategoryName(id))).OrderBy(c => c.Label).Prepend(All).Append(category).DistinctBy(c => c.Id).ToArray();
        category = Categories.First(c => c.Id == category.Id); Changed(nameof(Categories)); Changed(nameof(Category));
    }
    protected override void NotifyRowActions() { Changed(nameof(CanOpenAccount)); OpenAccountCommand.NotifyCanExecuteChanged(); }
    protected override bool CanOpenDetail(ReportRow row) => row.FinancialSource != null;
    protected override void OpenDetail(ReportRow row) { if (row.FinancialSource is {} source) navigate(source); }
    protected override IEnumerable<ReportChoice> GetIssuers(ExpenseReportData d) => d.Actors.Keys.Select(id => new ReportChoice(id,d.ActorName(id)));
    protected override IEnumerable<ReportChoice> GetAccounts(ExpenseReportData d) => d.Accounts.Values.Select(a => new ReportChoice(a.Id,a.Label));
    protected override ReportPage Project(ExpenseReportData d, bool movement, ReportFilter f, string currency) =>
        ExpenseReportProjection.Build(d,movement,f,currency,category.Id,verification.Id == null ? null : verification.Id == 1);
}
