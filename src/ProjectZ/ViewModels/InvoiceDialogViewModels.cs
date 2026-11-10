using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class InvoicePrintPreviewViewModel : ObservableObject
{
    private bool isA4Mode;
    private string statusMessage = "";

    public SalesInvoiceDesignItem Invoice { get; }

    public bool IsThermalMode
    {
        get => !isA4Mode;
        set
        {
            if (isA4Mode == !value) return;
            isA4Mode = !value;
            Changed();
            Changed(nameof(IsA4Mode));
            Changed(nameof(CurrentModeLabel));
        }
    }

    public bool IsA4Mode
    {
        get => isA4Mode;
        set
        {
            if (isA4Mode == value) return;
            isA4Mode = value;
            Changed();
            Changed(nameof(IsThermalMode));
            Changed(nameof(CurrentModeLabel));
        }
    }

    public string CurrentModeLabel => IsA4Mode ? "فاتورة A4 رسمية" : "إيصال حراري 80 مم";

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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public Action? RequestClose { get; set; }
    public ActionCommand SelectThermalModeCommand { get; }
    public ActionCommand SelectA4ModeCommand { get; }
    public ActionCommand PrintCommand { get; }
    public ActionCommand CloseCommand { get; }

    public InvoicePrintPreviewViewModel(SalesInvoiceDesignItem invoice, Action<string> onPrintPreview)
    {
        Invoice = invoice;
        SelectThermalModeCommand = new ActionCommand(() => IsThermalMode = true);
        SelectA4ModeCommand = new ActionCommand(() => IsA4Mode = true);
        PrintCommand = new ActionCommand(() =>
        {
            var msg = $"وضع التصميم (بدون طابعة Backend): تمت معاينة طباعة الفاتورة {Invoice.InvoiceNumber} بصيغة «{CurrentModeLabel}».";
            StatusMessage = msg;
            onPrintPreview(CurrentModeLabel);
        });
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }
}

public sealed record InvoiceReturnLineRequest(SalesInvoiceLineDesignItem Line, int ReturnQuantity);

public sealed record InvoiceReturnSubmission(
    IReadOnlyList<InvoiceReturnLineRequest> Lines,
    string Reason,
    bool ReturnAsAvailableStock,
    bool RefundCashToCustomer);

public sealed class InvoiceReturnLineViewModel : ObservableObject
{
    private readonly Action onChanged;
    private int returnQuantity;

    public SalesInvoiceLineDesignItem Line { get; }
    public int MaxReturnable => Line.RemainingQuantity;
    public string MaxReturnableDisplay => $"متاح للإرجاع: {MaxReturnable}";

    public int ReturnQuantity
    {
        get => returnQuantity;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxReturnable);
            if (returnQuantity == clamped) return;
            returnQuantity = clamped;
            Changed();
            Changed(nameof(ReturnQuantityDisplay));
            Changed(nameof(LineRefundAmount));
            Changed(nameof(LineRefundAmountDisplay));
            onChanged();
        }
    }

    public string ReturnQuantityDisplay => ReturnQuantity.ToString(CultureInfo.InvariantCulture);
    public decimal LineRefundAmount => ReturnQuantity * Line.UnitPrice;
    public string LineRefundAmountDisplay => PosFormatting.FormatCurrency(LineRefundAmount);

    public ActionCommand IncrementCommand { get; }
    public ActionCommand DecrementCommand { get; }
    public ActionCommand ReturnAllLineCommand { get; }

    public InvoiceReturnLineViewModel(SalesInvoiceLineDesignItem line, Action onChanged)
    {
        Line = line;
        this.onChanged = onChanged;
        IncrementCommand = new ActionCommand(() => ReturnQuantity++);
        DecrementCommand = new ActionCommand(() => ReturnQuantity--);
        ReturnAllLineCommand = new ActionCommand(() => ReturnQuantity = MaxReturnable);
    }
}

public sealed class InvoiceReturnViewModel : ObservableObject
{
    private string reason = "";
    private bool returnAsAvailableStock = true;
    private bool refundCashToCustomer = true;
    private string error = "";

    public SalesInvoiceDesignItem Invoice { get; }
    public ObservableCollection<InvoiceReturnLineViewModel> ReturnLines { get; } = [];
    public IReadOnlyList<string> SuggestedReasons { get; } =
    [
        "تبديل موديل",
        "عيب صناعة",
        "عدم ملاءمة الجهاز",
        "تراجع العميل"
    ];

    public string Reason
    {
        get => reason;
        set { reason = value; Changed(); Error = ""; }
    }

    public bool ReturnAsAvailableStock
    {
        get => returnAsAvailableStock;
        set
        {
            if (returnAsAvailableStock == value) return;
            returnAsAvailableStock = value;
            Changed();
            Changed(nameof(MarkAsDamagedStock));
        }
    }

    public bool MarkAsDamagedStock
    {
        get => !returnAsAvailableStock;
        set => ReturnAsAvailableStock = !value;
    }

    public bool RefundCashToCustomer
    {
        get => refundCashToCustomer;
        set
        {
            if (refundCashToCustomer == value) return;
            refundCashToCustomer = value;
            Changed();
        }
    }

    public int TotalReturnUnits => ReturnLines.Sum(l => l.ReturnQuantity);
    public decimal EstimatedReturnTotal => ReturnLines.Sum(l => l.LineRefundAmount);
    public string EstimatedReturnTotalDisplay => PosFormatting.FormatCurrency(EstimatedReturnTotal);
    public string SummaryDisplay => $"إجمالي القطع المرتجعة: {TotalReturnUnits} قطعة · القيمة: {EstimatedReturnTotalDisplay}";

    public string Error
    {
        get => error;
        private set { error = value; Changed(); }
    }

    public Action<bool>? RequestCloseWithResult { get; set; }
    public RelayCommand<string> PickReasonCommand { get; }
    public ActionCommand ReturnAllInvoiceCommand { get; }
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public InvoiceReturnViewModel(SalesInvoiceDesignItem invoice, Action<InvoiceReturnSubmission> onConfirm)
    {
        Invoice = invoice;
        refundCashToCustomer = !invoice.HasRemaining;

        foreach (var line in invoice.Items.Where(i => i.RemainingQuantity > 0))
            ReturnLines.Add(new InvoiceReturnLineViewModel(line, NotifyTotals));

        PickReasonCommand = new RelayCommand<string>(r =>
        {
            if (!string.IsNullOrWhiteSpace(r))
                Reason = r;
        });

        ReturnAllInvoiceCommand = new ActionCommand(() =>
        {
            foreach (var l in ReturnLines)
                l.ReturnQuantity = l.MaxReturnable;
        });

        ConfirmCommand = new ActionCommand(() =>
        {
            var selected = ReturnLines
                .Where(l => l.ReturnQuantity > 0)
                .Select(l => new InvoiceReturnLineRequest(l.Line, l.ReturnQuantity))
                .ToList();

            if (selected.Count == 0)
            {
                Error = "حدد كمية واحدة على الأقل للإرجاع من بنود الفاتورة.";
                return;
            }

            var trimmedReason = Reason.Trim();
            if (string.IsNullOrWhiteSpace(trimmedReason))
            {
                Error = "يرجى تحديد أو كتابة سبب المرتجع.";
                return;
            }

            onConfirm(new InvoiceReturnSubmission(selected, trimmedReason, ReturnAsAvailableStock, RefundCashToCustomer));
            RequestCloseWithResult?.Invoke(true);
        });

        CancelCommand = new ActionCommand(() => RequestCloseWithResult?.Invoke(false));
    }

    private void NotifyTotals()
    {
        Changed(nameof(TotalReturnUnits));
        Changed(nameof(EstimatedReturnTotal));
        Changed(nameof(EstimatedReturnTotalDisplay));
        Changed(nameof(SummaryDisplay));
        Error = "";
    }
}

public sealed record CollectPaymentSubmission(
    decimal CashAmount,
    decimal VodafoneAmount,
    string VodafoneReference,
    decimal InstaPayAmount,
    string InstaPayReference,
    string Note);

public sealed class CollectInvoicePaymentViewModel : ObservableObject
{
    private string cashAmountText;
    private string vodafoneAmountText = "";
    private string vodafoneReference = "";
    private string instaPayAmountText = "";
    private string instaPayReference = "";
    private string note = "";
    private string error = "";

    public SalesInvoiceDesignItem Invoice { get; }

    public string CashAmountText
    {
        get => cashAmountText;
        set { cashAmountText = value; Changed(); NotifySummary(); }
    }

    public string VodafoneAmountText
    {
        get => vodafoneAmountText;
        set { vodafoneAmountText = value; Changed(); NotifySummary(); }
    }

    public string VodafoneReference
    {
        get => vodafoneReference;
        set { vodafoneReference = value; Changed(); Error = ""; }
    }

    public string InstaPayAmountText
    {
        get => instaPayAmountText;
        set { instaPayAmountText = value; Changed(); NotifySummary(); }
    }

    public string InstaPayReference
    {
        get => instaPayReference;
        set { instaPayReference = value; Changed(); Error = ""; }
    }

    public string Note
    {
        get => note;
        set { note = value; Changed(); }
    }

    public string Error
    {
        get => error;
        private set { error = value; Changed(); }
    }

    private decimal ParsedCash => PosFormatting.TryParseAmount(CashAmountText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedVodafone => PosFormatting.TryParseAmount(VodafoneAmountText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedInstaPay => PosFormatting.TryParseAmount(InstaPayAmountText, out var v) ? Math.Max(0m, v) : 0m;

    public decimal PaymentTotal => ParsedCash + ParsedVodafone + ParsedInstaPay;
    public decimal RemainingAfter => Math.Max(0m, Invoice.RemainingAmount - PaymentTotal);
    public string PaymentTotalDisplay => PosFormatting.FormatCurrency(PaymentTotal);
    public string RemainingAfterDisplay => PosFormatting.FormatCurrency(RemainingAfter);

    public Action<bool>? RequestCloseWithResult { get; set; }
    public ActionCommand FillFullCashCommand { get; }
    public ActionCommand FillFullVodafoneCommand { get; }
    public ActionCommand FillFullInstaPayCommand { get; }
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public CollectInvoicePaymentViewModel(SalesInvoiceDesignItem invoice, Action<CollectPaymentSubmission> onConfirm)
    {
        Invoice = invoice;
        cashAmountText = PosFormatting.FormatPlainNumber(invoice.RemainingAmount);

        FillFullCashCommand = new ActionCommand(() =>
        {
            CashAmountText = PosFormatting.FormatPlainNumber(Invoice.RemainingAmount);
            VodafoneAmountText = "";
            InstaPayAmountText = "";
        });

        FillFullVodafoneCommand = new ActionCommand(() =>
        {
            CashAmountText = "";
            VodafoneAmountText = PosFormatting.FormatPlainNumber(Invoice.RemainingAmount);
            InstaPayAmountText = "";
        });

        FillFullInstaPayCommand = new ActionCommand(() =>
        {
            CashAmountText = "";
            VodafoneAmountText = "";
            InstaPayAmountText = PosFormatting.FormatPlainNumber(Invoice.RemainingAmount);
        });

        ConfirmCommand = new ActionCommand(() =>
        {
            if (PaymentTotal <= 0m)
            {
                Error = "أدخل مبلغ تحصيل أكبر من صفر.";
                return;
            }

            if (PaymentTotal > Invoice.RemainingAmount)
            {
                Error = $"مبلغ التحصيل ({PaymentTotalDisplay}) أكبر من المتبقي على الفاتورة ({Invoice.RemainingAmountDisplay}).";
                return;
            }

            onConfirm(new CollectPaymentSubmission(
                ParsedCash,
                ParsedVodafone,
                VodafoneReference.Trim(),
                ParsedInstaPay,
                InstaPayReference.Trim(),
                Note.Trim()));
            RequestCloseWithResult?.Invoke(true);
        });

        CancelCommand = new ActionCommand(() => RequestCloseWithResult?.Invoke(false));
    }

    private void NotifySummary()
    {
        Changed(nameof(PaymentTotal));
        Changed(nameof(RemainingAfter));
        Changed(nameof(PaymentTotalDisplay));
        Changed(nameof(RemainingAfterDisplay));
        Error = "";
    }
}
