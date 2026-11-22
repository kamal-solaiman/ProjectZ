using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

/// <summary>One account share of a listed expense, as the row tooltip and the details window show it.</summary>
public sealed class ExpenseInvoicePaymentLineViewModel
{
    public ExpenseInvoicePaymentLineViewModel(int financialAccountId, string accountAlias, decimal amount)
    {
        FinancialAccountId = financialAccountId;
        AccountAlias = string.IsNullOrWhiteSpace(accountAlias) ? "حساب محذوف" : accountAlias;
        Amount = amount;
    }

    public int FinancialAccountId { get; }
    public string AccountAlias { get; }
    public decimal Amount { get; }
    public string AmountDisplay => PosFormatting.FormatCurrency(Amount);
    public string ShareDisplay => $"{AmountDisplay} من {AccountAlias}";
    public string Label => $"{AccountAlias} · {AmountDisplay}";
}

/// <summary>
/// One row of the «فواتير المصروفات» table: the stored expense plus everything the filters,
/// the totals and the details window need, already formatted for RTL display.
/// </summary>
public sealed class ExpenseInvoiceListItemViewModel : ObservableObject
{
    private bool isSelected;

    public ExpenseInvoiceListItemViewModel(ExpenseInvoiceRecord record)
    {
        Id = record.Id;
        ExpenseNumber = record.ExpenseNumber;
        ExpenseDate = record.ExpenseDate.Date;
        Amount = record.Amount;
        Description = record.Description;
        ExpenseCategoryId = record.ExpenseCategoryId;
        CategoryName = record.CategoryName;
        ActorUserName = record.ActorUserName;
        CreatedAtUtc = record.CreatedAtUtc;
        Payments = record.Payments
            .Select(payment => new ExpenseInvoicePaymentLineViewModel(
                payment.FinancialAccountId, payment.AccountAlias, payment.Amount))
            .ToList();
    }

    public ExpenseInvoiceListItemViewModel(
        string expenseNumber, DateTime expenseDate, string categoryName, decimal amount, string description,
        string actorUserName, DateTime createdAtUtc, IEnumerable<ExpenseInvoicePaymentLineViewModel> payments)
    {
        Id = 0;
        ExpenseNumber = expenseNumber;
        ExpenseDate = expenseDate.Date;
        Amount = amount;
        Description = description;
        CategoryName = categoryName;
        ActorUserName = actorUserName;
        CreatedAtUtc = createdAtUtc;
        Payments = payments.ToList();
    }

    public int Id { get; }
    public string ExpenseNumber { get; }
    public DateTime ExpenseDate { get; }
    public decimal Amount { get; }
    public string Description { get; }
    public int ExpenseCategoryId { get; }
    public string CategoryName { get; }
    public string ActorUserName { get; }
    public DateTime CreatedAtUtc { get; }
    public IReadOnlyList<ExpenseInvoicePaymentLineViewModel> Payments { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            Changed();
        }
    }

    public string DateDisplay => ExpenseDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
    public string CreatedAtDisplay => CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string AmountDisplay => PosFormatting.FormatCurrency(Amount);
    public decimal AmountPlain => Math.Round(Amount, 2, MidpointRounding.AwayFromZero);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string DescriptionDisplay => HasDescription ? Description : "بدون بيان";

    public int PaymentMethodsCount => Payments.Count;
    public bool IsSplitPayment => Payments.Count > 1;
    public decimal PaymentsTotal => Payments.Sum(payment => payment.Amount);
    public bool HasPaymentBalanceGap => Math.Abs(PaymentsTotal - Amount) > 0.005m;

    public string PaymentMethodsDisplay => Payments.Count switch
    {
        0 => "بدون وسيلة دفع",
        1 => "حساب واحد",
        _ => $"مقسّم على {Payments.Count} حسابات"
    };

    /// <summary>«120.00 من الخزينة · 180.00 من فودافون كاش» — used in the tooltip and the details window.</summary>
    public string PaymentBreakdown => Payments.Count == 0
        ? "لا توجد وسائل دفع مسجلة لهذا المصروف."
        : string.Join(" · ", Payments.Select(payment => payment.ShareDisplay));

    public IReadOnlyList<string> AccountAliases => Payments.Select(payment => payment.AccountAlias).ToList();

    /// <summary>True when the free-text search term matches the number, the description, the section, the actor or an account.</summary>
    public bool Matches(string term) =>
        ExpenseNumber.Contains(term, StringComparison.CurrentCultureIgnoreCase)
        || Description.Contains(term, StringComparison.CurrentCultureIgnoreCase)
        || CategoryName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
        || ActorUserName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
        || Payments.Any(payment => payment.AccountAlias.Contains(term, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>
/// «فواتير المصروفات»: the full register of the expenses saved from «اضافة مصروف», read from
/// dbo.Expenses + dbo.ExpensePayments. The screen is a review surface: date, section, account,
/// user, amount and text filters, sorting, totals, CSV export, printing and a details window.
/// </summary>
public sealed class ExpenseInvoicesViewModel : ObservableObject
{
    public const string AllCategories = "كل التصنيفات";
    public const string AllAccounts = "كل الحسابات";
    public const string AllUsers = "كل المستخدمين";

    public const string AllPayments = "كل المصروفات";
    public const string SinglePayment = "وسيلة دفع واحدة";
    public const string SplitPayment = "أكثر من وسيلة دفع";

    public const string AllDatesPreset = "الكل";
    public const string TodayPreset = "اليوم";
    public const string Last7DaysPreset = "آخر 7 أيام";
    public const string ThisMonthPreset = "هذا الشهر";
    public const string LastMonthPreset = "الشهر الماضي";
    public const string CustomDatesPreset = "فترة مخصصة";

    public const string NewestFirstSort = "الأحدث أولًا";
    public const string OldestFirstSort = "الأقدم أولًا";
    public const string HighestAmountSort = "الأعلى مبلغًا";
    public const string LowestAmountSort = "الأقل مبلغًا";
    public const string NumberSort = "رقم المصروف";

    private readonly IExpenseInvoiceReader? repository;
    private readonly List<ExpenseInvoiceListItemViewModel> loadedInvoices = [];

    private string searchText = "";
    private string selectedDatePreset = AllDatesPreset;
    private DateTime? fromDate;
    private DateTime? toDate;
    private string selectedCategoryFilter = AllCategories;
    private string selectedAccountFilter = AllAccounts;
    private string selectedUserFilter = AllUsers;
    private string selectedPaymentFilter = AllPayments;
    private string selectedSort = NewestFirstSort;
    private string minimumAmountText = "";
    private string maximumAmountText = "";
    private ExpenseInvoiceListItemViewModel? selectedInvoice;
    private string statusMessage;
    private bool isErrorStatus;
    private bool isBusy;

    public ExpenseInvoicesViewModel(IExpenseInvoiceReader? repository = null)
    {
        this.repository = repository;

        RefreshCommand = new ActionCommand(() => RefreshFromDatabase());
        ClearFiltersCommand = new ActionCommand(ClearFilters);
        SelectDatePresetCommand = new RelayCommand<string>(preset => ApplyDatePreset(preset));
        AnalyzeCategoryCommand = new RelayCommand<ExpenseInvoiceListItemViewModel>(i =>
        {
            if (i is { Id: > 0, ExpenseCategoryId: > 0 }) AnalyzeCategoryRequested?.Invoke(i.ExpenseCategoryId,i.ExpenseDate);
        }, i => i is { Id: > 0, ExpenseCategoryId: > 0 });
        OpenDetailsCommand = new RelayCommand<ExpenseInvoiceListItemViewModel>(OpenDetails);
        OpenAddExpenseCommand = new ActionCommand(() => OpenAddExpenseRequested?.Invoke());

        if (repository is null)
        {
            LoadDesignSamples();
            statusMessage =
                "وضع العرض التجريبي: لا يوجد اتصال بقاعدة البيانات، والمعروض خمسة مصروفات توضيحية فقط. " +
                "بعد تسجيل الدخول تُقرأ كل فواتير المصروفات من dbo.Expenses وdbo.ExpensePayments.";
        }
        else
        {
            statusMessage = "اضغط «تحديث» لتحميل فواتير المصروفات من قاعدة البيانات.";
        }

        RefreshFilteredInvoices();
    }

    // ------------------------------------------------------------ filter inputs

    public ObservableCollection<ExpenseInvoiceListItemViewModel> FilteredInvoices { get; } = [];
    public ObservableCollection<string> CategoryFilters { get; } = [AllCategories];
    public ObservableCollection<string> AccountFilters { get; } = [AllAccounts];
    public ObservableCollection<string> UserFilters { get; } = [AllUsers];

    public IReadOnlyList<string> PaymentFilters { get; } = [AllPayments, SinglePayment, SplitPayment];
    public IReadOnlyList<string> SortOptions { get; } =
        [NewestFirstSort, OldestFirstSort, HighestAmountSort, LowestAmountSort, NumberSort];

    /// <summary>Raised with the details view model; the view opens the dialog window.</summary>
    public Action<ExpenseInvoiceDetailsViewModel>? OpenDetailsDialogRequested { get; set; }

    /// <summary>Raised by «تسجيل مصروف جديد»; the shell navigates to «اضافة مصروف».</summary>
    public Action? OpenAddExpenseRequested { get; set; }

    public Action<int,DateTime>? AnalyzeCategoryRequested { get; set; }

    public string SearchText
    {
        get => searchText;
        set
        {
            var wanted = value ?? "";
            if (searchText == wanted) return;
            searchText = wanted;
            Changed();
            Changed(nameof(HasActiveFilters));
            RefreshFilteredInvoices();
        }
    }

    public DateTime? FromDate
    {
        get => fromDate;
        set
        {
            if (fromDate == value) return;
            fromDate = value;
            selectedDatePreset = CustomDatesPreset;
            Changed();
            NotifyDatePresetFlags();
            RefreshFilteredInvoices();
        }
    }

    public DateTime? ToDate
    {
        get => toDate;
        set
        {
            if (toDate == value) return;
            toDate = value;
            selectedDatePreset = CustomDatesPreset;
            Changed();
            NotifyDatePresetFlags();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedDatePreset
    {
        get => selectedDatePreset;
        private set
        {
            if (selectedDatePreset == value) return;
            selectedDatePreset = value;
            Changed();
            NotifyDatePresetFlags();
        }
    }

    public bool IsAllDatePreset => selectedDatePreset == AllDatesPreset;
    public bool IsTodayPreset => selectedDatePreset == TodayPreset;
    public bool IsLast7DaysPreset => selectedDatePreset == Last7DaysPreset;
    public bool IsThisMonthPreset => selectedDatePreset == ThisMonthPreset;
    public bool IsLastMonthPreset => selectedDatePreset == LastMonthPreset;
    public bool IsCustomDatePreset => selectedDatePreset == CustomDatesPreset;

    // The combo boxes hand null back for a moment while their ItemsSource is rebuilt, so every
    // "all" filter coerces null to its own label instead of filtering everything out.
    public string SelectedCategoryFilter
    {
        get => selectedCategoryFilter;
        set
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? AllCategories : value;
            if (selectedCategoryFilter == wanted) return;
            selectedCategoryFilter = wanted;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedAccountFilter
    {
        get => selectedAccountFilter;
        set
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? AllAccounts : value;
            if (selectedAccountFilter == wanted) return;
            selectedAccountFilter = wanted;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedUserFilter
    {
        get => selectedUserFilter;
        set
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? AllUsers : value;
            if (selectedUserFilter == wanted) return;
            selectedUserFilter = wanted;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedPaymentFilter
    {
        get => selectedPaymentFilter;
        set
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? AllPayments : value;
            if (selectedPaymentFilter == wanted) return;
            selectedPaymentFilter = wanted;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedSort
    {
        get => selectedSort;
        set
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? NewestFirstSort : value;
            if (selectedSort == wanted) return;
            selectedSort = wanted;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string MinimumAmountText
    {
        get => minimumAmountText;
        set
        {
            var wanted = value ?? "";
            if (minimumAmountText == wanted) return;
            minimumAmountText = wanted;
            Changed();
            Changed(nameof(HasInvalidAmountRange));
            RefreshFilteredInvoices();
        }
    }

    public string MaximumAmountText
    {
        get => maximumAmountText;
        set
        {
            var wanted = value ?? "";
            if (maximumAmountText == wanted) return;
            maximumAmountText = wanted;
            Changed();
            Changed(nameof(HasInvalidAmountRange));
            RefreshFilteredInvoices();
        }
    }

    public ExpenseInvoiceListItemViewModel? SelectedInvoice
    {
        get => selectedInvoice;
        set
        {
            if (ReferenceEquals(selectedInvoice, value)) return;
            if (selectedInvoice is not null) selectedInvoice.IsSelected = false;
            selectedInvoice = value;
            if (selectedInvoice is not null) selectedInvoice.IsSelected = true;
            Changed();
        }
    }

    // ---------------------------------------------------------------- status

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

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (isBusy == value) return;
            isBusy = value;
            Changed();
        }
    }

    /// <summary>True when the screen reads SQL; false in the design preview.</summary>
    public bool IsDatabaseBacked => repository is not null;
    public string DataSourceLabel => IsDatabaseBacked ? "متصل بقاعدة البيانات" : "وضع العرض التجريبي";

    public bool HasInvoices => loadedInvoices.Count > 0;
    public bool HasNoInvoices => loadedInvoices.Count == 0;
    public bool HasNoFilteredInvoices => FilteredInvoices.Count == 0;
    public bool ShowNoMatchState => loadedInvoices.Count > 0 && FilteredInvoices.Count == 0;

    public string EmptyStateText => loadedInvoices.Count == 0
        ? "لا توجد فواتير مصروفات مسجلة بعد. سجّل أول مصروف من «المصروفات ← اضافة مصروف» وسيظهر هنا فورًا."
        : "لا يوجد مصروف مطابق للفلاتر الحالية. جرّب توسيع الفترة أو إلغاء أحد الفلاتر.";

    // ---------------------------------------------------------------- totals

    public int ShownCount => FilteredInvoices.Count;
    public string ShownCountDisplay => ShownCount == 1 ? "مصروف واحد" : $"{ShownCount} مصروف";
    public decimal ShownTotalAmount => Math.Round(FilteredInvoices.Sum(invoice => invoice.Amount), 2, MidpointRounding.AwayFromZero);
    public string ShownTotalDisplay => PosFormatting.FormatCurrency(ShownTotalAmount);
    public decimal ShownAverageAmount => ShownCount == 0
        ? 0m
        : Math.Round(ShownTotalAmount / ShownCount, 2, MidpointRounding.AwayFromZero);
    public string ShownAverageDisplay => PosFormatting.FormatCurrency(ShownAverageAmount);

    public int SplitCount => FilteredInvoices.Count(invoice => invoice.IsSplitPayment);
    public string SplitCountDisplay => SplitCount == 1 ? "مصروف مقسّم واحد" : $"{SplitCount} مصروف مقسّم";
    public int SingleAccountCount => FilteredInvoices.Count(invoice => !invoice.IsSplitPayment);
    public string SingleAccountCountDisplay => SingleAccountCount == 1
        ? "حساب واحد"
        : $"{SingleAccountCount} بحساب واحد";

    public int CategoriesUsedCount => FilteredInvoices.Select(invoice => invoice.CategoryName)
        .Distinct(StringComparer.CurrentCulture).Count();
    public int AccountsUsedCount => FilteredInvoices
        .SelectMany(invoice => invoice.AccountAliases)
        .Distinct(StringComparer.CurrentCulture)
        .Count();
    public string AccountsUsedDisplay => AccountsUsedCount == 1
        ? "حساب مالي واحد"
        : $"{AccountsUsedCount} حسابات مالية";

    /// <summary>The section with the largest share of the shown total, e.g. «إيجار 4,500.00 ج.م».</summary>
    public string TopCategoryDisplay
    {
        get
        {
            var top = FilteredInvoices
                .GroupBy(invoice => invoice.CategoryName)
                .Select(group => new { Name = group.Key, Total = group.Sum(invoice => invoice.Amount) })
                .OrderByDescending(item => item.Total)
                .FirstOrDefault();
            return top is null ? "—" : $"{top.Name} · {PosFormatting.FormatCurrency(top.Total)}";
        }
    }

    public string HistorySourceLabel =>
        $"{ShownCountDisplay} · {ShownTotalDisplay} · {SplitCountDisplay} — محسوبة من الفلاتر الحالية";

    public bool HasInvalidAmountRange =>
        ExpensePaymentLineViewModel.TryParseMoney(MinimumAmountText, out var minimum)
        && ExpensePaymentLineViewModel.TryParseMoney(MaximumAmountText, out var maximum)
        && minimum > maximum;

    public bool HasInvalidDateRange => FromDate is { } from && ToDate is { } to && from.Date > to.Date;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText)
        || !IsAllDatePreset
        || SelectedCategoryFilter != AllCategories
        || SelectedAccountFilter != AllAccounts
        || SelectedUserFilter != AllUsers
        || SelectedPaymentFilter != AllPayments
        || !string.IsNullOrWhiteSpace(MinimumAmountText)
        || !string.IsNullOrWhiteSpace(MaximumAmountText);

    // -------------------------------------------------------------- commands

    public ActionCommand RefreshCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand OpenAddExpenseCommand { get; }
    public RelayCommand<string> SelectDatePresetCommand { get; }
    public RelayCommand<ExpenseInvoiceListItemViewModel> AnalyzeCategoryCommand { get; }
    public RelayCommand<ExpenseInvoiceListItemViewModel> OpenDetailsCommand { get; }

    // ------------------------------------------------------------ data loading

    /// <summary>Reloads every expense and its payment lines from SQL Server.</summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null) return false;

        IsBusy = true;
        try
        {
            var records = repository.LoadInvoices();
            loadedInvoices.Clear();
            foreach (var record in records)
                loadedInvoices.Add(new ExpenseInvoiceListItemViewModel(record));
        }
        catch (Exception ex)
        {
            IsBusy = false;
            SetStatus(DescribeError(ex, "تعذر تحميل فواتير المصروفات من قاعدة البيانات."), true);
            return false;
        }
        IsBusy = false;

        RebuildFilterOptions();
        RefreshFilteredInvoices();
        SetStatus(
            FilteredInvoices.Count == loadedInvoices.Count
                ? $"تم تحميل {loadedInvoices.Count} فاتورة مصروفات من قاعدة البيانات."
                : $"تم تحميل {loadedInvoices.Count} فاتورة، والمعروض منها {FilteredInvoices.Count} بعد الفلاتر.",
            false);
        return true;
    }

    /// <summary>Alias used by the shell when another screen adds or changes an expense.</summary>
    public void NotifyExternalDataChanged() => RefreshFromDatabase();

    private void LoadDesignSamples()
    {
        foreach (var sample in ExpenseDesignSeedData.CreateSampleExpenseInvoices())
        {
            loadedInvoices.Add(new ExpenseInvoiceListItemViewModel(
                sample.ExpenseNumber,
                sample.ExpenseDate,
                sample.CategoryName,
                sample.Amount,
                sample.Description,
                sample.ActorUserName,
                DateTime.Now.AddDays(-1),
                sample.Payments.Select(payment =>
                    new ExpenseInvoicePaymentLineViewModel(0, payment.AccountAlias, payment.Amount))));
        }

        loadedInvoices.Sort((left, right) => right.ExpenseDate.CompareTo(left.ExpenseDate));
        RebuildFilterOptions();
    }

    private void RebuildFilterOptions()
    {
        var categories = loadedInvoices.Select(invoice => invoice.CategoryName)
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
        var accounts = loadedInvoices.SelectMany(invoice => invoice.AccountAliases)
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
        var users = loadedInvoices.Select(invoice => invoice.ActorUserName)
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        RebuildOptions(CategoryFilters, AllCategories, categories);
        RebuildOptions(AccountFilters, AllAccounts, accounts);
        RebuildOptions(UserFilters, AllUsers, users);

        if (!CategoryFilters.Contains(SelectedCategoryFilter)) SelectedCategoryFilter = AllCategories;
        if (!AccountFilters.Contains(SelectedAccountFilter)) SelectedAccountFilter = AllAccounts;
        if (!UserFilters.Contains(SelectedUserFilter)) SelectedUserFilter = AllUsers;
    }

    private static void RebuildOptions(ObservableCollection<string> target, string allLabel, List<string> values)
    {
        var wanted = new List<string> { allLabel };
        wanted.AddRange(values);
        if (target.SequenceEqual(wanted, StringComparer.CurrentCulture)) return;

        target.Clear();
        foreach (var value in wanted) target.Add(value);
    }

    // --------------------------------------------------------------- filtering

    private void RefreshFilteredInvoices()
    {
        var query = loadedInvoices.Where(MatchesDateRange);

        if (SelectedCategoryFilter != AllCategories)
            query = query.Where(invoice => invoice.CategoryName == SelectedCategoryFilter);

        if (SelectedAccountFilter != AllAccounts)
            query = query.Where(invoice => invoice.AccountAliases.Contains(SelectedAccountFilter, StringComparer.CurrentCulture));

        if (SelectedUserFilter != AllUsers)
            query = query.Where(invoice => invoice.ActorUserName == SelectedUserFilter);

        query = SelectedPaymentFilter switch
        {
            SplitPayment => query.Where(invoice => invoice.IsSplitPayment),
            SinglePayment => query.Where(invoice => !invoice.IsSplitPayment),
            _ => query
        };

        if (ExpensePaymentLineViewModel.TryParseMoney(MinimumAmountText, out var minimum))
            query = query.Where(invoice => invoice.Amount >= minimum);

        if (ExpensePaymentLineViewModel.TryParseMoney(MaximumAmountText, out var maximum))
            query = query.Where(invoice => invoice.Amount <= maximum);

        var term = (SearchText ?? "").Trim();
        if (term.Length > 0)
            query = query.Where(invoice => invoice.Matches(term));

        query = SelectedSort switch
        {
            OldestFirstSort => query.OrderBy(invoice => invoice.ExpenseDate).ThenBy(invoice => invoice.Id),
            HighestAmountSort => query.OrderByDescending(invoice => invoice.Amount).ThenByDescending(invoice => invoice.ExpenseDate),
            LowestAmountSort => query.OrderBy(invoice => invoice.Amount).ThenByDescending(invoice => invoice.ExpenseDate),
            NumberSort => query.OrderBy(invoice => invoice.ExpenseNumber, StringComparer.Ordinal),
            _ => query.OrderByDescending(invoice => invoice.ExpenseDate).ThenByDescending(invoice => invoice.Id)
        };

        var wanted = query.ToList();
        FilteredInvoices.Clear();
        foreach (var invoice in wanted) FilteredInvoices.Add(invoice);

        if (selectedInvoice is not null && !wanted.Contains(selectedInvoice))
            SelectedInvoice = null;

        NotifyTotalsChanged();
    }

    private bool MatchesDateRange(ExpenseInvoiceListItemViewModel invoice)
    {
        if (FromDate is { } from && invoice.ExpenseDate < from.Date) return false;
        if (ToDate is { } to && invoice.ExpenseDate > to.Date) return false;
        return true;
    }

    private void ApplyDatePreset(string? preset)
    {
        var today = DateTime.Today;
        switch (preset)
        {
            case TodayPreset:
                SetRange(today, today, TodayPreset);
                break;
            case Last7DaysPreset:
                SetRange(today.AddDays(-6), today, Last7DaysPreset);
                break;
            case ThisMonthPreset:
                SetRange(new DateTime(today.Year, today.Month, 1), today, ThisMonthPreset);
                break;
            case LastMonthPreset:
                var firstOfThisMonth = new DateTime(today.Year, today.Month, 1);
                SetRange(firstOfThisMonth.AddMonths(-1), firstOfThisMonth.AddDays(-1), LastMonthPreset);
                break;
            case CustomDatesPreset:
                SelectedDatePreset = CustomDatesPreset;
                break;
            default:
                SetRange(null, null, AllDatesPreset);
                break;
        }
    }

    private void SetRange(DateTime? from, DateTime? to, string preset)
    {
        fromDate = from;
        toDate = to;
        selectedDatePreset = preset;
        Changed(nameof(FromDate));
        Changed(nameof(ToDate));
        Changed(nameof(SelectedDatePreset));
        NotifyDatePresetFlags();
        RefreshFilteredInvoices();
    }

    private void NotifyDatePresetFlags()
    {
        Changed(nameof(IsAllDatePreset));
        Changed(nameof(IsTodayPreset));
        Changed(nameof(IsLast7DaysPreset));
        Changed(nameof(IsThisMonthPreset));
        Changed(nameof(IsLastMonthPreset));
        Changed(nameof(IsCustomDatePreset));
        Changed(nameof(HasInvalidDateRange));
        Changed(nameof(HasActiveFilters));
    }

    private void NotifyTotalsChanged()
    {
        Changed(nameof(ShownCount));
        Changed(nameof(ShownCountDisplay));
        Changed(nameof(ShownTotalAmount));
        Changed(nameof(ShownTotalDisplay));
        Changed(nameof(ShownAverageAmount));
        Changed(nameof(ShownAverageDisplay));
        Changed(nameof(SplitCount));
        Changed(nameof(SplitCountDisplay));
        Changed(nameof(SingleAccountCount));
        Changed(nameof(SingleAccountCountDisplay));
        Changed(nameof(CategoriesUsedCount));
        Changed(nameof(AccountsUsedCount));
        Changed(nameof(AccountsUsedDisplay));
        Changed(nameof(TopCategoryDisplay));
        Changed(nameof(HistorySourceLabel));
        Changed(nameof(HasInvoices));
        Changed(nameof(HasNoInvoices));
        Changed(nameof(HasNoFilteredInvoices));
        Changed(nameof(ShowNoMatchState));
        Changed(nameof(EmptyStateText));
        Changed(nameof(HasActiveFilters));
        Changed(nameof(HasInvalidDateRange));
    }

    private void ClearFilters()
    {
        searchText = "";
        minimumAmountText = "";
        maximumAmountText = "";
        selectedCategoryFilter = AllCategories;
        selectedAccountFilter = AllAccounts;
        selectedUserFilter = AllUsers;
        selectedPaymentFilter = AllPayments;
        selectedSort = NewestFirstSort;
        fromDate = null;
        toDate = null;
        selectedDatePreset = AllDatesPreset;

        Changed(nameof(SearchText));
        Changed(nameof(MinimumAmountText));
        Changed(nameof(MaximumAmountText));
        Changed(nameof(SelectedCategoryFilter));
        Changed(nameof(SelectedAccountFilter));
        Changed(nameof(SelectedUserFilter));
        Changed(nameof(SelectedPaymentFilter));
        Changed(nameof(SelectedSort));
        Changed(nameof(SelectedDatePreset));
        Changed(nameof(FromDate));
        Changed(nameof(ToDate));
        NotifyDatePresetFlags();
        RefreshFilteredInvoices();
        SetStatus("تم إلغاء كل الفلاتر وعرض كل فواتير المصروفات.", false);
    }

    public void OpenFromReport(int id)
    {
        if (repository is not null && IsErrorStatus) { SelectedInvoice = null; return; }
        ClearFilters();
        var invoice = loadedInvoices.FirstOrDefault(i => i.Id == id && id > 0);
        if (invoice is null) { SelectedInvoice = null; SetStatus("لم يعد مستند المصروف متاحًا.", true); return; }
        SearchText = invoice.ExpenseNumber;
        OpenDetails(invoice);
    }

    private void OpenDetails(ExpenseInvoiceListItemViewModel? invoice)
    {
        if (invoice is null) return;

        SelectedInvoice = invoice;
        OpenDetailsDialogRequested?.Invoke(new ExpenseInvoiceDetailsViewModel(invoice));
    }

    /// <summary>Builds the UTF-8 CSV the view saves through the file dialog.</summary>
    public IEnumerable<string> CreateCsvLines()
    {
        yield return string.Join(',', new[]
        {
            "رقم المصروف", "التاريخ", "التصنيف", "البيان", "المبلغ", "عدد وسائل الدفع",
            "وسائل الدفع", "المستخدم", "تاريخ التسجيل"
        }.Select(EscapeCsv));

        foreach (var invoice in FilteredInvoices)
        {
            yield return string.Join(',', new[]
            {
                invoice.ExpenseNumber,
                invoice.DateDisplay,
                invoice.CategoryName,
                invoice.Description,
                invoice.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                invoice.PaymentMethodsCount.ToString(CultureInfo.InvariantCulture),
                invoice.PaymentBreakdown,
                invoice.ActorUserName,
                invoice.CreatedAtDisplay
            }.Select(EscapeCsv));
        }
    }

    private static string EscapeCsv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsErrorStatus = isError;
    }

    private static string DescribeError(Exception exception, string fallback) => exception switch
    {
        ExpenseDataException or ExpenseCategoryDataException => exception.Message,
        InvalidOperationException { InnerException: not null } => exception.Message,
        _ => fallback
    };
}

/// <summary>Read-only details of one stored expense: its header plus every account share.</summary>
public sealed class ExpenseInvoiceDetailsViewModel : ObservableObject
{
    public ExpenseInvoiceDetailsViewModel(ExpenseInvoiceListItemViewModel invoice)
    {
        Invoice = invoice;
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    public ExpenseInvoiceListItemViewModel Invoice { get; }
    public IReadOnlyList<ExpenseInvoicePaymentLineViewModel> Payments => Invoice.Payments;
    public Action? RequestClose { get; set; }
    public ActionCommand CloseCommand { get; }

    public bool HasMultiplePayments => Invoice.IsSplitPayment;
    public decimal PaymentsTotal => Invoice.PaymentsTotal;
    public string PaymentsTotalDisplay => PosFormatting.FormatCurrency(PaymentsTotal);
    public bool IsPaymentTotalMatchingAmount => !Invoice.HasPaymentBalanceGap;
    public string PaymentTotalMessage => IsPaymentTotalMatchingAmount
        ? "مجموع وسائل الدفع مطابق لمبلغ المصروف."
        : "تنبيه: مجموع وسائل الدفع لا يطابق مبلغ المصروف؛ راجع حركات الحسابات.";

    public string HeaderTitle => $"مصروف {Invoice.ExpenseNumber}";
    public string Subtitle => $"{Invoice.CategoryName} · {Invoice.DateDisplay} · {Invoice.ActorUserName}";
    public string AmountDisplay => Invoice.AmountDisplay;
    public string DescriptionDisplay => Invoice.DescriptionDisplay;
    public string CreatedAtDisplay => Invoice.CreatedAtDisplay;
    public string PaymentMethodsDisplay => Invoice.PaymentMethodsDisplay;
}
