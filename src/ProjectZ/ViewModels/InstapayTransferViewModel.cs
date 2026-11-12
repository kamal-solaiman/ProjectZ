using System.Collections.ObjectModel;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class InstapayTransferViewModel : ObservableObject
{
    private readonly MyAccountsViewModel accounts;
    private readonly string currentUserName;
    private FinancialAccountDesignItem? selectedWallet;
    private string selectedOperationType = "إيداع للعميل";
    private string customerNumber = "";
    private string amountText = "";
    private string commissionText = "0";
    private string note = "";
    private string walletSearchText = "";
    private bool showSelectedWalletOperationsOnly;
    private string statusMessage = "تصميم تفاعلي بدون Backend: العمليات تؤثر على أرصدة الجلسة الحالية وتظهر أيضًا في شاشة حساباتي.";
    private bool isErrorStatus;
    private int nextOperationId;
    private int nextFinancialTransactionId;

    public ObservableCollection<FinancialAccountDesignItem> FilteredWallets { get; } = [];
    public ObservableCollection<InstapayTransferOperation> Operations { get; }
    public ObservableCollection<InstapayTransferOperation> FilteredOperations { get; } = [];
    public IReadOnlyList<string> OperationTypeOptions { get; } = ["إيداع للعميل", "سحب للعميل"];

    public FinancialAccountDesignItem? SelectedWallet
    {
        get => selectedWallet;
        set
        {
            if (ReferenceEquals(selectedWallet, value)) return;
            if (selectedWallet is not null) selectedWallet.IsSelected = false;
            selectedWallet = value;
            if (selectedWallet is not null) selectedWallet.IsSelected = true;
            Changed(); Changed(nameof(HasSelectedWallet)); NotifyPreviewChanged(); RefreshFilteredOperations();
        }
    }
    public bool HasSelectedWallet => SelectedWallet is not null;
    public string SelectedOperationType
    {
        get => selectedOperationType;
        set { if (selectedOperationType == value) return; selectedOperationType = value; Changed(); Changed(nameof(IsDepositSelected)); Changed(nameof(OperationButtonText)); Changed(nameof(OperationExplanation)); NotifyPreviewChanged(); }
    }
    public bool IsDepositSelected => SelectedOperationType == "إيداع للعميل";
    public string CustomerNumber { get => customerNumber; set { if (customerNumber == value) return; customerNumber = value; Changed(); } }
    public string AmountText { get => amountText; set { if (amountText == value) return; amountText = value; Changed(); NotifyPreviewChanged(); } }
    public string CommissionText { get => commissionText; set { if (commissionText == value) return; commissionText = value; Changed(); NotifyPreviewChanged(); } }
    public string Note { get => note; set { if (note == value) return; note = value; Changed(); } }
    public string WalletSearchText { get => walletSearchText; set { if (walletSearchText == value) return; walletSearchText = value; Changed(); RefreshWallets(); } }
    public bool ShowSelectedWalletOperationsOnly { get => showSelectedWalletOperationsOnly; set { if (showSelectedWalletOperationsOnly == value) return; showSelectedWalletOperationsOnly = value; Changed(); RefreshFilteredOperations(); } }
    public bool HasNoFilteredWallets => FilteredWallets.Count == 0;
    public bool HasNoFilteredOperations => FilteredOperations.Count == 0;

    public string OperationButtonText => IsDepositSelected ? "تنفيذ الإيداع للعميل" : "تنفيذ السحب للعميل";
    public string OperationExplanation => IsDepositSelected
        ? "يقل رصيد الخط بالمبلغ، ويزيد النقدي بالمبلغ + العمولة. يُراجع حد السحب اليومي والشهري."
        : "يزيد رصيد الخط بالمبلغ، ويقل النقدي بالمبلغ مع إضافة العمولة. يُراجع حد الإيداع اليومي والشهري.";
    public string WalletEffectPreview => Preview(out var amount, out _) && SelectedWallet is not null
        ? $"{(IsDepositSelected ? "−" : "+")} {PosFormatting.FormatCurrency(amount)}" : "—";
    public string CashEffectPreview => Preview(out var amount, out var commission) && SelectedWallet is not null
        ? $"{(CashDelta(amount, commission) >= 0m ? "+" : "−")} {PosFormatting.FormatCurrency(Math.Abs(CashDelta(amount, commission)))}" : "—";
    public string WalletBalanceAfterPreview => SelectedWallet is null || !Preview(out var amount, out _) ? "—" : PosFormatting.FormatCurrency(SelectedWallet.Balance + (IsDepositSelected ? -amount : amount));
    public string CashBalanceAfterPreview => SelectedWallet is null || !Preview(out var amount, out var commission) ? "—" : PosFormatting.FormatCurrency(accounts.CashAccount.Balance + CashDelta(amount, commission));
    public string SelectedDailyRemainingDisplay => SelectedWallet is null ? "—" : IsDepositSelected ? SelectedWallet.DailyWithdrawalRemainingDisplay : SelectedWallet.DailyDepositRemainingDisplay;
    public string SelectedMonthlyRemainingDisplay => SelectedWallet is null ? "—" : IsDepositSelected ? SelectedWallet.MonthlyWithdrawalRemainingDisplay : SelectedWallet.MonthlyDepositRemainingDisplay;
    public string StatusMessage { get => statusMessage; private set { if (statusMessage == value) return; statusMessage = value; Changed(); Changed(nameof(HasStatusMessage)); } }
    public bool IsErrorStatus { get => isErrorStatus; private set { if (isErrorStatus == value) return; isErrorStatus = value; Changed(); } }
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ActionCommand ExecuteOperationCommand { get; }
    public ActionCommand ClearFormCommand { get; }
    public ActionCommand RefreshCommand { get; }
    public ActionCommand ClearWalletSearchCommand { get; }
    public RelayCommand<FinancialAccountDesignItem> SelectWalletCommand { get; }
    public RelayCommand<FinancialAccountDesignItem> DepositForWalletCommand { get; }
    public RelayCommand<FinancialAccountDesignItem> WithdrawForWalletCommand { get; }
    public RelayCommand<string> SelectOperationTypeCommand { get; }

    public InstapayTransferViewModel() : this(new MyAccountsViewModel(), "المشرف") { }

    public InstapayTransferViewModel(MyAccountsViewModel accounts, string currentUserName)
    {
        this.accounts = accounts;
        this.currentUserName = currentUserName;
        Operations = InstapayTransferDesignSeedData.CreateOperations();
        nextOperationId = Operations.Select(o => o.Id).DefaultIfEmpty(0).Max() + 1;
        nextFinancialTransactionId = accounts.Transactions.Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;

        ExecuteOperationCommand = new ActionCommand(ExecuteOperation);
        ClearFormCommand = new ActionCommand(ClearForm);
        RefreshCommand = new ActionCommand(() => { NotifyExternalDataChanged(); SetStatus("تم تحديث الخطوط والأرصدة واستهلاك الحدود.", false); });
        ClearWalletSearchCommand = new ActionCommand(() => WalletSearchText = "");
        SelectWalletCommand = new RelayCommand<FinancialAccountDesignItem>(wallet => { if (wallet is not null) SelectedWallet = wallet; });
        DepositForWalletCommand = new RelayCommand<FinancialAccountDesignItem>(wallet => StartOperation(wallet, true));
        WithdrawForWalletCommand = new RelayCommand<FinancialAccountDesignItem>(wallet => StartOperation(wallet, false));
        SelectOperationTypeCommand = new RelayCommand<string>(type => { if (!string.IsNullOrWhiteSpace(type)) SelectedOperationType = type; });
        NotifyExternalDataChanged();
    }

    public void NotifyExternalDataChanged()
    {
        accounts.NotifyExternalDataChanged();
        RefreshWallets(); RefreshFilteredOperations(); NotifyPreviewChanged();
    }

    private IEnumerable<FinancialAccountDesignItem> Wallets() => accounts.Accounts.Where(a => a.Kind == FinancialAccountKind.InstaPay);

    private void RefreshWallets()
    {
        var keep = SelectedWallet;
        var query = WalletSearchText.Trim();
        FilteredWallets.Clear();
        foreach (var wallet in Wallets().Where(w => query.Length == 0 || w.Alias.Contains(query, StringComparison.OrdinalIgnoreCase) || w.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderByDescending(w => w.IsDefault).ThenByDescending(w => w.IsActive).ThenBy(w => w.Id))
            FilteredWallets.Add(wallet);
        SelectedWallet = FilteredWallets.FirstOrDefault(w => ReferenceEquals(w, keep)) ?? FilteredWallets.FirstOrDefault();
        Changed(nameof(HasNoFilteredWallets));
    }

    private void StartOperation(FinancialAccountDesignItem? wallet, bool deposit)
    {
        if (wallet is null) return;
        SelectedWallet = wallet;
        SelectedOperationType = deposit ? "إيداع للعميل" : "سحب للعميل";
        SetStatus(deposit ? "أدخل رقم العميل والمبلغ والعمولة لإتمام الإيداع." : "أدخل رقم العميل والمبلغ والعمولة لإتمام السحب.", false);
    }

    private void ExecuteOperation()
    {
        if (SelectedWallet is null) { SetStatus("اختر خط إنستاباي أولًا.", true); return; }
        if (SelectedWallet.Kind != FinancialAccountKind.InstaPay) { SetStatus("الحساب المحدد ليس محفظة إنستاباي.", true); return; }
        if (!SelectedWallet.IsActive) { SetStatus("هذا الخط موقوف من شاشة حساباتي ولا يقبل عمليات جديدة.", true); return; }
        var normalizedCustomer = new string(CustomerNumber.Where(char.IsDigit).ToArray());
        if (normalizedCustomer.Length != 11 || !normalizedCustomer.StartsWith("01", StringComparison.Ordinal)) { SetStatus("أدخل رقم عميل صحيحًا مكونًا من 11 رقمًا ويبدأ بـ 01.", true); return; }
        if (!AccountEditorViewModel.TryMoney(AmountText, out var amount) || amount <= 0m) { SetStatus("أدخل مبلغًا أكبر من صفر.", true); return; }
        if (!AccountEditorViewModel.TryMoney(CommissionText, out var commission) || commission < 0m) { SetStatus("أدخل عمولة صحيحة؛ اكتب صفرًا إذا لا توجد عمولة.", true); return; }
        if (IsDepositSelected && amount > SelectedWallet.Balance) { SetStatus($"الرصيد المتاح في الخط ({SelectedWallet.BalanceDisplay}) أقل من مبلغ الإيداع.", true); return; }
        var cashDelta = CashDelta(amount, commission);
        if (cashDelta < 0m && Math.Abs(cashDelta) > accounts.CashAccount.Balance) { SetStatus($"الرصيد النقدي ({accounts.CashAccount.BalanceDisplay}) لا يكفي لصرف صافي مبلغ العملية.", true); return; }
        var limitError = AccountTransactionViewModel.ValidateLimits(SelectedWallet, incoming: !IsDepositSelected, amount);
        if (limitError is not null) { SetStatus(limitError, true); return; }

        var wallet = SelectedWallet;
        wallet.Balance += IsDepositSelected ? -amount : amount;
        accounts.CashAccount.Balance += cashDelta;
        var operation = new InstapayTransferOperation
        {
            Id = nextOperationId++, WalletId = wallet.Id, WalletAlias = wallet.Alias, WalletNumber = wallet.Identifier,
            Kind = IsDepositSelected ? InstapayOperationKind.DepositToCustomer : InstapayOperationKind.WithdrawForCustomer,
            CustomerNumber = normalizedCustomer, Amount = amount, Commission = commission, CashDelta = cashDelta,
            WalletBalanceAfter = wallet.Balance, Note = Note.Trim(), Timestamp = DateTime.Now, ActorName = currentUserName
        };
        Operations.Insert(0, operation);
        AddFinancialTransaction(wallet, !IsDepositSelected, IsDepositSelected ? "إنستاباي — إيداع للعميل" : "إنستاباي — سحب للعميل", amount, normalizedCustomer, Note.Trim(), true);
        if (IsDepositSelected)
        {
            AddFinancialTransaction(accounts.CashAccount, true, "نقدية إيداع إنستاباي", amount, normalizedCustomer, Note.Trim(), false);
            if (commission > 0m) AddFinancialTransaction(accounts.CashAccount, true, "عمولة إنستاباي", commission, normalizedCustomer, "عمولة عملية إيداع", false);
        }
        else
        {
            AddFinancialTransaction(accounts.CashAccount, false, "نقدية سحب إنستاباي", amount, normalizedCustomer, Note.Trim(), false);
            if (commission > 0m) AddFinancialTransaction(accounts.CashAccount, true, "عمولة إنستاباي", commission, normalizedCustomer, "عمولة عملية سحب", false);
        }

        accounts.NotifyExternalDataChanged();
        RefreshWallets(); RefreshFilteredOperations(); NotifyPreviewChanged();
        SetStatus($"تم تنفيذ {operation.OperationTitle} بمبلغ {operation.AmountDisplay} وعمولة {operation.CommissionDisplay} بنجاح.", false);
        CustomerNumber = ""; AmountText = ""; CommissionText = "0"; Note = "";
    }

    private void AddFinancialTransaction(FinancialAccountDesignItem account, bool incoming, string category, decimal amount, string counterparty, string note, bool counts)
    {
        accounts.Transactions.Insert(0, new FinancialTransactionDesignItem
        {
            Id = nextFinancialTransactionId++, AccountId = account.Id, Timestamp = DateTime.Now, IsIncoming = incoming,
            Category = category, Amount = amount, BalanceAfter = account.Balance, Counterparty = counterparty,
            Note = note, ActorName = currentUserName, CountsTowardLimits = counts
        });
    }

    private decimal CashDelta(decimal amount, decimal commission) => IsDepositSelected ? amount + commission : commission - amount;
    private bool Preview(out decimal amount, out decimal commission)
    {
        var validAmount = AccountEditorViewModel.TryMoney(AmountText, out amount) && amount >= 0m;
        var validCommission = AccountEditorViewModel.TryMoney(CommissionText, out commission) && commission >= 0m;
        return validAmount && validCommission;
    }

    private void ClearForm()
    {
        CustomerNumber = ""; AmountText = ""; CommissionText = "0"; Note = "";
        SetStatus("تم تفريغ بيانات العملية مع الاحتفاظ بالخط المحدد.", false);
    }

    private void RefreshFilteredOperations()
    {
        FilteredOperations.Clear();
        foreach (var operation in Operations.Where(o => !ShowSelectedWalletOperationsOnly || SelectedWallet is null || o.WalletId == SelectedWallet.Id).OrderByDescending(o => o.Timestamp).ThenByDescending(o => o.Id)) FilteredOperations.Add(operation);
        Changed(nameof(HasNoFilteredOperations));
    }

    private void NotifyPreviewChanged()
    {
        Changed(nameof(WalletEffectPreview)); Changed(nameof(CashEffectPreview));
        Changed(nameof(WalletBalanceAfterPreview)); Changed(nameof(CashBalanceAfterPreview));
        Changed(nameof(SelectedDailyRemainingDisplay)); Changed(nameof(SelectedMonthlyRemainingDisplay));
    }

    private void SetStatus(string message, bool error) { IsErrorStatus = error; StatusMessage = message; }
}
