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

    public decimal LineTotal => Quantity * UnitSellPrice;
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);
}

public sealed class MaintenanceOrderDesignItem : ObservableObject
{
    private MaintenanceOrderStatus status = MaintenanceOrderStatus.Received;
    private MaintenancePriority priority = MaintenancePriority.Normal;
    private string assignedTechnician = "";
    private string diagnosisNotes = "";
    private decimal? finalServiceCost;
    private decimal depositCashPaid;
    private decimal depositVodafonePaid;
    private string depositVodafoneReference = "";
    private decimal depositInstaPayPaid;
    private string depositInstaPayReference = "";

    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public DateTime ReceivedAt { get; init; }
    public string ReceivedByName { get; init; } = "";
    public DateTime? ExpectedDeliveryAt { get; init; }
    public string ExpectedDeliveryLabel { get; init; } = "";

    // Customer snapshot
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string CustomerPhone { get; init; } = "";
    public string CustomerSecondaryPhone { get; init; } = "";
    public string CustomerAddress { get; init; } = "";
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
            Changed(nameof(IsReceived));
            Changed(nameof(IsDiagnosing));
            Changed(nameof(IsInRepair));
            Changed(nameof(IsWaitingParts));
            Changed(nameof(IsReadyForPickup));
            Changed(nameof(IsDelivered));
            Changed(nameof(IsCancelled));
            Changed(nameof(IsActiveOrder));
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
        }
    }

    public decimal? FinalServiceCost
    {
        get => finalServiceCost;
        set
        {
            if (finalServiceCost == value) return;
            finalServiceCost = value;
            Changed();
            NotifyFinancialsChanged();
        }
    }

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

    public string DepositVodafoneReference
    {
        get => depositVodafoneReference;
        set
        {
            if (depositVodafoneReference == value) return;
            depositVodafoneReference = value;
            Changed();
            Changed(nameof(HasDepositVodafone));
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

    public string DepositInstaPayReference
    {
        get => depositInstaPayReference;
        set
        {
            if (depositInstaPayReference == value) return;
            depositInstaPayReference = value;
            Changed();
            Changed(nameof(HasDepositInstaPay));
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

    public string PriorityTitle => Priority switch
    {
        MaintenancePriority.Urgent => "مستعجل",
        MaintenancePriority.WarrantyRecheck => "ضمان / إعادة فحص",
        _ => "عادي"
    };

    public bool IsUrgent => Priority == MaintenancePriority.Urgent;
    public bool IsWarranty => Priority == MaintenancePriority.WarrantyRecheck;

    public decimal TotalDepositPaid => DepositCashPaid + DepositVodafonePaid + DepositInstaPayPaid;
    public bool HasDeposit => TotalDepositPaid > 0m;
    public bool HasDepositCash => DepositCashPaid > 0m;
    public bool HasDepositVodafone => DepositVodafonePaid > 0m || !string.IsNullOrWhiteSpace(DepositVodafoneReference);
    public bool HasDepositInstaPay => DepositInstaPayPaid > 0m || !string.IsNullOrWhiteSpace(DepositInstaPayReference);

    public decimal CurrentEffectiveCost => FinalServiceCost ?? (IsEstimatePendingInspection ? 0m : EstimatedCost);
    public decimal EstimatedRemaining => IsEstimatePendingInspection && !FinalServiceCost.HasValue
        ? 0m
        : Math.Max(0m, CurrentEffectiveCost - TotalDepositPaid);
    public bool HasEstimatedRemaining => !IsEstimatePendingInspection && EstimatedRemaining > 0m;

    public string EstimatedCostDisplay => IsEstimatePendingInspection && !FinalServiceCost.HasValue
        ? "يُحدد بعد الفحص"
        : PosFormatting.FormatCurrency(CurrentEffectiveCost);

    public string TotalDepositPaidDisplay => PosFormatting.FormatCurrency(TotalDepositPaid);
    public string DepositCashPaidDisplay => PosFormatting.FormatCurrency(DepositCashPaid);
    public string DepositVodafonePaidDisplay => PosFormatting.FormatCurrency(DepositVodafonePaid);
    public string DepositInstaPayPaidDisplay => PosFormatting.FormatCurrency(DepositInstaPayPaid);
    public string EstimatedRemainingDisplay => IsEstimatePendingInspection && !FinalServiceCost.HasValue
        ? "يتحدد بعد الفحص"
        : PosFormatting.FormatCurrency(EstimatedRemaining);

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

    public void NotifyFinancialsChanged()
    {
        Changed(nameof(TotalDepositPaid));
        Changed(nameof(HasDeposit));
        Changed(nameof(HasDepositCash));
        Changed(nameof(HasDepositVodafone));
        Changed(nameof(HasDepositInstaPay));
        Changed(nameof(CurrentEffectiveCost));
        Changed(nameof(EstimatedRemaining));
        Changed(nameof(HasEstimatedRemaining));
        Changed(nameof(EstimatedCostDisplay));
        Changed(nameof(TotalDepositPaidDisplay));
        Changed(nameof(DepositCashPaidDisplay));
        Changed(nameof(DepositVodafonePaidDisplay));
        Changed(nameof(DepositInstaPayPaidDisplay));
        Changed(nameof(EstimatedRemainingDisplay));
        Changed(nameof(DepositSummaryText));
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
            DepositVodafoneReference = "VF-441022"
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
            Status = MaintenanceOrderStatus.ReadyForPickup,
            Priority = MaintenancePriority.Normal,
            AssignedTechnician = "م. مصطفى — لابتوب وبوردة",
            IsEstimatePendingInspection = false,
            EstimatedCost = 750m,
            DepositInstaPayPaid = 300m,
            DepositInstaPayReference = "IP-882014"
        };
        ord1.ActivityLog.Add(new MaintenanceActivityEntry
        {
            Timestamp = ord1.ReceivedAt,
            ActionTitle = "استلام جهاز صيانة",
            Details = "استلام لابتوب Dell Inspiron 15 مع عربون 300.00 ج.م إنستاباي.",
            ActorName = "المشرف",
            Amount = 300m
        });
        list.Add(ord1);

        return list;
    }
}
