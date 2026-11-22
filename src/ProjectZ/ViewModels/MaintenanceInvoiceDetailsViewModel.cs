using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceInvoiceDetailsViewModel : ObservableObject
{
    private readonly string currentUserName;
    private readonly Data.IMaintenanceSettlement? repository;
    private string statusMessage = "";
    private bool isErrorStatus;
    private string collectPaymentAmountText = "";

    public MaintenanceOrderDesignItem Order { get; private set; }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value) return;
            statusMessage = value;
            Changed();
            Changed(nameof(HasStatusMessage));
        }
    }

    public bool IsErrorStatus
    {
        get => isErrorStatus;
        private set
        {
            if (isErrorStatus == value) return;
            isErrorStatus = value;
            Changed();
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasWarrantySection => Order.IsDelivered || Order.WarrantyDays > 0 || !string.IsNullOrWhiteSpace(Order.DeliveryNotes);

    public string ProfitMarginDisplay
    {
        get
        {
            if (Order.IsCancelled || Order.IsPriceStillPending || Order.CurrentEffectiveCost <= 0m)
                return "—";
            var pct = Math.Round((Order.EstimatedProfit / Order.CurrentEffectiveCost) * 100m, 1);
            return $"{pct:0.#}%";
        }
    }

    public string PartsTotalUnitsSummary => Order.HasUsedParts
        ? $"{Order.UsedParts.Count} بند ({Order.UsedPartsTotalCount} قطعة)"
        : "لا توجد قطع غيار مصروفة";

    public string CollectPaymentAmountText
    {
        get => collectPaymentAmountText;
        set
        {
            if (collectPaymentAmountText == value) return;
            collectPaymentAmountText = value;
            Changed();
        }
    }

    public bool CanCollectPayment => Order is not null && Order.IsActiveOrder && repository is not null;

    public Action? RequestClose { get; set; }
    public Func<MaintenanceDeliveryViewModel, bool?>? OpenDeliveryDialogRequested { get; set; }
    public Action<MaintenanceReceiptPreviewViewModel>? OpenReceiptPreviewRequested { get; set; }
    public Action<MaintenanceOrderDesignItem>? OpenInActionsRequested { get; set; }
    public Action? NotifyOrdersChanged { get; set; }

    public ActionCommand PrintPreviewCommand { get; }
    public ActionCommand DeliverAndCollectCommand { get; }
    public ActionCommand CollectCashCommand { get; }
    public ActionCommand CollectVodafoneCommand { get; }
    public ActionCommand CollectInstaPayCommand { get; }
    public ActionCommand OpenInActionsCommand { get; }
    public ActionCommand CloseCommand { get; }

    public MaintenanceInvoiceDetailsViewModel(
        MaintenanceOrderDesignItem order,
        string currentUserName,
        Data.IMaintenanceSettlement? repository = null)
    {
        Order = order;
        this.currentUserName = currentUserName;
        this.repository = repository;

        PrintPreviewCommand = new ActionCommand(() => OpenPrintPreview(null));
        DeliverAndCollectCommand = new ActionCommand(OpenDelivery);
        CollectCashCommand = new ActionCommand(() => CollectPayment(FinancialAccountKind.Cash, "كاش"));
        CollectVodafoneCommand = new ActionCommand(() => CollectPayment(FinancialAccountKind.VodafoneCash, "فودافون كاش"));
        CollectInstaPayCommand = new ActionCommand(() => CollectPayment(FinancialAccountKind.InstaPay, "إنستاباي"));
        OpenInActionsCommand = new ActionCommand(() =>
        {
            RequestClose?.Invoke();
            OpenInActionsRequested?.Invoke(Order);
        });
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    /// <summary>Records a payment (deposit / part payment) directly on the invoice order.</summary>
    private void CollectPayment(FinancialAccountKind account, string channelTitle)
    {
        if (repository is null || !Order.IsActiveOrder)
            return;

        if (!PosFormatting.TryParseAmount(CollectPaymentAmountText, out var amount) || amount <= 0m)
        {
            SetStatus("أدخل مبلغًا أكبر من صفر لتحصيل الدفعة.", true);
            return;
        }

        var order = Order;
        var key = Data.MaintenanceTicketKey.Open(order);
        if (MaintenancePosting.Save(repository, order, () => repository.CollectDeposit(key, account, amount), AcceptSaved, SetStatus,
                $"تم تحصيل {PosFormatting.FormatCurrency(amount)} عبر {channelTitle} على {order.OrderNumber}."))
            CollectPaymentAmountText = "";
    }

    private void AcceptSaved(MaintenanceOrderDesignItem saved)
    {
        Order = saved;
        Changed(nameof(Order));
        Changed(nameof(CanCollectPayment));
        NotifyFinancialRefresh();
        NotifyOrdersChanged?.Invoke();
    }

    private void OpenDelivery()
    {
        if (!Order.IsActiveOrder) { SetStatus("هذا الأمر مغلق بالفعل.", true); return; }
        MaintenancePosting.Deliver(new MaintenanceDeliveryViewModel(Order), repository, OpenDeliveryDialogRequested,
            AcceptSaved, OpenReceiptPreviewRequested, currentUserName, SetStatus);
    }

    private void OpenPrintPreview(MaintenanceReceiptPrintMode? mode) =>
        MaintenancePosting.Preview(Order, repository, currentUserName, OpenReceiptPreviewRequested, SetStatus, mode);

    public void NotifyFinancialRefresh()
    {
        Changed(nameof(HasWarrantySection));
        Changed(nameof(ProfitMarginDisplay));
        Changed(nameof(PartsTotalUnitsSummary));
        Changed(nameof(PaymentsCountSummary));
    }

    public string PaymentsCountSummary => Order.PaymentHistorySummaryText;

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
