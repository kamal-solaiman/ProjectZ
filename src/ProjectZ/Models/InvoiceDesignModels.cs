using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum SalesInvoiceStatus
{
    Completed = 0,
    PartialCredit = 1,
    PartiallyReturned = 2,
    FullyReturned = 3,
    Cancelled = 4,
    Exchanged = 5
}

public sealed class SalesInvoiceActivityEntry
{
    public DateTime Timestamp { get; init; }
    public string ActionTitle { get; init; } = "";
    public string Details { get; init; } = "";
    public string ActorName { get; init; } = "";
    public decimal Amount { get; init; }

    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public bool HasAmount => Amount > 0m;
    public string AmountDisplay => HasAmount ? PosFormatting.FormatCurrency(Amount) : "";
}

public sealed class SalesInvoiceLineDesignItem : ObservableObject
{
    private int returnedQuantity;

    public int LineNumber { get; init; }
    public int ProductId { get; init; }
    public string Barcode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Unit { get; init; } = "قطعة";
    public bool TrackSerialNumber { get; init; }
    public List<int> UnitRegistrationIds { get; init; } = [];
    public List<string> SerialNumbers { get; init; } = [];
    public PriceTier PriceTier { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal UnitCost { get; init; }
    public bool IsExchangeReplacement { get; init; }

    public int ReturnedQuantity
    {
        get => returnedQuantity;
        set
        {
            var clamped = Math.Clamp(value, 0, Quantity);
            if (returnedQuantity == clamped) return;
            returnedQuantity = clamped;
            Changed();
            Changed(nameof(RemainingQuantity));
            Changed(nameof(HasReturned));
            Changed(nameof(ReturnedSummaryDisplay));
            Changed(nameof(LineReturnedTotal));
            Changed(nameof(LineNetTotal));
            Changed(nameof(LineNetTotalDisplay));
            Changed(nameof(LineCostTotal));
            Changed(nameof(LineCostTotalDisplay));
            Changed(nameof(LineProfit));
            Changed(nameof(LineProfitDisplay));
        }
    }

    public int RemainingQuantity => Math.Max(0, Quantity - ReturnedQuantity);
    public bool HasReturned => ReturnedQuantity > 0;
    public string PriceTierTitle => PosFormatting.GetPriceTierShortTitle(PriceTier);
    public string QuantityDisplay => Quantity.ToString(CultureInfo.InvariantCulture);
    public string ReturnedSummaryDisplay => ReturnedQuantity > 0
        ? $"مرتجع: {ReturnedQuantity}"
        : "";

    public string SerialSummary
    {
        get
        {
            if (SerialNumbers.Count > 0)
            {
                var ids = UnitRegistrationIds.Count > 0
                    ? $" · ID: {string.Join(", ", UnitRegistrationIds.Select(i => $"#{i}"))}"
                    : "";
                return $"IMEI/SN: {string.Join("، ", SerialNumbers)}{ids}";
            }

            if (UnitRegistrationIds.Count > 0)
            {
                if (UnitRegistrationIds.Count <= 3)
                    return $"IDs: {string.Join(", ", UnitRegistrationIds.Select(i => $"#{i}"))}";
                return $"IDs: #{UnitRegistrationIds[0]} .. #{UnitRegistrationIds[^1]} ({UnitRegistrationIds.Count} قطع)";
            }

            return "بدون سيريال مسجل";
        }
    }

    public decimal LineGrossTotal => UnitPrice * Quantity;
    public decimal LineReturnedTotal => UnitPrice * ReturnedQuantity;
    public decimal LineNetTotal => UnitPrice * RemainingQuantity;
    public decimal LineCostTotal => UnitCost * RemainingQuantity;
    public decimal LineProfit => LineNetTotal - LineCostTotal;

    public string UnitPriceDisplay => PosFormatting.FormatCurrency(UnitPrice);
    public string LineGrossTotalDisplay => PosFormatting.FormatCurrency(LineGrossTotal);
    public string LineNetTotalDisplay => PosFormatting.FormatCurrency(LineNetTotal);
    public string LineCostTotalDisplay => PosFormatting.FormatCurrency(LineCostTotal);
    public string LineProfitDisplay => PosFormatting.FormatCurrency(LineProfit);
}

public sealed class SalesInvoiceDesignItem : ObservableObject
{
    private SalesInvoiceStatus status = SalesInvoiceStatus.Completed;
    private decimal cashPaid;
    private decimal vodafoneCashPaid;
    private string vodafoneCashReference = "";
    private decimal instaPayPaid;
    private string instaPayReference = "";
    private decimal refundedAmount;
    private string notes = "";
    private bool isSelected;
    private bool isReturnWorkspaceSelected;

    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public DateTime IssuedAt { get; init; }
    public string CashierName { get; init; } = "";
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "عميل نقدي (عام)";
    public string CustomerPhone { get; init; } = "";
    public string CustomerAddress { get; init; } = "";
    public bool IsWalkInCustomer { get; init; } = true;
    public PriceTier DefaultPriceTier { get; init; } = PriceTier.Retail;
    public decimal DiscountAmount { get; init; }

    public ObservableCollection<SalesInvoiceLineDesignItem> Items { get; } = [];
    public ObservableCollection<SalesInvoiceActivityEntry> ActivityLog { get; } = [];

    public SalesInvoiceStatus Status
    {
        get => status;
        set
        {
            if (status == value) return;
            status = value;
            Changed();
            Changed(nameof(StatusTitle));
            Changed(nameof(IsCompleted));
            Changed(nameof(IsPartialCredit));
            Changed(nameof(IsPartiallyReturned));
            Changed(nameof(IsFullyReturned));
            Changed(nameof(IsCancelled));
            Changed(nameof(IsExchanged));
            Changed(nameof(CanReturn));
            Changed(nameof(CanCollectRemaining));
            Changed(nameof(CanCancel));
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

    public decimal RefundedAmount
    {
        get => refundedAmount;
        set
        {
            if (refundedAmount == value) return;
            refundedAmount = Math.Max(0m, value);
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

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected == value) return;
            isSelected = value;
            Changed();
        }
    }

    public bool IsReturnWorkspaceSelected
    {
        get => isReturnWorkspaceSelected;
        internal set
        {
            if (isReturnWorkspaceSelected == value) return;
            isReturnWorkspaceSelected = value;
            Changed();
        }
    }

    public string IssuedDateDisplay => IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string IssuedTimeDisplay => IssuedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string IssuedFullDisplay => IssuedAt.ToString("yyyy-MM-dd · HH:mm", CultureInfo.InvariantCulture);
    public string CustomerPhoneDisplay => string.IsNullOrWhiteSpace(CustomerPhone) ? "بدون هاتف" : CustomerPhone;
    public string CustomerAddressDisplay => string.IsNullOrWhiteSpace(CustomerAddress) ? "غير مسجل" : CustomerAddress;
    public string CustomerTypeDisplay => IsWalkInCustomer ? "عميل نقدي عام" : "عميل مسجل";
    public string DefaultPriceTierTitle => PosFormatting.GetPriceTierTitle(DefaultPriceTier);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public int TotalLineCount => Items.Count;
    public int TotalUnitsCount => Items.Sum(i => i.Quantity);
    public int ReturnedUnitsCount => Items.Sum(i => i.ReturnedQuantity);
    public string ItemsAndUnitsSummary => ReturnedUnitsCount > 0
        ? $"{TotalLineCount} صنف / {TotalUnitsCount} قطعة (مرتجع {ReturnedUnitsCount})"
        : $"{TotalLineCount} {(TotalLineCount == 1 ? "صنف" : "أصناف")} / {TotalUnitsCount} قطعة";

    public decimal Subtotal => Items.Sum(i => i.LineGrossTotal);
    public decimal EffectiveDiscount => Math.Min(Subtotal, DiscountAmount);
    public decimal NetTotal => Math.Max(0m, Subtotal - EffectiveDiscount);
    public decimal ReturnedTotal => Status == SalesInvoiceStatus.FullyReturned
        ? NetTotal
        : Math.Min(NetTotal, Items.Sum(i => i.LineReturnedTotal));
    public decimal EffectiveNetSales => Status is SalesInvoiceStatus.Cancelled or SalesInvoiceStatus.FullyReturned
        ? 0m
        : Math.Max(0m, NetTotal - ReturnedTotal);
    public decimal TotalCost => Status is SalesInvoiceStatus.Cancelled or SalesInvoiceStatus.FullyReturned
        ? 0m
        : Items.Sum(i => i.LineCostTotal);
    public decimal NetProfit => EffectiveNetSales - TotalCost;

    public string ProfitMarginDisplay
    {
        get
        {
            if (EffectiveNetSales <= 0m) return "0%";
            var pct = Math.Round((NetProfit / EffectiveNetSales) * 100m, 1);
            return $"{pct.ToString("0.#", CultureInfo.InvariantCulture)}%";
        }
    }

    public decimal GrossPaid => CashPaid + VodafoneCashPaid + InstaPayPaid;
    public decimal NetCollected => Math.Max(0m, GrossPaid - RefundedAmount);
    public decimal RemainingAmount => Status is SalesInvoiceStatus.Cancelled or SalesInvoiceStatus.FullyReturned
        ? 0m
        : Math.Max(0m, EffectiveNetSales - NetCollected);
    public decimal ChangeAmount => Status is SalesInvoiceStatus.Cancelled or SalesInvoiceStatus.FullyReturned
        ? 0m
        : Math.Max(0m, NetCollected - EffectiveNetSales);

    public bool HasDiscount => EffectiveDiscount > 0m;
    public bool HasReturnedAmount => ReturnedTotal > 0m;
    public bool HasRefundedAmount => RefundedAmount > 0m;
    public bool HasRemaining => RemainingAmount > 0m;
    public bool HasChange => ChangeAmount > 0m;
    public bool HasCashPaid => CashPaid > 0m;
    public bool HasVodafoneCash => VodafoneCashPaid > 0m || !string.IsNullOrWhiteSpace(VodafoneCashReference);
    public bool HasInstaPay => InstaPayPaid > 0m || !string.IsNullOrWhiteSpace(InstaPayReference);

    public string SubtotalDisplay => PosFormatting.FormatCurrency(Subtotal);
    public string EffectiveDiscountDisplay => PosFormatting.FormatCurrency(EffectiveDiscount);
    public string NetTotalDisplay => PosFormatting.FormatCurrency(NetTotal);
    public string ReturnedTotalDisplay => PosFormatting.FormatCurrency(ReturnedTotal);
    public string EffectiveNetSalesDisplay => PosFormatting.FormatCurrency(EffectiveNetSales);
    public string TotalCostDisplay => PosFormatting.FormatCurrency(TotalCost);
    public string NetProfitDisplay => PosFormatting.FormatCurrency(NetProfit);
    public string CashPaidDisplay => PosFormatting.FormatCurrency(CashPaid);
    public string VodafoneCashPaidDisplay => PosFormatting.FormatCurrency(VodafoneCashPaid);
    public string InstaPayPaidDisplay => PosFormatting.FormatCurrency(InstaPayPaid);
    public string GrossPaidDisplay => PosFormatting.FormatCurrency(GrossPaid);
    public string RefundedAmountDisplay => PosFormatting.FormatCurrency(RefundedAmount);
    public string NetCollectedDisplay => PosFormatting.FormatCurrency(NetCollected);
    public string RemainingAmountDisplay => PosFormatting.FormatCurrency(RemainingAmount);
    public string ChangeAmountDisplay => PosFormatting.FormatCurrency(ChangeAmount);

    public string StatusTitle => Status switch
    {
        SalesInvoiceStatus.PartialCredit => "آجل / متبقي",
        SalesInvoiceStatus.PartiallyReturned => "مرتجع جزئي",
        SalesInvoiceStatus.FullyReturned => "مرتجع كلي",
        SalesInvoiceStatus.Cancelled => "ملغاة",
        SalesInvoiceStatus.Exchanged => "مستبدلة / معدلة",
        _ => "مكتملة"
    };

    public bool IsCompleted => Status == SalesInvoiceStatus.Completed;
    public bool IsPartialCredit => Status == SalesInvoiceStatus.PartialCredit;
    public bool IsPartiallyReturned => Status == SalesInvoiceStatus.PartiallyReturned;
    public bool IsFullyReturned => Status == SalesInvoiceStatus.FullyReturned;
    public bool IsCancelled => Status == SalesInvoiceStatus.Cancelled;
    public bool IsExchanged => Status == SalesInvoiceStatus.Exchanged;

    public bool CanReturn => Status is SalesInvoiceStatus.Completed
        or SalesInvoiceStatus.PartialCredit
        or SalesInvoiceStatus.PartiallyReturned
        or SalesInvoiceStatus.Exchanged
        && Items.Any(i => i.RemainingQuantity > 0);
    public bool CanCollectRemaining => Status is SalesInvoiceStatus.PartialCredit
        or SalesInvoiceStatus.PartiallyReturned
        or SalesInvoiceStatus.Exchanged
        && RemainingAmount > 0m;
    public bool CanCancel => Status is SalesInvoiceStatus.Completed or SalesInvoiceStatus.PartialCredit;

    public string PaymentMethodSummary
    {
        get
        {
            if (IsCancelled) return "ملغاة";
            if (IsFullyReturned) return "مرتجع بالكامل";

            var channels = new List<string>(3);
            if (CashPaid > 0m) channels.Add("كاش");
            if (VodafoneCashPaid > 0m) channels.Add("فودافون كاش");
            if (InstaPayPaid > 0m) channels.Add("إنستاباي");

            if (channels.Count == 0)
                return HasRemaining ? "آجل بالكامل" : "غير محدد";

            if (channels.Count == 1)
                return HasRemaining ? $"{channels[0]} + متبقي" : channels[0];

            return HasRemaining ? $"مقسم ({string.Join(" + ", channels)}) + متبقي" : $"مقسم ({string.Join(" + ", channels)})";
        }
    }

    public string PaymentFilterCategory
    {
        get
        {
            if (HasRemaining) return "آجل / متبقي";
            var count = (CashPaid > 0m ? 1 : 0) + (VodafoneCashPaid > 0m ? 1 : 0) + (InstaPayPaid > 0m ? 1 : 0);
            if (count >= 2) return "دفع مقسم";
            if (CashPaid > 0m) return "نقدي";
            if (VodafoneCashPaid > 0m) return "فودافون كاش";
            if (InstaPayPaid > 0m) return "إنستاباي";
            return "نقدي";
        }
    }

    public void NotifyFinancialsChanged()
    {
        Changed(nameof(TotalUnitsCount));
        Changed(nameof(ReturnedUnitsCount));
        Changed(nameof(ItemsAndUnitsSummary));
        Changed(nameof(Subtotal));
        Changed(nameof(EffectiveDiscount));
        Changed(nameof(NetTotal));
        Changed(nameof(ReturnedTotal));
        Changed(nameof(EffectiveNetSales));
        Changed(nameof(TotalCost));
        Changed(nameof(NetProfit));
        Changed(nameof(ProfitMarginDisplay));
        Changed(nameof(GrossPaid));
        Changed(nameof(NetCollected));
        Changed(nameof(RemainingAmount));
        Changed(nameof(ChangeAmount));
        Changed(nameof(HasDiscount));
        Changed(nameof(HasReturnedAmount));
        Changed(nameof(HasRefundedAmount));
        Changed(nameof(HasRemaining));
        Changed(nameof(HasChange));
        Changed(nameof(HasCashPaid));
        Changed(nameof(HasVodafoneCash));
        Changed(nameof(HasInstaPay));
        Changed(nameof(SubtotalDisplay));
        Changed(nameof(EffectiveDiscountDisplay));
        Changed(nameof(NetTotalDisplay));
        Changed(nameof(ReturnedTotalDisplay));
        Changed(nameof(EffectiveNetSalesDisplay));
        Changed(nameof(TotalCostDisplay));
        Changed(nameof(NetProfitDisplay));
        Changed(nameof(CashPaidDisplay));
        Changed(nameof(VodafoneCashPaidDisplay));
        Changed(nameof(InstaPayPaidDisplay));
        Changed(nameof(GrossPaidDisplay));
        Changed(nameof(RefundedAmountDisplay));
        Changed(nameof(NetCollectedDisplay));
        Changed(nameof(RemainingAmountDisplay));
        Changed(nameof(ChangeAmountDisplay));
        Changed(nameof(PaymentMethodSummary));
        Changed(nameof(PaymentFilterCategory));
        Changed(nameof(CanReturn));
        Changed(nameof(CanCollectRemaining));
        Changed(nameof(CanCancel));
    }
}

public static class InvoiceDesignSeedData
{
    public static ObservableCollection<SalesInvoiceDesignItem> CreateSharedSampleInvoices(
        IReadOnlyList<PosProductDesignItem> catalog,
        IReadOnlyList<InventoryUnitRegistrationItem> registrations)
    {
        var list = new ObservableCollection<SalesInvoiceDesignItem>();
        if (catalog.Count < 10) return list;

        var soldUnitsByProduct = registrations
            .Where(r => r.Status == InventoryUnitStatus.Sold)
            .GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<int> ClaimSoldUnitIds(int productId, string invoiceNumber, int count)
        {
            if (!soldUnitsByProduct.TryGetValue(productId, out var units))
                return [];

            var claimed = new List<int>();
            foreach (var u in units)
            {
                if (!string.IsNullOrEmpty(u.SaleInvoiceNumber)) continue;
                u.SaleInvoiceNumber = invoiceNumber;
                claimed.Add(u.Id);
                if (claimed.Count >= count) break;
            }
            return claimed;
        }

        List<string> GetSerials(IEnumerable<int> ids) => registrations
            .Where(r => ids.Contains(r.Id) && !string.IsNullOrWhiteSpace(r.SerialNumber))
            .Select(r => r.SerialNumber)
            .ToList();

        var today = DateTime.Today;

        // 1) Today - Completed Split Payment Invoice (Cash + Vodafone + InstaPay + Discount + Mixed Price Tiers)
        var inv6No = "INV-2026-0006";
        var inv6Unit1 = ClaimSoldUnitIds(catalog[0].Id, inv6No, 2);
        var inv6Unit2 = ClaimSoldUnitIds(catalog[2].Id, inv6No, 1);
        var inv6 = new SalesInvoiceDesignItem
        {
            Id = 6,
            InvoiceNumber = inv6No,
            IssuedAt = today.AddHours(14).AddMinutes(35),
            CashierName = "المشرف",
            CustomerId = 1,
            CustomerName = "أحمد محمود علي",
            CustomerPhone = "01012345678",
            CustomerAddress = "كفر الشيخ - شارع الخليفة المأمون",
            IsWalkInCustomer = false,
            DefaultPriceTier = PriceTier.Retail,
            Status = SalesInvoiceStatus.Completed,
            DiscountAmount = 45m,
            CashPaid = 620m,
            VodafoneCashPaid = 700m,
            VodafoneCashReference = "VF-884210",
            InstaPayPaid = 550m,
            InstaPayReference = "IP-309412",
            Notes = "تسليم فوري بالمحل — العميل اختبر السماعة والشواحن."
        };
        inv6.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 1,
            ProductId = catalog[0].Id,
            Barcode = catalog[0].Barcode,
            ProductName = catalog[0].Name,
            Category = catalog[0].Category,
            Unit = catalog[0].UnitDisplay,
            UnitRegistrationIds = inv6Unit1,
            PriceTier = PriceTier.Retail,
            Quantity = 2,
            UnitPrice = catalog[0].RetailPrice,
            UnitCost = 300m
        });
        inv6.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 2,
            ProductId = catalog[2].Id,
            Barcode = catalog[2].Barcode,
            ProductName = catalog[2].Name,
            Category = catalog[2].Category,
            Unit = catalog[2].UnitDisplay,
            UnitRegistrationIds = inv6Unit2,
            PriceTier = PriceTier.Special,
            Quantity = 1,
            UnitPrice = catalog[2].SpecialPrice,
            UnitCost = catalog[2].CostPrice
        });
        inv6.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 3,
            ProductId = catalog[3].Id,
            Barcode = catalog[3].Barcode,
            ProductName = catalog[3].Name,
            Category = catalog[3].Category,
            Unit = catalog[3].UnitDisplay,
            PriceTier = PriceTier.Retail,
            Quantity = 2,
            UnitPrice = catalog[3].RetailPrice,
            UnitCost = catalog[3].CostPrice
        });
        inv6.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv6.IssuedAt,
            ActionTitle = "إصدار فاتورة بيع",
            Details = "سداد مقسم (نقدي + فودافون كاش + إنستاباي) مع خصم 45 ج.م.",
            ActorName = inv6.CashierName,
            Amount = inv6.NetTotal
        });

        // 2) Today - Partial Credit Invoice (Remaining balance to collect)
        var inv5No = "INV-2026-0005";
        var inv5Unit1 = ClaimSoldUnitIds(catalog[5].Id, inv5No, 1);
        var inv5 = new SalesInvoiceDesignItem
        {
            Id = 5,
            InvoiceNumber = inv5No,
            IssuedAt = today.AddHours(11).AddMinutes(20),
            CashierName = "كريم السيد",
            CustomerId = 2,
            CustomerName = "محمد سامي إبراهيم",
            CustomerPhone = "01122334455",
            CustomerAddress = "كفر الشيخ - تقسيم 2",
            IsWalkInCustomer = false,
            DefaultPriceTier = PriceTier.Special,
            Status = SalesInvoiceStatus.PartialCredit,
            DiscountAmount = 0m,
            CashPaid = 700m,
            Notes = "اتفق العميل على سداد المتبقي (475 ج.م) خلال 48 ساعة."
        };
        inv5.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 1,
            ProductId = catalog[5].Id,
            Barcode = catalog[5].Barcode,
            ProductName = catalog[5].Name,
            Category = catalog[5].Category,
            Unit = catalog[5].UnitDisplay,
            UnitRegistrationIds = inv5Unit1,
            PriceTier = PriceTier.Special,
            Quantity = 1,
            UnitPrice = catalog[5].SpecialPrice,
            UnitCost = catalog[5].CostPrice
        });
        inv5.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 2,
            ProductId = catalog[8].Id,
            Barcode = catalog[8].Barcode,
            ProductName = catalog[8].Name,
            Category = catalog[8].Category,
            Unit = catalog[8].UnitDisplay,
            PriceTier = PriceTier.Special,
            Quantity = 1,
            UnitPrice = catalog[8].SpecialPrice,
            UnitCost = catalog[8].CostPrice
        });
        inv5.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv5.IssuedAt,
            ActionTitle = "إصدار فاتورة آجل جزئي",
            Details = "تحصيل 700 ج.م كاش وتبقّى 475 ج.م على حساب العميل.",
            ActorName = inv5.CashierName,
            Amount = 700m
        });

        // 3) Yesterday - Tracked Serial / IMEI Phone Sale via InstaPay
        var inv4No = "INV-2026-0004";
        var inv4Unit1 = ClaimSoldUnitIds(catalog[6].Id, inv4No, 1);
        var inv4Serials = GetSerials(inv4Unit1);
        var inv4 = new SalesInvoiceDesignItem
        {
            Id = 4,
            InvoiceNumber = inv4No,
            IssuedAt = today.AddDays(-1).AddHours(17).AddMinutes(10),
            CashierName = "المشرف",
            CustomerId = 4,
            CustomerName = "كريم حسن عبد الله",
            CustomerPhone = "01555443322",
            CustomerAddress = "دسوق - شارع الشركات",
            IsWalkInCustomer = false,
            DefaultPriceTier = PriceTier.Special,
            Status = SalesInvoiceStatus.Completed,
            DiscountAmount = 100m,
            InstaPayPaid = 17400m,
            InstaPayReference = "IP-220194",
            Notes = "هاتف سامسونج A55 بضمان محلي سنة — تم تفعيل الضمان بالسيريال."
        };
        inv4.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 1,
            ProductId = catalog[6].Id,
            Barcode = catalog[6].Barcode,
            ProductName = catalog[6].Name,
            Category = catalog[6].Category,
            Unit = catalog[6].UnitDisplay,
            TrackSerialNumber = true,
            UnitRegistrationIds = inv4Unit1,
            SerialNumbers = inv4Serials.Count > 0 ? inv4Serials : ["35890710550012"],
            PriceTier = PriceTier.Special,
            Quantity = 1,
            UnitPrice = catalog[6].SpecialPrice,
            UnitCost = catalog[6].CostPrice
        });
        inv4.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv4.IssuedAt,
            ActionTitle = "إصدار فاتورة بيع هاتف",
            Details = "سداد كامل عبر إنستاباي (#IP-220194) مع تسجيل سيريال IMEI.",
            ActorName = inv4.CashierName,
            Amount = inv4.NetTotal
        });

        // 4) Two days ago - Wholesale Invoice
        var inv3No = "INV-2026-0003";
        var inv3Unit1 = ClaimSoldUnitIds(catalog[1].Id, inv3No, 1);
        var inv3Unit2 = ClaimSoldUnitIds(catalog[4].Id, inv3No, 1);
        var inv3 = new SalesInvoiceDesignItem
        {
            Id = 3,
            InvoiceNumber = inv3No,
            IssuedAt = today.AddDays(-2).AddHours(13).AddMinutes(45),
            CashierName = "كريم السيد",
            CustomerId = 3,
            CustomerName = "محل النور للإكسسوارات",
            CustomerPhone = "01298765432",
            CustomerAddress = "وسط البلد - برج السلام",
            IsWalkInCustomer = false,
            DefaultPriceTier = PriceTier.Wholesale,
            Status = SalesInvoiceStatus.Completed,
            DiscountAmount = 50m,
            CashPaid = 1200m,
            VodafoneCashPaid = 1120m,
            VodafoneCashReference = "VF-771203",
            Notes = "طلبية جملة أسبوعية لمحل النور."
        };
        inv3.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 1,
            ProductId = catalog[1].Id,
            Barcode = catalog[1].Barcode,
            ProductName = catalog[1].Name,
            Category = catalog[1].Category,
            Unit = catalog[1].UnitDisplay,
            UnitRegistrationIds = inv3Unit1,
            PriceTier = PriceTier.Wholesale,
            Quantity = 10,
            UnitPrice = catalog[1].WholesalePrice,
            UnitCost = catalog[1].CostPrice
        });
        inv3.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 2,
            ProductId = catalog[4].Id,
            Barcode = catalog[4].Barcode,
            ProductName = catalog[4].Name,
            Category = catalog[4].Category,
            Unit = catalog[4].UnitDisplay,
            UnitRegistrationIds = inv3Unit2,
            PriceTier = PriceTier.Wholesale,
            Quantity = 6,
            UnitPrice = catalog[4].WholesalePrice,
            UnitCost = catalog[4].CostPrice
        });
        inv3.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 3,
            ProductId = catalog[3].Id,
            Barcode = catalog[3].Barcode,
            ProductName = catalog[3].Name,
            Category = catalog[3].Category,
            Unit = catalog[3].UnitDisplay,
            PriceTier = PriceTier.Wholesale,
            Quantity = 2,
            UnitPrice = catalog[3].WholesalePrice,
            UnitCost = catalog[3].CostPrice
        });
        inv3.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv3.IssuedAt,
            ActionTitle = "إصدار فاتورة جملة",
            Details = "طلبية جملة (18 قطعة) مدفوعة نقدي + فودافون كاش.",
            ActorName = inv3.CashierName,
            Amount = inv3.NetTotal
        });

        // 5) Three days ago - Partially Returned Invoice
        var inv2No = "INV-2026-0002";
        var inv2Unit1 = ClaimSoldUnitIds(catalog[7].Id, inv2No, 1);
        var inv2Serials = GetSerials(inv2Unit1);
        var inv2 = new SalesInvoiceDesignItem
        {
            Id = 2,
            InvoiceNumber = inv2No,
            IssuedAt = today.AddDays(-3).AddHours(16).AddMinutes(15),
            CashierName = "المشرف",
            CustomerId = 0,
            CustomerName = "عميل نقدي (عام)",
            IsWalkInCustomer = true,
            DefaultPriceTier = PriceTier.Retail,
            Status = SalesInvoiceStatus.PartiallyReturned,
            DiscountAmount = 0m,
            CashPaid = 12690m,
            RefundedAmount = 290m,
            Notes = "أرجع العميل حامل السيارة في اليوم التالي واسترد قيمته نقدًا."
        };
        inv2.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 1,
            ProductId = catalog[7].Id,
            Barcode = catalog[7].Barcode,
            ProductName = catalog[7].Name,
            Category = catalog[7].Category,
            Unit = catalog[7].UnitDisplay,
            TrackSerialNumber = true,
            UnitRegistrationIds = inv2Unit1,
            SerialNumbers = inv2Serials.Count > 0 ? inv2Serials : ["35890810610022"],
            PriceTier = PriceTier.Retail,
            Quantity = 1,
            UnitPrice = catalog[7].RetailPrice,
            UnitCost = catalog[7].CostPrice
        });
        inv2.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 2,
            ProductId = catalog[9].Id,
            Barcode = catalog[9].Barcode,
            ProductName = catalog[9].Name,
            Category = catalog[9].Category,
            Unit = catalog[9].UnitDisplay,
            PriceTier = PriceTier.Retail,
            Quantity = 1,
            ReturnedQuantity = 1,
            UnitPrice = catalog[9].RetailPrice,
            UnitCost = catalog[9].CostPrice
        });
        inv2.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv2.IssuedAt,
            ActionTitle = "إصدار فاتورة بيع",
            Details = "بيع هاتف Redmi Note 13 Pro + حامل سيارة نقدًا.",
            ActorName = inv2.CashierName,
            Amount = 12690m
        });
        inv2.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv2.IssuedAt.AddDays(1).AddHours(2),
            ActionTitle = "مرتجع جزئي",
            Details = "إرجاع 1× حامل موبايل مغناطيسي (عدم ملاءمة لوحة السيارة) — إرجاع للمخزون ورد 290 ج.م نقدًا.",
            ActorName = "المشرف",
            Amount = 290m
        });

        // 6) Five days ago - Cancelled Invoice
        var inv1 = new SalesInvoiceDesignItem
        {
            Id = 1,
            InvoiceNumber = "INV-2026-0001",
            IssuedAt = today.AddDays(-5).AddHours(12).AddMinutes(5),
            CashierName = "كريم السيد",
            CustomerId = 0,
            CustomerName = "عميل نقدي (عام)",
            IsWalkInCustomer = true,
            DefaultPriceTier = PriceTier.Retail,
            Status = SalesInvoiceStatus.Cancelled,
            DiscountAmount = 0m,
            VodafoneCashPaid = 950m,
            VodafoneCashReference = "VF-640119",
            RefundedAmount = 950m,
            Notes = "ألغى العميل الطلب قبل التسليم لعدم توفر اللون الأسود."
        };
        inv1.Items.Add(new SalesInvoiceLineDesignItem
        {
            LineNumber = 1,
            ProductId = catalog[2].Id,
            Barcode = catalog[2].Barcode,
            ProductName = catalog[2].Name,
            Category = catalog[2].Category,
            Unit = catalog[2].UnitDisplay,
            PriceTier = PriceTier.Retail,
            Quantity = 1,
            UnitPrice = catalog[2].RetailPrice,
            UnitCost = catalog[2].CostPrice
        });
        inv1.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv1.IssuedAt,
            ActionTitle = "إصدار فاتورة بيع",
            Details = "سداد مبدئي عبر فودافون كاش (#VF-640119).",
            ActorName = inv1.CashierName,
            Amount = 950m
        });
        inv1.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = inv1.IssuedAt.AddMinutes(15),
            ActionTitle = "إلغاء الفاتورة",
            Details = "تم إلغاء الفاتورة ورد المبلغ بالكامل وإعادة القطع للمخزون.",
            ActorName = "المشرف",
            Amount = 950m
        });

        list.Add(inv6);
        list.Add(inv5);
        list.Add(inv4);
        list.Add(inv3);
        list.Add(inv2);
        list.Add(inv1);
        return list;
    }
}
