using System.Collections.ObjectModel;
using ProjectZ.Models;
using ProjectZ.ViewModels;
using ProjectZ.Views;

internal static class PurchasePostingTests
{
    internal static void Run(Action<bool,string> check)
    {
        var source = new PurchaseInvoiceDesignItem { Id=10, SupplierId=4, InvoiceNumber="PUR-UI-10", SupplierName="مورد الاختبار",
            Status=PurchaseInvoiceStatus.PartiallyReturned, DiscountAmount=90, CashPaid=120.55m, IssuedAt=DateTime.Today };
        source.Items.Add(new() { DatabaseLineId=1, ProductId=7, Quantity=2, ReturnedQuantity=1, UnitPurchaseCost=100, UnitRegistrationIds=[77] });
        source.Items.Add(new() { DatabaseLineId=2, ProductId=8, Quantity=1, UnitPurchaseCost=40, UnitRegistrationIds=[88] });
        var note = new SupplierCreditItem(12,4,10,"PRT-UI-12",70.55m,0,0,70.55m,[0,0,0,0,0,0,0,1]);
        source.CreditNotes.Add(note);
        InventoryUnitRegistrationItem[] units = [new() { Id=77, ProductId=7 }, new() { Id=88, ProductId=8 }];
        var ret = new PurchaseReturnViewModel(source,units);
        ret.SelectAllReturnableCommand.Execute(null);
        check(ret.TotalReturnUnits==2 && ret.TotalReturnValue==50 && ret.AppliedToCredit==0 && ret.ExpectedRefundAboveCredit==50,
            "Return preview distinguishes 140 gross from 50 refundable net, without a cash default");
        var edit = new EditPurchaseInvoiceViewModel(source,[],units) { DiscountAmountText="91" };
        edit.SaveCommand.Execute(null);
        check(edit.Result is null && edit.HasError, "Even after an earlier credit, edit must cover actual posted settlement");
        edit.DiscountAmountText="90"; edit.SaveCommand.Execute(null);
        check(edit.Result is not null && edit.RemainingPayable==0 && source.CashPaid==120.55m && source.SupplierRefundReceived==0,
            "Document editor preserves posted cash, receipt and credit; no channel fields submitted");

        PurchaseInvoiceDesignItem Debt(int id,int supplier) { var d=new PurchaseInvoiceDesignItem { Id=id,SupplierId=supplier,Status=PurchaseInvoiceStatus.FullCredit }; d.Items.Add(new(){Quantity=1,UnitPurchaseCost=40}); return d; }
        var target=Debt(20,4); var wrong=Debt(21,5);
        var account=new PurchaseReceiptAccount(9,"محفظة غير افتراضية","خط 2");
        var use=new SupplierCreditDialogViewModel(source,[target,wrong,source],[account]) { AmountText="15", Target=wrong };
        use.ConfirmCommand.Execute(null);
        check(use.Debts.SequenceEqual([target]) && use.Result is null, "Credit dialog does not offer another supplier or its source invoice");
        use.Target=target; use.ConfirmCommand.Execute(null);
        check(use.Result?.Target==target && use.Result.Account is null && use.Result.Credit==note && use.Result.Amount==15,
            "Application carries the displayed note and debt, not a financial account");
        use.IsReceipt=true; use.ConfirmCommand.Execute(null);
        check(use.Result is null && use.Error.Length>0, "Switching to receipt does not reuse the debt as consent to collect");
        use.Account=account; use.ConfirmCommand.Execute(null);
        check(use.Result is null, "Actual account alone is not receipt evidence");
        use.Evidence=" إيصال 17 "; use.ConfirmCommand.Execute(null);
        check(use.Result?.Account==account && use.Result.Target is null && use.Result.Evidence=="إيصال 17", "Receipt carries an explicit account and proof, never a debt application");
        // Instantiate the actual window under the loaded app resources (not a mock binder).
        var window=new SupplierCreditWindow(use); check(window.DataContext==use,"WPF loads the supplier-credit window and shared theme resources"); window.Close();
        var pay=new PaySupplierInvoiceViewModel(target) { CashAmountText="17", InstaPayAmountText="invalid" };
        pay.ConfirmCommand.Execute(null); check(pay.Result is null && pay.HasError,"Invalid split is not silently omitted from payment");

        source.ActivityLog.Add(new() { Timestamp=source.IssuedAt, ActionTitle="عنوان قابل للترجمة", DocumentDelta=150 });
        source.ActivityLog.Add(new() { Timestamp=source.IssuedAt.AddMinutes(2), ActionTitle="تصحيح", DocumentDelta=-100 });
        source.ActivityLog.Add(new() { Timestamp=source.IssuedAt.AddMinutes(3), ActionTitle="سداد أو مرتجع؟", Amount=9999 });
        source.Postings.Add(new(source.IssuedAt.AddMinutes(1),"سداد حقيقي للقناة",-120.55m));
        var entry=new SupplierDirectoryEntryViewModel(new(){Id=4}); entry.Recalculate([source]);
        var statement=SuppliersViewModel.BuildStatement(entry);
        check(statement.Count==3 && statement[^1].RunningBalance==-70.55m && entry.Outstanding==0 && entry.AvailableCredit==70.55m,
            "Supplier statement ignores free-text amounts, shows the unspent right, and matches posted changes");
        // An open detail window must pick up the replacement collection row after a nested posting.
        var next = Debt(20,4); next.CashPaid=40;
        var details=new PurchaseInvoiceDetailsViewModel(target,new ObservableCollection<SupplierDesignItem>(),[],_=>{},_=>{},_=>{},_=>{},_=>{},_=>false,_=>{},_=>next,()=>("تمت القراءة",false));
        details.PayRemainingCommand.Execute(null);
        check(details.Invoice==next && !details.Invoice.CanPayRemaining && details.StatusMessage=="تمت القراءة", "Nested invoice details rebind to the committed instance before another payment");
    }
}
