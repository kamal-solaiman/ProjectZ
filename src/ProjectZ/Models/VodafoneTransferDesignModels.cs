using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Helpers;

namespace ProjectZ.Models;

public enum VodafoneOperationKind
{
    DepositToCustomer,
    WithdrawForCustomer
}

public sealed class VodafoneTransferOperation : ObservableObject
{
    private int walletId;
    private string walletAlias = "";
    private string walletNumber = "";
    private VodafoneOperationKind kind;
    private string customerNumber = "";
    private decimal amount;
    private decimal commission;
    private decimal cashDelta;
    private decimal walletBalanceAfter;
    private string reference = "";
    private string note = "";
    private bool isReversed;
    private string reversalReference = "";

    public int Id { get; init; }
    public int WalletId { get => walletId; set { walletId = value; Changed(); } }
    public string WalletAlias { get => walletAlias; set { walletAlias = value; Changed(); } }
    public string WalletNumber { get => walletNumber; set { walletNumber = value; Changed(); } }
    public VodafoneOperationKind Kind { get => kind; set { kind = value; Changed(); Changed(nameof(IsDeposit)); Changed(nameof(OperationTitle)); Changed(nameof(WalletEffectDisplay)); } }
    public string CustomerNumber { get => customerNumber; set { customerNumber = value; Changed(); } }
    public decimal Amount { get => amount; set { amount = value; Changed(); Changed(nameof(AmountDisplay)); Changed(nameof(WalletEffectDisplay)); } }
    public decimal Commission { get => commission; set { commission = value; Changed(); Changed(nameof(CommissionDisplay)); } }
    public decimal CashDelta { get => cashDelta; set { cashDelta = value; Changed(); Changed(nameof(TotalCashDisplay)); } }
    public decimal WalletBalanceAfter { get => walletBalanceAfter; set { walletBalanceAfter = value; Changed(); Changed(nameof(WalletBalanceAfterDisplay)); } }
    public string Reference { get => reference; set { reference = value; Changed(); } }
    public string Note { get => note; set { note = value; Changed(); } }
    public DateTime Timestamp { get; init; }
    public string ActorName { get; init; } = "";
    public bool IsReversed { get => isReversed; set { isReversed = value; Changed(); Changed(nameof(StatusTitle)); Changed(nameof(CanEditOrDelete)); } }
    public string ReversalReference { get => reversalReference; set { reversalReference = value; Changed(); } }
    public string ReversedBy { get; set; } = "";
    public DateTime? ReversedAt { get; set; }

    public bool IsDeposit => Kind == VodafoneOperationKind.DepositToCustomer;
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

public static class VodafoneTransferDesignSeedData
{
    public static ObservableCollection<VodafoneTransferOperation> CreateOperations() =>
    [
        new() { Id = 1, WalletId = 2, WalletAlias = "فودافون — الحساب الرئيسي", WalletNumber = "01015501122", Kind = VodafoneOperationKind.DepositToCustomer, CustomerNumber = "01012345678", Amount = 1500m, Commission = 15m, CashDelta = 1515m, WalletBalanceAfter = 22250m, Reference = "VF-DP-1001", Timestamp = DateTime.Now.AddHours(-1), ActorName = "المشرف" },
        new() { Id = 2, WalletId = 2, WalletAlias = "فودافون — الحساب الرئيسي", WalletNumber = "01015501122", Kind = VodafoneOperationKind.WithdrawForCustomer, CustomerNumber = "01123456789", Amount = 800m, Commission = 8m, CashDelta = -792m, WalletBalanceAfter = 23050m, Reference = "VF-WD-1002", Timestamp = DateTime.Now.AddHours(-2), ActorName = "المشرف" },
        new() { Id = 3, WalletId = 3, WalletAlias = "فودافون — فرع المنصورة", WalletNumber = "01122334455", Kind = VodafoneOperationKind.DepositToCustomer, CustomerNumber = "01234567890", Amount = 2500m, Commission = 20m, CashDelta = 2520m, WalletBalanceAfter = 6400m, Reference = "VF-DP-1003", Timestamp = DateTime.Now.AddDays(-1), ActorName = "المشرف" },
        new() { Id = 4, WalletId = 3, WalletAlias = "فودافون — فرع المنصورة", WalletNumber = "01122334455", Kind = VodafoneOperationKind.WithdrawForCustomer, CustomerNumber = "01098765432", Amount = 1200m, Commission = 12m, CashDelta = -1188m, WalletBalanceAfter = 7600m, Reference = "VF-WD-1004", Timestamp = DateTime.Now.AddDays(-2), ActorName = "المشرف" }
    ];
}
