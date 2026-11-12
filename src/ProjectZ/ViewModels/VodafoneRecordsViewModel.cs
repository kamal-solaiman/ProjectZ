using System.Collections.ObjectModel;
using System.Globalization;
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
    private DateTime? fromDate;
    private DateTime? toDate;
    private int selectedWalletId;
    private string selectedKind = "كل العمليات";
    private string selectedStatus = "كل الحالات";
    private string customerFilter = "";
    private string referenceFilter = "";
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
    private string editReference = "";
    private string editNote = "";

    public ObservableCollection<VodafoneTransferOperation> FilteredOperations { get; } = [];
    public ObservableCollection<VodafoneWalletFilterOption> WalletFilters { get; } = [];
    public ObservableCollection<FinancialAccountDesignItem> EditWallets { get; } = [];
    public IReadOnlyList<string> KindFilters { get; } = ["كل العمليات", "إيداع للعميل", "سحب للعميل"];
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
    public string ReferenceFilter { get => referenceFilter; set { referenceFilter = value; Changed(); ApplyFilters(); } }
    public string UserFilter { get => userFilter; set { userFilter = value; Changed(); ApplyFilters(); } }
    public string MinimumAmountText { get => minimumAmountText; set { minimumAmountText = value; Changed(); ApplyFilters(); } }
    public string MaximumAmountText { get => maximumAmountText; set { maximumAmountText = value; Changed(); ApplyFilters(); } }
    public string MinimumCommissionText { get => minimumCommissionText; set { minimumCommissionText = value; Changed(); ApplyFilters(); } }
    public string MaximumCommissionText { get => maximumCommissionText; set { maximumCommissionText = value; Changed(); ApplyFilters(); } }
    public bool HasNoOperations => FilteredOperations.Count == 0;
    public string ResultCountDisplay => $"{FilteredOperations.Count} من {transfers.Operations.Count} عملية";
    public string FilteredAmountDisplay => PosFormatting.FormatCurrency(FilteredOperations.Sum(o => o.Amount));
    public string FilteredCommissionDisplay => PosFormatting.FormatCurrency(FilteredOperations.Sum(o => o.Commission));
    public string StatusMessage { get => statusMessage; private set { statusMessage = value; Changed(); } }
    public bool IsErrorStatus { get => isErrorStatus; private set { isErrorStatus = value; Changed(); } }

    public FinancialAccountDesignItem? EditWallet { get => editWallet; set { editWallet = value; Changed(); } }
    public string EditKind { get => editKind; set { editKind = value; Changed(); } }
    public string EditCustomerNumber { get => editCustomerNumber; set { editCustomerNumber = value; Changed(); } }
    public string EditAmountText { get => editAmountText; set { editAmountText = value; Changed(); } }
    public string EditCommissionText { get => editCommissionText; set { editCommissionText = value; Changed(); } }
    public string EditReference { get => editReference; set { editReference = value; Changed(); } }
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
        ReverseOperationCommand = new RelayCommand<VodafoneTransferOperation>(operation => { if (operation is not null) { SelectedOperation = operation; RequestReverse(); } });
        DeleteOperationCommand = new RelayCommand<VodafoneTransferOperation>(operation => { if (operation is not null) { SelectedOperation = operation; RequestDelete(); } });
        NotifyExternalDataChanged();
    }

    public void NotifyExternalDataChanged()
    {
        transfers.NotifyExternalDataChanged();
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

    public void ApplyFilters()
    {
        FilteredOperations.Clear();
        foreach (var operation in transfers.Operations.Where(Matches).OrderByDescending(o => o.Timestamp).ThenByDescending(o => o.Id)) FilteredOperations.Add(operation);
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
        if (!Contains(operation.CustomerNumber, CustomerFilter) || !Contains(operation.Reference, ReferenceFilter) || !Contains(operation.ActorName, UserFilter)) return false;
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
        customerFilter = ""; referenceFilter = ""; userFilter = ""; minimumAmountText = ""; maximumAmountText = ""; minimumCommissionText = ""; maximumCommissionText = "";
        Changed(nameof(FromDate)); Changed(nameof(ToDate)); Changed(nameof(SelectedWalletId)); Changed(nameof(SelectedKind)); Changed(nameof(SelectedStatus));
        Changed(nameof(CustomerFilter)); Changed(nameof(ReferenceFilter)); Changed(nameof(UserFilter)); Changed(nameof(MinimumAmountText)); Changed(nameof(MaximumAmountText)); Changed(nameof(MinimumCommissionText)); Changed(nameof(MaximumCommissionText));
        ApplyFilters(); SetStatus("تم إلغاء جميع الفلاتر.", false);
    }

    private void BeginEdit()
    {
        if (SelectedOperation is null) { SetStatus("اختر عملية أولًا.", true); return; }
        if (SelectedOperation.IsReversed) { SetStatus("لا يمكن تعديل عملية معكوسة.", true); return; }
        EditWallet = EditWallets.FirstOrDefault(w => w.Id == SelectedOperation.WalletId);
        EditKind = SelectedOperation.OperationTitle; EditCustomerNumber = SelectedOperation.CustomerNumber;
        EditAmountText = SelectedOperation.Amount.ToString("0.##", CultureInfo.InvariantCulture); EditCommissionText = SelectedOperation.Commission.ToString("0.##", CultureInfo.InvariantCulture);
        EditReference = SelectedOperation.Reference; EditNote = SelectedOperation.Note;
        EditRequested?.Invoke();
    }

    public bool ApplyEdit()
    {
        if (SelectedOperation is null || SelectedOperation.IsReversed || EditWallet is null) { SetStatus("اختر عملية وخط فودافون صالحين.", true); return false; }
        var customer = new string(EditCustomerNumber.Where(char.IsDigit).ToArray());
        if (customer.Length != 11 || !customer.StartsWith("01", StringComparison.Ordinal)) { SetStatus("رقم العميل يجب أن يتكون من 11 رقمًا ويبدأ بـ 01.", true); return false; }
        if (!AccountEditorViewModel.TryMoney(EditAmountText, out var amount) || amount <= 0m || !AccountEditorViewModel.TryMoney(EditCommissionText, out var commission) || commission < 0m) { SetStatus("أدخل مبلغًا أكبر من صفر وعمولة صحيحة.", true); return false; }
        if (string.IsNullOrWhiteSpace(EditReference)) { SetStatus("المرجع مطلوب.", true); return false; }
        var operation = SelectedOperation;
        var oldWallet = accounts.Accounts.First(w => w.Id == operation.WalletId);
        var oldDeposit = operation.IsDeposit; var oldAmount = operation.Amount; var oldCommission = operation.Commission;
        var newDeposit = EditKind == "إيداع للعميل"; var correctionReference = $"EDT-{operation.Id}-{DateTime.Now:HHmmss}";
        if (!CanUndo(operation, out var undoError)) { SetStatus(undoError, true); return false; }
        ApplyEffect(oldWallet, operation.CustomerNumber, oldAmount, oldCommission, oldDeposit, true, correctionReference, "عكس قبل تعديل");
        var validationError = ValidateNewEffect(EditWallet, newDeposit, amount, commission);
        if (validationError is not null)
        {
            ApplyEffect(oldWallet, operation.CustomerNumber, oldAmount, oldCommission, oldDeposit, false, correctionReference, "استرجاع بعد فشل التعديل");
            FinalizeFinancialChanges(); SetStatus(validationError, true); return false;
        }
        ApplyEffect(EditWallet, customer, amount, commission, newDeposit, false, correctionReference, "تطبيق تعديل السجل");
        operation.WalletId = EditWallet.Id; operation.WalletAlias = EditWallet.Alias; operation.WalletNumber = EditWallet.Identifier;
        operation.Kind = newDeposit ? VodafoneOperationKind.DepositToCustomer : VodafoneOperationKind.WithdrawForCustomer;
        operation.CustomerNumber = customer; operation.Amount = amount; operation.Commission = commission;
        operation.CashDelta = newDeposit ? amount + commission : commission - amount; operation.WalletBalanceAfter = EditWallet.Balance;
        operation.Reference = EditReference.Trim(); operation.Note = EditNote.Trim();
        FinalizeFinancialChanges(); ApplyFilters(); SetStatus($"تم تعديل العملية {operation.Reference} وتسوية الأرصدة.", false); return true;
    }

    private string? ValidateNewEffect(FinancialAccountDesignItem wallet, bool deposit, decimal amount, decimal commission)
    {
        if (!wallet.IsActive) return "خط فودافون المحدد موقوف.";
        if (deposit && wallet.Balance < amount) return "رصيد الخط لا يكفي لمبلغ الإيداع بعد التعديل.";
        var cashDelta = deposit ? amount + commission : commission - amount;
        if (cashDelta < 0m && accounts.CashAccount.Balance < -cashDelta) return "النقدي لا يكفي لصافي العملية بعد التعديل.";
        return AccountTransactionViewModel.ValidateLimits(wallet, !deposit, amount);
    }

    private bool CanUndo(VodafoneTransferOperation operation, out string error)
    {
        var wallet = accounts.Accounts.First(w => w.Id == operation.WalletId);
        if (!operation.IsDeposit && wallet.Balance < operation.Amount) { error = "رصيد الخط الحالي لا يسمح بعكس هذه العملية."; return false; }
        if (operation.CashDelta > 0m && accounts.CashAccount.Balance < operation.CashDelta) { error = "الرصيد النقدي الحالي لا يسمح بعكس هذه العملية."; return false; }
        error = ""; return true;
    }

    private void RequestReverse()
    {
        if (SelectedOperation is null) { SetStatus("اختر عملية أولًا.", true); return; }
        if (SelectedOperation.IsReversed) { SetStatus("هذه العملية معكوسة بالفعل.", true); return; }
        var operation = SelectedOperation;
        Confirm(new ConfirmActionViewModel("عكس عملية فودافون", operation.Reference, "سيُنشأ أثر مالي عكسي مع الاحتفاظ بالسجل الأصلي وتحديده كعملية معكوسة.", "تأكيد العكس"), () =>
        {
            if (!CanUndo(operation, out var error)) { SetStatus(error, true); return; }
            var wallet = accounts.Accounts.First(w => w.Id == operation.WalletId);
            operation.ReversalReference = $"RVS-{operation.Id}-{DateTime.Now:HHmmss}";
            ApplyEffect(wallet, operation.CustomerNumber, operation.Amount, operation.Commission, operation.IsDeposit, true, operation.ReversalReference, "عكس عملية فودافون");
            operation.IsReversed = true; operation.ReversedBy = currentUserName; operation.ReversedAt = DateTime.Now;
            FinalizeFinancialChanges(); ApplyFilters(); SetStatus($"تم عكس العملية {operation.Reference}. مرجع العكس: {operation.ReversalReference}", false);
        });
    }

    private void RequestDelete()
    {
        if (SelectedOperation is null) { SetStatus("اختر عملية أولًا.", true); return; }
        var operation = SelectedOperation;
        Confirm(new ConfirmActionViewModel("حذف سجل فودافون", operation.Reference, "سيُحذف السجل من قائمة عمليات فودافون، وتُسوّى آثاره المالية غير المعكوسة بحركة مراجعة في حساباتي.", "حذف السجل"), () =>
        {
            if (!operation.IsReversed)
            {
                if (!CanUndo(operation, out var error)) { SetStatus(error, true); return; }
                var wallet = accounts.Accounts.First(w => w.Id == operation.WalletId);
                ApplyEffect(wallet, operation.CustomerNumber, operation.Amount, operation.Commission, operation.IsDeposit, true, $"DEL-{operation.Id}-{DateTime.Now:HHmmss}", "تسوية حذف سجل فودافون");
            }
            transfers.Operations.Remove(operation); SelectedOperation = null;
            FinalizeFinancialChanges(); ApplyFilters(); SetStatus($"تم حذف السجل {operation.Reference} مع الاحتفاظ بأثر المراجعة المالي.", false);
        });
    }

    private void ApplyEffect(FinancialAccountDesignItem wallet, string customer, decimal amount, decimal commission, bool deposit, bool reverse, string operationReference, string category)
    {
        var walletIncoming = deposit ? reverse : !reverse;
        wallet.Balance += walletIncoming ? amount : -amount;
        AddLedger(wallet, walletIncoming, $"{category} — الخط", amount, customer, operationReference, true);
        if (deposit)
        {
            var incoming = !reverse;
            accounts.CashAccount.Balance += incoming ? amount + commission : -(amount + commission);
            AddLedger(accounts.CashAccount, incoming, $"{category} — النقدي والعمولة", amount + commission, customer, operationReference, false);
        }
        else
        {
            var cashOutgoing = !reverse;
            accounts.CashAccount.Balance += cashOutgoing ? -amount : amount;
            AddLedger(accounts.CashAccount, !cashOutgoing, $"{category} — مبلغ النقدي", amount, customer, operationReference, false);
            if (commission > 0m)
            {
                accounts.CashAccount.Balance += cashOutgoing ? commission : -commission;
                AddLedger(accounts.CashAccount, cashOutgoing, $"{category} — العمولة", commission, customer, operationReference, false);
            }
        }
    }

    private void AddLedger(FinancialAccountDesignItem account, bool incoming, string category, decimal amount, string customer, string operationReference, bool counts)
    {
        var id = accounts.Transactions.Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
        accounts.Transactions.Insert(0, new FinancialTransactionDesignItem { Id = id, AccountId = account.Id, Timestamp = DateTime.Now, IsIncoming = incoming, Category = category, Amount = amount, BalanceAfter = account.Balance, Counterparty = customer, Reference = operationReference, Note = "مراجعة من شاشة سجلات فودافون كاش", ActorName = currentUserName, CountsTowardLimits = counts });
    }

    private void FinalizeFinancialChanges()
    {
        accounts.NotifyExternalDataChanged(); transfers.NotifyExternalDataChanged();
    }

    private void Confirm(ConfirmActionViewModel vm, Action action)
    {
        if (ConfirmRequested is not null) ConfirmRequested(vm, action);
        else action();
    }

    public IEnumerable<string> CreateCsvLines()
    {
        yield return "التاريخ,الوقت,الخط,رقم الخط,النوع,رقم العميل,المبلغ,العمولة,رصيد الخط بعدها,المستخدم,المرجع,الحالة,مرجع العكس,ملاحظات";
        foreach (var o in FilteredOperations)
            yield return string.Join(',', new[] { o.DateDisplay, o.TimeDisplay, o.WalletAlias, o.WalletNumber, o.OperationTitle, o.CustomerNumber, o.Amount.ToString("0.##", CultureInfo.InvariantCulture), o.Commission.ToString("0.##", CultureInfo.InvariantCulture), o.WalletBalanceAfter.ToString("0.##", CultureInfo.InvariantCulture), o.ActorName, o.Reference, o.StatusTitle, o.ReversalReference, o.Note }.Select(EscapeCsv));
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private void SetStatus(string message, bool error) { IsErrorStatus = error; StatusMessage = message; }
}
