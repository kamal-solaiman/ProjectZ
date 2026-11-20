using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static ProjectZ.Models.PurchaseFactKind;
using static SqlTest;

internal static class PurchaseReportTests
{
    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings, int actor, int supplier, int product, int serial,
        int source, int target, int note, string firstReturn, int receiving, int legacy, int archivedInvoice)
    {
        var period = new ReportPeriod(ReportPeriod.Today.AddDays(-1),ReportPeriod.Today.AddDays(1));
        var repo = new PurchaseReportRepository(settings);
        PurchaseReportData Read(ReportPeriod p) => repo.LoadAsync(p,CancellationToken.None).GetAwaiter().GetResult();
        var before = PurchaseSettlementTests.Footprint(db);
        var d = Read(period);
        Check(before==PurchaseSettlementTests.Footprint(db),"Reading all purchase report sections changes no invoice, balance, note, stock, audit or version");
        var i = d.Invoices.Single(x=>x.Id==source);
        Check(i.Net==0 && i.Due==0 && i.Available==50 && i.Paid==120.55m && i.Received==20.55m && !i.NeedsReview,
            "Current source snapshot retains split outflows, actual receipt and remaining supplier credit after full return");
        var events = d.Events.Where(e=>e.InvoiceId==source).ToList();
        Check(events.Single(e=>e.Kind==Receipt) is {Amount:20.55m, Reference:"RCPT-25", LedgerId:not null} receipt && receipt.AccountId==receiving && receipt.CreditId==note
            && events.Count(e=>e.Kind==Payment)==2 && events.Where(e=>e.Kind==Payment).Sum(e=>e.Amount)==120.55m,
            "Split payments and one linked receipt on the exact non-default account, regardless of return units");
        Check(events.Where(e=>e.Kind==Return).Sum(e=>e.DocumentDelta)==-150 && events.Where(e=>e.Kind==CreditIssue).Sum(e=>e.Amount)==120.55m
            && d.ReturnLines.Where(l=>l.InvoiceId==source).Sum(l=>l.UnitCost*l.Quantity)==240,
            "240 gross of returned items is neither the 150 net reduction nor the 120.55 credit nor money received");
        Check(d.Events.Single(e=>e.Kind==CreditApplication && e.CreditId==note) is {Amount:50m,AccountId:null} application && application.RelatedId==source && application.InvoiceId==target
            && d.Notes.Single(n=>n.Id==note) is {Applied:50m,Received:20.55m,Available:0m}, "One non-cash application, with both invoices and the note identity");
        Check(d.Events.Single(e=>e.Kind==Cancel && e.InvoiceId==target).DocumentDelta==-60 && !d.Documents[archivedInvoice].CanOpenSupplier,
            "Cancellation is counted once, and archived suppliers remain in the historical report");
        Check(d.Invoices.Single(x=>x.Id==legacy).NeedsReview && d.Events.Single(e=>e.InvoiceId==legacy && e.Kind==Return).DocumentDelta==null,
            "An undocumented old return is visible with unknown value, not zero or new credit");

        var postings = new PurchaseRepository(settings,actor);
        var future = new DateTime(2035,2,3); var day=new ReportPeriod(future,future);
        var id=postings.CreateInvoice(new(supplier,"DISC-27",day.StartUtc.AddHours(2),100,[new(product,2,300),new(serial,1,400)],0,0,0,"اختبار")).InvoiceId;
        var issued=Read(day); var original=issued.Lines.OrderBy(l=>l.Position).ToArray();
        Check(issued.Invoices.Single().Net==900 && original.Select(l=>l.Net).SequenceEqual([540m,360m]) && !issued.Events.Any(e=>day.Contains(e.AtUtc)),
            "Backdated/forward-dated document cohort and actual posting dates are independent; 600/400 with 100 discount gives 540/360");
        var version=Scalar<byte[]>(db,$"SELECT RowVersion FROM dbo.PurchaseInvoices WHERE Id={id}");
        var line=Scalar<int>(db,$"SELECT Id FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId={id} AND ProductId={product}");
        postings.ReturnToSupplier(new(id,version,[new(line,1)],"اختبار التوزيع على الباقي",false));
        issued=Read(day); var remaining=issued.Lines.OrderBy(l=>l.Position).ToArray();
        Check(remaining.Select(l=>l.Discount).SequenceEqual([42.86m,57.14m]) && remaining.Sum(l=>l.Net)==issued.Invoices.Single().Net
            && issued.Events.Single(e=>e.InvoiceId==id && e.Kind==Return).DocumentDelta==-300,
            "After return reallocate 100 on remaining 300/400 to the cent; do not use analytical shares to value the return");

        // Move only disposable fixture clocks; one application must be discoverable via source OR target without duplication.
        Execute(db,"UPDATE dbo.PurchaseInvoices SET IssuedAtUtc=DATEADD(second,-1,@at) WHERE Id=@source; UPDATE dbo.PurchaseReturns SET ReturnedAtUtc=@at WHERE ReturnNumber=@r; UPDATE dbo.SupplierCreditApplications SET AppliedAtUtc=@end WHERE CreditId=@n; UPDATE p SET PaidAtUtc=@at FROM dbo.PurchasePayments p JOIN dbo.SupplierCreditReceipts x ON x.PaymentId=p.Id WHERE x.Evidence=N'RCPT-25'; UPDATE t SET OccurredAtUtc=@at FROM dbo.FinancialTransactions t JOIN dbo.SupplierCreditReceipts x ON x.LedgerId=t.Id WHERE x.Evidence=N'RCPT-25';",
            ("@source",source),("@r",firstReturn),("@n",note),("@at",day.StartUtc),("@end",day.EndUtc));
        var activity=Read(day);
        var movement=PurchaseReportProjection.Build(activity,true,new(),"ج.م");
        Check(movement.Metrics[0].Value==-100 && movement.Metrics[1].Value==0 && movement.Metrics[2].Value==20.55m && movement.Metrics[4].Value==70.55m && movement.Metrics[5].Value==0,
            "Midnight on an older invoice: separate 100 goods return, 70.55 right and 20.55 receipt; exclude next midnight's application");
        var older=Read(new(future.AddDays(-1),future.AddDays(-1))); var page=PurchaseReportProjection.Build(older,false,new(),"ج.م");
        Check(page.Tables.Single(t=>t.Title=="المقاصات").Rows.Single().RelatedInvoiceId==source && page.Tables.Single(t=>t.Title=="إشعارات المورد").Rows.Count==2,
            "Issuance cohort retains later applications on other target invoices, not just credits applied to itself");
        var next=Read(new(future.AddDays(1),future.AddDays(1)));
        Check(next.Invoices.Count==0 && next.Events.Single().Key.Kind==(int)CreditApplication && next.Documents.ContainsKey(source) && next.Documents.ContainsKey(target),
            "Exclusive upper boundary becomes the next day's noncash application, both source and target outside cohort");

        int deleted=postings.CreateInvoice(new(supplier,"DEL-27",DateTime.UtcNow,0,[new(product,1,80)],0,0,0,"")).InvoiceId;
        postings.DeleteInvoice(deleted,Scalar<byte[]>(db,$"SELECT RowVersion FROM dbo.PurchaseInvoices WHERE Id={deleted}"));
        Execute(db,$"INSERT dbo.PurchasePayments(PurchaseInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,ActorUserId) VALUES({legacy},{receiving},2,3,SYSUTCDATETIME(),{actor});");
        var audit=Read(period);
        Check(audit.Documents[deleted] is {CanOpen:false} gone && gone.SupplierId==supplier && gone.SupplierReference=="DEL-27" && audit.Events.Where(e=>e.InvoiceId==deleted).Sum(e=>e.DocumentDelta)==0
            && audit.Events.Single(e=>e.InvoiceId==deleted && e.Kind==Delete).DocumentDelta==-80,
            "Physical deletion retains valued dated audit entries without offering a nonexistent invoice");
        var old=PurchaseReportProjection.Build(audit,true,new(InvoiceIds:new HashSet<int>{legacy}),"ج.م");
        Check(old.Metrics[0].Value==null && old.Metrics[2].Value==0 && old.Metrics[3].Value==3 && old.Warning.Length>0,
            "Legacy recorded money without linked evidence is reported separately; no false verified receipt or historical valuation");
        using var till=new SqlConnection(settings.GetConnectionString()); till.Open();
        Check(Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.ReadPurchaseReport','OBJECT','EXECUTE');")==1 && Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.PurchasePayments','OBJECT','INSERT');")==0,
            "Report is executable by the restricted till without any new money or document write permission");
    }
}
