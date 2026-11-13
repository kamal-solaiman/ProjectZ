using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceInvoiceDetailsViewModel : ObservableObject
{
    private readonly string currentUserName;
    private readonly Data.MaintenanceRepository? repository;
    // Entries accumulated since the last DB save; flushed together with the order update.
    private readonly List<MaintenanceActivityEntry> pendingActivities = new();
    private readonly List<Data.MaintenancePaymentEntry> pendingPayments = new();
    private string statusMessage = "";
    private bool isErrorStatus;
    private string collectPaymentAmountText = "";

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
        Data.MaintenanceRepository? repository = null)
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

        pendingPayments.Add(new Data.MaintenancePaymentEntry(account, 1, 1, amount, DateTime.Now, $"دفعة من الفاتورة - {channelTitle}"));

        RecordActivity(new MaintenanceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "تحصيل دفعة من فاتورة الصيانة",
            Details = $"تحصيل {PosFormatting.FormatCurrency(amount)} عبر {channelTitle}.",
            ActorName = currentUserName,
            Amount = amount
        });

        CollectPaymentAmountText = "";
        if (TryPersistOrder(
                $"تم تحصيل دفعة {PosFormatting.FormatCurrency(amount)} عبر {channelTitle} على الفاتورة {Order.OrderNumber}.",
                "تعذّر حفظ الدفعة"))
        {
            // TryPersistOrder already refreshed financials, reloaded the orders and set the status.
        }
    }

    /// <summary>Flushes accumulated activities/payments and the order state to the database.</summary>
    /// <returns>True when the save succeeded (or no repository is connected).</returns>
    private bool TryPersistOrder(string successMessage, string errorPrefix)
    {
        if (repository is null)
            return true;

        try
        {
            repository.UpdateMaintenanceOrder(Order, pendingActivities, pendingPayments);
            pendingActivities.Clear();
            pendingPayments.Clear();
            NotifyFinancialRefresh();
            NotifyOrdersChanged?.Invoke();
            SetStatus(successMessage, false);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"{errorPrefix}: {ex.Message}", true);
            return false;
        }
    }

    private void RecordActivity(MaintenanceActivityEntry entry)
    {
        Order.ActivityLog.Add(entry);
        pendingActivities.Add(entry);
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
            var cashBefore = Order.FinalPaymentCashPaid;
            var vodBefore = Order.FinalPaymentVodafonePaid;
            var instaBefore = Order.FinalPaymentInstaPayPaid;
            var refundBefore = Order.RefundedDepositAmount;

            var cashNew = Order.FinalPaymentCashPaid - cashBefore;
            var vodNew = Order.FinalPaymentVodafonePaid - vodBefore;
            var instaNew = Order.FinalPaymentInstaPayPaid - instaBefore;
            var refundNew = Order.RefundedDepositAmount - refundBefore;
            var totalNewCollected = cashNew + vodNew + instaNew;

            if (cashNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.Cash, 2, 1, cashNew, DateTime.Now, "دفعة تسليم - كاش"));
            if (vodNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.VodafoneCash, 2, 1, vodNew, DateTime.Now, "دفعة تسليم - فودافون كاش"));
            if (instaNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.InstaPay, 2, 1, instaNew, DateTime.Now, "دفعة تسليم - إنستاباي"));
            if (refundNew > 0m)
                pendingPayments.Add(new Data.MaintenancePaymentEntry(FinancialAccountKind.Cash, 2, 2, refundNew, DateTime.Now, "رد فرق عربون عند التسليم"));

            var summaryParts = new List<string>
            {
                $"التكلفة النهائية: {Order.EstimatedCostDisplay}",
                vm.WarrantyDays > 0 ? $"ضمان {vm.WarrantyDays} يومًا" : "بدون ضمان"
            };
            if (totalNewCollected > 0m)
                summaryParts.Add($"تحصيل عند التسليم: {PosFormatting.FormatCurrency(totalNewCollected)}");
            if (refundNew > 0m)
                summaryParts.Add($"رد فرق عربون: {PosFormatting.FormatCurrency(refundNew)}");
            if (!string.IsNullOrWhiteSpace(Order.DeliveryNotes))
                summaryParts.Add(Order.DeliveryNotes);

            RecordActivity(new MaintenanceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "تسليم الجهاز وإغلاق أمر الصيانة",
                Details = string.Join(" · ", summaryParts),
                ActorName = currentUserName,
                Amount = totalNewCollected > 0m ? totalNewCollected : Order.CurrentEffectiveCost
            });

            if (repository is not null)
                TryPersistOrder(
                    $"تم تسليم الجهاز {Order.OrderNumber} وإغلاق الفاتورة ({Order.WarrantySummaryDisplay}).",
                    "تعذّر حفظ التسليم");
            else
            {
                NotifyFinancialRefresh();
                NotifyOrdersChanged?.Invoke();
                SetStatus($"تم تسليم الجهاز {Order.OrderNumber} وإغلاق الفاتورة ({Order.WarrantySummaryDisplay}).", false);
            }

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
