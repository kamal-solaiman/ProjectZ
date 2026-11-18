using System.Collections.ObjectModel;
using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;

internal static class SaleSettlementTests
{
    // Run only after the LocalDB guard / limited-login setup in Program.cs.
    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings, int actor, int category, int walletA, int cash)
    {
        var repo = new SalesRepository(settings, actor);
        var customer = repo.CreateCustomer("عميل اختبار التسوية", "01040001234", "", "", "", PriceTier.Retail);
        Execute(db, "UPDATE dbo.FinancialAccounts SET CurrentBalance=10000 WHERE IsActive=1;");
        int Product(string name, decimal price) => Scalar<int>(db, """
            INSERT dbo.Products(CategoryId,Name,UnitName,CostPrice,RetailPrice)
            OUTPUT inserted.Id VALUES(@category,@name,N'قطعة',17,@price);
            """, ("@category", category), ("@name", name), ("@price", price));
        var qtyProduct = Product("مخزون كمي", 100);
        var unitProduct = Product("وحدات مسجلة", 100);
        var cheaper = Product("بديل", 50);
        Execute(db, $"""
            INSERT dbo.StockMovements(ProductId,MovementType,QuantityDelta,UnitCost,ActorUserId) VALUES
                ({qtyProduct},N'Supply',100,17,{actor}),({cheaper},N'Supply',10,17,{actor});
            INSERT dbo.InventoryUnits(ProductId,SerialNumber,PurchaseCost)
                VALUES({unitProduct},N'SALE-A',11),({unitProduct},N'SALE-B',12),({unitProduct},N'SALE-C',13);
            INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId)
                SELECT ProductId,Id,N'Supply',1,PurchaseCost,{actor} FROM dbo.InventoryUnits WHERE ProductId={unitProduct};
            """);
        SalesInvoiceDesignItem Read(int id)
        {
            var invoices = new ObservableCollection<SalesInvoiceDesignItem>();
            repo.LoadInvoicesInto(invoices);
            return invoices.Single(i => i.Id == id);
        }
        SalesInvoiceDesignItem Sell(SaleItemRequest[] lines, decimal discount, decimal c, decimal w, bool walkIn = false) =>
            Read(repo.CompleteSale(new(walkIn ? null : customer.Id, walkIn, PriceTier.Retail, discount, lines, c, w, 0m, "")).InvoiceId);
        SaleItemRequest Qty(int n) => new(qtyProduct, n, PriceTier.Retail, 100m);
        SaleItemRequest Units(int n) => new(unitProduct, n, PriceTier.Retail, 100m);
        decimal Value(string query) => Scalar<decimal>(db, query);
        decimal NetPaid(int id, int account) => Value($"SELECT COALESCE(SUM(CASE WHEN PaymentKind=1 THEN Amount ELSE -Amount END),0) FROM dbo.SalesPayments WHERE SalesInvoiceId={id} AND FinancialAccountId={account};");
        decimal Stock(int product) => Value($"SELECT QuantityOnHand FROM dbo.vwProductStock WHERE ProductId={product};");
        void Totals(int id, SalesInvoiceStatus status, decimal net, decimal paid, decimal due)
        {
            var row = Read(id);
            Check(row.Status == status && row.EffectiveNetSales == net && row.NetCollected == paid && row.RemainingAmount == due,
                $"Invoice {id}: UI status/net/collected/due = {status}/{net}/{paid}/{due}");
            Check(Scalar<int>(db, """
                SELECT COUNT(*) FROM dbo.vwSalesInvoiceSummary WHERE SalesInvoiceId=@id AND InvoiceStatus=@status
                AND NetSales=@net AND NetCollected=@paid AND RemainingAmount=@due AND NetProfit=@profit;
                """, ("@id", id), ("@status", (byte)status), ("@net", net), ("@paid", paid), ("@due", due), ("@profit", row.NetProfit)) == 1,
                $"Invoice {id}: SQL reporting agrees through the last cent");
        }
        static void Rejected(Action action, int expectedSql)
        {
            try { action(); }
            catch (SalesDataException e) when (e.InnerException is SqlException sql && sql.Number == expectedSql) { return; }
            throw new InvalidOperationException($"Expected the isolated operation to be refused with SQL {expectedSql}.");
        }
        // Include counts, quantities, versions, balances and sequences: rejection must leave no business effects.
        string Footprint() => Scalar<string>(db, """
            SELECT CONCAT((SELECT COUNT(*) FROM dbo.SalesPayments),':',(SELECT COUNT(*) FROM dbo.FinancialTransactions),':',
              (SELECT COUNT(*) FROM dbo.StockMovements),':',(SELECT COUNT(*) FROM dbo.SalesInvoiceLines),':',
              (SELECT COUNT(*) FROM dbo.AuditLog),':',(SELECT COUNT(*) FROM dbo.ReturnExchanges),':',
              (SELECT COUNT(*) FROM dbo.SalesInvoiceActivities),':',(SELECT SUM(LastValue) FROM dbo.DocumentSequences),':',
              (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(Id,CurrentBalance)) FROM dbo.FinancialAccounts),':',
              (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(Id,RowVersion)) FROM dbo.SalesInvoices),':',
              (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(Id,ReturnedQuantity)) FROM dbo.SalesInvoiceLines),':',
              (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(Id,UnitStatus)) FROM dbo.InventoryUnits),':',
              (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(SalesInvoiceLineId,InventoryUnitId,AllocationStatus)) FROM dbo.SalesInvoiceLineUnits));
            """);
        void NoEffect(Action action, int error, string message)
        {
            var before = Footprint();
            Rejected(action, error);
            Check(before == Footprint(), message);
        }
        static SalesRepository.ReturnLineRequest Back(SalesInvoiceDesignItem i, int lineNo, int good, int damaged = 0) =>
            new(i.Items.Single(l => l.LineNumber == lineNo).DatabaseLineId, good, damaged);
        SalesRepository.AdjustmentReceipt Return(SalesInvoiceDesignItem i, params SalesRepository.ReturnLineRequest[] lines) =>
            repo.CreateReturn(new(i, "اختبار المرتجع", lines));

        NoEffect(() => Sell([new(qtyProduct, 1, PriceTier.Retail, 1m)], 0, 1, 0), 50105, "No sale on a stale cart price");
        var credit = Sell([Qty(10)], 0, 0, 100);
        var sel = Back(credit, 1, 5);
        Return(credit, sel);
        Totals(credit.Id, SalesInvoiceStatus.PartiallyReturned, 500, 100, 400);
        Check(Stock(qtyProduct) == 95 && NetPaid(credit.Id, walletA) == 100 && Value($"SELECT COUNT(*) FROM dbo.SalesPayments WHERE SalesInvoiceId={credit.Id} AND PaymentKind=2;") == 0,
            "1,000 sale / 100 collected / 500 return: restore five pieces, reduce debt by 500, pay out nothing");
        NoEffect(() => Return(credit, sel), 50110, "Repeating an eligible quantity with a stale version writes nothing");
        NoEffect(() => repo.CancelSale(credit), 50110, "A stale till cannot cancel after a return");
        var current = Read(credit.Id);
        NoEffect(() => Return(current, Back(current, 1, 6)), 50116, "Cannot return more than the five remaining pieces");
        NoEffect(() => repo.CollectPayment(current, 400.01m, 0, 0, ""), 50112, "Server rejects collection one cent above actual due");
        // Both tills actually submit the same valid pre-write version via separate SqlClient connections.
        using var gate = new Barrier(2);
        bool PayAtTill()
        {
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Till synchronization failed.");
            try { repo.CollectPayment(current, 250, 0, 0, "دفعة اختبار"); return true; }
            catch (SalesDataException e) when (e.InnerException is SqlException { Number: 50110 }) { return false; }
        }
        var tills = new[] { Task.Run(PayAtTill), Task.Run(PayAtTill) };
        Task.WaitAll(tills);
        Check(tills.Count(t => t.Result) == 1, "Exactly one of two simultaneous collections commits");
        Totals(credit.Id, SalesInvoiceStatus.PartiallyReturned, 500, 350, 150);
        repo.CollectPayment(Read(credit.Id), 150, 0, 0, "");
        Totals(credit.Id, SalesInvoiceStatus.PartiallyReturned, 500, 500, 0);

        // 5-cent discount across two original lines; first line's 299.96 is split 99.99/99.98/99.99.
        var split = Sell([Units(3), Qty(1)], .05m, 40, 160);
        Check(split.Items[0].ReturnValue(1) == 99.99m && split.Items[0].SavedNetAmount == 299.96m
              && split.Items[0].UnitCost == 12m && split.Items[1].SavedNetAmount == 99.99m, "Frozen price/cost/discount shares in UI");
        Execute(db, $"UPDATE dbo.FinancialAccounts SET IsDefault=0 WHERE Id={walletA};");
        var walletB = Scalar<int>(db, "INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,IsDefault,CurrentBalance) OUTPUT inserted.Id VALUES(1,N'محفظة أخرى',N'SALE-TEST-B',1,5000);");
        repo.CollectPayment(split, 0, 199.95m, 0, "");
        split = Read(split.Id);
        var first = Back(split, 1, 1);
        var preview = split.DescribeReduction(split.Items[0].ReturnValue(1));
        Check(preview.Contains($"#{walletA})") && !preview.Contains($"#{walletB})") && preview.Contains("59.99"),
            "Till previews the original wallet and its cap, not the new default");
        NoEffect(() => Return(split, sel), 50116, "Cannot return another invoice's line");
        Execute(db, $"UPDATE dbo.FinancialAccounts SET IsActive=0 WHERE Id={walletA};");
        NoEffect(() => Return(split, first), 50103, "Stopped original wallet rolls back the cash leg, stock and document too");
        Execute(db, $"UPDATE dbo.FinancialAccounts SET IsActive=1,CurrentBalance=1 WHERE Id={walletA};");
        NoEffect(() => Return(split, first), 50103, "No substitution of the default wallet or cash when the original balance is short");
        Execute(db, $"UPDATE dbo.FinancialAccounts SET CurrentBalance=10000 WHERE Id={walletA};");
        var r1 = Return(split, first);
        Totals(split.Id, SalesInvoiceStatus.PartiallyReturned, 299.96m, 299.96m, 0);
        Check(NetPaid(split.Id, cash) == 0 && NetPaid(split.Id, walletA) == 100.01m && NetPaid(split.Id, walletB) == 199.95m,
            "Partial refund consumes only 40 cash and 59.99 from the exact original wallet");
        Check(Value($"SELECT SUM(Amount) FROM dbo.FinancialTransactions WHERE IsIncoming=0 AND Note=N'{r1.Number}' AND SourceEntityType=N'ReturnExchange';") == 99.99m,
            "Refund ledger matches the posted return");
        split = Read(split.Id);
        Check(split.Items[0].ReturnValue(1) == 99.98m, "Cent rounding depends on cumulative returned quantity");
        var r2 = Return(split, Back(split, 1, 2), Back(split, 2, 0, 1));
        Totals(split.Id, SalesInvoiceStatus.FullyReturned, 0, 0, 0);
        Check(new[] { cash, walletA, walletB }.All(a => NetPaid(split.Id, a) == 0), "Full return refunds exactly the signed amounts left per account");
        var history = repo.LoadReturnExchanges().Single(r => r.OperationNumber == r2.Number);
        Check(history.GoodUnits == 2 && history.DamagedUnits == 1 && history.ReturnedValue == 299.96m && Stock(qtyProduct) == 94
              && Value($"SELECT QuantityOnlyDamaged FROM dbo.vwProductStock WHERE ProductId={qtyProduct};") == 1,
            "Joining two tagged units never multiplies line value; damaged quantity is not sellable");
        NoEffect(() => repo.CollectPayment(Read(split.Id), 1, 0, 0, ""), 50111, "No payment on a fully returned invoice");

        var exchange = Sell([Qty(10)], 0, 0, 600);
        SalesRepository.AdjustmentReceipt Swap(SalesInvoiceDesignItem i, int line, int qty, SaleItemRequest replacement, decimal collect) =>
            repo.CreateExchange(new(i, "استبدال", [Back(i, line, qty)], [replacement]), new(collect, 0, 0));
        NoEffect(() => Swap(exchange, 1, 5, new(cheaper, 1, PriceTier.Retail, 45), 0), 50105, "Changed replacement price rolls back stock and return");
        Swap(exchange, 1, 5, new(cheaper, 1, PriceTier.Retail, 50), 0);
        Totals(exchange.Id, SalesInvoiceStatus.Exchanged, 550, 550, 0);
        Check(NetPaid(exchange.Id, walletB) == 550 && NetPaid(exchange.Id, cash) == 0, "Cheaper exchange reduces debt by 400 and refunds only 50 to the wallet");
        NoEffect(() => Swap(exchange, 1, 5, Qty(3), 0), 50110, "Stale exchange leaves no money or stock entries");
        var upgraded = Swap(Read(exchange.Id), 2, 1, Qty(1), 20);
        Totals(exchange.Id, SalesInvoiceStatus.Exchanged, 600, 570, 30);
        Check(repo.LoadReturnExchanges().Single(r => r.OperationNumber == upgraded.Number).ReplacementValue == 100 &&
              Read(exchange.Id).ActivityLog.Last().Amount == 100, "Replacement appears once in the document and activity");
        repo.CollectPayment(Read(exchange.Id), 30, 0, 0, "");
        Totals(exchange.Id, SalesInvoiceStatus.Exchanged, 600, 600, 0);

        var discounted = Sell([Qty(2)], 20, 0, 150);
        Swap(discounted, 1, 1, new(cheaper, 1, PriceTier.Retail, 50), 0);
        Totals(discounted.Id, SalesInvoiceStatus.Exchanged, 140, 140, 0);
        var adjusted = Read(discounted.Id);
        Check(adjusted.Items[0].SavedNetAmount == 180 && adjusted.Items[0].ReturnValue(1) == 90 &&
              adjusted.Items[1].SavedNetAmount == 50 && adjusted.RefundedAmount == 10,
              "Exchange leaves original discount shares fixed; the 50 replacement does not share the original discount");

        var late = Sell([Qty(2)], 20, 0, 180);
        Return(late, Back(late, 1, 1));
        // A legacy discounted return has no saved net value. Never collect a debt created merely
        // by applying today's allocation to what the old program treated as a gross refund.
        Execute(db, $"UPDATE dbo.ReturnExchangeLines SET NetAmount=NULL WHERE SalesInvoiceLineId={late.Items[0].DatabaseLineId};");
        NoEffect(() => Return(Read(late.Id), Back(late, 1, 1)), 50115, "Legacy discounted partial return is held for reconciliation");
        NoEffect(() => repo.CollectPayment(Read(late.Id), 1, 0, 0, ""), 50115, "No automatic collection of a revalued legacy balance");

        var stockBefore = Stock(qtyProduct);
        var cancel = Sell([Units(2), Qty(3)], 0, 0, 500, walkIn: true);
        NoEffect(() => Swap(cancel, 1, 1, Qty(2), 0), 50118, "Walk-in cannot leave an upgrade on credit");
        // Simulate an older same-account refund with no return document. Cancel must not pay it twice.
        Execute(db, $"""
            INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,ActorUserId)
                VALUES({cancel.Id},{walletB},2,75,SYSUTCDATETIME(),{actor});
            UPDATE dbo.FinancialAccounts SET CurrentBalance=CurrentBalance-75 WHERE Id={walletB};
            """);
        var walletBefore = Value($"SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={walletB};");
        // Remove only the new optional link to exercise movement-only invoices issued before the upgrade.
        Execute(db, $"UPDATE dbo.StockMovements SET SalesInvoiceLineId=NULL WHERE SourceEntityType=N'SalesInvoice' AND SourceEntityId={cancel.Id} AND InventoryUnitId IS NULL;");
        repo.CancelSale(cancel);
        Totals(cancel.Id, SalesInvoiceStatus.Cancelled, 0, 0, 0);
        Check(Stock(qtyProduct) == stockBefore && Stock(unitProduct) == 3 && NetPaid(cancel.Id, walletB) == 0 &&
              Value($"SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={walletB};") == walletBefore - 425,
              "Cancellation restores quantity and serialized stock and refunds only the 425 still held");
        NoEffect(() => repo.CancelSale(cancel), 50110, "Repeating cancellation does not refund or restock twice");
        NoEffect(() => repo.CancelSale(Read(cancel.Id)), 50113, "Even a fresh version cannot cancel a closed invoice");
        NoEffect(() => repo.CollectPayment(Read(cancel.Id), 1, 0, 0, ""), 50111, "No collection on a cancelled invoice");
        var resale = Sell([Units(3)], 0, 300, 0);
        Check(resale.Items[0].UnitRegistrationIds.Count == 3, "All released allocations can be sold again");
        var mixed = Return(resale, Back(resale, 1, 2, 1));
        var mixedHistory = repo.LoadReturnExchanges().Single(r => r.OperationNumber == mixed.Number);
        Check(mixedHistory.GoodUnits == 2 && mixedHistory.DamagedUnits == 1 && mixedHistory.ReturnedValue == 300 && Stock(unitProduct) == 2,
            "Mixed conditions on one serialized line keep distinct document quantities, values and sellable stock");
        // A legacy cross-channel mismatch must be investigated, not reattributed silently.
        var mismatch = Sell([Qty(1)], 0, 100, 0);
        Execute(db, $"INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,ActorUserId) VALUES({mismatch.Id},{walletA},2,1,SYSUTCDATETIME(),{actor});");
        NoEffect(() => repo.CancelSale(mismatch), 50101, "Legacy negative account net blocks settlement without changing history");
        using var limited = new SqlConnection(settings.GetConnectionString());
        limited.Open();
        Check(Scalar<int>(limited, "SELECT HAS_PERMS_BY_NAME('dbo.SalesPayments','OBJECT','INSERT') + HAS_PERMS_BY_NAME('dbo.SalesInvoices','OBJECT','UPDATE') + COALESCE(HAS_PERMS_BY_NAME('dbo.PostSaleMoney','OBJECT','EXECUTE'),0);") == 0,
            "The till can execute guarded posting but cannot insert payments, reset status, or execute the private helper directly");
    }
}
