using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum PurchaseInvoiceStatus
{
    Paid = 0,
    PartialCredit = 1,
    FullCredit = 2,
    Cancelled = 3,
    PartiallyReturned = 4,
    FullyReturned = 5
}

public enum PurchasePaymentPresetMode
{
    CashOnly = 0,
    VodafoneCashOnly = 1,
    InstaPayOnly = 2,
    Split = 3,
    FullCredit = 4
}

public sealed class SupplierDesignItem : ObservableObject
{
    private int totalPurchasesCount;
    private decimal outstandingPayable;

    private int id;
    private string name = "";
    private string phone = "";
    private string secondaryPhone = "";
    private string companyName = "";
    private string specialization = "";
    private string address = "";
    private string vodafoneCashNumber = "";
    private string instaPayAddress = "";
    private string notes = "";

    public int Id { get => id; init => id = value; }
    public string Name { get => name; set { if (name == value) return; name = value; Changed(); } }
    public string Phone { get => phone; set { if (phone == value) return; phone = value; Changed(); Changed(nameof(DisplayPhone)); Changed(nameof(SearchSubtitle)); } }
    public string SecondaryPhone { get => secondaryPhone; set { if (secondaryPhone == value) return; secondaryPhone = value; Changed(); Changed(nameof(HasSecondaryPhone)); } }
    public string CompanyName { get => companyName; set { if (companyName == value) return; companyName = value; Changed(); Changed(nameof(DisplayCompany)); Changed(nameof(SearchSubtitle)); } }
    public string Specialization { get => specialization; set { if (specialization == value) return; specialization = value; Changed(); Changed(nameof(SpecializationDisplay)); } }
    public string Address { get => address; set { if (address == value) return; address = value; Changed(); Changed(nameof(AddressDisplay)); Changed(nameof(HasAddress)); } }
    public string VodafoneCashNumber { get => vodafoneCashNumber; set { if (vodafoneCashNumber == value) return; vodafoneCashNumber = value; Changed(); Changed(nameof(HasVodafoneCashNumber)); Changed(nameof(VodafoneCashDisplay)); } }
    public string InstaPayAddress { get => instaPayAddress; set { if (instaPayAddress == value) return; instaPayAddress = value; Changed(); Changed(nameof(HasInstaPayAddress)); Changed(nameof(InstaPayDisplay)); } }
    public string Notes { get => notes; set { if (notes == value) return; notes = value; Changed(); Changed(nameof(HasNotes)); } }

    public string SupplierCodeDisplay => $"SUP-{Id:000}";
    public string InitialBadgeText => string.IsNullOrWhiteSpace(Name) ? "م" : Name.Trim()[0].ToString();
    public string SpecializationDisplay => string.IsNullOrWhiteSpace(Specialization) ? "توريد عام" : Specialization;
    public string AddressDisplay => string.IsNullOrWhiteSpace(Address) ? "غير مسجل" : Address;
    public string VodafoneCashDisplay => string.IsNullOrWhiteSpace(VodafoneCashNumber) ? "غير مسجل" : VodafoneCashNumber;
    public string InstaPayDisplay => string.IsNullOrWhiteSpace(InstaPayAddress) ? "غير مسجل" : InstaPayAddress;
    public bool HasSecondaryPhone => !string.IsNullOrWhiteSpace(SecondaryPhone);
    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);
    public bool HasVodafoneCashNumber => !string.IsNullOrWhiteSpace(VodafoneCashNumber);
    public bool HasInstaPayAddress => !string.IsNullOrWhiteSpace(InstaPayAddress);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public int TotalPurchasesCount
    {
        get => totalPurchasesCount;
        set
        {
            if (totalPurchasesCount == value) return;
            totalPurchasesCount = Math.Max(0, value);
            Changed();
        }
    }

    public decimal OutstandingPayable
    {
        get => outstandingPayable;
        set
        {
            if (outstandingPayable == value) return;
            outstandingPayable = Math.Max(0m, value);
            Changed();
            Changed(nameof(OutstandingPayableDisplay));
            Changed(nameof(HasOutstandingPayable));
        }
    }

    public string DisplayPhone => string.IsNullOrWhiteSpace(Phone) ? "بدون هاتف" : Phone;
    public string DisplayCompany => string.IsNullOrWhiteSpace(CompanyName) ? "توريد مباشر" : CompanyName;
    public string SearchSubtitle => string.IsNullOrWhiteSpace(CompanyName)
        ? DisplayPhone
        : $"{CompanyName} · {DisplayPhone}";
    public bool HasOutstandingPayable => OutstandingPayable > 0m;
    public string OutstandingPayableDisplay => PosFormatting.FormatCurrency(OutstandingPayable);
}

public sealed record PurchaseInvoiceActivityEntry
{
    public DateTime Timestamp { get; init; }
    public string ActionTitle { get; init; } = "";
    public string Details { get; init; } = "";
    public string ActorName { get; init; } = "";
    public decimal? Amount { get; init; }

    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string AmountDisplay => Amount.HasValue ? PosFormatting.FormatCurrency(Amount.Value) : "—";
}

public sealed class PurchaseInvoiceLineDesignItem : ObservableObject
{
    private int lineNumber;
    private int quantity;
    private int returnedQuantity;
    private decimal unitPurchaseCost;
    private IReadOnlyList<int> unitRegistrationIds = [];
    private IReadOnlyList<string> serialNumbers = [];

    public int LineNumber
    {
        get => lineNumber;
        set
        {
            if (lineNumber == value) return;
            lineNumber = value;
            Changed();
        }
    }

    /// <summary>dbo.PurchaseInvoiceLines.Id for a saved invoice; 0 for a draft that has never been persisted.</summary>
    public int DatabaseLineId { get; init; }

    public int ProductId { get; init; }
    public string Barcode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Unit { get; init; } = "قطعة";
    public bool TrackSerialNumber { get; init; }

    public IReadOnlyList<int> UnitRegistrationIds
    {
        get => unitRegistrationIds;
        set
        {
            unitRegistrationIds = value ?? [];
            Changed();
            Changed(nameof(SerialSummary));
        }
    }

    public IReadOnlyList<string> SerialNumbers
    {
        get => serialNumbers;
        set
        {
            serialNumbers = value ?? [];
            Changed();
            Changed(nameof(SerialSummary));
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
            NotifyLineChanged();
        }
    }

    public int ReturnedQuantity
    {
        get => returnedQuantity;
        set
        {
            var clamped = Math.Clamp(value, 0, Quantity);
            if (returnedQuantity == clamped) return;
            returnedQuantity = clamped;
            Changed();
            NotifyLineChanged();
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
            NotifyLineChanged();
        }
    }

    public int RemainingQuantity => Math.Max(0, Quantity - ReturnedQuantity);
    public bool HasReturnedQuantity => ReturnedQuantity > 0;
    public decimal LineTotal => Quantity * UnitPurchaseCost;
    public decimal ReturnedAmount => ReturnedQuantity * UnitPurchaseCost;
    public decimal NetActiveLineTotal => RemainingQuantity * UnitPurchaseCost;

    public string QuantityDisplay => Quantity.ToString(CultureInfo.InvariantCulture);
    public string ReturnedQuantityDisplay => ReturnedQuantity.ToString(CultureInfo.InvariantCulture);
    public string RemainingQuantityDisplay => RemainingQuantity.ToString(CultureInfo.InvariantCulture);
    public string UnitPurchaseCostDisplay => PosFormatting.FormatCurrency(UnitPurchaseCost);
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);
    public string ReturnedAmountDisplay => PosFormatting.FormatCurrency(ReturnedAmount);
    public string NetActiveLineTotalDisplay => PosFormatting.FormatCurrency(NetActiveLineTotal);

    public string SerialSummary
    {
        get
        {
            if (SerialNumbers.Count > 0)
                return $"سيريال: {string.Join("، ", SerialNumbers.Take(3))}" + (SerialNumbers.Count > 3 ? $" (+{SerialNumbers.Count - 3})" : "");
            if (UnitRegistrationIds.Count > 0)
                return $"ID مخزون: #{UnitRegistrationIds[0]}" + (UnitRegistrationIds.Count > 1 ? $"..#{UnitRegistrationIds[^1]}" : "");
            return TrackSerialNumber ? "تتبع سيريال" : "بدون سيريال";
        }
    }

    public void NotifyLineChanged()
    {
        Changed(nameof(RemainingQuantity));
        Changed(nameof(HasReturnedQuantity));
        Changed(nameof(LineTotal));
        Changed(nameof(ReturnedAmount));
        Changed(nameof(NetActiveLineTotal));
        Changed(nameof(QuantityDisplay));
        Changed(nameof(ReturnedQuantityDisplay));
        Changed(nameof(RemainingQuantityDisplay));
        Changed(nameof(UnitPurchaseCostDisplay));
        Changed(nameof(LineTotalDisplay));
        Changed(nameof(ReturnedAmountDisplay));
        Changed(nameof(NetActiveLineTotalDisplay));
    }
}

public sealed class PurchaseInvoiceDesignItem : ObservableObject
{
    private PurchaseInvoiceStatus status = PurchaseInvoiceStatus.Paid;
    private string supplierInvoiceNumber = "";
    private DateTime issuedAt;
    private int supplierId;
    private string supplierName = "";
    private string supplierPhone = "";
    private string supplierCompany = "";
    private string supplierAddress = "";
    private decimal discountAmount;
    private decimal cashPaid;
    private decimal vodafoneCashPaid;
    private decimal instaPayPaid;
    private decimal supplierRefundReceived;
    private string notes = "";
    private bool isSelected;

    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public string BuyerName { get; init; } = "";

    public string SupplierInvoiceNumber
    {
        get => supplierInvoiceNumber;
        set
        {
            if (supplierInvoiceNumber == value) return;
            supplierInvoiceNumber = value;
            Changed();
            Changed(nameof(SupplierInvoiceNumberDisplay));
            Changed(nameof(HasSupplierInvoiceNumber));
        }
    }

    public DateTime IssuedAt
    {
        get => issuedAt;
        set
        {
            if (issuedAt == value) return;
            issuedAt = value;
            Changed();
            Changed(nameof(IssuedDateDisplay));
            Changed(nameof(IssuedTimeDisplay));
            Changed(nameof(IssuedFullDisplay));
        }
    }

    public int SupplierId
    {
        get => supplierId;
        set
        {
            if (supplierId == value) return;
            supplierId = value;
            Changed();
        }
    }

    public string SupplierName
    {
        get => supplierName;
        set
        {
            if (supplierName == value) return;
            supplierName = value;
            Changed();
        }
    }

    public string SupplierPhone
    {
        get => supplierPhone;
        set
        {
            if (supplierPhone == value) return;
            supplierPhone = value;
            Changed();
            Changed(nameof(SupplierPhoneDisplay));
        }
    }

    public string SupplierCompany
    {
        get => supplierCompany;
        set
        {
            if (supplierCompany == value) return;
            supplierCompany = value;
            Changed();
            Changed(nameof(SupplierCompanyDisplay));
        }
    }

    public string SupplierAddress
    {
        get => supplierAddress;
        set
        {
            if (supplierAddress == value) return;
            supplierAddress = value;
            Changed();
            Changed(nameof(SupplierAddressDisplay));
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
            NotifyFinancialsChanged();
        }
    }

    public ObservableCollection<PurchaseInvoiceLineDesignItem> Items { get; } = [];
    public ObservableCollection<PurchaseInvoiceActivityEntry> ActivityLog { get; } = [];

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

    public PurchaseInvoiceStatus Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            Changed();
            NotifyStatusChanged();
            NotifyFinancialsChanged();
        }
    }

    public decimal CashPaid
    {
        get => cashPaid;
        set
        {
            if (cashPaid == value) return;
            cashPaid = Math.Max(0m, value);
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal VodafoneCashPaid
    {
        get => vodafoneCashPaid;
        set
        {
            if (vodafoneCashPaid == value) return;
            vodafoneCashPaid = Math.Max(0m, value);
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal InstaPayPaid
    {
        get => instaPayPaid;
        set
        {
            if (instaPayPaid == value) return;
            instaPayPaid = Math.Max(0m, value);
            Changed();
            NotifyFinancialsChanged();
        }
    }

    public decimal SupplierRefundReceived
    {
        get => supplierRefundReceived;
        set
        {
            var clamped = Math.Max(0m, value);
            if (supplierRefundReceived == clamped) return;
            supplierRefundReceived = clamped;
            Changed();
            NotifyFinancialsChanged();
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
            Changed(nameof(HasNotes));
        }
    }

    public string IssuedDateDisplay => IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string IssuedTimeDisplay => IssuedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string IssuedFullDisplay => IssuedAt.ToString("yyyy-MM-dd · HH:mm", CultureInfo.InvariantCulture);
    public string SupplierPhoneDisplay => string.IsNullOrWhiteSpace(SupplierPhone) ? "بدون هاتف" : SupplierPhone;
    public string SupplierAddressDisplay => string.IsNullOrWhiteSpace(SupplierAddress) ? "غير مسجل" : SupplierAddress;
    public string SupplierCompanyDisplay => string.IsNullOrWhiteSpace(SupplierCompany) ? "توريد مباشر" : SupplierCompany;
    public string SupplierInvoiceNumberDisplay => string.IsNullOrWhiteSpace(SupplierInvoiceNumber) ? "—" : SupplierInvoiceNumber;
    public bool HasSupplierInvoiceNumber => !string.IsNullOrWhiteSpace(SupplierInvoiceNumber);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public int TotalLineCount => Items.Count;
    public int TotalUnitsCount => Items.Sum(i => i.Quantity);
    public int TotalReturnedUnitsCount => Items.Sum(i => i.ReturnedQuantity);
    public int ActiveUnitsCount => Items.Sum(i => i.RemainingQuantity);
    public string ItemsAndUnitsSummary => TotalReturnedUnitsCount > 0
        ? $"{TotalLineCount} {(TotalLineCount == 1 ? "صنف" : "أصناف")} / {ActiveUnitsCount} من {TotalUnitsCount} قطعة"
        : $"{TotalLineCount} {(TotalLineCount == 1 ? "صنف" : "أصناف")} / {TotalUnitsCount} قطعة";

    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public decimal ReturnedGoodsTotal => Items.Sum(i => i.ReturnedAmount);
    public bool HasReturnedGoods => ReturnedGoodsTotal > 0m;
    public decimal ActiveGrossTotal => Math.Max(0m, Subtotal - ReturnedGoodsTotal);
    public decimal EffectiveDiscount => Math.Min(ActiveGrossTotal, DiscountAmount);
    public decimal NetTotal => Status == PurchaseInvoiceStatus.Cancelled
        ? 0m
        : Math.Max(0m, ActiveGrossTotal - EffectiveDiscount);
    public decimal TotalPaid => CashPaid + VodafoneCashPaid + InstaPayPaid;
    public decimal NetCashOutflow => Status == PurchaseInvoiceStatus.Cancelled
        ? 0m
        : Math.Max(0m, TotalPaid - SupplierRefundReceived);
    public decimal RemainingPayable => Status == PurchaseInvoiceStatus.Cancelled || Status == PurchaseInvoiceStatus.FullyReturned
        ? 0m
        : Math.Max(0m, NetTotal - NetCashOutflow);

    public bool HasDiscount => EffectiveDiscount > 0m;
    public bool HasRemainingPayable => RemainingPayable > 0m;
    public bool HasCashPaid => CashPaid > 0m;
    public bool HasVodafoneCash => VodafoneCashPaid > 0m;
    public bool HasInstaPay => InstaPayPaid > 0m;
    public bool HasSupplierRefundReceived => SupplierRefundReceived > 0m;

    public string SubtotalDisplay => PosFormatting.FormatCurrency(Subtotal);
    public string ReturnedGoodsTotalDisplay => PosFormatting.FormatCurrency(ReturnedGoodsTotal);
    public string ActiveGrossTotalDisplay => PosFormatting.FormatCurrency(ActiveGrossTotal);
    public string EffectiveDiscountDisplay => PosFormatting.FormatCurrency(EffectiveDiscount);
    public string NetTotalDisplay => PosFormatting.FormatCurrency(NetTotal);
    public string CashPaidDisplay => PosFormatting.FormatCurrency(CashPaid);
    public string VodafoneCashPaidDisplay => PosFormatting.FormatCurrency(VodafoneCashPaid);
    public string InstaPayPaidDisplay => PosFormatting.FormatCurrency(InstaPayPaid);
    public string TotalPaidDisplay => PosFormatting.FormatCurrency(TotalPaid);
    public string SupplierRefundReceivedDisplay => PosFormatting.FormatCurrency(SupplierRefundReceived);
    public string NetCashOutflowDisplay => PosFormatting.FormatCurrency(NetCashOutflow);
    public string RemainingPayableDisplay => PosFormatting.FormatCurrency(RemainingPayable);

    public string StatusTitle => Status switch
    {
        PurchaseInvoiceStatus.PartialCredit => "مدفوعة جزئيًا (آجل)",
        PurchaseInvoiceStatus.FullCredit => "آجل بالكامل",
        PurchaseInvoiceStatus.PartiallyReturned => "مرتجع جزئي",
        PurchaseInvoiceStatus.FullyReturned => "مرتجع كلي",
        PurchaseInvoiceStatus.Cancelled => "ملغاة",
        _ => "مدفوعة بالكامل"
    };

    public bool IsPaid => Status == PurchaseInvoiceStatus.Paid;
    public bool IsPartialCredit => Status == PurchaseInvoiceStatus.PartialCredit;
    public bool IsFullCredit => Status == PurchaseInvoiceStatus.FullCredit;
    public bool IsPartiallyReturned => Status == PurchaseInvoiceStatus.PartiallyReturned;
    public bool IsFullyReturned => Status == PurchaseInvoiceStatus.FullyReturned;
    public bool IsCancelled => Status == PurchaseInvoiceStatus.Cancelled;

    public bool CanPayRemaining => !IsCancelled && !IsFullyReturned && RemainingPayable > 0m;
    public bool CanEdit => !IsCancelled && !IsFullyReturned;
    public bool CanReturn => !IsCancelled && !IsFullyReturned && Items.Any(i => i.RemainingQuantity > 0);
    public bool CanCancel => !IsCancelled && !IsFullyReturned;

    public string PaymentMethodSummary
    {
        get
        {
            if (IsCancelled) return "ملغاة";
            if (IsFullyReturned) return HasSupplierRefundReceived ? "مرتجع كلي (مسترد)" : "مرتجع كلي";

            var channels = new List<string>(3);
            if (CashPaid > 0m) channels.Add("كاش");
            if (VodafoneCashPaid > 0m) channels.Add("فودافون كاش");
            if (InstaPayPaid > 0m) channels.Add("إنستاباي");

            if (channels.Count == 0)
                return HasRemainingPayable ? "آجل بالكامل للمورد" : "غير محدد";

            if (channels.Count == 1)
                return HasRemainingPayable ? $"{channels[0]} + آجل" : channels[0];

            return HasRemainingPayable
                ? $"مقسم ({string.Join(" + ", channels)}) + آجل"
                : $"مقسم ({string.Join(" + ", channels)})";
        }
    }

    public void RecalculateStatusFromBalances()
    {
        if (IsCancelled) return;

        var anyItems = Items.Count > 0;
        var allReturned = anyItems && Items.All(i => i.RemainingQuantity == 0);
        var anyReturned = Items.Any(i => i.ReturnedQuantity > 0);

        if (allReturned)
        {
            Status = PurchaseInvoiceStatus.FullyReturned;
        }
        else if (anyReturned)
        {
            Status = PurchaseInvoiceStatus.PartiallyReturned;
        }
        else if (RemainingPayable <= 0m)
        {
            Status = PurchaseInvoiceStatus.Paid;
        }
        else if (NetCashOutflow <= 0m)
        {
            Status = PurchaseInvoiceStatus.FullCredit;
        }
        else
        {
            Status = PurchaseInvoiceStatus.PartialCredit;
        }
    }

    private void NotifyStatusChanged()
    {
        Changed(nameof(StatusTitle));
        Changed(nameof(IsPaid));
        Changed(nameof(IsPartialCredit));
        Changed(nameof(IsFullCredit));
        Changed(nameof(IsPartiallyReturned));
        Changed(nameof(IsFullyReturned));
        Changed(nameof(IsCancelled));
        Changed(nameof(CanPayRemaining));
        Changed(nameof(CanEdit));
        Changed(nameof(CanReturn));
        Changed(nameof(CanCancel));
    }

    public void NotifyFinancialsChanged()
    {
        Changed(nameof(TotalLineCount));
        Changed(nameof(TotalUnitsCount));
        Changed(nameof(TotalReturnedUnitsCount));
        Changed(nameof(ActiveUnitsCount));
        Changed(nameof(ItemsAndUnitsSummary));
        Changed(nameof(Subtotal));
        Changed(nameof(ReturnedGoodsTotal));
        Changed(nameof(HasReturnedGoods));
        Changed(nameof(ActiveGrossTotal));
        Changed(nameof(EffectiveDiscount));
        Changed(nameof(NetTotal));
        Changed(nameof(TotalPaid));
        Changed(nameof(NetCashOutflow));
        Changed(nameof(RemainingPayable));
        Changed(nameof(HasDiscount));
        Changed(nameof(HasRemainingPayable));
        Changed(nameof(HasCashPaid));
        Changed(nameof(HasVodafoneCash));
        Changed(nameof(HasInstaPay));
        Changed(nameof(HasSupplierRefundReceived));
        Changed(nameof(SubtotalDisplay));
        Changed(nameof(ReturnedGoodsTotalDisplay));
        Changed(nameof(ActiveGrossTotalDisplay));
        Changed(nameof(EffectiveDiscountDisplay));
        Changed(nameof(NetTotalDisplay));
        Changed(nameof(CashPaidDisplay));
        Changed(nameof(VodafoneCashPaidDisplay));
        Changed(nameof(InstaPayPaidDisplay));
        Changed(nameof(TotalPaidDisplay));
        Changed(nameof(SupplierRefundReceivedDisplay));
        Changed(nameof(NetCashOutflowDisplay));
        Changed(nameof(RemainingPayableDisplay));
        Changed(nameof(PaymentMethodSummary));
        Changed(nameof(CanPayRemaining));
        Changed(nameof(CanEdit));
        Changed(nameof(CanReturn));
        Changed(nameof(CanCancel));
    }
}

public static class PurchaseDesignSeedData
{
    public static ObservableCollection<SupplierDesignItem> CreateSharedSampleSuppliers() =>
    [
        new SupplierDesignItem
        {
            Id = 1,
            Name = "أحمد علي",
            Phone = "01015501122",
            SecondaryPhone = "01122003344",
            CompanyName = "مؤسسة أحمد علي للتجارة",
            Specialization = "شواحن وإكسسوارات",
            Address = "شارع العطارين — القاهرة",
            VodafoneCashNumber = "01015501122",
            InstaPayAddress = "ahmed.ali@instapay",
            Notes = "مورد شواحن وإكسسوارات أصلية",
            TotalPurchasesCount = 1,
            OutstandingPayable = 0m
        },
        new SupplierDesignItem
        {
            Id = 2,
            Name = "محمود كامل",
            Phone = "01122334455",
            CompanyName = "كامل جروب للتوزيع",
            Specialization = "هواتف وقطع غيار",
            Address = "المنصورة — شارع الجلاء",
            VodafoneCashNumber = "01122334455",
            Notes = "إمكانية السداد المقسم والآجل 15 يوم",
            TotalPurchasesCount = 1,
            OutstandingPayable = 2600m
        },
        new SupplierDesignItem
        {
            Id = 3,
            Name = "شركة النور للتوزيع",
            Phone = "01288990011",
            SecondaryPhone = "01066554433",
            CompanyName = "النور للإلكترونيات والهواتف",
            Specialization = "هواتف وسماعات",
            Address = "مدينة نصر — القاهرة",
            VodafoneCashNumber = "01288990011",
            InstaPayAddress = "alnoor@instapay",
            Notes = "توريد هواتف وسماعات بالضمان",
            TotalPurchasesCount = 1,
            OutstandingPayable = 0m
        },
        new SupplierDesignItem
        {
            Id = 4,
            Name = "الفتح للإلكترونيات",
            Phone = "01099887766",
            CompanyName = "شركة الفتح للتوريد",
            Specialization = "إلكترونيات",
            Address = "وسط البلد — القاهرة",
            VodafoneCashNumber = "01099887766",
            InstaPayAddress = "elfath@instapay",
            TotalPurchasesCount = 0,
            OutstandingPayable = 0m
        },
        new SupplierDesignItem
        {
            Id = 5,
            Name = "الرواد موبايل",
            Phone = "01555443322",
            CompanyName = "الرواد للتوزيع السريع",
            Specialization = "توزيع هواتف",
            Address = "طنطا — شارع البحر",
            TotalPurchasesCount = 0,
            OutstandingPayable = 0m
        }
    ];

    public static ObservableCollection<PurchaseInvoiceDesignItem> CreateSharedSamplePurchaseInvoices(
        IEnumerable<PosProductDesignItem> catalog,
        IEnumerable<InventoryUnitRegistrationItem> registrations,
        IEnumerable<SupplierDesignItem> suppliers)
    {
        var list = new ObservableCollection<PurchaseInvoiceDesignItem>();
        var catalogList = catalog.ToList();
        var regList = registrations.ToList();
        var supList = suppliers.ToList();

        var sup1 = supList.FirstOrDefault(s => s.Name == "أحمد علي") ?? supList.First();
        var sup2 = supList.FirstOrDefault(s => s.Name == "محمود كامل") ?? supList.First();
        var sup3 = supList.FirstOrDefault(s => s.Name == "شركة النور للتوزيع") ?? supList.First();
        var sup4 = supList.FirstOrDefault(s => s.Name == "الفتح للإلكترونيات") ?? supList.First();

        var p1 = catalogList.FirstOrDefault(p => p.Id == 1);
        var p3 = catalogList.FirstOrDefault(p => p.Id == 3);
        var p7 = catalogList.FirstOrDefault(p => p.Id == 7);

        if (p1 is not null)
        {
            var inv118Regs = regList.Where(r => r.ProductId == p1.Id && r.PurchaseInvoiceId == "INV-118").Select(r => r.Id).ToList();
            var pur1 = new PurchaseInvoiceDesignItem
            {
                Id = 1,
                InvoiceNumber = "PUR-2026-0001",
                SupplierInvoiceNumber = "INV-118",
                IssuedAt = new DateTime(2026, 11, 5, 14, 15, 0),
                BuyerName = "المشرف",
                SupplierId = sup1.Id,
                SupplierName = sup1.Name,
                SupplierPhone = sup1.Phone,
                SupplierCompany = sup1.CompanyName,
                SupplierAddress = sup1.Address,
                Status = PurchaseInvoiceStatus.Paid,
                CashPaid = 550m,
                VodafoneCashPaid = 500m,
                InstaPayPaid = 500m,
                Notes = "توريد دفعة شواحن سريعة — دفع مقسم بين كاش وفودافون وإنستاباي."
            };
            pur1.Items.Add(new PurchaseInvoiceLineDesignItem
            {
                LineNumber = 1,
                ProductId = p1.Id,
                Barcode = p1.Barcode,
                ProductName = p1.Name,
                Category = p1.Category,
                Unit = p1.UnitDisplay,
                TrackSerialNumber = p1.TrackSerialNumber,
                UnitRegistrationIds = inv118Regs,
                Quantity = 5,
                UnitPurchaseCost = 310m
            });
            pur1.ActivityLog.Add(new PurchaseInvoiceActivityEntry
            {
                Timestamp = pur1.IssuedAt,
                ActionTitle = "اعتماد فاتورة شراء",
                Details = $"توريد 5 قطع للمخزون باسم المورد {sup1.Name} (دفع مقسم: كاش + فودافون + إنستاباي).",
                ActorName = "المشرف",
                Amount = pur1.NetTotal
            });
            list.Add(pur1);

            var inv101Regs = regList.Where(r => r.ProductId == p1.Id && r.PurchaseInvoiceId == "INV-101").Select(r => r.Id).ToList();
            var pur2 = new PurchaseInvoiceDesignItem
            {
                Id = 2,
                InvoiceNumber = "PUR-2026-0002",
                SupplierInvoiceNumber = "INV-101",
                IssuedAt = new DateTime(2026, 10, 15, 11, 30, 0),
                BuyerName = "المشرف",
                SupplierId = sup2.Id,
                SupplierName = sup2.Name,
                SupplierPhone = sup2.Phone,
                SupplierCompany = sup2.CompanyName,
                SupplierAddress = sup2.Address,
                Status = PurchaseInvoiceStatus.PartialCredit,
                DiscountAmount = 0m,
                CashPaid = 4000m,
                Notes = "دفعة أولى كاش والباقي 2,600.00 ج.م آجل للمورد."
            };
            pur2.Items.Add(new PurchaseInvoiceLineDesignItem
            {
                LineNumber = 1,
                ProductId = p1.Id,
                Barcode = p1.Barcode,
                ProductName = p1.Name,
                Category = p1.Category,
                Unit = p1.UnitDisplay,
                TrackSerialNumber = p1.TrackSerialNumber,
                UnitRegistrationIds = inv101Regs,
                Quantity = 22,
                UnitPurchaseCost = 300m
            });
            pur2.ActivityLog.Add(new PurchaseInvoiceActivityEntry
            {
                Timestamp = pur2.IssuedAt,
                ActionTitle = "اعتماد فاتورة شراء (سداد جزئي)",
                Details = "تم توريد 22 قطعة وسداد 4,000.00 ج.م نقدًا مع بقاء 2,600.00 ج.م آجل للمورد.",
                ActorName = "المشرف",
                Amount = 4000m
            });
            list.Add(pur2);
        }

        if (p3 is not null)
        {
            var p3AvailableRegs = regList
                .Where(r => r.ProductId == p3.Id && r.PurchaseInvoiceId == "INV-103" && r.IsAvailable)
                .ToList();
            if (p3AvailableRegs.Count > 0)
            {
                var pur4 = new PurchaseInvoiceDesignItem
                {
                    Id = 4,
                    InvoiceNumber = "PUR-2026-0004",
                    SupplierInvoiceNumber = "FTH-2026-88",
                    IssuedAt = DateTime.Today.AddDays(-1).AddHours(15),
                    BuyerName = "المشرف",
                    SupplierId = sup4.Id,
                    SupplierName = sup4.Name,
                    SupplierPhone = sup4.Phone,
                    SupplierCompany = sup4.CompanyName,
                    SupplierAddress = sup4.Address,
                    Status = PurchaseInvoiceStatus.FullCredit,
                    DiscountAmount = 0m,
                    Notes = "توريد آجل بالكامل لمدة 7 أيام — بانتظار السداد للمورد."
                };
                pur4.Items.Add(new PurchaseInvoiceLineDesignItem
                {
                    LineNumber = 1,
                    ProductId = p3.Id,
                    Barcode = p3.Barcode,
                    ProductName = p3.Name,
                    Category = p3.Category,
                    Unit = p3.UnitDisplay,
                    TrackSerialNumber = p3.TrackSerialNumber,
                    UnitRegistrationIds = p3AvailableRegs.Select(r => r.Id).ToList(),
                    SerialNumbers = p3AvailableRegs.Where(r => !string.IsNullOrWhiteSpace(r.SerialNumber)).Select(r => r.SerialNumber).ToList(),
                    Quantity = p3AvailableRegs.Count,
                    UnitPurchaseCost = p3.CostPrice
                });
                pur4.ActivityLog.Add(new PurchaseInvoiceActivityEntry
                {
                    Timestamp = pur4.IssuedAt,
                    ActionTitle = "اعتماد فاتورة شراء (آجل بالكامل)",
                    Details = $"توريد {p3AvailableRegs.Count} قطعة من المورد {sup4.Name} بقيمة {pur4.NetTotalDisplay} على الحساب الآجل.",
                    ActorName = "المشرف",
                    Amount = pur4.NetTotal
                });
                sup4.TotalPurchasesCount += 1;
                sup4.OutstandingPayable += pur4.NetTotal;
                list.Insert(0, pur4);
            }
        }

        if (p7 is not null)
        {
            var p7Regs = regList.Where(r => r.ProductId == p7.Id && r.IsAvailable).Take(3).ToList();
            var pur3 = new PurchaseInvoiceDesignItem
            {
                Id = 3,
                InvoiceNumber = "PUR-2026-0003",
                SupplierInvoiceNumber = "NOR-2026-44",
                IssuedAt = DateTime.Today.AddHours(10),
                BuyerName = "المشرف",
                SupplierId = sup3.Id,
                SupplierName = sup3.Name,
                SupplierPhone = sup3.Phone,
                SupplierCompany = sup3.CompanyName,
                SupplierAddress = sup3.Address,
                Status = PurchaseInvoiceStatus.Paid,
                DiscountAmount = 400m,
                InstaPayPaid = 50000m,
                Notes = "دفعة هواتف سامسونج — تحويل إنستاباي."
            };
            pur3.Items.Add(new PurchaseInvoiceLineDesignItem
            {
                LineNumber = 1,
                ProductId = p7.Id,
                Barcode = p7.Barcode,
                ProductName = p7.Name,
                Category = p7.Category,
                Unit = p7.UnitDisplay,
                TrackSerialNumber = p7.TrackSerialNumber,
                UnitRegistrationIds = p7Regs.Select(r => r.Id).ToList(),
                SerialNumbers = p7Regs.Where(r => !string.IsNullOrWhiteSpace(r.SerialNumber)).Select(r => r.SerialNumber).ToList(),
                Quantity = p7Regs.Count,
                UnitPurchaseCost = 16800m
            });
            pur3.ActivityLog.Add(new PurchaseInvoiceActivityEntry
            {
                Timestamp = pur3.IssuedAt,
                ActionTitle = "اعتماد فاتورة شراء",
                Details = "توريد 3 هواتف سامسونج Galaxy A55 وسداد 50,000.00 ج.م عبر إنستاباي بعد خصم 400 ج.م.",
                ActorName = "المشرف",
                Amount = 50000m
            });
            list.Insert(0, pur3);
        }

        return list;
    }
}
