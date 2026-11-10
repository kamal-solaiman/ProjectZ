using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum PurchaseInvoiceStatus
{
    Paid = 0,
    PartialCredit = 1,
    FullCredit = 2,
    Cancelled = 3
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

    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Phone { get; init; } = "";
    public string SecondaryPhone { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public string Address { get; init; } = "";
    public string Notes { get; init; } = "";

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
    public int LineNumber { get; init; }
    public int ProductId { get; init; }
    public string Barcode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Unit { get; init; } = "قطعة";
    public bool TrackSerialNumber { get; init; }
    public IReadOnlyList<int> UnitRegistrationIds { get; init; } = [];
    public IReadOnlyList<string> SerialNumbers { get; init; } = [];
    public int Quantity { get; init; }
    public decimal UnitPurchaseCost { get; init; }

    public decimal LineTotal => Quantity * UnitPurchaseCost;
    public string QuantityDisplay => Quantity.ToString(CultureInfo.InvariantCulture);
    public string UnitPurchaseCostDisplay => PosFormatting.FormatCurrency(UnitPurchaseCost);
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);

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
}

public sealed class PurchaseInvoiceDesignItem : ObservableObject
{
    private PurchaseInvoiceStatus status = PurchaseInvoiceStatus.Paid;
    private decimal cashPaid;
    private decimal vodafoneCashPaid;
    private string vodafoneCashReference = "";
    private decimal instaPayPaid;
    private string instaPayReference = "";
    private string notes = "";

    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public string SupplierInvoiceNumber { get; init; } = "";
    public DateTime IssuedAt { get; init; }
    public string BuyerName { get; init; } = "";

    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string SupplierPhone { get; init; } = "";
    public string SupplierCompany { get; init; } = "";
    public string SupplierAddress { get; init; } = "";

    public decimal DiscountAmount { get; init; }

    public ObservableCollection<PurchaseInvoiceLineDesignItem> Items { get; } = [];
    public ObservableCollection<PurchaseInvoiceActivityEntry> ActivityLog { get; } = [];

    public PurchaseInvoiceStatus Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            Changed();
            Changed(nameof(StatusTitle));
            Changed(nameof(IsPaid));
            Changed(nameof(IsPartialCredit));
            Changed(nameof(IsFullCredit));
            Changed(nameof(IsCancelled));
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

    public string VodafoneCashReference
    {
        get => vodafoneCashReference;
        set
        {
            if (vodafoneCashReference == value) return;
            vodafoneCashReference = value;
            Changed();
            Changed(nameof(HasVodafoneCash));
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

    public string InstaPayReference
    {
        get => instaPayReference;
        set
        {
            if (instaPayReference == value) return;
            instaPayReference = value;
            Changed();
            Changed(nameof(HasInstaPay));
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
    public string ItemsAndUnitsSummary =>
        $"{TotalLineCount} {(TotalLineCount == 1 ? "صنف" : "أصناف")} / {TotalUnitsCount} قطعة";

    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public decimal EffectiveDiscount => Math.Min(Subtotal, DiscountAmount);
    public decimal NetTotal => Math.Max(0m, Subtotal - EffectiveDiscount);
    public decimal TotalPaid => CashPaid + VodafoneCashPaid + InstaPayPaid;
    public decimal RemainingPayable => Status == PurchaseInvoiceStatus.Cancelled
        ? 0m
        : Math.Max(0m, NetTotal - TotalPaid);

    public bool HasDiscount => EffectiveDiscount > 0m;
    public bool HasRemainingPayable => RemainingPayable > 0m;
    public bool HasCashPaid => CashPaid > 0m;
    public bool HasVodafoneCash => VodafoneCashPaid > 0m || !string.IsNullOrWhiteSpace(VodafoneCashReference);
    public bool HasInstaPay => InstaPayPaid > 0m || !string.IsNullOrWhiteSpace(InstaPayReference);

    public string SubtotalDisplay => PosFormatting.FormatCurrency(Subtotal);
    public string EffectiveDiscountDisplay => PosFormatting.FormatCurrency(EffectiveDiscount);
    public string NetTotalDisplay => PosFormatting.FormatCurrency(NetTotal);
    public string CashPaidDisplay => PosFormatting.FormatCurrency(CashPaid);
    public string VodafoneCashPaidDisplay => PosFormatting.FormatCurrency(VodafoneCashPaid);
    public string InstaPayPaidDisplay => PosFormatting.FormatCurrency(InstaPayPaid);
    public string TotalPaidDisplay => PosFormatting.FormatCurrency(TotalPaid);
    public string RemainingPayableDisplay => PosFormatting.FormatCurrency(RemainingPayable);

    public string StatusTitle => Status switch
    {
        PurchaseInvoiceStatus.PartialCredit => "مدفوعة جزئيًا (آجل)",
        PurchaseInvoiceStatus.FullCredit => "آجل بالكامل",
        PurchaseInvoiceStatus.Cancelled => "ملغاة",
        _ => "مدفوعة بالكامل"
    };

    public bool IsPaid => Status == PurchaseInvoiceStatus.Paid;
    public bool IsPartialCredit => Status == PurchaseInvoiceStatus.PartialCredit;
    public bool IsFullCredit => Status == PurchaseInvoiceStatus.FullCredit;
    public bool IsCancelled => Status == PurchaseInvoiceStatus.Cancelled;

    public string PaymentMethodSummary
    {
        get
        {
            if (IsCancelled) return "ملغاة";

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

    public void NotifyFinancialsChanged()
    {
        Changed(nameof(TotalUnitsCount));
        Changed(nameof(ItemsAndUnitsSummary));
        Changed(nameof(Subtotal));
        Changed(nameof(EffectiveDiscount));
        Changed(nameof(NetTotal));
        Changed(nameof(TotalPaid));
        Changed(nameof(RemainingPayable));
        Changed(nameof(HasDiscount));
        Changed(nameof(HasRemainingPayable));
        Changed(nameof(HasCashPaid));
        Changed(nameof(HasVodafoneCash));
        Changed(nameof(HasInstaPay));
        Changed(nameof(SubtotalDisplay));
        Changed(nameof(EffectiveDiscountDisplay));
        Changed(nameof(NetTotalDisplay));
        Changed(nameof(CashPaidDisplay));
        Changed(nameof(VodafoneCashPaidDisplay));
        Changed(nameof(InstaPayPaidDisplay));
        Changed(nameof(TotalPaidDisplay));
        Changed(nameof(RemainingPayableDisplay));
        Changed(nameof(PaymentMethodSummary));
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
            Address = "شارع العطارين — القاهرة",
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
            Address = "المنصورة — شارع الجلاء",
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
            Address = "مدينة نصر — القاهرة",
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
            Address = "وسط البلد — القاهرة",
            TotalPurchasesCount = 0,
            OutstandingPayable = 0m
        },
        new SupplierDesignItem
        {
            Id = 5,
            Name = "الرواد موبايل",
            Phone = "01555443322",
            CompanyName = "الرواد للتوزيع السريع",
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

        var p1 = catalogList.FirstOrDefault(p => p.Id == 1);
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
                VodafoneCashReference = "VF-994120",
                InstaPayPaid = 500m,
                InstaPayReference = "IP-771022",
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

        if (p7 is not null)
        {
            var p7Regs = regList.Where(r => r.ProductId == p7.Id).Take(3).Select(r => r.Id).ToList();
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
                InstaPayReference = "IP-998123",
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
                UnitRegistrationIds = p7Regs,
                Quantity = 3,
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
