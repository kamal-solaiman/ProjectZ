using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PurchaseInvoiceLineStatusRow
{
    public int LineNumber { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string Category { get; init; } = "";
    public string Unit { get; init; } = "قطعة";
    public int Quantity { get; init; }
    public int ReturnedQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public int AvailableCount { get; init; }
    public int SoldCount { get; init; }
    public int DamagedCount { get; init; }
    public decimal UnitPurchaseCost { get; init; }
    public decimal LineTotal { get; init; }
    public decimal NetActiveLineTotal { get; init; }
    public string SerialSummary { get; init; } = "";

    public bool HasReturnedQuantity => ReturnedQuantity > 0;
    public bool HasSoldUnits => SoldCount > 0;
    public bool HasDamagedUnits => DamagedCount > 0;

    public string QuantityDisplay => Quantity.ToString(CultureInfo.InvariantCulture);
    public string ReturnedQuantityDisplay => ReturnedQuantity.ToString(CultureInfo.InvariantCulture);
    public string RemainingQuantityDisplay => RemainingQuantity.ToString(CultureInfo.InvariantCulture);
    public string UnitPurchaseCostDisplay => PosFormatting.FormatCurrency(UnitPurchaseCost);
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);
    public string NetActiveLineTotalDisplay => PosFormatting.FormatCurrency(NetActiveLineTotal);

    public string StockStatusSummary
    {
        get
        {
            if (RemainingQuantity == 0)
                return "مسحوب / مرتجع بالكامل";

            var parts = new List<string>(3) { $"متاح: {AvailableCount}" };
            if (SoldCount > 0) parts.Add($"مباع: {SoldCount}");
            if (DamagedCount > 0) parts.Add($"هالك: {DamagedCount}");
            return string.Join(" · ", parts);
        }
    }
}

public sealed class PurchaseInvoiceDetailsViewModel : ObservableObject
{
    private readonly ObservableCollection<SupplierDesignItem> sharedSuppliers;
    private readonly ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations;
    private readonly Action<PurchaseInvoiceDesignItem> onPrint;
    private readonly Action<PurchaseInvoiceDesignItem> onPayRemaining;
    private readonly Action<PurchaseInvoiceDesignItem> onEdit;
    private readonly Action<PurchaseInvoiceDesignItem> onReturn;
    private readonly Action<PurchaseInvoiceDesignItem> onCancel;
    private readonly Func<PurchaseInvoiceDesignItem, bool> onDelete;

    private string statusMessage = "";
    private bool isErrorStatus;
    private int soldUnitsCount;
    private int availableUnitsCount;
    private int damagedUnitsCount;

    public PurchaseInvoiceDesignItem Invoice { get; }
    public ObservableCollection<PurchaseInvoiceLineStatusRow> LineRows { get; } = [];

    public int SoldUnitsCount
    {
        get => soldUnitsCount;
        private set
        {
            if (soldUnitsCount == value) return;
            soldUnitsCount = value;
            Changed();
            Changed(nameof(HasSoldUnits));
            Changed(nameof(CanDeleteInvoice));
            Changed(nameof(SoldProtectionBannerText));
        }
    }

    public int AvailableUnitsCount
    {
        get => availableUnitsCount;
        private set
        {
            if (availableUnitsCount == value) return;
            availableUnitsCount = value;
            Changed();
        }
    }

    public int DamagedUnitsCount
    {
        get => damagedUnitsCount;
        private set
        {
            if (damagedUnitsCount == value) return;
            damagedUnitsCount = value;
            Changed();
        }
    }

    public bool HasSoldUnits => SoldUnitsCount > 0;
    public bool CanDeleteInvoice => SoldUnitsCount == 0;
    public string SoldProtectionBannerText =>
        $"تنبيه حماية المخزون: توجد {SoldUnitsCount} قطعة مباعة للعملاء من هذه الفاتورة (متاح: {AvailableUnitsCount} · هالك: {DamagedUnitsCount}). لا يمكن الحذف النهائي أو تقليص الكمية أقل من القطع المباعة.";

    public string SupplierOutstandingDisplay
    {
        get
        {
            var sup = sharedSuppliers.FirstOrDefault(s => s.Id == Invoice.SupplierId || s.Name == Invoice.SupplierName);
            return sup is not null ? sup.OutstandingPayableDisplay : PosFormatting.FormatCurrency(Invoice.RemainingPayable);
        }
    }

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

    public Action? RequestClose { get; set; }

    public ActionCommand PrintPreviewCommand { get; }
    public ActionCommand PayRemainingCommand { get; }
    public ActionCommand EditInvoiceCommand { get; }
    public ActionCommand ReturnToSupplierCommand { get; }
    public ActionCommand CancelInvoiceCommand { get; }
    public ActionCommand DeleteInvoiceCommand { get; }
    public ActionCommand CloseCommand { get; }

    public PurchaseInvoiceDetailsViewModel(
        PurchaseInvoiceDesignItem invoice,
        ObservableCollection<SupplierDesignItem> sharedSuppliers,
        ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations,
        Action<PurchaseInvoiceDesignItem> onPrint,
        Action<PurchaseInvoiceDesignItem> onPayRemaining,
        Action<PurchaseInvoiceDesignItem> onEdit,
        Action<PurchaseInvoiceDesignItem> onReturn,
        Action<PurchaseInvoiceDesignItem> onCancel,
        Func<PurchaseInvoiceDesignItem, bool> onDelete)
    {
        Invoice = invoice;
        this.sharedSuppliers = sharedSuppliers;
        this.sharedRegistrations = sharedRegistrations;
        this.onPrint = onPrint;
        this.onPayRemaining = onPayRemaining;
        this.onEdit = onEdit;
        this.onReturn = onReturn;
        this.onCancel = onCancel;
        this.onDelete = onDelete;

        PrintPreviewCommand = new ActionCommand(() =>
        {
            this.onPrint(Invoice);
            RefreshDetails();
        });

        PayRemainingCommand = new ActionCommand(() =>
        {
            if (!Invoice.CanPayRemaining)
            {
                SetStatus("لا يوجد رصيد آجل متبقٍ للسداد على هذه الفاتورة.", true);
                return;
            }
            this.onPayRemaining(Invoice);
            RefreshDetails();
        });

        EditInvoiceCommand = new ActionCommand(() =>
        {
            if (!Invoice.CanEdit)
            {
                SetStatus("لا يمكن تعديل فاتورة ملغاة أو مرتجعة بالكامل.", true);
                return;
            }
            this.onEdit(Invoice);
            RefreshDetails();
        });

        ReturnToSupplierCommand = new ActionCommand(() =>
        {
            if (!Invoice.CanReturn)
            {
                SetStatus("لا توجد أصناف متبقية قابلة للإرجاع في هذه الفاتورة.", true);
                return;
            }
            this.onReturn(Invoice);
            RefreshDetails();
        });

        CancelInvoiceCommand = new ActionCommand(() =>
        {
            if (!Invoice.CanCancel)
            {
                SetStatus("الفاتورة ملغاة أو مرتجعة بالكامل بالفعل.", true);
                return;
            }
            this.onCancel(Invoice);
            RefreshDetails();
        });

        DeleteInvoiceCommand = new ActionCommand(() =>
        {
            if (SoldUnitsCount > 0)
            {
                SetStatus($"لا يمكن حذف الفاتورة نهائيًا لوجود {SoldUnitsCount} قطعة مباعة للعملاء منها. استخدم «مرتجع للمورد» للقطع غير المباعة.", true);
                return;
            }

            if (this.onDelete(Invoice))
                RequestClose?.Invoke();
        });

        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());

        RefreshDetails();
    }

    public void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }

    public void RefreshDetails()
    {
        LineRows.Clear();
        var totalSold = 0;
        var totalAvailable = 0;
        var totalDamaged = 0;

        foreach (var line in Invoice.Items)
        {
            var regs = sharedRegistrations.Where(r => line.UnitRegistrationIds.Contains(r.Id)).ToList();
            var sold = regs.Count(r => r.IsSold);
            var available = regs.Count(r => r.IsAvailable);
            var damaged = regs.Count(r => r.IsDamaged);

            totalSold += sold;
            totalAvailable += available;
            totalDamaged += damaged;

            LineRows.Add(new PurchaseInvoiceLineStatusRow
            {
                LineNumber = line.LineNumber,
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                Barcode = line.Barcode,
                Category = line.Category,
                Unit = line.Unit,
                Quantity = line.Quantity,
                ReturnedQuantity = line.ReturnedQuantity,
                RemainingQuantity = line.RemainingQuantity,
                AvailableCount = available,
                SoldCount = sold,
                DamagedCount = damaged,
                UnitPurchaseCost = line.UnitPurchaseCost,
                LineTotal = line.LineTotal,
                NetActiveLineTotal = line.NetActiveLineTotal,
                SerialSummary = line.SerialSummary
            });
        }

        SoldUnitsCount = totalSold;
        AvailableUnitsCount = totalAvailable;
        DamagedUnitsCount = totalDamaged;
        Changed(nameof(SupplierOutstandingDisplay));
        Changed(nameof(Invoice));
    }
}

public sealed record PaySupplierSubmission(
    decimal CashPaid,
    decimal VodafoneCashPaid,
    string VodafoneCashReference,
    decimal InstaPayPaid,
    string InstaPayReference,
    string Notes);

public sealed class PaySupplierInvoiceViewModel : ObservableObject
{
    private string cashAmountText = "";
    private string vodafoneCashAmountText = "";
    private string vodafoneCashReference = "";
    private string instaPayAmountText = "";
    private string instaPayReference = "";
    private string notes = "";
    private string errorMessage = "";

    public PurchaseInvoiceDesignItem Invoice { get; }
    public decimal OriginalRemaining { get; }

    public string CashAmountText
    {
        get => cashAmountText;
        set
        {
            if (cashAmountText == value) return;
            cashAmountText = value;
            Changed();
            NotifyCalculations();
        }
    }

    public string VodafoneCashAmountText
    {
        get => vodafoneCashAmountText;
        set
        {
            if (vodafoneCashAmountText == value) return;
            vodafoneCashAmountText = value;
            Changed();
            NotifyCalculations();
        }
    }

    public string VodafoneCashReference
    {
        get => vodafoneCashReference;
        set
        {
            if (vodafoneCashReference == value) return;
            vodafoneCashReference = value;
            Changed();
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
            NotifyCalculations();
        }
    }

    public string InstaPayReference
    {
        get => instaPayReference;
        set
        {
            if (instaPayReference == value) return;
            instaPayReference = value;
            Changed();
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (notes == value) return;
            notes = value;
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

    public decimal CashAmount => PosFormatting.TryParseAmount(CashAmountText, out var cash) && cash >= 0m ? cash : 0m;
    public decimal VodafoneCashAmount => PosFormatting.TryParseAmount(VodafoneCashAmountText, out var vf) && vf >= 0m ? vf : 0m;
    public decimal InstaPayAmount => PosFormatting.TryParseAmount(InstaPayAmountText, out var ip) && ip >= 0m ? ip : 0m;
    public decimal TotalPayNow => CashAmount + VodafoneCashAmount + InstaPayAmount;
    public bool HasOverpayment => TotalPayNow > OriginalRemaining;
    public decimal RemainingAfterPayment => Math.Max(0m, OriginalRemaining - TotalPayNow);

    public string OriginalRemainingDisplay => PosFormatting.FormatCurrency(OriginalRemaining);
    public string TotalPayNowDisplay => PosFormatting.FormatCurrency(TotalPayNow);
    public string RemainingAfterPaymentDisplay => PosFormatting.FormatCurrency(RemainingAfterPayment);

    public Action? RequestCloseWithSuccess { get; set; }
    public Action? RequestCloseCancel { get; set; }
    public PaySupplierSubmission? Result { get; private set; }

    public ActionCommand FillCashRemainingCommand { get; }
    public ActionCommand FillVodafoneRemainingCommand { get; }
    public ActionCommand FillInstaPayRemainingCommand { get; }
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public PaySupplierInvoiceViewModel(PurchaseInvoiceDesignItem invoice)
    {
        Invoice = invoice;
        OriginalRemaining = invoice.RemainingPayable;
        cashAmountText = OriginalRemaining > 0m ? OriginalRemaining.ToString("0.##", CultureInfo.InvariantCulture) : "0";

        FillCashRemainingCommand = new ActionCommand(() =>
        {
            CashAmountText = OriginalRemaining.ToString("0.##", CultureInfo.InvariantCulture);
            VodafoneCashAmountText = "";
            InstaPayAmountText = "";
        });

        FillVodafoneRemainingCommand = new ActionCommand(() =>
        {
            CashAmountText = "";
            VodafoneCashAmountText = OriginalRemaining.ToString("0.##", CultureInfo.InvariantCulture);
            InstaPayAmountText = "";
        });

        FillInstaPayRemainingCommand = new ActionCommand(() =>
        {
            CashAmountText = "";
            VodafoneCashAmountText = "";
            InstaPayAmountText = OriginalRemaining.ToString("0.##", CultureInfo.InvariantCulture);
        });

        ConfirmCommand = new ActionCommand(Submit);
        CancelCommand = new ActionCommand(() => RequestCloseCancel?.Invoke());
    }

    private void NotifyCalculations()
    {
        ErrorMessage = "";
        Changed(nameof(TotalPayNow));
        Changed(nameof(HasOverpayment));
        Changed(nameof(RemainingAfterPayment));
        Changed(nameof(TotalPayNowDisplay));
        Changed(nameof(RemainingAfterPaymentDisplay));
    }

    private void Submit()
    {
        if (TotalPayNow <= 0m)
        {
            ErrorMessage = "أدخل مبلغ سداد أكبر من صفر.";
            return;
        }

        if (HasOverpayment)
        {
            ErrorMessage = $"إجمالي السداد ({TotalPayNowDisplay}) يتجاوز المتبقي الآجل للمورد ({OriginalRemainingDisplay}).";
            return;
        }

        Result = new PaySupplierSubmission(
            CashAmount,
            VodafoneCashAmount,
            VodafoneCashReference.Trim(),
            InstaPayAmount,
            InstaPayReference.Trim(),
            Notes.Trim());
        RequestCloseWithSuccess?.Invoke();
    }
}

public sealed record PurchaseReturnLineRequest(PurchaseInvoiceLineDesignItem Line, int ReturnQuantity);

public sealed record PurchaseReturnSubmission(
    IReadOnlyList<PurchaseReturnLineRequest> Lines,
    string Reason,
    string Notes,
    bool PreferDamagedFirst,
    string RefundChannel,
    decimal CashRefundReceived);

public sealed class PurchaseReturnLineViewModel : ObservableObject
{
    private readonly Action onChanged;
    private int returnQuantity;
    private string returnQuantityText = "0";

    public PurchaseInvoiceLineDesignItem Line { get; }
    public int AvailableCount { get; }
    public int DamagedCount { get; }
    public int SoldCount { get; }
    public int MaxReturnable { get; }

    public int ReturnQuantity
    {
        get => returnQuantity;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxReturnable);
            if (returnQuantity == clamped && returnQuantityText == clamped.ToString(CultureInfo.InvariantCulture))
                return;

            returnQuantity = clamped;
            returnQuantityText = clamped.ToString(CultureInfo.InvariantCulture);
            Changed();
            Changed(nameof(ReturnQuantityText));
            Changed(nameof(LineReturnValue));
            Changed(nameof(LineReturnValueDisplay));
            onChanged();
        }
    }

    public string ReturnQuantityText
    {
        get => returnQuantityText;
        set
        {
            if (returnQuantityText == value) return;
            returnQuantityText = value;
            Changed();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                var clamped = Math.Clamp(parsed, 0, MaxReturnable);
                if (returnQuantity != clamped)
                {
                    returnQuantity = clamped;
                    Changed(nameof(ReturnQuantity));
                    Changed(nameof(LineReturnValue));
                    Changed(nameof(LineReturnValueDisplay));
                    onChanged();
                }
            }
        }
    }

    public decimal LineReturnValue => ReturnQuantity * Line.UnitPurchaseCost;
    public string LineReturnValueDisplay => PosFormatting.FormatCurrency(LineReturnValue);
    public string MaxReturnableDisplay => $"متاح للإرجاع: {MaxReturnable}";
    public string StockBreakdownDisplay
    {
        get
        {
            var parts = new List<string> { $"متاح: {AvailableCount}" };
            if (DamagedCount > 0) parts.Add($"هالك: {DamagedCount}");
            if (SoldCount > 0) parts.Add($"مباع (محمي): {SoldCount}");
            return string.Join(" · ", parts);
        }
    }

    public bool CanReturnAny => MaxReturnable > 0;

    public ActionCommand IncrementCommand { get; }
    public ActionCommand DecrementCommand { get; }
    public ActionCommand ReturnAllLineCommand { get; }

    public PurchaseReturnLineViewModel(
        PurchaseInvoiceLineDesignItem line,
        int availableCount,
        int damagedCount,
        int soldCount,
        Action onChanged)
    {
        Line = line;
        AvailableCount = availableCount;
        DamagedCount = damagedCount;
        SoldCount = soldCount;
        MaxReturnable = Math.Min(line.RemainingQuantity, availableCount + damagedCount);
        this.onChanged = onChanged;

        IncrementCommand = new ActionCommand(() => ReturnQuantity++);
        DecrementCommand = new ActionCommand(() => ReturnQuantity--);
        ReturnAllLineCommand = new ActionCommand(() => ReturnQuantity = MaxReturnable);
    }
}

public sealed class PurchaseReturnViewModel : ObservableObject
{
    private string reason = "تلف عند الاستلام / عطل مصنعي";
    private string notes = "";
    private bool preferDamagedFirst;
    private string refundChannel = "نقدي (كاش)";
    private string errorMessage = "";

    public PurchaseInvoiceDesignItem Invoice { get; }
    public ObservableCollection<PurchaseReturnLineViewModel> Lines { get; } = [];

    public IReadOnlyList<string> QuickReasons { get; } =
    [
        "تلف عند الاستلام / عطل مصنعي",
        "بضاعة زائدة عن الحاجة",
        "مواصفات غير مطابقة للطلب",
        "اتفاق تصفية مع المورد",
        "فرق سعر / استبدال دفعة"
    ];

    public IReadOnlyList<string> RefundChannels { get; } =
    [
        "نقدي (كاش)",
        "فودافون كاش",
        "إنستاباي",
        "يُترك كرصيد / مقاصة عند المورد (لاحقًا)"
    ];

    public string Reason
    {
        get => reason;
        set
        {
            if (reason == value) return;
            reason = value;
            ErrorMessage = "";
            Changed();
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (notes == value) return;
            notes = value;
            Changed();
        }
    }

    public bool PreferDamagedFirst
    {
        get => preferDamagedFirst;
        set
        {
            if (preferDamagedFirst == value) return;
            preferDamagedFirst = value;
            Changed();
        }
    }

    public string RefundChannel
    {
        get => refundChannel;
        set
        {
            if (refundChannel == value || value is null) return;
            refundChannel = value;
            Changed();
            Changed(nameof(ReceiveImmediateCashRefund));
            Changed(nameof(CashRefundToReceive));
            Changed(nameof(CashRefundToReceiveDisplay));
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

    public int TotalReturnUnits => Lines.Sum(l => l.ReturnQuantity);
    public decimal TotalReturnValue => Lines.Sum(l => l.LineReturnValue);
    public decimal AppliedToCredit => Math.Min(TotalReturnValue, Invoice.RemainingPayable);
    public decimal ExpectedRefundAboveCredit => Math.Max(0m, TotalReturnValue - AppliedToCredit);
    public bool NeedsSupplierRefundChoice => ExpectedRefundAboveCredit > 0m;
    public bool ReceiveImmediateCashRefund => !RefundChannel.Contains("لاحقًا", StringComparison.Ordinal);
    public decimal CashRefundToReceive => ReceiveImmediateCashRefund ? ExpectedRefundAboveCredit : 0m;
    public decimal NewRemainingPayable => Math.Max(0m, Invoice.RemainingPayable - AppliedToCredit);

    public string TotalReturnUnitsDisplay => $"{TotalReturnUnits} قطعة";
    public string TotalReturnValueDisplay => PosFormatting.FormatCurrency(TotalReturnValue);
    public string AppliedToCreditDisplay => PosFormatting.FormatCurrency(AppliedToCredit);
    public string ExpectedRefundAboveCreditDisplay => PosFormatting.FormatCurrency(ExpectedRefundAboveCredit);
    public string CashRefundToReceiveDisplay => PosFormatting.FormatCurrency(CashRefundToReceive);
    public string NewRemainingPayableDisplay => PosFormatting.FormatCurrency(NewRemainingPayable);

    public Action? RequestCloseWithSuccess { get; set; }
    public Action? RequestCloseCancel { get; set; }
    public PurchaseReturnSubmission? Result { get; private set; }

    public ActionCommand SelectAllReturnableCommand { get; }
    public ActionCommand ClearAllCommand { get; }
    public RelayCommand<string> PickReasonCommand { get; }
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public PurchaseReturnViewModel(
        PurchaseInvoiceDesignItem invoice,
        IReadOnlyList<InventoryUnitRegistrationItem> sharedRegistrations)
    {
        Invoice = invoice;
        var anyDamaged = false;

        foreach (var line in invoice.Items)
        {
            var regs = sharedRegistrations.Where(r => line.UnitRegistrationIds.Contains(r.Id)).ToList();
            var available = regs.Count(r => r.IsAvailable);
            var damaged = regs.Count(r => r.IsDamaged);
            var sold = regs.Count(r => r.IsSold);
            if (damaged > 0) anyDamaged = true;

            Lines.Add(new PurchaseReturnLineViewModel(line, available, damaged, sold, NotifyTotals));
        }

        preferDamagedFirst = anyDamaged;

        SelectAllReturnableCommand = new ActionCommand(() =>
        {
            foreach (var line in Lines)
                line.ReturnQuantity = line.MaxReturnable;
        });

        ClearAllCommand = new ActionCommand(() =>
        {
            foreach (var line in Lines)
                line.ReturnQuantity = 0;
        });

        PickReasonCommand = new RelayCommand<string>(r =>
        {
            if (!string.IsNullOrWhiteSpace(r))
                Reason = r;
        });

        ConfirmCommand = new ActionCommand(Submit);
        CancelCommand = new ActionCommand(() => RequestCloseCancel?.Invoke());
    }

    private void NotifyTotals()
    {
        ErrorMessage = "";
        Changed(nameof(TotalReturnUnits));
        Changed(nameof(TotalReturnValue));
        Changed(nameof(AppliedToCredit));
        Changed(nameof(ExpectedRefundAboveCredit));
        Changed(nameof(NeedsSupplierRefundChoice));
        Changed(nameof(CashRefundToReceive));
        Changed(nameof(NewRemainingPayable));
        Changed(nameof(TotalReturnUnitsDisplay));
        Changed(nameof(TotalReturnValueDisplay));
        Changed(nameof(AppliedToCreditDisplay));
        Changed(nameof(ExpectedRefundAboveCreditDisplay));
        Changed(nameof(CashRefundToReceiveDisplay));
        Changed(nameof(NewRemainingPayableDisplay));
    }

    private void Submit()
    {
        var selected = Lines
            .Where(l => l.ReturnQuantity > 0)
            .Select(l => new PurchaseReturnLineRequest(l.Line, l.ReturnQuantity))
            .ToList();

        if (selected.Count == 0)
        {
            ErrorMessage = "حدد كمية واحدة على الأقل لإرجاعها للمورد.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "يرجى تحديد سبب المرتجع للمورد.";
            return;
        }

        Result = new PurchaseReturnSubmission(
            selected,
            Reason.Trim(),
            Notes.Trim(),
            PreferDamagedFirst,
            RefundChannel,
            CashRefundToReceive);
        RequestCloseWithSuccess?.Invoke();
    }
}

public sealed class EditPurchaseLineItemViewModel : ObservableObject
{
    private readonly Action onChanged;
    private readonly Action<EditPurchaseLineItemViewModel> onRemove;
    private int quantity;
    private string quantityText;
    private decimal unitPurchaseCost;
    private string unitPurchaseCostText;

    public PurchaseInvoiceLineDesignItem? ExistingLine { get; }
    public PosProductDesignItem Product { get; }
    public int SoldCount { get; }
    public int DamagedCount { get; }
    public int ReturnedQuantity { get; }
    public int MinAllowedQuantity { get; }
    public bool CanRemove { get; }

    public int Quantity
    {
        get => quantity;
        set
        {
            var clamped = Math.Max(MinAllowedQuantity, value);
            var text = clamped.ToString(CultureInfo.InvariantCulture);
            if (quantity == clamped && quantityText == text) return;
            quantity = clamped;
            quantityText = text;
            Changed();
            Changed(nameof(QuantityText));
            Changed(nameof(LineTotal));
            Changed(nameof(NetActiveLineTotal));
            Changed(nameof(LineTotalDisplay));
            Changed(nameof(NetActiveLineTotalDisplay));
            onChanged();
        }
    }

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (quantityText == value) return;
            quantityText = value;
            Changed();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= MinAllowedQuantity)
            {
                if (quantity != parsed)
                {
                    quantity = parsed;
                    Changed(nameof(Quantity));
                    Changed(nameof(LineTotal));
                    Changed(nameof(NetActiveLineTotal));
                    Changed(nameof(LineTotalDisplay));
                    Changed(nameof(NetActiveLineTotalDisplay));
                    onChanged();
                }
            }
            else
            {
                onChanged();
            }
        }
    }

    public decimal UnitPurchaseCost
    {
        get => unitPurchaseCost;
        set
        {
            var clamped = Math.Max(0m, value);
            if (unitPurchaseCost == clamped) return;
            unitPurchaseCost = clamped;
            Changed();
            Changed(nameof(LineTotal));
            Changed(nameof(NetActiveLineTotal));
            Changed(nameof(LineTotalDisplay));
            Changed(nameof(NetActiveLineTotalDisplay));
            onChanged();
        }
    }

    public string UnitPurchaseCostText
    {
        get => unitPurchaseCostText;
        set
        {
            if (unitPurchaseCostText == value) return;
            unitPurchaseCostText = value;
            Changed();
            UnitPurchaseCost = PosFormatting.TryParseAmount(value, out var parsed) && parsed >= 0m ? parsed : 0m;
        }
    }

    public int RemainingActiveQuantity => Math.Max(0, Quantity - ReturnedQuantity);
    public decimal LineTotal => Quantity * UnitPurchaseCost;
    public decimal NetActiveLineTotal => RemainingActiveQuantity * UnitPurchaseCost;
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);
    public string NetActiveLineTotalDisplay => PosFormatting.FormatCurrency(NetActiveLineTotal);
    public bool HasReturnedQuantity => ReturnedQuantity > 0;
    public string ReturnedSummary => $"مرتجع: {ReturnedQuantity}";

    public string LockSummary
    {
        get
        {
            var parts = new List<string>();
            if (SoldCount > 0) parts.Add($"مباع: {SoldCount}");
            if (DamagedCount > 0) parts.Add($"هالك: {DamagedCount}");
            if (ReturnedQuantity > 0) parts.Add($"مرتجع: {ReturnedQuantity}");
            return parts.Count == 0
                ? "مرن (قابل للتعديل والحذف)"
                : $"الحد الأدنى {MinAllowedQuantity} ({string.Join(" · ", parts)})";
        }
    }

    public ActionCommand IncrementCommand { get; }
    public ActionCommand DecrementCommand { get; }
    public ActionCommand RemoveCommand { get; }

    public EditPurchaseLineItemViewModel(
        PosProductDesignItem product,
        PurchaseInvoiceLineDesignItem? existingLine,
        int quantity,
        decimal unitPurchaseCost,
        int returnedQuantity,
        int soldCount,
        int damagedCount,
        Action onChanged,
        Action<EditPurchaseLineItemViewModel> onRemove)
    {
        Product = product;
        ExistingLine = existingLine;
        ReturnedQuantity = returnedQuantity;
        SoldCount = soldCount;
        DamagedCount = damagedCount;
        MinAllowedQuantity = existingLine is null
            ? 1
            : Math.Max(1, returnedQuantity + soldCount + damagedCount);
        CanRemove = existingLine is null || (returnedQuantity == 0 && soldCount == 0 && damagedCount == 0);

        this.quantity = Math.Max(MinAllowedQuantity, quantity);
        quantityText = this.quantity.ToString(CultureInfo.InvariantCulture);
        this.unitPurchaseCost = unitPurchaseCost;
        unitPurchaseCostText = unitPurchaseCost.ToString("0.##", CultureInfo.InvariantCulture);

        this.onChanged = onChanged;
        this.onRemove = onRemove;

        IncrementCommand = new ActionCommand(() => Quantity++);
        DecrementCommand = new ActionCommand(() => Quantity--);
        RemoveCommand = new ActionCommand(() => this.onRemove(this));
    }
}

public sealed record EditPurchaseLineSubmission(
    PurchaseInvoiceLineDesignItem? ExistingLine,
    PosProductDesignItem Product,
    int NewQuantity,
    decimal NewUnitPurchaseCost);

public sealed record EditPurchaseSubmission(
    SupplierDesignItem Supplier,
    string SupplierInvoiceNumber,
    DateTime PurchaseDate,
    string Notes,
    decimal DiscountAmount,
    decimal CashPaid,
    decimal VodafoneCashPaid,
    string VodafoneCashReference,
    decimal InstaPayPaid,
    string InstaPayReference,
    IReadOnlyList<EditPurchaseLineSubmission> Lines);

public sealed class EditPurchaseInvoiceViewModel : ObservableObject
{
    private readonly IReadOnlyList<PosProductDesignItem> catalog;
    private readonly IReadOnlyList<SupplierDesignItem> suppliers;

    private SupplierDesignItem selectedSupplier;
    private string supplierSearchText = "";
    private bool isSupplierPickerOpen;
    private string supplierInvoiceNumber;
    private DateTime purchaseDate;
    private string notes;
    private string productSearchText = "";
    private string discountAmountText;
    private string cashAmountText;
    private string vodafoneCashAmountText;
    private string vodafoneCashReference;
    private string instaPayAmountText;
    private string instaPayReference;
    private string errorMessage = "";

    public PurchaseInvoiceDesignItem Invoice { get; }
    public ObservableCollection<EditPurchaseLineItemViewModel> Lines { get; } = [];
    public ObservableCollection<SupplierDesignItem> FilteredSuppliers { get; } = [];
    public ObservableCollection<PosProductDesignItem> FilteredCatalog { get; } = [];

    public SupplierDesignItem SelectedSupplier
    {
        get => selectedSupplier;
        private set
        {
            if (ReferenceEquals(selectedSupplier, value)) return;
            selectedSupplier = value;
            Changed();
        }
    }

    public string SupplierSearchText
    {
        get => supplierSearchText;
        set
        {
            if (supplierSearchText == value) return;
            supplierSearchText = value;
            Changed();
            RefreshFilteredSuppliers();
        }
    }

    public bool IsSupplierPickerOpen
    {
        get => isSupplierPickerOpen;
        set
        {
            if (isSupplierPickerOpen == value) return;
            isSupplierPickerOpen = value;
            if (isSupplierPickerOpen) RefreshFilteredSuppliers();
            Changed();
        }
    }

    public string SupplierInvoiceNumber
    {
        get => supplierInvoiceNumber;
        set
        {
            if (supplierInvoiceNumber == value) return;
            supplierInvoiceNumber = value;
            Changed();
        }
    }

    public DateTime PurchaseDate
    {
        get => purchaseDate;
        set
        {
            if (purchaseDate == value) return;
            purchaseDate = value;
            Changed();
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (notes == value) return;
            notes = value;
            Changed();
        }
    }

    public string ProductSearchText
    {
        get => productSearchText;
        set
        {
            if (productSearchText == value) return;
            productSearchText = value;
            Changed();
            RefreshFilteredCatalog();
        }
    }

    public string DiscountAmountText
    {
        get => discountAmountText;
        set
        {
            if (discountAmountText == value) return;
            discountAmountText = value;
            Changed();
            NotifyFinancials();
        }
    }

    public string CashAmountText
    {
        get => cashAmountText;
        set
        {
            if (cashAmountText == value) return;
            cashAmountText = value;
            Changed();
            NotifyFinancials();
        }
    }

    public string VodafoneCashAmountText
    {
        get => vodafoneCashAmountText;
        set
        {
            if (vodafoneCashAmountText == value) return;
            vodafoneCashAmountText = value;
            Changed();
            NotifyFinancials();
        }
    }

    public string VodafoneCashReference
    {
        get => vodafoneCashReference;
        set
        {
            if (vodafoneCashReference == value) return;
            vodafoneCashReference = value;
            Changed();
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
            NotifyFinancials();
        }
    }

    public string InstaPayReference
    {
        get => instaPayReference;
        set
        {
            if (instaPayReference == value) return;
            instaPayReference = value;
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

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal ReturnedGoodsTotal => Lines.Sum(l => l.ReturnedQuantity * l.UnitPurchaseCost);
    public decimal ActiveGrossTotal => Math.Max(0m, Subtotal - ReturnedGoodsTotal);
    public decimal DiscountAmount => PosFormatting.TryParseAmount(DiscountAmountText, out var disc) && disc >= 0m ? disc : 0m;
    public decimal EffectiveDiscount => Math.Min(ActiveGrossTotal, DiscountAmount);
    public decimal NetTotal => Math.Max(0m, ActiveGrossTotal - EffectiveDiscount);

    public decimal CashAmount => PosFormatting.TryParseAmount(CashAmountText, out var cash) && cash >= 0m ? cash : 0m;
    public decimal VodafoneCashAmount => PosFormatting.TryParseAmount(VodafoneCashAmountText, out var vf) && vf >= 0m ? vf : 0m;
    public decimal InstaPayAmount => PosFormatting.TryParseAmount(InstaPayAmountText, out var ip) && ip >= 0m ? ip : 0m;
    public decimal TotalPaid => CashAmount + VodafoneCashAmount + InstaPayAmount;
    public decimal NetCashOutflow => Math.Max(0m, TotalPaid - Invoice.SupplierRefundReceived);
    public decimal RemainingPayable => Math.Max(0m, NetTotal - NetCashOutflow);
    public bool HasOverpayment => NetCashOutflow > NetTotal;

    public string SubtotalDisplay => PosFormatting.FormatCurrency(Subtotal);
    public string ReturnedGoodsTotalDisplay => PosFormatting.FormatCurrency(ReturnedGoodsTotal);
    public bool HasReturnedGoods => ReturnedGoodsTotal > 0m;
    public string EffectiveDiscountDisplay => PosFormatting.FormatCurrency(EffectiveDiscount);
    public string NetTotalDisplay => PosFormatting.FormatCurrency(NetTotal);
    public string TotalPaidDisplay => PosFormatting.FormatCurrency(TotalPaid);
    public string RemainingPayableDisplay => PosFormatting.FormatCurrency(RemainingPayable);
    public string LinesCountDisplay => $"{Lines.Count} بند · {Lines.Sum(l => l.Quantity)} قطعة";

    public Action? RequestCloseWithSuccess { get; set; }
    public Action? RequestCloseCancel { get; set; }
    public EditPurchaseSubmission? Result { get; private set; }

    public ActionCommand ToggleSupplierPickerCommand { get; }
    public RelayCommand<SupplierDesignItem> SelectSupplierCommand { get; }
    public ActionCommand ScanOrSearchEnterCommand { get; }
    public RelayCommand<PosProductDesignItem> AddProductCommand { get; }
    public ActionCommand FillRemainingCashCommand { get; }
    public ActionCommand FillRemainingVodafoneCommand { get; }
    public ActionCommand FillRemainingInstaPayCommand { get; }
    public ActionCommand ClearPaymentsToCreditCommand { get; }
    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    public EditPurchaseInvoiceViewModel(
        PurchaseInvoiceDesignItem invoice,
        IReadOnlyList<SupplierDesignItem> suppliers,
        IReadOnlyList<PosProductDesignItem> catalog,
        IReadOnlyList<InventoryUnitRegistrationItem> sharedRegistrations)
    {
        Invoice = invoice;
        this.suppliers = suppliers;
        this.catalog = catalog;

        selectedSupplier = suppliers.FirstOrDefault(s => s.Id == invoice.SupplierId || s.Name == invoice.SupplierName)
                           ?? new SupplierDesignItem
                           {
                               Id = invoice.SupplierId,
                               Name = invoice.SupplierName,
                               Phone = invoice.SupplierPhone,
                               CompanyName = invoice.SupplierCompany,
                               Address = invoice.SupplierAddress
                           };

        supplierInvoiceNumber = invoice.SupplierInvoiceNumber;
        purchaseDate = invoice.IssuedAt.Date;
        notes = invoice.Notes;
        discountAmountText = invoice.DiscountAmount > 0m ? invoice.DiscountAmount.ToString("0.##", CultureInfo.InvariantCulture) : "0";
        cashAmountText = invoice.CashPaid > 0m ? invoice.CashPaid.ToString("0.##", CultureInfo.InvariantCulture) : "0";
        vodafoneCashAmountText = invoice.VodafoneCashPaid > 0m ? invoice.VodafoneCashPaid.ToString("0.##", CultureInfo.InvariantCulture) : "0";
        vodafoneCashReference = invoice.VodafoneCashReference;
        instaPayAmountText = invoice.InstaPayPaid > 0m ? invoice.InstaPayPaid.ToString("0.##", CultureInfo.InvariantCulture) : "0";
        instaPayReference = invoice.InstaPayReference;

        foreach (var line in invoice.Items)
        {
            var product = catalog.FirstOrDefault(p => p.Id == line.ProductId)
                          ?? new PosProductDesignItem
                          {
                              Id = line.ProductId,
                              Barcode = line.Barcode,
                              Name = line.ProductName,
                              Category = line.Category,
                              Unit = line.Unit,
                              TrackSerialNumber = line.TrackSerialNumber,
                              CostPrice = line.UnitPurchaseCost
                          };

            var regs = sharedRegistrations.Where(r => line.UnitRegistrationIds.Contains(r.Id)).ToList();
            var sold = regs.Count(r => r.IsSold);
            var damaged = regs.Count(r => r.IsDamaged);

            Lines.Add(new EditPurchaseLineItemViewModel(
                product,
                line,
                line.Quantity,
                line.UnitPurchaseCost,
                line.ReturnedQuantity,
                sold,
                damaged,
                NotifyFinancials,
                RemoveLine));
        }

        ToggleSupplierPickerCommand = new ActionCommand(() => IsSupplierPickerOpen = !IsSupplierPickerOpen);
        SelectSupplierCommand = new RelayCommand<SupplierDesignItem>(sup =>
        {
            if (sup is null) return;
            SelectedSupplier = sup;
            SupplierSearchText = "";
            IsSupplierPickerOpen = false;
        });

        ScanOrSearchEnterCommand = new ActionCommand(HandleScanOrAddFirst);
        AddProductCommand = new RelayCommand<PosProductDesignItem>(AddProduct);

        FillRemainingCashCommand = new ActionCommand(() =>
        {
            var add = Math.Max(0m, NetTotal - NetCashOutflow);
            CashAmountText = (CashAmount + add).ToString("0.##", CultureInfo.InvariantCulture);
        });
        FillRemainingVodafoneCommand = new ActionCommand(() =>
        {
            var add = Math.Max(0m, NetTotal - NetCashOutflow);
            VodafoneCashAmountText = (VodafoneCashAmount + add).ToString("0.##", CultureInfo.InvariantCulture);
        });
        FillRemainingInstaPayCommand = new ActionCommand(() =>
        {
            var add = Math.Max(0m, NetTotal - NetCashOutflow);
            InstaPayAmountText = (InstaPayAmount + add).ToString("0.##", CultureInfo.InvariantCulture);
        });
        ClearPaymentsToCreditCommand = new ActionCommand(() =>
        {
            CashAmountText = "0";
            VodafoneCashAmountText = "0";
            InstaPayAmountText = "0";
        });

        SaveCommand = new ActionCommand(Submit);
        CancelCommand = new ActionCommand(() => RequestCloseCancel?.Invoke());

        RefreshFilteredSuppliers();
        RefreshFilteredCatalog();
    }

    private void RemoveLine(EditPurchaseLineItemViewModel lineVm)
    {
        if (!lineVm.CanRemove)
        {
            ErrorMessage = $"لا يمكن حذف «{lineVm.Product.Name}» لوجود قطع مباعة أو هالكة أو مرتجعة مرتبطة به.";
            return;
        }

        Lines.Remove(lineVm);
        NotifyFinancials();
    }

    private void HandleScanOrAddFirst()
    {
        var q = ProductSearchText.Trim();
        if (string.IsNullOrEmpty(q)) return;

        var exact = catalog.FirstOrDefault(p => p.Barcode.Equals(q, StringComparison.OrdinalIgnoreCase))
                    ?? FilteredCatalog.FirstOrDefault();
        if (exact is not null)
        {
            AddProduct(exact);
            ProductSearchText = "";
        }
    }

    private void AddProduct(PosProductDesignItem? product)
    {
        if (product is null) return;

        var existing = Lines.FirstOrDefault(l => l.Product.Id == product.Id);
        if (existing is not null)
        {
            existing.Quantity++;
            return;
        }

        Lines.Add(new EditPurchaseLineItemViewModel(
            product,
            null,
            1,
            product.CostPrice > 0m ? product.CostPrice : 1m,
            0,
            0,
            0,
            NotifyFinancials,
            RemoveLine));
        NotifyFinancials();
    }

    private void RefreshFilteredSuppliers()
    {
        var q = SupplierSearchText.Trim();
        var matches = suppliers.Where(s =>
            string.IsNullOrEmpty(q)
            || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.Phone.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.CompanyName.Contains(q, StringComparison.OrdinalIgnoreCase));

        FilteredSuppliers.Clear();
        foreach (var s in matches)
            FilteredSuppliers.Add(s);
    }

    private void RefreshFilteredCatalog()
    {
        var q = ProductSearchText.Trim();
        var matches = catalog
            .Where(p => string.IsNullOrEmpty(q)
                        || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Barcode.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(8);

        FilteredCatalog.Clear();
        foreach (var p in matches)
            FilteredCatalog.Add(p);
    }

    private void NotifyFinancials()
    {
        ErrorMessage = "";
        Changed(nameof(Subtotal));
        Changed(nameof(ReturnedGoodsTotal));
        Changed(nameof(HasReturnedGoods));
        Changed(nameof(ActiveGrossTotal));
        Changed(nameof(EffectiveDiscount));
        Changed(nameof(NetTotal));
        Changed(nameof(TotalPaid));
        Changed(nameof(NetCashOutflow));
        Changed(nameof(RemainingPayable));
        Changed(nameof(HasOverpayment));
        Changed(nameof(SubtotalDisplay));
        Changed(nameof(ReturnedGoodsTotalDisplay));
        Changed(nameof(EffectiveDiscountDisplay));
        Changed(nameof(NetTotalDisplay));
        Changed(nameof(TotalPaidDisplay));
        Changed(nameof(RemainingPayableDisplay));
        Changed(nameof(LinesCountDisplay));
    }

    private void Submit()
    {
        if (Lines.Count == 0)
        {
            ErrorMessage = "يجب أن تحتوي الفاتورة على صنف واحد على الأقل (أو استخدم إلغاء/حذف الفاتورة).";
            return;
        }

        foreach (var line in Lines)
        {
            if (!int.TryParse(line.QuantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQty)
                || parsedQty < line.MinAllowedQuantity)
            {
                ErrorMessage = $"كمية «{line.Product.Name}» يجب ألا تقل عن {line.MinAllowedQuantity} ({line.LockSummary}).";
                return;
            }

            if (line.UnitPurchaseCost <= 0m)
            {
                ErrorMessage = $"يرجى إدخال سعر شراء صحيح لصنف «{line.Product.Name}».";
                return;
            }
        }

        if (Invoice.SupplierRefundReceived > 0m && TotalPaid < Invoice.SupplierRefundReceived)
        {
            ErrorMessage = $"إجمالي المدفوع ({TotalPaidDisplay}) لا يمكن أن يقل عن المبلغ المسترد سابقًا من المورد ({Invoice.SupplierRefundReceivedDisplay}).";
            return;
        }

        if (HasOverpayment)
        {
            ErrorMessage = $"صافي المدفوع للمورد يتجاوز صافي الفاتورة ({NetTotalDisplay}). يرجى ضبط المبالغ المدفوعة.";
            return;
        }

        Result = new EditPurchaseSubmission(
            SelectedSupplier,
            SupplierInvoiceNumber.Trim(),
            PurchaseDate.Date,
            Notes.Trim(),
            DiscountAmount,
            CashAmount,
            VodafoneCashAmount,
            VodafoneCashReference.Trim(),
            InstaPayAmount,
            InstaPayReference.Trim(),
            Lines.Select(l => new EditPurchaseLineSubmission(l.ExistingLine, l.Product, l.Quantity, l.UnitPurchaseCost)).ToList());
        RequestCloseWithSuccess?.Invoke();
    }
}
