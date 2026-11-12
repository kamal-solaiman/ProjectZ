using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed record AccountEditorResult(
    FinancialAccountKind Kind,
    string Alias,
    string Identifier,
    decimal OpeningBalance,
    decimal DailyDepositLimit,
    decimal DailyWithdrawalLimit,
    decimal MonthlyDepositLimit,
    decimal MonthlyWithdrawalLimit,
    string Notes);

public sealed class AccountEditorViewModel : ObservableObject
{
    private readonly FinancialAccountDesignItem? existing;
    private readonly Func<FinancialAccountKind, string, int?, string?> identifierValidator;
    private string selectedProvider;
    private string alias = "";
    private string identifier = "";
    private string openingBalanceText = "0";
    private string dailyDepositLimitText = "30000";
    private string dailyWithdrawalLimitText = "20000";
    private string monthlyDepositLimitText = "300000";
    private string monthlyWithdrawalLimitText = "250000";
    private string notes = "";
    private string error = "";

    public bool IsEditMode => existing is not null;
    public bool IsProviderSelectionVisible => !IsEditMode;
    public string WindowTitle => IsEditMode ? "ProjectZ | تعديل المحفظة وحدودها" : "ProjectZ | إضافة محفظة جديدة";
    public string HeaderTitle => IsEditMode ? "تعديل المحفظة وحدود التشغيل" : "إضافة محفظة إلكترونية";
    public string HeaderSubtitle => IsEditMode ? "تعديل الاسم وبيانات الحساب وحدود الإيداع والسحب دون تغيير الرصيد الحالي." : "أضف رقم فودافون كاش أو عنوان إنستاباي مع رصيد افتتاحي وحدود مستقلة.";
    public string SaveButtonText => IsEditMode ? "حفظ التعديلات" : "إضافة المحفظة";
    public IReadOnlyList<string> ProviderOptions { get; } = ["فودافون كاش", "إنستاباي"];
    public string SelectedProvider { get => selectedProvider; set { if (selectedProvider == value) return; selectedProvider = value; Changed(); ClearError(); } }
    public string Alias { get => alias; set { if (alias == value) return; alias = value; Changed(); ClearError(); } }
    public string Identifier { get => identifier; set { if (identifier == value) return; identifier = value; Changed(); ClearError(); } }
    public string OpeningBalanceText { get => openingBalanceText; set { if (openingBalanceText == value) return; openingBalanceText = value; Changed(); ClearError(); } }
    public string DailyDepositLimitText { get => dailyDepositLimitText; set { if (dailyDepositLimitText == value) return; dailyDepositLimitText = value; Changed(); ClearError(); } }
    public string DailyWithdrawalLimitText { get => dailyWithdrawalLimitText; set { if (dailyWithdrawalLimitText == value) return; dailyWithdrawalLimitText = value; Changed(); ClearError(); } }
    public string MonthlyDepositLimitText { get => monthlyDepositLimitText; set { if (monthlyDepositLimitText == value) return; monthlyDepositLimitText = value; Changed(); ClearError(); } }
    public string MonthlyWithdrawalLimitText { get => monthlyWithdrawalLimitText; set { if (monthlyWithdrawalLimitText == value) return; monthlyWithdrawalLimitText = value; Changed(); ClearError(); } }
    public string Notes { get => notes; set { if (notes == value) return; notes = value; Changed(); } }
    public string Error { get => error; private set { if (error == value) return; error = value; Changed(); } }
    public AccountEditorResult? Result { get; private set; }
    public Action? RequestClose { get; set; }
    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    public AccountEditorViewModel(FinancialAccountKind initialKind, FinancialAccountDesignItem? existing, Func<FinancialAccountKind, string, int?, string?> identifierValidator)
    {
        this.existing = existing;
        this.identifierValidator = identifierValidator;
        selectedProvider = (existing?.Kind ?? initialKind) == FinancialAccountKind.InstaPay ? "إنستاباي" : "فودافون كاش";
        if (existing is not null)
        {
            alias = existing.Alias; identifier = existing.Identifier; openingBalanceText = existing.Balance.ToString(CultureInfo.InvariantCulture);
            dailyDepositLimitText = Text(existing.DailyDepositLimit); dailyWithdrawalLimitText = Text(existing.DailyWithdrawalLimit);
            monthlyDepositLimitText = Text(existing.MonthlyDepositLimit); monthlyWithdrawalLimitText = Text(existing.MonthlyWithdrawalLimit); notes = existing.Notes;
        }
        SaveCommand = new ActionCommand(Save);
        CancelCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Alias)) { Error = "أدخل اسمًا واضحًا للمحفظة."; return; }
        if (string.IsNullOrWhiteSpace(Identifier)) { Error = "أدخل رقم المحفظة أو عنوان إنستاباي."; return; }
        var kind = SelectedProvider == "إنستاباي" ? FinancialAccountKind.InstaPay : FinancialAccountKind.VodafoneCash;
        var duplicate = identifierValidator(kind, Identifier.Trim(), existing?.Id);
        if (!string.IsNullOrWhiteSpace(duplicate)) { Error = duplicate; return; }
        if (!TryMoney(OpeningBalanceText, out var opening) || (!IsEditMode && opening < 0m)) { Error = "أدخل رصيدًا افتتاحيًا صحيحًا."; return; }
        if (!TryMoney(DailyDepositLimitText, out var dd) || !TryMoney(DailyWithdrawalLimitText, out var dw) ||
            !TryMoney(MonthlyDepositLimitText, out var md) || !TryMoney(MonthlyWithdrawalLimitText, out var mw) ||
            dd < 0m || dw < 0m || md < 0m || mw < 0m) { Error = "حدود التشغيل يجب أن تكون أرقامًا صحيحة؛ اكتب صفرًا لعدم وجود حد."; return; }
        if ((dd > 0m && md > 0m && dd > md) || (dw > 0m && mw > 0m && dw > mw)) { Error = "الحد اليومي لا يمكن أن يتجاوز الحد الشهري."; return; }
        Result = new AccountEditorResult(kind, Alias.Trim(), Identifier.Trim(), opening, dd, dw, md, mw, Notes.Trim());
        RequestClose?.Invoke();
    }

    private static string Text(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    internal static bool TryMoney(string text, out decimal value) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    private void ClearError() { if (Error.Length > 0) Error = ""; }
}

public sealed record AccountTransactionResult(FinancialMovementKind Kind, decimal Amount, string Counterparty, string Note);

public sealed class AccountTransactionViewModel : ObservableObject
{
    private readonly FinancialAccountDesignItem account;
    private string selectedMovement = "إيداع رصيد";
    private string amountText = "";
    private string counterparty = "";
    private string note = "";
    private string error = "";
    public string WindowTitle => $"ProjectZ | حركة على {account.DisplayName}";
    public string HeaderTitle => "تسجيل حركة مالية";
    public string AccountSummary => $"{account.ProviderTitle} · {account.DisplayName} · الرصيد {account.BalanceDisplay}";
    public IReadOnlyList<string> MovementOptions { get; } = ["إيداع رصيد", "سحب رصيد", "تسوية بالإضافة", "تسوية بالخصم"];
    public string SelectedMovement { get => selectedMovement; set { if (selectedMovement == value) return; selectedMovement = value; Changed(); ClearError(); } }
    public string AmountText { get => amountText; set { if (amountText == value) return; amountText = value; Changed(); ClearError(); } }
    public string Counterparty { get => counterparty; set { if (counterparty == value) return; counterparty = value; Changed(); } }
    public string Note { get => note; set { if (note == value) return; note = value; Changed(); } }
    public string Error { get => error; private set { if (error == value) return; error = value; Changed(); } }
    public AccountTransactionResult? Result { get; private set; }
    public Action? RequestClose { get; set; }
    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    public AccountTransactionViewModel(FinancialAccountDesignItem account)
    {
        this.account = account;
        SaveCommand = new ActionCommand(Save);
        CancelCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void Save()
    {
        if (!account.IsActive) { Error = "الحساب موقوف ولا يقبل حركات جديدة."; return; }
        if (!AccountEditorViewModel.TryMoney(AmountText, out var amount) || amount <= 0m) { Error = "أدخل مبلغًا أكبر من صفر."; return; }
        var kind = SelectedMovement switch { "سحب رصيد" => FinancialMovementKind.Withdrawal, "تسوية بالإضافة" => FinancialMovementKind.PositiveAdjustment, "تسوية بالخصم" => FinancialMovementKind.NegativeAdjustment, _ => FinancialMovementKind.Deposit };
        var incoming = kind is FinancialMovementKind.Deposit or FinancialMovementKind.PositiveAdjustment;
        if (!incoming && amount > account.Balance) { Error = $"المبلغ يتجاوز الرصيد المتاح ({account.BalanceDisplay})."; return; }
        if (kind is FinancialMovementKind.Deposit or FinancialMovementKind.Withdrawal)
        {
            var limitError = ValidateLimits(account, incoming, amount);
            if (limitError is not null) { Error = limitError; return; }
        }
        Result = new AccountTransactionResult(kind, amount, Counterparty.Trim(), Note.Trim());
        RequestClose?.Invoke();
    }

    internal static string? ValidateLimits(FinancialAccountDesignItem account, bool incoming, decimal amount)
    {
        if (account.IsCash) return null;
        if (incoming)
        {
            if (account.DailyDepositLimit > 0m && account.DepositedToday + amount > account.DailyDepositLimit) return $"تتجاوز حد الإيداع اليومي. المتبقي: {PosFormatting.FormatCurrency(Math.Max(0m, account.DailyDepositLimit - account.DepositedToday))}.";
            if (account.MonthlyDepositLimit > 0m && account.DepositedThisMonth + amount > account.MonthlyDepositLimit) return "تتجاوز حد الإيداع الشهري المحدد للمحفظة.";
        }
        else
        {
            if (account.DailyWithdrawalLimit > 0m && account.WithdrawnToday + amount > account.DailyWithdrawalLimit) return $"تتجاوز حد السحب اليومي. المتبقي: {PosFormatting.FormatCurrency(Math.Max(0m, account.DailyWithdrawalLimit - account.WithdrawnToday))}.";
            if (account.MonthlyWithdrawalLimit > 0m && account.WithdrawnThisMonth + amount > account.MonthlyWithdrawalLimit) return "تتجاوز حد السحب الشهري المحدد للمحفظة.";
        }
        return null;
    }

    private void ClearError() { if (Error.Length > 0) Error = ""; }
}

public sealed record AccountTransferResult(FinancialAccountDesignItem Target, decimal Amount, string Note);

public sealed class AccountTransferViewModel : ObservableObject
{
    private readonly FinancialAccountDesignItem source;
    private FinancialAccountDesignItem? selectedTarget;
    private string amountText = "";
    private string note = "";
    private string error = "";
    public string WindowTitle => "ProjectZ | تحويل بين الحسابات";
    public string SourceSummary => $"من: {source.DisplayName} · المتاح {source.BalanceDisplay}";
    public IReadOnlyList<FinancialAccountDesignItem> Targets { get; }
    public FinancialAccountDesignItem? SelectedTarget { get => selectedTarget; set { if (ReferenceEquals(selectedTarget, value)) return; selectedTarget = value; Changed(); ClearError(); } }
    public string AmountText { get => amountText; set { if (amountText == value) return; amountText = value; Changed(); ClearError(); } }
    public string Note { get => note; set { if (note == value) return; note = value; Changed(); } }
    public string Error { get => error; private set { if (error == value) return; error = value; Changed(); } }
    public AccountTransferResult? Result { get; private set; }
    public Action? RequestClose { get; set; }
    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    public AccountTransferViewModel(FinancialAccountDesignItem source, IEnumerable<FinancialAccountDesignItem> accounts)
    {
        this.source = source;
        Targets = accounts.Where(a => a.Id != source.Id && a.IsActive).ToList();
        selectedTarget = Targets.FirstOrDefault();
        SaveCommand = new ActionCommand(Save);
        CancelCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void Save()
    {
        if (!source.IsActive) { Error = "الحساب المصدر موقوف."; return; }
        if (SelectedTarget is null) { Error = "اختر الحساب المستلم."; return; }
        if (!AccountEditorViewModel.TryMoney(AmountText, out var amount) || amount <= 0m) { Error = "أدخل مبلغ تحويل أكبر من صفر."; return; }
        if (amount > source.Balance) { Error = $"المبلغ يتجاوز الرصيد المتاح ({source.BalanceDisplay})."; return; }
        var sourceError = AccountTransactionViewModel.ValidateLimits(source, false, amount);
        if (sourceError is not null) { Error = $"المصدر: {sourceError}"; return; }
        var targetError = AccountTransactionViewModel.ValidateLimits(SelectedTarget, true, amount);
        if (targetError is not null) { Error = $"المستلم: {targetError}"; return; }
        Result = new AccountTransferResult(SelectedTarget, amount, Note.Trim());
        RequestClose?.Invoke();
    }

    private void ClearError() { if (Error.Length > 0) Error = ""; }
}
