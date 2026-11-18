using System.Collections.ObjectModel;
using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;

internal static class PurchaseSettlementTests
{
    // Compare complete ordered rows, including links, versions, sequences and per-account balances, not just counts.
    internal static string Footprint(SqlConnection db)
    {
        string Rows(string table, string order = "Id") => Scalar<string>(db, $"SELECT (SELECT * FROM dbo.{table} ORDER BY {order} FOR JSON PATH) ;");
        return string.Join('|', new[] { "Suppliers", "PurchaseInvoices", "PurchaseInvoiceLines", "PurchaseReturns", "PurchaseReturnLines", "SupplierCreditNotes",
            "SupplierCreditApplications", "SupplierCreditReceipts", "InventoryUnits", "Products", "StockMovements", "FinancialAccounts", "FinancialTransactions",
            "PurchasePayments", "PurchaseInvoiceActivities", "AuditLog" }.Select(t => Rows(t)))
            + Rows("PurchaseInvoiceLineUnits", "PurchaseInvoiceLineId,InventoryUnitId") + Rows("PurchaseReturnUnits", "InventoryUnitId") + Rows("DocumentSequences", "DocumentType,SequenceYear");
    }

    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings, int actor, int product, int serial)
    {
        var repo = new PurchaseRepository(settings, actor);
        var otherTill = new PurchaseRepository(settings, actor);
        using var client = new SqlConnection(settings.GetConnectionString()); client.Open();
        foreach (var table in new[] { "PurchaseInvoices", "PurchaseInvoiceLines", "PurchaseInvoiceLineUnits", "PurchaseReturns", "PurchaseReturnLines", "PurchaseReturnUnits",
            "PurchasePayments", "PurchaseInvoiceActivities", "SupplierCreditNotes", "SupplierCreditApplications", "SupplierCreditReceipts" })
            foreach (var permission in new[] { "INSERT", "UPDATE", "DELETE" })
                Check(Scalar<int>(client, $"SELECT HAS_PERMS_BY_NAME('dbo.{table}','OBJECT','{permission}')") == 0, $"Till cannot {permission} {table}");
        foreach (var proc in new[] { "ClaimSupplierCredit", "WritePurchasePayment", "WritePurchaseLines", "WithdrawPurchaseUnits", "FinishPurchasePosting" })
            Check(Scalar<int>(client, $"SELECT COALESCE(HAS_PERMS_BY_NAME('dbo.{proc}','OBJECT','EXECUTE'),0)") == 0, $"Till cannot call {proc}");

        var supplier = repo.CreateSupplier("اختبار إشعار المورد", "", "", "", "", "", "", "", "");
        var outside = repo.CreateSupplier("اختبار مورد مختلف", "", "", "", "", "", "", "", "");
        Execute(db, "UPDATE dbo.FinancialAccounts SET CurrentBalance=5000 WHERE AccountKind=0 OR (AccountKind=1 AND IsDefault=1);");
        int receiving = Scalar<int>(db,"INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,CurrentBalance) OUTPUT inserted.Id VALUES(1,N'حساب قبض غير افتراضي',N'01070123123',20);");
        List<PurchaseInvoiceDesignItem> Load()
        {
            var invoices = new ObservableCollection<PurchaseInvoiceDesignItem>();
            repo.LoadInvoicesInto(invoices); return invoices.ToList();
        }
        PurchaseInvoiceDesignItem Read(int id) => Load().Single(i => i.Id == id);
        PurchaseInvoiceDesignItem Create(int owner, decimal discount, decimal cash, decimal wallet, params PurchaseLineRequest[] items) =>
            Read(repo.CreateInvoice(new(owner, "SUP-P25", DateTime.Now, discount, items, cash, wallet, 0, "اختبار")).InvoiceId);
        PurchaseInvoiceEdit Edit(PurchaseInvoiceDesignItem i, decimal discount) => new(i.Id, i.DatabaseRowVersion, i.SupplierId, "مرجع معدل", i.IssuedAt, "تحديث دون مال", discount,
            i.Items.Select(l => new PurchaseLineRequest(l.ProductId, l.Quantity, l.UnitPurchaseCost, l.DatabaseLineId)).ToList());
        int CashRows() => Scalar<int>(db,"SELECT COUNT(*) FROM dbo.FinancialTransactions;");
        decimal Balance(int id) => Scalar<decimal>(db,$"SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={id};");
        void Refused(Action write, int sql, string test)
        {
            var before = Footprint(db);
            try { write(); }
            catch (PurchaseDataException ex) when (ex.InnerException is SqlException e && e.Number == sql)
            { Check(Footprint(db) == before, test + ": nothing persisted"); return; }
            throw new Exception("Expected SQL refusal " + sql + ": " + test);
        }
        void FailAudit(Action write, string test)
        {
            Execute(db,"CREATE TRIGGER dbo.P25FailLast ON dbo.AuditLog AFTER INSERT AS THROW 50299,N'final-write test',1;");
            try { Refused(write,50299,test); } finally { Execute(db,"DROP TRIGGER dbo.P25FailLast;"); }
        }

        var source = Create(supplier.Id, 90, 100, 20.55m, new(product,2,100), new(serial,1,40));
        var a = Create(supplier.Id, 0, 0, 0, new PurchaseLineRequest(product,1,60));
        var b = Create(supplier.Id, 0, 0, 0, new PurchaseLineRequest(product,1,60));
        var wrong = Create(outside.Id, 0, 0, 0, new PurchaseLineRequest(product,1,1000));
        var sourceId = source.Id;
        int Line(PurchaseInvoiceDesignItem i, int p) => i.Items.Single(l => l.ProductId == p).DatabaseLineId;
        int ledger = CashRows();
        Check(source.NetTotal == 150 && source.RemainingPayable == 29.45m, "Discount and split cash outflow establish the exact debt");
        Refused(() => repo.UpdateInvoice(Edit(source,241)),50255,"Document discount exceeds goods");
        Refused(() => repo.UpdateInvoice(Edit(source,121)),50255,"Edited net below posted coverage");
        Refused(() => repo.PayInvoice(new(source.Id,source.DatabaseRowVersion,30,0,0,"")),50255,"Payment exceeds actual debt");
        Refused(() => repo.PayInvoice(new(source.Id,source.DatabaseRowVersion,-1,10,0,"")),50255,"A negative tender cannot hide in a split");
        Refused(() => repo.UpdateInvoice(Edit(source,90) with { SupplierId=outside.Id }),50252,"No transferring a saved invoice to another supplier");
        var firstReturn = new PurchaseReturnRequest(source.Id,source.DatabaseRowVersion,[new(Line(source,product),1)],"تالف عند الاستلام",false);
        FailAudit(() => repo.ReturnToSupplier(firstReturn),"Final return write rolls back header, detail, units, stock, note, version and sequence");
        var r1 = repo.ReturnToSupplier(firstReturn);
        Check(r1.DebtReduction==29.45m && r1.CreditIssued==70.55m && r1.ReturnedValue==100 && CashRows()==ledger,"First return cancels debt then issues 70.55, no incoming money");
        Refused(() => otherTill.ReturnToSupplier(firstReturn),50251,"Replayed return version");
        source=Read(sourceId);
        repo.UpdateInvoice(Edit(source,90));
        Check(CashRows()==ledger && Scalar<decimal>(db,$"SELECT CostPrice FROM dbo.Products WHERE Id={product}")==1000,"Editing a historic partly returned invoice preserves cash and the latest catalog price");
        source=Read(sourceId);
        var secondReturn = new PurchaseReturnRequest(sourceId,source.DatabaseRowVersion,[new(Line(source,product),1),new(Line(source,serial),1)],"باقي القطع",false);
        var r2=repo.ReturnToSupplier(secondReturn);
        Check(r2.ReturnedValue==50 && r2.CreditIssued==50 && r2.DebtReduction==0 && r2.Status==PurchaseInvoiceStatus.FullyReturned && CashRows()==ledger,
            "Returning 140 gross reduces discounted net by only 50; full return does not invent a receipt");
        Check(Scalar<int>(db,"SELECT COUNT(*) FROM dbo.PurchaseReturns r JOIN dbo.PurchaseReturnLines x ON x.PurchaseReturnId=r.Id JOIN dbo.PurchaseReturnUnits u ON u.PurchaseReturnLineId=x.Id WHERE ReturnNumber=@n AND x.Quantity=1",("@n",r2.ReturnNumber))==2,
            "Both selected lines and exact physical units are linked to the saved second return");
        source=Read(sourceId);
        var note=source.CreditNotes.Single(c=>c.ReturnNumber==r1.ReturnNumber);
        Check(source.AvailableCredit==120.55m && source.SupplierRefundReceived==0 && source.RemainingPayable==0 && source.Items.All(l=>l.UnitRegistrationIds.Count==0),"Full-return readback separates actual money and available notes");
        Refused(() => repo.ApplyCredit(wrong.Id,wrong.DatabaseRowVersion,note,10),50257,"No cross-supplier application");
        Refused(() => repo.ApplyCredit(a.Id,a.DatabaseRowVersion,note,61),50257,"Cannot apply more than target debt");
        Refused(() => repo.ReceiveCredit(sourceId,source.DatabaseRowVersion,note,70.56m,receiving,"over"),50257,"Cannot receive more than note");
        Refused(() => repo.ReceiveCredit(sourceId,source.DatabaseRowVersion,note,20.55m,receiving,"  "),50258,"Actual receipt requires evidence");
        Execute(db,$"UPDATE dbo.FinancialAccounts SET IsActive=0 WHERE Id={receiving};");
        Refused(() => repo.ReceiveCredit(sourceId,source.DatabaseRowVersion,note,20.55m,receiving,"RCPT-25"),50256,"Stopped actual account is not replaced by the default");
        Execute(db,$"UPDATE dbo.FinancialAccounts SET IsActive=1 WHERE Id={receiving};");
        FailAudit(() => repo.ReceiveCredit(sourceId,source.DatabaseRowVersion,note,20.55m,receiving,"RCPT-25"),"Final receipt failure restores note, account, payment, ledger and proof");
        repo.ReceiveCredit(sourceId,source.DatabaseRowVersion,note,20.55m,receiving,"RCPT-25");
        source=Read(sourceId);
        Check(source.SupplierRefundReceived==20.55m && source.RemainingPayable==0 && source.AvailableCredit==100 && Balance(receiving)==40.55m && CashRows()==ledger+1 &&
              Scalar<int>(db,$"SELECT COUNT(*) FROM dbo.SupplierCreditReceipts r JOIN dbo.PurchasePayments p ON p.Id=r.PaymentId JOIN dbo.FinancialTransactions t ON t.Id=r.LedgerId WHERE r.Evidence=N'RCPT-25' AND p.PaymentKind=2 AND t.IsIncoming=1 AND p.Amount=t.Amount AND t.Amount=20.55 AND p.FinancialAccountId=t.FinancialAccountId AND t.FinancialAccountId={receiving}")==1,
            "One evidenced receipt reaches exactly the chosen account; source invoice coverage does not change twice");
        Refused(() => repo.ApplyCredit(a.Id,a.DatabaseRowVersion,note,10),50251,"Receipt invalidates the displayed credit version on another till");
        note=source.CreditNotes.Single(c=>c.Id==note.Id);
        Refused(() => repo.ReceiveCredit(sourceId,source.DatabaseRowVersion,note,10,receiving,"RCPT-25"),50258,"Even with fresh versions a used receipt cannot post again");
        FailAudit(() => repo.ApplyCredit(a.Id,a.DatabaseRowVersion,note,50),"Final application failure restores debt and spendable credit");
        using var gate=new Barrier(2);
        Task<bool> Apply(PurchaseRepository session, PurchaseInvoiceDesignItem target) => Task.Run(() =>
        {
            gate.SignalAndWait();
            try { session.ApplyCredit(target.Id,target.DatabaseRowVersion,note,50); return true; }
            catch(PurchaseDataException ex) when(ex.InnerException is SqlException { Number:50251 or 1205 }) { return false; }
        });
        var t1=Apply(repo,a); var t2=Apply(otherTill,b); Task.WaitAll(t1,t2);
        var winner=Read(t1.Result ? a.Id : b.Id); var loser=Read(t1.Result ? b.Id : a.Id);
        Check(t1.Result!=t2.Result && winner.RemainingPayable==10 && winner.CreditApplied==50 && loser.RemainingPayable==60 && Read(sourceId).AvailableCredit==50 && CashRows()==ledger+1,
            "Two sessions compete for the same 50 on different debts: one application, no money movement");
        Refused(() => repo.PayInvoice(new(winner.Id, winner.DatabaseRowVersion,11,0,0,"")),50255,"Payment bound includes applied credit");
        var cancelled=repo.CancelInvoice(winner.Id,winner.DatabaseRowVersion);
        Check(cancelled.Status==PurchaseInvoiceStatus.Cancelled && cancelled.CreditIssued==50 && cancelled.DebtReduction==10 && CashRows()==ledger+1 && Read(winner.Id).SupplierRefundReceived==0,
            "Cancelling a credit-settled invoice reissues coverage, never an imaginary cash receipt");
        var paid=Create(supplier.Id,0,30,0,new PurchaseLineRequest(product,1,30)); ledger=CashRows();
        repo.CancelInvoice(paid.Id,paid.DatabaseRowVersion); paid=Read(paid.Id);
        Check(paid.NetCashOutflow==30 && paid.AvailableCredit==30 && paid.RemainingPayable==0 && CashRows()==ledger,"Cancelling a cash-paid invoice retains the real outflow and records an unspent right");
        repo.ReceiveCredit(paid.Id,paid.DatabaseRowVersion,paid.CreditNotes.Single(),5,receiving,"RCPT-CANCEL");
        var rows=Load().Where(i=>i.SupplierId==supplier.Id).ToList();
        var directory=repo.LoadSuppliers().Single(s=>s.Id==supplier.Id);
        Check(directory.OutstandingPayable==rows.Sum(i=>i.RemainingPayable) && directory.AvailableCredit==rows.Sum(i=>i.AvailableCredit) &&
              rows.Sum(i=>i.NetTotal-i.NetCashOutflow)==directory.OutstandingPayable-directory.AvailableCredit &&
              rows.Sum(i=>i.Postings.Sum(p=>p.Change)+i.ActivityLog.Sum(a=>a.DocumentDelta??0m))==directory.OutstandingPayable-directory.AvailableCredit,
            "Invoice, supplier summary and chronological posting rows reconcile debt less spendable credit, including cancellations");

        Refused(() => repo.DeleteSupplier(supplier.Id),50257,"Outstanding debt/credit cannot disappear from the supplier directory");
        var noteOnly=repo.CreateSupplier("إشعار بلا ديون", "", "", "", "", "", "", "", "");
        var onlyPaid=Create(noteOnly.Id,0,6,0,new PurchaseLineRequest(product,1,6));
        repo.CancelInvoice(onlyPaid.Id,onlyPaid.DatabaseRowVersion);
        Refused(()=>repo.DeleteSupplier(noteOnly.Id),50257,"A supplier owed nothing cannot be archived while the shop still has an unspent right");
        onlyPaid=Read(onlyPaid.Id);
        repo.ReceiveCredit(onlyPaid.Id,onlyPaid.DatabaseRowVersion,onlyPaid.CreditNotes.Single(),6,receiving,"CLOSE-NOTE");
        Check(!repo.DeleteSupplier(noteOnly.Id) && Scalar<int>(db,$"SELECT IsActive FROM dbo.Suppliers WHERE Id={noteOnly.Id}")==0,"Once settled, directory archives supplier without removing linked history");
        var partly=Create(supplier.Id,0,120,0,new PurchaseLineRequest(product,2,60));
        repo.ReturnToSupplier(new(partly.Id,partly.DatabaseRowVersion,[new(Line(partly,product),1)],"إرجاع نصف الكمية",false));
        partly=Read(partly.Id);
        repo.ReceiveCredit(partly.Id,partly.DatabaseRowVersion,partly.CreditNotes.Single(),20,receiving,"ACTUAL-20");
        partly=Read(partly.Id); ledger=CashRows(); var receivedBalance=Balance(receiving);
        var edit=Edit(partly,0) with { Lines=[new(product,2,60,Line(partly,product)),new(serial,1,10)] };
        repo.UpdateInvoice(edit); partly=Read(partly.Id);
        Check(partly.NetTotal==70 && partly.RemainingPayable==10 && partly.SupplierRefundReceived==20 && partly.AvailableCredit==40 && CashRows()==ledger && Balance(receiving)==receivedBalance &&
              partly.Items.Single(l=>l.ProductId==serial).UnitRegistrationIds.Count==1, "Adding a historic line after a real receipt preserves cash, prior receipt and remaining note; real new line ID owns its unit");
        Refused(()=>repo.UpdateInvoice(edit),50251,"A stale edit after a receipt cannot be replayed");
        Refused(()=>repo.UpdateInvoice(Edit(partly,11)),50255,"Edit after receipt cannot depress net below posted coverage");
        var debtor=Create(supplier.Id,0,100,0,new PurchaseLineRequest(product,10,100)); ledger=CashRows();
        var debtReturn=repo.ReturnToSupplier(new(debtor.Id,debtor.DatabaseRowVersion,[new(Line(debtor,product),5)],"رد على الحساب",false));
        Check(debtReturn.CreditIssued==0 && debtReturn.DebtReduction==500 && Read(debtor.Id).RemainingPayable==400 && CashRows()==ledger,"Debt-only return: 1000 invoice / 100 paid / 500 returned leaves 400 due, no cash or note");
        Refused(()=>repo.PayInvoice(new(debtor.Id,debtor.DatabaseRowVersion,10,0,0,"")),50251,"Payment dialog opened before return cannot post later");
        var cashId=Scalar<int>(db,"SELECT Id FROM dbo.FinancialAccounts WHERE AccountKind=0");
        Execute(db,$"UPDATE dbo.FinancialAccounts SET CurrentBalance=0 WHERE Id={cashId};");
        var due=Read(debtor.Id);
        Refused(()=>repo.PayInvoice(new(due.Id,due.DatabaseRowVersion,1,0,0,"")),50256,"Insufficient actual funds refuse the payout");

        var stock=Create(supplier.Id,0,0,0,new PurchaseLineRequest(serial,1,30));
        var unit=stock.Items[0].UnitRegistrationIds.Single();
        Execute(db,$"UPDATE dbo.InventoryUnits SET UnitStatus=1 WHERE Id={unit};");
        Refused(()=>repo.ReturnToSupplier(new(stock.Id,stock.DatabaseRowVersion,[new(Line(stock,serial),1)],"مباع",false)),50254,"Cannot fake an untracked departure when a unit is sold");
        Refused(()=>repo.CancelInvoice(stock.Id,stock.DatabaseRowVersion),50254,"Cannot cancel out a sold unit");
        Execute(db,$"UPDATE dbo.InventoryUnits SET UnitStatus=2,DamagedAtUtc=SYSUTCDATETIME() WHERE Id={unit}; INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId) VALUES({serial},{unit},N'Damage',-1,30,{actor});");
        repo.ReturnToSupplier(new(stock.Id,stock.DatabaseRowVersion,[new(Line(stock,serial),1)],"هالك",true));
        Check(Scalar<int>(db,$"SELECT SUM(QuantityDelta) FROM dbo.StockMovements WHERE InventoryUnitId={unit};")==0 && Scalar<int>(db,$"SELECT COUNT(*) FROM dbo.PurchaseReturnUnits WHERE InventoryUnitId={unit} AND WasDamaged=1")==1,
            "Returning damaged goods records physical evidence without decrementing sellable stock twice");
        // Terminal units retain history but cannot mask older quantity-only stock after the last live unit leaves.
        var mixed=Scalar<int>(db,$"INSERT dbo.Products(CategoryId,Name,UnitName,RetailPrice) OUTPUT inserted.Id SELECT CategoryId,N'رصيد كمي باق',N'قطعة',10 FROM dbo.Products WHERE Id={product}");
        Execute(db,$"INSERT dbo.StockMovements(ProductId,MovementType,QuantityDelta,ActorUserId) VALUES({mixed},N'Supply',3,{actor});");
        var onlyUnit=Create(supplier.Id,0,0,0,new PurchaseLineRequest(mixed,1,10));
        repo.CancelInvoice(onlyUnit.Id,onlyUnit.DatabaseRowVersion);
        new SalesRepository(settings,actor).CompleteSale(new(null,true,PriceTier.Retail,0,[new(mixed,1,PriceTier.Retail,10)],10,0,0,""));
        Check(Scalar<int>(db,$"SELECT QuantityOnHand FROM dbo.vwProductStock WHERE ProductId={mixed}")==2,"Retired purchased unit cannot hide the remaining quantity-only stock from POS");
        // Old client left a return header but never executed the detail insert. No automatic credit/backfill.
        var legacy=Create(supplier.Id,0,0,0,new PurchaseLineRequest(product,2,20));
        Execute(db,$"INSERT dbo.PurchaseReturns(ReturnNumber,PurchaseInvoiceId,ReturnedAtUtc,ActorUserId) VALUES(N'PRT-LEGACY-25',{legacy.Id},SYSUTCDATETIME(),{actor}); UPDATE dbo.PurchaseInvoiceLines SET ReturnedQuantity=1 WHERE PurchaseInvoiceId={legacy.Id};");
        legacy=Read(legacy.Id);
        Check(legacy.NeedsReview && legacy.AvailableCredit==0,"Incomplete historical return is visible for review, never promoted to spendable credit");
        Refused(()=>repo.PayInvoice(new(legacy.Id,legacy.DatabaseRowVersion,10,0,0,"")),50253,"Legacy evidence gap blocks further settlement");
    }
}
