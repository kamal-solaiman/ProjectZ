using System.Collections.ObjectModel;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.ViewModels;

internal static class MaintenancePostingTests
{
    private static MaintenanceOrderDesignItem Shown(int id = 7) => new()
    {
        Id=id, OrderNumber=$"MNT-UI-{id}", CustomerName="عميل", ReceivedAt=DateTime.Today,
        DatabaseRowVersion=[0,0,0,0,0,0,0,1], EstimatedCost=100, DepositCashPaid=30, FinalPaymentInstaPayPaid=20
    };

    private sealed class Store : IMaintenanceSettlement
    {
        public readonly List<MaintenanceDelivery> Requests = [];
        public MaintenanceOrderDesignItem Saved = Shown();
        public bool Refuse, FailRead;
        public int Collections, Prints;
        public byte[] CollectDeposit(MaintenanceTicketKey key, FinancialAccountKind account, decimal amount)
        {
            Collections++;
            if (Refuse) throw new MaintenanceDataException("رفض الاختبار");
            Saved = Shown(key.Id);
            Saved.DepositCashPaid += amount;
            return Saved.DatabaseRowVersion = [0,0,0,0,0,0,0,2];
        }
        public byte[] Deliver(MaintenanceDelivery request)
        {
            Requests.Add(request);
            if (Refuse) throw new MaintenanceDataException("رفض الاختبار");
            Saved = Shown(request.Ticket.Id);
            Saved.FinalPaymentCashPaid += request.Cash;
            Saved.Status = MaintenanceOrderStatus.Delivered;
            return Saved.DatabaseRowVersion = [0,0,0,0,0,0,0,2];
        }
        public MaintenanceOrderDesignItem LoadTicket(int id) => FailRead ? throw new MaintenanceDataException("فشل القراءة") : Saved;
        public DateTime AppendMaintenanceActivity(MaintenanceTicketKey key, byte mode) { Prints++; return DateTime.Now; }
    }

    internal static void Run(Action<bool,string> check)
    {
        var shown=Shown();
        bool? Confirm(MaintenanceDeliveryViewModel dialog)
        {
            check(dialog.RemainingBeforeFinalPayment==50 && dialog.CurrentFinalCollected==50, "Delivery counts prior finals as well as deposits");
            dialog.ConfirmAndPrintCommand.Execute(null);
            check(dialog.IsConfirmed && dialog.Request?.Cash==50 && !shown.IsDelivered && shown.TotalCollectedAll==50,
                "Confirmation produces an intent; no status change, money or warranty yet");
            return true;
        }
        void UnexpectedPrint(MaintenanceReceiptPreviewViewModel _) => throw new InvalidOperationException("Uncommitted warranty receipt");
        var orders = new ObservableCollection<MaintenanceOrderDesignItem> { shown };
        var workshop = new MaintenanceActionsViewModel("اختبار",orders,[]) { OpenDeliveryDialogRequested=Confirm, OpenReceiptPreviewRequested=UnexpectedPrint };
        workshop.OpenDeliveryDialogCommand.Execute(null);
        var register = new MaintenanceInvoicesViewModel("اختبار",orders) { OpenDeliveryDialogRequested=Confirm, OpenReceiptPreviewRequested=UnexpectedPrint };
        register.OpenDeliverDialogCommand.Execute(shown);
        check(workshop.IsErrorStatus && register.IsErrorStatus && shown.IsActiveOrder && shown.ActivityLog.Count==0,
            "Workshop and invoice register reject unbacked confirmations without printing or changing the order");
        var preview=new MaintenanceReceiptPreviewViewModel(shown,initialMode:MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt);
        preview.SelectDeliveryReceiptModeCommand.Execute(null);
        check(!preview.IsDeliveryReceiptMode, "An open/ready order cannot be printed as delivered manually either");

        var store=new Store { Refuse=true };
        var notices=0; MaintenanceReceiptPreviewViewModel? receipt=null;
        var profile=new CustomersViewModel("اختبار",[],[],orders,maintenanceRepository:store,maintenanceChanged:()=>notices++)
        { OpenMaintenanceDeliveryRequested=Confirm, OpenMaintenanceReceiptPreviewRequested=vm=>receipt=vm };
        MaintenanceInvoiceDetailsViewModel? details=null;
        profile.OpenMaintenanceDetailsRequested=d=>details=d;
        profile.OpenMaintenanceDetailsCommand.Execute(shown);
        check(details?.CanCollectPayment==true, "Customer detail uses the maintenance posting store");
        details!.CollectPaymentAmountText="10";
        details.CollectCashCommand.Execute(null);
        details.DeliverAndCollectCommand.Execute(null);
        profile.DeliverAndCollectMaintenanceCommand.Execute(shown);
        check(details.IsErrorStatus && profile.IsErrorStatus && store.Collections==1 && store.Requests.Count==2 && receipt is null && notices==0
              && shown.PaymentHistory.Count==0 && shown.ActivityLog.Count==0 && shown.TotalCollectedAll==50,
            "Rejected detail collection and both customer delivery paths leave no pending entry or success callback");
        store.Refuse=false; store.FailRead=true;
        profile.DeliverAndCollectMaintenanceCommand.Execute(shown);
        check(receipt is null && notices==0 && profile.IsErrorStatus && profile.StatusMessage.Contains("لا تكرر"),
            "Commit acknowledged but reread failed: distinguish it from a rejection; never print or resubmit");
        store.FailRead=false;
        profile.OpenMaintenanceDeliveryRequested=dialog=>
        {
            // Simulate replacement of the shared collection while the dialog is open.
            orders.Clear(); orders.Add(Shown(99));
            shown.DatabaseRowVersion![7]=9;
            return Confirm(dialog);
        };
        profile.DeliverAndCollectMaintenanceCommand.Execute(shown);
        var last=store.Requests.Last();
        check(last.Ticket.Id==shown.Id && last.Ticket.Version![7]==1 && last.Cash==50 && last.ExpectedTotal==100,
            "Selection/reload cannot retarget a confirmed request or substitute a newer version");
        check(notices==1 && receipt?.Order==store.Saved && receipt.IsDeliveryReceiptMode && receipt.Order.TotalCollectedAll==100 && !shown.IsDelivered,
            "Only the reread committed snapshot, not the selected row or edited draft, supplies the warranty receipt");
        receipt!.PrintCommand.Execute(null);
        check(store.Prints==1 && store.Collections==1 && store.Requests.Count==4, "Printing appends an activity; it cannot retry a rejected payment");
        var otherDetail = new MaintenanceInvoiceDetailsViewModel(Shown(8),"اختبار",store) { CollectPaymentAmountText="10" };
        otherDetail.CollectCashCommand.Execute(null);
        check(otherDetail.Order==store.Saved && otherDetail.Order.Id==8 && otherDetail.Order.TotalCollectedAll==60 && store.Collections==2,
            "The next ticket receives only its own payment; a failed ten-pound collection is not replayed");
    }
}
