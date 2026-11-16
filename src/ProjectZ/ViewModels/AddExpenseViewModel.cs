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
/// «اضافة مصروف»: records one expense (section + amount + date + optional statement) and
/// deducts the amount from the financial account the user picks. The section list comes from
/// dbo.ExpenseCategories, so the categories screen stays the single source of truth.
/// </summary>
public sealed class AddExpenseViewModel : ObservableObject
{
    private const int MaxDescriptionLength = 1000;

    private readonly ExpenseRepository? repository;
    private readonly FinancialRepository? financialRepository;

    private ExpenseCategoryOption? selectedCategory;
    private FinancialAccountDesignItem? selectedAccount;
    private string amountText = "";
    private string descriptionText = "";
    private DateTime? expenseDate = DateTime.Today;
    private bool isBusy;
    private bool isErrorStatus;
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
            selectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
            statusMessage = "وضع العرض التجريبي: لا يوجد اتصال بقاعدة البيانات، ولن يُحفظ أي مصروف فعلي. اختر تصنيفًا وحسابًا وأدخل المبلغ.";
        }
        else
        {
            statusMessage = "اختر تصنيف المصروف والحساب المالي، ثم أدخل المبلغ والتاريخ واضغط «حفظ المصروف».";
        }
    }

    public ObservableCollection<ExpenseCategoryOption> Categories { get; } = [];
    public ObservableCollection<FinancialAccountDesignItem> Accounts { get; } = [];

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

    public FinancialAccountDesignItem? SelectedAccount
    {
        get => selectedAccount;
        set
        {
            if (ReferenceEquals(selectedAccount, value)) return;
            selectedAccount = value;
            Changed();
            NotifyAccountSummaryChanged();
        }
    }

    /// <summary>Raw text of the amount box; parsed with InvariantCulture then the current culture.</summary>
    public string AmountText
    {
        get => amountText;
        set
        {
            if (amountText == value) return;
            amountText = value;
            Changed();
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

    // ------------------------------------------------------------ derived state

    public bool HasSelectedCategory => SelectedCategory is not null;
    public string SelectedCategoryTitle => SelectedCategory?.Name ?? "لم يتم اختيار تصنيف";
    public string SelectedCategoryDescription => SelectedCategory?.Description ?? "";
    public bool HasSelectedCategoryDescription => SelectedCategory?.HasDescription == true;

    public bool HasSelectedAccount => SelectedAccount is not null;
    public string SelectedAccountAlias => SelectedAccount?.Alias ?? "لم يتم اختيار حساب";
    public string SelectedAccountProviderTitle => SelectedAccount?.ProviderTitle ?? "";
    public string SelectedAccountBalanceDisplay => SelectedAccount?.BalanceDisplay ?? "—";

    public bool HasNoCategories => Categories.Count == 0;
    public bool HasNoAccounts => Accounts.Count == 0;
    public string EmptyCategoriesText =>
        "لا توجد تصنيفات مصروفات مسجلة. افتح «تصنيفات المصروفات» وأضف قسمًا واحدًا على الأقل (مثل: إيجار) ثم عد إلى هذه الشاشة.";
    public string EmptyAccountsText =>
        "لا توجد حسابات مالية نشطة. افتح «حساباتي» وأضف الخزنة أو المحفظة التي سيُخصم منها المصروف ثم عد إلى هذه الشاشة.";

    public string AmountDisplay => TryParseAmount(AmountText, out var amount)
        ? PosFormatting.FormatCurrency(amount)
        : "—";

    public string RemainingAfterSaveDisplay => SelectedAccount is not null && TryParseAmount(AmountText, out var amount)
        ? PosFormatting.FormatCurrency(SelectedAccount.Balance - amount)
        : "—";

    /// <summary>Inline warning before saving: the selected account cannot cover the amount.</summary>
    public bool HasInsufficientBalance =>
        SelectedAccount is not null && TryParseAmount(AmountText, out var amount) && amount > SelectedAccount.Balance;

    public string InsufficientBalanceText => SelectedAccount is null
        ? ""
        : $"المبلغ أكبر من رصيد «{SelectedAccount.Alias}» المتاح ({SelectedAccount.BalanceDisplay}). " +
          "خفّض المبلغ أو اختر حسابًا آخر قبل الحفظ.";

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
    public ActionCommand ClearFormCommand { get; }
    public ActionCommand RefreshCommand { get; }

    // --------------------------------------------------------------- data load

    /// <summary>
    /// Reloads the sections and the active accounts. Called when the screen is opened, when the
    /// categories screen reports a change and after a save (the deducted balance must be visible).
    /// </summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null && financialRepository is null) return false;

        var categoryId = SelectedCategory?.Id ?? 0;
        var accountId = SelectedAccount?.Id ?? 0;
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
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == accountId) ?? Accounts.FirstOrDefault();

        Changed(nameof(HasNoCategories));
        Changed(nameof(HasNoAccounts));

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

    // ------------------------------------------------------------------ actions

    private void Save()
    {
        if (IsBusy) return;

        if (SelectedCategory is null)
        {
            SetStatus("اختر تصنيف المصروف أولًا من القائمة.", true);
            return;
        }

        if (!TryParseAmount(AmountText, out var amount))
        {
            SetStatus("أدخل مبلغًا صحيحًا أكبر من الصفر، مثل 250.00.", true);
            return;
        }

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

        if (SelectedAccount is null)
        {
            SetStatus("اختر الحساب المالي الذي سيُخصم منه المصروف.", true);
            return;
        }

        if (amount > SelectedAccount.Balance)
        {
            SetStatus(InsufficientBalanceText, true);
            return;
        }

        var description = DescriptionText.Trim();
        if (description.Length > MaxDescriptionLength)
        {
            SetStatus($"البيان أطول من {MaxDescriptionLength} حرف. اختصره ثم أعد المحاولة.", true);
            return;
        }

        var accountAlias = SelectedAccount.Alias;

        if (repository is not null)
        {
            IsBusy = true;
            try
            {
                var saved = repository.Save(new SaveExpenseRequest(
                    SelectedCategory.Id,
                    SelectedAccount.Id,
                    date.Date,
                    amount,
                    description));

                RegisterSaved(saved.ExpenseNumber, amount, accountAlias, saved.BalanceAfter);
                RefreshAccountsAfterSave(saved.FinancialAccountId);
                SetStatus(
                    $"تم حفظ المصروف {saved.ExpenseNumber} بقيمة {PosFormatting.FormatCurrency(amount)} " +
                    $"من «{accountAlias}» وتحديث الرصيد. يمكنك تسجيل مصروف آخر.",
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

        // Design preview: no SQL. The amount is deducted from the in-memory account so the
        // summary panel behaves exactly like the real screen.
        var number = $"EXP-{DateTime.Now.Year}-{designSequence++:0000}";
        SelectedAccount.Balance -= amount;
        var balanceAfter = SelectedAccount.Balance;
        RegisterSaved(number, amount, accountAlias, balanceAfter);
        SetStatus(
            $"تم تسجيل المصروف {number} في وضع العرض التجريبي بقيمة {PosFormatting.FormatCurrency(amount)} " +
            $"من «{accountAlias}». لم يُحفظ شيء في قاعدة البيانات.",
            false);
        NotifyAccountSummaryChanged();
    }

    private void ClearForm()
    {
        AmountText = "";
        DescriptionText = "";
        ExpenseDate = DateTime.Today;
        SetStatus("تم تفريغ المبلغ والبيان والعودة إلى تاريخ اليوم. التصنيف والحساب المختاران باقيان.", false);
    }

    private void RegisterSaved(string number, decimal amount, string accountAlias, decimal balanceAfter)
    {
        lastSavedNumber = number;
        lastSavedSummary =
            $"{PosFormatting.FormatCurrency(amount)} — تم الخصم من «{accountAlias}» — الرصيد المتبقي " +
            PosFormatting.FormatCurrency(balanceAfter);
        hasLastSaved = true;
        Changed(nameof(HasLastSaved));
        Changed(nameof(LastSavedNumber));
        Changed(nameof(LastSavedSummary));

        // Keep the date, section and account so the next expense is one short entry away.
        AmountText = "";
        DescriptionText = "";
    }

    private void RefreshAccountsAfterSave(int accountId)
    {
        if (financialRepository is null) return;

        try
        {
            financialRepository.LoadAccountsInto(Accounts);
        }
        catch (Exception)
        {
            // The expense is already committed; the summary panel keeps the balance returned by
            // the save call and the next manual refresh retries the account list.
            NotifyAccountSummaryChanged();
            return;
        }

        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == accountId) ?? SelectedAccount;
        Changed(nameof(HasNoAccounts));
        NotifyAccountSummaryChanged();
    }

    // ------------------------------------------------------------------ helpers

    private static bool TryParseAmount(string text, out decimal amount)
    {
        var candidate = (text ?? "").Trim().Replace('٫', '.');
        if (decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            return amount > 0m;

        if (decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
            return amount > 0m;

        amount = 0m;
        return false;
    }

    private void NotifyAmountSummaryChanged()
    {
        Changed(nameof(AmountDisplay));
        Changed(nameof(RemainingAfterSaveDisplay));
        Changed(nameof(HasInsufficientBalance));
        Changed(nameof(InsufficientBalanceText));
    }

    private void NotifyAccountSummaryChanged()
    {
        Changed(nameof(HasSelectedAccount));
        Changed(nameof(SelectedAccountAlias));
        Changed(nameof(SelectedAccountProviderTitle));
        Changed(nameof(SelectedAccountBalanceDisplay));
        Changed(nameof(RemainingAfterSaveDisplay));
        Changed(nameof(HasInsufficientBalance));
        Changed(nameof(InsufficientBalanceText));
    }

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
