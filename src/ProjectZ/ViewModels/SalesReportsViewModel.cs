using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class SalesReportsViewModel(ISalesReportSource? source, Action<int> openInvoice)
    : ReportWorkspaceViewModel<SalesReportData>(source, openInvoice)
{
    public override string SectionTitle => "تقارير المبيعات";
    public override string ExportPrefix => "Sales";
    public override string SearchLabel => "رقم الفاتورة أو اسم العميل";
    public override string IssuerLabel => "كاشير إصدار الفاتورة";
    public override string Explanation => IsMovement
        ? "دفتر الحركة بتوقيت مصر • قيمة البضاعة منفصلة عن الأموال المحصلة أو المردودة فعليًا."
        : "فواتير صادرة في الفترة بحالتها وقت القراءة • تشمل دفعاتها ومرتجعاتها اللاحقة. مجمل الربح قبل مصروفات المحل.";
    protected override IReadOnlyList<ReportChoice> StatusChoices { get; } = Enumerable.Range(0, 6)
        .Select(k => new ReportChoice(k, ReportInvoice.StatusText((byte)k))).Prepend(All).ToArray();
    protected override IReadOnlyList<ReportChoice> EventChoices { get; } = Enum.GetValues<SalesEventKind>()
        .Select(k => new ReportChoice((int)k, SalesReportEvent.KindText(k))).Prepend(All).ToArray();
    protected override IEnumerable<ReportChoice> GetIssuers(SalesReportData d) => d.Invoices.Select(i => i.Doc).Concat(d.Events.Select(e => e.Doc))
        .DistinctBy(x => x.CashierId).Select(x => new ReportChoice(x.CashierId, $"{x.Cashier} (#{x.CashierId})"));
    protected override IEnumerable<ReportChoice> GetAccounts(SalesReportData d) => d.Events.Where(e => e.AccountId.HasValue)
        .DistinctBy(e => e.AccountId).Select(e => new ReportChoice(e.AccountId, e.AccountLabel));
    protected override ReportPage Project(SalesReportData d, bool movement, ReportFilter filter, string currency) =>
        SalesReportProjection.Build(d, movement, filter, currency);
}
