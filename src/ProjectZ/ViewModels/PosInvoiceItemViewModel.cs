using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PosInvoiceItemViewModel : ObservableObject
{
    private readonly Action onChanged;
    private readonly Func<PosProductDesignItem, int, bool>? canSetQuantity;
    private int lineNumber;
    private int quantity;
    private PriceTier selectedPriceTier;
    private PriceTier invoicePriceTier;

    public PosProductDesignItem Product { get; private set; }
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

    /// <summary>Maximum units this line may hold: the product's available stock, never below the current quantity.</summary>
    public int MaxQuantity => Math.Max(1, Product.AvailableStock);

    public int Quantity
    {
        get => quantity;
        set
        {
            var clamped = Math.Max(1, value);
            if (canSetQuantity is not null && !canSetQuantity(Product, clamped))
            {
                // Reject above-stock edits: keep the stored value and let the caller report the real availability.
                Changed(nameof(Quantity));
                return;
            }

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
        Action onChanged,
        Func<PosProductDesignItem, int, bool>? canSetQuantity = null)
    {
        Product = product;
        this.lineNumber = lineNumber;
        this.invoicePriceTier = invoicePriceTier;
        selectedPriceTier = initialTier ?? invoicePriceTier;
        this.quantity = Math.Max(1, quantity);
        this.onChanged = onChanged;
        this.canSetQuantity = canSetQuantity;
    }

    /// <summary>Points the line at the freshly loaded product instance so stock and prices are never stale.</summary>
    internal void RebindProduct(PosProductDesignItem product)
    {
        if (ReferenceEquals(Product, product) || Product.Id != product.Id) return;
        Product = product;
        Changed(nameof(Product));
        Changed(nameof(MaxQuantity));
        Changed(nameof(UnitPrice));
        Changed(nameof(UnitPriceDisplay));
        Changed(nameof(LineTotal));
        Changed(nameof(LineTotalDisplay));
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
