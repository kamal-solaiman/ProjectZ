using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class InvoicesViewModel : ObservableObject
{
    private readonly ObservableCollection<SalesInvoiceDesignItem> sharedInvoices;
    private readonly ObservableCollection<PosProductDesignItem> sharedCatalog;
    private readonly ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations;
    private readonly ObservableCollection<ReturnExchangeOperationItem>? sharedReturnOperations;
    private readonly string currentCashierName;
    private readonly Action? navigateToPos;
    private readonly Action? notifyInventoryChanged;
    private readonly SalesRepository? repository;
    private Action? notifyReturnExchangeChanged;

    private int nextInvoiceId;
    private SalesInvoiceDesignItem? selectedInvoice;
    private string editableNotes = "";

    private string searchText = "";
    private string selectedDatePreset = "الكل";
    private DateTime? customFromDate = DateTime.Today.AddDays(-7);
    private DateTime? customToDate = DateTime.Today;
    private string selectedPaymentFilter = "الكل";
    private string selectedStatusFilter = "الكل";
    private string selectedCashierFilter = "كل الكاشيرية";

    private string statusMessage = "اضغط على أي فاتورة محفوظة لمراجعة تفاصيلها وسجل حركاتها والتحصيل والمرتجع.";
    private bool isErrorStatus;

    public ObservableCollection<SalesInvoiceDesignItem> FilteredInvoices { get; } = [];
    public ObservableCollection<string> CashierFilters { get; } = ["كل الكاشيرية"];

    public IReadOnlyList<string> DatePresets { get; } = ["الكل", "اليوم", "آخر 7 أيام", "هذا الشهر", "فترة مخصصة"];
    public IReadOnlyList<string> PaymentFilters { get; } = ["الكل", "نقدي", "فودافون كاش", "إنستاباي", "دفع مقسم", "آجل / متبقي"];
    public IReadOnlyList<string> StatusFilters { get; } = ["الكل", "مكتملة", "آجل / متبقي", "مرتجع جزئي", "مرتجع كلي", "مستبدلة / معدلة", "ملغاة"];

    public Action<InvoicePrintPreviewViewModel>? OpenPrintPreviewRequested { get; set; }
    public Action<InvoiceReturnViewModel>? OpenReturnDialogRequested { get; set; }
    public Action<CollectInvoicePaymentViewModel>? OpenCollectPaymentRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    public SalesInvoiceDesignItem? SelectedInvoice
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

            editableNotes = selectedInvoice?.Notes ?? "";
            Changed();
            Changed(nameof(HasSelectedInvoice));
            Changed(nameof(EditableNotes));
        }
    }

    public bool HasSelectedInvoice => SelectedInvoice is not null;

    public string EditableNotes
    {
        get => editableNotes;
        set
        {
            if (editableNotes == value) return;
            editableNotes = value;
            Changed();
        }
    }

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

    public string SelectedCashierFilter
    {
        get => selectedCashierFilter;
        set
        {
            if (selectedCashierFilter == value || value is null) return;
            selectedCashierFilter = value;
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

    public bool HasNoFilteredInvoices => FilteredInvoices.Count == 0;

    // KPI Summaries (computed on FilteredInvoices so date/cashier/status filters reflect live)
    public string InvoicesCountDisplay => $"{FilteredInvoices.Count} فاتورة";
    public string InvoicesBreakdownSummary
    {
        get
        {
            var completed = FilteredInvoices.Count(i => i.IsCompleted);
            var credit = FilteredInvoices.Count(i => i.HasRemaining);
            var returned = FilteredInvoices.Count(i => i.IsPartiallyReturned || i.IsFullyReturned);
            return $"مكتملة: {completed} · آجل: {credit} · مرتجع: {returned}";
        }
    }

    public decimal TotalNetSales => FilteredInvoices.Sum(i => i.EffectiveNetSales);
    public string TotalNetSalesDisplay => PosFormatting.FormatCurrency(TotalNetSales);
    public string TotalDiscountsSubtext => $"الخصومات: {PosFormatting.FormatCurrency(FilteredInvoices.Where(i => !i.IsCancelled && !i.IsFullyReturned).Sum(i => i.EffectiveDiscount))}";

    public decimal TotalCost => FilteredInvoices.Sum(i => i.TotalCost);
    public decimal TotalProfit => FilteredInvoices.Sum(i => i.NetProfit);
    public string TotalProfitDisplay => PosFormatting.FormatCurrency(TotalProfit);
    public string TotalCostAndMarginDisplay
    {
        get
        {
            var margin = TotalNetSales > 0m ? Math.Round((TotalProfit / TotalNetSales) * 100m, 1) : 0m;
            return $"التكلفة: {PosFormatting.FormatCurrency(TotalCost)} · هامش {margin.ToString("0.#", CultureInfo.InvariantCulture)}%";
        }
    }

    public decimal TotalCashCollected => FilteredInvoices.Where(i => !i.IsCancelled).Sum(i => Math.Max(0m, i.CashPaid - Math.Min(i.CashPaid, i.RefundedAmount)));
    public decimal TotalVodafoneCollected => FilteredInvoices.Where(i => !i.IsCancelled).Sum(i => i.VodafoneCashPaid);
    public decimal TotalInstaPayCollected => FilteredInvoices.Where(i => !i.IsCancelled).Sum(i => i.InstaPayPaid);
    public decimal TotalCollectedAll => FilteredInvoices.Sum(i => i.NetCollected);
    public string TotalCollectedAllDisplay => PosFormatting.FormatCurrency(TotalCollectedAll);
    public string ChannelsBreakdownDisplay =>
        $"كاش: {PosFormatting.FormatNumber(TotalCashCollected)} · فودافون: {PosFormatting.FormatNumber(TotalVodafoneCollected)} · إنستاباي: {PosFormatting.FormatNumber(TotalInstaPayCollected)}";

    public decimal TotalRemaining => FilteredInvoices.Sum(i => i.RemainingAmount);
    public decimal TotalReturned => FilteredInvoices.Sum(i => i.ReturnedTotal);
    public string TotalRemainingDisplay => PosFormatting.FormatCurrency(TotalRemaining);
    public string TotalReturnedSubtext => $"المرتجعات: {PosFormatting.FormatCurrency(TotalReturned)}";

    public ActionCommand GoToPosCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public RelayCommand<string> SelectDatePresetCommand { get; }
    public ActionCommand FilterCreditOnlyCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> SelectInvoiceCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> OpenPrintPreviewCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> OpenReturnDialogCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> OpenCollectPaymentCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> CancelInvoiceCommand { get; }
    public ActionCommand SaveNotesCommand { get; }

    public InvoicesViewModel()
        : this(CreateDefaultSeedInvoices(), null, null)
    {
    }

    private static (
        ObservableCollection<SalesInvoiceDesignItem> Invoices,
        ObservableCollection<PosProductDesignItem> Catalog,
        ObservableCollection<InventoryUnitRegistrationItem> Registrations) CreateDefaultSeedInvoices()
    {
        var catalog = PosDesignSeedData.CreateSharedSampleCatalog();
        var registrations = InventoryDesignSeedData.CreateSharedSampleRegistrations(catalog);
        var invoices = InvoiceDesignSeedData.CreateSharedSampleInvoices(catalog, registrations);
        return (invoices, catalog, registrations);
    }

    private InvoicesViewModel(
        (ObservableCollection<SalesInvoiceDesignItem> Invoices, ObservableCollection<PosProductDesignItem> Catalog, ObservableCollection<InventoryUnitRegistrationItem> Registrations) defaults,
        Action? navigateToPos,
        Action? notifyInventoryChanged)
        : this(defaults.Invoices, defaults.Catalog, defaults.Registrations, "المشرف", navigateToPos, notifyInventoryChanged)
    {
    }

    public InvoicesViewModel(
        ObservableCollection<SalesInvoiceDesignItem> sharedInvoices,
        ObservableCollection<PosProductDesignItem> sharedCatalog,
        ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations,
        string currentCashierName = "المشرف",
        Action? navigateToPos = null,
        Action? notifyInventoryChanged = null,
        ObservableCollection<ReturnExchangeOperationItem>? sharedReturnOperations = null,
        Action? notifyReturnExchangeChanged = null,
        SalesRepository? repository = null)
    {
        this.repository = repository;
        this.sharedInvoices = sharedInvoices;
        this.sharedCatalog = sharedCatalog;
        this.sharedRegistrations = sharedRegistrations;
        this.sharedReturnOperations = sharedReturnOperations;
        this.currentCashierName = currentCashierName;
        this.navigateToPos = navigateToPos;
        this.notifyInventoryChanged = notifyInventoryChanged;
        this.notifyReturnExchangeChanged = notifyReturnExchangeChanged;

        nextInvoiceId = sharedInvoices.Count > 0 ? sharedInvoices.Max(i => i.Id) + 1 : 1;

        GoToPosCommand = new ActionCommand(() => this.navigateToPos?.Invoke());
        ClearFiltersCommand = new ActionCommand(() =>
        {
            SearchText = "";
            SelectedDatePreset = "الكل";
            SelectedPaymentFilter = "الكل";
            SelectedStatusFilter = "الكل";
            SelectedCashierFilter = "كل الكاشيرية";
        });
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        SelectDatePresetCommand = new RelayCommand<string>(preset =>
        {
            if (!string.IsNullOrWhiteSpace(preset))
                SelectedDatePreset = preset;
        });
        FilterCreditOnlyCommand = new ActionCommand(() =>
        {
            SelectedStatusFilter = SelectedStatusFilter == "آجل / متبقي" ? "الكل" : "آجل / متبقي";
        });

        SelectInvoiceCommand = new RelayCommand<SalesInvoiceDesignItem>(inv =>
        {
            if (inv is not null)
                SelectedInvoice = inv;
        });

        OpenPrintPreviewCommand = new RelayCommand<SalesInvoiceDesignItem>(inv =>
            OpenPrintPreview(inv ?? SelectedInvoice));
        OpenReturnDialogCommand = new RelayCommand<SalesInvoiceDesignItem>(inv =>
            OpenReturnDialog(inv ?? SelectedInvoice));
        OpenCollectPaymentCommand = new RelayCommand<SalesInvoiceDesignItem>(inv =>
            OpenCollectPaymentDialog(inv ?? SelectedInvoice));
        CancelInvoiceCommand = new RelayCommand<SalesInvoiceDesignItem>(inv =>
            RequestCancelInvoice(inv ?? SelectedInvoice));

        SaveNotesCommand = new ActionCommand(SaveSelectedNotes);

        this.sharedInvoices.CollectionChanged += OnSharedInvoicesChanged;

        RebuildCashierFilters();
        RefreshFilteredInvoices();
        SelectedInvoice = FilteredInvoices.FirstOrDefault();
    }

    /// <summary>
    /// Persists a POS sale. With a repository the invoice, its lines, the serialized units, the payments
    /// and the financial ledger are written in one Serializable transaction; design mode keeps the old
    /// in-memory behaviour so the standalone previews still work.
    /// </summary>
    public SalesInvoiceDesignItem RecordCompletedPosSale(PosInvoiceViewModel posInvoice)
    {
        if (repository is not null)
            return PersistCompletedPosSale(posInvoice);

        var invoiceId = nextInvoiceId++;
        var invoiceNo = $"INV-2026-{invoiceId:0000}";

        var invoice = new SalesInvoiceDesignItem
        {
            Id = invoiceId,
            InvoiceNumber = invoiceNo,
            IssuedAt = DateTime.Now,
            CashierName = currentCashierName,
            CustomerId = posInvoice.SelectedCustomer.Id,
            CustomerName = posInvoice.SelectedCustomer.Name,
            CustomerPhone = posInvoice.SelectedCustomer.Phone,
            CustomerAddress = posInvoice.SelectedCustomer.Address,
            IsWalkInCustomer = posInvoice.SelectedCustomer.IsWalkIn,
            DefaultPriceTier = posInvoice.InvoicePriceTier,
            Status = SalesInvoiceStatus.Completed,
            DiscountAmount = posInvoice.EffectiveDiscount,
            CashPaid = posInvoice.CashAmount,
            VodafoneCashPaid = posInvoice.VodafoneCashAmount,
            InstaPayPaid = posInvoice.InstaPayAmount,
            Notes = posInvoice.HasChange
                ? $"تم تسليم الباقي للعميل ({posInvoice.ChangeAmountDisplay})."
                : ""
        };

        var lineNo = 1;
        foreach (var posLine in posInvoice.Items)
        {
            var claimedIds = new List<int>();
            var serials = new List<string>();
            var availableUnits = sharedRegistrations
                .Where(r => r.ProductId == posLine.Product.Id && r.IsAvailable)
                .OrderBy(r => r.Id)
                .Take(posLine.Quantity)
                .ToList();

            foreach (var unit in availableUnits)
            {
                unit.Status = InventoryUnitStatus.Sold;
                unit.SaleInvoiceNumber = invoiceNo;
                claimedIds.Add(unit.Id);
                if (!string.IsNullOrWhiteSpace(unit.SerialNumber))
                    serials.Add(unit.SerialNumber);
            }

            if (availableUnits.Count > 0)
            {
                var remainingRegCount = sharedRegistrations.Count(r => r.ProductId == posLine.Product.Id && r.IsAvailable);
                posLine.Product.AvailableStock = remainingRegCount;
            }
            else
            {
                posLine.Product.AvailableStock = Math.Max(0, posLine.Product.AvailableStock - posLine.Quantity);
            }

            var unitCost = availableUnits.Count > 0
                ? Math.Round(availableUnits.Average(u => u.PurchasePrice), 2)
                : posLine.Product.CostPrice;

            invoice.Items.Add(new SalesInvoiceLineDesignItem
            {
                LineNumber = lineNo++,
                ProductId = posLine.Product.Id,
                Barcode = posLine.Product.Barcode,
                ProductName = posLine.Product.Name,
                Category = posLine.Product.Category,
                Unit = posLine.Product.UnitDisplay,
                TrackSerialNumber = posLine.Product.TrackSerialNumber,
                UnitRegistrationIds = claimedIds,
                SerialNumbers = serials,
                PriceTier = posLine.SelectedPriceTier,
                Quantity = posLine.Quantity,
                UnitPrice = posLine.UnitPrice,
                UnitCost = unitCost
            });
        }

        invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = invoice.IssuedAt,
            ActionTitle = "إصدار فاتورة من نقطة البيع",
            Details = $"سداد: {invoice.PaymentMethodSummary} ({invoice.NetTotalDisplay}).",
            ActorName = currentCashierName,
            Amount = invoice.NetTotal
        });

        sharedInvoices.Insert(0, invoice);
        RebuildCashierFilters();
        RefreshFilteredInvoices();
        SelectedInvoice = invoice;
        notifyInventoryChanged?.Invoke();
        notifyReturnExchangeChanged?.Invoke();
        SetStatus($"تم استلام الفاتورة الجديدة {invoiceNo} من نقطة البيع بقيمة {invoice.NetTotalDisplay}.", false);
        return invoice;
    }

    private SalesInvoiceDesignItem PersistCompletedPosSale(PosInvoiceViewModel posInvoice)
    {
        var customer = posInvoice.SelectedCustomer;

        // Snapshot the cart before the SQL write: reloading stock afterwards must not change what was sold.
        var soldLines = posInvoice.Items
            .Select(item => new
            {
                item.Product,
                item.SelectedPriceTier,
                item.Quantity,
                item.UnitPrice
            })
            .ToList();

        var request = new PosSaleRequest(
            customer.IsWalkIn ? null : customer.Id,
            customer.Name,
            customer.Phone,
            customer.Address,
            customer.IsWalkIn,
            posInvoice.InvoicePriceTier,
            posInvoice.EffectiveDiscount,
            soldLines.Select(item => new PosSaleLineRequest(
                item.Product.Id,
                item.Product.Barcode,
                item.Product.Name,
                item.Product.Category,
                item.Product.UnitDisplay,
                item.Product.TrackSerialNumber,
                item.SelectedPriceTier,
                item.Quantity,
                item.UnitPrice,
                item.Product.CostPrice)).ToList(),
            posInvoice.CashAmount,
            posInvoice.VodafoneCashAmount,
            posInvoice.InstaPayAmount,
            posInvoice.HasChange
                ? $"تم تسليم الباقي للعميل ({posInvoice.ChangeAmountDisplay})."
                : "");

        var saved = repository!.CompleteSale(request);

        var invoice = new SalesInvoiceDesignItem
        {
            Id = saved.InvoiceId,
            InvoiceNumber = saved.InvoiceNumber,
            IssuedAt = DateTime.Now,
            CashierName = currentCashierName,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerPhone = customer.Phone,
            CustomerAddress = customer.Address,
            IsWalkInCustomer = customer.IsWalkIn,
            DefaultPriceTier = posInvoice.InvoicePriceTier,
            Status = saved.IsCredit ? SalesInvoiceStatus.PartialCredit : SalesInvoiceStatus.Completed,
            DiscountAmount = posInvoice.EffectiveDiscount,
            CashPaid = Math.Min(posInvoice.CashAmount, saved.NetTotal),
            VodafoneCashPaid = posInvoice.VodafoneCashAmount,
            InstaPayPaid = posInvoice.InstaPayAmount,
            Notes = request.Notes
        };

        var lineNo = 1;
        foreach (var posLine in soldLines)
        {
            invoice.Items.Add(new SalesInvoiceLineDesignItem
            {
                LineNumber = lineNo++,
                ProductId = posLine.Product.Id,
                Barcode = posLine.Product.Barcode,
                ProductName = posLine.Product.Name,
                Category = posLine.Product.Category,
                Unit = posLine.Product.UnitDisplay,
                TrackSerialNumber = posLine.Product.TrackSerialNumber,
                PriceTier = posLine.SelectedPriceTier,
                Quantity = posLine.Quantity,
                UnitPrice = posLine.UnitPrice,
                UnitCost = posLine.Product.CostPrice
            });
        }

        invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "إصدار فاتورة من نقطة البيع",
            Details = $"سداد: {invoice.PaymentMethodSummary} ({invoice.NetTotalDisplay}).",
            ActorName = currentCashierName,
            Amount = invoice.NetTotal
        });

        notifyInventoryChanged?.Invoke();
        notifyReturnExchangeChanged?.Invoke();

        // Re-read the invoice from SQL so every line carries its database id (required later by returns).
        var reloaded = RefreshInvoicesFromDatabase();
        var current = reloaded
            ? sharedInvoices.FirstOrDefault(i => i.Id == saved.InvoiceId) ?? invoice
            : invoice;

        if (!reloaded)
        {
            sharedInvoices.Insert(0, invoice);
            RebuildCashierFilters();
            RefreshFilteredInvoices();
        }

        SelectedInvoice = current;
        SetStatus($"تم حفظ الفاتورة {saved.InvoiceNumber} في قاعدة البيانات بقيمة {invoice.NetTotalDisplay}.", false);
        return current;
    }

    /// <summary>Reloads saved sales invoices (headers, lines, serials, payments and activity) from SQL Server.</summary>
    public bool RefreshInvoicesFromDatabase()
    {
        if (repository is null) return false;

        try
        {
            repository.LoadInvoicesInto(sharedInvoices);
            nextInvoiceId = sharedInvoices.Count > 0 ? sharedInvoices.Max(i => i.Id) + 1 : 1;
            RebuildCashierFilters();
            RefreshFilteredInvoices();
            NotifyKpis();
            SetStatus("تم تحميل فواتير البيع المحفوظة من قاعدة البيانات.", false);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex is SalesDataException data ? data.Message : "تعذر تحميل الفواتير من قاعدة البيانات.", true);
            return false;
        }
    }

    public void SetReturnExchangeNotifier(Action? notifier)
    {
        notifyReturnExchangeChanged = notifier;
    }

    public void NotifyExternalInvoicesChanged()
    {
        RebuildCashierFilters();
        RefreshFilteredInvoices();
        NotifyKpis();
    }

    private void SaveSelectedNotes()
    {
        if (SelectedInvoice is null) return;

        var trimmed = EditableNotes.Trim();
        if (SelectedInvoice.Notes == trimmed)
        {
            SetStatus("لم يتغير نص الملاحظات.", false);
            return;
        }

        if (repository is not null && SelectedInvoice.Id > 0)
        {
            try
            {
                repository.UpdateNotes(SelectedInvoice.Id, trimmed);
            }
            catch (Exception ex)
            {
                SetStatus(ex is SalesDataException data ? data.Message : "تعذر حفظ ملاحظات الفاتورة في قاعدة البيانات.", true);
                return;
            }
        }

        SelectedInvoice.Notes = trimmed;
        SelectedInvoice.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "تحديث ملاحظات الفاتورة",
            Details = string.IsNullOrWhiteSpace(trimmed) ? "تم مسح الملاحظات." : trimmed,
            ActorName = currentCashierName
        });

        SetStatus($"تم حفظ ملاحظات الفاتورة {SelectedInvoice.InvoiceNumber}.", false);
    }

    private void OpenPrintPreview(SalesInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        var vm = new InvoicePrintPreviewViewModel(invoice, modeLabel =>
        {
            invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "معاينة / طباعة الفاتورة",
                Details = $"طباعة بصيغة {modeLabel}.",
                ActorName = currentCashierName
            });
            SetStatus($"تمت معاينة طباعة الفاتورة {invoice.InvoiceNumber} ({modeLabel}).", false);
        });

        OpenPrintPreviewRequested?.Invoke(vm);
    }

    private void OpenReturnDialog(SalesInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        if (!invoice.CanReturn)
        {
            SetStatus($"لا يمكن عمل مرتجع للفاتورة {invoice.InvoiceNumber} في حالتها الحالية ({invoice.StatusTitle}).", true);
            return;
        }

        var vm = new InvoiceReturnViewModel(invoice, submission =>
        {
            if (repository is not null && invoice.Id > 0)
                return PostDatabaseReturn(invoice, submission);

            decimal returnValue = 0m;
            var returnedUnitsCount = 0;

            foreach (var req in submission.Lines)
            {
                req.Line.ReturnedQuantity += req.ReturnQuantity;
                returnValue += req.ReturnQuantity * req.Line.UnitPrice;
                returnedUnitsCount += req.ReturnQuantity;

                // Restore or mark damaged in Inventory registrations
                var product = sharedCatalog.FirstOrDefault(p => p.Id == req.Line.ProductId);
                var matchedRegs = sharedRegistrations
                    .Where(r => req.Line.UnitRegistrationIds.Contains(r.Id) && r.IsSold)
                    .Take(req.ReturnQuantity)
                    .ToList();

                foreach (var reg in matchedRegs)
                {
                    reg.SaleInvoiceNumber = "";
                    if (submission.ReturnAsAvailableStock)
                    {
                        reg.Status = InventoryUnitStatus.Available;
                    }
                    else
                    {
                        reg.Status = InventoryUnitStatus.Damaged;
                        reg.DamageReason = $"مرتجع فاتورة {invoice.InvoiceNumber}: {submission.Reason}";
                        reg.DamagedAt = DateTime.Today;
                        reg.DamageSettlementStatus = "قيد المراجعة";
                    }
                }

                if (product is not null)
                {
                    if (matchedRegs.Count > 0)
                    {
                        product.AvailableStock = sharedRegistrations.Count(r => r.ProductId == product.Id && r.IsAvailable);
                        product.DamagedCount = sharedRegistrations.Count(r => r.ProductId == product.Id && r.IsDamaged);
                    }
                    else if (submission.ReturnAsAvailableStock)
                    {
                        product.AvailableStock += req.ReturnQuantity;
                    }
                    else
                    {
                        product.DamagedCount += req.ReturnQuantity;
                    }
                }
            }

            if (submission.RefundCashToCustomer)
                invoice.RefundedAmount += returnValue;

            var anyRemainingItems = invoice.Items.Any(i => i.RemainingQuantity > 0);
            invoice.Status = anyRemainingItems
                ? SalesInvoiceStatus.PartiallyReturned
                : SalesInvoiceStatus.FullyReturned;
            invoice.NotifyFinancialsChanged();

            var stockAction = submission.ReturnAsAvailableStock
                ? "إعادة للمتاح بالمخزون"
                : "تحويل لمخزون الهالك";
            var refundNote = submission.RefundCashToCustomer
                ? $"رد {PosFormatting.FormatCurrency(returnValue)} للعميل"
                : $"خصم {PosFormatting.FormatCurrency(returnValue)} من الرصيد";

            invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = anyRemainingItems ? "مرتجع جزئي" : "مرتجع كلي",
                Details = $"إرجاع {returnedUnitsCount} قطعة ({submission.Reason}) — {stockAction} · {refundNote}.",
                ActorName = currentCashierName,
                Amount = returnValue
            });

            if (sharedReturnOperations is not null)
            {
                var opId = sharedReturnOperations.Count > 0 ? sharedReturnOperations.Max(o => o.Id) + 1 : 1;
                var summaryItems = string.Join("، ", submission.Lines.Select(l =>
                    $"{l.ReturnQuantity}× {l.Line.ProductName} ({(submission.ReturnAsAvailableStock ? "سليم ← المخزون" : "هالك ← مخزون الهالك")})"));
                sharedReturnOperations.Insert(0, new ReturnExchangeOperationItem
                {
                    Id = opId,
                    OperationNumber = $"RET-2026-{opId:0000}",
                    OperationType = ReturnExchangeType.Return,
                    CreatedAt = DateTime.Now,
                    ActorName = currentCashierName,
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    CustomerName = invoice.CustomerName,
                    CustomerPhone = invoice.CustomerPhone,
                    Reason = submission.Reason,
                    GoodReturnedUnitsCount = submission.ReturnAsAvailableStock ? returnedUnitsCount : 0,
                    DamagedReturnedUnitsCount = submission.ReturnAsAvailableStock ? 0 : returnedUnitsCount,
                    ReplacementUnitsCount = 0,
                    ReturnedValue = returnValue,
                    ReplacementValue = 0m,
                    ReturnedItemsSummary = summaryItems,
                    ReplacementItemsSummary = "—",
                    SettlementSummary = refundNote
                });
            }

            notifyInventoryChanged?.Invoke();
            notifyReturnExchangeChanged?.Invoke();
            RefreshFilteredInvoices();
            NotifyKpis();
            SetStatus($"تم تسجيل مرتجع على الفاتورة {invoice.InvoiceNumber} ({returnedUnitsCount} قطعة) وتحديث المخزون.", false);
            return null;
        });

        OpenReturnDialogRequested?.Invoke(vm);
    }

    private void OpenCollectPaymentDialog(SalesInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        if (!invoice.CanCollectRemaining)
        {
            SetStatus($"الفاتورة {invoice.InvoiceNumber} ليس عليها مبلغ متبقٍ للتحصيل.", true);
            return;
        }

        var vm = new CollectInvoicePaymentViewModel(invoice, submission =>
        {
            var paidNow = submission.CashAmount + submission.VodafoneAmount + submission.InstaPayAmount;

            if (repository is not null && invoice.Id > 0)
            {
                try
                {
                    repository.CollectPayment(invoice.Id, submission.CashAmount, submission.VodafoneAmount,
                        submission.InstaPayAmount, invoice.RemainingAmount, invoice.CustomerName, submission.Note);
                }
                catch (Exception ex)
                {
                    return ex is SalesDataException data
                        ? data.Message
                        : "تعذر حفظ التحصيل في قاعدة البيانات؛ لم يُسجل أي مبلغ.";
                }
            }

            invoice.CashPaid += submission.CashAmount;
            invoice.VodafoneCashPaid += submission.VodafoneAmount;
            invoice.InstaPayPaid += submission.InstaPayAmount;
            if (invoice.RemainingAmount <= 0m && invoice.IsPartialCredit)
                invoice.Status = SalesInvoiceStatus.Completed;

            invoice.NotifyFinancialsChanged();

            var parts = new List<string>(3);
            if (submission.CashAmount > 0m) parts.Add($"كاش {PosFormatting.FormatCurrency(submission.CashAmount)}");
            if (submission.VodafoneAmount > 0m) parts.Add($"فودافون {PosFormatting.FormatCurrency(submission.VodafoneAmount)}");
            if (submission.InstaPayAmount > 0m) parts.Add($"إنستاباي {PosFormatting.FormatCurrency(submission.InstaPayAmount)}");
            var extraNote = string.IsNullOrWhiteSpace(submission.Note) ? "" : $" — {submission.Note}";

            invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "تحصيل دفعة متبقية",
                Details = $"{string.Join(" + ", parts)}{extraNote} | المتبقي بعد التحصيل: {invoice.RemainingAmountDisplay}.",
                ActorName = currentCashierName,
                Amount = paidNow
            });

            RefreshFilteredInvoices();
            NotifyKpis();
            notifyReturnExchangeChanged?.Invoke();
            notifyInventoryChanged?.Invoke();
            SetStatus($"تم تحصيل {PosFormatting.FormatCurrency(paidNow)} على الفاتورة {invoice.InvoiceNumber} وحفظها في قاعدة البيانات.", false);
            return null;
        });

        OpenCollectPaymentRequested?.Invoke(vm);
    }

    private void RequestCancelInvoice(SalesInvoiceDesignItem? invoice)
    {
        if (invoice is null) return;
        SelectedInvoice = invoice;

        if (!invoice.CanCancel)
        {
            SetStatus($"لا يمكن إلغاء الفاتورة {invoice.InvoiceNumber} في حالتها الحالية ({invoice.StatusTitle}).", true);
            return;
        }

        var confirmVm = new ConfirmActionViewModel(
            "تأكيد إلغاء الفاتورة",
            $"{invoice.InvoiceNumber} — {invoice.CustomerName}",
            $"هل تريد إلغاء الفاتورة {invoice.InvoiceNumber} بقيمة {invoice.NetTotalDisplay}؟ سيتم رد المبلغ المحصل وإعادة قطعها إلى المخزون المتاح.",
            "إلغاء الفاتورة");

        OpenConfirmDialogRequested?.Invoke(confirmVm, () =>
        {
            if (repository is not null && invoice.Id > 0)
            {
                CancelPersistedInvoice(invoice);
                return;
            }

            foreach (var line in invoice.Items)
            {
                var product = sharedCatalog.FirstOrDefault(p => p.Id == line.ProductId);
                var soldRegs = sharedRegistrations
                    .Where(r => line.UnitRegistrationIds.Contains(r.Id) && r.IsSold)
                    .ToList();

                foreach (var reg in soldRegs)
                {
                    reg.Status = InventoryUnitStatus.Available;
                    reg.SaleInvoiceNumber = "";
                }

                var unreturnedQty = line.RemainingQuantity;
                line.ReturnedQuantity = line.Quantity;

                if (product is not null)
                {
                    if (soldRegs.Count > 0)
                        product.AvailableStock = sharedRegistrations.Count(r => r.ProductId == product.Id && r.IsAvailable);
                    else if (unreturnedQty > 0)
                        product.AvailableStock += unreturnedQty;
                }
            }

            var refunded = invoice.NetCollected;
            invoice.RefundedAmount = invoice.GrossPaid;
            invoice.Status = SalesInvoiceStatus.Cancelled;
            invoice.NotifyFinancialsChanged();

            invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionTitle = "إلغاء الفاتورة بالكامل",
                Details = $"تم إلغاء الفاتورة وإعادة القطع للمخزون ورد {PosFormatting.FormatCurrency(refunded)}.",
                ActorName = currentCashierName,
                Amount = refunded
            });

            notifyInventoryChanged?.Invoke();
            notifyReturnExchangeChanged?.Invoke();
            RefreshFilteredInvoices();
            NotifyKpis();
            SetStatus($"تم إلغاء الفاتورة {invoice.InvoiceNumber} وإعادة أصنافها للمخزون.", false);
        });
    }

    /// <returns>Null on success, or the reason the dialog must stay open for the operator to fix.</returns>
    private string? PostDatabaseReturn(SalesInvoiceDesignItem invoice, InvoiceReturnSubmission submission)
    {
        var requests = new List<SalesRepository.ReturnLineRequest>();
        foreach (var req in submission.Lines)
        {
            if (req.ReturnQuantity <= 0) continue;
            if (req.Line.DatabaseLineId <= 0)
                return "هذا البند غير محفوظ في قاعدة البيانات؛ حدّث شاشة الفواتير وأعد المحاولة.";

            requests.Add(new SalesRepository.ReturnLineRequest(
                req.Line.DatabaseLineId,
                req.ReturnQuantity,
                submission.ReturnAsAvailableStock,
                req.Line.UnitPrice,
                req.Line.UnitCost > 0m ? req.Line.UnitCost : req.Line.UnitPrice));
        }

        if (requests.Count == 0)
            return "حدد كمية واحدة على الأقل للإرجاع من بنود الفاتورة.";

        string operationNumber;
        try
        {
            operationNumber = repository!.CreateReturn(new SalesRepository.ReturnRequest(
                invoice, submission.Reason, submission.RefundCashToCustomer, requests));
        }
        catch (Exception ex)
        {
            return ex is SalesDataException data
                ? data.Message
                : "تعذر حفظ المرتجع في قاعدة البيانات؛ لم تتغير الفاتورة ولا المخزون.";
        }

        var returnedUnits = requests.Sum(r => r.Quantity);
        var returnValue = requests.Sum(r => r.Quantity * r.UnitPrice);

        foreach (var req in submission.Lines)
        {
            if (req.ReturnQuantity <= 0) continue;
            req.Line.ReturnedQuantity += req.ReturnQuantity;
        }

        var anyRemaining = invoice.Items.Any(i => i.RemainingQuantity > 0);
        invoice.Status = anyRemaining ? SalesInvoiceStatus.PartiallyReturned : SalesInvoiceStatus.FullyReturned;
        if (submission.RefundCashToCustomer) invoice.RefundedAmount += returnValue;
        invoice.NotifyFinancialsChanged();

        sharedReturnOperations?.Insert(0, new ReturnExchangeOperationItem
        {
            Id = sharedReturnOperations.Count > 0 ? sharedReturnOperations.Max(o => o.Id) + 1 : 1,
            OperationNumber = operationNumber,
            OperationType = ReturnExchangeType.Return,
            CreatedAt = DateTime.Now,
            ActorName = currentCashierName,
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            CustomerName = invoice.CustomerName,
            CustomerPhone = invoice.CustomerPhone,
            Reason = submission.Reason,
            GoodReturnedUnitsCount = submission.ReturnAsAvailableStock ? returnedUnits : 0,
            DamagedReturnedUnitsCount = submission.ReturnAsAvailableStock ? 0 : returnedUnits,
            ReplacementUnitsCount = 0,
            ReturnedValue = returnValue,
            ReplacementValue = 0m,
            ReturnedItemsSummary = string.Join("، ", submission.Lines
                .Where(l => l.ReturnQuantity > 0)
                .Select(l => $"{l.ReturnQuantity}× {l.Line.ProductName} " +
                             $"({(submission.ReturnAsAvailableStock ? "سليم ← المخزون" : "هالك ← مخزون الهالك")})")),
            ReplacementItemsSummary = "—",
            SettlementSummary = submission.RefundCashToCustomer
                ? $"رد للعميل: {PosFormatting.FormatCurrency(returnValue)}"
                : $"خصم {PosFormatting.FormatCurrency(returnValue)} من الرصيد"
        });

        notifyReturnExchangeChanged?.Invoke();
        RefreshFilteredInvoices();
        NotifyKpis();

        // Reload the invoice rows after the SQL write so returned quantities and new activity come from the database.
        if (!RefreshInvoicesFromDatabase())
            KeepInvoiceSelection(invoice.Id);

        notifyInventoryChanged?.Invoke();
        SetStatus($"تم تسجيل المرتجع {operationNumber} على الفاتورة {invoice.InvoiceNumber} ({returnedUnits} قطعة) في قاعدة البيانات.", false);
        return null;
    }

    private void CancelPersistedInvoice(SalesInvoiceDesignItem invoice)
    {
        var refunded = invoice.NetCollected;
        try
        {
            repository!.CancelSale(invoice);
        }
        catch (Exception ex)
        {
            SetStatus(ex is SalesDataException data ? data.Message : "تعذر إلغاء الفاتورة في قاعدة البيانات.", true);
            return;
        }

        foreach (var line in invoice.Items)
            line.ReturnedQuantity = line.Quantity;

        invoice.RefundedAmount = invoice.GrossPaid;
        invoice.Status = SalesInvoiceStatus.Cancelled;
        invoice.NotifyFinancialsChanged();
        invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = DateTime.Now,
            ActionTitle = "إلغاء الفاتورة بالكامل",
            Details = $"تم إلغاء الفاتورة وإعادة القطع للمخزون ورد {PosFormatting.FormatCurrency(refunded)}.",
            ActorName = currentCashierName,
            Amount = refunded
        });

        notifyReturnExchangeChanged?.Invoke();
        RefreshFilteredInvoices();
        NotifyKpis();

        if (!RefreshInvoicesFromDatabase())
            KeepInvoiceSelection(invoice.Id);

        notifyInventoryChanged?.Invoke();
        SetStatus($"تم إلغاء الفاتورة {invoice.InvoiceNumber} في قاعدة البيانات وإعادة أصنافها للمخزون.", false);
    }

    /// <summary>Re-selects the invoice the operator was working on after the list is rebuilt from SQL.</summary>
    private void KeepInvoiceSelection(int invoiceId)
    {
        var match = sharedInvoices.FirstOrDefault(i => i.Id == invoiceId);
        if (match is not null)
            SelectedInvoice = match;
    }

    private void OnSharedInvoicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCashierFilters();
        RefreshFilteredInvoices();
    }

    private void RebuildCashierFilters()
    {
        var current = SelectedCashierFilter;
        var names = sharedInvoices
            .Select(i => i.CashierName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        CashierFilters.Clear();
        CashierFilters.Add("كل الكاشيرية");
        foreach (var n in names)
            CashierFilters.Add(n);

        if (CashierFilters.Contains(current))
            selectedCashierFilter = current;
        else
            selectedCashierFilter = "كل الكاشيرية";

        Changed(nameof(SelectedCashierFilter));
    }

    private void RefreshFilteredInvoices()
    {
        var query = searchText.Trim();
        var today = DateTime.Today;

        var matches = sharedInvoices.Where(inv =>
        {
            if (SelectedCashierFilter != "كل الكاشيرية"
                && !inv.CashierName.Equals(SelectedCashierFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (SelectedStatusFilter != "الكل" && inv.StatusTitle != SelectedStatusFilter)
                return false;

            if (SelectedPaymentFilter != "الكل")
            {
                var matchesPayment = SelectedPaymentFilter switch
                {
                    "نقدي" => inv.CashPaid > 0m && inv.VodafoneCashPaid == 0m && inv.InstaPayPaid == 0m && !inv.HasRemaining,
                    "فودافون كاش" => inv.VodafoneCashPaid > 0m && inv.CashPaid == 0m && inv.InstaPayPaid == 0m && !inv.HasRemaining,
                    "إنستاباي" => inv.InstaPayPaid > 0m && inv.CashPaid == 0m && inv.VodafoneCashPaid == 0m && !inv.HasRemaining,
                    "دفع مقسم" => ((inv.CashPaid > 0m ? 1 : 0) + (inv.VodafoneCashPaid > 0m ? 1 : 0) + (inv.InstaPayPaid > 0m ? 1 : 0)) >= 2,
                    "آجل / متبقي" => inv.HasRemaining || inv.IsPartialCredit,
                    _ => true
                };
                if (!matchesPayment) return false;
            }

            var invDate = inv.IssuedAt.Date;
            var matchesDate = SelectedDatePreset switch
            {
                "اليوم" => invDate == today,
                "آخر 7 أيام" => invDate >= today.AddDays(-6) && invDate <= today,
                "هذا الشهر" => invDate.Year == today.Year && invDate.Month == today.Month,
                "فترة مخصصة" => (!CustomFromDate.HasValue || invDate >= CustomFromDate.Value.Date)
                                 && (!CustomToDate.HasValue || invDate <= CustomToDate.Value.Date),
                _ => true
            };
            if (!matchesDate) return false;

            if (string.IsNullOrEmpty(query))
                return true;

            return inv.InvoiceNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || inv.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || inv.CustomerPhone.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || inv.CashierName.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || inv.Items.Any(line =>
                       line.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase)
                       || line.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                       || line.SerialNumbers.Any(sn => sn.Contains(query, StringComparison.OrdinalIgnoreCase))
                       || line.UnitRegistrationIds.Any(id => id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)));
        }).OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id).ToList();

        FilteredInvoices.Clear();
        foreach (var item in matches)
            FilteredInvoices.Add(item);

        if (SelectedInvoice is null || !FilteredInvoices.Contains(SelectedInvoice))
            SelectedInvoice = FilteredInvoices.FirstOrDefault();

        Changed(nameof(HasNoFilteredInvoices));
        NotifyKpis();
    }

    private void NotifyKpis()
    {
        Changed(nameof(InvoicesCountDisplay));
        Changed(nameof(InvoicesBreakdownSummary));
        Changed(nameof(TotalNetSales));
        Changed(nameof(TotalNetSalesDisplay));
        Changed(nameof(TotalDiscountsSubtext));
        Changed(nameof(TotalCost));
        Changed(nameof(TotalProfit));
        Changed(nameof(TotalProfitDisplay));
        Changed(nameof(TotalCostAndMarginDisplay));
        Changed(nameof(TotalCashCollected));
        Changed(nameof(TotalVodafoneCollected));
        Changed(nameof(TotalInstaPayCollected));
        Changed(nameof(TotalCollectedAll));
        Changed(nameof(TotalCollectedAllDisplay));
        Changed(nameof(ChannelsBreakdownDisplay));
        Changed(nameof(TotalRemaining));
        Changed(nameof(TotalReturned));
        Changed(nameof(TotalRemainingDisplay));
        Changed(nameof(TotalReturnedSubtext));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
