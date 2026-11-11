using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceInvoiceDetailsViewModel : ObservableObject
{
    private readonly string currentUserName;
    private string statusMessage = "";
    private bool isErrorStatus;

    public MaintenanceOrderDesignItem Order { get; }

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

    public Action? RequestClose { get; set; }
    public Func<MaintenanceDeliveryViewModel, bool?>? OpenDeliveryDialogRequested { get; set; }
    public Action<MaintenanceReceiptPreviewViewModel>? OpenReceiptPreviewRequested { get; set; }
    public Action<MaintenanceOrderDesignItem>? OpenInActionsRequested { get; set; }
    public Action? NotifyOrdersChanged { get; set; }

    public ActionCommand PrintPreviewCommand { get; }
    public ActionCommand DeliverAndCollectCommand { get; }
    public ActionCommand OpenInActionsCommand { get; }
    public ActionCommand CloseCommand { get; }

    public MaintenanceInvoiceDetailsViewModel(MaintenanceOrderDesignItem order, string currentUserName)
    {
        Order = order;
        this.currentUserName = currentUserName;

        PrintPreviewCommand = new ActionCommand(() => OpenPrintPreview(null));
        DeliverAndCollectCommand = new ActionCommand(OpenDelivery);
        OpenInActionsCommand = new ActionCommand(() =>
        {
            RequestClose?.Invoke();
            OpenInActionsRequested?.Invoke(Order);
        });
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void OpenDelivery()
    {
        if (Order.IsCancelled)
        {
            SetStatus("هذا الأمر ملغى؛ أعد فتحه من شاشة إجراءات الصيانة أولًا.", true);
            return;
        }

        if (Order.IsDelivered)
        {
            SetStatus("هذا الأمر مسلّم ومغلق بالفعل.", true);
            return;
        }

        var vm = new MaintenanceDeliveryViewModel(Order, currentUserName);
        var result = OpenDeliveryDialogRequested?.Invoke(vm);
        if (result == true || vm.IsConfirmed)
        {
            NotifyFinancialRefresh();
            NotifyOrdersChanged?.Invoke();
            SetStatus($"تم تسليم الجهاز {Order.OrderNumber} وإغلاق الفاتورة ({Order.WarrantySummaryDisplay}).", false);

            if (vm.ShouldOpenPrintAfterClose)
                OpenPrintPreview(MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt);
        }
    }

    private void OpenPrintPreview(MaintenanceReceiptPrintMode? explicitMode)
    {
        var initialMode = explicitMode ?? (Order.IsDelivered || Order.IsReadyForPickup
            ? MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt
            : MaintenanceReceiptPrintMode.CustomerIntakeReceipt);

        var previewVm = new MaintenanceReceiptPreviewViewModel(
            Order,
            modeLabel =>
            {
                Order.ActivityLog.Add(new MaintenanceActivityEntry
                {
                    Timestamp = DateTime.Now,
                    ActionTitle = $"طباعة: {modeLabel}",
                    Details = $"معاينة وطباعة {modeLabel} من فاتورة الصيانة {Order.OrderNumber}.",
                    ActorName = currentUserName
                });
                SetStatus($"تمت معاينة طباعة {Order.OrderNumber} ({modeLabel}).", false);
            },
            initialMode);

        OpenReceiptPreviewRequested?.Invoke(previewVm);
    }

    public void NotifyFinancialRefresh()
    {
        Changed(nameof(HasWarrantySection));
        Changed(nameof(ProfitMarginDisplay));
        Changed(nameof(PartsTotalUnitsSummary));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
