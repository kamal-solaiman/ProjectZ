using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PurchaseReportsViewModel(IPurchaseReportSource? source, Action<int> openInvoice, Action<int> openSupplier)
    : ReportWorkspaceViewModel<PurchaseReportData>(source, openInvoice, openSupplier)
{
    public override string SectionTitle => PurchaseReportProjection.Section;
    public override string ExportPrefix => "Purchases";
    public override string SearchLabel => "رقم الفاتورة أو مرجعها أو المورد";
    public override string IssuerLabel => "مسجّل فاتورة الشراء";
    public override string Explanation => IsMovement
        ? "حسب وقت التسجيل في مصر ولو كانت الفاتورة أقدم. السداد والقبض منفصلان عن إصدار الإشعار والمقاصة."
        : "حسب تاريخ مستند الشراء، بحالته وقت القراءة. يُوزَّع الخصم تحليليًا على البضاعة الباقية؛ رصيد المحل ليس قبضًا.";
    protected override IReadOnlyList<ReportChoice> StatusChoices { get; } = Enumerable.Range(0, 6)
        .Select(k => new ReportChoice(k, PurchaseReportInvoice.StatusText(k))).Prepend(All).ToArray();
    protected override IReadOnlyList<ReportChoice> EventChoices { get; } = Enum.GetValues<PurchaseFactKind>()
        .Select(k => new ReportChoice((int)k, PurchaseFact.KindText(k))).Prepend(All).ToArray();
    protected override IEnumerable<ReportChoice> GetIssuers(PurchaseReportData d) => d.Documents.Values.Where(i=>i.BuyerId.HasValue)
        .DistinctBy(i=>i.BuyerId).Select(i=>new ReportChoice(i.BuyerId,i.BuyerLabel));
    protected override IEnumerable<ReportChoice> GetSuppliers(PurchaseReportData d) => d.Documents.Values.Where(i=>i.SupplierId.HasValue)
        .DistinctBy(i=>i.SupplierId).Select(i=>new ReportChoice(i.SupplierId,i.SupplierLabel));
    protected override IEnumerable<ReportChoice> GetAccounts(PurchaseReportData d) => d.Events.Where(e=>e.AccountId.HasValue)
        .DistinctBy(e=>e.AccountId).Select(e=>new ReportChoice(e.AccountId,e.AccountLabel));
    protected override string? GetDrillPermissionCode(ReportRow row) => "SCREEN.PURCHASE_INVOICES";
    protected override ReportPage Project(PurchaseReportData d, bool movement, ReportFilter filter, string currency) =>
        PurchaseReportProjection.Build(d, movement, filter, currency);
}
