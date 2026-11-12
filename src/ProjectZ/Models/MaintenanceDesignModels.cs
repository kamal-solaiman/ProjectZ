using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum MaintenanceOrderStatus
{
    Received = 0,
    Diagnosing = 1,
    InRepair = 2,
    WaitingParts = 3,
    ReadyForPickup = 4,
    Delivered = 5,
    Cancelled = 6
}

public enum MaintenancePriority
{
    Normal = 0,
    Urgent = 1,
    WarrantyRecheck = 2
}

public enum MaintenancePasscodeType
{
    None = 0,
    PinOrPassword = 1,
    Pattern = 2
}

public enum MaintenanceDepositMode
{
    NoDeposit = 0,
    CashOnly = 1,
    VodafoneCashOnly = 2,
    InstaPayOnly = 3,
    Split = 4
}

public sealed record MaintenanceActivityEntry
{
    public DateTime Timestamp { get; init; }
    public string ActionTitle { get; init; } = "";
    public string Details { get; init; } = "";
    public string ActorName { get; init; } = "";
    public decimal? Amount { get; init; }

    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string AmountDisplay => Amount.HasValue ? PosFormatting.FormatCurrency(Amount.Value) : "—";
}

public sealed class MaintenancePartUsageItem : ObservableObject
{
    public int ProductId { get; init; }
    public string Barcode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal UnitSellPrice { get; init; }
    public bool IsManualPart { get; init; }

    public decimal LineTotal => Quantity * UnitSellPrice;
    public decimal LineCostTotal => Quantity * UnitCost;
    public string QuantityDisplay => $"{Quantity} قطعة";
    public string UnitSellPriceDisplay => PosFormatting.FormatCurrency(UnitSellPrice);
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);
    public string SourceBadgeText => IsManualPart ? "بند يدوي" : "قطع الغيار";
}

public sealed class MaintenanceSparePartItem : ObservableObject
{
    private string name = "";
    private string category = "شاشات";
    private int quantity;
    private decimal costPrice;
    private decimal sellPrice;
    private bool isSelected;

    public int Id { get; init; }

    public string Name
    {
        get => name;
        set
        {
            if (name == value) return;
            name = value;
            Changed();
        }
    }

    public string Category
    {
        get => category;
        set
        {
            if (category == value) return;
            category = value;
            Changed();
        }
    }

    public int Quantity
    {
        get => quantity;
        set
        {
            var clamped = Math.Max(0, value);
            if (quantity == clamped) return;
            quantity = clamped;
            Changed();
            Changed(nameof(QuantityDisplay));
            Changed(nameof(AvailableStockDisplay));
            Changed(nameof(TotalCostValue));
            Changed(nameof(TotalCostValueDisplay));
            Changed(nameof(IsOutOfStock));
            Changed(nameof(IsLowStock));
            Changed(nameof(StockStatusTitle));
        }
    }

    public decimal CostPrice
    {
        get => costPrice;
        set
        {
            var clamped = Math.Max(0m, value);
            if (costPrice == clamped) return;
            costPrice = clamped;
            Changed();
            Changed(nameof(CostPriceDisplay));
            Changed(nameof(UnitProfit));
            Changed(nameof(UnitProfitDisplay));
            Changed(nameof(TotalCostValue));
            Changed(nameof(TotalCostValueDisplay));
        }
    }

    public decimal SellPrice
    {
        get => sellPrice;
        set
        {
            var clamped = Math.Max(0m, value);
            if (sellPrice == clamped) return;
            sellPrice = clamped;
            Changed();
            Changed(nameof(SellPriceDisplay));
            Changed(nameof(UnitProfit));
            Changed(nameof(UnitProfitDisplay));
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            Changed();
        }
    }

    public string CodeDisplay => $"SPR-{Id:000}";
    public string QuantityDisplay => $"{Quantity} قطعة";
    public string AvailableStockDisplay => $"المتاح: {Quantity}";
    public string CostPriceDisplay => PosFormatting.FormatCurrency(CostPrice);
    public string SellPriceDisplay => PosFormatting.FormatCurrency(SellPrice);
    public decimal UnitProfit => Math.Max(0m, SellPrice - CostPrice);
    public string UnitProfitDisplay => PosFormatting.FormatCurrency(UnitProfit);
    public decimal TotalCostValue => Quantity * CostPrice;
    public string TotalCostValueDisplay => PosFormatting.FormatCurrency(TotalCostValue);
    public bool IsOutOfStock => Quantity <= 0;
    public bool IsLowStock => Quantity > 0 && Quantity <= 3;
    public string StockStatusTitle => IsOutOfStock ? "نفد" : (IsLowStock ? "منخفض" : "متاح");
}

public sealed class MaintenanceOrderDesignItem : ObservableObject
{
    private MaintenanceOrderStatus status = MaintenanceOrderStatus.Received;
    private MaintenancePriority priority = MaintenancePriority.Normal;
    private string assignedTechnician = "";
    private DateTime? expectedDeliveryAt;
    private string expectedDeliveryLabel = "";
    private string diagnosisNotes = "";
    private decimal laborCost;
    private decimal discountAmount;
    private decimal? finalServiceCost;
    private decimal depositCashPaid;
    private decimal depositVodafonePaid;
    private decimal depositInstaPayPaid;
    private decimal refundedDepositAmount;
    private decimal finalPaymentCashPaid;
    private decimal finalPaymentVodafonePaid;
    private decimal finalPaymentInstaPayPaid;
    private string customerName = "";
    private string customerPhone = "";
    private string customerSecondaryPhone = "";
    private string customerAddress = "";
    private DateTime? deliveredAt;
    private int warrantyDays;
    private string deliveryNotes = "";
    private string cancellationReason = "";
    private bool hasWorkshopPricing;
    private bool isInvoiceRowSelected;

    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public DateTime ReceivedAt { get; init; }
    public string ReceivedByName { get; init; } = "";

    public DateTime? ExpectedDeliveryAt
    {
        get => expectedDeliveryAt;
        set
        {
            if (expectedDeliveryAt == value) return;
            expectedDeliveryAt = value;
            Changed();
            Changed(nameof(ExpectedDeliveryDisplay));
        }
    }

    public string ExpectedDeliveryLabel
    {
        get => expectedDeliveryLabel;
        set
        {
            if (expectedDeliveryLabel == value) return;
            expectedDeliveryLabel = value;
            Changed();
            Changed(nameof(ExpectedDeliveryDisplay));
        }
    }

    // Customer snapshot
    public int CustomerId { get; init; }

    public string CustomerName
    {
        get => customerName;
        set
        {
            if (customerName == value) return;
            customerName = value;
            Changed();
        }
    }

    public string CustomerPhone
    {
        get => customerPhone;
        set
        {
            if (customerPhone == value) return;
            customerPhone = value;
            Changed();
            Changed(nameof(CustomerPhoneDisplay));
        }
    }

    public string CustomerSecondaryPhone
    {
        get => customerSecondaryPhone;
        set
        {
            if (customerSecondaryPhone == value) return;
            customerSecondaryPhone = value;
            Changed();
        }
    }

    public string CustomerAddress
    {
        get => customerAddress;
        set
        {
            if (customerAddress == value) return;
            customerAddress = value;
            Changed();
        }
    }

    public bool IsWalkInCustomer { get; init; }

    // Device & intake check
    public string DeviceType { get; init; } = "هاتف ذكي";
    public string DeviceBrand { get; init; } = "";
    public string DeviceModel { get; init; } = "";
    public string DeviceColor { get; init; } = "";
    public string SerialOrImei { get; init; } = "";
    public MaintenancePasscodeType PasscodeType { get; init; } = MaintenancePasscodeType.None;
    public string PasscodeValue { get; init; } = "";
    public string ReportedFault { get; init; } = "";
    public string PhysicalCondition { get; init; } = "";
    public string AccessoriesReceived { get; init; } = "";
    public string IntakeNotes { get; init; } = "";

    // Initial financial estimate
    public bool IsEstimatePendingInspection { get; init; }
    public decimal EstimatedCost { get; init; }

    public ObservableCollection<MaintenancePartUsageItem> UsedParts { get; } = [];
    public ObservableCollection<MaintenanceActivityEntry> ActivityLog { get; } = [];

    public MaintenanceOrderStatus Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            Changed();
            Changed(nameof(StatusTitle));
            Changed(nameof(NextRecommendedStatus));
            Changed(nameof(NextRecommendedStatusTitle));
            Changed(nameof(CanAdvanceStatus));
            Changed(nameof(IsReceived));
            Changed(nameof(IsDiagnosing));
            Changed(nameof(IsInRepair));
            Changed(nameof(IsWaitingParts));
            Changed(nameof(IsReadyForPickup));
            Changed(nameof(IsDelivered));
            Changed(nameof(IsCancelled));
            Changed(nameof(IsActiveOrder));
            Changed(nameof(IsClosedOrder));
        }
    }

    public MaintenancePriority Priority
    {
        get => priority;
        set
        {
            if (priority == value) return;
            priority = value;
            Changed();
            Changed(nameof(PriorityTitle));
            Changed(nameof(IsUrgent));
            Changed(nameof(IsWarranty));
        }
    }

    public string AssignedTechnician
    {
        get => assignedTechnician;
        set
        {
            if (assignedTechnician == value) return;
            assignedTechnician = value;
            Changed();
            Changed(nameof(AssignedTechnicianDisplay));
        }
    }

    public string DiagnosisNotes
    {
        get => diagnosisNotes;
        set
        {
            if (diagnosisNotes == value) return;
            diagnosisNotes = value;
            Changed();
            Changed(nameof(HasDiagnosisNotes));
        }
    }

    public bool HasDiagnosisNotes => !string.IsNullOrWhiteSpace(DiagnosisNotes);

    public decimal LaborCost
    {
        get => laborCost;
        set
        {
            var clamped = Math.Max(0m, value);
            if (laborCost == clamped) return;
            laborCost = clamped;
            Changed();
            Changed(nameof(LaborCostDisplay));
            NotifyFinancialsChanged();
        }
    }

    public decimal DiscountAmount
    {
        get => discountAmount;
        set
        {
            var clamped = Math.Max(0m, value);
            if (discountAmount == clamped) return;
            discountAmount = clamped;
            Changed();
            Changed(nameof(DiscountAmountDisplay));
            NotifyFinancialsChanged();
        }
    }

    public bool HasWorkshopPricing
    {
        get => hasWorkshopPricing || FinalServiceCost.HasValue || UsedParts.Count > 0 || LaborCost > 0m || DiscountAmount > 0m;
        set
        {
            if (hasWorkshopPricing == value) return;
            hasWorkshopPricing = value;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal? FinalServiceCost
    {
        get => finalServiceCost;
        set
        {
            if (finalServiceCost == value) return;
            finalServiceCost = value;
            if (value.HasValue)
                hasWorkshopPricing = true;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal RefundedDepositAmount
    {
        get => refundedDepositAmount;
        set
        {
            var clamped = Math.Max(0m, value);
            if (refundedDepositAmount == clamped) return;
            refundedDepositAmount = clamped;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal FinalPaymentCashPaid
    {
        get => finalPaymentCashPaid;
        set
        {
            var clamped = Math.Max(0m, value);
            if (finalPaymentCashPaid == clamped) return;
            finalPaymentCashPaid = clamped;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal FinalPaymentVodafonePaid
    {
        get => finalPaymentVodafonePaid;
        set
        {
            var clamped = Math.Max(0m, value);
            if (finalPaymentVodafonePaid == clamped) return;
            finalPaymentVodafonePaid = clamped;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal FinalPaymentInstaPayPaid
    {
        get => finalPaymentInstaPayPaid;
        set
        {
            var clamped = Math.Max(0m, value);
            if (finalPaymentInstaPayPaid == clamped) return;
            finalPaymentInstaPayPaid = clamped;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public DateTime? DeliveredAt
    {
        get => deliveredAt;
        set
        {
            if (deliveredAt == value) return;
            deliveredAt = value;
            Changed();
            Changed(nameof(DeliveredAtDisplay));
            Changed(nameof(WarrantySummaryDisplay));
        }
    }

    public int WarrantyDays
    {
        get => warrantyDays;
        set
        {
            var clamped = Math.Max(0, value);
            if (warrantyDays == clamped) return;
            warrantyDays = clamped;
            Changed();
            Changed(nameof(HasWarrantyCoverage));
            Changed(nameof(WarrantySummaryDisplay));
        }
    }

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

    public string CancellationReason
    {
        get => cancellationReason;
        set
        {
            if (cancellationReason == value) return;
            cancellationReason = value;
            Changed();
            Changed(nameof(HasCancellationReason));
        }
    }

    public bool HasCancellationReason => !string.IsNullOrWhiteSpace(CancellationReason);
    public bool HasWarrantyCoverage => WarrantyDays > 0;

    public decimal DepositCashPaid
    {
        get => depositCashPaid;
        set
        {
            var clamped = Math.Max(0m, value);
            if (depositCashPaid == clamped) return;
            depositCashPaid = clamped;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal DepositVodafonePaid
    {
        get => depositVodafonePaid;
        set
        {
            var clamped = Math.Max(0m, value);
            if (depositVodafonePaid == clamped) return;
            depositVodafonePaid = clamped;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal DepositInstaPayPaid
    {
        get => depositInstaPayPaid;
        set
        {
            var clamped = Math.Max(0m, value);
            if (depositInstaPayPaid == clamped) return;
            depositInstaPayPaid = clamped;
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public string ReceivedDateDisplay => ReceivedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string ReceivedTimeDisplay => ReceivedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string ReceivedFullDisplay => ReceivedAt.ToString("yyyy-MM-dd · HH:mm", CultureInfo.InvariantCulture);
    public string CustomerPhoneDisplay => string.IsNullOrWhiteSpace(CustomerPhone) ? "بدون هاتف" : CustomerPhone;
    public string AssignedTechnicianDisplay => string.IsNullOrWhiteSpace(AssignedTechnician) ? "غير محدد" : AssignedTechnician;

    public string DeviceFullTitle
    {
        get
        {
            var main = string.Join(" ", new[] { DeviceBrand, DeviceModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(main)) main = DeviceType;
            return string.IsNullOrWhiteSpace(DeviceColor) ? main : $"{main} ({DeviceColor})";
        }
    }

    public string SerialOrImeiDisplay => string.IsNullOrWhiteSpace(SerialOrImei) ? "غير مسجل" : SerialOrImei;
    public bool HasSerialOrImei => !string.IsNullOrWhiteSpace(SerialOrImei);

    public string PasscodeDisplay => PasscodeType switch
    {
        MaintenancePasscodeType.PinOrPassword => string.IsNullOrWhiteSpace(PasscodeValue) ? "كود سري (غير مكتوب)" : $"كود: {PasscodeValue}",
        MaintenancePasscodeType.Pattern => string.IsNullOrWhiteSpace(PasscodeValue) ? "نمط سحب" : $"نمط: {PasscodeValue}",
        _ => "بدون قفل"
    };

    public string PhysicalConditionDisplay => string.IsNullOrWhiteSpace(PhysicalCondition) ? "حالة ظاهرية سليمة" : PhysicalCondition;
    public string AccessoriesDisplay => string.IsNullOrWhiteSpace(AccessoriesReceived) ? "الجهاز فقط (بدون ملحقات)" : AccessoriesReceived;
    public bool HasIntakeNotes => !string.IsNullOrWhiteSpace(IntakeNotes);

    public string ExpectedDeliveryDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ExpectedDeliveryLabel) && ExpectedDeliveryAt.HasValue)
                return $"{ExpectedDeliveryLabel} ({ExpectedDeliveryAt.Value:yyyy-MM-dd})";
            if (ExpectedDeliveryAt.HasValue)
                return ExpectedDeliveryAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(ExpectedDeliveryLabel) ? "يُحدد بعد الفحص" : ExpectedDeliveryLabel;
        }
    }

    public string StatusTitle => Status switch
    {
        MaintenanceOrderStatus.Diagnosing => "قيد الفحص",
        MaintenanceOrderStatus.InRepair => "قيد الصيانة",
        MaintenanceOrderStatus.WaitingParts => "بانتظار قطع غيار",
        MaintenanceOrderStatus.ReadyForPickup => "جاهز للتسليم",
        MaintenanceOrderStatus.Delivered => "تم التسليم",
        MaintenanceOrderStatus.Cancelled => "ملغى",
        _ => "تم الاستلام"
    };

    public bool IsReceived => Status == MaintenanceOrderStatus.Received;
    public bool IsDiagnosing => Status == MaintenanceOrderStatus.Diagnosing;
    public bool IsInRepair => Status == MaintenanceOrderStatus.InRepair;
    public bool IsWaitingParts => Status == MaintenanceOrderStatus.WaitingParts;
    public bool IsReadyForPickup => Status == MaintenanceOrderStatus.ReadyForPickup;
    public bool IsDelivered => Status == MaintenanceOrderStatus.Delivered;
    public bool IsCancelled => Status == MaintenanceOrderStatus.Cancelled;
    public bool IsActiveOrder => Status is not (MaintenanceOrderStatus.Delivered or MaintenanceOrderStatus.Cancelled);
    public bool IsClosedOrder => !IsActiveOrder;

    public MaintenanceOrderStatus? NextRecommendedStatus => Status switch
    {
        MaintenanceOrderStatus.Received => MaintenanceOrderStatus.Diagnosing,
        MaintenanceOrderStatus.Diagnosing => MaintenanceOrderStatus.InRepair,
        MaintenanceOrderStatus.WaitingParts => MaintenanceOrderStatus.InRepair,
        MaintenanceOrderStatus.InRepair => MaintenanceOrderStatus.ReadyForPickup,
        MaintenanceOrderStatus.ReadyForPickup => MaintenanceOrderStatus.Delivered,
        _ => null
    };

    public string NextRecommendedStatusTitle => Status switch
    {
        MaintenanceOrderStatus.Received => "بدء الفحص",
        MaintenanceOrderStatus.Diagnosing => "بدء الصيانة",
        MaintenanceOrderStatus.WaitingParts => "بدء الصيانة",
        MaintenanceOrderStatus.InRepair => "جاهز للتسليم",
        MaintenanceOrderStatus.ReadyForPickup => "تسليم وتحصيل",
        _ => ""
    };

    public bool CanAdvanceStatus => NextRecommendedStatus.HasValue;

    public string PriorityTitle => Priority switch
    {
        MaintenancePriority.Urgent => "مستعجل",
        MaintenancePriority.WarrantyRecheck => "ضمان / إعادة فحص",
        _ => "عادي"
    };

    public bool IsUrgent => Priority == MaintenancePriority.Urgent;
    public bool IsWarranty => Priority == MaintenancePriority.WarrantyRecheck;

    public decimal TotalDepositPaid => DepositCashPaid + DepositVodafonePaid + DepositInstaPayPaid;
    public decimal NetDepositRetained => Math.Max(0m, TotalDepositPaid - RefundedDepositAmount);
    public decimal TotalFinalPaymentPaid => FinalPaymentCashPaid + FinalPaymentVodafonePaid + FinalPaymentInstaPayPaid;
    public decimal TotalCollectedAll => NetDepositRetained + TotalFinalPaymentPaid;
    public bool HasDeposit => TotalDepositPaid > 0m;
    public bool HasRefundedDeposit => RefundedDepositAmount > 0m;
    public bool HasFinalPayment => TotalFinalPaymentPaid > 0m;
    public bool HasDepositCash => DepositCashPaid > 0m;
    public bool HasDepositVodafone => DepositVodafonePaid > 0m;
    public bool HasDepositInstaPay => DepositInstaPayPaid > 0m;

    public decimal PartsTotal => UsedParts.Sum(p => p.LineTotal);
    public decimal PartsCostTotal => UsedParts.Sum(p => p.LineCostTotal);
    public bool HasUsedParts => UsedParts.Count > 0;
    public int UsedPartsTotalCount => UsedParts.Sum(p => p.Quantity);
    public decimal ComputedServiceTotal => Math.Max(0m, LaborCost + PartsTotal - DiscountAmount);

    public decimal CurrentEffectiveCost => HasWorkshopPricing
        ? (FinalServiceCost ?? ComputedServiceTotal)
        : (IsEstimatePendingInspection ? 0m : EstimatedCost);

    public bool IsPriceStillPending => IsEstimatePendingInspection && !HasWorkshopPricing;

    public decimal EstimatedRemaining => IsPriceStillPending
        ? 0m
        : Math.Max(0m, CurrentEffectiveCost - TotalCollectedAll);

    public decimal OverpaidDepositAmount => IsPriceStillPending
        ? 0m
        : Math.Max(0m, NetDepositRetained - CurrentEffectiveCost);

    public bool HasEstimatedRemaining => !IsPriceStillPending && EstimatedRemaining > 0m;
    public decimal EstimatedProfit => Math.Max(0m, CurrentEffectiveCost - PartsCostTotal);

    public string EstimatedCostDisplay => IsPriceStillPending
        ? "يُحدد بعد الفحص"
        : PosFormatting.FormatCurrency(CurrentEffectiveCost);

    public string PartsTotalDisplay => PosFormatting.FormatCurrency(PartsTotal);
    public string LaborCostDisplay => PosFormatting.FormatCurrency(LaborCost);
    public string DiscountAmountDisplay => PosFormatting.FormatCurrency(DiscountAmount);
    public string TotalDepositPaidDisplay => PosFormatting.FormatCurrency(TotalDepositPaid);
    public string RefundedDepositAmountDisplay => PosFormatting.FormatCurrency(RefundedDepositAmount);
    public string NetDepositRetainedDisplay => PosFormatting.FormatCurrency(NetDepositRetained);
    public string TotalFinalPaymentPaidDisplay => PosFormatting.FormatCurrency(TotalFinalPaymentPaid);
    public string TotalCollectedAllDisplay => PosFormatting.FormatCurrency(TotalCollectedAll);
    public string DepositCashPaidDisplay => PosFormatting.FormatCurrency(DepositCashPaid);
    public string DepositVodafonePaidDisplay => PosFormatting.FormatCurrency(DepositVodafonePaid);
    public string DepositInstaPayPaidDisplay => PosFormatting.FormatCurrency(DepositInstaPayPaid);
    public string FinalPaymentCashPaidDisplay => PosFormatting.FormatCurrency(FinalPaymentCashPaid);
    public string FinalPaymentVodafonePaidDisplay => PosFormatting.FormatCurrency(FinalPaymentVodafonePaid);
    public string FinalPaymentInstaPayPaidDisplay => PosFormatting.FormatCurrency(FinalPaymentInstaPayPaid);
    public string PartsCostTotalDisplay => PosFormatting.FormatCurrency(PartsCostTotal);
    public string EstimatedProfitDisplay => PosFormatting.FormatCurrency(EstimatedProfit);
    public string EstimatedRemainingDisplay => IsPriceStillPending
        ? "يتحدد بعد الفحص"
        : PosFormatting.FormatCurrency(EstimatedRemaining);

    public bool IsInvoiceRowSelected
    {
        get => isInvoiceRowSelected;
        set
        {
            if (isInvoiceRowSelected == value) return;
            isInvoiceRowSelected = value;
            Changed();
        }
    }

    public bool HasDiscount => DiscountAmount > 0m;
    public bool HasFinalPaymentCash => FinalPaymentCashPaid > 0m;
    public bool HasFinalPaymentVodafone => FinalPaymentVodafonePaid > 0m;
    public bool HasFinalPaymentInstaPay => FinalPaymentInstaPayPaid > 0m;
    public decimal TotalCashCollected => DepositCashPaid + FinalPaymentCashPaid;
    public decimal TotalVodafoneCollected => DepositVodafonePaid + FinalPaymentVodafonePaid;
    public decimal TotalInstaPayCollected => DepositInstaPayPaid + FinalPaymentInstaPayPaid;
    public bool HasAnyCashCollected => TotalCashCollected > 0m;
    public bool HasAnyVodafoneCollected => TotalVodafoneCollected > 0m;
    public bool HasAnyInstaPayCollected => TotalInstaPayCollected > 0m;
    public bool IsMultiChannelPayment =>
        (HasAnyCashCollected ? 1 : 0) + (HasAnyVodafoneCollected ? 1 : 0) + (HasAnyInstaPayCollected ? 1 : 0) > 1;
    public string TotalCashCollectedDisplay => PosFormatting.FormatCurrency(TotalCashCollected);
    public string TotalVodafoneCollectedDisplay => PosFormatting.FormatCurrency(TotalVodafoneCollected);
    public string TotalInstaPayCollectedDisplay => PosFormatting.FormatCurrency(TotalInstaPayCollected);
    public string PartsAndLaborSummaryText =>
        $"قطع: {PartsTotalDisplay} · مصنعية: {LaborCostDisplay}";

    public string PaymentChannelsSummaryText
    {
        get
        {
            var channels = new List<string>(3);
            if (HasAnyCashCollected) channels.Add("كاش");
            if (HasAnyVodafoneCollected) channels.Add("فودافون كاش");
            if (HasAnyInstaPayCollected) channels.Add("إنستاباي");
            if (channels.Count == 0)
                return RefundedDepositAmount > 0m ? "تم استرداد العربون" : "بدون تحصيل";
            var joined = string.Join(" + ", channels);
            return RefundedDepositAmount > 0m ? $"{joined} (بعد خصم مرتجع)" : joined;
        }
    }

    public string DeliveredAtDisplay => DeliveredAt.HasValue
        ? DeliveredAt.Value.ToString("yyyy-MM-dd · HH:mm", CultureInfo.InvariantCulture)
        : "لم يُسلم بعد";

    public string WarrantySummaryDisplay
    {
        get
        {
            if (WarrantyDays <= 0) return "بدون ضمان";
            if (DeliveredAt.HasValue)
            {
                var endsAt = DeliveredAt.Value.Date.AddDays(WarrantyDays);
                return $"ضمان {WarrantyDays} يومًا (حتى {endsAt:yyyy-MM-dd})";
            }
            return $"ضمان {WarrantyDays} يومًا بعد التسليم";
        }
    }

    public string DepositSummaryText
    {
        get
        {
            if (TotalDepositPaid <= 0m) return "بدون مقدم / عربون";

            var channels = new List<string>(3);
            if (DepositCashPaid > 0m) channels.Add("كاش");
            if (DepositVodafonePaid > 0m) channels.Add("فودافون كاش");
            if (DepositInstaPayPaid > 0m) channels.Add("إنستاباي");
            return $"{TotalDepositPaidDisplay} ({string.Join(" + ", channels)})";
        }
    }

    public string FinalPaymentSummaryText
    {
        get
        {
            if (TotalFinalPaymentPaid <= 0m) return "لا يوجد تحصيل إضافي عند التسليم";

            var channels = new List<string>(3);
            if (FinalPaymentCashPaid > 0m) channels.Add($"كاش {PosFormatting.FormatCurrency(FinalPaymentCashPaid)}");
            if (FinalPaymentVodafonePaid > 0m) channels.Add($"فودافون {PosFormatting.FormatCurrency(FinalPaymentVodafonePaid)}");
            if (FinalPaymentInstaPayPaid > 0m) channels.Add($"إنستاباي {PosFormatting.FormatCurrency(FinalPaymentInstaPayPaid)}");
            return string.Join(" + ", channels);
        }
    }

    public void RecalculateWorkshopTotal()
    {
        hasWorkshopPricing = true;
        FinalServiceCost = ComputedServiceTotal;
        Changed(nameof(HasWorkshopPricing));
        NotifyFinancialsChanged();
    }

    public void NotifyFinancialsChanged()
    {
        Changed(nameof(PartsTotal));
        Changed(nameof(PartsCostTotal));
        Changed(nameof(HasUsedParts));
        Changed(nameof(UsedPartsTotalCount));
        Changed(nameof(ComputedServiceTotal));
        Changed(nameof(HasWorkshopPricing));
        Changed(nameof(IsPriceStillPending));
        Changed(nameof(TotalDepositPaid));
        Changed(nameof(NetDepositRetained));
        Changed(nameof(TotalFinalPaymentPaid));
        Changed(nameof(TotalCollectedAll));
        Changed(nameof(HasDeposit));
        Changed(nameof(HasRefundedDeposit));
        Changed(nameof(HasFinalPayment));
        Changed(nameof(HasDepositCash));
        Changed(nameof(HasDepositVodafone));
        Changed(nameof(HasDepositInstaPay));
        Changed(nameof(CurrentEffectiveCost));
        Changed(nameof(EstimatedRemaining));
        Changed(nameof(OverpaidDepositAmount));
        Changed(nameof(HasEstimatedRemaining));
        Changed(nameof(EstimatedProfit));
        Changed(nameof(EstimatedCostDisplay));
        Changed(nameof(PartsTotalDisplay));
        Changed(nameof(LaborCostDisplay));
        Changed(nameof(DiscountAmountDisplay));
        Changed(nameof(TotalDepositPaidDisplay));
        Changed(nameof(RefundedDepositAmountDisplay));
        Changed(nameof(NetDepositRetainedDisplay));
        Changed(nameof(TotalFinalPaymentPaidDisplay));
        Changed(nameof(TotalCollectedAllDisplay));
        Changed(nameof(DepositCashPaidDisplay));
        Changed(nameof(DepositVodafonePaidDisplay));
        Changed(nameof(DepositInstaPayPaidDisplay));
        Changed(nameof(FinalPaymentCashPaidDisplay));
        Changed(nameof(FinalPaymentVodafonePaidDisplay));
        Changed(nameof(FinalPaymentInstaPayPaidDisplay));
        Changed(nameof(PartsCostTotalDisplay));
        Changed(nameof(EstimatedProfitDisplay));
        Changed(nameof(EstimatedRemainingDisplay));
        Changed(nameof(HasDiscount));
        Changed(nameof(HasFinalPaymentCash));
        Changed(nameof(HasFinalPaymentVodafone));
        Changed(nameof(HasFinalPaymentInstaPay));
        Changed(nameof(TotalCashCollected));
        Changed(nameof(TotalVodafoneCollected));
        Changed(nameof(TotalInstaPayCollected));
        Changed(nameof(HasAnyCashCollected));
        Changed(nameof(HasAnyVodafoneCollected));
        Changed(nameof(HasAnyInstaPayCollected));
        Changed(nameof(IsMultiChannelPayment));
        Changed(nameof(TotalCashCollectedDisplay));
        Changed(nameof(TotalVodafoneCollectedDisplay));
        Changed(nameof(TotalInstaPayCollectedDisplay));
        Changed(nameof(PartsAndLaborSummaryText));
        Changed(nameof(PaymentChannelsSummaryText));
        Changed(nameof(DepositSummaryText));
        Changed(nameof(FinalPaymentSummaryText));
    }
}

public static class MaintenanceDesignSeedData
{
    public static IReadOnlyList<string> DeviceTypes { get; } =
    [
        "هاتف ذكي",
        "تابلت",
        "لابتوب",
        "ساعة ذكية",
        "إكسسوار / أخرى"
    ];

    public static IReadOnlyList<string> DeviceBrands { get; } =
    [
        "Apple",
        "Samsung",
        "Xiaomi",
        "OPPO",
        "Realme",
        "Huawei",
        "Vivo",
        "Infinix",
        "Lenovo",
        "Dell",
        "HP",
        "أخرى"
    ];

    public static IReadOnlyList<string> CommonFaults { get; } =
    [
        "كسر شاشة / عطل تاتش",
        "تغيير بطارية / شحن سريع يفصل",
        "عطل سوكيت / مدخل شحن",
        "الجهاز فاصل باور (لا يفتح)",
        "سوفت وير / تخطي حساب / فورمات",
        "تعرض لسوائل / تنظيف بوردة",
        "مشكلة سماعة / مايك / جرس",
        "مشكلة شبكة / واي فاي / كاميرا",
        "تنظيف وصيانة دورية / حرارة"
    ];

    public static IReadOnlyList<string> ConditionChecklistOptions { get; } =
    [
        "شاشة مكسورة",
        "خدوش بالهيكل",
        "كسر بالظهر",
        "الجهاز لا يفتح للفحص",
        "آثار رطوبة / سوائل",
        "انتفاخ بطارية",
        "سليم ظاهريًا"
    ];

    public static IReadOnlyList<string> AccessoryChecklistOptions { get; } =
    [
        "شاحن",
        "كابل",
        "جراب",
        "كرت SIM",
        "كارت ميموري",
        "العلبة",
        "قلم"
    ];

    public static IReadOnlyList<string> DeliverySlotOptions { get; } =
    [
        "اليوم مساءً",
        "غدًا",
        "خلال 48 ساعة",
        "بعد الفحص والاتصال بالعميل"
    ];

    public static IReadOnlyList<string> Technicians { get; } =
    [
        "ورشة الصيانة العامة",
        "م. كريم — هاردوير وشاشات",
        "م. عمر — سوفت وير",
        "م. مصطفى — لابتوب وبوردة"
    ];

    public static IReadOnlyList<string> DiagnosisSnippets { get; } =
    [
        "تم الفحص الظاهري والداخلي والتأكد من سلامة البوردة الأساسية.",
        "العطل يتطلب استبدال قطعة غيار أصلية مع اختبار الشحن والشبكة بعد التركيب.",
        "تم تنظيف البوردة وإزالة آثار الرطوبة وفحص مسارات الطاقة.",
        "تم تثبيت النظام / السوفت وير واختبار الكاميرا والصوت والميكروفون.",
        "بانتظار توفير قطعة الغيار المطلوبة من المورد وإبلاغ العميل.",
        "تم الانتهاء من الصيانة والاختبار بنجاح والجهاز جاهز للتسليم."
    ];

    public static IReadOnlyList<string> CancellationReasons { get; } =
    [
        "العميل رفض التكلفة بعد الفحص",
        "الجهاز لا يُصلح (تلف بالمعالج / البوردة)",
        "عدم توفر قطعة الغيار المطلوبة",
        "العميل استعجل وسحب الجهاز قبل الإصلاح",
        "سبب آخر"
    ];

    public static IReadOnlyList<int> WarrantyDayOptions { get; } = [0, 7, 14, 30, 90];

    public static IReadOnlyList<string> SparePartCategories { get; } =
    [
        "شاشات",
        "بطاريات",
        "فلاتات ومداخل شحن",
        "سماعات ومايك",
        "كاميرات وحساسات",
        "آي سي وبوردة",
        "هاوسنج وظهر",
        "مستلزمات صيانة",
        "أخرى"
    ];

    public static ObservableCollection<MaintenanceSparePartItem> CreateSharedSampleSpareParts() =>
    [
        new MaintenanceSparePartItem
        {
            Id = 1,
            Name = "شاشة iPhone 13 Pro OLED أصلية",
            Category = "شاشات",
            Quantity = 4,
            CostPrice = 3100m,
            SellPrice = 4200m
        },
        new MaintenanceSparePartItem
        {
            Id = 2,
            Name = "بطارية Samsung Galaxy A54 أصلية",
            Category = "بطاريات",
            Quantity = 6,
            CostPrice = 520m,
            SellPrice = 850m
        },
        new MaintenanceSparePartItem
        {
            Id = 3,
            Name = "فلاتة وسوكيت شحن Galaxy S23 Ultra",
            Category = "فلاتات ومداخل شحن",
            Quantity = 5,
            CostPrice = 240m,
            SellPrice = 480m
        },
        new MaintenanceSparePartItem
        {
            Id = 4,
            Name = "شاشة OPPO Reno 11 AMOLED أصلية",
            Category = "شاشات",
            Quantity = 2,
            CostPrice = 1850m,
            SellPrice = 2500m
        },
        new MaintenanceSparePartItem
        {
            Id = 5,
            Name = "سماعة خارجية Xiaomi Redmi Note 13 Pro",
            Category = "سماعات ومايك",
            Quantity = 8,
            CostPrice = 110m,
            SellPrice = 250m
        },
        new MaintenanceSparePartItem
        {
            Id = 6,
            Name = "فلاتة باور وبصمة iPhone 11 / 12",
            Category = "فلاتات ومداخل شحن",
            Quantity = 3,
            CostPrice = 160m,
            SellPrice = 350m
        },
        new MaintenanceSparePartItem
        {
            Id = 7,
            Name = "معجون حراري احترافي Arctic MX-4 + وسادات تبريد",
            Category = "مستلزمات صيانة",
            Quantity = 10,
            CostPrice = 140m,
            SellPrice = 300m
        },
        new MaintenanceSparePartItem
        {
            Id = 8,
            Name = "آي سي شحن U2 / Tristar أصلي",
            Category = "آي سي وبوردة",
            Quantity = 7,
            CostPrice = 280m,
            SellPrice = 650m
        }
    ];

    public static ObservableCollection<MaintenanceOrderDesignItem> CreateSharedSampleOrders(
        IEnumerable<PosCustomerDesignItem> customers)
    {
        var customerList = customers.ToList();
        var cust1 = customerList.FirstOrDefault(c => c.Id == 2) ?? customerList.FirstOrDefault() ?? new PosCustomerDesignItem
        {
            Id = 2,
            Name = "محمود حسن",
            Phone = "01012345678",
            Address = "مدينة نصر"
        };
        var cust2 = customerList.FirstOrDefault(c => c.Id == 3) ?? cust1;
        var cust3 = customerList.FirstOrDefault(c => c.Id == 4) ?? cust1;

        var list = new ObservableCollection<MaintenanceOrderDesignItem>();

        var ord4 = new MaintenanceOrderDesignItem
        {
            Id = 4,
            OrderNumber = "MNT-2026-0004",
            ReceivedAt = DateTime.Today.AddHours(11).AddMinutes(20),
            ReceivedByName = "المشرف",
            ExpectedDeliveryAt = DateTime.Today,
            ExpectedDeliveryLabel = "اليوم مساءً",
            CustomerId = cust1.Id,
            CustomerName = cust1.Name,
            CustomerPhone = cust1.Phone,
            CustomerSecondaryPhone = cust1.SecondaryPhone,
            CustomerAddress = cust1.Address,
            DeviceType = "هاتف ذكي",
            DeviceBrand = "Apple",
            DeviceModel = "iPhone 13 Pro",
            DeviceColor = "أزرق سييرا",
            SerialOrImei = "358912004512398",
            PasscodeType = MaintenancePasscodeType.PinOrPassword,
            PasscodeValue = "258046",
            ReportedFault = "كسر شاشة / عطل تاتش — الشاشة مكسورة والتاتش لا يستجيب بالجزء السفلي.",
            PhysicalCondition = "شاشة مكسورة، خدوش بالهيكل",
            AccessoriesReceived = "جراب",
            IntakeNotes = "العميل طلب تركيب شاشة أصلية والتواصل قبل تغيير أي قطعة إضافية.",
            Status = MaintenanceOrderStatus.Received,
            Priority = MaintenancePriority.Urgent,
            AssignedTechnician = "م. كريم — هاردوير وشاشات",
            IsEstimatePendingInspection = false,
            EstimatedCost = 4200m,
            DepositCashPaid = 500m
        };
        ord4.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord4.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة",
            Details = "استلام iPhone 13 Pro (مستعجل) مع تحصيل عربون 500.00 ج.م كاش.",
            ActorName = "المشرف",
            Amount = 500m
        });
        list.Add(ord4);

        var ord3 = new MaintenanceOrderDesignItem
        {
            Id = 3,
            OrderNumber = "MNT-2026-0003",
            ReceivedAt = DateTime.Today.AddHours(9).AddMinutes(45),
            ReceivedByName = "المشرف",
            ExpectedDeliveryAt = DateTime.Today.AddDays(1),
            ExpectedDeliveryLabel = "غدًا",
            CustomerId = cust2.Id,
            CustomerName = cust2.Name,
            CustomerPhone = cust2.Phone,
            CustomerAddress = cust2.Address,
            DeviceType = "هاتف ذكي",
            DeviceBrand = "Samsung",
            DeviceModel = "Galaxy S23 Ultra",
            DeviceColor = "أسود فانتوم",
            SerialOrImei = "354120987654321",
            PasscodeType = MaintenancePasscodeType.Pattern,
            PasscodeValue = "1-2-5-8-9",
            ReportedFault = "عطل سوكيت / مدخل شحن — الشحن البطيء ويفصل عند تحريك الكابل.",
            PhysicalCondition = "سليم ظاهريًا",
            AccessoriesReceived = "الجهاز فقط (بدون ملحقات)",
            IntakeNotes = "يُحدد السعر النهائي بعد فحص فلاتة الشحن والآي سي.",
            Status = MaintenanceOrderStatus.Diagnosing,
            Priority = MaintenancePriority.Normal,
            AssignedTechnician = "م. كريم — هاردوير وشاشات",
            IsEstimatePendingInspection = true,
            EstimatedCost = 0m,
            DepositVodafonePaid = 100m,
        };
        ord3.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord3.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة + رسوم فحص",
            Details = "استلام Galaxy S23 Ultra للفحص مع رسوم فحص 100.00 ج.م فودافون كاش.",
            ActorName = "المشرف",
            Amount = 100m
        });
        list.Add(ord3);

        var ord2 = new MaintenanceOrderDesignItem
        {
            Id = 2,
            OrderNumber = "MNT-2026-0002",
            ReceivedAt = DateTime.Today.AddDays(-1).AddHours(17),
            ReceivedByName = "المشرف",
            ExpectedDeliveryAt = DateTime.Today,
            ExpectedDeliveryLabel = "اليوم مساءً",
            CustomerId = cust3.Id,
            CustomerName = cust3.Name,
            CustomerPhone = cust3.Phone,
            CustomerAddress = cust3.Address,
            DeviceType = "هاتف ذكي",
            DeviceBrand = "Xiaomi",
            DeviceModel = "Redmi Note 13 Pro",
            DeviceColor = "بنفسجي",
            SerialOrImei = "865412009988771",
            PasscodeType = MaintenancePasscodeType.None,
            ReportedFault = "مشكلة سماعة / مايك / جرس — صوت السماعة الخارجية ضعيف بعد الصيانة السابقة.",
            PhysicalCondition = "سليم ظاهريًا",
            AccessoriesReceived = "كرت SIM، جراب",
            IntakeNotes = "ضمان صيانة خلال 14 يوم.",
            Status = MaintenanceOrderStatus.InRepair,
            Priority = MaintenancePriority.WarrantyRecheck,
            AssignedTechnician = "م. كريم — هاردوير وشاشات",
            IsEstimatePendingInspection = false,
            EstimatedCost = 0m
        };
        ord2.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord2.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة (ضمان)",
            Details = "استلام مراجعة ضمان لضعف صوت السماعة بدون تكلفة إضافية.",
            ActorName = "المشرف"
        });
        list.Add(ord2);

        var ord1 = new MaintenanceOrderDesignItem
        {
            Id = 1,
            OrderNumber = "MNT-2026-0001",
            ReceivedAt = DateTime.Today.AddDays(-1).AddHours(13),
            ReceivedByName = "المشرف",
            ExpectedDeliveryAt = DateTime.Today,
            ExpectedDeliveryLabel = "اليوم مساءً",
            CustomerId = cust1.Id,
            CustomerName = cust1.Name,
            CustomerPhone = cust1.Phone,
            CustomerAddress = cust1.Address,
            DeviceType = "لابتوب",
            DeviceBrand = "Dell",
            DeviceModel = "Inspiron 15 3520",
            DeviceColor = "فضي",
            SerialOrImei = "DL-99X42K1",
            PasscodeType = MaintenancePasscodeType.PinOrPassword,
            PasscodeValue = "1234",
            ReportedFault = "تنظيف وصيانة دورية / حرارة — ارتفاع حرارة المعالج مع صوت عالٍ للمروحة + ويندوز.",
            PhysicalCondition = "خدوش بالهيكل",
            AccessoriesReceived = "شاحن، كابل",
            IntakeNotes = "نسخ ملفات سطح المكتب قبل الفورمات.",
            DiagnosisNotes = "تم تنظيف نظام التبريد وتغيير المعجون الحراري وتثبيت ويندوز 11، واجتياز اختبار الحرارة لمدة ساعتين.",
            LaborCost = 450m,
            Status = MaintenanceOrderStatus.ReadyForPickup,
            Priority = MaintenancePriority.Normal,
            AssignedTechnician = "م. مصطفى — لابتوب وبوردة",
            IsEstimatePendingInspection = false,
            EstimatedCost = 750m,
            DepositInstaPayPaid = 300m,
        };
        ord1.UsedParts.Add(new MaintenancePartUsageItem
        {
            ProductId = 7,
            Barcode = "SPR-007",
            ProductName = "معجون حراري احترافي Arctic MX-4 + وسادات تبريد",
            Quantity = 1,
            UnitCost = 140m,
            UnitSellPrice = 300m,
            IsManualPart = false
        });
        ord1.RecalculateWorkshopTotal();
        ord1.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord1.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة",
            Details = "استلام لابتوب Dell Inspiron 15 مع عربون 300.00 ج.م إنستاباي.",
            ActorName = "المشرف",
            Amount = 300m
        });
        ord1.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord1.ReceivedAt.AddHours(3),
            ActionTitle = "تحويل الحالة إلى: جاهز للتسليم",
            Details = "إتمام التنظيف والويندوز واعتماد التكلفة النهائية 750.00 ج.م.",
            ActorName = "م. مصطفى — لابتوب وبوردة",
            Amount = 750m
        });
        list.Add(ord1);

        var ord5 = new MaintenanceOrderDesignItem
        {
            Id = 5,
            OrderNumber = "MNT-2026-0005",
            ReceivedAt = DateTime.Today.AddDays(-2).AddHours(15),
            ReceivedByName = "المشرف",
            ExpectedDeliveryAt = DateTime.Today.AddDays(1),
            ExpectedDeliveryLabel = "غدًا",
            CustomerId = cust2.Id,
            CustomerName = cust2.Name,
            CustomerPhone = cust2.Phone,
            CustomerAddress = cust2.Address,
            DeviceType = "هاتف ذكي",
            DeviceBrand = "OPPO",
            DeviceModel = "Reno 11 5G",
            DeviceColor = "أخضر",
            SerialOrImei = "864410022993311",
            PasscodeType = MaintenancePasscodeType.PinOrPassword,
            PasscodeValue = "0000",
            ReportedFault = "كسر باغة / شاشة AMOLED تحتاج تغيير كامل.",
            PhysicalCondition = "شاشة مكسورة",
            AccessoriesReceived = "الجهاز فقط (بدون ملحقات)",
            IntakeNotes = "تم إبلاغ العميل بانتظار الشاشة الأصلية.",
            DiagnosisNotes = "البوردة والشحن سليمين؛ بانتظار توريد شاشة Reno 11 الأصلية.",
            LaborCost = 350m,
            Status = MaintenanceOrderStatus.WaitingParts,
            Priority = MaintenancePriority.Normal,
            AssignedTechnician = "م. كريم — هاردوير وشاشات",
            IsEstimatePendingInspection = false,
            EstimatedCost = 2850m,
            DepositCashPaid = 500m
        };
        ord5.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord5.ReceivedAt,
            ActionTitle = "بانتظار قطع غيار",
            Details = "تم فحص الجهاز وطلب شاشة AMOLED أصلية.",
            ActorName = "م. كريم — هاردوير وشاشات"
        });
        list.Add(ord5);

        var ord6 = new MaintenanceOrderDesignItem
        {
            Id = 6,
            OrderNumber = "MNT-2026-0006",
            ReceivedAt = DateTime.Today.AddDays(-2).AddHours(10).AddMinutes(15),
            ReceivedByName = "المشرف",
            ExpectedDeliveryAt = DateTime.Today.AddDays(-1),
            ExpectedDeliveryLabel = "غدًا",
            CustomerId = cust1.Id,
            CustomerName = cust1.Name,
            CustomerPhone = cust1.Phone,
            CustomerAddress = cust1.Address,
            DeviceType = "هاتف ذكي",
            DeviceBrand = "Samsung",
            DeviceModel = "Galaxy A54 5G",
            DeviceColor = "أبيض",
            SerialOrImei = "356011092233445",
            PasscodeType = MaintenancePasscodeType.PinOrPassword,
            PasscodeValue = "7410",
            ReportedFault = "تغيير بطارية / شحن سريع يفصل — البطارية تفرغ بسرعة وتسخن عند الشحن.",
            PhysicalCondition = "سليم ظاهريًا",
            AccessoriesReceived = "جراب",
            IntakeNotes = "العميل طلب بطارية أصلية مع اختبار الشحن السريع.",
            DiagnosisNotes = "تم استبدال البطارية الأصلية وفحص دائرة الشحن السريع 25W وتأكد ثبات الفولت.",
            LaborCost = 300m,
            DiscountAmount = 50m,
            Status = MaintenanceOrderStatus.Delivered,
            Priority = MaintenancePriority.Normal,
            AssignedTechnician = "م. كريم — هاردوير وشاشات",
            IsEstimatePendingInspection = false,
            EstimatedCost = 1150m,
            DepositCashPaid = 200m,
            FinalPaymentVodafonePaid = 900m,
            DeliveredAt = DateTime.Today.AddDays(-1).AddHours(18).AddMinutes(30),
            WarrantyDays = 30,
            DeliveryNotes = "تم اختبار الشحن ونسبة البطارية بحضور العميل."
        };
        ord6.UsedParts.Add(new MaintenancePartUsageItem
        {
            ProductId = 2,
            Barcode = "SPR-002",
            ProductName = "بطارية Samsung Galaxy A54 أصلية",
            Quantity = 1,
            UnitCost = 520m,
            UnitSellPrice = 850m,
            IsManualPart = false
        });
        ord6.RecalculateWorkshopTotal();
        ord6.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord6.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة",
            Details = "استلام Galaxy A54 5G مع تحصيل عربون 200.00 ج.م كاش.",
            ActorName = "المشرف",
            Amount = 200m
        });
        ord6.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord6.ReceivedAt.AddHours(4),
            ActionTitle = "صرف قطعة غيار واعتماد التسعير",
            Details = "تركيب بطارية أصلية واعتماد صافي الفاتورة 1,100.00 ج.م بعد خصم 50.00 ج.م.",
            ActorName = "م. كريم — هاردوير وشاشات",
            Amount = 1100m
        });
        ord6.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord6.DeliveredAt ?? DateTime.Today,
            ActionTitle = "تسليم الجهاز وإغلاق التذكرة",
            Details = "تحصيل نهائي 900.00 ج.م فودافون كاش مع ضمان 30 يومًا.",
            ActorName = "المشرف",
            Amount = 900m
        });
        list.Add(ord6);

        var ord7 = new MaintenanceOrderDesignItem
        {
            Id = 7,
            OrderNumber = "MNT-2026-0007",
            ReceivedAt = DateTime.Today.AddDays(-3).AddHours(12).AddMinutes(10),
            ReceivedByName = "المشرف",
            ExpectedDeliveryAt = DateTime.Today.AddDays(-2),
            ExpectedDeliveryLabel = "بعد الفحص والاتصال بالعميل",
            CustomerId = cust3.Id,
            CustomerName = cust3.Name,
            CustomerPhone = cust3.Phone,
            CustomerAddress = cust3.Address,
            DeviceType = "هاتف ذكي",
            DeviceBrand = "OPPO",
            DeviceModel = "A78 4G",
            DeviceColor = "أسود",
            SerialOrImei = "861223004455661",
            PasscodeType = MaintenancePasscodeType.None,
            ReportedFault = "الجهاز فاصل باور (لا يفتح) بعد سقوطه في الماء.",
            PhysicalCondition = "آثار رطوبة / سوائل",
            AccessoriesReceived = "الجهاز فقط (بدون ملحقات)",
            IntakeNotes = "تم التنبيه باحتمال تلف البوردة الرئيسية بسبب السوائل.",
            DiagnosisNotes = "تلف حاد بالمعالج ومسارات التغذية الرئيسية على البوردة نتيجة تأكسد السوائل.",
            Status = MaintenanceOrderStatus.Cancelled,
            Priority = MaintenancePriority.Normal,
            AssignedTechnician = "م. مصطفى — لابتوب وبوردة",
            IsEstimatePendingInspection = true,
            EstimatedCost = 0m,
            DepositVodafonePaid = 150m,
            RefundedDepositAmount = 150m,
            CancellationReason = "الجهاز لا يُصلح (تلف بالمعالج / البوردة)"
        };
        ord7.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord7.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة",
            Details = "استلام OPPO A78 للفحص مع تحصيل عربون 150.00 ج.م فودافون كاش.",
            ActorName = "المشرف",
            Amount = 150m
        });
        ord7.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord7.ReceivedAt.AddHours(5),
            ActionTitle = "إلغاء أمر الصيانة",
            Details = "السبب: الجهاز لا يُصلح (تلف بالمعالج / البوردة) · استرداد عربون: 150.00 ج.م.",
            ActorName = "المشرف",
            Amount = 150m
        });
        list.Add(ord7);

        return list;
    }
}
