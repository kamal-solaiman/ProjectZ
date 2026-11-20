using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;

internal static class SalesReportTests
{
    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings, int older, int fullyReturned, int exchange, int cancel, int legacy, int cash, int wallet)
    {
        // These are real 22 postings. Only the isolated test DBA moves their clocks to deterministic Cairo boundaries.
        var reports=new SalesReportRepository(settings);
        SalesReportData Read(SalesReportPeriod p)=>reports.LoadAsync(p,CancellationToken.None).GetAwaiter().GetResult();
        var day=new DateTime(2034,1,12); var period=new SalesReportPeriod(day,day);
        Execute(db,$"""
            UPDATE dbo.SalesInvoices SET IssuedAtUtc=DATEADD(second,-1,@start) WHERE Id={older};
            UPDATE dbo.SalesPayments SET PaidAtUtc=CASE WHEN Amount=100 THEN DATEADD(second,-1,@start) ELSE @start END WHERE SalesInvoiceId={older};
            UPDATE dbo.ReturnExchanges SET CreatedAtUtc=DATEADD(minute,4,@start) WHERE SalesInvoiceId={older};
            """,("@start",period.StartUtc));
        var activity=Read(period);
        Check(!activity.Invoices.Any() && activity.Events.Count==2 && activity.Events.Single(e=>e.IsMoney).Received==200
            && activity.Events.Single(e=>!e.IsMoney).SalesDelta==-300 && activity.Events.Sum(e=>e.Refunded)==0,
            "A 200 collection at inclusive midnight and 300 goods return today on yesterday's sale; NO refund or new issued invoice");
        var yesterday=Read(new(day.AddDays(-1),day.AddDays(-1))); var doc=yesterday.Invoices.Single(i=>i.Doc.Id==older);
        Check(doc.Gross==1000 && doc.Net==700 && doc.Received==300 && doc.Due==400 && yesterday.Events.Count(e=>e.Doc.Id==older)==4,
            "The issue-date cohort includes later changes and its full payment history; no detail/payment fan-out");
        Check(!SalesReportProjection.Build(yesterday,true,new(),"ج.م").Tables[0].Rows.Any(r=>r.Cells.Contains(-300m)),"Activity excludes later returns from yesterday");
        Execute(db,$"UPDATE dbo.SalesPayments SET PaidAtUtc=@until WHERE SalesInvoiceId={older} AND Amount=200;",("@until",period.EndUtc));
        Check(Read(period).Events.Count==1 && Read(new(day.AddDays(1),day.AddDays(1))).Events.Single().Received==200,
            "Exclusive upper boundary and an independent refresh; no stale or duplicated movement");

        var today=SalesReportPeriod.Today;
        var all=Read(new(today.AddDays(-1),today.AddDays(1)));
        var full=all.Invoices.Single(i=>i.Doc.Id==fullyReturned); var eFull=all.Events.Where(e=>e.Doc.Id==fullyReturned).ToArray();
        Check(full.Net==0 && full.Cost==0 && full.Refunded==399.95m && eFull.Where(e=>e.Kind==SalesEventKind.Return).Sum(e=>e.SalesDelta)==-399.95m
            && eFull.Any(e=>e.AccountId==cash && e.Refunded==40) && eFull.Any(e=>e.AccountId==wallet && e.Refunded==59.99m),
            "Multiple lines, serialized units and split original wallets never multiply report money; refunds retain the exact account");
        var eSwap=all.Events.Where(e=>e.Doc.Id==exchange).ToArray();
        Check(eSwap.Where(e=>e.Kind==SalesEventKind.Issue).Sum(e=>e.SalesDelta)==180 && eSwap.Single(e=>e.Kind==SalesEventKind.ExchangeReturn).SalesDelta==-90
            && eSwap.Single(e=>e.Kind==SalesEventKind.ExchangeIssue).SalesDelta==50 && eSwap.Sum(e=>e.Refunded)==10,
            "An exchange's replacement is issued once on its event, not backdated to the original sale");
        Check(all.Events.Single(e=>e.Doc.Id==cancel && e.Kind==SalesEventKind.Cancel).SalesDelta==-500
            && all.Invoices.Single(i=>i.Doc.Id==cancel).Net==0 && all.Events.Any(e=>e.Doc.Id==cancel && e.Refunded==425),
            "Cancellation reads its dated audit net, not the 425 paid out or the current zero invoice net");
        // Fixtures in the disposable DB only: pre-22 CancelSale saved a typed audit time but not $.net.
        var cancelled = all.Events.Single(e=>e.Doc.Id==cancel && e.Kind==SalesEventKind.Cancel);
        Execute(db,"UPDATE dbo.AuditLog SET OldValuesJson=NULL,NewValuesJson=N'{\"details\":\"إلغاء فاتورة\"}' WHERE Id=@id;", ("@id",cancelled.SourceId));
        var legacyCancel=Read(all.Period).Events.Where(e=>e.Doc.Id==cancel).ToArray();
        Check(legacyCancel.Single(e=>e.Kind==SalesEventKind.Cancel) is {NeedsReview:true, SalesDelta:null} unknown
            && unknown.AtUtc==cancelled.AtUtc && legacyCancel.Sum(e=>e.Refunded)==425,
            "Legacy typed cancellation retains its date and actual refund, without manufacturing the missing goods amount");
        var old=SalesReportProjection.Build(all,true,new(InvoiceIds:new HashSet<int>{legacy}),"ج.م");
        Check(old.Metrics[0].Value is null && old.Warning.Length>0 && old.Metrics[3].Value==180,"A legacy return without stored value flags the goods total; actual receipts remain exact");
        var original=all.Lines.First(l=>l.InvoiceId==exchange); var gross=original.Net;
        Execute(db,$"UPDATE dbo.Products SET CostPrice=CostPrice+1 WHERE Id={original.ProductId};");
        var reload=Read(all.Period).Lines.Single(l=>l.InvoiceId==exchange && l.Number==original.Number);
        Check(reload.UnitCost==original.UnitCost && reload.Net==gross,"Catalog cost changes do not revalue a historical line");
        using var till=new SqlConnection(settings.GetConnectionString());till.Open();
        Check(Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.ReadSalesReport','OBJECT','EXECUTE');")==1 &&
            Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.SalesInvoices','OBJECT','UPDATE');")==0,"Cashier reads all report columns without gaining invoice write access");
    }
}
