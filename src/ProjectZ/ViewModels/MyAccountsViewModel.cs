using System.Collections.ObjectModel;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MyAccountsViewModel : ObservableObject
{
    private readonly string currentUserName;
    private readonly FinancialRepository? repository;
    private FinancialAccountDesignItem? selectedAccount;
    private string selectedProviderFilter = "كل المحافظ";
    private string selectedStatusFilter = "كل الحالات";
    private string walletSearchText = "";
    private string transactionSearchText = "";
    private string selectedTransactionType = "كل الحركات";
    private string statusMessage = "تحميل البيانات من قاعدة البيانات.";
    private bool isErrorStatus;
    private bool refreshFromDatabase;
    private int nextAccountId = 100;
    private int nextTransactionId = 1000;

    public ObservableCollection<FinancialAccountDesignItem> Accounts { get; }
    public ObservableCollection<FinancialTransactionDesignItem> Transactions { get; }
    public ObservableCollection<FinancialAccountDesignItem> FilteredWallets { get; } = [];
    public ObservableCollection<FinancialTransactionDesignItem> SelectedAccountTransactions { get; } = [];
    public IReadOnlyList<string> ProviderFilterOptions { get; } = ["كل المحافظ", "فودافون كاش", "إنستاباي"];
    public IReadOnlyList<string> StatusFilterOptions { get; } = ["كل الحالات", "النشطة فقط", "الموقوفة فقط", "تحتاج مراجعة الحدود"];
    public IReadOnlyList<string> TransactionTypeOptions { get; } = ["كل الحركات", "وارد", "منصرف", "إيداع وسحب", "تحصيل وسداد النظام", "تسويات وتحويلات"];

    public FinancialAccountDesignItem CashAccount => Accounts.First(a => a.IsCash);
    public FinancialAccountDesignItem? SelectedAccount
    {
        get => selectedAccount;
        set
        {
            if (ReferenceEquals(selectedAccount, value)) return;
            if (selectedAccount is not null) selectedAccount.IsSelected = false;
            selectedAccount = value;
            if (selectedAccount is not null) selectedAccount.IsSelected = true;
            RefreshSelectedTransactions();
            Changed(); Changed(nameof(HasSelectedAccount)); Changed(nameof(IsCashSelected)); Changed(nameof(SelectedAccountTitle));
        }
    }
    public bool HasSelectedAccount => SelectedAccount is not null;
    public bool IsCashSelected => SelectedAccount?.IsCash == true;
    public string SelectedAccountTitle => SelectedAccount is null ? "سجل الحركات" : $"سجل حركات: {SelectedAccount.DisplayName}";

    public string SelectedProviderFilter { get => selectedProviderFilter; set { if (selectedProviderFilter == value) return; selectedProviderFilter = value; Changed(); RefreshFilteredWallets(); } }
    public string SelectedStatusFilter { get => selectedStatusFilter; set { if (selectedStatusFilter == value) return; selectedStatusFilter = value; Changed(); RefreshFilteredWallets(); } }
    public string WalletSearchText { get => walletSearchText; set { if (walletSearchText == value) return; walletSearchText = value; Changed(); RefreshFilteredWallets(); } }
    public string TransactionSearchText { get => transactionSearchText; set { if (transactionSearchText == value) return; transactionSearchText = value; Changed(); RefreshSelectedTransactions(); } }
    public string SelectedTransactionType { get => selectedTransactionType; set { if (selectedTransactionType == value) return; selectedTransactionType = value; Changed(); RefreshSelectedTransactions(); } }
    public bool HasNoFilteredWallets => FilteredWallets.Count == 0;
    public bool HasNoSelectedTransactions => SelectedAccountTransactions.Count == 0;

    public string TotalLiquidityDisplay => PosFormatting.FormatCurrency(Accounts.Sum(a => a.Balance));
    public string ActiveAccountsSubtext => $"{Accounts.Count(a => a.IsActive)} حسابات نشطة · {Accounts.Count(a => !a.IsCash)} محافظ إلكترونية";
    public string DigitalBalancesDisplay => PosFormatting.FormatCurrency(Accounts.Where(a => !a.IsCash).Sum(a => a.Balance));
    public string DigitalBreakdownSubtext => $"فودافون: {PosFormatting.FormatCurrency(Accounts.Where(a => a.Kind == FinancialAccountKind.VodafoneCash).Sum(a => a.Balance))} · إنستاباي: {PosFormatting.FormatCurrency(Accounts.Where(a => a.Kind == FinancialAccountKind.InstaPay).Sum(a => a.Balance))}";
    public string TodayIncomingDisplay => PosFormatting.FormatCurrency(Transactions.Where(t => t.IsIncoming && t.Timestamp.Date == DateTime.Today).Sum(t => t.Amount));
    public string TodayOutgoingSubtext => $"منصرف اليوم: {PosFormatting.FormatCurrency(Transactions.Where(t => !t.IsIncoming && t.Timestamp.Date == DateTime.Today).Sum(t => t.Amount))}";
    public string LimitWarningsCountDisplay => $"{Accounts.Count(a => a.HasLimitWarning)} تنبيهات";
    public string LimitWarningsSubtext => Accounts.Any(a => a.HasLimitWarning) ? "محافظ اقتربت من الحدود المحددة" : "كل الحدود ضمن النطاق الآمن";
    public string StatusMessage { get => statusMessage; private set { if (statusMessage == value) return; statusMessage = value; Changed(); Changed(nameof(HasStatusMessage)); } }
    public bool IsErrorStatus { get => isErrorStatus; private set { if (isErrorStatus == value) return; isErrorStatus = value; Changed(); } }
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool RefreshFromDatabase { get => refreshFromDatabase; private set { if (refreshFromDatabase == value) return; refreshFromDatabase = value; Changed(); } }
    public string DataSourceLabel => repository is null ? "تصميم محلي بدون اتصال" : "قاعدة البيانات (SQL Server)";
    public FinancialRepository? Repository => repository;

    public Action<AccountEditorViewModel>? OpenAccountEditorRequested { get; set; }
    public Action<AccountTransactionViewModel>? OpenTransactionRequested { get; set; }
    public Action<AccountTransferViewModel>? OpenTransferRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    public RelayCommand<string> AddWalletCommand { get; }
    public ActionCommand AddVodafoneWalletCommand { get; }
    public ActionCommand AddInstaPayWalletCommand { get; }
    public ActionCommand EditSelectedAccountCommand { get; }
    public ActionCommand ToggleSelectedAccountCommand { get; }
    public ActionCommand SetSelectedAsDefaultCommand { get; }
    public ActionCommand DeleteSelectedAccountCommand { get; }
    public ActionCommand NewSelectedTransactionCommand { get; }
    public ActionCommand NewCashTransactionCommand { get; }
    public ActionCommand TransferFromSelectedCommand { get; }
    public ActionCommand TransferFromCashCommand { get; }
    public ActionCommand SelectCashCommand { get; }
    public ActionCommand ClearWalletFiltersCommand { get; }
    public ActionCommand ClearTransactionFiltersCommand { get; }
    public ActionCommand RefreshCommand { get; }
    public RelayCommand<FinancialAccountDesignItem> SelectAccountCommand { get; }

    public MyAccountsViewModel() : this("المشرف") { }

    public MyAccountsViewModel(string currentUserName) : this(currentUserName, null) { }

    public MyAccountsViewModel(string currentUserName, FinancialRepository? repository)
    {
        this.currentUserName = currentUserName;
        this.repository = repository;

        if (repository is not null)
        {
            RefreshFromDatabase = true;
            LoadFromDatabase();
        }
        else
        {
            Accounts = FinanceDesignSeedData.CreateAccounts();
            Transactions = FinanceDesignSeedData.CreateTransactions();
            nextAccountId = Accounts.Max(a => a.Id) + 1;
            nextTransactionId = Transactions.Max(t => t.Id) + 1;
        }

        AddWalletCommand = new RelayCommand<string>(provider => AddWallet(provider == "إنستاباي" ? FinancialAccountKind.InstaPay : FinancialAccountKind.VodafoneCash));
        AddVodafoneWalletCommand = new ActionCommand(() => AddWallet(FinancialAccountKind.VodafoneCash));
        AddInstaPayWalletCommand = new ActionCommand(() => AddWallet(FinancialAccountKind.InstaPay));
        EditSelectedAccountCommand = new ActionCommand(EditSelectedAccount);
        ToggleSelectedAccountCommand = new ActionCommand(ToggleSelectedAccount);
        SetSelectedAsDefaultCommand = new ActionCommand(SetSelectedAsDefault);
        DeleteSelectedAccountCommand = new ActionCommand(DeleteSelectedAccount);
        NewSelectedTransactionCommand = new ActionCommand(() => OpenTransaction(SelectedAccount));
        NewCashTransactionCommand = new ActionCommand(() => OpenTransaction(CashAccount));
        TransferFromSelectedCommand = new ActionCommand(() => OpenTransfer(SelectedAccount));
        TransferFromCashCommand = new ActionCommand(() => OpenTransfer(CashAccount));
        SelectCashCommand = new ActionCommand(() => SelectedAccount = CashAccount);
        ClearWalletFiltersCommand = new ActionCommand(ClearWalletFilters);
        ClearTransactionFiltersCommand = new ActionCommand(() => { TransactionSearchText = ""; SelectedTransactionType = "كل الحركات"; });
        RefreshCommand = new ActionCommand(() => { if (repository is not null) LoadFromDatabase(); else RecalculateAll(); SetStatus("تم تحديث الأرصدة واستهلاك الحدود وسجل الحركات.", false); });
        SelectAccountCommand = new RelayCommand<FinancialAccountDesignItem>(account => { if (account is not null) SelectedAccount = account; });

        RecalculateAll();
        SelectedAccount = FilteredWallets.FirstOrDefault();
    }

    public void NotifyExternalDataChanged()
    {
        if (repository is not null) LoadFromDatabase();
        else RecalculateAll();
    }

    private void LoadFromDatabase()
    {
        if (repository is null) return;
        try
        {
            repository.LoadAccountsInto(Accounts);
            repository.LoadTransactionsInto(Transactions, 0);
            nextAccountId = Accounts.Max(a => a.Id) + 1;
            nextTransactionId = Transactions.Max(t => t.Id) + 1;
            RecalculateAll();
            SetStatus("تم تحميل البيانات من قاعدة البيانات بنجاح.", false);
        }
        catch (Exception ex)
        {
            SetStatus($"تعذّر الاتصال بقاعدة البيانات: {ex.Message}", true);
        }
    }

    /// <summary>Loads customer transfer operations from the database into the given collections.</summary>
    public void LoadCustomerTransfers(
        ObservableCollection<VodafoneTransferOperation> vodafoneTarget,
        ObservableCollection<InstapayTransferOperation> instapayTarget)
    {
        if (repository is null) return;
        try
        {
            repository.LoadCustomerTransfersInto(vodafoneTarget, instapayTarget);
        }
        catch (Exception ex)
        {
            SetStatus($"تعذّر تحميل سجلات التحويلات: {ex.Message}", true);
        }
    }

    /// <summary>Loads InstaPay customer transfer operations from the database.</summary>
    public void LoadInstapayTransfers(ObservableCollection<InstapayTransferOperation> target)
    {
        if (repository is null) return;
        try
        {
            var empty = new ObservableCollection<VodafoneTransferOperation>();
            repository.LoadCustomerTransfersInto(empty, target);
        }
        catch (Exception ex)
        {
            SetStatus($"تعذّر تحميل سجلات التحويلات: {ex.Message}", true);
        }
    }

    /// <summary>Loads Vodafone Cash customer transfer operations from the database.</summary>
    public void LoadVodafoneTransfers(ObservableCollection<VodafoneTransferOperation> target)
    {
        if (repository is null) return;
        try
        {
            var empty = new ObservableCollection<InstapayTransferOperation>();
            repository.LoadCustomerTransfersInto(target, empty);
        }
        catch (Exception ex)
        {
            SetStatus($"تعذّر تحميل سجلات التحويلات: {ex.Message}", true);
        }
    }

    private void AddWallet(FinancialAccountKind kind)
    {
        var vm = new AccountEditorViewModel(kind, null, ValidateIdentifier);
        OpenAccountEditorRequested?.Invoke(vm);
        if (vm.Result is not { } result) return;

        if (repository is not null)
        {
            try
            {
                var saved = repository.SaveAccount(null, result.Kind, result.Alias, result.Identifier,
                    result.OpeningBalance, result.DailyDepositLimit, result.DailyWithdrawalLimit,
                    result.MonthlyDepositLimit, result.MonthlyWithdrawalLimit, result.Notes,
                    !Accounts.Any(a => a.Kind == result.Kind && a.IsDefault));
                LoadFromDatabase();
                var refreshedAccount = Accounts.First(a => a.Id == saved.AccountId);
                SelectedAccount = refreshedAccount;
                SetStatus($"تمت إضافة «{refreshedAccount.Alias}» برصيد مستقل وحدود تشغيل خاصة.", false);
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر إضافة المحفظة: {ex.Message}", true);
            }
            return;
        }

        var account = new FinancialAccountDesignItem
        {
            Id = nextAccountId++, Kind = result.Kind, Alias = result.Alias, Identifier = result.Identifier, Balance = result.OpeningBalance,
            DailyDepositLimit = result.DailyDepositLimit, DailyWithdrawalLimit = result.DailyWithdrawalLimit,
            MonthlyDepositLimit = result.MonthlyDepositLimit, MonthlyWithdrawalLimit = result.MonthlyWithdrawalLimit, Notes = result.Notes,
            IsDefault = !Accounts.Any(a => a.Kind == result.Kind && a.IsDefault)
        };
        Accounts.Add(account);
        if (result.OpeningBalance > 0m)
            AddTransaction(account, true, "رصيد افتتاحي", result.OpeningBalance, "إضافة محفظة", result.Notes, false);
        RecalculateAll(); SelectedAccount = account;
        SetStatus($"تمت إضافة «{account.Alias}» برصيد مستقل وحدود تشغيل خاصة.", false);
    }

    private void EditSelectedAccount()
    {
        if (SelectedAccount is null) return;
        if (SelectedAccount.IsCash) { SetStatus("الحساب النقدي موحد وثابت؛ استخدم «تسوية / إيداع / سحب» لتعديل رصيده.", true); return; }
        var account = SelectedAccount;
        var vm = new AccountEditorViewModel(account.Kind, account, ValidateIdentifier);
        OpenAccountEditorRequested?.Invoke(vm);
        if (vm.Result is not { } result) return;

        if (repository is not null)
        {
            try
            {
                repository.SaveAccount(account.Id, result.Kind, result.Alias, result.Identifier,
                    account.Balance, result.DailyDepositLimit, result.DailyWithdrawalLimit,
                    result.MonthlyDepositLimit, result.MonthlyWithdrawalLimit, result.Notes, result.Kind == account.Kind && account.IsDefault);
                LoadFromDatabase();
                var refreshed = Accounts.FirstOrDefault(a => a.Id == account.Id);
                if (refreshed is not null) SelectedAccount = refreshed;
                SetStatus($"تم حفظ إعدادات «{result.Alias}» وحدود الإيداع والسحب.", false);
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر تعديل المحفظة: {ex.Message}", true);
            }
            return;
        }

        account.Alias = result.Alias; account.Identifier = result.Identifier;
        account.DailyDepositLimit = result.DailyDepositLimit; account.DailyWithdrawalLimit = result.DailyWithdrawalLimit;
        account.MonthlyDepositLimit = result.MonthlyDepositLimit; account.MonthlyWithdrawalLimit = result.MonthlyWithdrawalLimit; account.Notes = result.Notes;
        RecalculateAll(); RefreshSelectedTransactions();
        SetStatus($"تم حفظ إعدادات «{account.Alias}» وحدود الإيداع والسحب.", false);
    }

    private void ToggleSelectedAccount()
    {
        if (SelectedAccount is null) return;
        if (SelectedAccount.IsCash) { SetStatus("لا يمكن إيقاف الخزنة النقدية لأنها حساب أساسي مرتبط بالنظام.", true); return; }
        var account = SelectedAccount;
        account.IsActive = !account.IsActive;
        if (!account.IsActive) account.IsDefault = false;
        NotifyKpis();
        SetStatus(account.IsActive ? $"تم تفعيل «{account.Alias}» وإتاحة الحركات." : $"تم إيقاف «{account.Alias}» مؤقتًا مع الاحتفاظ بالرصيد والسجل.", false);
    }

    private void SetSelectedAsDefault()
    {
        if (SelectedAccount is null || SelectedAccount.IsCash) return;
        if (!SelectedAccount.IsActive) { SetStatus("فعّل المحفظة أولًا قبل تعيينها كافتراضية.", true); return; }
        foreach (var account in Accounts.Where(a => a.Kind == SelectedAccount.Kind)) account.IsDefault = ReferenceEquals(account, SelectedAccount);
        SetStatus($"تم تعيين «{SelectedAccount.Alias}» كمحفظة {SelectedAccount.ProviderTitle} الافتراضية.", false);
    }

    private void DeleteSelectedAccount()
    {
        if (SelectedAccount is null || SelectedAccount.IsCash) return;
        var account = SelectedAccount;

        if (repository is not null)
        {
            var confirm = new ConfirmActionViewModel("تأكيد حذف المحفظة", account.DisplayName,
                "سيتم حذف المحفظة نهائيًا إذا كانت مكتملة (بلا رصيد وبلا حركات). إذا كان لها سجل، سيتم إيقافا مؤقتًا مع الاحتفاظ بالأرشيف. هل تريد المتابعة؟",
                "حذف المحفظة");
            void Delete()
            {
                try
                {
                    var (deleted, message) = repository.DeleteAccount(account.Id, account.Alias);
                    LoadFromDatabase();
                    SelectedAccount = FilteredWallets.FirstOrDefault();
                    SetStatus(message, false);
                }
                catch (Exception ex)
                {
                    SetStatus($"تعذّر حذف المحفظة: {ex.Message}", true);
                }
            }
            if (OpenConfirmDialogRequested is not null) OpenConfirmDialogRequested(confirm, Delete); else Delete();
            return;
        }

        if (account.Balance != 0m) { SetStatus("لا يمكن حذف محفظة عليها رصيد؛ حوّل أو اسحب الرصيد أولًا.", true); return; }
        if (Transactions.Any(t => t.AccountId == account.Id)) { SetStatus("لا يمكن حذف محفظة لها سجل حركات؛ يمكنك إيقافها مؤقتًا للحفاظ على الأرشيف.", true); return; }
        var confirmLocal = new ConfirmActionViewModel("تأكيد حذف المحفظة", account.DisplayName, "المحفظة بلا رصيد أو حركات. سيتم حذفها نهائيًا من التصميم المحلي. هل تريد المتابعة؟", "حذف المحفظة");
        void DeleteLocal() { Accounts.Remove(account); RecalculateAll(); SelectedAccount = FilteredWallets.FirstOrDefault(); SetStatus($"تم حذف «{account.Alias}».", false); }
        if (OpenConfirmDialogRequested is not null) OpenConfirmDialogRequested(confirmLocal, DeleteLocal); else DeleteLocal();
    }

    private void OpenTransaction(FinancialAccountDesignItem? account)
    {
        if (account is null) return;
        if (!account.IsActive) { SetStatus("لا يمكن تسجيل حركة على حساب موقوف.", true); return; }
        var vm = new AccountTransactionViewModel(account);
        OpenTransactionRequested?.Invoke(vm);
        if (vm.Result is not { } result) return;
        var incoming = result.Kind is FinancialMovementKind.Deposit or FinancialMovementKind.PositiveAdjustment;
        var counts = result.Kind is FinancialMovementKind.Deposit or FinancialMovementKind.Withdrawal;
        var category = result.Kind switch { FinancialMovementKind.Deposit => "إيداع", FinancialMovementKind.Withdrawal => "سحب", FinancialMovementKind.PositiveAdjustment => "تسوية بالإضافة", _ => "تسوية بالخصم" };

        if (repository is not null)
        {
            try
            {
                repository.SaveTransaction(account.Id, result.Kind, result.Amount,
                    string.IsNullOrWhiteSpace(result.Counterparty) ? category : result.Counterparty,
                    result.Note, counts);
                LoadFromDatabase();
                var refreshed = Accounts.FirstOrDefault(a => a.Id == account.Id);
                if (refreshed is not null) SelectedAccount = refreshed;
                SetStatus($"تم تسجيل {category} بقيمة {PosFormatting.FormatCurrency(result.Amount)} على «{account.DisplayName}».", false);
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر تسجيل الحركة: {ex.Message}", true);
            }
            return;
        }

        account.Balance += incoming ? result.Amount : -result.Amount;
        AddTransaction(account, incoming, category, result.Amount, string.IsNullOrWhiteSpace(result.Counterparty) ? category : result.Counterparty, result.Note, counts);
        RecalculateAll(); RefreshSelectedTransactions(); NotifyKpis();
        SetStatus($"تم تسجيل {category} بقيمة {PosFormatting.FormatCurrency(result.Amount)} على «{account.DisplayName}».", false);
    }

    private void OpenTransfer(FinancialAccountDesignItem? source)
    {
        if (source is null) return;
        if (!source.IsActive) { SetStatus("فعّل حساب المصدر قبل التحويل.", true); return; }
        var vm = new AccountTransferViewModel(source, Accounts);
        OpenTransferRequested?.Invoke(vm);
        if (vm.Result is not { } result) return;

        if (repository is not null)
        {
            try
            {
                repository.SaveTransaction(source.Id, FinancialMovementKind.Withdrawal, result.Amount,
                    $"إلى {result.Target.DisplayName}", result.Note, true);
                repository.SaveTransaction(result.Target.Id, FinancialMovementKind.Deposit, result.Amount,
                    $"من {source.DisplayName}", result.Note, true);
                LoadFromDatabase();
                var refreshedSource = Accounts.FirstOrDefault(a => a.Id == source.Id);
                if (refreshedSource is not null) SelectedAccount = refreshedSource;
                SetStatus($"تم تحويل {PosFormatting.FormatCurrency(result.Amount)} من «{source.DisplayName}» إلى «{result.Target.DisplayName}».", false);
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر التحويل: {ex.Message}", true);
            }
            return;
        }

        source.Balance -= result.Amount; result.Target.Balance += result.Amount;
        AddTransaction(source, false, "تحويل صادر", result.Amount, $"إلى {result.Target.DisplayName}", result.Note, true);
        AddTransaction(result.Target, true, "تحويل وارد", result.Amount, $"من {source.DisplayName}", result.Note, true);
        RecalculateAll(); RefreshSelectedTransactions();
        SetStatus($"تم تحويل {PosFormatting.FormatCurrency(result.Amount)} من «{source.DisplayName}» إلى «{result.Target.DisplayName}».", false);
    }

    private void AddTransaction(FinancialAccountDesignItem account, bool incoming, string category, decimal amount, string counterparty, string note, bool counts)
    {
        Transactions.Insert(0, new FinancialTransactionDesignItem
        {
            Id = nextTransactionId++, AccountId = account.Id, Timestamp = DateTime.Now, IsIncoming = incoming, Category = category,
            Amount = amount, BalanceAfter = account.Balance, Counterparty = counterparty, Note = note,
            ActorName = currentUserName, CountsTowardLimits = counts
        });
    }

    private string? ValidateIdentifier(FinancialAccountKind kind, string identifier, int? editingId)
    {
        var normalized = identifier.Trim();
        var duplicate = Accounts.FirstOrDefault(a => a.Id != (editingId ?? -1) && a.Kind == kind && a.Identifier.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return duplicate is null ? null : $"هذا الحساب مسجل بالفعل باسم «{duplicate.Alias}». كل رقم أو عنوان يجب أن يكون مستقلًا.";
    }

    private void ClearWalletFilters()
    {
        SelectedProviderFilter = "كل المحافظ"; SelectedStatusFilter = "كل الحالات"; WalletSearchText = "";
    }

    private void RefreshFilteredWallets()
    {
        var query = WalletSearchText.Trim();
        IEnumerable<FinancialAccountDesignItem> filtered = Accounts.Where(a => !a.IsCash);
        if (SelectedProviderFilter != "كل المحافظ") filtered = filtered.Where(a => a.ProviderTitle == SelectedProviderFilter);
        filtered = SelectedStatusFilter switch
        {
            "النشطة فقط" => filtered.Where(a => a.IsActive),
            "الموقوفة فقط" => filtered.Where(a => !a.IsActive),
            "تحتاج مراجعة الحدود" => filtered.Where(a => a.HasLimitWarning),
            _ => filtered
        };
        if (query.Length > 0) filtered = filtered.Where(a => a.Alias.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Identifier.Contains(query, StringComparison.OrdinalIgnoreCase) || a.ProviderTitle.Contains(query, StringComparison.OrdinalIgnoreCase));
        FilteredWallets.Clear(); foreach (var account in filtered.OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.IsActive).ThenBy(a => a.Id)) FilteredWallets.Add(account);
        Changed(nameof(HasNoFilteredWallets));
        if (SelectedAccount is not null && !SelectedAccount.IsCash && !FilteredWallets.Contains(SelectedAccount)) SelectedAccount = FilteredWallets.FirstOrDefault();
    }

    private void RefreshSelectedTransactions()
    {
        SelectedAccountTransactions.Clear();
        if (SelectedAccount is not null)
        {
            var query = TransactionSearchText.Trim();
            IEnumerable<FinancialTransactionDesignItem> rows = Transactions.Where(t => t.AccountId == SelectedAccount.Id);
            rows = SelectedTransactionType switch
            {
                "وارد" => rows.Where(t => t.IsIncoming),
                "منصرف" => rows.Where(t => !t.IsIncoming),
                "إيداع وسحب" => rows.Where(t => t.Category is "إيداع" or "سحب"),
                "تحصيل وسداد النظام" => rows.Where(t => t.Category.Contains("تحصيل") || t.Category.Contains("سداد") || t.Category.Contains("مبيعات") || t.Category.Contains("صيانة")),
                "تسويات وتحويلات" => rows.Where(t => t.Category.Contains("تسوية") || t.Category.Contains("تحويل")),
                _ => rows
            };
            if (query.Length > 0) rows = rows.Where(t => t.Category.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Counterparty.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Note.Contains(query, StringComparison.OrdinalIgnoreCase));
            foreach (var row in rows.OrderByDescending(t => t.Timestamp).ThenByDescending(t => t.Id)) SelectedAccountTransactions.Add(row);
        }
        Changed(nameof(HasNoSelectedTransactions));
    }

    private void RecalculateAll()
    {
        foreach (var account in Accounts) account.RecalculateUsage(Transactions);
        RefreshFilteredWallets(); RefreshSelectedTransactions(); NotifyKpis();
    }

    private void NotifyKpis()
    {
        Changed(nameof(TotalLiquidityDisplay)); Changed(nameof(ActiveAccountsSubtext)); Changed(nameof(DigitalBalancesDisplay));
        Changed(nameof(DigitalBreakdownSubtext)); Changed(nameof(TodayIncomingDisplay)); Changed(nameof(TodayOutgoingSubtext));
        Changed(nameof(LimitWarningsCountDisplay)); Changed(nameof(LimitWarningsSubtext));
    }

    private void SetStatus(string message, bool error) { IsErrorStatus = error; StatusMessage = message; }
}
