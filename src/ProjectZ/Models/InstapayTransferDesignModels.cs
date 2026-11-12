using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum InstapayOperationKind
{
    DepositToCustomer,
    WithdrawForCustomer
}

public sealed class InstapayTransferOperation
{
    public int Id { get; init; }
    public int WalletId { get; init; }
    public string WalletAlias { get; init; } = "";
    public string WalletNumber { get; init; } = "";
    public InstapayOperationKind Kind { get; init; }
    public string CustomerNumber { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal Commission { get; init; }
    public decimal CashDelta { get; init; }
    public decimal WalletBalanceAfter { get; init; }
    public string Reference { get; init; } = "";
    public string Note { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public string ActorName { get; init; } = "";

    public bool IsDeposit => Kind == InstapayOperationKind.DepositToCustomer;
    public string OperationTitle => IsDeposit ? "إيداع للعميل" : "سحب للعميل";
    public string DateDisplay => Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string TimeDisplay => Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string AmountDisplay => PosFormatting.FormatCurrency(Amount);
    public string CommissionDisplay => PosFormatting.FormatCurrency(Commission);
    public string TotalCashDisplay => $"{(CashDelta >= 0m ? "+" : "−")} {PosFormatting.FormatCurrency(Math.Abs(CashDelta))}";
    public string WalletBalanceAfterDisplay => PosFormatting.FormatCurrency(WalletBalanceAfter);
    public string WalletEffectDisplay => IsDeposit ? $"− {AmountDisplay}" : $"+ {AmountDisplay}";
}

public static class InstapayTransferDesignSeedData
{
    public static ObservableCollection<InstapayTransferOperation> CreateOperations() =>
    [
        new() { Id = 1, WalletId = 4, WalletAlias = "إنستاباي — الحساب الرئيسي", WalletNumber = "projectz@instapay", Kind = InstapayOperationKind.DepositToCustomer, CustomerNumber = "01012345678", Amount = 1800m, Commission = 18m, CashDelta = 1818m, WalletBalanceAfter = 40300m, Reference = "IP-DP-2001", Timestamp = DateTime.Now.AddHours(-1), ActorName = "المشرف" },
        new() { Id = 2, WalletId = 4, WalletAlias = "إنستاباي — الحساب الرئيسي", WalletNumber = "projectz@instapay", Kind = InstapayOperationKind.WithdrawForCustomer, CustomerNumber = "01123456789", Amount = 950m, Commission = 10m, CashDelta = -940m, WalletBalanceAfter = 41250m, Reference = "IP-WD-2002", Timestamp = DateTime.Now.AddHours(-2), ActorName = "المشرف" },
        new() { Id = 3, WalletId = 5, WalletAlias = "إنستاباي — حساب احتياطي", WalletNumber = "reserve@instapay", Kind = InstapayOperationKind.DepositToCustomer, CustomerNumber = "01234567890", Amount = 2200m, Commission = 22m, CashDelta = 2222m, WalletBalanceAfter = 10300m, Reference = "IP-DP-2003", Timestamp = DateTime.Now.AddDays(-1), ActorName = "المشرف" },
        new() { Id = 4, WalletId = 5, WalletAlias = "إنستاباي — حساب احتياطي", WalletNumber = "reserve@instapay", Kind = InstapayOperationKind.WithdrawForCustomer, CustomerNumber = "01098765432", Amount = 1100m, Commission = 11m, CashDelta = -1089m, WalletBalanceAfter = 11400m, Reference = "IP-WD-2004", Timestamp = DateTime.Now.AddDays(-2), ActorName = "المشرف" }
    ];
}
