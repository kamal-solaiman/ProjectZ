using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PurchaseDraftInvoiceViewModel : ObservableObject
{
    private readonly ObservableCollection<SupplierDesignItem> allSuppliers;
    private readonly Action<string, bool>? notifyStatus;

    private bool isActive;
    private SupplierDesignItem? selectedSupplier;
    private string supplierSearchText = "";
    private bool isSupplierPickerOpen;
    private string supplierInvoiceNumber = "";
    private DateTime purchaseDate = DateTime.Today;
    private string notes = "";

    private string discountAmountText = "";
    private decimal discountAmount;

    private PurchasePaymentPresetMode activePaymentPreset = PurchasePaymentPresetMode.CashOnly;
    private bool isCashEnabled = true;
    private bool isVodafoneCashEnabled;
    private bool isInstaPayEnabled;

    private string cashAmountText = "";
    private decimal cashAmount;
    private string vodafoneCashAmountText = "";
    private decimal vodafoneCashAmount;
    private string instaPayAmountText = "";
    private decimal instaPayAmount;

    private bool isUpdatingPayments;
    private bool hasManualSinglePaymentEdit;

    public int DraftNumber { get; }

    public bool IsActive
    {
        get => isActive;
        internal set
        {
            if (isActive == value) return;
            isActive = value;
            Changed();
        }
    }

    public string TabTitle => $"فاتورة شراء #{DraftNumber}";

    public string TabItemsSummary => Items.Count == 0
        ? "فاتورة فارغة"
        : $"{Items.Count} {(Items.Count == 1 ? "صنف" : "أصناف")} · {NetTotalDisplay}";

    public ObservableCollection<PurchaseInvoiceItemViewModel> Items { get; } = [];
    public ObservableCollection<SupplierDesignItem> FilteredSuppliers { get; } = [];

    public SupplierDesignItem? SelectedSupplier
    {
        get => selectedSupplier;
        private set
        {
            if (ReferenceEquals(selectedSupplier, value)) return;
            selectedSupplier = value;
            Changed();
            Changed(nameof(HasSelectedSupplier));
            Changed(nameof(NeedsSupplierSelection));
        }
    }

    public bool HasSelectedSupplier => SelectedSupplier is not null;
    public bool NeedsSupplierSelection => SelectedSupplier is null;

    public string SupplierSearchText
    {
        get => supplierSearchText;
        set
        {
            if (supplierSearchText == value) return;
            supplierSearchText = value;
            Changed();
            if (!string.IsNullOrWhiteSpace(supplierSearchText))
                isSupplierPickerOpen = true;
            Changed(nameof(IsSupplierPickerVisible));
            RefreshFilteredSuppliers();
        }
    }

    public bool IsSupplierPickerOpen
    {
        get => isSupplierPickerOpen;
        set
        {
            if (isSupplierPickerOpen == value) return;
            isSupplierPickerOpen = value;
            Changed();
            Changed(nameof(IsSupplierPickerVisible));
            if (isSupplierPickerOpen)
                RefreshFilteredSuppliers();
        }
    }

    public bool IsSupplierPickerVisible => isSupplierPickerOpen || !string.IsNullOrWhiteSpace(supplierSearchText);
    public bool IsSupplierNotFound => !string.IsNullOrWhiteSpace(supplierSearchText) && FilteredSuppliers.Count == 0;
    public bool HasFilteredSuppliers => FilteredSuppliers.Count > 0;

    public string SupplierInvoiceNumber
    {
        get => supplierInvoiceNumber;
        set
        {
            if (supplierInvoiceNumber == value) return;
            supplierInvoiceNumber = value;
            Changed();
        }
    }

    public DateTime PurchaseDate
    {
        get => purchaseDate;
        set
        {
            if (purchaseDate == value) return;
            purchaseDate = value;
            Changed();
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (notes == value) return;
            notes = value;
            Changed();
        }
    }

    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0;
    public int TotalUnitsCount => Items.Sum(item => item.Quantity);
    public string ItemsCountSummary => $"{Items.Count} صنف ({TotalUnitsCount} قطعة)";

    public decimal Subtotal => Items.Sum(item => item.LineTotal);
    public string SubtotalDisplay => PosFormatting.FormatCurrency(Subtotal);

    public string DiscountAmountText
    {
        get => discountAmountText;
        set
        {
            if (discountAmountText == value) return;
            discountAmountText = value;
            Changed();
            if (PosFormatting.TryParseAmount(value, out var parsed) && parsed >= 0m)
            {
                discountAmount = parsed;
                NotifyTotalsChanged(syncSinglePayment: true);
            }
            else if (string.IsNullOrWhiteSpace(value))
            {
                discountAmount = 0m;
                NotifyTotalsChanged(syncSinglePayment: true);
            }
        }
    }

    public decimal EffectiveDiscount => Math.Min(Subtotal, discountAmount);
    public decimal NetTotal => Math.Max(0m, Subtotal - EffectiveDiscount);
    public string DiscountDisplay => PosFormatting.FormatCurrency(EffectiveDiscount);
    public string NetTotalDisplay => PosFormatting.FormatCurrency(NetTotal);

    public PurchasePaymentPresetMode ActivePaymentPreset
    {
        get => activePaymentPreset;
        private set
        {
            if (activePaymentPreset == value) return;
            activePaymentPreset = value;
            Changed();
            Changed(nameof(IsCashPresetSelected));
            Changed(nameof(IsVodafonePresetSelected));
            Changed(nameof(IsInstaPayPresetSelected));
            Changed(nameof(IsSplitPresetSelected));
            Changed(nameof(IsFullCreditPresetSelected));
        }
    }

    public bool IsCashPresetSelected => ActivePaymentPreset == PurchasePaymentPresetMode.CashOnly;
    public bool IsVodafonePresetSelected => ActivePaymentPreset == PurchasePaymentPresetMode.VodafoneCashOnly;
    public bool IsInstaPayPresetSelected => ActivePaymentPreset == PurchasePaymentPresetMode.InstaPayOnly;
    public bool IsSplitPresetSelected => ActivePaymentPreset == PurchasePaymentPresetMode.Split;
    public bool IsFullCreditPresetSelected => ActivePaymentPreset == PurchasePaymentPresetMode.FullCredit;

    public bool IsCashEnabled
    {
        get => isCashEnabled;
        set
        {
            if (isCashEnabled == value) return;
            isCashEnabled = value;
            Changed();
            if (!isUpdatingPayments)
            {
                if (!value) SetCashTextProgrammatic("");
                UpdatePresetFromEnabledChannels();
                NotifyPaymentSummaryChanged();
            }
        }
    }

    public bool IsVodafoneCashEnabled
    {
        get => isVodafoneCashEnabled;
        set
        {
            if (isVodafoneCashEnabled == value) return;
            isVodafoneCashEnabled = value;
            Changed();
            if (!isUpdatingPayments)
            {
                if (!value)
                {
                    SetVodafoneTextProgrammatic("");;
                }
                UpdatePresetFromEnabledChannels();
                NotifyPaymentSummaryChanged();
            }
        }
    }

    public bool IsInstaPayEnabled
    {
        get => isInstaPayEnabled;
        set
        {
            if (isInstaPayEnabled == value) return;
            isInstaPayEnabled = value;
            Changed();
            if (!isUpdatingPayments)
            {
                if (!value)
                {
                    SetInstaPayTextProgrammatic("");;
                }
                UpdatePresetFromEnabledChannels();
                NotifyPaymentSummaryChanged();
            }
        }
    }

    public string CashAmountText
    {
        get => cashAmountText;
        set
        {
            if (cashAmountText == value) return;
            cashAmountText = value;
            Changed();
            cashAmount = PosFormatting.TryParseAmount(value, out var parsed) ? Math.Max(0m, parsed) : 0m;
            if (!isUpdatingPayments)
            {
                hasManualSinglePaymentEdit = true;
                if (cashAmount > 0m && !IsCashEnabled)
                {
                    isCashEnabled = true;
                    Changed(nameof(IsCashEnabled));
                }
                UpdatePresetFromEnabledChannels();
            }
            NotifyPaymentSummaryChanged();
        }
    }

    public decimal CashAmount => cashAmount;

    public string VodafoneCashAmountText
    {
        get => vodafoneCashAmountText;
        set
        {
            if (vodafoneCashAmountText == value) return;
            vodafoneCashAmountText = value;
            Changed();
            vodafoneCashAmount = PosFormatting.TryParseAmount(value, out var parsed) ? Math.Max(0m, parsed) : 0m;
            if (!isUpdatingPayments)
            {
                hasManualSinglePaymentEdit = true;
                if (vodafoneCashAmount > 0m && !IsVodafoneCashEnabled)
                {
                    isVodafoneCashEnabled = true;
                    Changed(nameof(IsVodafoneCashEnabled));
                }
                UpdatePresetFromEnabledChannels();
            }
            NotifyPaymentSummaryChanged();
        }
    }

    public decimal VodafoneCashAmount => vodafoneCashAmount;

    public string InstaPayAmountText
    {
        get => instaPayAmountText;
        set
        {
            if (instaPayAmountText == value) return;
            instaPayAmountText = value;
            Changed();
            instaPayAmount = PosFormatting.TryParseAmount(value, out var parsed) ? Math.Max(0m, parsed) : 0m;
            if (!isUpdatingPayments)
            {
                hasManualSinglePaymentEdit = true;
                if (instaPayAmount > 0m && !IsInstaPayEnabled)
                {
                    isInstaPayEnabled = true;
                    Changed(nameof(IsInstaPayEnabled));
                }
                UpdatePresetFromEnabledChannels();
            }
            NotifyPaymentSummaryChanged();
        }
    }

    public decimal InstaPayAmount => instaPayAmount;

    public decimal TotalPaid => CashAmount + VodafoneCashAmount + InstaPayAmount;
    public decimal RemainingPayable => Math.Max(0m, NetTotal - TotalPaid);
    public decimal OverpaidAmount => Math.Max(0m, TotalPaid - NetTotal);
    public bool HasRemainingPayable => NetTotal > 0m && RemainingPayable > 0m;
    public bool HasOverpayment => OverpaidAmount > 0m;
    public bool IsFullyPaid => NetTotal > 0m && RemainingPayable == 0m && OverpaidAmount == 0m;

    public string TotalPaidDisplay => PosFormatting.FormatCurrency(TotalPaid);
    public string RemainingPayableDisplay => PosFormatting.FormatCurrency(RemainingPayable);
    public string OverpaidAmountDisplay => PosFormatting.FormatCurrency(OverpaidAmount);

    public string DraftStatusTitle
    {
        get
        {
            if (IsEmpty || NetTotal <= 0m) return "بانتظار إضافة أصناف";
            if (HasOverpayment) return "المدفوع يتجاوز الصافي المستحق";
            if (TotalPaid <= 0m) return "آجل بالكامل للمورد";
            if (RemainingPayable > 0m) return "مدفوعة جزئيًا (متبقي آجل للمورد)";
            return "مدفوعة بالكامل";
        }
    }

    public string PaymentMixSummary
    {
        get
        {
            if (NetTotal <= 0m) return "لا توجد مبالغ بعد";
            var parts = new List<string>(3);
            if (CashAmount > 0m) parts.Add($"كاش: {PosFormatting.FormatCurrency(CashAmount)}");
            if (VodafoneCashAmount > 0m) parts.Add($"فودافون كاش: {PosFormatting.FormatCurrency(VodafoneCashAmount)}");
            if (InstaPayAmount > 0m) parts.Add($"إنستاباي: {PosFormatting.FormatCurrency(InstaPayAmount)}");

            if (parts.Count == 0)
                return $"آجل بالكامل للمورد ({RemainingPayableDisplay})";

            var joined = string.Join(" + ", parts);
            if (RemainingPayable > 0m)
                return $"{joined} | آجل: {RemainingPayableDisplay}";

            return joined;
        }
    }

    public ActionCommand ToggleSupplierPickerCommand { get; }
    public ActionCommand ClearSupplierSearchCommand { get; }
    public ActionCommand ClearSupplierSelectionCommand { get; }
    public RelayCommand<SupplierDesignItem> SelectSupplierCommand { get; }

    public RelayCommand<PurchaseInvoiceItemViewModel> IncrementItemCommand { get; }
    public RelayCommand<PurchaseInvoiceItemViewModel> DecrementItemCommand { get; }
    public RelayCommand<PurchaseInvoiceItemViewModel> RemoveItemCommand { get; }
    public ActionCommand ClearInvoiceCommand { get; }

    public ActionCommand SetCashPresetCommand { get; }
    public ActionCommand SetVodafonePresetCommand { get; }
    public ActionCommand SetInstaPayPresetCommand { get; }
    public ActionCommand SetSplitPresetCommand { get; }
    public ActionCommand SetFullCreditPresetCommand { get; }

    public ActionCommand FillCashRemainingCommand { get; }
    public ActionCommand FillVodafoneRemainingCommand { get; }
    public ActionCommand FillInstaPayRemainingCommand { get; }

    public PurchaseDraftInvoiceViewModel(
        int draftNumber,
        ObservableCollection<SupplierDesignItem> allSuppliers,
        Action<string, bool>? notifyStatus = null)
    {
        DraftNumber = draftNumber;
        this.allSuppliers = allSuppliers;
        this.notifyStatus = notifyStatus;

        allSuppliers.CollectionChanged += OnSuppliersCollectionChanged;

        ToggleSupplierPickerCommand = new ActionCommand(() => IsSupplierPickerOpen = !IsSupplierPickerOpen);
        ClearSupplierSearchCommand = new ActionCommand(() =>
        {
            SupplierSearchText = "";
            IsSupplierPickerOpen = false;
        });
        ClearSupplierSelectionCommand = new ActionCommand(ClearSupplierSelection);
        SelectSupplierCommand = new RelayCommand<SupplierDesignItem>(supplier =>
        {
            if (supplier is not null)
                SelectSupplier(supplier);
        });

        IncrementItemCommand = new RelayCommand<PurchaseInvoiceItemViewModel>(item =>
        {
            if (item is not null)
                item.Quantity++;
        });
        DecrementItemCommand = new RelayCommand<PurchaseInvoiceItemViewModel>(item =>
        {
            if (item is null) return;
            if (item.Quantity > 1)
                item.Quantity--;
            else
                RemoveItem(item);
        });
        RemoveItemCommand = new RelayCommand<PurchaseInvoiceItemViewModel>(item =>
        {
            if (item is not null)
                RemoveItem(item);
        });
        ClearInvoiceCommand = new ActionCommand(ClearInvoice);

        SetCashPresetCommand = new ActionCommand(() => ApplyPaymentPreset(PurchasePaymentPresetMode.CashOnly));
        SetVodafonePresetCommand = new ActionCommand(() => ApplyPaymentPreset(PurchasePaymentPresetMode.VodafoneCashOnly));
        SetInstaPayPresetCommand = new ActionCommand(() => ApplyPaymentPreset(PurchasePaymentPresetMode.InstaPayOnly));
        SetSplitPresetCommand = new ActionCommand(() => ApplyPaymentPreset(PurchasePaymentPresetMode.Split));
        SetFullCreditPresetCommand = new ActionCommand(() => ApplyPaymentPreset(PurchasePaymentPresetMode.FullCredit));

        FillCashRemainingCommand = new ActionCommand(() => FillRemainingIntoChannel(PurchasePaymentPresetMode.CashOnly));
        FillVodafoneRemainingCommand = new ActionCommand(() => FillRemainingIntoChannel(PurchasePaymentPresetMode.VodafoneCashOnly));
        FillInstaPayRemainingCommand = new ActionCommand(() => FillRemainingIntoChannel(PurchasePaymentPresetMode.InstaPayOnly));

        RefreshFilteredSuppliers();
    }

    internal void Detach()
        => allSuppliers.CollectionChanged -= OnSuppliersCollectionChanged;

    /// <summary>Drops the chosen supplier, e.g. when a SQL reload removed it from the directory.</summary>
    public void ClearSupplierSelection()
    {
        SelectedSupplier = null;
        IsSupplierPickerOpen = true;
    }

    public void AddProduct(PosProductDesignItem product, int quantity = 1)
    {
        var existing = Items.FirstOrDefault(item => item.Product.Id == product.Id);
        if (existing is not null)
        {
            existing.Quantity += Math.Max(1, quantity);
        }
        else
        {
            var item = new PurchaseInvoiceItemViewModel(
                product,
                Items.Count + 1,
                quantity,
                OnItemChanged);
            Items.Add(item);
        }

        RenumberItems();
        NotifyTotalsChanged(syncSinglePayment: true);
    }

    public void SelectSupplier(SupplierDesignItem supplier)
    {
        SelectedSupplier = supplier;
        supplierSearchText = "";
        Changed(nameof(SupplierSearchText));
        IsSupplierPickerOpen = false;
        RefreshFilteredSuppliers();
    }

    public void ApplyPaymentPreset(PurchasePaymentPresetMode mode)
    {
        isUpdatingPayments = true;
        try
        {
            hasManualSinglePaymentEdit = false;
            ActivePaymentPreset = mode;

            switch (mode)
            {
                case PurchasePaymentPresetMode.CashOnly:
                    SetChannels(cash: true, vodafone: false, instaPay: false);
                    SetCashTextProgrammatic(NetTotal > 0m ? PosFormatting.FormatPlainNumber(NetTotal) : "");
                    SetVodafoneTextProgrammatic("");
                    SetInstaPayTextProgrammatic("");
                    break;

                case PurchasePaymentPresetMode.VodafoneCashOnly:
                    SetChannels(cash: false, vodafone: true, instaPay: false);
                    SetCashTextProgrammatic("");
                    SetVodafoneTextProgrammatic(NetTotal > 0m ? PosFormatting.FormatPlainNumber(NetTotal) : "");
                    SetInstaPayTextProgrammatic("");
                    break;

                case PurchasePaymentPresetMode.InstaPayOnly:
                    SetChannels(cash: false, vodafone: false, instaPay: true);
                    SetCashTextProgrammatic("");
                    SetVodafoneTextProgrammatic("");
                    SetInstaPayTextProgrammatic(NetTotal > 0m ? PosFormatting.FormatPlainNumber(NetTotal) : "");
                    break;

                case PurchasePaymentPresetMode.Split:
                    SetChannels(cash: true, vodafone: true, instaPay: true);
                    break;

                case PurchasePaymentPresetMode.FullCredit:
                    SetChannels(cash: false, vodafone: false, instaPay: false);
                    SetCashTextProgrammatic("");
                    SetVodafoneTextProgrammatic("");
                    SetInstaPayTextProgrammatic("");
                    break;
            }
        }
        finally
        {
            isUpdatingPayments = false;
        }

        NotifyPaymentSummaryChanged();
    }

    private void FillRemainingIntoChannel(PurchasePaymentPresetMode channel)
    {
        if (NetTotal <= 0m) return;

        isUpdatingPayments = true;
        try
        {
            hasManualSinglePaymentEdit = true;
            var currentOtherTotal = channel switch
            {
                PurchasePaymentPresetMode.CashOnly => VodafoneCashAmount + InstaPayAmount,
                PurchasePaymentPresetMode.VodafoneCashOnly => CashAmount + InstaPayAmount,
                PurchasePaymentPresetMode.InstaPayOnly => CashAmount + VodafoneCashAmount,
                _ => 0m
            };

            var targetAmount = Math.Max(0m, NetTotal - currentOtherTotal);
            switch (channel)
            {
                case PurchasePaymentPresetMode.CashOnly:
                    isCashEnabled = true;
                    Changed(nameof(IsCashEnabled));
                    SetCashTextProgrammatic(targetAmount > 0m ? PosFormatting.FormatPlainNumber(targetAmount) : "0");
                    break;
                case PurchasePaymentPresetMode.VodafoneCashOnly:
                    isVodafoneCashEnabled = true;
                    Changed(nameof(IsVodafoneCashEnabled));
                    SetVodafoneTextProgrammatic(targetAmount > 0m ? PosFormatting.FormatPlainNumber(targetAmount) : "0");
                    break;
                case PurchasePaymentPresetMode.InstaPayOnly:
                    isInstaPayEnabled = true;
                    Changed(nameof(IsInstaPayEnabled));
                    SetInstaPayTextProgrammatic(targetAmount > 0m ? PosFormatting.FormatPlainNumber(targetAmount) : "0");
                    break;
            }

            UpdatePresetFromEnabledChannels();
        }
        finally
        {
            isUpdatingPayments = false;
        }

        NotifyPaymentSummaryChanged();
    }

    public void ClearInvoice()
    {
        Items.Clear();
        SelectedSupplier = null;
        SupplierInvoiceNumber = "";
        PurchaseDate = DateTime.Today;
        Notes = "";
        discountAmountText = "";
        discountAmount = 0m;
        Changed(nameof(DiscountAmountText));;;
        hasManualSinglePaymentEdit = false;

        ApplyPaymentPreset(PurchasePaymentPresetMode.CashOnly);
        NotifyTotalsChanged(syncSinglePayment: false);
        notifyStatus?.Invoke($"تم تفريغ مسودة فاتورة الشراء #{DraftNumber}.", false);
    }

    public PurchaseInvoiceDesignItem BuildPreviewSnapshot(string buyerName, string invoiceNumber)
    {
        var sup = SelectedSupplier;
        var status = TotalPaid <= 0m && NetTotal > 0m
            ? PurchaseInvoiceStatus.FullCredit
            : RemainingPayable > 0m
                ? PurchaseInvoiceStatus.PartialCredit
                : PurchaseInvoiceStatus.Paid;

        var item = new PurchaseInvoiceDesignItem
        {
            Id = 0,
            InvoiceNumber = invoiceNumber,
            SupplierInvoiceNumber = SupplierInvoiceNumber.Trim(),
            IssuedAt = PurchaseDate.Date.Add(DateTime.Now.TimeOfDay),
            BuyerName = buyerName,
            SupplierId = sup?.Id ?? 0,
            SupplierName = sup?.Name ?? "لم يُحدد المورد بعد",
            SupplierPhone = sup?.Phone ?? "",
            SupplierCompany = sup?.CompanyName ?? "",
            SupplierAddress = sup?.Address ?? "",
            Status = status,
            DiscountAmount = EffectiveDiscount,
            CashPaid = CashAmount,
            VodafoneCashPaid = VodafoneCashAmount,
            InstaPayPaid = InstaPayAmount,
            Notes = Notes.Trim()
        };

        var idx = 1;
        foreach (var line in Items)
        {
            item.Items.Add(new PurchaseInvoiceLineDesignItem
            {
                LineNumber = idx++,
                ProductId = line.Product.Id,
                Barcode = line.Product.Barcode,
                ProductName = line.Product.Name,
                Category = line.Product.Category,
                Unit = line.Product.UnitDisplay,
                TrackSerialNumber = line.Product.TrackSerialNumber,
                Quantity = line.Quantity,
                UnitPurchaseCost = line.UnitPurchaseCost
            });
        }

        return item;
    }

    private void RemoveItem(PurchaseInvoiceItemViewModel item)
    {
        Items.Remove(item);
        RenumberItems();
        NotifyTotalsChanged(syncSinglePayment: true);
    }

    private void OnItemChanged()
    {
        NotifyTotalsChanged(syncSinglePayment: true);
    }

    private void RenumberItems()
    {
        for (var i = 0; i < Items.Count; i++)
            Items[i].LineNumber = i + 1;
    }

    private void NotifyTotalsChanged(bool syncSinglePayment)
    {
        Changed(nameof(HasItems));
        Changed(nameof(IsEmpty));
        Changed(nameof(TotalUnitsCount));
        Changed(nameof(ItemsCountSummary));
        Changed(nameof(Subtotal));
        Changed(nameof(SubtotalDisplay));
        Changed(nameof(EffectiveDiscount));
        Changed(nameof(DiscountDisplay));
        Changed(nameof(NetTotal));
        Changed(nameof(NetTotalDisplay));
        Changed(nameof(TabItemsSummary));

        if (syncSinglePayment && !hasManualSinglePaymentEdit
            && ActivePaymentPreset is PurchasePaymentPresetMode.CashOnly
                or PurchasePaymentPresetMode.VodafoneCashOnly
                or PurchasePaymentPresetMode.InstaPayOnly)
        {
            SyncSinglePaymentChannelToNetTotal();
        }

        NotifyPaymentSummaryChanged();
    }

    private void SyncSinglePaymentChannelToNetTotal()
    {
        isUpdatingPayments = true;
        try
        {
            var amountString = NetTotal > 0m ? PosFormatting.FormatPlainNumber(NetTotal) : "";
            switch (ActivePaymentPreset)
            {
                case PurchasePaymentPresetMode.CashOnly when IsCashEnabled:
                    SetCashTextProgrammatic(amountString);
                    break;
                case PurchasePaymentPresetMode.VodafoneCashOnly when IsVodafoneCashEnabled:
                    SetVodafoneTextProgrammatic(amountString);
                    break;
                case PurchasePaymentPresetMode.InstaPayOnly when IsInstaPayEnabled:
                    SetInstaPayTextProgrammatic(amountString);
                    break;
            }
        }
        finally
        {
            isUpdatingPayments = false;
        }
    }

    private void UpdatePresetFromEnabledChannels()
    {
        var enabledCount = (IsCashEnabled ? 1 : 0)
            + (IsVodafoneCashEnabled ? 1 : 0)
            + (IsInstaPayEnabled ? 1 : 0);

        if (enabledCount == 0)
        {
            ActivePaymentPreset = PurchasePaymentPresetMode.FullCredit;
            return;
        }

        if (enabledCount >= 2)
        {
            ActivePaymentPreset = PurchasePaymentPresetMode.Split;
            return;
        }

        if (IsCashEnabled)
            ActivePaymentPreset = PurchasePaymentPresetMode.CashOnly;
        else if (IsVodafoneCashEnabled)
            ActivePaymentPreset = PurchasePaymentPresetMode.VodafoneCashOnly;
        else if (IsInstaPayEnabled)
            ActivePaymentPreset = PurchasePaymentPresetMode.InstaPayOnly;
    }

    private void SetChannels(bool cash, bool vodafone, bool instaPay)
    {
        if (isCashEnabled != cash)
        {
            isCashEnabled = cash;
            Changed(nameof(IsCashEnabled));
        }

        if (isVodafoneCashEnabled != vodafone)
        {
            isVodafoneCashEnabled = vodafone;
            Changed(nameof(IsVodafoneCashEnabled));
        }

        if (isInstaPayEnabled != instaPay)
        {
            isInstaPayEnabled = instaPay;
            Changed(nameof(IsInstaPayEnabled));
        }
    }

    private void SetCashTextProgrammatic(string text)
    {
        cashAmountText = text;
        cashAmount = PosFormatting.TryParseAmount(text, out var parsed) ? Math.Max(0m, parsed) : 0m;
        Changed(nameof(CashAmountText));
    }

    private void SetVodafoneTextProgrammatic(string text)
    {
        vodafoneCashAmountText = text;
        vodafoneCashAmount = PosFormatting.TryParseAmount(text, out var parsed) ? Math.Max(0m, parsed) : 0m;
        Changed(nameof(VodafoneCashAmountText));
    }

    private void SetInstaPayTextProgrammatic(string text)
    {
        instaPayAmountText = text;
        instaPayAmount = PosFormatting.TryParseAmount(text, out var parsed) ? Math.Max(0m, parsed) : 0m;
        Changed(nameof(InstaPayAmountText));
    }

    private void NotifyPaymentSummaryChanged()
    {
        Changed(nameof(CashAmount));
        Changed(nameof(VodafoneCashAmount));
        Changed(nameof(InstaPayAmount));
        Changed(nameof(TotalPaid));
        Changed(nameof(TotalPaidDisplay));
        Changed(nameof(RemainingPayable));
        Changed(nameof(RemainingPayableDisplay));
        Changed(nameof(OverpaidAmount));
        Changed(nameof(OverpaidAmountDisplay));
        Changed(nameof(HasRemainingPayable));
        Changed(nameof(HasOverpayment));
        Changed(nameof(IsFullyPaid));
        Changed(nameof(DraftStatusTitle));
        Changed(nameof(PaymentMixSummary));
    }

    private void OnSuppliersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFilteredSuppliers();

    public void RefreshFilteredSuppliers()
    {
        var query = supplierSearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? allSuppliers
            : allSuppliers.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(s.Phone) && s.Phone.Contains(query, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(s.CompanyName) && s.CompanyName.Contains(query, StringComparison.OrdinalIgnoreCase)));

        FilteredSuppliers.Clear();
        foreach (var match in matches)
            FilteredSuppliers.Add(match);

        Changed(nameof(IsSupplierNotFound));
        Changed(nameof(HasFilteredSuppliers));
    }
}
