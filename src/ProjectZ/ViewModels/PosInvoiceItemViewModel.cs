using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PosInvoiceItemViewModel : ObservableObject
{
    private readonly Action onChanged;
    private int lineNumber;
    private int quantity;
    private PriceTier selectedPriceTier;
    private PriceTier invoicePriceTier;

    public PosProductDesignItem Product { get; }
    public IReadOnlyList<PriceTierOption> PriceTierOptions => PosFormatting.PriceTierOptions;

    public int LineNumber
    {
        get => lineNumber;
        internal set
        {
            if (lineNumber == value) return;
            lineNumber = value;
            Changed();
        }
    }

    public int Quantity
    {
        get => quantity;
        set
        {
            var clamped = Math.Max(1, value);
            if (quantity == clamped) return;
            quantity = clamped;
            Changed();
            Changed(nameof(LineTotal));
            Changed(nameof(LineTotalDisplay));
            onChanged();
        }
    }

    public PriceTier SelectedPriceTier
    {
        get => selectedPriceTier;
        set
        {
            if (selectedPriceTier == value) return;
            selectedPriceTier = value;
            Changed();
            Changed(nameof(SelectedPriceTierTitle));
            Changed(nameof(IsCustomTierOverride));
            Changed(nameof(UnitPrice));
            Changed(nameof(UnitPriceDisplay));
            Changed(nameof(LineTotal));
            Changed(nameof(LineTotalDisplay));
            onChanged();
        }
    }

    public bool IsCustomTierOverride => selectedPriceTier != invoicePriceTier;
    public string SelectedPriceTierTitle => PosFormatting.GetPriceTierShortTitle(selectedPriceTier);
    public decimal UnitPrice => Product.GetPrice(selectedPriceTier);
    public string UnitPriceDisplay => PosFormatting.FormatNumber(UnitPrice);
    public decimal LineTotal => UnitPrice * Quantity;
    public string LineTotalDisplay => PosFormatting.FormatNumber(LineTotal);

    public PosInvoiceItemViewModel(
        PosProductDesignItem product,
        int lineNumber,
        PriceTier invoicePriceTier,
        PriceTier? initialTier,
        int quantity,
        Action onChanged)
    {
        Product = product;
        this.lineNumber = lineNumber;
        this.invoicePriceTier = invoicePriceTier;
        selectedPriceTier = initialTier ?? invoicePriceTier;
        this.quantity = Math.Max(1, quantity);
        this.onChanged = onChanged;
    }

    internal void ApplyInvoicePriceTier(PriceTier newInvoiceTier)
    {
        invoicePriceTier = newInvoiceTier;
        selectedPriceTier = newInvoiceTier;
        Changed(nameof(SelectedPriceTier));
        Changed(nameof(SelectedPriceTierTitle));
        Changed(nameof(IsCustomTierOverride));
        Changed(nameof(UnitPrice));
        Changed(nameof(UnitPriceDisplay));
        Changed(nameof(LineTotal));
        Changed(nameof(LineTotalDisplay));
    }
}
