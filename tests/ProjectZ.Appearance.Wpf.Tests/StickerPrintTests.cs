using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

namespace ProjectZ.Tests;

/// <summary>
/// Probes for the barcode stickers: the Code128 symbol must be a real, scannable encoding,
/// the label must print the store name on a black band with white text plus the black product
/// name and barcode, and the maintenance sticker must carry store, customer and order code.
/// </summary>
internal static class StickerPrintTests
{
    public static void Run(Action<bool, string> check)
    {
        // --- Code128: the symbol must decode back to the printed value -----------------------
        check(Code128Encoder.Patterns.Length == 107, "Code128 has the 107 symbols of ISO 15417");
        check(Decode(Code128Encoder.Encode("6221039111128")) == "6221039111128",
            "A numeric barcode round-trips through code set C");
        check(Decode(Code128Encoder.Encode("ABC-123")) == "ABC-123",
            "Letters and punctuation round-trip through code set B");
        check(Decode(Code128Encoder.Encode("MNT-2026-0042")) == "MNT-2026-0042",
            "A maintenance order code round-trips through the B/C switch");
        check(Decode(Code128Encoder.Encode("AB1234CD")) == "AB1234CD",
            "A mixed value switches to C and back to B at the right symbol");
        check(Code128Encoder.Encode("123456789012").Count == 10 * 6,
            "Twelve digits encode as start, set C, six pairs, check and stop");

        check(Code128Encoder.Validate("  ") == Code128Encoder.EmptyMessage, "Blank barcode is refused with an Arabic message");
        check(Code128Encoder.Validate("عربي") is { } message && message.Contains("Code128", StringComparison.Ordinal),
            "Non-Latin barcode text is refused because Code128 cannot draw it");
        check(Code128Encoder.Validate("123456") is null, "A digits-only barcode is printable");

        // --- Inventory sticker: required content and colours ---------------------------------
        var product = new PosProductDesignItem { Name = "شاحن أيفون 20 وات", Barcode = "6221039111128" };
        var sticker = BarcodeStickerPrinting.ForProduct(product, "نيو فونز", out var error);
        check(error is null && sticker is not null, "A saved item with a barcode builds an inventory sticker");
        check(sticker!.Kind == StickerKind.ProductBarcode && sticker.PrintedStoreName == "نيو فونز"
              && sticker.Title == "شاحن أيفون 20 وات" && sticker.Barcode == "6221039111128",
            "Inventory sticker carries the store name, product name and barcode");

        var label = BarcodeStickerPrinting.BuildSticker(sticker);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        check(Math.Abs(label.Width - BarcodeStickerPrinting.ToDips(BarcodeSticker.LabelWidthMillimetres)) < 0.01
              && Math.Abs(label.Height - BarcodeStickerPrinting.ToDips(BarcodeSticker.LabelHeightMillimetres)) < 0.01,
            "Inventory sticker is exactly 50 × 25 mm");

        var stack = (StackPanel)((Border)label).Child;
        var band = (Border)stack.Children[0];
        var storeText = (TextBlock)band.Child;
        check(Equals(band.Background, Brushes.Black) && Equals(storeText.Foreground, Brushes.White)
              && storeText.Text == "نيو فونز",
            "Store name prints white on a black band");
        var nameText = (TextBlock)stack.Children[1];
        check(Equals(nameText.Foreground, Brushes.Black) && nameText.Text == "شاحن أيفون 20 وات",
            "Product name prints in black");
        var ink = Descendants<Image>(label).ToList();
        var digits = Descendants<TextBlock>(label).Last();
        check(ink.Count == 1 && Equals(digits.Foreground, Brushes.Black) && digits.Text == "6221039111128",
            "Barcode symbol and its digits print in black");
        check(ink[0].Width <= BarcodeStickerPrinting.ToDips(BarcodeSticker.LabelWidthMillimetres - 2 * BarcodeSticker.QuietZoneMillimetres) + 0.01,
            "Barcode keeps a 3 mm quiet zone on each side");

        // --- Document geometry: one label per label-sized page -------------------------------
        var page = new Size(BarcodeStickerPrinting.ToDips(50), BarcodeStickerPrinting.ToDips(25));
        var onePerPage = BarcodeStickerPrinting.BuildDocument(sticker, 5, page, new Rect(page));
        check(onePerPage.Pages.Count == 5, "Five copies on 50 × 25 mm label paper produce five pages");
        var a4 = new Size(BarcodeStickerPrinting.ToDips(210), BarcodeStickerPrinting.ToDips(297));
        var a4Document = BarcodeStickerPrinting.BuildDocument(sticker, 30, a4, new Rect(a4));
        check(a4Document.Pages.Count == 1 && BarcodeStickerPrinting.LabelGrid(a4).PerPage >= 30,
            "Thirty copies fit one A4 sheet as a cut-out grid");
        var refused = false;
        try { BarcodeStickerPrinting.BuildDocument(sticker, 1, new Size(60, 30), new Rect(0, 0, 60, 30)); }
        catch (InvalidOperationException) { refused = true; }
        check(refused, "Paper smaller than the label is refused with an Arabic message");

        // --- Maintenance sticker: store + customer + order code ------------------------------
        var order = new MaintenanceOrderDesignItem { OrderNumber = "MNT-2026-0042" };
        order.CustomerName = "أحمد محمود";
        var maintenance = BarcodeStickerPrinting.ForMaintenanceOrder(order, "نيو فونز", out var maintenanceError);
        check(maintenanceError is null && maintenance!.Kind == StickerKind.MaintenanceOrder
              && maintenance.PrintedStoreName == "نيو فونز" && maintenance.Title == "أحمد محمود"
              && maintenance.Barcode == "MNT-2026-0042",
            "Maintenance sticker carries store, customer and order code");
        var orderLabel = BarcodeStickerPrinting.BuildSticker(maintenance);
        var orderStack = (StackPanel)((Border)orderLabel).Child;
        var orderBand = (Border)orderStack.Children[0];
        check(Equals(orderBand.Background, Brushes.Black) && Equals(((TextBlock)orderBand.Child).Foreground, Brushes.White),
            "Maintenance sticker prints the store name white on black");
        check(Descendants<TextBlock>(orderLabel).Any(text => text.Text == "أحمد محمود")
              && Descendants<TextBlock>(orderLabel).Any(text => text.Text == "MNT-2026-0042"),
            "Maintenance sticker prints the customer name and order code in black");
        check(!Descendants<Image>(orderLabel).Any(), "Maintenance sticker has no decorative barcode graphic");

        // --- Screen commands: every refusal is an Arabic status, never a crash ---------------
        var emptyStore = BarcodeStickerPrinting.ForProduct(product, " ", out var storeError);
        check(emptyStore is null && storeError == BarcodeStickerPrinting.NoStoreNameMessage,
            "Printing is refused until the store name is saved in settings");
        var blank = new PosProductDesignItem { Name = "بلا باركود" };
        check(BarcodeStickerPrinting.ForProduct(blank, "نيو فونز", out var blankError) is null
              && blankError!.Contains("بلا باركود", StringComparison.Ordinal),
            "An item without a barcode explains how to add one");

        var inventory = new InventoryViewModel();
        inventory.BarcodeCopiesText = "0";
        inventory.OpenBarcodePrintCommand.Execute(product);
        check(inventory.IsErrorStatus && inventory.StatusMessage.Contains("1 إلى 100", StringComparison.Ordinal),
            "Zero copies is refused before any printer dialog");
        inventory.BarcodeCopiesText = "abc";
        inventory.OpenBarcodePrintCommand.Execute(product);
        check(inventory.IsErrorStatus, "A non-numeric copy count is refused in Arabic");

        // The receipt preview keeps the 80 mm previews but prints the sticker straight away.
        var preview = new MaintenanceReceiptPreviewViewModel(order);
        check(preview.PrintButtonLabel == "معاينة الطباعة", "Receipt modes keep the preview action");
        preview.SelectDeviceStickerModeCommand.Execute(null);
        check(preview.IsDeviceStickerMode && preview.PrintButtonLabel == "طباعة الملصق الآن"
              && preview.StickerHint.Contains("MNT-2026-0042", StringComparison.Ordinal),
            "Sticker mode switches the button to a real print and names the order code");
        preview.SelectCustomerReceiptModeCommand.Execute(null);
        preview.PrintCommand.Execute(null);
        check(!preview.IsErrorStatus && preview.StatusMessage.Contains("لم تُفعّل بعد", StringComparison.Ordinal),
            "A receipt mode says plainly that receipt printing is not enabled yet");
    }


    /// <summary>
    /// Minimal Code128 reader: walks the element widths back into symbols, checks the check symbol
    /// and the stop pattern, then rebuilds the text from code sets B and C. A symbol that does not
    /// decode exactly means a scanner would read the wrong value.
    /// </summary>
    private static string Decode(IReadOnlyList<int> widths)
    {
        var symbols = new List<int>();
        var index = 0;
        while (true)
        {
            if (index + 6 > widths.Count) throw new Exception("Code128 ends before the stop symbol");
            var pattern = string.Concat(widths.Skip(index).Take(6));
            // The stop symbol has a seventh element (the two-module termination bar).
            if (pattern == "233111" && index + 7 == widths.Count && widths[index + 6] == 2)
            {
                symbols.Add(106);
                index += 7;
                break;
            }

            var value = Array.IndexOf(Code128Encoder.Patterns, pattern);
            if (value < 0) throw new Exception("Undecodable Code128 pattern: " + pattern);
            symbols.Add(value);
            index += 6;
        }

        if (index != widths.Count) throw new Exception("Extra modules follow the Code128 stop symbol");
        var body = symbols.Take(symbols.Count - 2).ToArray();
        var checksum = body[0];
        for (var position = 1; position < body.Length; position++) checksum += body[position] * position;
        if (checksum % 103 != symbols[^2]) throw new Exception("Code128 check symbol is wrong");

        var text = new System.Text.StringBuilder();
        var set = body[0] == 104 ? 'B' : body[0] == 105 ? 'C' : throw new Exception("Unsupported start code");
        foreach (var symbol in body.Skip(1))
        {
            if (symbol == 99) { set = 'C'; continue; }
            if (symbol == 100) { set = 'B'; continue; }
            if (set == 'C')
            {
                text.Append(symbol.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                continue;
            }

            text.Append((char)(symbol + 32));
        }

        return text.ToString();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match) yield return match;
            if (child is DependencyObject node)
                foreach (var nested in Descendants<T>(node))
                    yield return nested;
        }
    }
}
