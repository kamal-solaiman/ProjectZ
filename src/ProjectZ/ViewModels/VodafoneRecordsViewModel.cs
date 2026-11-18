using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed record VodafoneWalletFilterOption(int Id, string Title);

public sealed class VodafoneRecordsViewModel : ObservableObject
{
    private readonly VodafoneTransferViewModel transfers;
    private readonly MyAccountsViewModel accounts;
    private readonly string currentUserName;
    private VodafoneTransferOperation? selectedOperation;
    // Keep the identity/version opened by the dialog, not the row a subsequent refresh selects.
    private sealed record EditTarget(VodafoneTransferOperation Operation, byte[] Version);
    private EditTarget? editTarget;
    private DateTime? fromDate;
    private DateTime? toDate;
    private int selectedWalletId;
    private string selectedKind = "كل العمليات";
    private string selectedStatus = "كل الحالات";
    private string customerFilter = "";
    private string userFilter = "";
    private string minimumAmountText = "";
    private string maximumAmountText = "";
    private string minimumCommissionText = "";
    private string maximumCommissionText = "";
    private string statusMessage = "سجلات الجلسة الحالية — التعديل والعكس والحذف ينشئ حركات تسوية في حساباتي.";
    private bool isErrorStatus;
    private FinancialAccountDesignItem? editWallet;
    private string editKind = "إيداع للعميل";
    private string editCustomerNumber = "";
    private string editAmountText = "";
    private string editCommissionText = "";
    private string editNote = "";

    // A line can be stopped or deleted from «حساباتي» while its operations stay in the ledger, and
    // LoadAccountsInto only loads IsActive = 1 rows, so the saved operation can outlive its wallet.
    // Every edit/reverse path must resolve the wallet with FirstOrDefault: First(predicate) threw
    // Enumerable.ThrowNoMatchException (an unhandled crash) as soon as the wallet row was gone.
    public const string MissingWalletMessage =
        "الخط المرتبط بهذه العملية غير موجود أو موقوف في «حساباتي»، فلا يمكن تعديل العملية أو عكسها. فعّل الخط من «حساباتي» واضغط «تحديث» ثم حاول مرة أخرى.";
    /// <summary>Movements that belong to another screen stay read-only here.</summary>
    public const string ReadOnlyMovementMessage =
        "هذه حركة دفتر للقراءة فقط. لتصحيح تحويل اختر سجله النشط؛ وراجع حركات البيع والشراء والصيانة والمصروف في شاشاتها.";

    public const string MissingCashMessage =
        "لا يوجد حساب «النقدية» نشط في «حساباتي»، فلا يمكن تسوية أثر العملية. راجع الحساب النقدي واضغط «تحديث» ثم حاول مرة أخرى.";

    // Every movement of the wallets of this kind: customer transfers plus sales, purchases,
    // returns, maintenance, expenses, wallet transfers and manual adjustments.
    private readonly ObservableCollection<VodafoneTransferOperation> allOperations = [];
    public ObservableCollection<VodafoneTransferOperation> FilteredOperations { get; } = [];
    public ObservableCollection<VodafoneWalletFilterOption> WalletFilters { get; } = [];
    public ObservableCollection<FinancialAccountDesignItem> EditWallets { get; } = [];
    public ObservableCollection<string> KindFilters { get; } = ["كل العمليات"];
    public IReadOnlyList<string> StatusFilters { get; } = ["كل الحالات", "ناجحة", "معكوسة"];
    public IReadOnlyList<string> EditKindOptions { get; } = ["إيداع للعميل", "سحب للعميل"];

    public VodafoneTransferOperation? SelectedOperation { get => selectedOperation; set { if (ReferenceEquals(selectedOperation, value)) return; selectedOperation = value; Changed(); Changed(nameof(HasSelectedOperation)); } }
    public bool HasSelectedOperation => SelectedOperation is not null;
    public DateTime? FromDate { get => fromDate; set { fromDate = value; Changed(); ApplyFilters(); } }
    public DateTime? ToDate { get => toDate; set { toDate = value; Changed(); ApplyFilters(); } }
    public int SelectedWalletId { get => selectedWalletId; set { selectedWalletId = value; Changed(); ApplyFilters(); } }
    public string SelectedKind { get => selectedKind; set { selectedKind = value; Changed(); ApplyFilters(); } }
    public string SelectedStatus { get => selectedStatus; set { selectedStatus = value; Changed(); ApplyFilters(); } }
    public string CustomerFilter { get => customerFilter; set { customerFilter = value; Changed(); ApplyFilters(); } }
    public string UserFilter { get => userFilter; set { userFilter = value; Changed(); ApplyFilters(); } }
    public string MinimumAmountText { get => minimumAmountText; set { minimumAmountText = value; Changed(); ApplyFilters(); } }
    public string MaximumAmountText { get => maximumAmountText; set { maximumAmountText = value; Changed(); ApplyFilters(); } }
    public string MinimumCommissionText { get => minimumCommissionText; set { minimumCommissionText = value; Changed(); ApplyFilters(); } }
    public string MaximumCommissionText { get => maximumCommissionText; set { maximumCommissionText = value; Changed(); ApplyFilters(); } }
    public bool HasNoOperations => FilteredOperations.Count == 0;
    public string ResultCountDisplay => $"{FilteredOperations.Count} من {allOperations.Count} حركة";
    public string FilteredAmountDisplay => PosFormatting.FormatCurrency(FilteredOperations.Sum(o => o.Amount));
    public string FilteredCommissionDisplay => PosFormatting.FormatCurrency(FilteredOperations.Where(o => o.CanEditOrDelete).Sum(o => o.Commission));
    public string StatusMessage { get => statusMessage; private set { statusMessage = value; Changed(); } }
    public bool IsErrorStatus { get => isErrorStatus; private set { isErrorStatus = value; Changed(); } }

    public FinancialAccountDesignItem? EditWallet { get => editWallet; set { editWallet = value; Changed(); } }
    public string EditKind { get => editKind; set { editKind = value; Changed(); } }
    public string EditCustomerNumber { get => editCustomerNumber; set { editCustomerNumber = value; Changed(); } }
    public string EditAmountText { get => editAmountText; set { editAmountText = value; Changed(); } }
    public string EditCommissionText { get => editCommissionText; set { editCommissionText = value; Changed(); } }
    public string EditNote { get => editNote; set { editNote = value; Changed(); } }

    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand RefreshCommand { get; }
    public RelayCommand<VodafoneTransferOperation> SelectOperationCommand { get; }
    public RelayCommand<VodafoneTransferOperation> ShowOperationDetailsCommand { get; }
    public RelayCommand<VodafoneTransferOperation> BeginOperationEditCommand { get; }
    public RelayCommand<VodafoneTransferOperation> ReverseOperationCommand { get; }
    public RelayCommand<VodafoneTransferOperation> DeleteOperationCommand { get; }
    public Action<VodafoneTransferOperation>? DetailsRequested { get; set; }
    public Action? EditRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? ConfirmRequested { get; set; }

    public VodafoneRecordsViewModel() : this(new MyAccountsViewModel(), "المشرف") { }
    private VodafoneRecordsViewModel(MyAccountsViewModel accounts, string currentUserName) : this(new VodafoneTransferViewModel(accounts, currentUserName), accounts, currentUserName) { }
    public VodafoneRecordsViewModel(VodafoneTransferViewModel transfers, MyAccountsViewModel accounts, string currentUserName)
    {
        this.transfers = transfers;
        this.accounts = accounts;
        this.currentUserName = currentUserName;
        ClearFiltersCommand = new ActionCommand(ClearFilters);
        RefreshCommand = new ActionCommand(NotifyExternalDataChanged);
        SelectOperationCommand = new RelayCommand<VodafoneTransferOperation>(operation => SelectedOperation = operation);
        ShowOperationDetailsCommand = new RelayCommand<VodafoneTransferOperation>(operation => { if (operation is not null) { SelectedOperation = operation; DetailsRequested?.Invoke(operation); } });
        BeginOperationEditCommand = new RelayCommand<VodafoneTransferOperation>(operation => { if (operation is not null) { SelectedOperation = operation; BeginEdit(); } });
        ReverseOperationCommand = new RelayCommand<VodafoneTransferOperation>(operation => { if (operation is not null) { SelectedOperation = operation; RequestReverse(fromDelete: false); } });
        DeleteOperationCommand = new RelayCommand<VodafoneTransferOperation>(operation => { if (operation is not null) { SelectedOperation = operation; RequestReverse(fromDelete: true); } });
        NotifyExternalDataChanged();
    }

    public void NotifyExternalDataChanged()
    {
        try { ReloadRows(); }
        catch (FinancialDataException ex) { allOperations.Clear(); ApplyFilters(); SetStatus(ex.Message, true); }
    }

    private void ReloadRows()
    {
        transfers.NotifyExternalDataChanged();
        LoadAllOperations();
        var walletId = SelectedWalletId;
        WalletFilters.Clear(); EditWallets.Clear();
        WalletFilters.Add(new VodafoneWalletFilterOption(0, "كل خطوط فودافون"));
        foreach (var wallet in accounts.Accounts.Where(a => a.Kind == FinancialAccountKind.VodafoneCash).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id))
        {
            WalletFilters.Add(new VodafoneWalletFilterOption(wallet.Id, $"{wallet.Alias} — {wallet.Identifier}"));
            EditWallets.Add(wallet);
        }
        SelectedWalletId = WalletFilters.Any(w => w.Id == walletId) ? walletId : 0;
        ApplyFilters();
    }

    /// <summary>
    /// Loads the whole wallet history. With a database the rows come from dbo.FinancialTransactions (so
    /// sales, purchases, returns, maintenance, expenses and transfers all appear); the design mode merges
    /// the customer-transfer seed with the wallet rows of the shared ledger.
    /// </summary>
    private void LoadAllOperations()
    {
        allOperations.Clear();
        if (accounts.Repository is not null)
        {
            accounts.Repository.LoadVodafoneWalletMovementsInto(allOperations);
        }
        else
        {
            foreach (var operation in transfers.Operations) allOperations.Add(operation);
            foreach (var movement in accounts.Transactions)
            {
                var account = accounts.Accounts.FirstOrDefault(candidate => candidate.Id == movement.AccountId);
                if (account is null || account.Kind != FinancialAccountKind.VodafoneCash) continue;
                allOperations.Add(new VodafoneTransferOperation
                {
                    MovementId = movement.Id,
                    WalletId = movement.AccountId,
                    WalletAlias = account.Alias,
                    WalletNumber = account.Identifier,
                    Kind = movement.IsIncoming ? VodafoneOperationKind.DepositToCustomer : VodafoneOperationKind.WithdrawForCustomer,
                    CustomerNumber = movement.Counterparty,
                    Amount = movement.Amount,
                    WalletBalanceAfter = movement.BalanceAfter,
                    Note = movement.Note,
                    Timestamp = movement.Timestamp,
                    ActorName = movement.ActorName,
                    IsCustomerTransfer = false,
                    IsIncoming = movement.IsIncoming,
                    SourceTitle = movement.Category,
                    CategoryTitle = movement.Category
                });
            }
        }

        RebuildKindFilters();
    }

    private void RebuildKindFilters()
    {
        var previous = selectedKind;
        var titles = allOperations.Select(operation => operation.OperationTitle)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct()
            .OrderBy(title => title, StringComparer.Ordinal)
            .ToList();
        KindFilters.Clear();
        KindFilters.Add("كل العمليات");
        foreach (var title in titles) KindFilters.Add(title);
        selectedKind = KindFilters.Contains(previous) ? previous : "كل العمليات";
        Changed(nameof(SelectedKind));
    }

    public void ApplyFilters()
    {
        FilteredOperations.Clear();
        foreach (var operation in allOperations.Where(Matches).OrderByDescending(o => o.Timestamp).ThenByDescending(o => o.Id)) FilteredOperations.Add(operation);
        if (SelectedOperation is not null && !FilteredOperations.Contains(SelectedOperation)) SelectedOperation = FilteredOperations.FirstOrDefault();
        Changed(nameof(HasNoOperations)); Changed(nameof(ResultCountDisplay)); Changed(nameof(FilteredAmountDisplay)); Changed(nameof(FilteredCommissionDisplay));
    }

    private bool Matches(VodafoneTransferOperation operation)
    {
        if (FromDate is { } from && operation.Timestamp.Date < from.Date) return false;
        if (ToDate is { } to && operation.Timestamp.Date > to.Date) return false;
        if (SelectedWalletId != 0 && operation.WalletId != SelectedWalletId) return false;
        if (SelectedKind != "كل العمليات" && operation.OperationTitle != SelectedKind) return false;
        if (SelectedStatus != "كل الحالات" && operation.StatusTitle != SelectedStatus) return false;
        if (!Contains(operation.CustomerNumber, CustomerFilter) || !Contains(operation.ActorName, UserFilter)) return false;
        return InRange(operation.Amount, MinimumAmountText, MaximumAmountText) && InRange(operation.Commission, MinimumCommissionText, MaximumCommissionText);
    }

    private static bool Contains(string source, string filter) => string.IsNullOrWhiteSpace(filter) || source.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool InRange(decimal value, string minimum, string maximum)
    {
        if (!string.IsNullOrWhiteSpace(minimum) && (!AccountEditorViewModel.TryMoney(minimum, out var min) || value < min)) return false;
        if (!string.IsNullOrWhiteSpace(maximum) && (!AccountEditorViewModel.TryMoney(maximum, out var max) || value > max)) return false;
        return true;
    }

    private void ClearFilters()
    {
        fromDate = null; toDate = null; selectedWalletId = 0; selectedKind = "كل العمليات"; selectedStatus = "كل الحالات";
        customerFilter = ""; userFilter = ""; minimumAmountText = ""; maximumAmountText = ""; minimumCommissionText = ""; maximumCommissionText = "";
        Changed(nameof(FromDate)); Changed(nameof(ToDate)); Changed(nameof(SelectedWalletId)); Changed(nameof(SelectedKind)); Changed(nameof(SelectedStatus));
        Changed(nameof(CustomerFilter)); Changed(nameof(UserFilter)); Changed(nameof(MinimumAmountText)); Changed(nameof(MaximumAmountText)); Changed(nameof(MinimumCommissionText)); Changed(nameof(MaximumCommissionText));
        ApplyFilters(); SetStatus("تم إلغاء جميع الفلاتر.", false);
    }

    private void BeginEdit()
    {
        editTarget = null;
        if (SelectedOperation is null) { SetStatus("اختر عملية أولًا.", true); return; }
        if (SelectedOperation.IsReversed) { SetStatus("لا يمكن تعديل عملية معكوسة.", true); return; }
        if (!SelectedOperation.IsCustomerTransfer) { SetStatus(ReadOnlyMovementMessage, true); return; }
        // Do not open the editor for an operation whose line is gone: the dialog would show an
        // empty wallet picker and saving used to crash with ThrowNoMatchException.
        if (FindWallet(SelectedOperation.WalletId) is not { } wallet) { SetStatus(MissingWalletMessage, true); return; }
        editTarget = new(SelectedOperation, SelectedOperation.RowVersion.ToArray());
        EditWallet = wallet;
        EditKind = SelectedOperation.OperationTitle; EditCustomerNumber = SelectedOperation.CustomerNumber;
        EditAmountText = SelectedOperation.Amount.ToString("0.##", CultureInfo.InvariantCulture); EditCommissionText = SelectedOperation.Commission.ToString("0.##", CultureInfo.InvariantCulture);
        EditNote = SelectedOperation.Note;
        EditRequested?.Invoke();
    }

    public bool ApplyEdit()
    {
        var target = editTarget;
        if (target is null || target.Operation.IsReversed || EditWallet is null) { SetStatus("اختر عملية وخط فودافون صالحين.", true); return false; }
        var customer = new string(EditCustomerNumber.Where(char.IsDigit).ToArray());
        if (customer.Length != 11 || !customer.StartsWith("01", StringComparison.Ordinal)) { SetStatus("رقم العميل يجب أن يتكون من 11 رقمًا ويبدأ بـ 01.", true); return false; }
        if (!AccountEditorViewModel.TryMoney(EditAmountText, out var amount) || amount <= 0m || !AccountEditorViewModel.TryMoney(EditCommissionText, out var commission) || commission < 0m) { SetStatus("أدخل مبلغًا أكبر من صفر وعمولة صحيحة.", true); return false; }
        var operation = target.Operation;
        var oldWallet = FindWallet(operation.WalletId);
        if (oldWallet is null) { SetStatus(MissingWalletMessage, true); return false; }
        var newDeposit = EditKind == "إيداع للعميل";
        if (!CanUndo(operation, out var undoError)) { SetStatus(undoError, true); return false; }
        var validationError = ValidateNewEffect(EditWallet, newDeposit, amount, commission, operation);
        if (validationError is not null) { SetStatus(validationError, true); return false; }
        if (accounts.Repository is not null)
        {
            try
            {
                var corrected = accounts.Repository.UpdateCustomerTransfer(operation.Id, target.Version, FinancialAccountKind.VodafoneCash,
                    new(EditWallet.Id, newDeposit, customer, amount, commission, EditNote));
                editTarget = null;
                RefreshAfterPost($"تم عكس التحويل رقم {operation.Id} وحفظ التصحيح برقم {corrected.TransferId}.");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"تعذّر تعديل السجل: {ex.Message}", true);
                return false;
            }
        }
        editTarget = null;
        ApplyEffect(oldWallet, operation.CustomerNumber, operation.Amount, operation.Commission, operation.IsDeposit, true, "عكس قبل تعديل");
        ApplyEffect(EditWallet, customer, amount, commission, newDeposit, false, "تطبيق تعديل السجل");
        operation.IsReversed = true; operation.ReversedBy = currentUserName; operation.ReversedAt = DateTime.Now;
        var replacement = new VodafoneTransferOperation
        {
            Id = transfers.Operations.Max(o => o.Id) + 1, CorrectsTransferId = operation.Id,
            WalletId = EditWallet.Id, WalletAlias = EditWallet.Alias, WalletNumber = EditWallet.Identifier,
            Kind = newDeposit ? VodafoneOperationKind.DepositToCustomer : VodafoneOperationKind.WithdrawForCustomer,
            CustomerNumber = customer, Amount = amount, Commission = commission, CashDelta = newDeposit ? amount + commission : commission - amount,
            WalletBalanceAfter = EditWallet.Balance, Note = EditNote.Trim(), Timestamp = DateTime.Now, ActorName = currentUserName
        };
        transfers.Operations.Insert(0, replacement);
        RefreshAfterPost($"تم عكس التحويل رقم {operation.Id} وحفظ التصحيح برقم {replacement.Id}.");
        return true;
    }

    private FinancialAccountDesignItem? FindWallet(int accountId) => accounts.Accounts.FirstOrDefault(w => w.Id == accountId);

    private string? ValidateNewEffect(FinancialAccountDesignItem wallet, bool deposit, decimal amount, decimal commission, VodafoneTransferOperation original)
    {
        if (!wallet.IsActive) return "خط فودافون المحدد موقوف.";
        var walletAfterUndo = wallet.Balance + (wallet.Id == original.WalletId ? (original.IsDeposit ? original.Amount : -original.Amount) : 0m);
        if (deposit && walletAfterUndo < amount) return "رصيد الخط لا يكفي لمبلغ الإيداع بعد التعديل.";
        var cashDelta = deposit ? amount + commission : commission - amount;
        if (accounts.CashAccount is not { } cash) return MissingCashMessage;
        if (cashDelta < 0m && cash.Balance - original.CashDelta < -cashDelta) return "النقدي لا يكفي لصافي العملية بعد التعديل.";
        return AccountTransactionViewModel.ValidateLimits(wallet, !deposit, amount);
    }

    private bool CanUndo(VodafoneTransferOperation operation, out string error)
    {
        var wallet = FindWallet(operation.WalletId);
        if (wallet is null) { error = MissingWalletMessage; return false; }
        if (accounts.CashAccount is not { } cash) { error = MissingCashMessage; return false; }
        if (!operation.IsDeposit && wallet.Balance < operation.Amount) { error = "رصيد الخط الحالي لا يسمح بعكس هذه العملية."; return false; }
        if (operation.CashDelta > 0m && cash.Balance < operation.CashDelta) { error = "الرصيد النقدي الحالي لا يسمح بعكس هذه العملية."; return false; }
        error = ""; return true;
    }

    private void RequestReverse(bool fromDelete)
    {
        if (SelectedOperation is null) { SetStatus("اختر عملية أولًا.", true); return; }
        if (SelectedOperation.IsReversed) { SetStatus("هذه العملية معكوسة بالفعل.", true); return; }
        if (!SelectedOperation.IsCustomerTransfer) { SetStatus(ReadOnlyMovementMessage, true); return; }
        var operation = SelectedOperation;
        var version = operation.RowVersion.ToArray(); // snapshot before opening the confirmation window
        var title = fromDelete ? "حذف سجل فودافون" : "عكس عملية فودافون";
        var reason = fromDelete ? $"تسوية حذف سجل فودافون رقم {operation.Id}" : $"عكس عملية فودافون رقم {operation.Id}";
        Confirm(new ConfirmActionViewModel(title, operation.DetailsDisplay,
            "سيُعكس أثر التحويل مع الاحتفاظ بسجله. لا يُحذف السجل نهائيًا.", "تأكيد العكس"), () =>
        {
            if (accounts.Repository is not null)
            {
                try
                {
                    accounts.Repository.ReverseCustomerTransfer(operation.Id, version, FinancialAccountKind.VodafoneCash, reason);
                    RefreshAfterPost($"تم عكس التحويل رقم {operation.Id} والاحتفاظ بسجله.");
                }
                catch (Exception ex) { SetStatus($"تعذّر عكس العملية: {ex.Message}", true); }
                return;
            }

            if (operation.IsReversed) { SetStatus("هذه العملية معكوسة بالفعل.", true); return; }
            if (!CanUndo(operation, out var error)) { SetStatus(error, true); return; }
            var wallet = FindWallet(operation.WalletId);
            if (wallet is null) { SetStatus(MissingWalletMessage, true); return; }
            ApplyEffect(wallet, operation.CustomerNumber, operation.Amount, operation.Commission, operation.IsDeposit, true, reason);
            operation.IsReversed = true; operation.ReversedBy = currentUserName; operation.ReversedAt = DateTime.Now;
            RefreshAfterPost($"تم عكس التحويل رقم {operation.Id} والاحتفاظ بسجله.");
        });
    }

    private void ApplyEffect(FinancialAccountDesignItem wallet, string customer, decimal amount, decimal commission, bool deposit, bool reverse, string category)
    {
        var cash = accounts.CashAccount ?? throw new InvalidOperationException(MissingCashMessage); // also checked by CanUndo
        var walletIncoming = deposit ? reverse : !reverse;
        wallet.Balance += walletIncoming ? amount : -amount;
        AddLedger(wallet, walletIncoming, $"{category} — الخط", amount, customer, true);
        if (deposit)
        {
            var incoming = !reverse;
            cash.Balance += incoming ? amount + commission : -(amount + commission);
            AddLedger(cash, incoming, $"{category} — النقدي والعمولة", amount + commission, customer, false);
        }
        else
        {
            var cashOutgoing = !reverse;
            cash.Balance += cashOutgoing ? -amount : amount;
            AddLedger(cash, !cashOutgoing, $"{category} — مبلغ النقدي", amount, customer, false);
            if (commission > 0m)
            {
                cash.Balance += cashOutgoing ? commission : -commission;
                AddLedger(cash, cashOutgoing, $"{category} — العمولة", commission, customer, false);
            }
        }
    }

    private void AddLedger(FinancialAccountDesignItem account, bool incoming, string category, decimal amount, string customer, bool counts)
    {
        var id = accounts.Transactions.Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
        accounts.Transactions.Insert(0, new FinancialTransactionDesignItem { Id = id, AccountId = account.Id, Timestamp = DateTime.Now, IsIncoming = incoming, Category = category, Amount = amount, BalanceAfter = account.Balance, Counterparty = customer, Note = "مراجعة من شاشة سجلات فودافون كاش", ActorName = currentUserName, CountsTowardLimits = counts });
    }

    // Do not report a committed posting as a failed save if only the subsequent read failed.
    private void RefreshAfterPost(string success)
    {
        try { ReloadRows(); SetStatus(success, false); }
        catch (Exception ex) { SetStatus($"{success} تعذّر تحديث العرض: {ex.Message}", true); }
    }

    private void Confirm(ConfirmActionViewModel vm, Action action)
    {
        if (ConfirmRequested is not null) ConfirmRequested(vm, action);
        else action();
    }

    public IEnumerable<string> CreateCsvLines()
    {
        yield return "التاريخ,الوقت,الخط,رقم الخط,المصدر,النوع,الطرف,رقم الحركة,المبلغ,العمولة,رصيد الخط بعدها,المستخدم,الحالة,ملاحظات";
        foreach (var o in FilteredOperations)
            yield return string.Join(',', new[] { o.DateDisplay, o.TimeDisplay, o.WalletAlias, o.WalletNumber, o.SourceTitle, o.OperationTitle, o.CustomerNumber, o.MovementId.ToString(CultureInfo.InvariantCulture), o.Amount.ToString("0.##", CultureInfo.InvariantCulture), o.Commission.ToString("0.##", CultureInfo.InvariantCulture), o.WalletBalanceAfter.ToString("0.##", CultureInfo.InvariantCulture), o.ActorName, o.StatusTitle, o.Note }.Select(EscapeCsv));
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private void SetStatus(string message, bool error) { IsErrorStatus = error; StatusMessage = message; }
}
