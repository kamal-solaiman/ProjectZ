using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static ProjectZ.Models.ServiceReportMode;
using static SqlTest;

internal static class MaintenanceReportTests
{
    internal static void CheckUnmatchedRefund(ConnectionSettingsProvider settings,int orderId)
    {
        var period=new ReportPeriod(ReportPeriod.Today.AddDays(-1),ReportPeriod.Today.AddDays(1));
        var d=new MaintenanceReportRepository(settings).LoadAsync(period,CancellationToken.None).GetAwaiter().GetResult();
        var page=MaintenanceReportProjection.Build(d,Movement,new(InvoiceIds:new HashSet<int>{orderId}),"ج.م");
        Check(d.Payments.Single(p=>p.OrderId==orderId && p.Kind==2) is {Amount:5m,LedgerNeedsReview:true,RefundNeedsReview:true}
            && page.Metrics.Single(m=>m.Label=="صافي المقبوض").Value==45 && page.Warning.Length>0,
            "A missing ledger leg is flagged, not counted as a different amount or used to discard the actual refund");
    }

    internal static void Run(SqlConnection db,ConnectionSettingsProvider settings,int actor,int cancelled,int depositOnly,int delivered,int legacy,int stockOrder,int stoppedPart)
    {
        var repo=new MaintenanceReportRepository(settings);
        MaintenanceReportData Read(ReportPeriod p)=>repo.LoadAsync(p,CancellationToken.None).GetAwaiter().GetResult();
        static ReportPage Page(MaintenanceReportData d,ServiceReportMode m,int? order=null)=>MaintenanceReportProjection.Build(d,m,new(InvoiceIds:order.HasValue?new HashSet<int>{order.Value}:null),"ج.م");
        static decimal? Metric(ReportPage p,string name)=>p.Metrics.Single(m=>m.Label==name).Value;
        var day=new ReportPeriod(ReportPeriod.Today.AddDays(-1),ReportPeriod.Today.AddDays(1));
        var before=MaintenanceSettlementTests.Footprint(db);
        var d=Read(day);
        Check(MaintenanceSettlementTests.Footprint(db)==before,"Reading all service report result sets changes no tickets, versions, money, inventory, activity or audit records");
        var closed=Page(d,Movement,cancelled);
        Check(Metric(closed,"عربون ودفعات تحت الحساب")==120m && Metric(closed,"محصّل عند التسليم")==30m && Metric(closed,"مردود للعميل")==150m && Metric(closed,"صافي المقبوض")==0
            && closed.Warning.Length==0 && Page(d,Delivered,cancelled).Tables[0].Rows.Count==0,
            "Repeated deliveries/reopens/cancel do not rebook earlier money: 120 in deposits + 30 final - 150 refunded");
        Check(d.Payments.Where(p=>p.OrderId==cancelled && p.Kind==2).All(p=>p.OriginalReceipt!=null && !p.RefundNeedsReview && !p.LedgerNeedsReview)
            && d.Activities.Count(a=>a.OrderId==cancelled && a.EventType==7 && a.RecordedStatus==1)==3,"Refund receipt/account lineage and three independent supervisor reopens remain auditable");
        Check(Metric(Page(d,Received,depositOnly),"صافي المقبوض")==70.17m && Metric(Page(d,Delivered,delivered),"صافي المقبوض")==30,
            "An estimate of 100 with a 70.17 deposit counts only 70.17; delivering at 100 with actual 25+5 counts only 30");
        Check(d.Payments.Single(p=>p.OrderId==legacy && p.Kind==2) is {Amount:5m,RefundNeedsReview:true,LedgerNeedsReview:false}
            && Page(d,Movement,legacy).Warning.Length>0,"Matched ledger total does not invent an original receipt for a legacy refund");
        Check(!d.Parts[stoppedPart].Active && d.Moves.Count(m=>m.OrderId==stockOrder && m.PartId==stoppedPart && m.Kind==3)==1
            && d.Moves.Count(m=>m.OrderId==stockOrder && m.PartId==stoppedPart && m.Kind==4)==1 && !d.Lines.Any(l=>l.OrderId==stockOrder),
            "Returned and archived workshop parts remain in period movements after the saved ticket line has gone");
        // Only fixture clocks are moved; no changes to the production data source or accounting policies.
        var date=new DateTime(2038,1,17); var period=new ReportPeriod(date,date);
        Execute(db,"""
            UPDATE dbo.MaintenanceOrders SET ReceivedAtUtc=DATEADD(second,-1,@start),DeliveredAtUtc=@end,
                   PasscodeValueProtected=N'PRIVATE-P28',DeliveryNotes=N'PRIVATE-P28' WHERE Id=@order;
            UPDATE dbo.MaintenancePayments SET PaidAtUtc=CASE WHEN PaymentStage=2 THEN @end WHEN Amount=15 THEN @start ELSE DATEADD(second,-1,@start) END WHERE MaintenanceOrderId=@order;
            INSERT dbo.MaintenanceActivities(MaintenanceOrderId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
                VALUES(@order,@start,N'PRIVATE-P28',N'PRIVATE-P28',987654.32,@actor);
            INSERT dbo.MaintenanceSparePartMovements(SparePartId,MovementKind,Quantity,QuantityBefore,QuantityAfter,UnitCost,Note,ActorUserId,OccurredAtUtc)
                VALUES(@part,2,2,3,5,987654.32,N'PRIVATE-P28',@actor,@start);
            """,("@start",period.StartUtc),("@end",period.EndUtc),("@order",delivered),("@actor",actor),("@part",stoppedPart));
        var at=Read(period); var move=Page(at,Movement);
        Check(Metric(move,"صافي المقبوض")==15 && move.Tables[0].Rows.Count==2 && !Page(at,Received).Tables[0].Rows.Any() && !Page(at,Delivered).Tables[0].Rows.Any(),
            "Boundary at Egypt midnight: one actual deposit on an older ticket and one nonmonetary activity, not tomorrow's delivery or isolated restock");
        var yesterday=Read(new(date.AddDays(-1),date.AddDays(-1)));var tomorrow=Read(new(date.AddDays(1),date.AddDays(1)));
        Check(Metric(Page(yesterday,Received,delivered),"صافي المقبوض")==30 && Metric(Page(tomorrow,Delivered,delivered),"صافي المقبوض")==30 && Metric(Page(tomorrow,Movement),"صافي المقبوض")==5,
            "Both ticket cohorts see the whole 30 as of reading, while only the final receipt of 5 falls on the next day");
        Check(Page(at,Stock).Tables.Single(t=>t.Title=="حركات مخزون الفترة").Rows.Single() is {CanOpenInvoice:false,PartId:null}
            && at.Moves.Single() is {Kind:2,Before:3,After:5,OrderId:null},"Independent restocking on an archived part has quantities but no invented ticket or active catalog target");
        using var till=new SqlConnection(settings.GetConnectionString());till.Open();
        Check(Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.ReadMaintenanceReport','OBJECT','EXECUTE')")==1 && Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.MaintenancePayments','OBJECT','UPDATE')")==0,"Restricted till can read without acquiring direct money writes");
        using var cmd=new SqlCommand("dbo.ReadMaintenanceReport",till){CommandType=System.Data.CommandType.StoredProcedure};
        cmd.Parameters.AddWithValue("@FromUtc",period.StartUtc);cmd.Parameters.AddWithValue("@UntilUtc",period.EndUtc);
        using var reader=cmd.ExecuteReader();int sets=0,amountColumns=0;
        do {
            sets++;
            for(int n=0;n<reader.FieldCount;n++)if(reader.GetFieldType(n)==typeof(decimal)) {
                Check(sets==3 && reader.GetName(n)=="Amount","Only a saved customer payment amount crosses the SQL report contract");amountColumns++; }
            while(reader.Read())for(int n=0;n<reader.FieldCount;n++)
                if(reader.GetValue(n) is string value && value.Contains("PRIVATE-P28"))throw new Exception("Private note, raw activity title or device unlock data leaked into service report");
        }while(reader.NextResult());
        Check(sets==7 && amountColumns==1,"Seven bounded result sets contain no pricing fields, activity amounts or protected/free-text notes");
    }
}
