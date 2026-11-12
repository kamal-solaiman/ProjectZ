using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PosInvoiceViewModel : ObservableObject
{
    private readonly ObservableCollection<PosCustomerDesignItem> allCustomers;
    private readonly Action<string, bool>? notifyStatus;

    private bool isActive;
    private PriceTier invoicePriceTier = PriceTier.Retail;
    private PosCustomerDesignItem selectedCustomer;
    private string customerSearchText = "";
    private bool isCustomerPickerOpen;

    private string discountAmountText = "";
    private decimal discountAmount;

    private PaymentPresetMode activePaymentPreset = PaymentPresetMode.CashOnly;
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

    public int InvoiceNumber { get; }

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

    public string TabTitle => $"فاتورة #{InvoiceNumber}";

    public string TabItemsSummary => Items.Count == 0
        ? "فاتورة فارغة"
        : $"{Items.Count} {(Items.Count == 1 ? "صنف" : "أصناف")} · {NetTotalDisplay}";

    public ObservableCollection<PosInvoiceItemViewModel> Items { get; } = [];
    public ObservableCollection<PosCustomerDesignItem> FilteredCustomers { get; } = [];

    public PriceTier InvoicePriceTier
    {
        get => invoicePriceTier;
        private set
        {
            if (invoicePriceTier == value) return;
            invoicePriceTier = value;
            Changed();
            Changed(nameof(InvoicePriceTierTitle));
            Changed(nameof(InvoicePriceTierBadgeText));
            Changed(nameof(IsInvoiceRetail));
            Changed(nameof(IsInvoiceSpecial));
            Changed(nameof(IsInvoiceWholesale));
        }
    }

    public string InvoicePriceTierTitle => PosFormatting.GetPriceTierTitle(InvoicePriceTier);
    public string InvoicePriceTierBadgeText => $"({InvoicePriceTierTitle})";
    public bool IsInvoiceRetail => InvoicePriceTier == PriceTier.Retail;
    public bool IsInvoiceSpecial => InvoicePriceTier == PriceTier.Special;
    public bool IsInvoiceWholesale => InvoicePriceTier == PriceTier.Wholesale;

    public PosCustomerDesignItem SelectedCustomer
    {
        get => selectedCustomer;
        private set
        {
            if (ReferenceEquals(selectedCustomer, value)) return;
            selectedCustomer = value;
            Changed();
            Changed(nameof(HasNamedCustomer));
            Changed(nameof(SelectedCustomerTierDisplay));
        }
    }

    public bool HasNamedCustomer => !SelectedCustomer.IsWalkIn;
    public string SelectedCustomerTierDisplay => $"التسعير الافتراضي: {SelectedCustomer.DefaultPriceTierTitle}";

    public string CustomerSearchText
    {
        get => customerSearchText;
        set
        {
            if (customerSearchText == value) return;
            customerSearchText = value;
            Changed();
            if (!string.IsNullOrWhiteSpace(customerSearchText))
                isCustomerPickerOpen = true;
            Changed(nameof(IsCustomerPickerVisible));
            RefreshFilteredCustomers();
        }
    }

    public bool IsCustomerPickerOpen
    {
        get => isCustomerPickerOpen;
        set
        {
            if (isCustomerPickerOpen == value) return;
            isCustomerPickerOpen = value;
            Changed();
            Changed(nameof(IsCustomerPickerVisible));
            if (isCustomerPickerOpen)
                RefreshFilteredCustomers();
        }
    }

    public bool IsCustomerPickerVisible => isCustomerPickerOpen || !string.IsNullOrWhiteSpace(customerSearchText);
    public bool IsCustomerNotFound => !string.IsNullOrWhiteSpace(customerSearchText) && FilteredCustomers.Count == 0;
    public bool HasFilteredCustomers => FilteredCustomers.Count > 0;

    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0;
    public int TotalUnitsCount => Items.Sum(item => item.Quantity);
    public string ItemsCountSummary => $"{Items.Count} صنف ({TotalUnitsCount} قطعة)";
    public bool HasAnyCustomItemTier => Items.Any(item => item.IsCustomTierOverride);

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
            discountAmount = PosFormatting.TryParseAmount(value, out var parsed)
                ? Math.Max(0m, parsed)
                : 0m;
            NotifyTotalsChanged(syncSinglePayment: true);
        }
    }

    public decimal EffectiveDiscount => Math.Min(Subtotal, discountAmount);
    public string DiscountDisplay => PosFormatting.FormatCurrency(EffectiveDiscount);
    public decimal NetTotal => Math.Max(0m, Subtotal - EffectiveDiscount);
    public string NetTotalDisplay => PosFormatting.FormatCurrency(NetTotal);

    public PaymentPresetMode ActivePaymentPreset
    {
        get => activePaymentPreset;
        private set
        {
            if (activePaymentPreset == value) return;
            activePaymentPreset = value;
            Changed();
            Changed(nameof(IsCashPresetActive));
            Changed(nameof(IsVodafonePresetActive));
            Changed(nameof(IsInstaPayPresetActive));
            Changed(nameof(IsSplitPresetActive));
        }
    }

    public bool IsCashPresetActive => ActivePaymentPreset == PaymentPresetMode.CashOnly;
    public bool IsVodafonePresetActive => ActivePaymentPreset == PaymentPresetMode.VodafoneCashOnly;
    public bool IsInstaPayPresetActive => ActivePaymentPreset == PaymentPresetMode.InstaPayOnly;
    public bool IsSplitPresetActive => ActivePaymentPreset == PaymentPresetMode.Split;

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
                if (!isCashEnabled)
                {
                    SetCashTextProgrammatic("");
                }
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
                if (!isVodafoneCashEnabled)
                {
                    SetVodafoneTextProgrammatic("");
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
                if (!isInstaPayEnabled)
                {
                    SetInstaPayTextProgrammatic("");
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

    public decimal CashAmount => IsCashEnabled ? cashAmount : 0m;
    public decimal VodafoneCashAmount => IsVodafoneCashEnabled ? vodafoneCashAmount : 0m;
    public decimal InstaPayAmount => IsInstaPayEnabled ? instaPayAmount : 0m;

    public decimal TotalPaid => CashAmount + VodafoneCashAmount + InstaPayAmount;
    public string TotalPaidDisplay => PosFormatting.FormatCurrency(TotalPaid);

    public decimal RemainingAmount => Math.Max(0m, NetTotal - TotalPaid);
    public string RemainingAmountDisplay => PosFormatting.FormatCurrency(RemainingAmount);
    public bool HasRemaining => RemainingAmount > 0m;

    public decimal ChangeAmount => Math.Max(0m, TotalPaid - NetTotal);
    public string ChangeAmountDisplay => PosFormatting.FormatCurrency(ChangeAmount);
    public bool HasChange => ChangeAmount > 0m;

    public bool IsFullySettled => NetTotal > 0m && RemainingAmount == 0m;

    public string PaymentMixSummary
    {
        get
        {
            var parts = new List<string>(3);
            if (CashAmount > 0m) parts.Add($"نقدي {PosFormatting.FormatNumber(CashAmount)}");
            if (VodafoneCashAmount > 0m) parts.Add($"فودافون كاش {PosFormatting.FormatNumber(VodafoneCashAmount)}");
            if (InstaPayAmount > 0m) parts.Add($"انستاباي {PosFormatting.FormatNumber(InstaPayAmount)}");

            if (parts.Count == 0)
            {
                return ActivePaymentPreset switch
                {
                    PaymentPresetMode.VodafoneCashOnly => "فودافون كاش (0.00)",
                    PaymentPresetMode.InstaPayOnly => "انستاباي (0.00)",
                    PaymentPresetMode.Split => "دفع متعدد (نقدي + فودافون كاش + انستاباي)",
                    _ => "نقدي (0.00)"
                };
            }

            return string.Join(" + ", parts);
        }
    }

    public ActionCommand SetInvoiceRetailCommand { get; }
    public ActionCommand SetInvoiceSpecialCommand { get; }
    public ActionCommand SetInvoiceWholesaleCommand { get; }

    public ActionCommand ToggleCustomerPickerCommand { get; }
    public ActionCommand ClearCustomerSearchCommand { get; }
    public ActionCommand ResetWalkInCustomerCommand { get; }
    public RelayCommand<PosCustomerDesignItem> SelectCustomerCommand { get; }

    public RelayCommand<PosInvoiceItemViewModel> IncrementItemCommand { get; }
    public RelayCommand<PosInvoiceItemViewModel> DecrementItemCommand { get; }
    public RelayCommand<PosInvoiceItemViewModel> RemoveItemCommand { get; }
    public ActionCommand ClearInvoiceCommand { get; }

    public ActionCommand SetCashPresetCommand { get; }
    public ActionCommand SetVodafonePresetCommand { get; }
    public ActionCommand SetInstaPayPresetCommand { get; }
    public ActionCommand SetSplitPresetCommand { get; }

    public ActionCommand FillCashRemainingCommand { get; }
    public ActionCommand FillVodafoneRemainingCommand { get; }
    public ActionCommand FillInstaPayRemainingCommand { get; }

    public PosInvoiceViewModel(
        int invoiceNumber,
        ObservableCollection<PosCustomerDesignItem> allCustomers,
        Action<string, bool>? notifyStatus = null)
    {
        InvoiceNumber = invoiceNumber;
        this.allCustomers = allCustomers;
        this.notifyStatus = notifyStatus;
        selectedCustomer = allCustomers.FirstOrDefault(c => c.IsWalkIn)
            ?? new PosCustomerDesignItem { Id = 0, Name = "عميل نقدي (عام)", IsWalkIn = true };

        allCustomers.CollectionChanged += OnCustomersCollectionChanged;

        SetInvoiceRetailCommand = new ActionCommand(() => ApplyTierToWholeInvoice(PriceTier.Retail));
        SetInvoiceSpecialCommand = new ActionCommand(() => ApplyTierToWholeInvoice(PriceTier.Special));
        SetInvoiceWholesaleCommand = new ActionCommand(() => ApplyTierToWholeInvoice(PriceTier.Wholesale));

        ToggleCustomerPickerCommand = new ActionCommand(() => IsCustomerPickerOpen = !IsCustomerPickerOpen);
        ClearCustomerSearchCommand = new ActionCommand(() =>
        {
            CustomerSearchText = "";
            IsCustomerPickerOpen = false;
        });
        ResetWalkInCustomerCommand = new ActionCommand(() =>
        {
            var walkIn = allCustomers.FirstOrDefault(c => c.IsWalkIn);
            if (walkIn is not null)
                SelectCustomer(walkIn);
        });
        SelectCustomerCommand = new RelayCommand<PosCustomerDesignItem>(customer =>
        {
            if (customer is not null)
                SelectCustomer(customer);
        });

        IncrementItemCommand = new RelayCommand<PosInvoiceItemViewModel>(item =>
        {
            if (item is not null)
                item.Quantity++;
        });
        DecrementItemCommand = new RelayCommand<PosInvoiceItemViewModel>(item =>
        {
            if (item is null) return;
            if (item.Quantity > 1)
                item.Quantity--;
            else
                RemoveItem(item);
        });
        RemoveItemCommand = new RelayCommand<PosInvoiceItemViewModel>(item =>
        {
            if (item is not null)
                RemoveItem(item);
        });
        ClearInvoiceCommand = new ActionCommand(ClearInvoice);

        SetCashPresetCommand = new ActionCommand(() => ApplyPaymentPreset(PaymentPresetMode.CashOnly));
        SetVodafonePresetCommand = new ActionCommand(() => ApplyPaymentPreset(PaymentPresetMode.VodafoneCashOnly));
        SetInstaPayPresetCommand = new ActionCommand(() => ApplyPaymentPreset(PaymentPresetMode.InstaPayOnly));
        SetSplitPresetCommand = new ActionCommand(() => ApplyPaymentPreset(PaymentPresetMode.Split));

        FillCashRemainingCommand = new ActionCommand(() => FillRemainingIntoChannel(PaymentPresetMode.CashOnly));
        FillVodafoneRemainingCommand = new ActionCommand(() => FillRemainingIntoChannel(PaymentPresetMode.VodafoneCashOnly));
        FillInstaPayRemainingCommand = new ActionCommand(() => FillRemainingIntoChannel(PaymentPresetMode.InstaPayOnly));

        RefreshFilteredCustomers();
    }

    internal void Detach()
        => allCustomers.CollectionChanged -= OnCustomersCollectionChanged;

    public void AddProduct(PosProductDesignItem product, int quantity = 1, PriceTier? explicitTier = null)
    {
        var existing = Items.FirstOrDefault(item => item.Product.Id == product.Id);
        if (existing is not null)
        {
            existing.Quantity += Math.Max(1, quantity);
        }
        else
        {
            var item = new PosInvoiceItemViewModel(
                product,
                Items.Count + 1,
                InvoicePriceTier,
                explicitTier ?? InvoicePriceTier,
                quantity,
                OnItemChanged);
            Items.Add(item);
        }

        RenumberItems();
        NotifyTotalsChanged(syncSinglePayment: true);
    }

    public void ApplyTierToWholeInvoice(PriceTier tier)
    {
        InvoicePriceTier = tier;
        foreach (var item in Items)
            item.ApplyInvoicePriceTier(tier);

        NotifyTotalsChanged(syncSinglePayment: true);
        notifyStatus?.Invoke(
            $"تم تطبيق {PosFormatting.GetPriceTierTitle(tier)} على الفاتورة #{InvoiceNumber} بالكامل.",
            false);
    }

    public void SelectCustomer(PosCustomerDesignItem customer, bool applyDefaultTier = true)
    {
        SelectedCustomer = customer;
        customerSearchText = "";
        Changed(nameof(CustomerSearchText));
        IsCustomerPickerOpen = false;
        RefreshFilteredCustomers();

        if (applyDefaultTier)
            ApplyTierToWholeInvoice(customer.DefaultPriceTier);
    }

    public void ApplyPaymentPreset(PaymentPresetMode mode)
    {
        isUpdatingPayments = true;
        try
        {
            hasManualSinglePaymentEdit = false;
            ActivePaymentPreset = mode;

            switch (mode)
            {
                case PaymentPresetMode.CashOnly:
                    SetChannels(cash: true, vodafone: false, instaPay: false);
                    SetCashTextProgrammatic(NetTotal > 0m ? PosFormatting.FormatPlainNumber(NetTotal) : "");
                    SetVodafoneTextProgrammatic("");
                    SetInstaPayTextProgrammatic("");
                    break;

                case PaymentPresetMode.VodafoneCashOnly:
                    SetChannels(cash: false, vodafone: true, instaPay: false);
                    SetCashTextProgrammatic("");
                    SetVodafoneTextProgrammatic(NetTotal > 0m ? PosFormatting.FormatPlainNumber(NetTotal) : "");
                    SetInstaPayTextProgrammatic("");
                    break;

                case PaymentPresetMode.InstaPayOnly:
                    SetChannels(cash: false, vodafone: false, instaPay: true);
                    SetCashTextProgrammatic("");
                    SetVodafoneTextProgrammatic("");
                    SetInstaPayTextProgrammatic(NetTotal > 0m ? PosFormatting.FormatPlainNumber(NetTotal) : "");
                    break;

                case PaymentPresetMode.Split:
                    SetChannels(cash: true, vodafone: true, instaPay: true);
                    break;
            }
        }
        finally
        {
            isUpdatingPayments = false;
        }

        NotifyPaymentSummaryChanged();
    }

    public void SetSplitAmounts(decimal cash, decimal vodafoneCash, decimal instaPay)
    {
        isUpdatingPayments = true;
        try
        {
            hasManualSinglePaymentEdit = true;
            ActivePaymentPreset = PaymentPresetMode.Split;
            SetChannels(cash: true, vodafone: true, instaPay: true);
            SetCashTextProgrammatic(cash > 0m ? PosFormatting.FormatPlainNumber(cash) : "");
            SetVodafoneTextProgrammatic(vodafoneCash > 0m ? PosFormatting.FormatPlainNumber(vodafoneCash) : "");
            SetInstaPayTextProgrammatic(instaPay > 0m ? PosFormatting.FormatPlainNumber(instaPay) : "");
        }
        finally
        {
            isUpdatingPayments = false;
        }

        NotifyPaymentSummaryChanged();
    }

    private void FillRemainingIntoChannel(PaymentPresetMode channel)
    {
        if (NetTotal <= 0m) return;

        isUpdatingPayments = true;
        try
        {
            hasManualSinglePaymentEdit = true;
            var currentOtherTotal = channel switch
            {
                PaymentPresetMode.CashOnly => VodafoneCashAmount + InstaPayAmount,
                PaymentPresetMode.VodafoneCashOnly => CashAmount + InstaPayAmount,
                PaymentPresetMode.InstaPayOnly => CashAmount + VodafoneCashAmount,
                _ => 0m
            };

            var targetAmount = Math.Max(0m, NetTotal - currentOtherTotal);
            switch (channel)
            {
                case PaymentPresetMode.CashOnly:
                    isCashEnabled = true;
                    Changed(nameof(IsCashEnabled));
                    SetCashTextProgrammatic(targetAmount > 0m ? PosFormatting.FormatPlainNumber(targetAmount) : "0");
                    break;
                case PaymentPresetMode.VodafoneCashOnly:
                    isVodafoneCashEnabled = true;
                    Changed(nameof(IsVodafoneCashEnabled));
                    SetVodafoneTextProgrammatic(targetAmount > 0m ? PosFormatting.FormatPlainNumber(targetAmount) : "0");
                    break;
                case PaymentPresetMode.InstaPayOnly:
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
        discountAmountText = "";
        discountAmount = 0m;
        Changed(nameof(DiscountAmountText));;;
        hasManualSinglePaymentEdit = false;

        var walkIn = allCustomers.FirstOrDefault(c => c.IsWalkIn);
        if (walkIn is not null)
            SelectedCustomer = walkIn;

        InvoicePriceTier = PriceTier.Retail;
        ApplyPaymentPreset(PaymentPresetMode.CashOnly);
        NotifyTotalsChanged(syncSinglePayment: false);
        notifyStatus?.Invoke($"تم تفريغ الفاتورة #{InvoiceNumber}.", false);
    }

    private void RemoveItem(PosInvoiceItemViewModel item)
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
        for (int i = 0; i < Items.Count; i++)
            Items[i].LineNumber = i + 1;
    }

    private void NotifyTotalsChanged(bool syncSinglePayment)
    {
        Changed(nameof(HasItems));
        Changed(nameof(IsEmpty));
        Changed(nameof(TotalUnitsCount));
        Changed(nameof(ItemsCountSummary));
        Changed(nameof(HasAnyCustomItemTier));
        Changed(nameof(Subtotal));
        Changed(nameof(SubtotalDisplay));
        Changed(nameof(EffectiveDiscount));
        Changed(nameof(DiscountDisplay));
        Changed(nameof(NetTotal));
        Changed(nameof(NetTotalDisplay));
        Changed(nameof(TabItemsSummary));

        if (syncSinglePayment && !hasManualSinglePaymentEdit && ActivePaymentPreset != PaymentPresetMode.Split)
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
                case PaymentPresetMode.CashOnly when IsCashEnabled:
                    SetCashTextProgrammatic(amountString);
                    break;
                case PaymentPresetMode.VodafoneCashOnly when IsVodafoneCashEnabled:
                    SetVodafoneTextProgrammatic(amountString);
                    break;
                case PaymentPresetMode.InstaPayOnly when IsInstaPayEnabled:
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

        if (enabledCount >= 2)
        {
            ActivePaymentPreset = PaymentPresetMode.Split;
            return;
        }

        if (IsCashEnabled)
            ActivePaymentPreset = PaymentPresetMode.CashOnly;
        else if (IsVodafoneCashEnabled)
            ActivePaymentPreset = PaymentPresetMode.VodafoneCashOnly;
        else if (IsInstaPayEnabled)
            ActivePaymentPreset = PaymentPresetMode.InstaPayOnly;
        else
            ActivePaymentPreset = PaymentPresetMode.Split;
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
        Changed(nameof(RemainingAmount));
        Changed(nameof(RemainingAmountDisplay));
        Changed(nameof(HasRemaining));
        Changed(nameof(ChangeAmount));
        Changed(nameof(ChangeAmountDisplay));
        Changed(nameof(HasChange));
        Changed(nameof(IsFullySettled));
        Changed(nameof(PaymentMixSummary));
    }

    private void OnCustomersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFilteredCustomers();

    public void RefreshFilteredCustomers()
    {
        var query = customerSearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? allCustomers
            : allCustomers.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(c.Phone) && c.Phone.Contains(query, StringComparison.OrdinalIgnoreCase)));

        FilteredCustomers.Clear();
        foreach (var match in matches)
            FilteredCustomers.Add(match);

        Changed(nameof(IsCustomerNotFound));
        Changed(nameof(HasFilteredCustomers));
    }
}
