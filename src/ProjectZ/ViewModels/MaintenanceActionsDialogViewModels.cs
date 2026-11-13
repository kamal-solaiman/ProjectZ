using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceDeliveryViewModel : ObservableObject
{
    private readonly string currentUserName;
    private MaintenanceDepositMode paymentMode;
    private string laborCostText;
    private string discountText;
    private string cashAmountText = "0";
    private string vodafoneAmountText = "0";
    private string instaPayAmountText = "0";
    private bool refundExcessDeposit = true;
    private int warrantyDays = 14;
    private string customWarrantyDaysText = "14";
    private string deliveryNotes = "";
    private string errorMessage = "";

    public MaintenanceOrderDesignItem Order { get; }
    public bool ShouldOpenPrintAfterClose { get; private set; }
    public bool IsConfirmed { get; private set; }

    public IReadOnlyList<int> WarrantyPresets => MaintenanceDesignSeedData.WarrantyDayOptions;

    public string LaborCostText
    {
        get => laborCostText;
        set
        {
            if (laborCostText == value) return;
            laborCostText = value;
            Changed();
            SyncSuggestedPaymentAfterPricingChange();
        }
    }

    public string DiscountText
    {
        get => discountText;
        set
        {
            if (discountText == value) return;
            discountText = value;
            Changed();
            SyncSuggestedPaymentAfterPricingChange();
        }
    }

    public decimal ParsedLaborCost => PosFormatting.TryParseAmount(LaborCostText, out var v) && v >= 0m ? v : 0m;
    public decimal ParsedDiscount => PosFormatting.TryParseAmount(DiscountText, out var v) && v >= 0m ? v : 0m;
    public decimal EffectiveFinalTotal => Math.Max(0m, Order.PartsTotal + ParsedLaborCost - ParsedDiscount);
    public decimal RemainingBeforeFinalPayment => Math.Max(0m, EffectiveFinalTotal - Order.NetDepositRetained);
    public decimal ExcessDepositOverTotal => Math.Max(0m, Order.NetDepositRetained - EffectiveFinalTotal);
    public bool HasRemainingToCollect => RemainingBeforeFinalPayment > 0m;
    public bool HasExcessDeposit => ExcessDepositOverTotal > 0m;

    public bool RefundExcessDeposit
    {
        get => refundExcessDeposit;
        set
        {
            if (refundExcessDeposit == value) return;
            refundExcessDeposit = value;
            Changed();
            Changed(nameof(ExcessDepositSummary));
        }
    }

    public string EffectiveFinalTotalDisplay => PosFormatting.FormatCurrency(EffectiveFinalTotal);
    public string RemainingBeforeFinalPaymentDisplay => PosFormatting.FormatCurrency(RemainingBeforeFinalPayment);
    public string ExcessDepositOverTotalDisplay => PosFormatting.FormatCurrency(ExcessDepositOverTotal);
    public string ExcessDepositSummary => RefundExcessDeposit
        ? $"سيتم رد فرق العربون ({ExcessDepositOverTotalDisplay}) للعميل عند التسليم."
        : $"يبقى الفرق ({ExcessDepositOverTotalDisplay}) ضمن رصيد الإيصال دون رد.";

    public bool IsNoFinalPaymentMode => paymentMode == MaintenanceDepositMode.NoDeposit;
    public bool IsCashMode => paymentMode == MaintenanceDepositMode.CashOnly;
    public bool IsVodafoneMode => paymentMode == MaintenanceDepositMode.VodafoneCashOnly;
    public bool IsInstaPayMode => paymentMode == MaintenanceDepositMode.InstaPayOnly;
    public bool IsSplitMode => paymentMode == MaintenanceDepositMode.Split;

    public bool ShowPaymentInputs => paymentMode != MaintenanceDepositMode.NoDeposit && HasRemainingToCollect;
    public bool ShowCashInput => paymentMode is MaintenanceDepositMode.CashOnly or MaintenanceDepositMode.Split;
    public bool ShowVodafoneInput => paymentMode is MaintenanceDepositMode.VodafoneCashOnly or MaintenanceDepositMode.Split;
    public bool ShowInstaPayInput => paymentMode is MaintenanceDepositMode.InstaPayOnly or MaintenanceDepositMode.Split;

    public string CashAmountText
    {
        get => cashAmountText;
        set
        {
            if (cashAmountText == value) return;
            cashAmountText = value;
            Changed();
            NotifyCollectedChanged();
        }
    }

    public string VodafoneAmountText
    {
        get => vodafoneAmountText;
        set
        {
            if (vodafoneAmountText == value) return;
            vodafoneAmountText = value;
            Changed();
            NotifyCollectedChanged();
        }
    }

    public string InstaPayAmountText
    {
        get => instaPayAmountText;
        set
        {
            if (instaPayAmountText == value) return;
            instaPayAmountText = value;
            Changed();
            NotifyCollectedChanged();
        }
    }

    public decimal ParsedCashPayment => ShowCashInput && PosFormatting.TryParseAmount(CashAmountText, out var v) && v >= 0m ? v : 0m;
    public decimal ParsedVodafonePayment => ShowVodafoneInput && PosFormatting.TryParseAmount(VodafoneAmountText, out var v) && v >= 0m ? v : 0m;
    public decimal ParsedInstaPayPayment => ShowInstaPayInput && PosFormatting.TryParseAmount(InstaPayAmountText, out var v) && v >= 0m ? v : 0m;
    public decimal CurrentFinalCollected => ParsedCashPayment + ParsedVodafonePayment + ParsedInstaPayPayment;
    public decimal StillUnpaidAfterInput => Math.Max(0m, RemainingBeforeFinalPayment - CurrentFinalCollected);

    public string CurrentFinalCollectedDisplay => PosFormatting.FormatCurrency(CurrentFinalCollected);
    public string StillUnpaidAfterInputDisplay => PosFormatting.FormatCurrency(StillUnpaidAfterInput);

    public int WarrantyDays
    {
        get => warrantyDays;
        private set
        {
            var clamped = Math.Max(0, value);
            if (warrantyDays == clamped) return;
            warrantyDays = clamped;
            Changed();
            Changed(nameof(WarrantyPreviewText));
        }
    }

    public string CustomWarrantyDaysText
    {
        get => customWarrantyDaysText;
        set
        {
            if (customWarrantyDaysText == value) return;
            customWarrantyDaysText = value;
            Changed();
            if (PosFormatting.TryParseNonNegativeInt(value, out var days))
                WarrantyDays = days;
        }
    }

    public string WarrantyPreviewText => WarrantyDays == 0
        ? "تسليم بدون ضمان"
        : $"ضمان {WarrantyDays} يومًا حتى {DateTime.Today.AddDays(WarrantyDays):yyyy-MM-dd}";

    public string DeliveryNotes
    {
        get => deliveryNotes;
        set
        {
            if (deliveryNotes == value) return;
            deliveryNotes = value;
            Changed();
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (errorMessage == value) return;
            errorMessage = value;
            Changed();
            Changed(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public Action? RequestClose { get; set; }
    public ActionCommand SetNoPaymentModeCommand { get; }
    public ActionCommand SetCashModeCommand { get; }
    public ActionCommand SetVodafoneModeCommand { get; }
    public ActionCommand SetInstaPayModeCommand { get; }
    public ActionCommand SetSplitModeCommand { get; }
    public RelayCommand<int> SelectWarrantyDaysCommand { get; }
    public ActionCommand ConfirmDeliveryCommand { get; }
    public ActionCommand ConfirmAndPrintCommand { get; }
    public ActionCommand CancelCommand { get; }

    public MaintenanceDeliveryViewModel(MaintenanceOrderDesignItem order, string currentUserName)
    {
        Order = order;
        this.currentUserName = currentUserName;

        // If workshop pricing hasn't been broken down yet, carry over initial estimate as labor baseline.
        var initialLabor = order.HasWorkshopPricing
            ? order.LaborCost
            : (order.IsEstimatePendingInspection ? 0m : Math.Max(0m, order.EstimatedCost - order.PartsTotal));

        laborCostText = PosFormatting.FormatPlainNumber(initialLabor);
        discountText = PosFormatting.FormatPlainNumber(order.DiscountAmount);
        deliveryNotes = order.DeliveryNotes;
        warrantyDays = order.Priority == MaintenancePriority.WarrantyRecheck ? 14 : (order.WarrantyDays > 0 ? order.WarrantyDays : 14);
        customWarrantyDaysText = warrantyDays.ToString();

        paymentMode = RemainingBeforeFinalPayment > 0m
            ? MaintenanceDepositMode.CashOnly
            : MaintenanceDepositMode.NoDeposit;
        if (RemainingBeforeFinalPayment > 0m)
            cashAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);

        SetNoPaymentModeCommand = new ActionCommand(() => SetPaymentMode(MaintenanceDepositMode.NoDeposit));
        SetCashModeCommand = new ActionCommand(() => SetPaymentMode(MaintenanceDepositMode.CashOnly));
        SetVodafoneModeCommand = new ActionCommand(() => SetPaymentMode(MaintenanceDepositMode.VodafoneCashOnly));
        SetInstaPayModeCommand = new ActionCommand(() => SetPaymentMode(MaintenanceDepositMode.InstaPayOnly));
        SetSplitModeCommand = new ActionCommand(() => SetPaymentMode(MaintenanceDepositMode.Split));
        SelectWarrantyDaysCommand = new RelayCommand<int>(days =>
        {
            WarrantyDays = days;
            CustomWarrantyDaysText = days.ToString();
        });
        ConfirmDeliveryCommand = new ActionCommand(() => ConfirmDelivery(printAfter: false));
        ConfirmAndPrintCommand = new ActionCommand(() => ConfirmDelivery(printAfter: true));
        CancelCommand = new ActionCommand(() =>
        {
            IsConfirmed = false;
            RequestClose?.Invoke();
        });
    }

    private void SetPaymentMode(MaintenanceDepositMode nextMode)
    {
        paymentMode = nextMode;
        if (nextMode == MaintenanceDepositMode.NoDeposit)
        {
            cashAmountText = "0";
            vodafoneAmountText = "0";
            instaPayAmountText = "0";
        }
        else if (nextMode == MaintenanceDepositMode.CashOnly)
        {
            cashAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);
            vodafoneAmountText = "0";
            instaPayAmountText = "0";
        }
        else if (nextMode == MaintenanceDepositMode.VodafoneCashOnly)
        {
            cashAmountText = "0";
            vodafoneAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);
            instaPayAmountText = "0";
        }
        else if (nextMode == MaintenanceDepositMode.InstaPayOnly)
        {
            cashAmountText = "0";
            vodafoneAmountText = "0";
            instaPayAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);
        }

        Changed(nameof(CashAmountText));
        Changed(nameof(VodafoneAmountText));
        Changed(nameof(InstaPayAmountText));
        NotifyModeAndTotals();
    }

    private void SyncSuggestedPaymentAfterPricingChange()
    {
        if (!HasRemainingToCollect)
        {
            paymentMode = MaintenanceDepositMode.NoDeposit;
            cashAmountText = "0";
            vodafoneAmountText = "0";
            instaPayAmountText = "0";
        }
        else if (paymentMode == MaintenanceDepositMode.NoDeposit)
        {
            paymentMode = MaintenanceDepositMode.CashOnly;
            cashAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);
        }
        else if (paymentMode == MaintenanceDepositMode.CashOnly)
        {
            cashAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);
        }
        else if (paymentMode == MaintenanceDepositMode.VodafoneCashOnly)
        {
            vodafoneAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);
        }
        else if (paymentMode == MaintenanceDepositMode.InstaPayOnly)
        {
            instaPayAmountText = PosFormatting.FormatPlainNumber(RemainingBeforeFinalPayment);
        }

        Changed(nameof(CashAmountText));
        Changed(nameof(VodafoneAmountText));
        Changed(nameof(InstaPayAmountText));
        NotifyModeAndTotals();
    }

    private void NotifyModeAndTotals()
    {
        Changed(nameof(IsNoFinalPaymentMode));
        Changed(nameof(IsCashMode));
        Changed(nameof(IsVodafoneMode));
        Changed(nameof(IsInstaPayMode));
        Changed(nameof(IsSplitMode));
        Changed(nameof(ShowPaymentInputs));
        Changed(nameof(ShowCashInput));
        Changed(nameof(ShowVodafoneInput));
        Changed(nameof(ShowInstaPayInput));
        NotifyCollectedChanged();
    }

    private void NotifyCollectedChanged()
    {
        Changed(nameof(ParsedLaborCost));
        Changed(nameof(ParsedDiscount));
        Changed(nameof(EffectiveFinalTotal));
        Changed(nameof(RemainingBeforeFinalPayment));
        Changed(nameof(ExcessDepositOverTotal));
        Changed(nameof(HasRemainingToCollect));
        Changed(nameof(HasExcessDeposit));
        Changed(nameof(EffectiveFinalTotalDisplay));
        Changed(nameof(RemainingBeforeFinalPaymentDisplay));
        Changed(nameof(ExcessDepositOverTotalDisplay));
        Changed(nameof(ExcessDepositSummary));
        Changed(nameof(CurrentFinalCollected));
        Changed(nameof(StillUnpaidAfterInput));
        Changed(nameof(CurrentFinalCollectedDisplay));
        Changed(nameof(StillUnpaidAfterInputDisplay));
    }

    private void ConfirmDelivery(bool printAfter)
    {
        ErrorMessage = "";

        if (!PosFormatting.TryParseAmount(LaborCostText, out var labor) || labor < 0m)
        {
            ErrorMessage = "أدخل قيمة صحيحة لأجرة الصيانة والمصنعية.";
            return;
        }

        if (!PosFormatting.TryParseAmount(DiscountText, out var discount) || discount < 0m)
        {
            ErrorMessage = "أدخل قيمة صحيحة للخصم.";
            return;
        }

        if (discount > Order.PartsTotal + labor)
        {
            ErrorMessage = "الخصم لا يمكن أن يتجاوز مجموع قطع الغيار والمصنعية.";
            return;
        }

        decimal cash = 0m, vodafone = 0m, instaPay = 0m;
        if (HasRemainingToCollect && paymentMode != MaintenanceDepositMode.NoDeposit)
        {
            if (ShowCashInput && (!PosFormatting.TryParseAmount(CashAmountText, out cash) || cash < 0m))
            {
                ErrorMessage = "أدخل مبلغًا صحيحًا للسداد النقدي.";
                return;
            }
            if (ShowVodafoneInput && (!PosFormatting.TryParseAmount(VodafoneAmountText, out vodafone) || vodafone < 0m))
            {
                ErrorMessage = "أدخل مبلغًا صحيحًا لفودافون كاش.";
                return;
            }
            if (ShowInstaPayInput && (!PosFormatting.TryParseAmount(InstaPayAmountText, out instaPay) || instaPay < 0m))
            {
                ErrorMessage = "أدخل مبلغًا صحيحًا لإنستاباي.";
                return;
            }

            var totalEntered = cash + vodafone + instaPay;
            if (totalEntered > RemainingBeforeFinalPayment)
            {
                ErrorMessage = $"إجمالي التحصيل ({PosFormatting.FormatCurrency(totalEntered)}) يتجاوز المتبقي المستحق ({RemainingBeforeFinalPaymentDisplay}).";
                return;
            }
        }

        if (!PosFormatting.TryParseNonNegativeInt(CustomWarrantyDaysText, out var wDays))
        {
            ErrorMessage = "أدخل عدد أيام ضمان صحيح (0 أو أكثر).";
            return;
        }

        Order.LaborCost = labor;
        Order.DiscountAmount = discount;
        Order.RecalculateWorkshopTotal();

        Order.FinalPaymentCashPaid += cash;
        Order.FinalPaymentVodafonePaid += vodafone;
        Order.FinalPaymentInstaPayPaid += instaPay;

        if (HasExcessDeposit && RefundExcessDeposit)
            Order.RefundedDepositAmount += ExcessDepositOverTotal;

        Order.WarrantyDays = wDays;
        Order.DeliveredAt = DateTime.Now;
        Order.DeliveryNotes = DeliveryNotes.Trim();
        Order.Status = MaintenanceOrderStatus.Delivered;

        Order.NotifyFinancialsChanged();
        IsConfirmed = true;
        ShouldOpenPrintAfterClose = printAfter;
        RequestClose?.Invoke();
    }
}

public sealed class MaintenanceCancelOrderViewModel : ObservableObject
{
    private string selectedReason;
    private string customReasonNotes = "";
    private bool returnUsedPartsToInventory = true;
    private bool refundDeposit = true;
    private string refundAmountText;
    private string errorMessage = "";

    public MaintenanceOrderDesignItem Order { get; }
    public IReadOnlyList<string> Reasons => MaintenanceDesignSeedData.CancellationReasons;

    public bool HasUsedParts => Order.HasUsedParts;
    public int UsedPartsCount => Order.UsedPartsTotalCount;
    public bool HasRetainedDeposit => Order.NetDepositRetained > 0m;
    public string RetainedDepositDisplay => Order.NetDepositRetainedDisplay;

    public string SelectedReason
    {
        get => selectedReason;
        set
        {
            if (selectedReason == value) return;
            selectedReason = value;
            Changed();
        }
    }

    public string CustomReasonNotes
    {
        get => customReasonNotes;
        set
        {
            if (customReasonNotes == value) return;
            customReasonNotes = value;
            Changed();
        }
    }

    public bool ReturnUsedPartsToInventory
    {
        get => returnUsedPartsToInventory;
        set
        {
            if (returnUsedPartsToInventory == value) return;
            returnUsedPartsToInventory = value;
            Changed();
        }
    }

    public bool RefundDeposit
    {
        get => refundDeposit;
        set
        {
            if (refundDeposit == value) return;
            refundDeposit = value;
            Changed();
        }
    }

    public string RefundAmountText
    {
        get => refundAmountText;
        set
        {
            if (refundAmountText == value) return;
            refundAmountText = value;
            Changed();
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (errorMessage == value) return;
            errorMessage = value;
            Changed();
            Changed(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsConfirmed { get; private set; }
    public bool ShouldReturnParts { get; private set; }
    public decimal ConfirmedRefundAmount { get; private set; }
    public string FinalReasonText { get; private set; } = "";

    public Action? RequestClose { get; set; }
    public RelayCommand<string> SelectQuickReasonCommand { get; }
    public ActionCommand ConfirmCancelCommand { get; }
    public ActionCommand AbortCommand { get; }

    public MaintenanceCancelOrderViewModel(MaintenanceOrderDesignItem order)
    {
        Order = order;
        selectedReason = Reasons[0];
        returnUsedPartsToInventory = order.HasUsedParts;
        refundDeposit = order.NetDepositRetained > 0m;
        refundAmountText = PosFormatting.FormatPlainNumber(order.NetDepositRetained);

        SelectQuickReasonCommand = new RelayCommand<string>(r =>
        {
            if (!string.IsNullOrWhiteSpace(r))
                SelectedReason = r;
        });
        ConfirmCancelCommand = new ActionCommand(ConfirmCancel);
        AbortCommand = new ActionCommand(() =>
        {
            IsConfirmed = false;
            RequestClose?.Invoke();
        });
    }

    private void ConfirmCancel()
    {
        ErrorMessage = "";
        decimal refundAmount = 0m;
        if (HasRetainedDeposit && RefundDeposit)
        {
            if (!PosFormatting.TryParseAmount(RefundAmountText, out refundAmount) || refundAmount < 0m)
            {
                ErrorMessage = "أدخل مبلغ استرداد عربون صحيح.";
                return;
            }
            if (refundAmount > Order.NetDepositRetained)
            {
                ErrorMessage = $"مبلغ الاسترداد لا يمكن أن يتجاوز صافي العربون ({RetainedDepositDisplay}).";
                return;
            }
        }

        var fullReason = string.IsNullOrWhiteSpace(CustomReasonNotes)
            ? SelectedReason
            : $"{SelectedReason} — {CustomReasonNotes.Trim()}";

        if (string.IsNullOrWhiteSpace(fullReason))
        {
            ErrorMessage = "اختر أو اكتب سبب الإلغاء / رفض الإصلاح.";
            return;
        }

        IsConfirmed = true;
        ShouldReturnParts = ReturnUsedPartsToInventory;
        ConfirmedRefundAmount = refundAmount;
        FinalReasonText = fullReason;
        RequestClose?.Invoke();
    }
}
