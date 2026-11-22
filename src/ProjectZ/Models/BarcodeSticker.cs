namespace ProjectZ.Models;

/// <summary>What a sticker carries. The physical layout is shared; only the printed lines change.</summary>
public enum StickerKind
{
    /// <summary>Inventory sticker: store name band, product name, then a real Code128 symbol.</summary>
    ProductBarcode = 0,

    /// <summary>Maintenance order sticker: store name band, customer name, then the order code.</summary>
    MaintenanceOrder = 1
}

/// <summary>
/// One printable sticker at its real physical size — 50 mm × 25 mm, the standard thermal label.
/// <para>
/// Product barcode: the store name is printed white on a black band, the product name in black,
/// and the barcode as a measured Code128 symbol with its digits in black underneath.
/// Maintenance order: the store name on the black band, the customer name, and the order code.
/// </para>
/// </summary>
public sealed record BarcodeSticker(StickerKind Kind, string StoreName, string Title, string Barcode)
{
    public const double LabelWidthMillimetres = 50;
    public const double LabelHeightMillimetres = 25;

    /// <summary>
    /// Blank space before the first bar and after the last bar (ISO/IEC 15417).
    /// Three millimetres on each side keeps the symbol readable by a handheld scanner.
    /// </summary>
    public const double QuietZoneMillimetres = 3;

    public bool IsProductBarcode => Kind == StickerKind.ProductBarcode;

    public string KindLabel => Kind == StickerKind.MaintenanceOrder ? "ملصق أمر صيانة" : "ملصق باركود منتج";

    /// <summary>The store name as printed; falls back to the application name when settings are empty.</summary>
    public string PrintedStoreName => string.IsNullOrWhiteSpace(StoreName)
        ? StoreProfile.DefaultStoreName
        : StoreName.Trim();

    /// <summary>
    /// Width in millimetres of one Code128 module, fitted to the space between the quiet zones.
    /// Capped at 0.42 mm; a very long value still stays readable and its digits print underneath.
    /// </summary>
    public double ModuleWidth(IReadOnlyList<int> widths)
    {
        var modules = widths.Sum();
        return modules <= 0 ? 0.42 : Math.Min(0.42, (LabelWidthMillimetres - 2 * QuietZoneMillimetres) / modules);
    }
}
