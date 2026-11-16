using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

/// <summary>One option of the «تصنيف المصروف» picker, read from dbo.ExpenseCategories.</summary>
public sealed record ExpenseCategoryOption(int Id, string Name, string Description)
{
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

/// <summary>
/// One payment line of the expense: the financial account that pays and the part it pays.
/// The lines of a single expense must add up to the expense amount.
/// </summary>
public sealed class ExpensePaymentLineViewModel : ObservableObject
{
    private readonly AddExpenseViewModel owner;
    private FinancialAccountDesignItem? selectedAccount;
    private string amountText = "";

    internal ExpensePaymentLineViewModel(AddExpenseViewModel owner, FinancialAccountDesignItem? account = null)
    {
        this.owner = owner;
        selectedAccount = account;
        RemoveCommand = new ActionCommand(() => owner.RemovePayment(this));
    }

    /// <summary>The active accounts, shared by every payment line.</summary>
    public ObservableCollection<FinancialAccountDesignItem> Accounts => owner.Accounts;

    public ActionCommand RemoveCommand { get; }

    public FinancialAccountDesignItem? SelectedAccount
    {
        get => selectedAccount;
        set
        {
            if (ReferenceEquals(selectedAccount, value)) return;
            selectedAccount = value;
            Changed();
            NotifyDerived();
            owner.OnPaymentLineChanged();
        }
    }

    public string AmountText
    {
        get => amountText;
        set
        {
            if (amountText == value) return;
            amountText = value;
            Changed();
            NotifyDerived();
            owner.OnPaymentLineChanged();
        }
    }

    /// <summary>With a single payment line the whole amount belongs to it, so the box is read-only.</summary>
    public bool IsSplitPayment => owner.Payments.Count > 1;

    public decimal AmountValue => TryParseMoney(AmountText, out var amount) ? amount : 0m;
    public string AmountDisplay => AmountValue > 0m ? PosFormatting.FormatCurrency(AmountValue) : "—";
    public bool HasSelectedAccount => SelectedAccount is not null;
    public string AccountBalanceDisplay => SelectedAccount?.BalanceDisplay ?? "—";
    public bool HasInsufficientBalance => SelectedAccount is not null && AmountValue > SelectedAccount.Balance;

    public string InsufficientBalanceText => SelectedAccount is null
        ? ""
        : $"مبلغ هذا السطر أكبر من رصيد «{SelectedAccount.Alias}» ({SelectedAccount.BalanceDisplay}).";

    internal void NotifySplitChanged() => Changed(nameof(IsSplitPayment));

    private void NotifyDerived()
    {
        Changed(nameof(AmountValue));
        Changed(nameof(AmountDisplay));
        Changed(nameof(HasSelectedAccount));
        Changed(nameof(AccountBalanceDisplay));
        Changed(nameof(HasInsufficientBalance));
        Changed(nameof(InsufficientBalanceText));
    }

    internal static bool TryParseMoney(string text, out decimal amount)
    {
        var candidate = (text ?? "").Trim().Replace('٫', '.');
        if (decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            return amount > 0m;

        if (decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
            return amount > 0m;

        amount = 0m;
        return false;
    }
}

/// <summary>
/// «اضافة مصروف»: records one expense (section + total + date + optional statement) and lets the
/// user pay it through one or more financial accounts in the same save. The section list comes
/// from dbo.ExpenseCategories and the accounts from dbo.FinancialAccounts.
/// </summary>
public sealed class AddExpenseViewModel : ObservableObject
{
    private const int MaxDescriptionLength = 1000;
    private const int MaxPayments = 8;

    private readonly ExpenseRepository? repository;
    private readonly FinancialRepository? financialRepository;

    private ExpenseCategoryOption? selectedCategory;
    private string amountText = "";
    private string descriptionText = "";
    private DateTime? expenseDate = DateTime.Today;
    private bool isBusy;
    private bool isErrorStatus;
    private bool isSyncingPayments;
    private string statusMessage;
    private string lastSavedNumber = "";
    private string lastSavedSummary = "";
    private bool hasLastSaved;
    private int designSequence = 1;

    public AddExpenseViewModel(ExpenseRepository? repository = null, FinancialRepository? financialRepository = null)
    {
        this.repository = repository;
        this.financialRepository = financialRepository;

        SaveCommand = new ActionCommand(Save);
        AddPaymentCommand = new ActionCommand(AddPayment);
        ClearFormCommand = new ActionCommand(ClearForm);
        RefreshCommand = new ActionCommand(() => RefreshFromDatabase());

        if (repository is null)
        {
            // Design preview: the same six starter sections the database seeds, plus the sample accounts.
            var id = 1;
            foreach (var section in ExpenseDesignSeedData.CreateSampleExpenseCategories())
                Categories.Add(new ExpenseCategoryOption(id++, section.Name, section.Description));
            foreach (var account in FinanceDesignSeedData.CreateAccounts().Where(a => a.IsActive))
                Accounts.Add(account);

            selectedCategory = Categories.FirstOrDefault();
            statusMessage = "وضع العرض التجريبي: لا يوجد اتصال بقاعدة البيانات، ولن يُحفظ أي مصروف فعلي. اختر تصنيفًا ووسائل الدفع وأدخل المبلغ.";
        }
        else
        {
            statusMessage = "اختر تصنيف المصروف ووسيلة دفع واحدة على الأقل، ثم أدخل المبلغ والتاريخ واضغط «حفظ المصروف».";
        }

        Payments.Add(new ExpensePaymentLineViewModel(
            this,
            Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault()));
    }

    public ObservableCollection<ExpenseCategoryOption> Categories { get; } = [];
    public ObservableCollection<FinancialAccountDesignItem> Accounts { get; } = [];

    /// <summary>The payment lines; at least one is always present.</summary>
    public ObservableCollection<ExpensePaymentLineViewModel> Payments { get; } = [];

    /// <summary>Raised after a successful save so the shell can refresh the accounts screen.</summary>
    public Action? ExpenseSaved { get; set; }

    public ExpenseCategoryOption? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (ReferenceEquals(selectedCategory, value)) return;
            selectedCategory = value;
            Changed();
            Changed(nameof(HasSelectedCategory));
            Changed(nameof(SelectedCategoryDescription));
            Changed(nameof(SelectedCategoryTitle));
        }
    }

    /// <summary>Raw text of the total-amount box; parsed with InvariantCulture then the current culture.</summary>
    public string AmountText
    {
        get => amountText;
        set
        {
            if (amountText == value) return;
            amountText = value;
            Changed();
            SyncSinglePayment();
            NotifyAmountSummaryChanged();
        }
    }

    public string DescriptionText
    {
        get => descriptionText;
        set
        {
            if (descriptionText == value) return;
            descriptionText = value;
            Changed();
            Changed(nameof(DescriptionCountDisplay));
            Changed(nameof(IsDescriptionOverLimit));
        }
    }

    public DateTime? ExpenseDate
    {
        get => expenseDate;
        set
        {
            if (expenseDate == value) return;
            expenseDate = value;
            Changed();
            Changed(nameof(ExpenseDateDisplay));
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (isBusy == value) return;
            isBusy = value;
            Changed();
            Changed(nameof(SaveButtonText));
        }
    }

    public string SaveButtonText => IsBusy ? "جارٍ الحفظ…" : "حفظ المصروف";

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value) return;
            statusMessage = value;
            Changed();
            Changed(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>True when the screen reads and writes SQL; false in the design preview.</summary>
    public bool IsDatabaseBacked => repository is not null;

    public bool IsErrorStatus
    {
        get => isErrorStatus;
        private set
        {
            if (isErrorStatus == value) return;
            isErrorStatus = value;
            Changed();
        }
    }

    // ------------------------------------------------------------ category state

    public bool HasSelectedCategory => SelectedCategory is not null;
    public string SelectedCategoryTitle => SelectedCategory?.Name ?? "لم يتم اختيار تصنيف";
    public string SelectedCategoryDescription => SelectedCategory?.Description ?? "";
    public bool HasSelectedCategoryDescription => SelectedCategory?.HasDescription == true;

    // ------------------------------------------------------------- payment state

    public int MaxPaymentMethods => MaxPayments;
    public bool CanAddPayment => Payments.Count < MaxPayments;
    public bool HasMultiplePayments => Payments.Count > 1;
    public int PaymentMethodsCount => Payments.Count;

    public string PaymentMethodsCountDisplay => PaymentMethodsCount == 1
        ? "وسيلة واحدة"
        : $"{PaymentMethodsCount} وسائل";

    public string SinglePaymentHint => Payments.Count > 1
        ? "وزّع كامل المبلغ على وسائل الدفع."
        : "وسيلة واحدة: يُخصم كامل المبلغ منها.";

    public bool HasNoAccounts => Accounts.Count == 0;

    public string EmptyAccountsText =>
        "لا توجد حسابات مالية نشطة. افتح «حساباتي» وأضف الخزنة أو المحفظة التي سيُخصم منها المصروف ثم عد إلى هذه الشاشة.";

    public decimal PaymentsAllocated => Math.Round(Payments.Sum(p => p.AmountValue), 2, MidpointRounding.AwayFromZero);
    public string PaymentsAllocatedDisplay => PosFormatting.FormatCurrency(PaymentsAllocated);

    public bool HasPaymentWithoutAccount => Payments.Any(p => p.SelectedAccount is null);
    public bool HasPaymentWithoutAmount => Payments.Any(p => p.AmountValue <= 0m);

    private bool HasTotalAmount => ExpensePaymentLineViewModel.TryParseMoney(AmountText, out _);

    public decimal AmountValue => ExpensePaymentLineViewModel.TryParseMoney(AmountText, out var amount)
        ? Math.Round(amount, 2, MidpointRounding.AwayFromZero)
        : 0m;
    public string AmountDisplay => HasTotalAmount ? PosFormatting.FormatCurrency(AmountValue) : "—";

    public decimal RemainingToAllocate => Math.Round(AmountValue - PaymentsAllocated, 2, MidpointRounding.AwayFromZero);
    public string RemainingToAllocateDisplay =>
        HasTotalAmount ? PosFormatting.FormatCurrency(RemainingToAllocate) : "—";

    /// <summary>True until every line has an account and a positive amount and the lines match the total.</summary>
    public bool HasAllocationGap =>
        Payments.Count == 0 || HasPaymentWithoutAmount || HasPaymentWithoutAccount
        || !HasTotalAmount || Math.Abs(RemainingToAllocate) > 0.005m;

    public bool IsAllocationBalanced => !HasAllocationGap;

    public string AllocationMessage
    {
        get
        {
            if (Payments.Count == 0) return "أضف وسيلة دفع واحدة على الأقل.";
            if (!HasTotalAmount) return "أدخل مبلغ المصروف أولًا ثم وزّعه على وسائل الدفع.";
            if (HasPaymentWithoutAccount) return "اختر الحساب المالي لكل وسيلة دفع.";
            if (HasPaymentWithoutAmount) return "أدخل مبلغًا صحيحًا أكبر من الصفر في كل وسيلة دفع.";
            if (RemainingToAllocate > 0m)
                return $"لم يُوزَّع بعد: {PosFormatting.FormatCurrency(RemainingToAllocate)} من مبلغ المصروف.";
            if (RemainingToAllocate < 0m)
                return $"الموزَّع أكبر من مبلغ المصروف بـ {PosFormatting.FormatCurrency(-RemainingToAllocate)}.";
            return "تم توزيع كامل المبلغ على وسائل الدفع.";
        }
    }

    public string PaymentMethodsSummary
    {
        get
        {
            var parts = Payments
                .Where(p => p.SelectedAccount is not null && p.AmountValue > 0m)
                .Select(p => $"{p.SelectedAccount!.Alias} {PosFormatting.FormatCurrency(p.AmountValue)}")
                .ToList();
            return parts.Count == 0 ? "—" : string.Join(" + ", parts);
        }
    }

    // ------------------------------------------------------------ other state

    public bool HasNoCategories => Categories.Count == 0;

    public string EmptyCategoriesText =>
        "لا توجد تصنيفات مصروفات مسجلة. افتح «تصنيفات المصروفات» وأضف قسمًا واحدًا على الأقل (مثل: إيجار) ثم عد إلى هذه الشاشة.";

    public string ExpenseDateDisplay => ExpenseDate is { } date
        ? date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
        : "—";

    public string DescriptionCountDisplay => $"{DescriptionText.Length} / {MaxDescriptionLength}";
    public bool IsDescriptionOverLimit => DescriptionText.Length > MaxDescriptionLength;

    public string TodayDisplay => DateTime.Today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------ last saved card

    public bool HasLastSaved => hasLastSaved;
    public string LastSavedNumber => lastSavedNumber;
    public string LastSavedSummary => lastSavedSummary;

    public ActionCommand SaveCommand { get; }
    public ActionCommand AddPaymentCommand { get; }
    public ActionCommand ClearFormCommand { get; }
    public ActionCommand RefreshCommand { get; }

    // --------------------------------------------------------------- data load

    /// <summary>
    /// Reloads the sections and the active accounts. Called when the screen is opened, when the
    /// categories screen reports a change and after a save (the deducted balances must be visible).
    /// </summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null && financialRepository is null) return false;

        var categoryId = SelectedCategory?.Id ?? 0;
        var accountIds = Payments.Select(p => p.SelectedAccount?.Id ?? 0).ToList();
        string? failure = null;

        if (repository is not null)
        {
            try
            {
                var rows = repository.LoadCategories();
                Categories.Clear();
                foreach (var row in rows)
                    Categories.Add(new ExpenseCategoryOption(row.Id, row.Name, row.Description));
            }
            catch (Exception ex)
            {
                failure = DescribeError(ex, "تعذر تحميل تصنيفات المصروفات من قاعدة البيانات.");
            }
        }

        if (financialRepository is not null)
        {
            try
            {
                financialRepository.LoadAccountsInto(Accounts);
            }
            catch (Exception ex)
            {
                failure ??= DescribeError(ex, "تعذّر تحميل الحسابات المالية من قاعدة البيانات.");
            }
        }

        SelectedCategory = Categories.FirstOrDefault(c => c.Id == categoryId) ?? Categories.FirstOrDefault();

        // Keep every payment line attached to the same account id when it is still active.
        for (var index = 0; index < Payments.Count; index++)
        {
            var wantedId = index < accountIds.Count ? accountIds[index] : 0;
            Payments[index].SelectedAccount =
                Accounts.FirstOrDefault(a => a.Id == wantedId) ?? Accounts.FirstOrDefault();
        }

        Changed(nameof(HasNoCategories));
        Changed(nameof(HasNoAccounts));
        NotifyPaymentsChanged();

        if (failure is not null)
        {
            SetStatus(failure, true);
            return false;
        }

        SetStatus(
            $"تم تحميل {Categories.Count} تصنيف مصروفات و{Accounts.Count} حساب مالي نشط من قاعدة البيانات.",
            false);
        return true;
    }

    /// <summary>Alias used by the shell after another screen changes the sections or the balances.</summary>
    public void NotifyExternalDataChanged() => RefreshFromDatabase();

    // ------------------------------------------------------------- payment actions

    internal void OnPaymentLineChanged()
    {
        NotifyAllocationChanged();
        Changed(nameof(PaymentMethodsSummary));
    }

    private void AddPayment()
    {
        if (!CanAddPayment)
        {
            SetStatus($"لا يمكن استخدام أكثر من {MaxPayments} وسائل دفع في المصروف الواحد.", true);
            return;
        }

        var usedAccounts = Payments
            .Where(p => p.SelectedAccount is not null)
            .Select(p => p.SelectedAccount!.Id)
            .ToHashSet();
        var freeAccount = Accounts.FirstOrDefault(a => !usedAccounts.Contains(a.Id));
        var line = new ExpensePaymentLineViewModel(this, freeAccount);

        // Going from one line to two splits the total in half so the user starts from a valid
        // allocation and only adjusts the amounts. With more lines the new one starts empty.
        if (Payments.Count == 1 && HasTotalAmount && Payments[0].AmountValue == AmountValue)
        {
            var firstHalf = Math.Round(AmountValue / 2m, 2, MidpointRounding.AwayFromZero);
            Payments[0].AmountText = FormatMoney(firstHalf);
            line.AmountText = FormatMoney(AmountValue - firstHalf);
        }

        Payments.Add(line);
        NotifyPaymentsChanged();
        SetStatus(
            Payments.Count == 2 && HasTotalAmount
                ? "قُسّم المبلغ مناصفةً بين وسيلتي الدفع مبدئيًا؛ عدّل المبالغ كما تريد."
                : "أضف مبلغ وسيلة الدفع الجديدة بحيث يساوي مجموع الوسائل مبلغ المصروف.",
            false);
    }

    internal void RemovePayment(ExpensePaymentLineViewModel line)
    {
        if (Payments.Count <= 1)
        {
            SetStatus("لا بد من وسيلة دفع واحدة على الأقل؛ لا يُحفظ مصروف بلا حساب مالي.", true);
            return;
        }

        var removedAlias = line.SelectedAccount?.Alias;
        Payments.Remove(line);
        SyncSinglePayment();
        NotifyPaymentsChanged();
        SetStatus(
            removedAlias is null
                ? "تم حذف وسيلة الدفع. مجموع الوسائل يجب أن يساوي مبلغ المصروف."
                : $"تم حذف وسيلة الدفع «{removedAlias}». راجع مجموع الوسائل مقابل مبلغ المصروف.",
            false);
    }

    private void SyncSinglePayment()
    {
        if (isSyncingPayments || Payments.Count != 1) return;

        isSyncingPayments = true;
        try
        {
            Payments[0].AmountText = HasTotalAmount ? FormatMoney(AmountValue) : "";
        }
        finally
        {
            isSyncingPayments = false;
        }
    }

    private void ResetPayments(int preferredAccountId)
    {
        Payments.Clear();
        var account = Accounts.FirstOrDefault(a => a.Id == preferredAccountId)
                      ?? Accounts.FirstOrDefault(a => a.IsDefault)
                      ?? Accounts.FirstOrDefault();
        Payments.Add(new ExpensePaymentLineViewModel(this, account));
        SyncSinglePayment();
        NotifyPaymentsChanged();
    }

    // ------------------------------------------------------------------ actions

    private void Save()
    {
        if (IsBusy) return;

        if (SelectedCategory is null)
        {
            SetStatus("اختر تصنيف المصروف أولًا من القائمة.", true);
            return;
        }

        if (!ExpensePaymentLineViewModel.TryParseMoney(AmountText, out var amount))
        {
            SetStatus("أدخل مبلغًا صحيحًا أكبر من الصفر، مثل 250.00.", true);
            return;
        }

        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        if (ExpenseDate is not { } date)
        {
            SetStatus("اختر تاريخ المصروف. التاريخ الافتراضي هو اليوم.", true);
            return;
        }

        if (date.Date > DateTime.Today)
        {
            SetStatus("لا يمكن تسجيل مصروف بتاريخ مستقبلي. اختر تاريخ اليوم أو تاريخًا سابقًا.", true);
            return;
        }

        if (Payments.Count == 0)
        {
            SetStatus("أضف وسيلة دفع واحدة على الأقل؛ لا يُحفظ مصروف بلا حساب مالي.", true);
            return;
        }

        foreach (var line in Payments)
        {
            if (line.SelectedAccount is null)
            {
                SetStatus("اختر الحساب المالي لكل وسيلة دفع.", true);
                return;
            }

            if (!ExpensePaymentLineViewModel.TryParseMoney(line.AmountText, out var part))
            {
                SetStatus("أدخل مبلغًا صحيحًا أكبر من الصفر في كل وسيلة دفع.", true);
                return;
            }

            if (part > line.SelectedAccount.Balance)
            {
                SetStatus(
                    $"المبلغ المخصص لحساب «{line.SelectedAccount.Alias}» ({PosFormatting.FormatCurrency(part)}) " +
                    $"أكبر من رصيده المتاح ({line.SelectedAccount.BalanceDisplay}).",
                    true);
                return;
            }
        }

        if (Payments.Select(p => p.SelectedAccount!.Id).Distinct().Count() != Payments.Count)
        {
            SetStatus("لا يمكن استخدام الحساب المالي نفسه في أكثر من وسيلة دفع. اجمع مبلغه في سطر واحد.", true);
            return;
        }

        if (Math.Abs(RemainingToAllocate) > 0.005m)
        {
            SetStatus($"{AllocationMessage} لا يمكن الحفظ قبل توزيع كامل المبلغ على وسائل الدفع.", true);
            return;
        }

        var description = DescriptionText.Trim();
        if (description.Length > MaxDescriptionLength)
        {
            SetStatus($"البيان أطول من {MaxDescriptionLength} حرف. اختصره ثم أعد المحاولة.", true);
            return;
        }

        var requests = Payments
            .Select(p => new ExpensePaymentRequest(p.SelectedAccount!.Id, p.AmountValue))
            .ToList();
        var firstAccountId = requests[0].FinancialAccountId;

        if (repository is not null)
        {
            IsBusy = true;
            try
            {
                var saved = repository.Save(new SaveExpenseRequest(
                    SelectedCategory.Id,
                    date.Date,
                    amount,
                    description,
                    requests));

                var methods = string.Join(
                    " + ",
                    saved.Payments.Select(p => $"{p.AccountAlias} {PosFormatting.FormatCurrency(p.Amount)}"));
                RegisterSaved(saved.ExpenseNumber, amount, methods);
                RefreshAccountsAfterSave(saved.Payments.ToDictionary(p => p.FinancialAccountId, p => p.BalanceAfter));
                ResetPayments(saved.Payments[0].FinancialAccountId);
                SetStatus(
                    $"تم حفظ المصروف {saved.ExpenseNumber} بقيمة {PosFormatting.FormatCurrency(amount)} " +
                    $"على {saved.Payments.Count} وسيلة دفع، وتحديث أرصدة الحسابات. يمكنك تسجيل مصروف آخر.",
                    false);
                ExpenseSaved?.Invoke();
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر حفظ المصروف في قاعدة البيانات."), true);
            }
            finally
            {
                IsBusy = false;
            }

            return;
        }

        // Design preview: no SQL. Every payment line is deducted from its in-memory account.
        var number = $"EXP-{DateTime.Now.Year}-{designSequence++:0000}";
        var designMethods = new List<string>(Payments.Count);
        foreach (var line in Payments)
        {
            line.SelectedAccount!.Balance -= line.AmountValue;
            designMethods.Add($"{line.SelectedAccount.Alias} {PosFormatting.FormatCurrency(line.AmountValue)}");
        }

        var designSummary = string.Join(" + ", designMethods);
        RegisterSaved(number, amount, designSummary);
        ResetPayments(firstAccountId);
        SetStatus(
            $"تم تسجيل المصروف {number} في وضع العرض التجريبي بقيمة {PosFormatting.FormatCurrency(amount)} " +
            $"على {designMethods.Count} وسيلة دفع. لم يُحفظ شيء في قاعدة البيانات.",
            false);
        NotifyAccountStateChanged();
    }

    private void ClearForm()
    {
        AmountText = "";
        DescriptionText = "";
        ExpenseDate = DateTime.Today;
        SetStatus("تم تفريغ المبلغ والبيان والعودة إلى تاريخ اليوم. التصنيف ووسائل الدفع المختارة باقية.", false);
    }

    private void RegisterSaved(string number, decimal amount, string paymentSummary)
    {
        lastSavedNumber = number;
        lastSavedSummary = $"{PosFormatting.FormatCurrency(amount)} — {paymentSummary}";
        hasLastSaved = true;
        Changed(nameof(HasLastSaved));
        Changed(nameof(LastSavedNumber));
        Changed(nameof(LastSavedSummary));

        // Keep the date, section and payment methods so the next expense is one short entry away.
        AmountText = "";
        DescriptionText = "";
    }

    private void RefreshAccountsAfterSave(IReadOnlyDictionary<int, decimal> balancesAfter)
    {
        if (financialRepository is not null)
        {
            try
            {
                financialRepository.LoadAccountsInto(Accounts);
            }
            catch (Exception)
            {
                // The expense is already committed; the summary panel keeps the balances returned
                // by the save call and the next manual refresh retries the account list.
            }
        }

        foreach (var account in Accounts)
            if (balancesAfter.TryGetValue(account.Id, out var balance))
                account.Balance = balance;

        Changed(nameof(HasNoAccounts));
        NotifyAccountStateChanged();
    }

    // ------------------------------------------------------------------ helpers

    internal void NotifyPaymentsChanged()
    {
        foreach (var line in Payments)
            line.NotifySplitChanged();

        Changed(nameof(CanAddPayment));
        Changed(nameof(PaymentMethodsCountDisplay));
        Changed(nameof(HasMultiplePayments));
        Changed(nameof(SinglePaymentHint));
        Changed(nameof(PaymentMethodsSummary));
        NotifyAllocationChanged();
    }

    private void NotifyAllocationChanged()
    {
        Changed(nameof(PaymentsAllocated));
        Changed(nameof(PaymentsAllocatedDisplay));
        Changed(nameof(RemainingToAllocate));
        Changed(nameof(RemainingToAllocateDisplay));
        Changed(nameof(HasAllocationGap));
        Changed(nameof(IsAllocationBalanced));
        Changed(nameof(AllocationMessage));
        Changed(nameof(HasPaymentWithoutAccount));
        Changed(nameof(HasPaymentWithoutAmount));
    }

    private void NotifyAmountSummaryChanged()
    {
        Changed(nameof(AmountValue));
        Changed(nameof(AmountDisplay));
        Changed(nameof(HasAllocationGap));
        Changed(nameof(IsAllocationBalanced));
        Changed(nameof(AllocationMessage));
        Changed(nameof(RemainingToAllocate));
        Changed(nameof(RemainingToAllocateDisplay));
    }

    private void NotifyAccountStateChanged()
    {
        Changed(nameof(HasNoAccounts));
        foreach (var line in Payments)
            line.NotifySplitChanged();
        NotifyAllocationChanged();
    }

    private static string FormatMoney(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string DescribeError(Exception exception, string fallback)
        => exception switch
        {
            ExpenseDataException data => data.Message,
            FinancialDataException finance => finance.Message,
            _ => fallback
        };

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
