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
            RequestDeleteInvoice);

        OpenDetailsDialogRequested?.Invoke(detailsVm);
        RefreshFilteredInvoices();
    }

    private void OpenPrintPreview(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        var vm = new PurchaseInvoicePrintPreviewViewModel(invoice, modeLabel =>
        {
            invoice.ActivityLog.Add(new PurchaseInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "معاينة / طباعة فاتورة شراء",
                Details = $"معاينة بصيغة «{modeLabel}».",
                ActorName = currentUserName
            });
            SetStatus($"تمت معاينة طباعة فاتورة المشتريات {invoice.InvoiceNumber} ({modeLabel}).", false);
        });

        OpenPrintPreviewRequested?.Invoke(vm);
    }

    private void OpenPayRemainingDialog(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        if (!invoice.CanPayRemaining)
        {
            SetStatus($"لا يوجد رصيد آجل متبقٍ للسداد على الفاتورة {invoice.InvoiceNumber}.", true);
            return;
        }

        var vm = new PaySupplierInvoiceViewModel(invoice);
        OpenPaySupplierDialogRequested?.Invoke(vm);
        if (vm.Result is not { } sub) return;

        if (repository is not null)
        {
            try
            {
                repository.PayInvoice(new PurchasePaymentRequest(invoice.Id, sub.CashPaid, sub.VodafoneCashPaid,
                    sub.InstaPayPaid, sub.Notes));
            }
            catch (Exception ex)
            {
                SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر تسجيل السداد في قاعدة البيانات.", true);
                return;
            }

            var paidInDatabase = sub.CashPaid + sub.VodafoneCashPaid + sub.InstaPayPaid;
            AfterDatabaseWrite(invoice.Id);
            SetStatus($"تم تسجيل سداد {PosFormatting.FormatCurrency(paidInDatabase)} للمورد على الفاتورة {invoice.InvoiceNumber} في قاعدة البيانات.", false);
            return;
        }

        var oldRemaining = invoice.RemainingPayable;
        invoice.CashPaid += sub.CashPaid;
        invoice.VodafoneCashPaid += sub.VodafoneCashPaid;
        invoice.InstaPayPaid += sub.InstaPayPaid;
        invoice.RecalculateStatusFromBalances();
        invoice.NotifyFinancialsChanged();

        var paidNow = sub.CashPaid + sub.VodafoneCashPaid + sub.InstaPayPaid;
        var creditReduced = Math.Max(0m, oldRemaining - invoice.RemainingPayable);
        var supplier = FindSupplierForInvoice(invoice);
        if (supplier is not null)
            supplier.OutstandingPayable = Math.Max(0m, supplier.OutstandingPayable - creditReduced);

        var channels = new List<string>(3);
        if (sub.CashPaid > 0m) channels.Add($"كاش {PosFormatting.FormatCurrency(sub.CashPaid)}");
        if (sub.VodafoneCashPaid > 0m) channels.Add($"فودافون كاش {PosFormatting.FormatCurrency(sub.VodafoneCashPaid)}");
        if (sub.InstaPayPaid > 0m) channels.Add($"إنستاباي {PosFormatting.FormatCurrency(sub.InstaPayPaid)}");

        var details = $"سداد دفعة للمورد ({string.Join(" + ", channels)})";
        if (!string.IsNullOrWhiteSpace(sub.Notes))
            details += $" — {sub.Notes}";
        if (invoice.RemainingPayable > 0m)
            details += $" · المتبقي: {invoice.RemainingPayableDisplay}";

        invoice.ActivityLog.Add(new PurchaseInvoiceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = invoice.RemainingPayable <= 0m ? "سداد كامل الرصيد للمورد" : "سداد دفعة من الآجل للمورد",
            Details = details,
            ActorName = currentUserName,
            Amount = paidNow
        });

        NotifyCrossScreenSync();
        RefreshFilteredInvoices();
        SetStatus($"تم تسجيل سداد {PosFormatting.FormatCurrency(paidNow)} للمورد على الفاتورة {invoice.InvoiceNumber}.", false);
    }

    private void OpenEditDialog(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        if (!invoice.CanEdit)
        {
            SetStatus($"لا يمكن تعديل الفاتورة {invoice.InvoiceNumber} لأنها {invoice.StatusTitle}.", true);
            return;
        }

        var vm = new EditPurchaseInvoiceViewModel(invoice, sharedSuppliers, sharedCatalog, sharedRegistrations);
        OpenEditDialogRequested?.Invoke(vm);
        if (vm.Result is not { } sub) return;

        if (repository is not null)
        {
            var request = new PurchaseInvoiceEdit(
                invoice.Id,
                sub.Supplier.Id,
                sub.SupplierInvoiceNumber,
                sub.Notes,
                sub.DiscountAmount,
                sub.CashPaid,
                sub.VodafoneCashPaid,
                sub.InstaPayPaid,
                sub.Lines.Select(l => new PurchaseLineEdit(
                    l.ExistingLine?.DatabaseLineId,
                    l.Product.Id,
                    l.Product.Barcode,
                    l.Product.Name,
                    l.Product.Category,
                    l.Product.UnitDisplay,
                    l.Product.TrackSerialNumber,
                    l.Quantity,
                    l.UnitPurchaseCost)).ToList());

            try
            {
                repository.UpdateInvoice(request);
            }
            catch (Exception ex)
            {
                SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر حفظ تعديلات الفاتورة في قاعدة البيانات.", true);
                return;
            }

            AfterDatabaseWrite(invoice.Id);
            SetStatus($"تم حفظ تعديلات الفاتورة {invoice.InvoiceNumber} في قاعدة البيانات ومزامنة المخزون وحساب المورد.", false);
            return;
        }

        var oldSupplier = FindSupplierForInvoice(invoice);
        var oldRemaining = invoice.RemainingPayable;
        var nextRegId = sharedRegistrations.Count > 0 ? sharedRegistrations.Max(r => r.Id) + 1 : 7001;
        var affectedProductIds = new HashSet<int>();

        // 1. Update invoice header & supplier snapshot
        invoice.SupplierId = sub.Supplier.Id;
        invoice.SupplierName = sub.Supplier.Name;
        invoice.SupplierPhone = sub.Supplier.Phone;
        invoice.SupplierCompany = sub.Supplier.CompanyName;
        invoice.SupplierAddress = sub.Supplier.Address;
        invoice.SupplierInvoiceNumber = sub.SupplierInvoiceNumber;
        invoice.IssuedAt = sub.PurchaseDate.Date.Add(invoice.IssuedAt.TimeOfDay);
        invoice.Notes = sub.Notes;
        invoice.DiscountAmount = sub.DiscountAmount;
        invoice.CashPaid = sub.CashPaid;
        invoice.VodafoneCashPaid = sub.VodafoneCashPaid;
        invoice.InstaPayPaid = sub.InstaPayPaid;

        // 2. Remove deleted lines (guaranteed CanRemove = no sold/damaged/returned units)
        var keptExistingLines = new HashSet<PurchaseInvoiceLineDesignItem>(
            sub.Lines.Where(l => l.ExistingLine is not null).Select(l => l.ExistingLine!));

        foreach (var removedLine in invoice.Items.Where(i => !keptExistingLines.Contains(i)).ToList())
        {
            affectedProductIds.Add(removedLine.ProductId);
            foreach (var regId in removedLine.UnitRegistrationIds)
            {
                var reg = sharedRegistrations.FirstOrDefault(r => r.Id == regId && r.IsAvailable);
                if (reg is not null)
                    sharedRegistrations.Remove(reg);
            }
            invoice.Items.Remove(removedLine);
        }

        // 3. Apply updates to existing lines and create new lines
        var lineNumber = 1;
        foreach (var lineSub in sub.Lines)
        {
            affectedProductIds.Add(lineSub.Product.Id);

            if (lineSub.ExistingLine is { } existingLine)
            {
                existingLine.LineNumber = lineNumber++;
                var lineRegs = sharedRegistrations
                    .Where(r => existingLine.UnitRegistrationIds.Contains(r.Id))
                    .ToList();

                var currentRegCount = lineRegs.Count;
                var targetActiveCount = Math.Max(0, lineSub.NewQuantity - existingLine.ReturnedQuantity);
                var updatedIds = existingLine.UnitRegistrationIds.ToList();
                var updatedSerials = existingLine.SerialNumbers.ToList();

                if (targetActiveCount > currentRegCount)
                {
                    var addCount = targetActiveCount - currentRegCount;
                    for (var i = 0; i < addCount; i++)
                    {
                        var regId = nextRegId++;
                        var serial = lineSub.Product.TrackSerialNumber
                            ? $"SN-{invoice.Id:000}-{regId}"
                            : "";
                        var newReg = new InventoryUnitRegistrationItem
                        {
                            Id = regId,
                            ProductId = lineSub.Product.Id,
                            ProductName = lineSub.Product.Name,
                            ProductBarcode = lineSub.Product.Barcode,
                            PurchasePrice = lineSub.NewUnitPurchaseCost,
                            SupplierName = sub.Supplier.Name,
                            RegisteredAt = invoice.IssuedAt.Date,
                            Status = InventoryUnitStatus.Available,
                            PurchaseInvoiceId = invoice.InvoiceNumber,
                            SerialNumber = serial
                        };
                        sharedRegistrations.Add(newReg);
                        lineRegs.Add(newReg);
                        updatedIds.Add(regId);
                        if (!string.IsNullOrEmpty(serial))
                            updatedSerials.Add(serial);
                    }
                }
                else if (targetActiveCount < currentRegCount)
                {
                    var removeCount = currentRegCount - targetActiveCount;
                    var removableAvailable = lineRegs
                        .Where(r => r.IsAvailable)
                        .OrderByDescending(r => r.Id)
                        .Take(removeCount)
                        .ToList();

                    foreach (var rem in removableAvailable)
                    {
                        sharedRegistrations.Remove(rem);
                        updatedIds.Remove(rem.Id);
                        if (!string.IsNullOrWhiteSpace(rem.SerialNumber))
                            updatedSerials.Remove(rem.SerialNumber);
                        lineRegs.Remove(rem);
                    }
                }

                // Sync cost, supplier & date onto surviving registrations of this line
                foreach (var reg in lineRegs)
                {
                    reg.PurchasePrice = lineSub.NewUnitPurchaseCost;
                    reg.SupplierName = sub.Supplier.Name;
                }

                existingLine.UnitRegistrationIds = updatedIds;
                existingLine.SerialNumbers = updatedSerials;
                existingLine.Quantity = lineSub.NewQuantity;
                existingLine.UnitPurchaseCost = lineSub.NewUnitPurchaseCost;
                lineSub.Product.CostPrice = lineSub.NewUnitPurchaseCost;
            }
            else
            {
                var claimedIds = new List<int>(lineSub.NewQuantity);
                var serials = new List<string>();
                for (var i = 0; i < lineSub.NewQuantity; i++)
                {
                    var regId = nextRegId++;
                    var serial = lineSub.Product.TrackSerialNumber
                        ? $"SN-{invoice.Id:000}-{regId}"
                        : "";
                    sharedRegistrations.Add(new InventoryUnitRegistrationItem
                    {
                        Id = regId,
                        ProductId = lineSub.Product.Id,
                        ProductName = lineSub.Product.Name,
                        ProductBarcode = lineSub.Product.Barcode,
                        PurchasePrice = lineSub.NewUnitPurchaseCost,
                        SupplierName = sub.Supplier.Name,
                        RegisteredAt = invoice.IssuedAt.Date,
                        Status = InventoryUnitStatus.Available,
                        PurchaseInvoiceId = invoice.InvoiceNumber,
                        SerialNumber = serial
                    });
                    claimedIds.Add(regId);
                    if (!string.IsNullOrEmpty(serial))
                        serials.Add(serial);
                }

                lineSub.Product.CostPrice = lineSub.NewUnitPurchaseCost;
                invoice.Items.Add(new PurchaseInvoiceLineDesignItem
                {
                    LineNumber = lineNumber++,
                    ProductId = lineSub.Product.Id,
                    Barcode = lineSub.Product.Barcode,
                    ProductName = lineSub.Product.Name,
                    Category = lineSub.Product.Category,
                    Unit = lineSub.Product.UnitDisplay,
                    TrackSerialNumber = lineSub.Product.TrackSerialNumber,
                    UnitRegistrationIds = claimedIds,
                    SerialNumbers = serials,
                    Quantity = lineSub.NewQuantity,
                    UnitPurchaseCost = lineSub.NewUnitPurchaseCost
                });
            }
        }

        // 4. Sync catalog stock counts
        SyncCatalogStockCounts(affectedProductIds);

        // 5. Recalculate invoice status & supplier payables
        invoice.RecalculateStatusFromBalances();
        invoice.NotifyFinancialsChanged();

        var newRemaining = invoice.RemainingPayable;
        var newSupplier = sub.Supplier;
        if (oldSupplier is not null && ReferenceEquals(oldSupplier, newSupplier))
        {
            oldSupplier.OutstandingPayable = Math.Max(0m, oldSupplier.OutstandingPayable - oldRemaining + newRemaining);
        }
        else
        {
            if (oldSupplier is not null)
            {
                oldSupplier.OutstandingPayable = Math.Max(0m, oldSupplier.OutstandingPayable - oldRemaining);
                oldSupplier.TotalPurchasesCount = Math.Max(0, oldSupplier.TotalPurchasesCount - 1);
            }

            var trackedNewSupplier = sharedSuppliers.FirstOrDefault(s => s.Id == newSupplier.Id || s.Name == newSupplier.Name);
            if (trackedNewSupplier is not null)
            {
                trackedNewSupplier.OutstandingPayable += newRemaining;
                trackedNewSupplier.TotalPurchasesCount += 1;
            }
        }

        invoice.ActivityLog.Add(new PurchaseInvoiceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "تعديل شامل لفاتورة الشراء",
            Details = $"المورد: {invoice.SupplierName} · {invoice.ItemsAndUnitsSummary} · الصافي: {invoice.NetTotalDisplay} · {invoice.PaymentMethodSummary}.",
            ActorName = currentUserName,
            Amount = invoice.NetTotal
        });

        NotifyCrossScreenSync();
        RebuildSupplierFilters();
        RefreshFilteredInvoices();
        SetStatus($"تم حفظ تعديلات الفاتورة {invoice.InvoiceNumber} ومزامنة المخزون وحساب المورد «{invoice.SupplierName}».", false);
    }

    private void OpenReturnDialog(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        if (!invoice.CanReturn)
        {
            SetStatus($"لا توجد قطع قابلة للإرجاع في الفاتورة {invoice.InvoiceNumber}.", true);
            return;
        }

        var vm = new PurchaseReturnViewModel(invoice, sharedRegistrations);
        if (!vm.Lines.Any(l => l.MaxReturnable > 0))
        {
            SetStatus($"جميع القطع غير المرتجعة في الفاتورة {invoice.InvoiceNumber} تم بيعها بالفعل للعملاء ولا يمكن إرجاعها للمورد.", true);
            return;
        }

        OpenReturnDialogRequested?.Invoke(vm);
        if (vm.Result is not { } sub) return;

        if (repository is not null)
        {
            var lines = sub.Lines
                .Where(l => l.Line.DatabaseLineId > 0)
                .Select(l => new PurchaseReturnLineInput(l.Line.DatabaseLineId, l.ReturnQuantity))
                .ToList();

            if (lines.Count == 0)
            {
                SetStatus("لا يمكن تسجيل المرتجع: البنود غير محفوظة في قاعدة البيانات. أعد تحميل الشاشة.", true);
                return;
            }

            try
            {
                var result = repository.ReturnToSupplier(new PurchaseReturnRequest(
                    invoice.Id, lines, sub.Reason, sub.Notes, sub.PreferDamagedFirst, sub.RefundChannel));

                AfterDatabaseWrite(invoice.Id);
                var settlement = result.RefundReceived > 0m
                    ? $"خصم {PosFormatting.FormatCurrency(result.CreditApplied)} من الآجل واسترداد {PosFormatting.FormatCurrency(result.RefundReceived)} ({sub.RefundChannel})"
                    : result.CreditApplied > 0m
                        ? $"خصم {PosFormatting.FormatCurrency(result.CreditApplied)} من الآجل للمورد"
                        : "تسوية رصيد عند المورد لاحقًا";

                SetStatus(
                    $"تم حفظ المرتجع {result.ReturnNumber}: {result.ReturnedUnits} قطعة بقيمة {PosFormatting.FormatCurrency(result.ReturnedValue)} للمورد «{invoice.SupplierName}» في قاعدة البيانات ({settlement}).",
                    false);
                return;
            }
            catch (Exception ex)
            {
                SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر حفظ المرتجع في قاعدة البيانات.", true);
                return;
            }
        }

        var oldRemaining = invoice.RemainingPayable;
        var affectedProductIds = new HashSet<int>();
        var totalReturnedUnits = 0;
        var totalReturnValue = 0m;
        var returnedSummaries = new List<string>();

        foreach (var req in sub.Lines)
        {
            var line = req.Line;
            affectedProductIds.Add(line.ProductId);

            var nonSoldRegs = sharedRegistrations
                .Where(r => line.UnitRegistrationIds.Contains(r.Id) && !r.IsSold)
                .OrderBy(r => sub.PreferDamagedFirst
                    ? (r.IsDamaged ? 0 : 1)
                    : (r.IsAvailable ? 0 : 1))
                .ThenByDescending(r => r.Id)
                .Take(req.ReturnQuantity)
                .ToList();

            var updatedIds = line.UnitRegistrationIds.ToList();
            var updatedSerials = line.SerialNumbers.ToList();

            foreach (var reg in nonSoldRegs)
            {
                sharedRegistrations.Remove(reg);
                updatedIds.Remove(reg.Id);
                if (!string.IsNullOrWhiteSpace(reg.SerialNumber))
                    updatedSerials.Remove(reg.SerialNumber);
            }

            line.UnitRegistrationIds = updatedIds;
            line.SerialNumbers = updatedSerials;
            line.ReturnedQuantity += req.ReturnQuantity;

            totalReturnedUnits += req.ReturnQuantity;
            totalReturnValue += req.ReturnQuantity * line.UnitPurchaseCost;
            returnedSummaries.Add($"{req.ReturnQuantity}× {line.ProductName}");
        }

        SyncCatalogStockCounts(affectedProductIds);

        invoice.SupplierRefundReceived += sub.CashRefundReceived;
        invoice.RecalculateStatusFromBalances();
        invoice.NotifyFinancialsChanged();

        var creditReduction = Math.Max(0m, oldRemaining - invoice.RemainingPayable);
        var supplier = FindSupplierForInvoice(invoice);
        if (supplier is not null)
            supplier.OutstandingPayable = Math.Max(0m, supplier.OutstandingPayable - creditReduction);

        var settlementNote = creditReduction > 0m && sub.CashRefundReceived > 0m
            ? $"خصم {PosFormatting.FormatCurrency(creditReduction)} من الآجل + استرداد {PosFormatting.FormatCurrency(sub.CashRefundReceived)} ({sub.RefundChannel})"
            : creditReduction > 0m
                ? $"خصم {PosFormatting.FormatCurrency(creditReduction)} من الآجل للمورد"
                : sub.CashRefundReceived > 0m
                    ? $"استرداد {PosFormatting.FormatCurrency(sub.CashRefundReceived)} من المورد ({sub.RefundChannel})"
                    : "تسوية رصيد عند المورد لاحقًا";

        var noteSuffix = string.IsNullOrWhiteSpace(sub.Notes) ? "" : $" — {sub.Notes}";
        invoice.ActivityLog.Add(new PurchaseInvoiceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = invoice.IsFullyReturned ? "مرتجع كلي للمورد" : "مرتجع جزئي للمورد",
            Details = $"إرجاع {totalReturnedUnits} قطعة ({string.Join("، ", returnedSummaries)}) · السبب: {sub.Reason} · {settlementNote}{noteSuffix}.",
            ActorName = currentUserName,
            Amount = totalReturnValue
        });

        NotifyCrossScreenSync();
        RefreshFilteredInvoices();
        SetStatus(
            $"تم تسجيل مرتجع {totalReturnedUnits} قطعة للمورد «{invoice.SupplierName}» بقيمة {PosFormatting.FormatCurrency(totalReturnValue)} ({settlementNote}).",
            false);
    }

    private void RequestCancelInvoice(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        if (!invoice.CanCancel)
        {
            SetStatus($"الفاتورة {invoice.InvoiceNumber} في حالة «{invoice.StatusTitle}» ولا يمكن إلغاؤها.", true);
            return;
        }

        var soldCount = CountSoldUnitsForInvoice(invoice);
        if (soldCount > 0)
        {
            SetStatus(
                $"لا يمكن إلغاء الفاتورة {invoice.InvoiceNumber} بالكامل لوجود {soldCount} قطعة مباعة للعملاء منها؛ استخدم «مرتجع للمورد» للقطع غير المباعة.",
                true);
            return;
        }

        var confirmVm = new ConfirmActionViewModel(
            "تأكيد إلغاء فاتورة الشراء",
            $"{invoice.InvoiceNumber} — {invoice.SupplierName}",
            $"سيتم وسم الفاتورة {invoice.InvoiceNumber} بحالة «ملغاة» مع بقائها في السجل للمراجعة، وسحب {invoice.ActiveUnitsCount} قطعة غير مباعة من المخزون، وتصفير الرصيد الآجل المتبقي ({invoice.RemainingPayableDisplay}). هل تريد المتابعة؟",
            "تأكيد إلغاء الفاتورة");

        OpenConfirmDialogRequested?.Invoke(confirmVm, () =>
        {
            if (repository is not null)
            {
                try
                {
                    repository.CancelInvoice(invoice.Id);
                }
                catch (Exception ex)
                {
                    SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر إلغاء الفاتورة في قاعدة البيانات.", true);
                    return;
                }

                AfterDatabaseWrite(invoice.Id);
                SetStatus($"تم إلغاء الفاتورة {invoice.InvoiceNumber} في قاعدة البيانات والاحتفاظ بها في السجل وسحب مخزونها.", false);
                return;
            }

            var oldRemaining = invoice.RemainingPayable;
            var affectedProducts = RemoveNonSoldRegistrationsForInvoice(invoice);
            SyncCatalogStockCounts(affectedProducts);

            var supplier = FindSupplierForInvoice(invoice);
            if (supplier is not null)
                supplier.OutstandingPayable = Math.Max(0m, supplier.OutstandingPayable - oldRemaining);

            invoice.Status = PurchaseInvoiceStatus.Cancelled;
            invoice.NotifyFinancialsChanged();
            invoice.ActivityLog.Add(new PurchaseInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "إلغاء فاتورة الشراء",
                Details = $"تم إلغاء الفاتورة وسحب القطع المرتبطة بها من المخزون وتصفير الآجل ({PosFormatting.FormatCurrency(oldRemaining)}).",
                ActorName = currentUserName,
                Amount = invoice.NetTotal
            });

            NotifyCrossScreenSync();
            RefreshFilteredInvoices();
            SetStatus($"تم إلغاء الفاتورة {invoice.InvoiceNumber} والاحتفاظ بها في السجل وسحب مخزونها.", false);
        });
    }

    private bool RequestDeleteInvoice(PurchaseInvoiceDesignItem? invoice)
    {
        if (invoice is null) return false;
        SelectedInvoice = invoice;

        var soldCount = CountSoldUnitsForInvoice(invoice);
        if (soldCount > 0)
        {
            SetStatus(
                $"لا يمكن حذف الفاتورة {invoice.InvoiceNumber} نهائيًا لوجود {soldCount} قطعة مباعة للعملاء منها. استخدم «مرتجع للمورد» للقطع المتبقية.",
                true);
            return false;
        }

        var deleted = false;
        var confirmVm = new ConfirmActionViewModel(
            "تحذير: حذف فاتورة شراء نهائيًا",
            $"{invoice.InvoiceNumber} — {invoice.SupplierName}",
            $"سيتم حذف الفاتورة {invoice.InvoiceNumber} نهائيًا من السجل، وسحب أي قطع مخزونية مرتبطة بها، وتحديث حساب المورد ({invoice.RemainingPayableDisplay}). لا يمكن التراجع عن هذا الإجراء. هل أنت متأكد؟",
            "حذف نهائي من السجل");

        OpenConfirmDialogRequested?.Invoke(confirmVm, () =>
        {
            if (repository is not null)
            {
                try
                {
                    repository.DeleteInvoice(invoice.Id);
                }
                catch (Exception ex)
                {
                    SetStatus(ex is PurchaseDataException data ? data.Message : "تعذر حذف الفاتورة من قاعدة البيانات.", true);
                    return;
                }

                AfterDatabaseWrite(invoiceId: 0);
                SetStatus($"تم حذف الفاتورة {invoice.InvoiceNumber} نهائيًا من قاعدة البيانات وسحب مخزونها وتحديث حساب المورد.", false);
                return;
            }

            var oldRemaining = invoice.RemainingPayable;
            var affectedProducts = RemoveNonSoldRegistrationsForInvoice(invoice);
            SyncCatalogStockCounts(affectedProducts);

            var supplier = FindSupplierForInvoice(invoice);
            if (supplier is not null)
            {
                supplier.OutstandingPayable = Math.Max(0m, supplier.OutstandingPayable - oldRemaining);
                supplier.TotalPurchasesCount = Math.Max(0, supplier.TotalPurchasesCount - 1);
            }

            if (ReferenceEquals(SelectedInvoice, invoice))
                SelectedInvoice = null;

            sharedPurchaseInvoices.Remove(invoice);
            deleted = true;

            NotifyCrossScreenSync();
            RebuildSupplierFilters();
            RefreshFilteredInvoices();
            SetStatus($"تم حذف الفاتورة {invoice.InvoiceNumber} نهائيًا وسحب مخزونها وتحديث حساب المورد.", false);
        });

        return deleted;
    }

    /// <summary>
    /// After a SQL write: re-read the ledger, refresh the stock counts and tell the other screens.
    /// The reloaded rows are new instances, so the operator is put back on the same invoice id.
    /// </summary>
    private void AfterDatabaseWrite(int invoiceId)
    {
        RefreshFromDatabase();
        if (invoiceId > 0)
            SelectedInvoice = sharedPurchaseInvoices.FirstOrDefault(i => i.Id == invoiceId) ?? SelectedInvoice;

        SyncCatalogStockCounts(sharedCatalog.Select(p => p.Id));
        NotifyCrossScreenSync();
        RebuildSupplierFilters();
        RefreshFilteredInvoices();
    }

    private int CountSoldUnitsForInvoice(PurchaseInvoiceDesignItem invoice)
    {
        var ids = invoice.Items.SelectMany(i => i.UnitRegistrationIds).ToHashSet();
        if (ids.Count == 0) return 0;
        return sharedRegistrations.Count(r => ids.Contains(r.Id) && r.IsSold);
    }

    private HashSet<int> RemoveNonSoldRegistrationsForInvoice(PurchaseInvoiceDesignItem invoice)
    {
        var affectedProductIds = new HashSet<int>();
        foreach (var line in invoice.Items)
        {
            affectedProductIds.Add(line.ProductId);
            var removable = sharedRegistrations
                .Where(r => line.UnitRegistrationIds.Contains(r.Id) && !r.IsSold)
                .ToList();
            foreach (var reg in removable)
                sharedRegistrations.Remove(reg);
            line.UnitRegistrationIds = [];
            line.SerialNumbers = [];
        }

        return affectedProductIds;
    }

    private void SyncCatalogStockCounts(IEnumerable<int> productIds)
    {
        foreach (var productId in productIds)
        {
            var product = sharedCatalog.FirstOrDefault(p => p.Id == productId);
            if (product is null) continue;

            product.AvailableStock = sharedRegistrations.Count(r => r.ProductId == productId && r.IsAvailable);
            product.DamagedCount = sharedRegistrations.Count(r => r.ProductId == productId && r.IsDamaged);
        }
    }

    private SupplierDesignItem? FindSupplierForInvoice(PurchaseInvoiceDesignItem invoice)
    {
        return sharedSuppliers.FirstOrDefault(s => s.Id == invoice.SupplierId)
               ?? sharedSuppliers.FirstOrDefault(s => s.Name.Equals(invoice.SupplierName, StringComparison.OrdinalIgnoreCase));
    }

    private void NotifyCrossScreenSync()
    {
        notifyInventoryChanged?.Invoke();
        notifyReturnExchangeChanged?.Invoke();
        notifyPurchaseEntryChanged?.Invoke();
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
