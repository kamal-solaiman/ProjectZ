using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class ReturnExchangeInvoiceLineViewModel : ObservableObject
{
    private readonly Action onChanged;
    private int goodReturnQty;
    private int damagedReturnQty;

    public SalesInvoiceLineDesignItem Line { get; }
    public int MaxReturnable => Line.RemainingQuantity;
    public string MaxReturnableDisplay => $"المتاح: {MaxReturnable}";

    public int GoodReturnQty
    {
        get => goodReturnQty;
        set
        {
            var maxAllowed = Math.Max(0, MaxReturnable - DamagedReturnQty);
            var clamped = Math.Clamp(value, 0, maxAllowed);
            if (goodReturnQty == clamped) return;
            goodReturnQty = clamped;
            Changed();
            NotifyLineTotals();
        }
    }

    public int DamagedReturnQty
    {
        get => damagedReturnQty;
        set
        {
            var maxAllowed = Math.Max(0, MaxReturnable - GoodReturnQty);
            var clamped = Math.Clamp(value, 0, maxAllowed);
            if (damagedReturnQty == clamped) return;
            damagedReturnQty = clamped;
            Changed();
            NotifyLineTotals();
        }
    }

    public int TotalSelectedReturnQty => GoodReturnQty + DamagedReturnQty;
    public bool HasSelectedReturn => TotalSelectedReturnQty > 0;
    public decimal LineReturnValue => TotalSelectedReturnQty * Line.UnitPrice;
    public string GoodReturnQtyDisplay => GoodReturnQty.ToString(CultureInfo.InvariantCulture);
    public string DamagedReturnQtyDisplay => DamagedReturnQty.ToString(CultureInfo.InvariantCulture);
    public string LineReturnValueDisplay => PosFormatting.FormatCurrency(LineReturnValue);

    public ActionCommand IncrementGoodCommand { get; }
    public ActionCommand DecrementGoodCommand { get; }
    public ActionCommand AllGoodCommand { get; }
    public ActionCommand IncrementDamagedCommand { get; }
    public ActionCommand DecrementDamagedCommand { get; }
    public ActionCommand AllDamagedCommand { get; }
    public ActionCommand ResetLineCommand { get; }

    public ReturnExchangeInvoiceLineViewModel(SalesInvoiceLineDesignItem line, Action onChanged)
    {
        Line = line;
        this.onChanged = onChanged;

        IncrementGoodCommand = new ActionCommand(() => GoodReturnQty++);
        DecrementGoodCommand = new ActionCommand(() => GoodReturnQty--);
        AllGoodCommand = new ActionCommand(() =>
        {
            damagedReturnQty = 0;
            Changed(nameof(DamagedReturnQty));
            Changed(nameof(DamagedReturnQtyDisplay));
            GoodReturnQty = MaxReturnable;
        });

        IncrementDamagedCommand = new ActionCommand(() => DamagedReturnQty++);
        DecrementDamagedCommand = new ActionCommand(() => DamagedReturnQty--);
        AllDamagedCommand = new ActionCommand(() =>
        {
            goodReturnQty = 0;
            Changed(nameof(GoodReturnQty));
            Changed(nameof(GoodReturnQtyDisplay));
            DamagedReturnQty = MaxReturnable;
        });

        ResetLineCommand = new ActionCommand(() =>
        {
            goodReturnQty = 0;
            damagedReturnQty = 0;
            Changed(nameof(GoodReturnQty));
            Changed(nameof(GoodReturnQtyDisplay));
            Changed(nameof(DamagedReturnQty));
            Changed(nameof(DamagedReturnQtyDisplay));
            NotifyLineTotals();
        });
    }

    private void NotifyLineTotals()
    {
        Changed(nameof(GoodReturnQtyDisplay));
        Changed(nameof(DamagedReturnQtyDisplay));
        Changed(nameof(TotalSelectedReturnQty));
        Changed(nameof(HasSelectedReturn));
        Changed(nameof(LineReturnValue));
        Changed(nameof(LineReturnValueDisplay));
        onChanged();
    }
}

public sealed class ExchangeReplacementCartItemViewModel : ObservableObject
{
    private readonly Action onChanged;
    private readonly Action<ExchangeReplacementCartItemViewModel> onRemove;
    private int quantity = 1;
    private PriceTier selectedPriceTier;

    public PosProductDesignItem Product { get; }
    public IReadOnlyList<PriceTierOption> PriceTierOptions => PosFormatting.PriceTierOptions;

    public PriceTier SelectedPriceTier
    {
        get => selectedPriceTier;
        set
        {
            if (selectedPriceTier == value) return;
            selectedPriceTier = value;
            Changed();
            Changed(nameof(UnitPrice));
            Changed(nameof(UnitPriceDisplay));
            Changed(nameof(LineTotal));
            Changed(nameof(LineTotalDisplay));
            onChanged();
        }
    }

    public int Quantity
    {
        get => quantity;
        set
        {
            var max = Math.Max(1, Product.AvailableStock);
            var clamped = Math.Clamp(value, 1, max);
            if (quantity == clamped) return;
            quantity = clamped;
            Changed();
            Changed(nameof(QuantityDisplay));
            Changed(nameof(LineTotal));
            Changed(nameof(LineTotalDisplay));
            onChanged();
        }
    }

    public decimal UnitPrice => Product.GetPrice(SelectedPriceTier);
    public decimal LineTotal => UnitPrice * Quantity;
    public string QuantityDisplay => Quantity.ToString(CultureInfo.InvariantCulture);
    public string UnitPriceDisplay => PosFormatting.FormatCurrency(UnitPrice);
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);

    public ActionCommand IncrementCommand { get; }
    public ActionCommand DecrementCommand { get; }
    public ActionCommand RemoveCommand { get; }

    public ExchangeReplacementCartItemViewModel(
        PosProductDesignItem product,
        PriceTier initialTier,
        Action onChanged,
        Action<ExchangeReplacementCartItemViewModel> onRemove)
    {
        Product = product;
        selectedPriceTier = initialTier;
        this.onChanged = onChanged;
        this.onRemove = onRemove;

        IncrementCommand = new ActionCommand(() => Quantity++);
        DecrementCommand = new ActionCommand(() => Quantity--);
        RemoveCommand = new ActionCommand(() => this.onRemove(this));
    }
}

public sealed class ReturnExchangeViewModel : ObservableObject
{
    private readonly ObservableCollection<SalesInvoiceDesignItem> sharedInvoices;
    private readonly ObservableCollection<PosProductDesignItem> sharedCatalog;
    private readonly ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations;
    private readonly ObservableCollection<ReturnExchangeOperationItem> sharedOperations;
    private readonly string currentCashierName;
    private readonly Action? navigateToInvoices;
    private readonly Action? notifyInventoryChanged;
    private readonly Action? notifyInvoicesChanged;

    private int nextOperationId = 2;
    private int nextRegistrationId = 4001;

    private bool isHistoryTabSelected;
    private string searchText = "";
    private string selectedDatePreset = "الكل";
    private DateTime? customFromDate = DateTime.Today;
    private DateTime? customToDate = DateTime.Today;
    private bool onlyEligibleInvoices;

    private SalesInvoiceDesignItem? selectedInvoice;
    private bool isExchangeMode;
    private string operationReason = "";

    private string replacementSearchText = "";
    private PriceTier replacementDefaultTier = PriceTier.Retail;

    private string refundMethod = "كاش (نقدي)";
    private string refundReference = "";
    private string diffCashText = "";
    private string diffVodafoneText = "";
    private string diffVodafoneReference = "";
    private string diffInstaPayText = "";
    private string diffInstaPayReference = "";

    private string historySearchText = "";
    private string selectedHistoryTypeFilter = "الكل";

    private string statusMessage = "مرحلة التصميم (بدون Backend): ابحث باسم العميل أو رقم الموبايل أو رقم الفاتورة أو بتاريخ اليوم لعرض الفاتورة كاملة وتنفيذ المرتجع أو الاستبدال.";
    private bool isErrorStatus;

    public ObservableCollection<SalesInvoiceDesignItem> FilteredInvoices { get; } = [];
    public ObservableCollection<ReturnExchangeInvoiceLineViewModel> InvoiceLines { get; } = [];
    public ObservableCollection<PosProductDesignItem> FilteredReplacementCatalog { get; } = [];
    public ObservableCollection<ExchangeReplacementCartItemViewModel> ReplacementItems { get; } = [];
    public ObservableCollection<ReturnExchangeOperationItem> FilteredOperations { get; } = [];

    public IReadOnlyList<string> DatePresets { get; } = ["الكل", "اليوم", "آخر 7 أيام", "هذا الشهر", "فترة مخصصة"];
    public IReadOnlyList<string> SuggestedReasons => ReturnExchangeDesignSeedData.OperationReasons;
    public IReadOnlyList<string> RefundMethods { get; } = ["كاش (نقدي)", "فودافون كاش", "إنستاباي", "خصم من المتبقي الآجل"];
    public IReadOnlyList<string> HistoryTypeFilters { get; } = ["الكل", "مرتجع", "استبدال"];
    public IReadOnlyList<PriceTierOption> PriceTierOptions => PosFormatting.PriceTierOptions;

    public Action<InvoicePrintPreviewViewModel>? OpenPrintPreviewRequested { get; set; }

    public bool IsWorkspaceTabSelected
    {
        get => !isHistoryTabSelected;
        set
        {
            if (isHistoryTabSelected == !value) return;
            isHistoryTabSelected = !value;
            Changed();
            Changed(nameof(IsHistoryTabSelected));
        }
    }

    public bool IsHistoryTabSelected
    {
        get => isHistoryTabSelected;
        set
        {
            if (isHistoryTabSelected == value) return;
            isHistoryTabSelected = value;
            Changed();
            Changed(nameof(IsWorkspaceTabSelected));
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

    public bool OnlyEligibleInvoices
    {
        get => onlyEligibleInvoices;
        set
        {
            if (onlyEligibleInvoices == value) return;
            onlyEligibleInvoices = value;
            Changed();
            RefreshFilteredInvoices();
        }
    }

    public SalesInvoiceDesignItem? SelectedInvoice
    {
        get => selectedInvoice;
        set
        {
            if (ReferenceEquals(selectedInvoice, value)) return;
            if (selectedInvoice is not null)
                selectedInvoice.IsReturnWorkspaceSelected = false;

            selectedInvoice = value;

            if (selectedInvoice is not null)
                selectedInvoice.IsReturnWorkspaceSelected = true;

            Changed();
            Changed(nameof(HasSelectedInvoice));
            Changed(nameof(CanOperateOnSelected));
            Changed(nameof(SelectedInvoiceId));
            RebuildWorkspaceForSelectedInvoice();
        }
    }

    public int SelectedInvoiceId => SelectedInvoice?.Id ?? 0;
    public bool HasSelectedInvoice => SelectedInvoice is not null;
    public bool CanOperateOnSelected => SelectedInvoice is not null && SelectedInvoice.CanReturn;
    public bool HasNoMatchedInvoices => FilteredInvoices.Count == 0;
    public string MatchedInvoicesCountDisplay => $"نتائج البحث: {FilteredInvoices.Count} فاتورة";

    public bool IsReturnMode
    {
        get => !isExchangeMode;
        set
        {
            if (isExchangeMode == !value) return;
            isExchangeMode = !value;
            Changed();
            Changed(nameof(IsExchangeMode));
            Changed(nameof(OperationActionButtonText));
            NotifySettlementChanged();
        }
    }

    public bool IsExchangeMode
    {
        get => isExchangeMode;
        set
        {
            if (isExchangeMode == value) return;
            isExchangeMode = value;
            Changed();
            Changed(nameof(IsReturnMode));
            Changed(nameof(OperationActionButtonText));
            NotifySettlementChanged();
        }
    }

    public string OperationActionButtonText => IsExchangeMode ? "تأكيد وحفظ الاستبدال" : "تأكيد وحفظ المرتجع";

    public string OperationReason
    {
        get => operationReason;
        set
        {
            if (operationReason == value) return;
            operationReason = value;
            Changed();
        }
    }

    public string ReplacementSearchText
    {
        get => replacementSearchText;
        set
        {
            if (replacementSearchText == value) return;
            replacementSearchText = value;
            Changed();
            RefreshReplacementCatalog();
        }
    }

    public PriceTier ReplacementDefaultTier
    {
        get => replacementDefaultTier;
        set
        {
            if (replacementDefaultTier == value) return;
            replacementDefaultTier = value;
            Changed();
        }
    }

    public bool HasReplacementItems => ReplacementItems.Count > 0;
    public bool HasNoReplacementResults => FilteredReplacementCatalog.Count == 0;

    public string RefundMethod
    {
        get => refundMethod;
        set
        {
            if (refundMethod == value || value is null) return;
            refundMethod = value;
            Changed();
            Changed(nameof(NeedsRefundReference));
        }
    }

    public bool NeedsRefundReference => RefundMethod is "فودافون كاش" or "إنستاباي";

    public string RefundReference
    {
        get => refundReference;
        set { refundReference = value; Changed(); }
    }

    public string DiffCashText
    {
        get => diffCashText;
        set { diffCashText = value; Changed(); NotifyPaymentDiffInputs(); }
    }

    public string DiffVodafoneText
    {
        get => diffVodafoneText;
        set { diffVodafoneText = value; Changed(); NotifyPaymentDiffInputs(); }
    }

    public string DiffVodafoneReference
    {
        get => diffVodafoneReference;
        set { diffVodafoneReference = value; Changed(); }
    }

    public string DiffInstaPayText
    {
        get => diffInstaPayText;
        set { diffInstaPayText = value; Changed(); NotifyPaymentDiffInputs(); }
    }

    public string DiffInstaPayReference
    {
        get => diffInstaPayReference;
        set { diffInstaPayReference = value; Changed(); }
    }

    // Settlement computations
    public int TotalGoodUnitsSelected => InvoiceLines.Sum(l => l.GoodReturnQty);
    public int TotalDamagedUnitsSelected => InvoiceLines.Sum(l => l.DamagedReturnQty);
    public int TotalReturnedUnitsSelected => TotalGoodUnitsSelected + TotalDamagedUnitsSelected;
    public int TotalReplacementUnitsSelected => ReplacementItems.Sum(r => r.Quantity);

    public decimal TotalReturnedValue => InvoiceLines.Sum(l => l.LineReturnValue);
    public decimal TotalReplacementValue => IsExchangeMode ? ReplacementItems.Sum(r => r.LineTotal) : 0m;
    public decimal NetDifference => IsExchangeMode ? TotalReplacementValue - TotalReturnedValue : -TotalReturnedValue;

    public bool HasAnySelectedReturn => TotalReturnedUnitsSelected > 0;
    public bool NeedsCustomerRefund => HasAnySelectedReturn && NetDifference < 0m;
    public bool NeedsCustomerPayment => IsExchangeMode && TotalReplacementUnitsSelected > 0 && NetDifference > 0m;
    public bool IsEvenExchange => IsExchangeMode && HasAnySelectedReturn && TotalReplacementUnitsSelected > 0 && NetDifference == 0m;

    public decimal SettlementMagnitude => Math.Abs(NetDifference);
    public string TotalReturnedValueDisplay => PosFormatting.FormatCurrency(TotalReturnedValue);
    public string TotalReplacementValueDisplay => PosFormatting.FormatCurrency(TotalReplacementValue);
    public string SettlementMagnitudeDisplay => PosFormatting.FormatCurrency(SettlementMagnitude);

    public string SettlementDirectionLabel
    {
        get
        {
            if (NeedsCustomerPayment) return "فرق مستحق على العميل دفعه:";
            if (NeedsCustomerRefund) return "مبلغ مستحق ردّه / خصمه للعميل:";
            return "الفرق المالي الصافي:";
        }
    }

    private decimal ParsedDiffCash => PosFormatting.TryParseAmount(DiffCashText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedDiffVodafone => PosFormatting.TryParseAmount(DiffVodafoneText, out var v) ? Math.Max(0m, v) : 0m;
    private decimal ParsedDiffInstaPay => PosFormatting.TryParseAmount(DiffInstaPayText, out var v) ? Math.Max(0m, v) : 0m;
    public decimal TotalDiffPaid => ParsedDiffCash + ParsedDiffVodafone + ParsedDiffInstaPay;
    public decimal DiffRemaining => NeedsCustomerPayment ? Math.Max(0m, SettlementMagnitude - TotalDiffPaid) : 0m;
    public string TotalDiffPaidDisplay => PosFormatting.FormatCurrency(TotalDiffPaid);
    public string DiffRemainingDisplay => PosFormatting.FormatCurrency(DiffRemaining);

    // History Tab Filters & KPIs
    public string HistorySearchText
    {
        get => historySearchText;
        set
        {
            if (historySearchText == value) return;
            historySearchText = value;
            Changed();
            RefreshFilteredOperations();
        }
    }

    public string SelectedHistoryTypeFilter
    {
        get => selectedHistoryTypeFilter;
        set
        {
            if (selectedHistoryTypeFilter == value || value is null) return;
            selectedHistoryTypeFilter = value;
            Changed();
            RefreshFilteredOperations();
        }
    }

    public bool HasNoHistoryRecords => FilteredOperations.Count == 0;
    public string HistoryTabLabel => $"سجل المرتجع والاستبدال ({sharedOperations.Count})";
    public int TotalOperationsCount => sharedOperations.Count;
    public string OperationsKpiSummary =>
        $"مرتجع: {sharedOperations.Count(o => o.IsReturn)} · استبدال: {sharedOperations.Count(o => o.IsExchange)}";
    public int TotalGoodReturnedKpi => sharedOperations.Sum(o => o.GoodReturnedUnitsCount);
    public int TotalDamagedReturnedKpi => sharedOperations.Sum(o => o.DamagedReturnedUnitsCount);
    public decimal TotalRefundedValueKpi => sharedOperations.Where(o => o.NetDifference < 0m).Sum(o => Math.Abs(o.NetDifference));
    public decimal TotalExchangeUpgradesKpi => sharedOperations.Where(o => o.NetDifference > 0m).Sum(o => o.NetDifference);
    public string TotalRefundedValueKpiDisplay => PosFormatting.FormatCurrency(TotalRefundedValueKpi);
    public string TotalExchangeUpgradesKpiSubtext => $"فروق استبدال محصلة: {PosFormatting.FormatCurrency(TotalExchangeUpgradesKpi)}";

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

    public ActionCommand SelectWorkspaceTabCommand { get; }
    public ActionCommand SelectHistoryTabCommand { get; }
    public ActionCommand GoToInvoicesCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public RelayCommand<string> SelectDatePresetCommand { get; }
    public RelayCommand<SalesInvoiceDesignItem> SelectInvoiceCommand { get; }
    public ActionCommand PrintSelectedInvoiceCommand { get; }

    public ActionCommand SelectReturnModeCommand { get; }
    public ActionCommand SelectExchangeModeCommand { get; }
    public ActionCommand ReturnAllGoodCommand { get; }
    public ActionCommand ReturnAllDamagedCommand { get; }
    public ActionCommand ClearLineSelectionsCommand { get; }
    public RelayCommand<PosProductDesignItem> AddReplacementProductCommand { get; }
    public RelayCommand<string> PickReasonCommand { get; }
    public ActionCommand FillDiffCashCommand { get; }
    public ActionCommand FillDiffVodafoneCommand { get; }
    public ActionCommand FillDiffInstaPayCommand { get; }
    public ActionCommand ConfirmOperationCommand { get; }
    public ActionCommand ResetOperationCommand { get; }
    public RelayCommand<ReturnExchangeOperationItem> OpenOperationInvoiceCommand { get; }

    public ReturnExchangeViewModel()
        : this(CreateDefaultSeed(), null, null, null)
    {
    }

    private static (
        ObservableCollection<SalesInvoiceDesignItem> Invoices,
        ObservableCollection<PosProductDesignItem> Catalog,
        ObservableCollection<InventoryUnitRegistrationItem> Registrations,
        ObservableCollection<ReturnExchangeOperationItem> Operations) CreateDefaultSeed()
    {
        var catalog = PosDesignSeedData.CreateSharedSampleCatalog();
        var registrations = InventoryDesignSeedData.CreateSharedSampleRegistrations(catalog);
        var invoices = InvoiceDesignSeedData.CreateSharedSampleInvoices(catalog, registrations);
        var ops = ReturnExchangeDesignSeedData.CreateSharedSampleOperations(invoices);
        return (invoices, catalog, registrations, ops);
    }

    private ReturnExchangeViewModel(
        (ObservableCollection<SalesInvoiceDesignItem> Invoices,
         ObservableCollection<PosProductDesignItem> Catalog,
         ObservableCollection<InventoryUnitRegistrationItem> Registrations,
         ObservableCollection<ReturnExchangeOperationItem> Operations) defaults,
        Action? navigateToInvoices,
        Action? notifyInventoryChanged,
        Action? notifyInvoicesChanged)
        : this(defaults.Invoices, defaults.Catalog, defaults.Registrations, defaults.Operations, "المشرف", navigateToInvoices, notifyInventoryChanged, notifyInvoicesChanged)
    {
    }

    public ReturnExchangeViewModel(
        ObservableCollection<SalesInvoiceDesignItem> sharedInvoices,
        ObservableCollection<PosProductDesignItem> sharedCatalog,
        ObservableCollection<InventoryUnitRegistrationItem> sharedRegistrations,
        ObservableCollection<ReturnExchangeOperationItem> sharedOperations,
        string currentCashierName = "المشرف",
        Action? navigateToInvoices = null,
        Action? notifyInventoryChanged = null,
        Action? notifyInvoicesChanged = null)
    {
        this.sharedInvoices = sharedInvoices;
        this.sharedCatalog = sharedCatalog;
        this.sharedRegistrations = sharedRegistrations;
        this.sharedOperations = sharedOperations;
        this.currentCashierName = currentCashierName;
        this.navigateToInvoices = navigateToInvoices;
        this.notifyInventoryChanged = notifyInventoryChanged;
        this.notifyInvoicesChanged = notifyInvoicesChanged;

        nextOperationId = sharedOperations.Count > 0 ? sharedOperations.Max(o => o.Id) + 1 : 1;
        nextRegistrationId = sharedRegistrations.Count > 0 ? sharedRegistrations.Max(r => r.Id) + 1 : 4001;

        SelectWorkspaceTabCommand = new ActionCommand(() => IsWorkspaceTabSelected = true);
        SelectHistoryTabCommand = new ActionCommand(() => IsHistoryTabSelected = true);
        GoToInvoicesCommand = new ActionCommand(() => this.navigateToInvoices?.Invoke());
        ClearFiltersCommand = new ActionCommand(() =>
        {
            SearchText = "";
            SelectedDatePreset = "الكل";
            OnlyEligibleInvoices = false;
        });
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        SelectDatePresetCommand = new RelayCommand<string>(preset =>
        {
            if (!string.IsNullOrWhiteSpace(preset))
                SelectedDatePreset = preset;
        });
        SelectInvoiceCommand = new RelayCommand<SalesInvoiceDesignItem>(inv =>
        {
            if (inv is not null)
                SelectedInvoice = inv;
        });
        PrintSelectedInvoiceCommand = new ActionCommand(() =>
        {
            if (SelectedInvoice is null) return;
            var vm = new InvoicePrintPreviewViewModel(SelectedInvoice, modeLabel =>
            {
                SelectedInvoice.ActivityLog.Add(new SalesInvoiceActivityEntry
                {
                    Timestamp = DateTime.Now,
                    ActionTitle = "طباعة الفاتورة من المرتجع والاستبدال",
                    Details = $"معاينة/طباعة بصيغة {modeLabel}.",
                    ActorName = currentCashierName
                });
                SetStatus($"تمت معاينة طباعة الفاتورة {SelectedInvoice.InvoiceNumber} ({modeLabel}).", false);
            });
            OpenPrintPreviewRequested?.Invoke(vm);
        });

        SelectReturnModeCommand = new ActionCommand(() => IsReturnMode = true);
        SelectExchangeModeCommand = new ActionCommand(() => IsExchangeMode = true);

        ReturnAllGoodCommand = new ActionCommand(() =>
        {
            foreach (var l in InvoiceLines)
                l.AllGoodCommand.Execute(null);
        });
        ReturnAllDamagedCommand = new ActionCommand(() =>
        {
            foreach (var l in InvoiceLines)
                l.AllDamagedCommand.Execute(null);
        });
        ClearLineSelectionsCommand = new ActionCommand(() =>
        {
            foreach (var l in InvoiceLines)
                l.ResetLineCommand.Execute(null);
        });

        AddReplacementProductCommand = new RelayCommand<PosProductDesignItem>(AddReplacementProduct);
        PickReasonCommand = new RelayCommand<string>(r =>
        {
            if (!string.IsNullOrWhiteSpace(r))
                OperationReason = r;
        });

        FillDiffCashCommand = new ActionCommand(() =>
        {
            DiffCashText = PosFormatting.FormatPlainNumber(SettlementMagnitude);
            DiffVodafoneText = "";
            DiffInstaPayText = "";
        });
        FillDiffVodafoneCommand = new ActionCommand(() =>
        {
            DiffCashText = "";
            DiffVodafoneText = PosFormatting.FormatPlainNumber(SettlementMagnitude);
            DiffInstaPayText = "";
        });
        FillDiffInstaPayCommand = new ActionCommand(() =>
        {
            DiffCashText = "";
            DiffVodafoneText = "";
            DiffInstaPayText = PosFormatting.FormatPlainNumber(SettlementMagnitude);
        });

        ConfirmOperationCommand = new ActionCommand(ExecuteReturnOrExchange);
        ResetOperationCommand = new ActionCommand(RebuildWorkspaceForSelectedInvoice);
        OpenOperationInvoiceCommand = new RelayCommand<ReturnExchangeOperationItem>(op =>
        {
            if (op is null) return;
            var target = sharedInvoices.FirstOrDefault(i => i.Id == op.InvoiceId || i.InvoiceNumber == op.InvoiceNumber);
            if (target is null) return;

            IsWorkspaceTabSelected = true;
            OnlyEligibleInvoices = false;
            SelectedDatePreset = "الكل";
            SearchText = target.InvoiceNumber;
            SelectedInvoice = target;
            SetStatus($"تم فتح الفاتورة {target.InvoiceNumber} الخاصة بالعملية {op.OperationNumber}.", false);
        });

        this.sharedInvoices.CollectionChanged += OnExternalCollectionChanged;
        this.sharedCatalog.CollectionChanged += OnReplacementCatalogChanged;
        this.sharedOperations.CollectionChanged += OnOperationsCollectionChanged;

        RefreshFilteredInvoices();
        RefreshReplacementCatalog();
        RefreshFilteredOperations();
        SelectedInvoice = FilteredInvoices.FirstOrDefault();
    }

    private void OnExternalCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFilteredInvoices();

    private void OnReplacementCatalogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshReplacementCatalog();

    private void OnOperationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFilteredOperations();
        NotifyOperationKpis();
    }

    private void RebuildWorkspaceForSelectedInvoice()
    {
        InvoiceLines.Clear();
        ReplacementItems.Clear();
        OperationReason = "";
        RefundReference = "";
        DiffCashText = "";
        DiffVodafoneText = "";
        DiffVodafoneReference = "";
        DiffInstaPayText = "";
        DiffInstaPayReference = "";

        if (SelectedInvoice is not null)
        {
            replacementDefaultTier = SelectedInvoice.DefaultPriceTier;
            Changed(nameof(ReplacementDefaultTier));
            refundMethod = SelectedInvoice.HasRemaining ? "خصم من المتبقي الآجل" : "كاش (نقدي)";
            Changed(nameof(RefundMethod));
            Changed(nameof(NeedsRefundReference));

            foreach (var line in SelectedInvoice.Items.Where(i => i.RemainingQuantity > 0))
                InvoiceLines.Add(new ReturnExchangeInvoiceLineViewModel(line, NotifySettlementChanged));
        }

        Changed(nameof(HasReplacementItems));
        Changed(nameof(CanOperateOnSelected));
        NotifySettlementChanged();
    }

    private void AddReplacementProduct(PosProductDesignItem? product)
    {
        if (product is null) return;
        if (product.AvailableStock <= 0)
        {
            SetStatus($"الصنف «{product.Name}» غير متاح حاليًا في المخزون (الرصيد 0).", true);
            return;
        }

        var existing = ReplacementItems.FirstOrDefault(r =>
            r.Product.Id == product.Id && r.SelectedPriceTier == ReplacementDefaultTier);
        if (existing is not null)
        {
            if (existing.Quantity >= product.AvailableStock)
            {
                SetStatus($"تم بلوغ الحد الأقصى المتاح بالمخزون لصنف «{product.Name}» ({product.AvailableStock}).", true);
                return;
            }
            existing.Quantity++;
        }
        else
        {
            var entry = new ExchangeReplacementCartItemViewModel(
                product,
                ReplacementDefaultTier,
                NotifySettlementChanged,
                item =>
                {
                    ReplacementItems.Remove(item);
                    Changed(nameof(HasReplacementItems));
                    NotifySettlementChanged();
                });
            ReplacementItems.Add(entry);
        }

        Changed(nameof(HasReplacementItems));
        NotifySettlementChanged();
    }

    private void ExecuteReturnOrExchange()
    {
        if (SelectedInvoice is null)
        {
            SetStatus("اختر فاتورة أولاً من نتائج البحث.", true);
            return;
        }

        if (!SelectedInvoice.CanReturn)
        {
            SetStatus($"الفاتورة {SelectedInvoice.InvoiceNumber} غير قابلة للمرتجع أو الاستبدال في حالتها الحالية ({SelectedInvoice.StatusTitle}).", true);
            return;
        }

        var selectedLines = InvoiceLines.Where(l => l.TotalSelectedReturnQty > 0).ToList();
        if (selectedLines.Count == 0)
        {
            SetStatus("حدد كمية واحدة على الأقل للإرجاع (سليمة أو هالكة) من بنود الفاتورة.", true);
            return;
        }

        if (IsExchangeMode && ReplacementItems.Count == 0)
        {
            SetStatus("في وضع الاستبدال يجب اختيار صنف بديل واحد على الأقل من الكتالوج.", true);
            return;
        }

        foreach (var rep in ReplacementItems)
        {
            var totalRequestedForProduct = ReplacementItems
                .Where(r => r.Product.Id == rep.Product.Id)
                .Sum(r => r.Quantity);
            if (totalRequestedForProduct > rep.Product.AvailableStock)
            {
                SetStatus($"الكمية المطلوبة من الصنف البديل «{rep.Product.Name}» ({totalRequestedForProduct}) تتجاوز المتاح بالمخزون ({rep.Product.AvailableStock}).", true);
                return;
            }
        }

        var trimmedReason = OperationReason.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
        {
            SetStatus("يرجى تحديد أو كتابة سبب المرتجع / الاستبدال.", true);
            return;
        }

        if (NeedsCustomerPayment)
        {
            if (TotalDiffPaid > SettlementMagnitude)
            {
                SetStatus($"المبلغ المدفوع ({TotalDiffPaidDisplay}) أكبر من فرق الاستبدال المستحق ({SettlementMagnitudeDisplay}).", true);
                return;
            }

            if (SelectedInvoice.IsWalkInCustomer && DiffRemaining > 0m)
            {
                SetStatus($"يجب تحصيل فرق الاستبدال بالكامل ({SettlementMagnitudeDisplay}) لأن الفاتورة لعميل نقدي عام.", true);
                return;
            }
        }

        var invoice = SelectedInvoice;
        var opId = nextOperationId++;
        var opNo = IsExchangeMode ? $"EXC-2026-{opId:0000}" : $"RET-2026-{opId:0000}";
        var now = DateTime.Now;

        var goodUnitsTotal = 0;
        var damagedUnitsTotal = 0;
        var returnedSummaryParts = new List<string>();

        // 1) Process returned lines (split per line: good -> Available stock, damaged -> Damaged stock)
        foreach (var lineVm in selectedLines)
        {
            var qtyToReturn = lineVm.TotalSelectedReturnQty;
            var goodQty = lineVm.GoodReturnQty;
            var damagedQty = lineVm.DamagedReturnQty;
            goodUnitsTotal += goodQty;
            damagedUnitsTotal += damagedQty;

            lineVm.Line.ReturnedQuantity += qtyToReturn;

            var product = sharedCatalog.FirstOrDefault(p => p.Id == lineVm.Line.ProductId);
            var matchedSoldRegs = sharedRegistrations
                .Where(r => (lineVm.Line.UnitRegistrationIds.Contains(r.Id)
                             || (r.ProductId == lineVm.Line.ProductId && r.SaleInvoiceNumber == invoice.InvoiceNumber))
                            && r.IsSold)
                .Take(qtyToReturn)
                .ToList();

            var regCursor = 0;

            // Good units return to Available stock
            for (var i = 0; i < goodQty; i++)
            {
                if (regCursor < matchedSoldRegs.Count)
                {
                    var reg = matchedSoldRegs[regCursor++];
                    reg.Status = InventoryUnitStatus.Available;
                    reg.SaleInvoiceNumber = "";
                }
                else
                {
                    sharedRegistrations.Add(new InventoryUnitRegistrationItem
                    {
                        Id = nextRegistrationId++,
                        ProductId = lineVm.Line.ProductId,
                        ProductName = lineVm.Line.ProductName,
                        ProductBarcode = lineVm.Line.Barcode,
                        PurchasePrice = lineVm.Line.UnitCost,
                        SupplierName = $"مرتجع عميل ({invoice.InvoiceNumber})",
                        RegisteredAt = DateTime.Today,
                        Status = InventoryUnitStatus.Available,
                        PurchaseInvoiceId = invoice.InvoiceNumber
                    });
                }
            }

            // Damaged units move directly to Damaged stock with reason & settlement status
            for (var i = 0; i < damagedQty; i++)
            {
                if (regCursor < matchedSoldRegs.Count)
                {
                    var reg = matchedSoldRegs[regCursor++];
                    reg.Status = InventoryUnitStatus.Damaged;
                    reg.SaleInvoiceNumber = "";
                    reg.DamageReason = $"{(IsExchangeMode ? "استبدال" : "مرتجع")} {invoice.InvoiceNumber}: {trimmedReason}";
                    reg.DamagedAt = DateTime.Today;
                    reg.DamageSettlementStatus = "قيد المراجعة";
                }
                else
                {
                    sharedRegistrations.Add(new InventoryUnitRegistrationItem
                    {
                        Id = nextRegistrationId++,
                        ProductId = lineVm.Line.ProductId,
                        ProductName = lineVm.Line.ProductName,
                        ProductBarcode = lineVm.Line.Barcode,
                        PurchasePrice = lineVm.Line.UnitCost,
                        SupplierName = $"مرتجع هالك ({invoice.InvoiceNumber})",
                        RegisteredAt = DateTime.Today,
                        Status = InventoryUnitStatus.Damaged,
                        PurchaseInvoiceId = invoice.InvoiceNumber,
                        DamageReason = $"{(IsExchangeMode ? "استبدال" : "مرتجع")} {invoice.InvoiceNumber}: {trimmedReason}",
                        DamagedAt = DateTime.Today,
                        DamageSettlementStatus = "قيد المراجعة"
                    });
                }
            }

            if (product is not null)
            {
                product.AvailableStock = sharedRegistrations.Count(r => r.ProductId == product.Id && r.IsAvailable);
                product.DamagedCount = sharedRegistrations.Count(r => r.ProductId == product.Id && r.IsDamaged);
            }

            var condDesc = (goodQty, damagedQty) switch
            {
                (> 0, > 0) => $"{goodQty} سليم + {damagedQty} هالك",
                (> 0, 0) => $"{goodQty} سليم ← المخزون",
                _ => $"{damagedQty} هالك ← مخزون الهالك"
            };
            returnedSummaryParts.Add($"{lineVm.Line.ProductName} ({condDesc})");
        }

        // 2) Process replacement lines in Exchange mode
        var replacementUnitsTotal = 0;
        var replacementSummaryParts = new List<string>();
        var nextLineNumber = invoice.Items.Count > 0 ? invoice.Items.Max(i => i.LineNumber) + 1 : 1;

        if (IsExchangeMode)
        {
            foreach (var rep in ReplacementItems)
            {
                replacementUnitsTotal += rep.Quantity;
                var claimedIds = new List<int>();
                var serials = new List<string>();

                var availRegs = sharedRegistrations
                    .Where(r => r.ProductId == rep.Product.Id && r.IsAvailable)
                    .OrderBy(r => r.Id)
                    .Take(rep.Quantity)
                    .ToList();

                foreach (var unit in availRegs)
                {
                    unit.Status = InventoryUnitStatus.Sold;
                    unit.SaleInvoiceNumber = invoice.InvoiceNumber;
                    claimedIds.Add(unit.Id);
                    if (!string.IsNullOrWhiteSpace(unit.SerialNumber))
                        serials.Add(unit.SerialNumber);
                }

                if (availRegs.Count > 0)
                    rep.Product.AvailableStock = sharedRegistrations.Count(r => r.ProductId == rep.Product.Id && r.IsAvailable);
                else
                    rep.Product.AvailableStock = Math.Max(0, rep.Product.AvailableStock - rep.Quantity);

                var unitCost = availRegs.Count > 0
                    ? Math.Round(availRegs.Average(u => u.PurchasePrice), 2)
                    : rep.Product.CostPrice;

                invoice.Items.Add(new SalesInvoiceLineDesignItem
                {
                    LineNumber = nextLineNumber++,
                    ProductId = rep.Product.Id,
                    Barcode = rep.Product.Barcode,
                    ProductName = rep.Product.Name,
                    Category = rep.Product.Category,
                    Unit = rep.Product.UnitDisplay,
                    TrackSerialNumber = rep.Product.TrackSerialNumber,
                    UnitRegistrationIds = claimedIds,
                    SerialNumbers = serials,
                    PriceTier = rep.SelectedPriceTier,
                    Quantity = rep.Quantity,
                    UnitPrice = rep.UnitPrice,
                    UnitCost = unitCost,
                    IsExchangeReplacement = true
                });

                replacementSummaryParts.Add($"{rep.Quantity}× {rep.Product.Name} ({rep.LineTotalDisplay})");
            }
        }

        // 3) Financial settlement & status update
        var returnedValue = TotalReturnedValue;
        var replacementValue = TotalReplacementValue;
        string settlementDesc;

        if (NeedsCustomerRefund)
        {
            var refundAmount = SettlementMagnitude;
            if (RefundMethod == "خصم من المتبقي الآجل" && invoice.HasRemaining)
            {
                var creditCovered = Math.Min(refundAmount, invoice.RemainingAmount);
                var extraCash = Math.Max(0m, refundAmount - creditCovered);
                if (extraCash > 0m)
                    invoice.RefundedAmount += extraCash;
                settlementDesc = extraCash > 0m
                    ? $"خصم {PosFormatting.FormatCurrency(creditCovered)} من الآجل + رد {PosFormatting.FormatCurrency(extraCash)}"
                    : $"خصم {PosFormatting.FormatCurrency(creditCovered)} من المتبقي الآجل";
            }
            else
            {
                invoice.RefundedAmount += refundAmount;
                var refSuffix = NeedsRefundReference && !string.IsNullOrWhiteSpace(RefundReference)
                    ? $" [مرجع: {RefundReference.Trim()}]"
                    : "";
                settlementDesc = $"رد للعميل: {PosFormatting.FormatCurrency(refundAmount)} ({RefundMethod}{refSuffix})";
            }
        }
        else if (NeedsCustomerPayment)
        {
            invoice.CashPaid += ParsedDiffCash;
            invoice.VodafoneCashPaid += ParsedDiffVodafone;
            if (!string.IsNullOrWhiteSpace(DiffVodafoneReference))
            {
                invoice.VodafoneCashReference = string.IsNullOrWhiteSpace(invoice.VodafoneCashReference)
                    ? DiffVodafoneReference.Trim()
                    : $"{invoice.VodafoneCashReference} / {DiffVodafoneReference.Trim()}";
            }

            invoice.InstaPayPaid += ParsedDiffInstaPay;
            if (!string.IsNullOrWhiteSpace(DiffInstaPayReference))
            {
                invoice.InstaPayReference = string.IsNullOrWhiteSpace(invoice.InstaPayReference)
                    ? DiffInstaPayReference.Trim()
                    : $"{invoice.InstaPayReference} / {DiffInstaPayReference.Trim()}";
            }

            var payParts = new List<string>(3);
            if (ParsedDiffCash > 0m) payParts.Add($"كاش {PosFormatting.FormatCurrency(ParsedDiffCash)}");
            if (ParsedDiffVodafone > 0m) payParts.Add($"فودافون {PosFormatting.FormatCurrency(ParsedDiffVodafone)}");
            if (ParsedDiffInstaPay > 0m) payParts.Add($"إنستاباي {PosFormatting.FormatCurrency(ParsedDiffInstaPay)}");

            settlementDesc = DiffRemaining > 0m
                ? $"تحصيل فرق {TotalDiffPaidDisplay} ({string.Join(" + ", payParts)}) + ترحيل {DiffRemainingDisplay} آجل"
                : $"تحصيل فرق {TotalDiffPaidDisplay} ({string.Join(" + ", payParts)})";
        }
        else
        {
            settlementDesc = "استبدال متساوي القيمة (0.00 ج.م)";
        }

        if (IsExchangeMode)
        {
            invoice.Status = SalesInvoiceStatus.Exchanged;
        }
        else
        {
            invoice.Status = invoice.Items.Any(i => i.RemainingQuantity > 0)
                ? SalesInvoiceStatus.PartiallyReturned
                : SalesInvoiceStatus.FullyReturned;
        }

        invoice.NotifyFinancialsChanged();

        var returnedJoined = string.Join("، ", returnedSummaryParts);
        var replacementJoined = replacementSummaryParts.Count > 0 ? string.Join("، ", replacementSummaryParts) : "—";

        invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
        {
            Timestamp = now,
            ActionTitle = IsExchangeMode ? $"عملية استبدال ({opNo})" : $"عملية مرتجع ({opNo})",
            Details = IsExchangeMode
                ? $"الراجع: {returnedJoined} | البديل: {replacementJoined} | السبب: {trimmedReason} | التسوية: {settlementDesc}."
                : $"المرتجع: {returnedJoined} | السبب: {trimmedReason} | التسوية: {settlementDesc}.",
            ActorName = currentCashierName,
            Amount = IsExchangeMode ? Math.Max(returnedValue, replacementValue) : returnedValue
        });

        var record = new ReturnExchangeOperationItem
        {
            Id = opId,
            OperationNumber = opNo,
            OperationType = IsExchangeMode ? ReturnExchangeType.Exchange : ReturnExchangeType.Return,
            CreatedAt = now,
            ActorName = currentCashierName,
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            CustomerName = invoice.CustomerName,
            CustomerPhone = invoice.CustomerPhone,
            Reason = trimmedReason,
            GoodReturnedUnitsCount = goodUnitsTotal,
            DamagedReturnedUnitsCount = damagedUnitsTotal,
            ReplacementUnitsCount = replacementUnitsTotal,
            ReturnedValue = returnedValue,
            ReplacementValue = replacementValue,
            ReturnedItemsSummary = returnedJoined,
            ReplacementItemsSummary = replacementJoined,
            SettlementSummary = settlementDesc
        };

        sharedOperations.Insert(0, record);
        notifyInventoryChanged?.Invoke();
        notifyInvoicesChanged?.Invoke();
        RefreshFilteredInvoices();
        RefreshReplacementCatalog();
        RebuildWorkspaceForSelectedInvoice();
        NotifyOperationKpis();

        SetStatus(
            $"تم تنفيذ {(IsExchangeMode ? "الاستبدال" : "المرتجع")} ({opNo}) على الفاتورة {invoice.InvoiceNumber}: {goodUnitsTotal} سليم للمخزون، {damagedUnitsTotal} هالك — {settlementDesc}.",
            false);
    }

    private void NotifySettlementChanged()
    {
        Changed(nameof(TotalGoodUnitsSelected));
        Changed(nameof(TotalDamagedUnitsSelected));
        Changed(nameof(TotalReturnedUnitsSelected));
        Changed(nameof(TotalReplacementUnitsSelected));
        Changed(nameof(TotalReturnedValue));
        Changed(nameof(TotalReplacementValue));
        Changed(nameof(NetDifference));
        Changed(nameof(HasAnySelectedReturn));
        Changed(nameof(NeedsCustomerRefund));
        Changed(nameof(NeedsCustomerPayment));
        Changed(nameof(IsEvenExchange));
        Changed(nameof(SettlementMagnitude));
        Changed(nameof(TotalReturnedValueDisplay));
        Changed(nameof(TotalReplacementValueDisplay));
        Changed(nameof(SettlementMagnitudeDisplay));
        Changed(nameof(SettlementDirectionLabel));
        NotifyPaymentDiffInputs();
    }

    private void NotifyPaymentDiffInputs()
    {
        Changed(nameof(TotalDiffPaid));
        Changed(nameof(DiffRemaining));
        Changed(nameof(TotalDiffPaidDisplay));
        Changed(nameof(DiffRemainingDisplay));
    }

    private void RefreshFilteredInvoices()
    {
        var query = searchText.Trim();
        var today = DateTime.Today;

        var matches = sharedInvoices.Where(inv =>
        {
            if (OnlyEligibleInvoices && !inv.CanReturn)
                return false;

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
                   || inv.VodafoneCashReference.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || inv.InstaPayReference.Contains(query, StringComparison.OrdinalIgnoreCase)
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

        Changed(nameof(HasNoMatchedInvoices));
        Changed(nameof(MatchedInvoicesCountDisplay));
    }

    private void RefreshReplacementCatalog()
    {
        var query = replacementSearchText.Trim();
        var matches = sharedCatalog.Where(p =>
            p.IsActiveForSale
            && p.AvailableStock > 0
            && (string.IsNullOrEmpty(query)
                || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Category.Contains(query, StringComparison.OrdinalIgnoreCase)));

        FilteredReplacementCatalog.Clear();
        foreach (var item in matches)
            FilteredReplacementCatalog.Add(item);

        Changed(nameof(HasNoReplacementResults));
    }

    private void RefreshFilteredOperations()
    {
        var query = historySearchText.Trim();
        var matches = sharedOperations.Where(op =>
        {
            if (SelectedHistoryTypeFilter == "مرتجع" && !op.IsReturn) return false;
            if (SelectedHistoryTypeFilter == "استبدال" && !op.IsExchange) return false;

            if (string.IsNullOrEmpty(query)) return true;

            return op.OperationNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || op.InvoiceNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || op.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || op.CustomerPhone.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || op.Reason.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || op.ReturnedItemsSummary.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || op.ReplacementItemsSummary.Contains(query, StringComparison.OrdinalIgnoreCase);
        }).OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);

        FilteredOperations.Clear();
        foreach (var item in matches)
            FilteredOperations.Add(item);

        Changed(nameof(HasNoHistoryRecords));
        NotifyOperationKpis();
    }

    private void NotifyOperationKpis()
    {
        Changed(nameof(HistoryTabLabel));
        Changed(nameof(TotalOperationsCount));
        Changed(nameof(OperationsKpiSummary));
        Changed(nameof(TotalGoodReturnedKpi));
        Changed(nameof(TotalDamagedReturnedKpi));
        Changed(nameof(TotalRefundedValueKpiDisplay));
        Changed(nameof(TotalExchangeUpgradesKpiSubtext));
    }

    public void NotifyExternalDataChanged()
    {
        RefreshFilteredInvoices();
        RefreshReplacementCatalog();
        RefreshFilteredOperations();
        Changed(nameof(CanOperateOnSelected));
        RebuildWorkspaceForSelectedInvoice();
        NotifyOperationKpis();
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
