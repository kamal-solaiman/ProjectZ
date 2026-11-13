using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class SupplierStatementRow
{
    public DateTime Timestamp { get; init; }
    public string Description { get; init; } = "";
    public string Reference { get; init; } = "";
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public decimal RunningBalance { get; init; }
    public string DateDisplay => Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string TimeDisplay => Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string DebitDisplay => Debit > 0m ? PosFormatting.FormatCurrency(Debit) : "—";
    public string CreditDisplay => Credit > 0m ? PosFormatting.FormatCurrency(Credit) : "—";
    public string RunningBalanceDisplay => PosFormatting.FormatCurrency(Math.Abs(RunningBalance));
    public string RunningBalanceSide => RunningBalance > 0m ? "له (آجل)" : RunningBalance < 0m ? "لنا" : "خالص";
    public bool IsCreditBalance => RunningBalance < 0m;
}

public sealed class SupplierDirectoryEntryViewModel : ObservableObject
{
    private bool isSelected;
    public SupplierDesignItem Supplier { get; }
    public List<PurchaseInvoiceDesignItem> PurchaseInvoices { get; } = [];
    public int InvoicesCount { get; private set; }
    public int OpenInvoicesCount { get; private set; }
    public decimal TotalPurchases { get; private set; }
    public decimal TotalPaid { get; private set; }
    public decimal TotalReturns { get; private set; }
    public decimal Outstanding { get; private set; }
    public DateTime? LastActivityAt { get; private set; }
    public bool HasTransactions => InvoicesCount > 0;
    public bool HasOutstanding => Outstanding > 0m;
    public string TotalPurchasesDisplay => PosFormatting.FormatCurrency(TotalPurchases);
    public string TotalPaidDisplay => PosFormatting.FormatCurrency(TotalPaid);
    public string TotalReturnsDisplay => PosFormatting.FormatCurrency(TotalReturns);
    public string OutstandingDisplay => PosFormatting.FormatCurrency(Outstanding);
    public string InvoicesSummary => HasTransactions ? $"{InvoicesCount} فاتورة · مفتوحة {OpenInvoicesCount}" : "بدون فواتير";
    public string BalanceStatusTitle => HasOutstanding ? $"مستحق له: {OutstandingDisplay}" : HasTransactions ? "خالص / مسدد" : "رصيد صفر";
    public string LastActivityDisplay => LastActivityAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    public bool IsSelected
    {
        get => isSelected;
        internal set { if (isSelected == value) return; isSelected = value; Changed(); }
    }

    public SupplierDirectoryEntryViewModel(SupplierDesignItem supplier) => Supplier = supplier;

    public void Recalculate(IEnumerable<PurchaseInvoiceDesignItem> invoices)
    {
        PurchaseInvoices.Clear();
        PurchaseInvoices.AddRange(invoices.OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id));
        InvoicesCount = PurchaseInvoices.Count(i => !i.IsCancelled);
        OpenInvoicesCount = PurchaseInvoices.Count(i => i.HasRemainingPayable);
        TotalPurchases = PurchaseInvoices.Where(i => !i.IsCancelled).Sum(i => i.NetTotal);
        TotalPaid = PurchaseInvoices.Where(i => !i.IsCancelled).Sum(i => i.NetCashOutflow);
        TotalReturns = PurchaseInvoices.Where(i => !i.IsCancelled).Sum(i => i.ReturnedGoodsTotal);
        Outstanding = PurchaseInvoices.Sum(i => i.RemainingPayable);
        LastActivityAt = PurchaseInvoices.Count == 0 ? null : PurchaseInvoices.Max(i => i.IssuedAt);

        Supplier.TotalPurchasesCount = InvoicesCount;
        Supplier.OutstandingPayable = Outstanding;
        Changed(nameof(InvoicesCount)); Changed(nameof(OpenInvoicesCount));
        Changed(nameof(TotalPurchases)); Changed(nameof(TotalPaid)); Changed(nameof(TotalReturns)); Changed(nameof(Outstanding));
        Changed(nameof(LastActivityAt)); Changed(nameof(HasTransactions)); Changed(nameof(HasOutstanding));
        Changed(nameof(TotalPurchasesDisplay)); Changed(nameof(TotalPaidDisplay)); Changed(nameof(TotalReturnsDisplay));
        Changed(nameof(OutstandingDisplay)); Changed(nameof(InvoicesSummary)); Changed(nameof(BalanceStatusTitle)); Changed(nameof(LastActivityDisplay));
    }
}

public sealed class SuppliersViewModel : ObservableObject
{
    private readonly ObservableCollection<SupplierDesignItem> sharedSuppliers;
    private readonly ObservableCollection<PurchaseInvoiceDesignItem> sharedPurchaseInvoices;
    private readonly PurchaseInvoicesViewModel purchaseActions;
    private readonly Action<SupplierDesignItem>? startPurchaseForSupplier;
    private readonly Action? notifyExternalDataChanged;
    private readonly List<SupplierDirectoryEntryViewModel> allEntries = [];

    private SupplierDirectoryEntryViewModel? selectedSupplierEntry;
    private string searchText = "";
    private string selectedSpecializationFilter = "كل التخصصات";
    private string selectedBalanceFilter = "جميع الموردين";
    private string selectedSortOption = "الأحدث نشاطًا";
    private int activeDetailTabIndex;
    private string statusMessage = "دليل الموردين الموحد: الحسابات تُحسب تلقائيًا من المشتريات والسداد والمرتجعات.";
    private bool isErrorStatus;

    public ObservableCollection<SupplierDirectoryEntryViewModel> FilteredSuppliers { get; } = [];
    public ObservableCollection<PurchaseInvoiceDesignItem> SelectedSupplierInvoices { get; } = [];
    public ObservableCollection<SupplierStatementRow> SelectedSupplierStatement { get; } = [];
    public ObservableCollection<string> SpecializationFilterOptions { get; } = ["كل التخصصات"];
    public IReadOnlyList<string> BalanceFilterOptions { get; } = ["جميع الموردين", "مستحق لهم مبالغ", "خالصون / مسددون", "لديهم فواتير", "بدون معاملات بعد"];
    public IReadOnlyList<string> SortOptions { get; } = ["الأحدث نشاطًا", "الأعلى تعاملًا", "الأعلى مستحقًا", "الأكثر فواتير", "الاسم أبجديًا"];

    public Action<string?, Func<string, bool>, Func<string, int?, string?>, Action<SupplierDesignItem>>? OpenAddSupplierDialogRequested { get; set; }
    public Action<SupplierDesignItem, Func<string, int?, string?>, Action<SupplierDesignItem>>? OpenEditSupplierDialogRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    public SupplierDirectoryEntryViewModel? SelectedSupplierEntry
    {
        get => selectedSupplierEntry;
        set
        {
            if (ReferenceEquals(selectedSupplierEntry, value)) return;
            if (selectedSupplierEntry is not null) selectedSupplierEntry.IsSelected = false;
            selectedSupplierEntry = value;
            if (selectedSupplierEntry is not null) selectedSupplierEntry.IsSelected = true;
            RefreshSelectedSupplierDetails();
            Changed(); Changed(nameof(HasSelectedSupplier));
        }
    }

    public bool HasSelectedSupplier => SelectedSupplierEntry is not null;
    public string SearchText { get => searchText; set { if (searchText == value) return; searchText = value; Changed(); RefreshFilteredSuppliers(); } }
    public string SelectedSpecializationFilter { get => selectedSpecializationFilter; set { if (string.IsNullOrWhiteSpace(value) || selectedSpecializationFilter == value) return; selectedSpecializationFilter = value; Changed(); RefreshFilteredSuppliers(); } }
    public string SelectedBalanceFilter { get => selectedBalanceFilter; set { if (string.IsNullOrWhiteSpace(value) || selectedBalanceFilter == value) return; selectedBalanceFilter = value; Changed(); RefreshFilteredSuppliers(); } }
    public string SelectedSortOption { get => selectedSortOption; set { if (string.IsNullOrWhiteSpace(value) || selectedSortOption == value) return; selectedSortOption = value; Changed(); RefreshFilteredSuppliers(); } }
    public int ActiveDetailTabIndex
    {
        get => activeDetailTabIndex;
        set { if (activeDetailTabIndex == value) return; activeDetailTabIndex = value; Changed(); Changed(nameof(IsInvoicesTabSelected)); Changed(nameof(IsStatementTabSelected)); }
    }
    public bool IsInvoicesTabSelected => ActiveDetailTabIndex == 0;
    public bool IsStatementTabSelected => ActiveDetailTabIndex == 1;
    public string SelectedInvoicesTabTitle => $"فواتير المشتريات ({SelectedSupplierEntry?.InvoicesCount ?? 0})";
    public string SelectedStatementTabTitle => $"كشف حساب ({SelectedSupplierStatement.Count})";
    public bool HasNoFilteredSuppliers => FilteredSuppliers.Count == 0;
    public bool HasNoSelectedSupplierInvoices => SelectedSupplierInvoices.Count == 0;
    public bool HasNoSelectedSupplierStatement => SelectedSupplierStatement.Count == 0;


    public string StatusMessage { get => statusMessage; private set { if (statusMessage == value) return; statusMessage = value; Changed(); Changed(nameof(HasStatusMessage)); } }
    public bool IsErrorStatus { get => isErrorStatus; private set { if (isErrorStatus == value) return; isErrorStatus = value; Changed(); } }
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ActionCommand AddSupplierCommand { get; }
    public ActionCommand EditSelectedSupplierCommand { get; }
    public ActionCommand DeleteSelectedSupplierCommand { get; }
    public ActionCommand StartPurchaseForSupplierCommand { get; }
    public ActionCommand PayOldestInvoiceCommand { get; }
    public ActionCommand RefreshDataCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public RelayCommand<SupplierDirectoryEntryViewModel> SelectSupplierCommand { get; }
    public RelayCommand<SupplierDirectoryEntryViewModel> EditSupplierRowCommand { get; }
    public RelayCommand<string> SelectDetailTabCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> PayInvoiceCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> ReturnInvoiceCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> OpenInvoiceDetailsCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> PrintInvoiceCommand { get; }

    public SuppliersViewModel()
    {
        sharedSuppliers = PurchaseDesignSeedData.CreateSharedSampleSuppliers();
        purchaseActions = new PurchaseInvoicesViewModel(null, sharedSuppliers, null, null, "المشرف");
        sharedPurchaseInvoices = GetInvoicesFromDesignActions(purchaseActions);
        AddSupplierCommand = new ActionCommand(OpenAddSupplier);
        EditSelectedSupplierCommand = new ActionCommand(() => EditSupplier(SelectedSupplierEntry));
        DeleteSelectedSupplierCommand = new ActionCommand(() => DeleteSupplier(SelectedSupplierEntry));
        StartPurchaseForSupplierCommand = new ActionCommand(StartPurchaseForSelected);
        PayOldestInvoiceCommand = new ActionCommand(PayOldestOutstandingInvoice);
        RefreshDataCommand = new ActionCommand(NotifyExternalDataChanged);
        ClearFiltersCommand = new ActionCommand(ClearAllFilters);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        SelectSupplierCommand = new RelayCommand<SupplierDirectoryEntryViewModel>(e => { if (e is not null) SelectedSupplierEntry = e; });
        EditSupplierRowCommand = new RelayCommand<SupplierDirectoryEntryViewModel>(e => { if (e is not null) { SelectedSupplierEntry = e; EditSupplier(e); } });
        SelectDetailTabCommand = new RelayCommand<string>(s => { if (int.TryParse(s, out var index)) ActiveDetailTabIndex = index; });
        PayInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(PayInvoice);
        ReturnInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(ReturnInvoice);
        OpenInvoiceDetailsCommand = new RelayCommand<PurchaseInvoiceDesignItem>(OpenInvoiceDetails);
        PrintInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(PrintInvoice);
        RebuildEntriesAndRefresh();
    }

    public SuppliersViewModel(
        ObservableCollection<SupplierDesignItem> sharedSuppliers,
        ObservableCollection<PurchaseInvoiceDesignItem> sharedPurchaseInvoices,
        PurchaseInvoicesViewModel purchaseActions,
        Action<SupplierDesignItem>? startPurchaseForSupplier = null,
        Action? notifyExternalDataChanged = null)
    {
        this.sharedSuppliers = sharedSuppliers;
        this.sharedPurchaseInvoices = sharedPurchaseInvoices;
        this.purchaseActions = purchaseActions;
        this.startPurchaseForSupplier = startPurchaseForSupplier;
        this.notifyExternalDataChanged = notifyExternalDataChanged;
        sharedSuppliers.CollectionChanged += OnExternalCollectionChanged;
        sharedPurchaseInvoices.CollectionChanged += OnExternalCollectionChanged;

        AddSupplierCommand = new ActionCommand(OpenAddSupplier);
        EditSelectedSupplierCommand = new ActionCommand(() => EditSupplier(SelectedSupplierEntry));
        DeleteSelectedSupplierCommand = new ActionCommand(() => DeleteSupplier(SelectedSupplierEntry));
        StartPurchaseForSupplierCommand = new ActionCommand(StartPurchaseForSelected);
        PayOldestInvoiceCommand = new ActionCommand(PayOldestOutstandingInvoice);
        RefreshDataCommand = new ActionCommand(NotifyExternalDataChanged);
        ClearFiltersCommand = new ActionCommand(ClearAllFilters);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        SelectSupplierCommand = new RelayCommand<SupplierDirectoryEntryViewModel>(e => { if (e is not null) SelectedSupplierEntry = e; });
        EditSupplierRowCommand = new RelayCommand<SupplierDirectoryEntryViewModel>(e => { if (e is not null) { SelectedSupplierEntry = e; EditSupplier(e); } });
        SelectDetailTabCommand = new RelayCommand<string>(s => { if (int.TryParse(s, out var index)) ActiveDetailTabIndex = index; });
        PayInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(PayInvoice);
        ReturnInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(ReturnInvoice);
        OpenInvoiceDetailsCommand = new RelayCommand<PurchaseInvoiceDesignItem>(OpenInvoiceDetails);
        PrintInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(PrintInvoice);
        RebuildEntriesAndRefresh();
    }

    private static ObservableCollection<PurchaseInvoiceDesignItem> GetInvoicesFromDesignActions(PurchaseInvoicesViewModel actions)
    {
        // Design-time collection is independent; runtime uses the explicit shared collection.
        return new ObservableCollection<PurchaseInvoiceDesignItem>(actions.FilteredInvoices);
    }

    public void NotifyExternalDataChanged() => RebuildEntriesAndRefresh();
    private void OnExternalCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildEntriesAndRefresh();

    private static string NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value) ? "" : new string(value.Where(char.IsDigit).ToArray());
    private string? ValidateDuplicatePhone(string phone, int? editingId)
    {
        var normalized = NormalizePhone(phone);
        if (normalized.Length == 0) return null;
        var duplicate = sharedSuppliers.FirstOrDefault(s => s.Id != (editingId ?? -1) &&
            (NormalizePhone(s.Phone) == normalized || NormalizePhone(s.SecondaryPhone) == normalized || NormalizePhone(s.VodafoneCashNumber) == normalized));
        return duplicate is null ? null : $"رقم الهاتف مسجل بالفعل للمورد «{duplicate.Name}» ({duplicate.SupplierCodeDisplay}).";
    }

    private void OpenAddSupplier()
    {
        OpenAddSupplierDialogRequested?.Invoke(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            name => sharedSuppliers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)),
            ValidateDuplicatePhone,
            created =>
            {
                var supplier = new SupplierDesignItem
                {
                    Id = sharedSuppliers.Select(s => s.Id).DefaultIfEmpty(0).Max() + 1,
                    Name = created.Name, Phone = created.Phone, SecondaryPhone = created.SecondaryPhone,
                    CompanyName = created.CompanyName, Specialization = created.Specialization, Address = created.Address,
                    VodafoneCashNumber = created.VodafoneCashNumber, InstaPayAddress = created.InstaPayAddress, Notes = created.Notes
                };
                sharedSuppliers.Add(supplier);
                RebuildSpecializations();
                RebuildEntriesAndRefresh(supplier.Id);
                notifyExternalDataChanged?.Invoke();
                SetStatus($"تم تسجيل المورد «{supplier.Name}» ({supplier.SupplierCodeDisplay}) بنجاح.", false);
            });
    }

    private void EditSupplier(SupplierDirectoryEntryViewModel? entry)
    {
        if (entry is null) return;
        OpenEditSupplierDialogRequested?.Invoke(entry.Supplier, ValidateDuplicatePhone, updated =>
        {
            foreach (var invoice in sharedPurchaseInvoices.Where(i => i.SupplierId == updated.Id))
            {
                invoice.SupplierName = updated.Name; invoice.SupplierPhone = updated.Phone;
                invoice.SupplierCompany = updated.CompanyName; invoice.SupplierAddress = updated.Address;
            }
            RebuildSpecializations();
            RebuildEntriesAndRefresh(updated.Id);
            notifyExternalDataChanged?.Invoke();
            SetStatus($"تم تحديث بيانات المورد «{updated.Name}» ومزامنتها مع فواتير المشتريات.", false);
        });
    }

    private void DeleteSupplier(SupplierDirectoryEntryViewModel? entry)
    {
        if (entry is null) return;
        if (entry.HasOutstanding)
        {
            SetStatus($"لا يمكن حذف المورد «{entry.Supplier.Name}» قبل سداد المستحق له ({entry.OutstandingDisplay}).", true);
            return;
        }
        var details = entry.HasTransactions
            ? $"للمورد {entry.InvoicesCount} فاتورة مسددة. سيبقى أرشيف الفواتير محفوظًا بعد حذفه من الدليل. هل تريد المتابعة؟"
            : $"سيتم حذف المورد «{entry.Supplier.Name}» نهائيًا من الدليل. هل تريد المتابعة؟";
        var confirm = new ConfirmActionViewModel("تأكيد حذف المورد", $"{entry.Supplier.Name} — {entry.Supplier.DisplayPhone}", details, "حذف المورد");
        void Confirm()
        {
            if (!sharedSuppliers.Remove(entry.Supplier)) return;
            RebuildSpecializations(); RebuildEntriesAndRefresh(); notifyExternalDataChanged?.Invoke();
            SetStatus($"تم حذف المورد «{entry.Supplier.Name}» من الدليل.", false);
        }
        if (OpenConfirmDialogRequested is not null) OpenConfirmDialogRequested(confirm, Confirm); else Confirm();
    }

    private void StartPurchaseForSelected()
    {
        if (SelectedSupplierEntry is null) return;
        startPurchaseForSupplier?.Invoke(SelectedSupplierEntry.Supplier);
    }

    private void PayOldestOutstandingInvoice()
    {
        var invoice = SelectedSupplierEntry?.PurchaseInvoices.Where(i => i.CanPayRemaining).OrderBy(i => i.IssuedAt).FirstOrDefault();
        if (invoice is null) { SetStatus("لا توجد فاتورة آجلة مفتوحة لهذا المورد.", true); return; }
        PayInvoice(invoice);
    }

    private void PayInvoice(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        if (!invoice.CanPayRemaining) { SetStatus($"الفاتورة {invoice.InvoiceNumber} لا تحتوي على رصيد مستحق للسداد.", true); return; }
        purchaseActions.OpenPaymentFromSupplier(invoice);
        AfterInvoiceAction($"تمت مراجعة سداد الفاتورة {invoice.InvoiceNumber} وتحديث حساب المورد.");
    }

    private void ReturnInvoice(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        purchaseActions.OpenReturnFromSupplier(invoice);
        AfterInvoiceAction($"تم تحديث المرتجع وحساب المورد للفاتورة {invoice.InvoiceNumber}.");
    }

    private void OpenInvoiceDetails(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        purchaseActions.OpenDetailsFromSupplier(invoice);
        AfterInvoiceAction($"تم إغلاق تفاصيل الفاتورة {invoice.InvoiceNumber} وتحديث الحساب.");
    }

    private void PrintInvoice(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        purchaseActions.OpenPrintFromSupplier(invoice);
        SetStatus($"تم فتح معاينة طباعة فاتورة الشراء {invoice.InvoiceNumber}.", false);
    }

    private void AfterInvoiceAction(string message)
    {
        var id = SelectedSupplierEntry?.Supplier.Id;
        RebuildEntriesAndRefresh(id);
        notifyExternalDataChanged?.Invoke();
        SetStatus(message, false);
    }

    private void ClearAllFilters()
    {
        searchText = ""; selectedSpecializationFilter = "كل التخصصات"; selectedBalanceFilter = "جميع الموردين"; selectedSortOption = "الأحدث نشاطًا";
        Changed(nameof(SearchText)); Changed(nameof(SelectedSpecializationFilter)); Changed(nameof(SelectedBalanceFilter)); Changed(nameof(SelectedSortOption));
        RefreshFilteredSuppliers(); SetStatus("تمت إعادة ضبط الفلاتر لعرض جميع الموردين.", false);
    }

    private void RebuildSpecializations()
    {
        var current = SelectedSpecializationFilter;
        var values = sharedSuppliers.Select(s => s.Specialization.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.CurrentCulture).OrderBy(s => s, StringComparer.CurrentCulture).ToList();
        SpecializationFilterOptions.Clear(); SpecializationFilterOptions.Add("كل التخصصات");
        foreach (var item in values) SpecializationFilterOptions.Add(item);
        if (!SpecializationFilterOptions.Contains(current)) SelectedSpecializationFilter = "كل التخصصات";
        Changed(nameof(SpecializationsBreakdownSubtext));
    }

    private void RebuildEntriesAndRefresh(int? selectSupplierId = null)
    {
        var previousId = selectSupplierId ?? SelectedSupplierEntry?.Supplier.Id;
        var previous = allEntries.ToList(); allEntries.Clear();
        foreach (var supplier in sharedSuppliers)
        {
            var entry = previous.FirstOrDefault(e => ReferenceEquals(e.Supplier, supplier)) ?? new SupplierDirectoryEntryViewModel(supplier);
            entry.Recalculate(sharedPurchaseInvoices.Where(i => i.SupplierId == supplier.Id));
            allEntries.Add(entry);
        }
        RefreshFilteredSuppliers(previousId);
    }

    private void RefreshFilteredSuppliers(int? preferredId = null)
    {
        var targetId = preferredId ?? SelectedSupplierEntry?.Supplier.Id;
        var query = SearchText.Trim();
        IEnumerable<SupplierDirectoryEntryViewModel> filtered = allEntries;
        if (SelectedSpecializationFilter != "كل التخصصات") filtered = filtered.Where(e => e.Supplier.Specialization.Equals(SelectedSpecializationFilter, StringComparison.OrdinalIgnoreCase));
        filtered = SelectedBalanceFilter switch
        {
            "مستحق لهم مبالغ" => filtered.Where(e => e.HasOutstanding),
            "خالصون / مسددون" => filtered.Where(e => e.HasTransactions && !e.HasOutstanding),
            "لديهم فواتير" => filtered.Where(e => e.HasTransactions),
            "بدون معاملات بعد" => filtered.Where(e => !e.HasTransactions),
            _ => filtered
        };
        if (query.Length > 0) filtered = filtered.Where(e =>
            e.Supplier.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Supplier.CompanyName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Supplier.Phone.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Supplier.SecondaryPhone.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Supplier.Specialization.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Supplier.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Supplier.SupplierCodeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Supplier.VodafoneCashNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Supplier.InstaPayAddress.Contains(query, StringComparison.OrdinalIgnoreCase) || e.PurchaseInvoices.Any(i => i.InvoiceNumber.Contains(query, StringComparison.OrdinalIgnoreCase) || i.SupplierInvoiceNumber.Contains(query, StringComparison.OrdinalIgnoreCase)));
        filtered = SelectedSortOption switch
        {
            "الأعلى تعاملًا" => filtered.OrderByDescending(e => e.TotalPurchases).ThenBy(e => e.Supplier.Name),
            "الأعلى مستحقًا" => filtered.OrderByDescending(e => e.Outstanding).ThenByDescending(e => e.TotalPurchases),
            "الأكثر فواتير" => filtered.OrderByDescending(e => e.InvoicesCount).ThenByDescending(e => e.TotalPurchases),
            "الاسم أبجديًا" => filtered.OrderBy(e => e.Supplier.Name, StringComparer.CurrentCulture),
            _ => filtered.OrderByDescending(e => e.LastActivityAt ?? DateTime.MinValue).ThenByDescending(e => e.Supplier.Id)
        };
        FilteredSuppliers.Clear(); foreach (var item in filtered) FilteredSuppliers.Add(item);
        SelectedSupplierEntry = (targetId.HasValue ? FilteredSuppliers.FirstOrDefault(e => e.Supplier.Id == targetId) : null) ?? FilteredSuppliers.FirstOrDefault();
        Changed(nameof(HasNoFilteredSuppliers));
    }

    private void RefreshSelectedSupplierDetails()
    {
        SelectedSupplierInvoices.Clear(); SelectedSupplierStatement.Clear();
        if (SelectedSupplierEntry is not null)
        {
            foreach (var invoice in SelectedSupplierEntry.PurchaseInvoices) SelectedSupplierInvoices.Add(invoice);
            foreach (var row in BuildStatement(SelectedSupplierEntry)) SelectedSupplierStatement.Add(row);
        }
        Changed(nameof(SelectedInvoicesTabTitle)); Changed(nameof(SelectedStatementTabTitle));
        Changed(nameof(HasNoSelectedSupplierInvoices)); Changed(nameof(HasNoSelectedSupplierStatement));
    }

    private static List<SupplierStatementRow> BuildStatement(SupplierDirectoryEntryViewModel entry)
    {
        var events = new List<(DateTime At, string Text, string Ref, decimal Debit, decimal Credit, int Order)>();
        foreach (var invoice in entry.PurchaseInvoices.Where(i => !i.IsCancelled))
        {
            var gross = invoice.NetTotal + invoice.ReturnedGoodsTotal;
            if (gross > 0m) events.Add((invoice.IssuedAt, "فاتورة شراء — زيادة مستحق المورد", invoice.InvoiceNumber, 0m, gross, 0));
            var laterPayments = invoice.ActivityLog.Where(a => a.ActionTitle.Contains("سداد", StringComparison.Ordinal)).Sum(a => a.Amount ?? 0m);
            var initialPaid = Math.Max(0m, invoice.TotalPaid - laterPayments);
            if (initialPaid > 0m) events.Add((invoice.IssuedAt.AddSeconds(1), "سداد عند إنشاء الفاتورة", invoice.InvoiceNumber, initialPaid, 0m, 1));
            foreach (var activity in invoice.ActivityLog)
            {
                if (activity.ActionTitle.Contains("سداد", StringComparison.Ordinal))
                    events.Add((activity.Timestamp, activity.ActionTitle, invoice.InvoiceNumber, activity.Amount ?? 0m, 0m, 2));
                else if (activity.ActionTitle.Contains("مرتجع", StringComparison.Ordinal))
                    events.Add((activity.Timestamp, activity.ActionTitle, invoice.InvoiceNumber, activity.Amount ?? 0m, 0m, 2));
            }
            if (invoice.SupplierRefundReceived > 0m)
                events.Add((invoice.ActivityLog.LastOrDefault(a => a.ActionTitle.Contains("مرتجع", StringComparison.Ordinal))?.Timestamp ?? invoice.IssuedAt, "مبلغ مسترد نقدًا من المورد", invoice.InvoiceNumber, 0m, invoice.SupplierRefundReceived, 3));
        }
        decimal balance = 0m;
        return events.OrderBy(e => e.At).ThenBy(e => e.Order).Select(e =>
        {
            balance += e.Credit - e.Debit;
            return new SupplierStatementRow { Timestamp = e.At, Description = e.Text, Reference = e.Ref, Debit = e.Debit, Credit = e.Credit, RunningBalance = balance };
        }).ToList();
    }

    private void SetStatus(string message, bool isError) { IsErrorStatus = isError; StatusMessage = message; }
}
