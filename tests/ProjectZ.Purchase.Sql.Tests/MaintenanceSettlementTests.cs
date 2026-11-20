using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;

internal static class MaintenanceSettlementTests
{
    // Snapshot persistent effects, not the database-wide rowversion/identity counters (gaps on rollback are normal).
    internal static string Footprint(SqlConnection db) => Scalar<string>(db, """
        SELECT CONCAT((SELECT Id,CONVERT(varchar(18),RowVersion,1) AS RV FROM dbo.MaintenanceOrders ORDER BY Id FOR JSON PATH),N'|',
          (SELECT * FROM dbo.MaintenanceOrderParts ORDER BY Id FOR JSON PATH),N'|',
          (SELECT Id,CONVERT(varchar(18),RowVersion,1) AS RV FROM dbo.MaintenanceSpareParts ORDER BY Id FOR JSON PATH),N'|',
          (SELECT * FROM dbo.MaintenanceSparePartMovements ORDER BY Id FOR JSON PATH),N'|',
          (SELECT * FROM dbo.MaintenancePayments ORDER BY Id FOR JSON PATH),N'|',
          (SELECT * FROM dbo.MaintenanceActivities ORDER BY Id FOR JSON PATH),N'|',
          (SELECT Id,CurrentBalance,CONVERT(varchar(18),RowVersion,1) AS RV FROM dbo.FinancialAccounts ORDER BY Id FOR JSON PATH),N'|',
          (SELECT * FROM dbo.FinancialTransactions WHERE SourceEntityType=N'MaintenanceOrder' ORDER BY Id FOR JSON PATH),N'|',
          (SELECT * FROM dbo.AuditLog WHERE Module=N'Maintenance' ORDER BY Id FOR JSON PATH),N'|',
          (SELECT SequenceYear,LastValue FROM dbo.DocumentSequences WHERE DocumentType=N'MaintenanceOrder' ORDER BY SequenceYear FOR JSON PATH));
        """);

    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings, int actor, int cash)
    {
        var first = new MaintenanceRepository(settings, actor);
        var second = new MaintenanceRepository(settings, actor);
        const FinancialAccountKind vf = FinancialAccountKind.VodafoneCash;
        using var till = new SqlConnection(settings.GetConnectionString());
        till.Open();
        foreach (var table in new[] { "MaintenanceOrders", "MaintenanceOrderParts", "MaintenancePayments", "MaintenanceActivities" })
            foreach (var permission in new[] { "INSERT", "UPDATE", "DELETE" })
                Check(Scalar<int>(till, $"SELECT COALESCE(HAS_PERMS_BY_NAME('dbo.{table}','OBJECT','{permission}'),0);") == 0, $"Till cannot {permission} {table}");
        foreach (var procedure in new[] { "WriteMaintenanceMoney", "ReleaseMaintenanceLine", "LogMaintenancePosting", "ReopenMaintenanceTicket" })
            Check(Scalar<int>(till, $"SELECT COALESCE(HAS_PERMS_BY_NAME('dbo.{procedure}','OBJECT','EXECUTE'),0);") == 0, $"Till cannot call {procedure}");
        Execute(db, "CREATE USER MaintenanceTestSupervisor WITHOUT LOGIN; ALTER ROLE ProjectZMaintenanceSupervisor ADD MEMBER MaintenanceTestSupervisor;");
        Execute(db, "UPDATE dbo.FinancialAccounts SET IsDefault=0 WHERE AccountKind IN (1,2);");
        int Wallet(int kind, string alias, int preferred) => Scalar<int>(db, $"""
            INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,IsDefault,CurrentBalance)
            OUTPUT inserted.Id VALUES({kind},@alias,@alias,{preferred},1000);
            """, ("@alias", alias));
        var a = Wallet(1, "MNT-A", 1); var b = Wallet(1, "MNT-B", 0); var ip = Wallet(2, "MNT-IP", 1);
        decimal Balance(int id) => Scalar<decimal>(db, $"SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={id};");
        var initialCash = Balance(cash);
        MaintenanceOrderDesignItem Open(decimal estimate, decimal c = 0, decimal v = 0, string serial = "", bool pending = false)
        {
            var draft = new MaintenanceOrderDesignItem { CustomerName = "عميل الصيانة", IsWalkInCustomer = true,
                EstimatedCost = estimate, IsEstimatePendingInspection = pending, ReceivedAt = new DateTime(1999,1,1),
                SerialOrImei = serial, DeviceModel = "اختبار", DepositCashPaid = c, DepositVodafonePaid = v };
            var saved = first.CreateMaintenanceOrder(draft);
            var order = first.LoadTicket(saved.OrderId);
            Check(order.DatabaseRowVersion!.SequenceEqual(saved.RowVersion!) && order.ReceivedAt.Year != 1999 && order.OrderNumber == saved.OrderNumber,
                "Intake returns the committed number, server time and version");
            return order;
        }
        static MaintenanceTicketKey Key(MaintenanceOrderDesignItem order) => MaintenanceTicketKey.Open(order);
        static MaintenanceDelivery Delivery(MaintenanceOrderDesignItem order, decimal total, decimal c = 0, decimal v = 0, decimal i = 0, bool refund = true) =>
            new(Key(order), total - order.PartsTotal, 0, total, c, v, i, refund, 14, "ضمان الاختبار");
        void Reopen(MaintenanceOrderDesignItem order) => Execute(db, """
            EXECUTE AS USER=N'MaintenanceTestSupervisor';
            BEGIN TRY EXEC dbo.ReopenMaintenanceTicket @id,@rv,@actor,N'مراجعة مع العميل'; END TRY
            BEGIN CATCH REVERT; THROW; END CATCH;
            REVERT;
            """, ("@id", order.Id), ("@rv", order.DatabaseRowVersion!), ("@actor", actor));
        void Refused(Action request, int number, string why)
        {
            var before = Footprint(db);
            try { request(); }
            catch (MaintenanceDataException ex) when (ex.InnerException is SqlException sql && sql.Number == number)
            { Check(before == Footprint(db), why + ": no persistent effect"); return; }
            throw new InvalidOperationException("Maintenance refusal expected: " + why);
        }
        static bool[] Race(Action left, Action right)
        {
            using var gate = new Barrier(2);
            Task<bool> Send(Action action) => Task.Run(() =>
            {
                if (!gate.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Second till did not reach the gate");
                try { action(); return true; }
                catch (MaintenanceDataException ex) when (ex.InnerException is SqlException { Number: 50241 }) { return false; }
            });
            return Task.WhenAll(Send(left), Send(right)).GetAwaiter().GetResult();
        }

        var order = Open(300, 20.11m, 79.89m, "MNT-EXACT");
        Refused(() => Open(300, 2, serial: "MNT-EXACT"), 50242, "Repeated open device");
        Refused(() => Open(9, 10), 50245, "Intake deposit above estimate");
        var stale = Key(second.LoadTicket(order.Id));
        first.CollectDeposit(Key(order), vf, 20);
        Refused(() => second.Cancel(stale, 100, true, "نافذة قديمة"), 50241, "Cancellation opened before collection");
        Execute(db, $"UPDATE dbo.FinancialAccounts SET IsDefault=0 WHERE Id={a}; UPDATE dbo.FinancialAccounts SET IsDefault=1 WHERE Id={b};");
        order = first.LoadTicket(order.Id);
        Refused(() => first.Deliver(Delivery(order,150,31)), 50245, "Collection above actual remaining 30");
        Refused(() => first.Deliver(Delivery(order,150) with { ExpectedTotal = 151 }), 50243, "Client-supplied header price disagrees with lines and labor");
        var leftDelivery = Delivery(order,150,v:20,i:10);
        var rightDelivery = Delivery(second.LoadTicket(order.Id),150,v:20,i:10);
        Check(Race(() => first.Deliver(leftDelivery), () => second.Deliver(rightDelivery)).Count(w => w) == 1, "Two delivery confirmations, one commit");
        order = first.LoadTicket(order.Id);
        Check(order.IsDelivered && order.TotalCollectedAll == 150 && order.EstimatedRemaining == 0 && order.PaymentHistory.Count == 5
              && Balance(cash) == initialCash+20.11m && Balance(a) == 1099.89m && Balance(b) == 1020 && Balance(ip) == 1010,
            "Split intake, later deposit and final tenders credited once to the accounts actually used");
        Check(Scalar<int>(db, $"""
            SELECT COUNT(*) FROM dbo.MaintenancePayments p JOIN dbo.MaintenanceOrders o ON o.Id=p.MaintenanceOrderId
            JOIN dbo.FinancialTransactions l ON l.SourceEntityType=N'MaintenanceOrder' AND l.SourceEntityId=o.Id AND l.FinancialAccountId=p.FinancialAccountId
            JOIN dbo.MaintenanceActivities act ON act.MaintenanceOrderId=o.Id AND act.OccurredAtUtc=o.DeliveredAtUtc
            WHERE o.Id={order.Id} AND p.PaymentStage=2 AND p.PaidAtUtc=o.DeliveredAtUtc AND p.PaidAtUtc=l.OccurredAtUtc AND l.Amount=p.Amount;
            """) == 2, "Delivery, both receipts/ledger legs and activity share the server timestamp");
        Refused(() => second.Deliver(rightDelivery), 50241, "Delivery replay");
        Refused(() => first.CollectDeposit(Key(order),vf,1),50242,"Fresh but closed ticket");
        Refused(() => first.SaveWorkshop(order,MaintenanceOrderStatus.Diagnosing,MaintenancePriority.Normal,"",null,"","",0,0),50242,"Workshop cannot reopen");
        Refused(() => first.Reopen(Key(order),"طلب الموظف"),229,"Shared cashier SQL login cannot approve reopen even with app admin actor ID");
        Check(!order.CanReopen, "Cashier never receives a reopen affordance");
        var printedKey=Key(order);
        first.AppendMaintenanceActivity(printedKey,2);
        Check(first.LoadTicket(order.Id).DatabaseRowVersion!.SequenceEqual(printedKey.Version!),"Printing appends only an activity, not a ticket mutation");
        Reopen(order);
        Refused(() => first.AppendMaintenanceActivity(printedKey,2),50241,"Warranty preview held open through reopen");
        Refused(() => first.AppendMaintenanceActivity(Key(first.LoadTicket(order.Id)),2),50242,"No warranty receipt for an open order");
        Check(Scalar<int>(db, $"""
            SELECT COUNT(*) FROM dbo.AuditLog WHERE Module=N'Maintenance' AND ActionName=N'Reopen' AND EntityId=N'{order.Id}'
              AND JSON_VALUE(OldValuesJson,'$.OrderStatus')='5' AND JSON_VALUE(OldValuesJson,'$.DeliveredAtUtc') IS NOT NULL
              AND JSON_VALUE(OldValuesJson,'$.WarrantyDays')='14' AND JSON_VALUE(OldValuesJson,'$.Payments[4].Amount') IS NOT NULL
              AND JSON_VALUE(NewValuesJson,'$.Details')=N'مراجعة مع العميل';
            """) == 1, "Authorized reopen preserves the original delivery and receipts, reason and actor");
        order = first.LoadTicket(order.Id);
        first.Deliver(Delivery(order,60,refund:false));
        order = first.LoadTicket(order.Id);
        Check(order.PaymentHistory.Count == 5 && order.OverpaidDepositAmount == 90 && order.TotalCollectedAll == 150, "Explicit retain leaves 90 visible and pays nothing");
        Reopen(order); order = first.LoadTicket(order.Id);
        Execute(db,$"UPDATE dbo.FinancialAccounts SET IsActive=0 WHERE Id={a};");
        Refused(() => first.Deliver(Delivery(order,60)),50246,"Stopped original wallet cannot redirect a split refund into the new default");
        Execute(db,$"UPDATE dbo.FinancialAccounts SET IsActive=1,CurrentBalance=1 WHERE Id={a};");
        Refused(() => first.Deliver(Delivery(order,60)),50246,"Insufficient original balance rolls back the preceding cash leg");
        Execute(db,$"UPDATE dbo.FinancialAccounts SET CurrentBalance=1099.89 WHERE Id={a};");
        first.Deliver(Delivery(order,60)); order = first.LoadTicket(order.Id);
        Check(order.TotalCollectedAll == 60 && order.NetDepositRetained == 30 && Balance(cash)==initialCash && Balance(a)==1030
              && Balance(b)==1020 && Balance(ip)==1010 && order.PaymentHistory.Count(p=>p.IsRefund && p.RefundOfPaymentId.HasValue)==2,
            "Re-delivery counts earlier finals: 90 back, 20.11 cash + 69.89 original A, nothing from B or InstaPay");
        Reopen(order); order=first.LoadTicket(order.Id);
        Refused(() => first.Cancel(Key(order),60.01m,false,"طلب"),50245,"Cannot refund a cent more than remaining receipts");
        first.Cancel(Key(order),60,false,"رد الباقي"); order=first.LoadTicket(order.Id);
        Check(order.IsCancelled && order.TotalCollectedAll==0 && order.NetDepositRetained==0 && Balance(a)==1000 && Balance(b)==1000 && Balance(ip)==1000 && Balance(cash)==initialCash,
            "Cancellation can refund retained deposits and previous finals exactly once");
        Check(Scalar<int>(db,$"""
            SELECT COUNT(*) FROM dbo.MaintenancePayments r JOIN dbo.MaintenancePayments p ON p.Id=r.RefundOfPaymentId
            WHERE r.MaintenanceOrderId={order.Id} AND (r.FinancialAccountId<>p.FinancialAccountId OR r.MaintenanceOrderId<>p.MaintenanceOrderId);
            """)==0, "All partial refunds remain on their source account and ticket");

        var credit = Open(100);
        var x=Key(first.LoadTicket(credit.Id)); var y=Key(second.LoadTicket(credit.Id));
        Check(Race(() => first.CollectDeposit(x,vf,70.17m), () => second.CollectDeposit(y,vf,70.17m)).Count(w=>w)==1,
            "Two tills collecting on one version cannot spend the remaining debt twice");
        credit=first.LoadTicket(credit.Id);
        Check(credit.EstimatedRemaining==29.83m && Scalar<decimal>(db,$"SELECT RemainingDue FROM dbo.vwMaintenanceOrderSummary WHERE MaintenanceOrderId={credit.Id};")==29.83m,
            "Report and open ticket agree to the cent");
        Refused(() => first.CollectDeposit(Key(credit),vf,30),50245,"Even a fresh version cannot over-collect");
        var pending=Open(0,v:10,pending:true);
        first.CollectDeposit(Key(pending),vf,15); pending=first.LoadTicket(pending.Id);
        first.Deliver(Delivery(pending,100,5)); pending=first.LoadTicket(pending.Id);
        Check(pending.IsDelivered && pending.EstimatedRemaining==70 && pending.TotalCollectedAll==30, "Pending estimate accepts deposits; partial delivery preserves the actual unpaid balance");

        var spare=Scalar<int>(db,"INSERT dbo.MaintenanceSpareParts(Name,Quantity,CostPrice,SellPrice) OUTPUT inserted.Id VALUES(N'اختبار القطعة',5,13.23,40.25);");
        int Qty() => Scalar<int>(db,$"SELECT Quantity FROM dbo.MaintenanceSpareParts WHERE Id={spare};");
        var stock=Open(200,v:25.11m);
        var notYetIssued=Key(stock);
        first.IssuePart(Key(stock),spare,2,10,0); stock=first.LoadTicket(stock.Id);
        Check(stock.CurrentEffectiveCost==90.50m && stock.UsedParts.Single().UnitCost==13.23m && Qty()==3, "Issue uses catalog prices and commits the line, quantity and price together");
        Refused(() => second.Cancel(notYetIssued,25,true,"قديم"),50241,"Old cancellation cannot return new lines");
        Refused(() => first.RemovePart(Key(stock),stock.UsedParts.Single().LineId,10,11),50243,"Invalid remaining discount rolls back even a removed stock line");
        first.AddManualPart(Key(stock),"اختبار يدوي",1,3,12.34m,10,0); stock=first.LoadTicket(stock.Id);
        Check(stock.CurrentEffectiveCost==102.84m && Qty()==3, "Manual charge affects price, not catalog inventory");
        Execute(db,$"UPDATE dbo.MaintenanceSpareParts SET IsActive=0 WHERE Id={spare};");
        Execute(db,"CREATE TRIGGER dbo.MntTestFailAudit ON dbo.AuditLog AFTER INSERT AS THROW 50298,N'اختبار تراجع التسوية',1;");
        try { Refused(() => first.Cancel(Key(stock),25.11m,true,"إلغاء وإرجاع"),50298,"Final audit insert fails after stock, order, split settlement and activity"); }
        finally { Execute(db,"DROP TRIGGER dbo.MntTestFailAudit;"); }
        var cb=Balance(b); var c0=Balance(cash);
        var delivery=Delivery(stock,102.84m,77.73m);
        var cancelKey=Key(second.LoadTicket(stock.Id));
        var race=Race(() => first.Deliver(delivery), () => second.Cancel(cancelKey,25.11m,true,"إلغاء"));
        stock=first.LoadTicket(stock.Id);
        Check(race.Count(w=>w)==1 && (race[0]
            ? stock.IsDelivered && stock.UsedParts.Count==2 && Qty()==3 && Balance(b)==cb && Balance(cash)==c0+77.73m
            : stock.IsCancelled && stock.UsedParts.Count==0 && Qty()==5 && Balance(b)==cb-25.11m && Balance(cash)==c0),
            "Concurrent delivery/cancel commits only the winner's status, money and stock");
        if(stock.IsDelivered) { Reopen(stock); first.Cancel(Key(first.LoadTicket(stock.Id)),102.84m,true,"إرجاع كامل"); }
        Check(Qty()==5 && first.LoadTicket(stock.Id).UsedParts.Count==0 && Scalar<int>(db,$"SELECT COUNT(*) FROM dbo.MaintenanceSparePartMovements WHERE MaintenanceOrderId={stock.Id} AND MovementKind=4;")==1,
            "Cancellation returns even a stopped catalog part exactly once, never returns manual stock");

        // Unattributed legacy cash refunds or mismatched account ledgers need reconciliation, not a guessed payout.
        var legacy=Open(100,v:50);
        Execute(db,$"""
            INSERT dbo.MaintenancePayments(MaintenanceOrderId,FinancialAccountId,PaymentStage,PaymentKind,Amount,PaidAtUtc,ActorUserId)
            VALUES({legacy.Id},{b},1,2,5,SYSUTCDATETIME(),{actor});
            """);
        Refused(() => first.Cancel(Key(legacy),45,true,"قديم"),50244,"Legacy payment/ledger mismatch");
        Execute(db,$"""
            UPDATE dbo.FinancialAccounts SET CurrentBalance=CurrentBalance-5 WHERE Id={b};
            INSERT dbo.FinancialTransactions(FinancialAccountId,IsIncoming,Category,Amount,BalanceAfter,ActorUserId,SourceEntityType,SourceEntityId)
            SELECT Id,0,N'رد دفعة صيانة',5,CurrentBalance,{actor},N'MaintenanceOrder',{legacy.Id} FROM dbo.FinancialAccounts WHERE Id={b};
            """);
        Refused(() => first.Cancel(Key(legacy),45,true,"قديم"),50244,"Even a balanced legacy refund with no source receipt cannot be paid twice");
        var noVersion=Footprint(db);
        try { Execute(till,$"EXEC dbo.SettleMaintenanceTicket {credit.Id},NULL,{actor},1,@Cash=1;"); throw new InvalidOperationException("Missing version accepted"); }
        catch (SqlException ex) when (ex.Number==50241) { Check(Footprint(db)==noVersion,"SQL itself refuses a missing version"); }
        MaintenanceReportTests.Run(db,settings,actor,order.Id,credit.Id,pending.Id,legacy.Id,stock.Id,spare);
    }
}
