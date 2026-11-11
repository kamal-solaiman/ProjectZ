using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum FinancialAccountKind
{
    Cash,
    VodafoneCash,
    InstaPay
}

public enum FinancialMovementKind
{
    Deposit,
    Withdrawal,
    PositiveAdjustment,
    NegativeAdjustment
}

public sealed class FinancialAccountDesignItem : ObservableObject
{
    private string alias = "";
    private string identifier = "";
    private decimal balance;
    private decimal dailyDepositLimit;
    private decimal dailyWithdrawalLimit;
    private decimal monthlyDepositLimit;
    private decimal monthlyWithdrawalLimit;
    private decimal depositedToday;
    private decimal withdrawnToday;
    private decimal depositedThisMonth;
    private decimal withdrawnThisMonth;
    private bool isActive = true;
    private bool isDefault;
    private bool isSelected;

    public int Id { get; init; }
    public FinancialAccountKind Kind { get; init; }
    public string Alias { get => alias; set { if (alias == value) return; alias = value; Changed(); Changed(nameof(DisplayName)); } }
    public string Identifier { get => identifier; set { if (identifier == value) return; identifier = value; Changed(); Changed(nameof(DisplayIdentifier)); } }
    public decimal Balance { get => balance; set { if (balance == value) return; balance = value; Changed(); Changed(nameof(BalanceDisplay)); } }
    public decimal DailyDepositLimit { get => dailyDepositLimit; set { if (dailyDepositLimit == value) return; dailyDepositLimit = Math.Max(0m, value); Changed(); NotifyLimitsChanged(); } }
    public decimal DailyWithdrawalLimit { get => dailyWithdrawalLimit; set { if (dailyWithdrawalLimit == value) return; dailyWithdrawalLimit = Math.Max(0m, value); Changed(); NotifyLimitsChanged(); } }
    public decimal MonthlyDepositLimit { get => monthlyDepositLimit; set { if (monthlyDepositLimit == value) return; monthlyDepositLimit = Math.Max(0m, value); Changed(); NotifyLimitsChanged(); } }
    public decimal MonthlyWithdrawalLimit { get => monthlyWithdrawalLimit; set { if (monthlyWithdrawalLimit == value) return; monthlyWithdrawalLimit = Math.Max(0m, value); Changed(); NotifyLimitsChanged(); } }
    public bool IsActive { get => isActive; set { if (isActive == value) return; isActive = value; Changed(); Changed(nameof(StatusTitle)); Changed(nameof(CanTransact)); } }
    public bool IsDefault { get => isDefault; set { if (isDefault == value) return; isDefault = value; Changed(); Changed(nameof(DefaultBadgeVisibility)); } }
    public bool IsSelected { get => isSelected; set { if (isSelected == value) return; isSelected = value; Changed(); } }
    public string Notes { get; set; } = "";

    public bool IsCash => Kind == FinancialAccountKind.Cash;
    public bool CanTransact => IsActive;
    public bool DefaultBadgeVisibility => IsDefault;
    public string DisplayName => IsCash ? "النقدي — الخزنة الرئيسية" : Alias;
    public string DisplayIdentifier => IsCash ? "حساب موحد مرتبط بكل النظام" : Identifier;
    public string ProviderTitle => Kind switch { FinancialAccountKind.Cash => "نقدي", FinancialAccountKind.VodafoneCash => "فودافون كاش", _ => "إنستاباي" };
    public string ProviderShortTitle => Kind == FinancialAccountKind.VodafoneCash ? "VF" : Kind == FinancialAccountKind.InstaPay ? "IP" : "CASH";
    public string StatusTitle => IsActive ? "نشط" : "موقوف";
    public string BalanceDisplay => PosFormatting.FormatCurrency(Balance);

    public decimal DepositedToday { get => depositedToday; private set { depositedToday = value; Changed(); Changed(nameof(DepositedTodayDisplay)); NotifyLimitsChanged(); } }
    public decimal WithdrawnToday { get => withdrawnToday; private set { withdrawnToday = value; Changed(); Changed(nameof(WithdrawnTodayDisplay)); NotifyLimitsChanged(); } }
    public decimal DepositedThisMonth { get => depositedThisMonth; private set { depositedThisMonth = value; Changed(); Changed(nameof(DepositedThisMonthDisplay)); NotifyLimitsChanged(); } }
    public decimal WithdrawnThisMonth { get => withdrawnThisMonth; private set { withdrawnThisMonth = value; Changed(); Changed(nameof(WithdrawnThisMonthDisplay)); NotifyLimitsChanged(); } }
    public string DepositedTodayDisplay => PosFormatting.FormatCurrency(DepositedToday);
    public string WithdrawnTodayDisplay => PosFormatting.FormatCurrency(WithdrawnToday);
    public string DepositedThisMonthDisplay => PosFormatting.FormatCurrency(DepositedThisMonth);
    public string WithdrawnThisMonthDisplay => PosFormatting.FormatCurrency(WithdrawnThisMonth);

    public double DailyDepositUsagePercent => UsagePercent(DepositedToday, DailyDepositLimit);
    public double DailyWithdrawalUsagePercent => UsagePercent(WithdrawnToday, DailyWithdrawalLimit);
    public double MonthlyDepositUsagePercent => UsagePercent(DepositedThisMonth, MonthlyDepositLimit);
    public double MonthlyWithdrawalUsagePercent => UsagePercent(WithdrawnThisMonth, MonthlyWithdrawalLimit);
    public string DailyDepositRemainingDisplay => RemainingDisplay(DepositedToday, DailyDepositLimit);
    public string DailyWithdrawalRemainingDisplay => RemainingDisplay(WithdrawnToday, DailyWithdrawalLimit);
    public string MonthlyDepositRemainingDisplay => RemainingDisplay(DepositedThisMonth, MonthlyDepositLimit);
    public string MonthlyWithdrawalRemainingDisplay => RemainingDisplay(WithdrawnThisMonth, MonthlyWithdrawalLimit);
    public string DailyDepositLimitDisplay => LimitDisplay(DailyDepositLimit);
    public string DailyWithdrawalLimitDisplay => LimitDisplay(DailyWithdrawalLimit);
    public string MonthlyDepositLimitDisplay => LimitDisplay(MonthlyDepositLimit);
    public string MonthlyWithdrawalLimitDisplay => LimitDisplay(MonthlyWithdrawalLimit);
    public bool HasLimitWarning => IsActive && !IsCash && new[]
    {
        DailyDepositUsagePercent, DailyWithdrawalUsagePercent, MonthlyDepositUsagePercent, MonthlyWithdrawalUsagePercent
    }.Any(p => p >= 80.0);
    public string LimitWarningText => HasLimitWarning ? "اقترب من أحد الحدود" : "الحدود ضمن النطاق";

    public void RecalculateUsage(IEnumerable<FinancialTransactionDesignItem> transactions)
    {
        var relevant = transactions.Where(t => t.AccountId == Id && t.CountsTowardLimits).ToList();
        var today = DateTime.Today;
        DepositedToday = relevant.Where(t => t.IsIncoming && t.Timestamp.Date == today).Sum(t => t.Amount);
        WithdrawnToday = relevant.Where(t => !t.IsIncoming && t.Timestamp.Date == today).Sum(t => t.Amount);
        DepositedThisMonth = relevant.Where(t => t.IsIncoming && t.Timestamp.Year == today.Year && t.Timestamp.Month == today.Month).Sum(t => t.Amount);
        WithdrawnThisMonth = relevant.Where(t => !t.IsIncoming && t.Timestamp.Year == today.Year && t.Timestamp.Month == today.Month).Sum(t => t.Amount);
    }

    private static double UsagePercent(decimal used, decimal limit) => limit <= 0m ? 0.0 : Math.Min(100.0, (double)(used / limit * 100m));
    private static string LimitDisplay(decimal limit) => limit <= 0m ? "غير محدود" : PosFormatting.FormatCurrency(limit);
    private static string RemainingDisplay(decimal used, decimal limit) => limit <= 0m ? "بدون حد" : $"متبقي {PosFormatting.FormatCurrency(Math.Max(0m, limit - used))}";

    private void NotifyLimitsChanged()
    {
        Changed(nameof(DailyDepositUsagePercent)); Changed(nameof(DailyWithdrawalUsagePercent));
        Changed(nameof(MonthlyDepositUsagePercent)); Changed(nameof(MonthlyWithdrawalUsagePercent));
        Changed(nameof(DailyDepositRemainingDisplay)); Changed(nameof(DailyWithdrawalRemainingDisplay));
        Changed(nameof(MonthlyDepositRemainingDisplay)); Changed(nameof(MonthlyWithdrawalRemainingDisplay));
        Changed(nameof(DailyDepositLimitDisplay)); Changed(nameof(DailyWithdrawalLimitDisplay));
        Changed(nameof(MonthlyDepositLimitDisplay)); Changed(nameof(MonthlyWithdrawalLimitDisplay));
        Changed(nameof(HasLimitWarning)); Changed(nameof(LimitWarningText));
    }
}

public sealed class FinancialTransactionDesignItem
{
    public int Id { get; init; }
    public int AccountId { get; init; }
    public DateTime Timestamp { get; init; }
    public bool IsIncoming { get; init; }
    public string Category { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }
    public string Counterparty { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Note { get; init; } = "";
    public string ActorName { get; init; } = "";
    public bool CountsTowardLimits { get; init; }
    public string DateDisplay => Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string TimeDisplay => Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string DirectionTitle => IsIncoming ? "وارد" : "منصرف";
    public string SignedAmountDisplay => $"{(IsIncoming ? "+" : "−")} {PosFormatting.FormatCurrency(Amount)}";
    public string BalanceAfterDisplay => PosFormatting.FormatCurrency(BalanceAfter);
    public string ReferenceDisplay => string.IsNullOrWhiteSpace(Reference) ? "—" : Reference;
    public string DetailsDisplay => string.IsNullOrWhiteSpace(Note) ? Counterparty : $"{Counterparty} · {Note}";
}

public static class FinanceDesignSeedData
{
    public static ObservableCollection<FinancialAccountDesignItem> CreateAccounts() =>
    [
        new() { Id = 1, Kind = FinancialAccountKind.Cash, Alias = "الخزنة الرئيسية", Balance = 68450m, IsActive = true, IsDefault = true },
        new() { Id = 2, Kind = FinancialAccountKind.VodafoneCash, Alias = "فودافون — الحساب الرئيسي", Identifier = "01015501122", Balance = 23750m, DailyDepositLimit = 30000m, DailyWithdrawalLimit = 20000m, MonthlyDepositLimit = 300000m, MonthlyWithdrawalLimit = 250000m, IsDefault = true, Notes = "المحفظة الافتراضية للتحصيل اليومي" },
        new() { Id = 3, Kind = FinancialAccountKind.VodafoneCash, Alias = "فودافون — فرع المنصورة", Identifier = "01122334455", Balance = 8900m, DailyDepositLimit = 15000m, DailyWithdrawalLimit = 10000m, MonthlyDepositLimit = 180000m, MonthlyWithdrawalLimit = 150000m, Notes = "مخصص لعمليات الفرع والموردين" },
        new() { Id = 4, Kind = FinancialAccountKind.InstaPay, Alias = "إنستاباي — الحساب الرئيسي", Identifier = "projectz@instapay", Balance = 42100m, DailyDepositLimit = 50000m, DailyWithdrawalLimit = 40000m, MonthlyDepositLimit = 600000m, MonthlyWithdrawalLimit = 500000m, IsDefault = true },
        new() { Id = 5, Kind = FinancialAccountKind.InstaPay, Alias = "إنستاباي — حساب احتياطي", Identifier = "reserve@instapay", Balance = 12500m, DailyDepositLimit = 25000m, DailyWithdrawalLimit = 15000m, MonthlyDepositLimit = 250000m, MonthlyWithdrawalLimit = 180000m, IsActive = false, Notes = "محفظة احتياطية موقوفة مؤقتًا" }
    ];

    public static ObservableCollection<FinancialTransactionDesignItem> CreateTransactions()
    {
        var now = DateTime.Now;
        return
        [
            new() { Id = 1, AccountId = 1, Timestamp = now.AddHours(-1), IsIncoming = true, Category = "تحصيل مبيعات", Amount = 4250m, BalanceAfter = 68450m, Counterparty = "فاتورة INV-2026-1042", Reference = "POS-1042", ActorName = "المشرف" },
            new() { Id = 2, AccountId = 2, Timestamp = now.AddHours(-2), IsIncoming = true, Category = "تحصيل مبيعات", Amount = 3500m, BalanceAfter = 23750m, Counterparty = "عميل — أحمد سامي", Reference = "VF-991204", ActorName = "المشرف" },
            new() { Id = 3, AccountId = 2, Timestamp = now.AddHours(-3), IsIncoming = false, Category = "سحب", Amount = 8000m, BalanceAfter = 20250m, Counterparty = "سحب من المحفظة", Reference = "WD-1008", Note = "سحب نقدي من وكيل", ActorName = "المشرف", CountsTowardLimits = true },
            new() { Id = 4, AccountId = 2, Timestamp = now.AddHours(-4), IsIncoming = true, Category = "إيداع", Amount = 9000m, BalanceAfter = 28250m, Counterparty = "إيداع في المحفظة", Reference = "DP-1044", ActorName = "المشرف", CountsTowardLimits = true },
            new() { Id = 5, AccountId = 4, Timestamp = now.AddHours(-2.5), IsIncoming = false, Category = "سداد مورد", Amount = 12000m, BalanceAfter = 42100m, Counterparty = "شركة النور للتوزيع", Reference = "IP-771022", ActorName = "المشرف" },
            new() { Id = 6, AccountId = 3, Timestamp = now.AddDays(-1), IsIncoming = true, Category = "إيداع", Amount = 12000m, BalanceAfter = 8900m, Counterparty = "إيداع في المحفظة", Reference = "DP-1031", ActorName = "المشرف", CountsTowardLimits = true },
            new() { Id = 7, AccountId = 1, Timestamp = now.AddDays(-1), IsIncoming = false, Category = "سداد مورد", Amount = 6000m, BalanceAfter = 64200m, Counterparty = "محمود كامل", Reference = "PUR-2026-0002", ActorName = "المشرف" },
            new() { Id = 8, AccountId = 4, Timestamp = now.AddDays(-2), IsIncoming = true, Category = "تحصيل صيانة", Amount = 2750m, BalanceAfter = 54100m, Counterparty = "تذكرة MNT-208", Reference = "IP-770811", ActorName = "المشرف" },
            new() { Id = 9, AccountId = 2, Timestamp = now.AddDays(-3), IsIncoming = false, Category = "سحب", Amount = 15000m, BalanceAfter = 19250m, Counterparty = "سحب من المحفظة", Reference = "WD-0992", ActorName = "المشرف", CountsTowardLimits = true },
            new() { Id = 10, AccountId = 5, Timestamp = now.AddDays(-5), IsIncoming = true, Category = "إيداع", Amount = 12500m, BalanceAfter = 12500m, Counterparty = "رصيد افتتاحي", Reference = "OPEN-005", ActorName = "المشرف", CountsTowardLimits = true }
        ];
    }
}
