using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum InstapayOperationKind
{
    DepositToCustomer,
    WithdrawForCustomer
}

public sealed class InstapayTransferOperation : ObservableObject
{
    private int walletId;
    private string walletAlias = "";
    private string walletNumber = "";
    private InstapayOperationKind kind;
    private string customerNumber = "";
    private decimal amount;
    private decimal commission;
    private decimal cashDelta;
    private decimal walletBalanceAfter;
    private string note = "";
    private bool isReversed;

    public int Id { get; init; }
    public int WalletId { get => walletId; set { walletId = value; Changed(); } }
    public string WalletAlias { get => walletAlias; set { walletAlias = value; Changed(); } }
    public string WalletNumber { get => walletNumber; set { walletNumber = value; Changed(); } }
    public InstapayOperationKind Kind { get => kind; set { kind = value; Changed(); Changed(nameof(IsDeposit)); Changed(nameof(OperationTitle)); Changed(nameof(WalletEffectDisplay)); } }
    public string CustomerNumber { get => customerNumber; set { customerNumber = value; Changed(); } }
    public decimal Amount { get => amount; set { amount = value; Changed(); Changed(nameof(AmountDisplay)); Changed(nameof(WalletEffectDisplay)); } }
    public decimal Commission { get => commission; set { commission = value; Changed(); Changed(nameof(CommissionDisplay)); } }
    public decimal CashDelta { get => cashDelta; set { cashDelta = value; Changed(); Changed(nameof(TotalCashDisplay)); } }
    public decimal WalletBalanceAfter { get => walletBalanceAfter; set { walletBalanceAfter = value; Changed(); Changed(nameof(WalletBalanceAfterDisplay)); } }
    public string Note { get => note; set { note = value; Changed(); } }
    public DateTime Timestamp { get; init; }
    public string ActorName { get; init; } = "";
    public bool IsReversed { get => isReversed; set { isReversed = value; Changed(); Changed(nameof(StatusTitle)); Changed(nameof(CanEditOrDelete)); } }
    public string ReversedBy { get; set; } = "";
    public DateTime? ReversedAt { get; set; }

    public bool IsDeposit => Kind == InstapayOperationKind.DepositToCustomer;
    public bool CanEditOrDelete => !IsReversed;
    public string StatusTitle => IsReversed ? "معكوسة" : "ناجحة";
    public string OperationTitle => IsDeposit ? "إيداع للعميل" : "سحب للعميل";
    public string DateDisplay => Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string TimeDisplay => Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string AmountDisplay => PosFormatting.FormatCurrency(Amount);
    public string CommissionDisplay => PosFormatting.FormatCurrency(Commission);
    public string TotalCashDisplay => $"{(CashDelta >= 0m ? "+" : "−")} {PosFormatting.FormatCurrency(Math.Abs(CashDelta))}";
    public string WalletBalanceAfterDisplay => PosFormatting.FormatCurrency(WalletBalanceAfter);
    public string WalletEffectDisplay => IsDeposit ? $"− {AmountDisplay}" : $"+ {AmountDisplay}";
    public string DetailsDisplay => $"{OperationTitle} · {WalletAlias} · {CustomerNumber} · {AmountDisplay} · عمولة {CommissionDisplay}";
}

public static class InstapayTransferDesignSeedData
{
    public static ObservableCollection<InstapayTransferOperation> CreateOperations() =>
    [
        new() { Id = 1, WalletId = 4, WalletAlias = "إنستاباي — الحساب الرئيسي", WalletNumber = "projectz@instapay", Kind = InstapayOperationKind.DepositToCustomer, CustomerNumber = "01012345678", Amount = 1800m, Commission = 18m, CashDelta = 1818m, WalletBalanceAfter = 40300m, Timestamp = DateTime.Now.AddHours(-1), ActorName = "المشرف" },
        new() { Id = 2, WalletId = 4, WalletAlias = "إنستاباي — الحساب الرئيسي", WalletNumber = "projectz@instapay", Kind = InstapayOperationKind.WithdrawForCustomer, CustomerNumber = "01123456789", Amount = 950m, Commission = 10m, CashDelta = -940m, WalletBalanceAfter = 41250m, Timestamp = DateTime.Now.AddHours(-2), ActorName = "المشرف" },
        new() { Id = 3, WalletId = 5, WalletAlias = "إنستاباي — حساب احتياطي", WalletNumber = "reserve@instapay", Kind = InstapayOperationKind.DepositToCustomer, CustomerNumber = "01234567890", Amount = 2200m, Commission = 22m, CashDelta = 2222m, WalletBalanceAfter = 10300m, Timestamp = DateTime.Now.AddDays(-1), ActorName = "المشرف" },
        new() { Id = 4, WalletId = 5, WalletAlias = "إنستاباي — حساب احتياطي", WalletNumber = "reserve@instapay", Kind = InstapayOperationKind.WithdrawForCustomer, CustomerNumber = "01098765432", Amount = 1100m, Commission = 11m, CashDelta = -1089m, WalletBalanceAfter = 11400m, Timestamp = DateTime.Now.AddDays(-2), ActorName = "المشرف" }
    ];
}
