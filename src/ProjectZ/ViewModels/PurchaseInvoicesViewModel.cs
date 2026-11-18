using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PurchaseInvoicesViewModel : ObservableObject
{
    private readonly ObservableCollection<PurchaseInvoiceDesignItem> sharedPurchaseInvoices;
    private readonly ObservableCollection<SupplierDesignItem> sharedSuppliers;
    private readonly ObservableCollection<PosProductDesignItem> sharedCatalog;
    private readonly ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations;
    private readonly string currentUserName;
    private readonly Action? navigateToPurchaseEntry;
    private readonly Action? notifyInventoryChanged;
    private readonly Action? notifyReturnExchangeChanged;
    private readonly Action? notifyPurchaseEntryChanged;
    private readonly PurchaseRepository? repository;

    private PurchaseInvoiceDesignItem? selectedInvoice;
    private string searchText = "";
    private string selectedDatePreset = "الكل";
    private DateTime? customFromDate = DateTime.Today.AddDays(-30);
    private DateTime? customToDate = DateTime.Today;
    private string selectedSupplierFilter = "كل الموردين";
    private string selectedPaymentFilter = "الكل";
    private string selectedStatusFilter = "الكل";
    private string selectedSortOption = "الأحدث أولًا";

    private string statusMessage = "اضغط على أي فاتورة شراء لعرض محتوياتها بالكامل والتحكم بالتعديل والمرتجع والسداد والإلغاء والحذف.";
    private bool isErrorStatus;

    public ObservableCollection<PurchaseInvoiceDesignItem> FilteredInvoices { get; } = [];
    public ObservableCollection<string> SupplierFilters { get; } = ["كل الموردين"];

    public IReadOnlyList<string> DatePresets { get; } = ["الكل", "اليوم", "آخر 7 أيام", "هذا الشهر", "فترة مخصصة"];
    public IReadOnlyList<string> PaymentFilters { get; } = ["الكل", "نقدي", "فودافون كاش", "إنستاباي", "دفع مقسم", "آجل / متبقي"];
    public IReadOnlyList<string> StatusFilters { get; } = ["الكل", "مدفوعة بالكامل", "مدفوعة جزئيًا (آجل)", "آجل بالكامل", "مرتجع جزئي", "مرتجع كلي", "ملغاة"];
    public IReadOnlyList<string> SortOptions { get; } = ["الأحدث أولًا", "الأقدم أولًا", "الأعلى قيمة", "الأعلى آجل متبقي"];

    public Action<PurchaseInvoiceDetailsViewModel>? OpenDetailsDialogRequested { get; set; }
    public Action<PurchaseInvoicePrintPreviewViewModel>? OpenPrintPreviewRequested { get; set; }
    public Action<SupplierCreditDialogViewModel>? OpenSupplierCreditRequested { get; set; }
    public Action<PaySupplierInvoiceViewModel>? OpenPaySupplierDialogRequested { get; set; }
    public Action<EditPurchaseInvoiceViewModel>? OpenEditDialogRequested { get; set; }
    public Action<PurchaseReturnViewModel>? OpenReturnDialogRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    public PurchaseInvoiceDesignItem? SelectedInvoice
    {
        get => selectedInvoice;
        set
        {
            if (ReferenceEquals(selectedInvoice, value)) return;
            if (selectedInvoice is not null)
                selectedInvoice.IsSelected = false;

            selectedInvoice = value;
            if (selectedInvoice is not null)
                selectedInvoice.IsSelected = true;

            Changed();
            Changed(nameof(HasSelectedInvoice));
        }
    }

    public bool HasSelectedInvoice => SelectedInvoice is not null;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedDatePreset
    {
        get => selectedDatePreset;
        set
        {
            if (selectedDatePreset == value || value is null) return;
            selectedDatePreset = value;
            Changed();
            Changed(nameof(IsAllDatePreset));
            Changed(nameof(IsTodayPreset));
            Changed(nameof(IsLast7DaysPreset));
            Changed(nameof(IsThisMonthPreset));
            Changed(nameof(IsCustomDatePreset));
            RefreshFilteredInvoices();
        }
    }

    public bool IsAllDatePreset => SelectedDatePreset == "الكل";
    public bool IsTodayPreset => SelectedDatePreset == "اليوم";
    public bool IsLast7DaysPreset => SelectedDatePreset == "آخر 7 أيام";
    public bool IsThisMonthPreset => SelectedDatePreset == "هذا الشهر";
    public bool IsCustomDatePreset => SelectedDatePreset == "فترة مخصصة";

    public DateTime? CustomFromDate
    {
        get => customFromDate;
        set
        {
            if (customFromDate == value) return;
            customFromDate = value;
            Changed();
            if (IsCustomDatePreset) RefreshFilteredInvoices();
        }
    }

    public DateTime? CustomToDate
    {
        get => customToDate;
        set
        {
            if (customToDate == value) return;
            customToDate = value;
            Changed();
            if (IsCustomDatePreset) RefreshFilteredInvoices();
        }
    }

    public string SelectedSupplierFilter
    {
        get => selectedSupplierFilter;
        set
        {
            if (selectedSupplierFilter == value || value is null) return;
            selectedSupplierFilter = value;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedPaymentFilter
    {
        get => selectedPaymentFilter;
        set
        {
            if (selectedPaymentFilter == value || value is null) return;
            selectedPaymentFilter = value;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedStatusFilter
    {
        get => selectedStatusFilter;
        set
        {
            if (selectedStatusFilter == value || value is null) return;
            selectedStatusFilter = value;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public string SelectedSortOption
    {
        get => selectedSortOption;
        set
        {
            if (selectedSortOption == value || value is null) return;
            selectedSortOption = value;
            Changed();
            RefreshFilteredInvoices();
        }
    }

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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasNoFilteredInvoices => FilteredInvoices.Count == 0;

    /// <summary>True when the screen reads and writes SQL Server instead of the design sample data.</summary>
    public bool IsConnectedToDatabase => repository is not null;

    public string DataSourceLabel => repository is null
        ? "معاينة تصميم — بدون قاعدة بيانات"
        : "متصل بقاعدة البيانات — SQL Server";

    public string HistorySourceLabel => repository is null
        ? "سجل تجريبي (بدون قاعدة بيانات)"
        : "السجل مقروء من dbo.PurchaseInvoices";

    public ActionCommand RefreshFromDatabaseCommand { get; }
    public ActionCommand GoToPurchaseEntryCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public RelayCommand<string> SelectDatePresetCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> OpenInvoiceDetailsCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> OpenPrintPreviewCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> OpenEditInvoiceCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> OpenReturnDialogCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> OpenPayRemainingCommand { get; }
    public RelayCommand<PurchaseInvoiceDesignItem> DeleteInvoiceCommand { get; }

    public PurchaseInvoicesViewModel()
        : this(
            null,
            null,
            null,
            null,
            "المشرف")
    {
    }

    public PurchaseInvoicesViewModel(
        ObservableCollection<PurchaseInvoiceDesignItem>? sharedPurchaseInvoices,
        ObservableCollection<SupplierDesignItem>? sharedSuppliers,
        ObservableCollection<PosProductDesignItem>? sharedCatalog,
        ObservableCollection<InventoryUnitRegistrationItem>? sharedRegistrations,
        string currentUserName,
        Action? navigateToPurchaseEntry = null,
        Action? notifyInventoryChanged = null,
        Action? notifyReturnExchangeChanged = null,
        Action? notifyPurchaseEntryChanged = null,
        PurchaseRepository? repository = null)
    {
        this.sharedCatalog = sharedCatalog ?? PosDesignSeedData.CreateSharedSampleCatalog();
        this.sharedRegistrations = sharedRegistrations ?? InventoryDesignSeedData.CreateSharedSampleRegistrations(this.sharedCatalog);
        this.sharedSuppliers = sharedSuppliers ?? PurchaseDesignSeedData.CreateSharedSampleSuppliers();
        this.sharedPurchaseInvoices = sharedPurchaseInvoices ?? PurchaseDesignSeedData.CreateSharedSamplePurchaseInvoices(
            this.sharedCatalog,
            this.sharedRegistrations,
            this.sharedSuppliers);
        this.currentUserName = currentUserName;
        this.navigateToPurchaseEntry = navigateToPurchaseEntry;
        this.notifyInventoryChanged = notifyInventoryChanged;
        this.notifyReturnExchangeChanged = notifyReturnExchangeChanged;
        this.notifyPurchaseEntryChanged = notifyPurchaseEntryChanged;
        this.repository = repository;

        this.sharedPurchaseInvoices.CollectionChanged += OnSharedInvoicesChanged;
        this.sharedSuppliers.CollectionChanged += OnSharedSuppliersChanged;

        RefreshFromDatabaseCommand = new ActionCommand(() => RefreshFromDatabase());
        GoToPurchaseEntryCommand = new ActionCommand(() => this.navigateToPurchaseEntry?.Invoke());
        ClearFiltersCommand = new ActionCommand(ClearFilters);
        SelectDatePresetCommand = new RelayCommand<string>(preset =>
        {
            if (!string.IsNullOrEmpty(preset))
                SelectedDatePreset = preset;
        });

        OpenInvoiceDetailsCommand = new RelayCommand<PurchaseInvoiceDesignItem>(OpenDetails);
        OpenPrintPreviewCommand = new RelayCommand<PurchaseInvoiceDesignItem>(OpenPrintPreview);
        OpenEditInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(OpenEditDialog);
        OpenReturnDialogCommand = new RelayCommand<PurchaseInvoiceDesignItem>(OpenReturnDialog);
        OpenPayRemainingCommand = new RelayCommand<PurchaseInvoiceDesignItem>(OpenPayRemainingDialog);
        DeleteInvoiceCommand = new RelayCommand<PurchaseInvoiceDesignItem>(inv => RequestDeleteInvoice(inv));

        RebuildSupplierFilters();
        RefreshFilteredInvoices();
    }

    public void NotifyExternalPurchasesChanged()
    {
        RebuildSupplierFilters();
        RefreshFilteredInvoices();
    }

    /// <summary>Reloads the purchase ledger (headers, lines, units, payments and activity) from SQL Server.</summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null)
        {
            NotifyExternalPurchasesChanged();
            return false;
        }

        try
        {
            // Keep the operator on the same invoice: a reload produces new instances for every row.
            var previousId = SelectedInvoice?.Id ?? 0;
            var previousNumber = SelectedInvoice?.InvoiceNumber ?? "";

            repository.LoadInvoicesInto(sharedPurchaseInvoices);
            RebuildSupplierFilters();
            RefreshFilteredInvoices();

            var restored = sharedPurchaseInvoices.FirstOrDefault(i => i.Id == previousId)
                           ?? sharedPurchaseInvoices.FirstOrDefault(i => i.InvoiceNumber == previousNumber);
            if (restored is not null)
            {
                selectedInvoice = null;
                SelectedInvoice = restored;
            }

            SetStatus($"تم تحميل {sharedPurchaseInvoices.Count} فاتورة شراء من قاعدة البيانات.", false);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر تحميل فواتير المشتريات من قاعدة البيانات.", true);
            return false;
        }
    }

    // Shared invoice actions used by the supplier 360° screen. Keeping one implementation
    // guarantees identical stock, payment, return and audit-log behavior in both screens.
    internal void OpenDetailsFromSupplier(PurchaseInvoiceDesignItem invoice) => OpenDetails(invoice);
    internal void OpenPrintFromSupplier(PurchaseInvoiceDesignItem invoice) => OpenPrintPreview(invoice);
    internal void OpenPaymentFromSupplier(PurchaseInvoiceDesignItem invoice) => OpenPayRemainingDialog(invoice);
    internal void OpenReturnFromSupplier(PurchaseInvoiceDesignItem invoice) => OpenReturnDialog(invoice);

    private void OnSharedInvoicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSupplierFilters();
        RefreshFilteredInvoices();
    }

    private void OnSharedSuppliersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildSupplierFilters();

    private void RebuildSupplierFilters()
    {
        var current = SelectedSupplierFilter;
        var names = new List<string> { "كل الموردين" };
        foreach (var name in sharedSuppliers.Select(s => s.Name)
                     .Concat(sharedPurchaseInvoices.Select(i => i.SupplierName)))
        {
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                names.Add(name);
        }

        SupplierFilters.Clear();
        foreach (var name in names)
            SupplierFilters.Add(name);

        if (!names.Contains(current))
            SelectedSupplierFilter = "كل الموردين";
    }

    private void ClearFilters()
    {
        searchText = "";
        Changed(nameof(SearchText));
        selectedDatePreset = "الكل";
        Changed(nameof(SelectedDatePreset));
        Changed(nameof(IsAllDatePreset));
        Changed(nameof(IsTodayPreset));
        Changed(nameof(IsLast7DaysPreset));
        Changed(nameof(IsThisMonthPreset));
        Changed(nameof(IsCustomDatePreset));
        selectedSupplierFilter = "كل الموردين";
        Changed(nameof(SelectedSupplierFilter));
        selectedPaymentFilter = "الكل";
        Changed(nameof(SelectedPaymentFilter));
        selectedStatusFilter = "الكل";
        Changed(nameof(SelectedStatusFilter));
        selectedSortOption = "الأحدث أولًا";
        Changed(nameof(SelectedSortOption));

        RefreshFilteredInvoices();
        SetStatus("تم مسح كافة الفلاتر وعرض جميع فواتير المشتريات.", false);
    }

    private void OpenDetails(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        var detailsVm = new PurchaseInvoiceDetailsViewModel(
            invoice,
            sharedSuppliers,
            sharedRegistrations,
            OpenPrintPreview,
            OpenPayRemainingDialog,
            OpenEditDialog,
            OpenReturnDialog,
            RequestCancelInvoice,
            RequestDeleteInvoice,
            SettleCredit,
            id => sharedPurchaseInvoices.FirstOrDefault(i => i.Id == id),
            () => (StatusMessage, IsErrorStatus));

        OpenDetailsDialogRequested?.Invoke(detailsVm);
        RefreshFilteredInvoices();
    }

    private void OpenPrintPreview(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        var vm = new PurchaseInvoicePrintPreviewViewModel(invoice, modeLabel =>
        {
            SetStatus($"تمت معاينة طباعة فاتورة المشتريات {invoice.InvoiceNumber} ({modeLabel}).", false);
        });

        OpenPrintPreviewRequested?.Invoke(vm);
    }

    private void OpenPayRemainingDialog(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null || !invoice.CanPayRemaining) { SetStatus("لا يوجد دين متاح للسداد على هذه الفاتورة.", true); return; }
        var vm = new PaySupplierInvoiceViewModel(invoice);
        SetStatus("لم يتم تسجيل دفعة جديدة.", false);
        OpenPaySupplierDialogRequested?.Invoke(vm);
        if (vm.Result is not { } sub) return;
        RunPosting(invoice, repo =>
        {
            repo.PayInvoice(new(invoice.Id, invoice.DatabaseRowVersion, sub.CashPaid, sub.VodafoneCashPaid, sub.InstaPayPaid, sub.Notes));
            return $"تم تسجيل سداد {PosFormatting.FormatCurrency(sub.CashPaid + sub.VodafoneCashPaid + sub.InstaPayPaid)} للمورد.";
        });
    }

    private void OpenEditDialog(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null || !invoice.CanEdit) { SetStatus("حالة الفاتورة لا تسمح بالتعديل.", true); return; }
        var vm = new EditPurchaseInvoiceViewModel(invoice, sharedCatalog, sharedRegistrations);
        SetStatus("لم يتم حفظ تعديل جديد.", false);
        OpenEditDialogRequested?.Invoke(vm);
        if (vm.Result is not { } sub) return;
        RunPosting(invoice, repo =>
        {
            repo.UpdateInvoice(new(invoice.Id, invoice.DatabaseRowVersion, invoice.SupplierId, sub.SupplierInvoiceNumber,
                sub.PurchaseDate.Date.Add(invoice.IssuedAt.TimeOfDay), sub.Notes, sub.DiscountAmount,
                sub.Lines.Select(l => new PurchaseLineRequest(l.Product.Id, l.NewQuantity, l.NewUnitPurchaseCost, l.ExistingLine?.DatabaseLineId)).ToList()));
            return $"تم تعديل المستند {invoice.InvoiceNumber} دون صرف أو قبض.";
        });
    }

    private void OpenReturnDialog(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null || !invoice.CanReturn) { SetStatus("لا توجد بنود قابلة للإرجاع أو أن الفاتورة تحتاج مراجعة.", true); return; }
        var vm = new PurchaseReturnViewModel(invoice, sharedRegistrations);
        SetStatus("لم يتم تسجيل مرتجع جديد.", false);
        OpenReturnDialogRequested?.Invoke(vm);
        if (vm.Result is not { } sub) return;
        RunPosting(invoice, repo => ReturnSummary(repo.ReturnToSupplier(new(invoice.Id, invoice.DatabaseRowVersion,
            sub.Lines.Select(l => new PurchaseReturnLineInput(l.Line.DatabaseLineId, l.ReturnQuantity)).ToList(),
            string.IsNullOrWhiteSpace(sub.Notes) ? sub.Reason : $"{sub.Reason} — {sub.Notes}", sub.PreferDamagedFirst))));
    }

    private static string ReturnSummary(PurchaseReturnResult r) =>
        $"{r.ReturnNumber} · خفض صافي الفاتورة {r.ReturnedValue:0.00}، خفض الدين {r.DebtReduction:0.00}، رصيد جديد لنا عند المورد {r.CreditIssued:0.00}. لم يُسجّل قبض.";

    private void RequestCancelInvoice(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null || !invoice.CanCancel) return;
        SetStatus("لم تُلغَ الفاتورة.", false);
        OpenConfirmDialogRequested?.Invoke(new ConfirmActionViewModel("إلغاء فاتورة الشراء", invoice.InvoiceNumber,
            "ستُسحب القطع المتبقية ويُلغى الدين. أي فائض مسدد يصدر كإشعار للمحل عند المورد، وليس كقبض نقدي. هل تريد المتابعة؟", "تأكيد الإلغاء"),
            () => RunPosting(invoice, repo => ReturnSummary(repo.CancelInvoice(invoice.Id, invoice.DatabaseRowVersion))));
    }

    private bool RequestDeleteInvoice(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return false;
        var deleted = false;
        SetStatus("لم تُحذف الفاتورة.", false);
        OpenConfirmDialogRequested?.Invoke(new ConfirmActionViewModel("حذف مستند بلا تسوية", invoice.InvoiceNumber,
            "يُرفض الحذف عند وجود سداد أو مرتجع أو مقاصة أو قطع غير متاحة. هل تريد حذف المستند وسحب وحداته؟", "تأكيد الحذف"),
            () => deleted = RunPosting(invoice, repo => { repo.DeleteInvoice(invoice.Id, invoice.DatabaseRowVersion); return "تم حذف المستند غير المسدد."; }));
        return deleted;
    }

    private void SettleCredit(PurchaseInvoiceDesignItem invoice)
    {
        if (repository is null || !invoice.CanSettleCredit) { SetStatus("لا يوجد إشعار متاح للتسوية.", true); return; }
        SupplierCreditDialogViewModel vm;
        try { vm = new(invoice, sharedPurchaseInvoices, repository.LoadReceiptAccounts()); }
        catch (Exception ex) { SetStatus(ex.Message, true); return; }
        SetStatus("لم تُسجّل مقاصة أو إيصال جديد.", false);
        OpenSupplierCreditRequested?.Invoke(vm);
        if (vm.Result is not { } r) return;
        RunPosting(invoice, repo =>
        {
            if (r.Target is { } target)
            {
                repo.ApplyCredit(target.Id, target.DatabaseRowVersion, r.Credit, r.Amount);
                return $"تمت مقاصة {r.Amount:0.00} على {target.InvoiceNumber} دون حركة مالية.";
            }
            repo.ReceiveCredit(invoice.Id, invoice.DatabaseRowVersion, r.Credit, r.Amount, r.Account!.Id, r.Evidence);
            return $"تم تسجيل قبض {r.Amount:0.00} في {r.Account.Label} بمرجع {r.Evidence}.";
        });
    }

    // A failed post is never queued. A failed readback is not a reason to replay a committed post.
    private bool RunPosting(PurchaseInvoiceDesignItem invoice, Func<PurchaseRepository, string> write)
    {
        if (repository is null) { SetStatus("الحفظ يحتاج اتصالًا بقاعدة البيانات.", true); return false; }
        string message;
        try { message = write(repository); }
        catch (Exception ex) { SetStatus(ex is PurchaseDataException ? ex.Message : "تعذر تأكيد الحفظ. حدّث السجل قبل أي محاولة أخرى.", true); return false; }
        try
        {
            if (!RefreshFromDatabase()) throw new PurchaseDataException(StatusMessage);
            SelectedInvoice = sharedPurchaseInvoices.FirstOrDefault(i => i.Id == invoice.Id);
            notifyInventoryChanged?.Invoke();
            notifyReturnExchangeChanged?.Invoke();
            notifyPurchaseEntryChanged?.Invoke();
            SetStatus(message, false);
        }
        catch (Exception) { SetStatus($"{message} تعذر تحديث كل الشاشات. أعد تحميل السجل ولا تكرر العملية.", true); }
        return true;
    }

    private void RefreshFilteredInvoices()
    {
        var query = SearchText.Trim();
        var today = DateTime.Today;

        var matches = sharedPurchaseInvoices.Where(inv =>
        {
            // Date filter
            if (IsTodayPreset && inv.IssuedAt.Date != today)
                return false;
            if (IsLast7DaysPreset && inv.IssuedAt.Date < today.AddDays(-6))
                return false;
            if (IsThisMonthPreset && (inv.IssuedAt.Year != today.Year || inv.IssuedAt.Month != today.Month))
                return false;
            if (IsCustomDatePreset)
            {
                if (CustomFromDate.HasValue && inv.IssuedAt.Date < CustomFromDate.Value.Date)
                    return false;
                if (CustomToDate.HasValue && inv.IssuedAt.Date > CustomToDate.Value.Date)
                    return false;
            }

            // Supplier filter
            if (SelectedSupplierFilter != "كل الموردين" &&
                !inv.SupplierName.Equals(SelectedSupplierFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            // Payment channel filter
            if (SelectedPaymentFilter != "الكل")
            {
                var payMatch = SelectedPaymentFilter switch
                {
                    "نقدي" => inv.CashPaid > 0m && inv.VodafoneCashPaid == 0m && inv.InstaPayPaid == 0m && !inv.HasRemainingPayable,
                    "فودافون كاش" => inv.VodafoneCashPaid > 0m && inv.CashPaid == 0m && inv.InstaPayPaid == 0m && !inv.HasRemainingPayable,
                    "إنستاباي" => inv.InstaPayPaid > 0m && inv.CashPaid == 0m && inv.VodafoneCashPaid == 0m && !inv.HasRemainingPayable,
                    "دفع مقسم" => (inv.CashPaid > 0m ? 1 : 0) + (inv.VodafoneCashPaid > 0m ? 1 : 0) + (inv.InstaPayPaid > 0m ? 1 : 0) > 1,
                    "آجل / متبقي" => inv.HasRemainingPayable,
                    _ => true
                };
                if (!payMatch) return false;
            }

            // Status filter
            if (SelectedStatusFilter != "الكل" && inv.StatusTitle != SelectedStatusFilter)
                return false;

            // Search query
            if (!string.IsNullOrEmpty(query))
            {
                var hit = inv.InvoiceNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || inv.SupplierInvoiceNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || inv.SupplierName.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || inv.SupplierPhone.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || inv.SupplierCompany.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || inv.BuyerName.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || inv.Items.Any(i =>
                              i.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase)
                              || i.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                              || i.SerialNumbers.Any(sn => sn.Contains(query, StringComparison.OrdinalIgnoreCase)));
                if (!hit) return false;
            }

            return true;
        });

        IEnumerable<PurchaseInvoiceDesignItem> ordered = SelectedSortOption switch
        {
            "الأقدم أولًا" => matches.OrderBy(i => i.IssuedAt).ThenBy(i => i.Id),
            "الأعلى قيمة" => matches.OrderByDescending(i => i.NetTotal).ThenByDescending(i => i.IssuedAt),
            "الأعلى آجل متبقي" => matches.OrderByDescending(i => i.RemainingPayable).ThenByDescending(i => i.IssuedAt),
            _ => matches.OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id)
        };

        var orderedList = ordered.ToList();
        FilteredInvoices.Clear();
        foreach (var item in orderedList)
            FilteredInvoices.Add(item);

        if (SelectedInvoice is not null && !FilteredInvoices.Contains(SelectedInvoice))
            SelectedInvoice = null;

        Changed(nameof(HasNoFilteredInvoices));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
