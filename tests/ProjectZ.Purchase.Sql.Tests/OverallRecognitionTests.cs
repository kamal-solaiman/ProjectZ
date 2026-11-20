using System.Collections.ObjectModel;
using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static ProjectZ.Models.OverallMeasure;
using static SqlTest;

internal static partial class OverallReportTests
{
    // Only this disposable LocalDB fixture relocates real, committed postings to separate Egypt days.
    private sealed class Calendar(ConnectionSettingsProvider settings, DateTime start)
    {
        public DateTime At(int day) => new ReportPeriod(start.AddDays(day),start.AddDays(day)).StartUtc;
        public OperatingTotals Read(int day,int? last=null)
        {
            var p=new ReportPeriod(start.AddDays(day),start.AddDays(last??day));
            var d=new OverallReportRepository(settings).LoadAsync(p,CancellationToken.None).GetAwaiter().GetResult();
            return new(d.Figures.Where(f=>f.Day>=DateOnly.FromDateTime(p.From)));
        }
    }
    private static int Product(SqlConnection db,decimal cost,decimal price) => Scalar<int>(db,"""
        INSERT dbo.Products(CategoryId,Name,UnitName,CostPrice,RetailPrice) OUTPUT inserted.Id
        SELECT MIN(Id),N'PRIVATE-P31',N'قطعة',@cost,@price FROM dbo.Categories;
        """,("@cost",cost),("@price",price));

    private static void SalesAndDamage(SqlConnection db,ConnectionSettingsProvider settings,int actor,int cash)
    {
        var t=new Calendar(settings,new DateTime(2044,1,12)); var sales=new SalesRepository(settings,actor);
        var product=Product(db,700,1000);
        Execute(db,"INSERT dbo.StockMovements(ProductId,MovementType,QuantityDelta,UnitCost,ActorUserId) VALUES(@p,N'Supply',1,700,@u);",("@p",product),("@u",actor));
        var customer=sales.CreateCustomer("PRIVATE SALE-P31","01040003100","","","",PriceTier.Retail);
        int sale=sales.CompleteSale(new(customer.Id,false,PriceTier.Retail,0,[new(product,1,PriceTier.Retail,1000)],200,0,0,"")).InvoiceId;
        var invoices=new ObservableCollection<SalesInvoiceDesignItem>(); sales.LoadInvoicesInto(invoices);var sold=invoices.Single(i=>i.Id==sale);
        sales.CreateReturn(new(sold,"PRIVATE-RETURN",[new(sold.Items.Single().DatabaseLineId,0,1)]));
        Execute(db,"""
            UPDATE dbo.Products SET CostPrice=912.34 WHERE Id=@p;
            UPDATE dbo.SalesInvoices SET IssuedAtUtc=@start WHERE Id=@id;
            UPDATE dbo.ReturnExchanges SET CreatedAtUtc=@end WHERE SalesInvoiceId=@id;
            UPDATE dbo.SalesPayments SET PaidAtUtc=CASE PaymentKind WHEN 1 THEN @start ELSE @end END WHERE SalesInvoiceId=@id;
            UPDATE dbo.FinancialTransactions SET OccurredAtUtc=@start WHERE SourceEntityType=N'SalesInvoice' AND SourceEntityId=@id;
            UPDATE dbo.FinancialTransactions SET OccurredAtUtc=@end WHERE SourceEntityType=N'ReturnExchange' AND SourceEntityId IN (SELECT Id FROM dbo.ReturnExchanges WHERE SalesInvoiceId=@id);
            UPDATE dbo.StockMovements SET OccurredAtUtc=@end WHERE MovementType=N'Damage' AND ProductId=@p;
            """,("@id",sale),("@start",t.At(0)),("@end",t.At(1)),("@p",product));
        var first=t.Read(0); var returned=t.Read(1);
        Check(first.NetSales==1000 && first[SalesCost]==700 && first.Profit==300 && first[SaleReceipts]==200 && first.CashChange==200,
            "Credit sale: 1000 - historical 700 earns 300 immediately while only 200 moves; changing catalog cost is immaterial");
        Check(returned.NetSales==-1000 && returned[SalesCost]==-700 && returned[Damage]==700 && returned.Profit==-1000 && returned.CashChange==-200,
            "Damaged quantity return reverses sales and COGS, writes off 700 exactly once, and refunds only the collected 200");
        // A missing historical damage issue is not zero loss even when the return value and historical line cost survived.
        Execute(db,"UPDATE dbo.StockMovements SET MovementType=N'LegacyUnclassified' WHERE ProductId=@p AND MovementType=N'Damage';",("@p",product));
        Check(t.Read(1).Profit==null && t.Read(0).Profit==300,"Missing return write-off blocks a definite result only on its affected date");

        var purchases=new PurchaseRepository(settings,actor); var supplier=purchases.CreateSupplier("مورد إثبات 31", "", "", "", "", "", "", "", "");
        int item=Product(db,100,130);
        PurchaseInvoiceDesignItem Buy(int qty, decimal discount,decimal paid=0)
        {
            int id=purchases.CreateInvoice(new(supplier.Id,"PRIVATE-P31",DateTime.Now,discount,[new(item,qty,100)],paid,0,0,"")).InvoiceId;
            var list=new ObservableCollection<PurchaseInvoiceDesignItem>(); purchases.LoadInvoicesInto(list); return list.Single(i=>i.Id==id);
        }
        void DamageUnit(int unit,int day)
        {
            Execute(db,"""
                UPDATE dbo.InventoryUnits SET UnitStatus=2,DamagedAtUtc=@at,RegisteredAtUtc=DATEADD(day,-1,@at) WHERE Id=@id;
                INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,OccurredAtUtc,ActorUserId)
                    SELECT ProductId,Id,N'Damage',-1,PurchaseCost,@at,@actor FROM dbo.InventoryUnits WHERE Id=@id;
                """,("@id",unit),("@at",t.At(day)),("@actor",actor));
        }
        string Return(PurchaseInvoiceDesignItem b,int qty,int day)
        {
            var r=purchases.ReturnToSupplier(new(b.Id,b.DatabaseRowVersion,[new(b.Items[0].DatabaseLineId,qty)],"اختبار مستند تخفيض",true));
            Execute(db,"UPDATE dbo.PurchaseReturns SET ReturnedAtUtc=@at WHERE ReturnNumber=@n;",("@at",t.At(day)),("@n",r.ReturnNumber));
            return r.ReturnNumber;
        }
        var due=Buy(1,20); DamageUnit(due.Items[0].UnitRegistrationIds.Single(),3); Return(due,1,4);
        Check(t.Read(3).NetDamage==100 && t.Read(3).Profit==-100 && t.Read(4) is {Profit:80m,CashChange:0m} recovery && recovery[DebtReduced]==80 && recovery[Compensation]==80,
            "100 loss on damage day, then a noncash 80 recovery on a documented supplier debt reduction");
        var mixed=Buy(2,20); DamageUnit(mixed.Items[0].UnitRegistrationIds.First(),5); Return(mixed,2,6);
        Check(t.Read(6)[DebtReduced]==180 && t.Read(6)[Compensation]==null && t.Read(6).Profit==null,
            "Mixed two-unit return at discounted 180 does not invent a 90 damaged share or suppress the proven debt reduction");
        var paid=Buy(1,20,80); DamageUnit(paid.Items[0].UnitRegistrationIds.Single(),7); Return(paid,1,8);
        var list=new ObservableCollection<PurchaseInvoiceDesignItem>(); purchases.LoadInvoicesInto(list); var note=list.Single(b=>b.Id==paid.Id);
        purchases.ReceiveCredit(note.Id,note.DatabaseRowVersion,note.CreditNotes.Single(),35,cash,"COMP-P31");
        Execute(db,"""
            UPDATE p SET PaidAtUtc=@at FROM dbo.PurchasePayments p WHERE PurchaseInvoiceId=@id AND PaymentKind=2;
            UPDATE t SET OccurredAtUtc=@at FROM dbo.FinancialTransactions t JOIN dbo.SupplierCreditReceipts x ON x.LedgerId=t.Id JOIN dbo.PurchasePayments p ON p.Id=x.PaymentId WHERE p.PurchaseInvoiceId=@id;
            """,("@id",paid.Id),("@at",t.At(9)));
        Check(t.Read(8)[CreditIssued]==80 && t.Read(8).Profit==80 && t.Read(9) is {Profit:0m,CashChange:35m} collection && collection[SupplierReceived]==35,
            "Credit for documented damage is recognized once at issue; later actual receipt of 35 is cash only");

        int unit=Scalar<int>(db,"INSERT dbo.InventoryUnits(ProductId,PurchaseCost) OUTPUT inserted.Id VALUES(@p,100);",("@p",item));
        DamageUnit(unit,11);
        Execute(db,"""
            UPDATE dbo.InventoryUnits SET DamageSettlementStatus=N'مرتجع للمورد' WHERE Id=@id;
            INSERT dbo.AuditLog(OccurredAtUtc,ActorUserId,Module,ActionName,EntityType,EntityId) VALUES(@at,@actor,N'Inventory',N'ReturnDamagedToSupplier',N'InventoryUnit',CONVERT(nvarchar(100),@id));
            """,("@id",unit),("@at",t.At(12)),("@actor",actor));
        Check(t.Read(12).Profit==0 && t.Read(12)[Compensation]==0,"Settlement text and label-only audit do not compensate damage");
        Execute(db,"""
            UPDATE dbo.InventoryUnits SET UnitStatus=0,DamagedAtUtc=NULL WHERE Id=@id;
            INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,OccurredAtUtc,ActorUserId) VALUES(@p,@id,N'DamageRestore',1,100,@at,@actor);
            """,("@p",item),("@id",unit),("@at",t.At(13)),("@actor",actor));
        DamageUnit(unit,14);
        // The existing hard-delete transaction for an unlinked damaged unit. The archive is not a new posting.
        Execute(db,"""
            BEGIN TRANSACTION;
            DELETE dbo.StockMovements WHERE InventoryUnitId=@id; DELETE dbo.InventoryUnits WHERE Id=@id AND UnitStatus=2;
            INSERT dbo.AuditLog(OccurredAtUtc,ActorUserId,Module,ActionName,EntityType,EntityId) VALUES(@at,@actor,N'Inventory',N'DeleteDamagedUnit',N'InventoryUnit',CONVERT(nvarchar(100),@id));
            COMMIT;
            """,("@at",t.At(15)),("@id",unit),("@actor",actor));
        Check(t.Read(11).Profit==-100 && t.Read(13).Profit==100 && t.Read(14).Profit==-100 && t.Read(15).Profit==0,
            "Deletion preserves original dated issues and genuine restoration; no loss or recovery on destruction itself");
        // Simulate pre-31, when the only surviving evidence was a typed unit mark and the later deletion audit.
        Execute(db,"""
            DELETE dbo.OverallRemovedDamage WHERE UnitId=@id;
            INSERT dbo.AuditLog(OccurredAtUtc,ActorUserId,Module,ActionName,EntityType,EntityId)
                SELECT at,@actor,N'Inventory',kind,N'InventoryUnit',CONVERT(nvarchar(100),@id) FROM (VALUES (@one,N'MarkUnitDamaged'),(@two,N'RestoreDamaged'),(@three,N'MarkUnitDamaged')) x(at,kind);
            """,("@id",unit),("@actor",actor),("@one",t.At(11)),("@two",t.At(13)),("@three",t.At(14)));
        Check(t.Read(11).Profit==null && t.Read(13)[Restored]==null && t.Read(14).Profit==null && t.Read(12).Profit==0 && t.Read(15).Profit==0,
            "Unvalued legacy unit marks retain their own dates; no invented charge on the deletion or intervening days");
    }

    private static void DeliveriesAndReopens(SqlConnection db,ConnectionSettingsProvider settings,int actor)
    {
        var t=new Calendar(settings,new DateTime(2045,2,1)); var writer=new MaintenanceRepository(settings,actor);
        var ticket=writer.CreateMaintenanceOrder(new() {CustomerName="PRIVATE-ORDER",IsWalkInCustomer=true,DeviceModel="اختبار",EstimatedCost=300,DepositCashPaid=100});
        int id=ticket.OrderId;
        MaintenanceTicketKey Key()=>MaintenanceTicketKey.Open(writer.LoadTicket(id));
        void Reopen()=>Execute(db,"""
            EXECUTE AS USER=N'MaintenanceTestSupervisor';
            BEGIN TRY EXEC dbo.ReopenMaintenanceTicket @id,@rv,@actor,N'مراجعة'; END TRY
            BEGIN CATCH REVERT; THROW; END CATCH; REVERT;
            """,("@id",id),("@rv",Key().Version!),("@actor",actor));
        writer.AddManualPart(Key(),"PRIVATE PART",2,20,40,220,0); var removed=writer.LoadTicket(id).UsedParts.Single().LineId;
        writer.Deliver(new(Key(),220,0,300,50,0,0,false,1,"")); Reopen();
        writer.CollectDeposit(Key(),FinancialAccountKind.Cash,10);
        writer.AddManualPart(Key(),"بند ثان",1,15,20,200,0); writer.Deliver(new(Key(),200,0,300,0,0,0,false,1,"")); Reopen();
        writer.RemovePart(Key(),removed,129,0); writer.Deliver(new(Key(),129,0,149,0,0,0,true,1,""));
        // One original transaction timestamp per event. Re-date the matching proof, including the dated JSON anchors.
        Execute(db,"""
            DECLARE @map TABLE(OldAt datetime2(7),NewAt datetime2(7));
            INSERT @map SELECT OccurredAtUtc,DATEADD(day,CONVERT(int,ROW_NUMBER() OVER(ORDER BY Id))-1,CONVERT(datetime2(7),@day))
                FROM dbo.AuditLog WHERE Module=N'Maintenance' AND EntityType=N'MaintenanceOrder' AND EntityId=CONVERT(nvarchar(100),@id) AND ActionName IN(N'Intake',N'Deliver',N'Reopen',N'CollectDeposit');
            UPDATE o SET ReceivedAtUtc=@day,DeliveredAtUtc=m.NewAt FROM dbo.MaintenanceOrders o JOIN @map m ON m.OldAt=o.DeliveredAtUtc WHERE o.Id=@id;
            UPDATE p SET PaidAtUtc=m.NewAt FROM dbo.MaintenancePayments p JOIN @map m ON m.OldAt=p.PaidAtUtc WHERE MaintenanceOrderId=@id;
            UPDATE t SET OccurredAtUtc=m.NewAt FROM dbo.FinancialTransactions t JOIN @map m ON m.OldAt=t.OccurredAtUtc WHERE SourceEntityType=N'MaintenanceOrder' AND SourceEntityId=@id;
            UPDATE a SET OccurredAtUtc=m.NewAt FROM dbo.MaintenanceActivities a JOIN @map m ON m.OldAt=a.OccurredAtUtc WHERE MaintenanceOrderId=@id;
            UPDATE a SET OccurredAtUtc=m.NewAt, OldValuesJson=CASE WHEN b.NewAt IS NULL THEN a.OldValuesJson ELSE JSON_MODIFY(a.OldValuesJson,'$.DeliveredAtUtc',CONVERT(varchar(33),b.NewAt,126)) END,
                NewValuesJson=CASE WHEN a.ActionName=N'Deliver' THEN JSON_MODIFY(a.NewValuesJson,'$.DeliveredAtUtc',CONVERT(varchar(33),m.NewAt,126)) ELSE a.NewValuesJson END
                FROM dbo.AuditLog a JOIN @map m ON m.OldAt=a.OccurredAtUtc LEFT JOIN @map b ON b.OldAt=TRY_CONVERT(datetime2(7),JSON_VALUE(a.OldValuesJson,'$.DeliveredAtUtc'))
                WHERE a.Module=N'Maintenance' AND a.EntityType=N'MaintenanceOrder' AND a.EntityId=CONVERT(nvarchar(100),@id);
            """,("@day",t.At(0)),("@id",id));
        Check(t.Read(0) is {Profit:0m,CashChange:100m} advance && advance[ServiceAdvance]==100,
            "100 intake advance is actual cash only, no revenue before first delivery");
        Check(t.Read(1) is {Profit:110m,CashChange:50m} first && first[ServiceEarned]==150 && first[ServiceParts]==40,
            "First delivery earns accumulated 100 + 50 less the ORIGINAL two manual parts costing 40, not today's edited lines");
        Check(t.Read(2).Profit==0 && t.Read(3).Profit==10 && t.Read(4).Profit==-15 && t.Read(4)[ServiceEarned]==0,
            "Reopen itself has no effect; later receipt +10 on its date, next close charges the +15 cost delta only");
        Check(t.Read(6).Profit==29 && t.Read(6)[ServiceEarned]==-11 && t.Read(6)[ServiceParts]==-40 && t.Read(0,6).Profit==134,
            "Cheaper parts release 40 at third close; only actual refund -11, leaving cumulative 149 collected minus 15 cost");
        Execute(db,"""
            UPDATE dbo.AuditLog SET OldValuesJson=JSON_MODIFY(OldValuesJson,'$.Parts',NULL) WHERE Module=N'Maintenance' AND ActionName=N'Reopen' AND OccurredAtUtc=@at AND EntityId=CONVERT(nvarchar(100),@id);
            """,("@at",t.At(2)),("@id",id));
        Check(t.Read(1).Profit==null && t.Read(4).Profit==null && t.Read(3).Profit==10 && t.Read(6).Profit==29,
            "Absent pre-reopen parts make only the two dependent costs unknown; unrelated receipts and subsequent proven deltas survive");
        Execute(db,"DELETE dbo.FinancialTransactions WHERE SourceEntityType=N'MaintenanceOrder' AND SourceEntityId=@id AND OccurredAtUtc=@at;",("@at",t.At(3)),("@id",id));
        Check(t.Read(3).Profit==null && t.Read(3)[ServiceAdvance]==10 && t.Read(3).CashChange==0,
            "A mismatched settlement cannot certify earned cash or manufacture a portfolio ledger leg");
    }
}
