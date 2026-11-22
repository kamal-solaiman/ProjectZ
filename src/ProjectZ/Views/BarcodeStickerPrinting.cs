using System.Diagnostics.CodeAnalysis;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Views;

/// <summary>
/// Builds the physical sticker (50 mm × 25 mm) from live data and sends it to Windows printing.
/// The label is built in millimetres and scaled to device-independent units, so a Code128 symbol
/// prints at a readable width on a label printer and the same geometry repeats across larger paper.
/// </summary>
public static class BarcodeStickerPrinting
{
    /// <summary>WPF device-independent units per inch; 96 units = 25.4 mm.</summary>
    public const double DipsPerMillimetre = 96 / 25.4;

    public const string NoStoreNameMessage = "اسم المحل غير مسجل. اضبطه من شاشة الإعدادات العامة أولًا ليُطبع على الملصق.";

    /// <summary>Allowance so a non-borderless driver margin never clips a bar.</summary>
    private const double SafetyMargin = 0.5;

    public static double ToDips(double millimetres) => millimetres * DipsPerMillimetre;

    // ------------------------------------------------------------------ sticker sources

    /// <summary>Inventory sticker for one catalog item, or null with an Arabic message when it cannot print.</summary>
    public static BarcodeSticker? ForProduct(PosProductDesignItem? product, string? storeName, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (product is null)
        {
            error = "اختر صنفًا لطباعة ملصقه.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(storeName))
        {
            error = NoStoreNameMessage;
            return null;
        }

        var barcode = (product.Barcode ?? string.Empty).Trim();
        if (Code128Encoder.Validate(barcode) is not null)
        {
            error = $"الصنف «{product.Name}» بلا باركود صالح للطباعة. أضف باركودًا من شاشة تعديل المنتج أولًا.";
            return null;
        }

        return new BarcodeSticker(
            StickerKind.ProductBarcode,
            storeName!,
            string.IsNullOrWhiteSpace(product.Name) ? "بلا اسم" : product.Name.Trim(),
            barcode);
    }

    /// <summary>Maintenance sticker for one order: store name, customer name and the order code.</summary>
    public static BarcodeSticker? ForMaintenanceOrder(MaintenanceOrderDesignItem? order, string? storeName, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (order is null)
        {
            error = "اختر أمر صيانة لطباعة ملصقه.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(storeName))
        {
            error = NoStoreNameMessage;
            return null;
        }

        var code = (order.OrderNumber ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            error = "أمر الصيانة بلا كود. احفظ الأمر أولًا ثم اطبع ملصقه.";
            return null;
        }

        var customer = (order.CustomerName ?? string.Empty).Trim();
        return new BarcodeSticker(
            StickerKind.MaintenanceOrder,
            storeName!,
            customer.Length == 0 ? "عميل غير مسجل" : customer,
            code);
    }

    // ------------------------------------------------------------------ label visual

    /// <summary>
    /// The label at real size. Inventory: black store band, product name, Code128 + digits.
    /// Maintenance: black store band, customer name, then «كود الأمر: MNT-…».
    /// </summary>
    public static FrameworkElement BuildSticker(BarcodeSticker sticker) => BuildStickerAt(sticker, 1);

    /// <summary>The same label scaled for an on-screen preview; printing always uses the real size.</summary>
    public static FrameworkElement BuildPreview(BarcodeSticker sticker, double zoom = 0.8)
    {
        var host = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = BuildStickerAt(sticker, zoom)
        };
        return host;
    }

    private static FrameworkElement BuildStickerAt(BarcodeSticker sticker, double millimetresPerUnit)
    {
        var zoom = millimetresPerUnit * ToDips(1);
        var label = new Border
        {
            Width = ToDips(BarcodeSticker.LabelWidthMillimetres) * zoom,
            Height = ToDips(BarcodeSticker.LabelHeightMillimetres) * zoom,
            Background = Brushes.White,
            SnapsToDevicePixels = true,
            // No layout rounding here: 25 mm is 94.5 DIP, and rounding it would print a label
            // that is not exactly 50 × 25 mm on a label printer.
            UseLayoutRounding = false,
            Language = XmlLanguage.GetLanguage("ar-EG"),
            FlowDirection = FlowDirection.LeftToRight
        };
        var stack = new StackPanel();
        label.Child = stack;

        // Store name: black background, white text — identical on both stickers.
        stack.Children.Add(new Border
        {
            Background = Brushes.Black,
            Padding = new Thickness(ToDips(1.2) * zoom, ToDips(0.9) * zoom, ToDips(1.2) * zoom, ToDips(0.9) * zoom),
            Child = new TextBlock
            {
                Text = sticker.PrintedStoreName,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                FontSize = ToDips(2.9) * zoom,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.RightToLeft,
                Language = XmlLanguage.GetLanguage("ar-EG")
            }
        });

        if (sticker.Kind == StickerKind.ProductBarcode)
        {
            stack.Children.Add(TextBlockFor(sticker.Title, ToDips(3.1) * zoom, FontWeights.Bold, ToDips(4.0) * zoom));
            stack.Children.Add(BuildBarcode(sticker, zoom));
            return label;
        }

        stack.Children.Add(TextBlockFor(sticker.Title, ToDips(3.6) * zoom, FontWeights.Bold, ToDips(4.8) * zoom));
        stack.Children.Add(BuildOrderCodeLine(sticker.Barcode, zoom));
        return label;
    }

    private static TextBlock TextBlockFor(string text, double fontSize, FontWeight weight, double lineHeight) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontWeight = weight,
        FontSize = fontSize,
        LineHeight = lineHeight,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = Brushes.Black,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        FlowDirection = FlowDirection.RightToLeft,
        Language = XmlLanguage.GetLanguage("ar-EG")
    };

    /// <summary>«كود الأمر:» in Arabic followed by the Latin code, forced left-to-right so it never reorders.</summary>
    private static FrameworkElement BuildOrderCodeLine(string code, double zoom)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Thickness(0, ToDips(0.6) * zoom, 0, 0)
        };
        panel.Children.Add(TextBlockFor(code, ToDips(3.6) * zoom, FontWeights.Bold, ToDips(4.6) * zoom));
        panel.Children.Add(new TextBlock
        {
            Text = "كود الأمر:",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = ToDips(2.6) * zoom,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            FlowDirection = FlowDirection.RightToLeft,
            Language = XmlLanguage.GetLanguage("ar-EG"),
            Margin = new Thickness(0, 0, ToDips(1.2) * zoom, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        host.Children.Add(panel);
        return host;
    }

    /// <summary>Measured Code128 symbol and its digits, both black on white.</summary>
    private static FrameworkElement BuildBarcode(BarcodeSticker sticker, double zoom)
    {
        var widths = Code128Encoder.Encode(sticker.Barcode);
        var module = sticker.ModuleWidth(widths) * ToDips(1) * zoom;
        var barHeight = ToDips(8.2) * zoom;

        var cursor = 0.0;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            for (var index = 0; index < widths.Count; index++)
            {
                var width = widths[index] * module;
                if (index % 2 == 0 && width > 0)
                    context.DrawRectangle(Brushes.Black, null, new Rect(cursor, 0, width, barHeight));
                cursor += width;
            }
        }

        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, ToDips(0.8) * zoom, 0, 0)
        };
        panel.Children.Add(new Image
        {
            Source = new DrawingImage(visual.Drawing),
            Width = cursor,
            Height = barHeight,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true
        });
        panel.Children.Add(new TextBlock
        {
            Text = sticker.Barcode,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontSize = 9 * zoom,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight
        });
        return panel;
    }

    // ------------------------------------------------------------------ layout and printing

    /// <summary>How many labels fit a printable area, and therefore how many go on one sheet.</summary>
    public static (int Across, int Down, int PerPage) LabelGrid(Size printableDips)
    {
        var across = Math.Clamp((int)(printableDips.Width / ToDips(BarcodeSticker.LabelWidthMillimetres + SafetyMargin)), 1, 20);
        var down = Math.Clamp((int)(printableDips.Height / ToDips(BarcodeSticker.LabelHeightMillimetres + SafetyMargin)), 1, 40);
        return (across, down, across * down);
    }

    /// <summary>
    /// The document handed to the printer: labels flow left-to-right then down, and every copy is
    /// one label. A 50 × 25 mm label page therefore prints exactly one sticker per page, while an
    /// A4 sheet repeats them in a grid that can be cut.
    /// </summary>
    public static FixedDocument BuildDocument(BarcodeSticker sticker, int copies, Size pageDips, Rect printableDips)
    {
        if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies), "عدد الملصقات يجب أن يكون أكبر من صفر.");

        var labelWidth = ToDips(BarcodeSticker.LabelWidthMillimetres);
        var labelHeight = ToDips(BarcodeSticker.LabelHeightMillimetres);
        if (printableDips.Width < ToDips(BarcodeSticker.LabelWidthMillimetres + SafetyMargin)
            || printableDips.Height < ToDips(BarcodeSticker.LabelHeightMillimetres + SafetyMargin))
            throw new InvalidOperationException(
                "المقاس المحدد في الطابعة أصغر من الملصق (50 × 25 مم). اختر طابعة ملصقات أو مقاس ورق أكبر.");

        var (across, _, perPage) = LabelGrid(printableDips.Size);
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = pageDips;

        var remaining = copies;
        while (remaining > 0)
        {
            var page = new FixedPage { Width = pageDips.Width, Height = pageDips.Height, Background = Brushes.White };
            page.Language = XmlLanguage.GetLanguage("ar-EG");
            var canvas = new Canvas { Width = pageDips.Width, Height = pageDips.Height, Background = Brushes.White };
            page.Children.Add(canvas);

            for (var slot = 0; slot < perPage && remaining > 0; slot++, remaining--)
            {
                var label = BuildSticker(sticker);
                Canvas.SetLeft(label, slot % across * labelWidth);
                Canvas.SetTop(label, slot / across * labelHeight);
                canvas.Children.Add(label);
            }

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        return document;
    }

    /// <summary>
    /// Asks for a printer, measures its page, then prints the copies. Runs on the UI thread and
    /// returns Arabic feedback instead of throwing, so the calling screen can show it in its status bar.
    /// </summary>
    public static StickerPrintResult Print(BarcodeSticker sticker, int copies)
    {
        if (copies < 1) return StickerPrintResult.Failed("عدد الملصقات يجب أن يكون أكبر من صفر.");

        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return StickerPrintResult.Cancelled();

            var printer = dialog.PrintQueue?.FullName ?? "الطابعة الافتراضية";
            var ticket = dialog.PrintTicket ?? new PrintTicket();
            ticket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket = ticket;

            var page = new Size(ToDips(BarcodeSticker.LabelWidthMillimetres), ToDips(BarcodeSticker.LabelHeightMillimetres));
            var printable = new Rect(page);

            if (dialog.PrintQueue is { } queue && queue.GetPrintCapabilities(ticket) is { } capabilities)
            {
                if (capabilities.PageImageableArea is { ExtentWidth: > 0, ExtentHeight: > 0 } area)
                    printable = new Rect(area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight);
                if (capabilities.PageMediaSizeCapability is { Count: > 0 } sizes)
                {
                    var selected = sizes.FirstOrDefault(size => size.PageMediaSizeName == ticket.PageMediaSize?.PageMediaSizeName);
                    if (selected is { Width: { } width, Height: { } height })
                        page = new Size(ToDips(width), ToDips(height));
                }
            }

            var document = BuildDocument(sticker, copies, page, printable);
            dialog.PrintDocument(document.DocumentPaginator, $"{sticker.KindLabel} — {sticker.Title}");
            return StickerPrintResult.Printed(printer, copies);
        }
        catch (Exception ex)
        {
            return StickerPrintResult.Failed($"تعذّرت طباعة الملصق: {ex.Message}");
        }
    }
}

/// <summary>Outcome of a sticker print request; <see cref="Copies"/> is 0 when the user cancelled.</summary>
public sealed record StickerPrintResult(bool Succeeded, bool WasCancelled, string PrinterName, int Copies, string Message)
{
    public static StickerPrintResult Cancelled() => new(false, true, string.Empty, 0, string.Empty);
    public static StickerPrintResult Printed(string printer, int copies) => new(true, false, printer, copies, string.Empty);
    public static StickerPrintResult Failed(string message) => new(false, false, string.Empty, 0, message);
}
