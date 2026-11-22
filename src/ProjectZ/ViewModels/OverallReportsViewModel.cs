using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class OverallReportsViewModel(IOverallReportSource? source) : ReportWorkspaceViewModel<OverallReportData>(source,null)
{
    public override string SectionTitle => "إجمالي التقارير";
    public override string Explanation => "كيف تكوّنت نتيجة المحل؟ قارن الفترة بالسابقة، ثم اختر قسمًا لقراءة إجمالياته. حركة المال ليست ربحًا.";
    public override IReadOnlyList<ReportChoice> Modes { get; } = [new(0,"ملخص المحل")];
    public override string TableCaption => "إجماليات فقط • لا يعرض هذا التقرير سجلات فردية";
    public override bool ShowDocumentTools => false;
    public override string ExportPrefix => "Overall";
    public override string ExportMode => "Summary";
    public override string SearchLabel => "";
    public override string IssuerLabel => "";
    protected override IReadOnlyList<ReportChoice> StatusChoices => [];
    protected override IReadOnlyList<ReportChoice> EventChoices => [];
    protected override IEnumerable<ReportChoice> GetIssuers(OverallReportData _) => [];
    protected override IEnumerable<ReportChoice> GetAccounts(OverallReportData _) => [];
    protected override ReportPage Project(OverallReportData data,bool movement,ReportFilter filter,string currency) => OverallReportProjection.Build(data,currency);
}
