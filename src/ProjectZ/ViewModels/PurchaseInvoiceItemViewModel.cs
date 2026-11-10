using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PurchaseInvoiceItemViewModel : ObservableObject
{
    private readonly Action onChanged;
    private int lineNumber;
    private int quantity;
    private string quantityText;
    private string unitPurchaseCostText;
    private decimal unitPurchaseCost;
    private bool isUpdatingQuantity;

    public PosProductDesignItem Product { get; }

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
            if (!isUpdatingQuantity)
            {
                isUpdatingQuantity = true;
                quantityText = quantity.ToString(CultureInfo.InvariantCulture);
                Changed(nameof(QuantityText));
                isUpdatingQuantity = false;
            }
            Changed();
            Changed(nameof(LineTotal));
            Changed(nameof(LineTotalDisplay));
            onChanged();
        }
    }

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (quantityText == value) return;
            quantityText = value;
            Changed();
            if (!isUpdatingQuantity && int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
            {
                isUpdatingQuantity = true;
                Quantity = parsed;
                isUpdatingQuantity = false;
            }
        }
    }

    public string UnitPurchaseCostText
    {
        get => unitPurchaseCostText;
        set
        {
            if (unitPurchaseCostText == value) return;
            unitPurchaseCostText = value;
            Changed();
            if (PosFormatting.TryParseAmount(value, out var parsed) && parsed >= 0m)
                UnitPurchaseCost = parsed;
        }
    }

    public decimal UnitPurchaseCost
    {
        get => unitPurchaseCost;
        private set
        {
            var normalized = Math.Max(0m, value);
            if (unitPurchaseCost == normalized) return;
            unitPurchaseCost = normalized;
            Changed();
            Changed(nameof(UnitPurchaseCostDisplay));
            Changed(nameof(LineTotal));
            Changed(nameof(LineTotalDisplay));
            Changed(nameof(IsCostDifferentFromCatalog));
            onChanged();
        }
    }

    public decimal LineTotal => Quantity * UnitPurchaseCost;
    public string UnitPurchaseCostDisplay => PosFormatting.FormatCurrency(UnitPurchaseCost);
    public string LineTotalDisplay => PosFormatting.FormatCurrency(LineTotal);
    public string PreviousCostDisplay => $"التكلفة الحالية: {Product.CostPriceDisplay}";
    public string CurrentStockDisplay => $"المتاح بالمخزون: {Product.AvailableStock}";
    public bool IsCostDifferentFromCatalog => UnitPurchaseCost != Product.CostPrice;

    public PurchaseInvoiceItemViewModel(
        PosProductDesignItem product,
        int lineNumber,
        int initialQuantity,
        Action onChanged)
    {
        Product = product;
        this.lineNumber = lineNumber;
        this.onChanged = onChanged;
        quantity = Math.Max(1, initialQuantity);
        quantityText = quantity.ToString(CultureInfo.InvariantCulture);
        unitPurchaseCost = Math.Max(0m, product.CostPrice);
        unitPurchaseCostText = unitPurchaseCost > 0m ? PosFormatting.FormatPlainNumber(unitPurchaseCost) : "";
    }
}
