using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using ProjectZ.Services;

namespace ProjectZ.Views;

/// <summary>
/// The classic 80 mm paper blueprint: centred store identity, a double rule, letter-spaced section
/// titles, ruled tables, a totals box and a signature line. Everything is black ink on white paper —
/// no fills, no accent colours, no rounded cards — so it prints the same on a thermal roll and on a
/// laser printer.
/// </summary>
public static class ClassicReceipt
{
    /// <summary>80 mm thermal roll: the printable width is about 72 mm.</summary>
    public const double PaperWidthMillimetres = 80;
    public const double ContentWidthMillimetres = 72;
    public const double PaddingMillimetres = 4;

    public const double DipsPerMillimetre = 96 / 25.4;

    private const string ArabicFont = "Segoe UI, Tahoma, Arial";
    private const string CodeFont = "Consolas, Courier New, Arial";

    public static double Mm(double millimetres) => millimetres * DipsPerMillimetre;

    // ------------------------------------------------------------------ building blocks

    /// <summary>Store name and contact details at the top, exactly like a pre-printed invoice book.</summary>
    public static FrameworkElement Header()
    {
        var panel = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        panel.Children.Add(Centered(StoreIdentity.Current.Name, 15, FontWeights.Bold, 22));
        panel.Children.Add(Centered(StoreIdentity.Current.ServiceCenterHeader, 9.5, FontWeights.SemiBold, 14));
        if (StoreIdentity.Current.HasContactLine)
            panel.Children.Add(Centered(StoreIdentity.Current.ContactLine, 8, FontWeights.Normal, 12, wrap: true));
        return panel;
    }

    /// <summary>A heavy rule under a thin one — the classic double line under the letterhead.</summary>
    public static FrameworkElement DoubleRule(double top = 2, double bottom = 2) => new StackPanel
    {
        Margin = new Thickness(0, Mm(top), 0, Mm(bottom)),
        Children =
        {
            new Border { Height = 1.1, Background = Brushes.Black },
            new Border { Height = 0.6, Background = Brushes.Black, Margin = new Thickness(0, 1.4, 0, 0) }
        }
    };

    public static FrameworkElement ThinRule(double top = 1.5, double bottom = 1.5) => new Border
    {
        Height = 0.7,
        Background = Brushes.Black,
        Margin = new Thickness(0, Mm(top), 0, Mm(bottom))
    };

    /// <summary>A dotted line, the way a classic receipt separates blocks.</summary>
    public static FrameworkElement DottedRule(double top = 1.5, double bottom = 1.5) => new Border
    {
        Margin = new Thickness(0, Mm(top), 0, Mm(bottom)),
        Child = new TextBlock
        {
            Text = new string('·', 90),
            FontSize = 7,
            LineHeight = Mm(2),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = Brushes.Black,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FlowDirection = FlowDirection.LeftToRight
        }
    };

    /// <summary>The document title, centred above its subtitle like an invoice heading.</summary>
    public static FrameworkElement Title(string text, string? subtitle = null)
    {
        var panel = new StackPanel { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(1), 0, Mm(1)) };
        panel.Children.Add(Centered(text, 12, FontWeights.Bold, 17));
        if (!string.IsNullOrWhiteSpace(subtitle))
            panel.Children.Add(Centered(subtitle, 8.5, FontWeights.Normal, 11));
        return panel;
    }

    /// <summary>A section caption between two rules, e.g. «بيانات العميل».</summary>
    public static FrameworkElement SectionTitle(string text)
    {
        var panel = new StackPanel { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(2.5), 0, Mm(1)) };
        panel.Children.Add(ThinRule(0, 1));
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(ArabicFont),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, Mm(0.6), 0, Mm(0.4))
        });
        panel.Children.Add(ThinRule(0.4, 0));
        return panel;
    }

    /// <summary>«العنوان: القيمة» as one right-to-left line; long values wrap under the label.</summary>
    public static FrameworkElement Field(string label, string? value, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Grid { Height = 0 };
        return new TextBlock
        {
            Text = $"{label}: {value}",
            FontFamily = new FontFamily(ArabicFont),
            FontSize = 8.6,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brushes.Black,
            FlowDirection = FlowDirection.RightToLeft,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            LineHeight = Mm(3.8),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(0, Mm(0.4), 0, 0)
        };
    }

    /// <summary>A single centred value, e.g. the document title or a total.</summary>
    public static FrameworkElement Centered(string? text, double size, FontWeight weight, double lineHeight, bool wrap = false)
    {
        return new TextBlock
        {
            Text = text ?? "",
            FontFamily = new FontFamily(ArabicFont),
            FontSize = size,
            FontWeight = weight,
            LineHeight = lineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            FlowDirection = FlowDirection.RightToLeft
        };
    }

    /// <summary>Right-to-left paragraph used for notes, warranty and legal text.</summary>
    public static FrameworkElement Paragraph(string? text, double size = 8.5)
    {
        return new TextBlock
        {
            Text = text ?? "",
            FontFamily = new FontFamily(ArabicFont),
            FontSize = size,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            FlowDirection = FlowDirection.RightToLeft,
            LineHeight = Mm(4.6),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
    }

    /// <summary>Table header cells, right to left: الصنف | الكمية | السعر | الإجمالي.</summary>
    public static FrameworkElement ItemTableHeader(params string[] headers)
    {
        var grid = new Grid { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(0.8), 0, Mm(0.8)) };
        for (var index = 0; index < headers.Length; index++)
        {
            var cell = new TextBlock
            {
                Text = headers[index],
                FontFamily = new FontFamily(ArabicFont),
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                FlowDirection = FlowDirection.RightToLeft
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cell, index);
            grid.Children.Add(cell);
        }

        var host = new StackPanel();
        host.Children.Add(ThinRule(0, 0.6));
        host.Children.Add(grid);
        host.Children.Add(ThinRule(0.6, 0));
        return host;
    }

    /// <summary>One table row; the item name may wrap, the numbers stay on one line.</summary>
    public static FrameworkElement ItemTableRow(string name, string quantity, string price, string total, string? detail = null)
    {
        var row = new Grid { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(0.9), 0, 0) };
        var cells = new[] { name, quantity, price, total };
        for (var index = 0; index < cells.Length; index++)
        {
            var isName = index == 0;
            var cell = new TextBlock
            {
                Text = cells[index],
                FontFamily = new FontFamily(isName && !HasArabic(cells[index]) ? CodeFont : ArabicFont),
                FontSize = 8.6,
                Foreground = Brushes.Black,
                FontWeight = isName ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = isName ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextAlignment = isName ? TextAlignment.Right : TextAlignment.Center,
                FlowDirection = isName ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Mm(0.6), 0, Mm(0.6), 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cell, index);
            row.Children.Add(cell);
        }

        var host = new StackPanel();
        host.Children.Add(row);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            host.Children.Add(new TextBlock
            {
                Text = detail,
                FontFamily = new FontFamily(CodeFont),
                FontSize = 7.4,
                Foreground = Brushes.Black,
                FlowDirection = FlowDirection.RightToLeft,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, Mm(0.3), 0, 0)
            });
        }

        return host;
    }

    private static bool HasArabic(string value) =>
        value.Any(character => character is >= '\u0600' and <= '\u06FF');

    /// <summary>Label on the right, amount on the left, as in a printed totals block.</summary>
    public static FrameworkElement TotalRow(string label, string value, bool bold = false, double size = 8.8)
    {
        var grid = new Grid { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(0.4), 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var key = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily(ArabicFont),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brushes.Black,
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(key, 0);
        grid.Children.Add(key);

        var amount = new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily(CodeFont),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brushes.Black,
            FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(Mm(1.5), 0, 0, 0)
        };
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);
        return grid;
    }

    /// <summary>
    /// A single centred line of text — the shape the owner asked for on the sales receipt, where the
    /// invoice number, the date and every total share the middle of the paper.
    /// </summary>
    public static FrameworkElement CenteredLine(string? text, bool bold = false, double size = 9)
    {
        return new TextBlock
        {
            Text = text ?? "",
            FontFamily = new FontFamily(ArabicFont),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection.RightToLeft,
            LineHeight = Mm(4.2),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(0, Mm(0.3), 0, 0)
        };
    }

    /// <summary>A centred section caption with a rule above and below, e.g. «الأصناف».</summary>
    public static FrameworkElement CenteredSectionTitle(string text)
    {
        var panel = new StackPanel { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(2), 0, Mm(0.5)) };
        panel.Children.Add(ThinRule(0, 1));
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(ArabicFont),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, Mm(0.7), 0, Mm(0.7))
        });
        panel.Children.Add(ThinRule(0.4, 0));
        return panel;
    }

    /// <summary>The grand total on its own centred line, framed top and bottom.</summary>
    public static FrameworkElement CenteredGrandTotal(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, Mm(1.2), 0, Mm(0.4)) };
        panel.Children.Add(new Border { Height = 1.1, Background = Brushes.Black });
        var line = CenteredLine($"{label}: {value}", bold: true, size: 11);
        line.Margin = new Thickness(0, Mm(1.2), 0, Mm(1.2));
        panel.Children.Add(line);
        panel.Children.Add(new Border { Height = 1.1, Background = Brushes.Black });
        return panel;
    }

    /// <summary>The grand-total row, framed top and bottom so the eye lands on it.</summary>
    public static FrameworkElement GrandTotal(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, Mm(1), 0, 0) };
        panel.Children.Add(new Border { Height = 1.1, Background = Brushes.Black });
        var row = TotalRow(label, value, bold: true, size: 10);
        row.Margin = new Thickness(0, Mm(1), 0, Mm(1));
        panel.Children.Add(row);
        panel.Children.Add(new Border { Height = 1.1, Background = Brushes.Black });
        return panel;
    }

    /// <summary>Square-cornered block with a heading, used for notes, totals and warranty.</summary>
    public static FrameworkElement Box(string heading, FrameworkElement body)
    {
        var panel = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        if (!string.IsNullOrWhiteSpace(heading))
            panel.Children.Add(new TextBlock
            {
                Text = heading,
                FontFamily = new FontFamily(ArabicFont),
                FontSize = 8.6,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Thickness(0, 0, 0, Mm(0.8))
            });
        panel.Children.Add(body);
        return new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.8),
            Padding = new Thickness(Mm(2)),
            Margin = new Thickness(0, Mm(1.5), 0, 0),
            Child = panel
        };
    }

    /// <summary>Signature and stamp lines at the foot of the document.</summary>
    public static FrameworkElement Signatures(params string[] captions)
    {
        var grid = new Grid { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(4), 0, 0) };
        for (var index = 0; index < captions.Length; index++)
        {
            var cell = new StackPanel { Margin = new Thickness(Mm(2), 0, Mm(2), 0) };
            cell.Children.Add(new Border
            {
                Height = 0.8,
                Background = Brushes.Black,
                Margin = new Thickness(0, 0, 0, Mm(1))
            });
            cell.Children.Add(Centered(captions[index], 8, FontWeights.Normal, 11));
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cell, index);
            grid.Children.Add(cell);
        }

        return grid;
    }

    /// <summary>Closing line: the store note, then the computer-printed disclaimer.</summary>
    public static FrameworkElement Footer(string note)
    {
        var panel = new StackPanel { FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, Mm(3), 0, 0) };
        panel.Children.Add(ThinRule(0, 1.5));
        if (!string.IsNullOrWhiteSpace(note))
            panel.Children.Add(new TextBlock
            {
                Text = note,
                FontFamily = new FontFamily(ArabicFont),
                FontSize = 8,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Thickness(0, Mm(1.5), 0, 0)
            });
        panel.Children.Add(new TextBlock
        {
            Text = "مستند مطبوع من نظام إدارة المحل · لا يُعتمد إلا بختم المحل",
            FontFamily = new FontFamily(ArabicFont),
            FontSize = 7.2,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, Mm(0.6), 0, 0)
        });
        return panel;
    }

    // ------------------------------------------------------------------ paper and printing

    /// <summary>Wraps the document body in an 80 mm white page with even margins.</summary>
    public static FrameworkElement Paper(IReadOnlyList<FrameworkElement> blocks)
    {
        var body = new StackPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            MaxWidth = Mm(ContentWidthMillimetres)
        };
        foreach (var block in blocks) body.Children.Add(block);

        return new Border
        {
            Width = Mm(PaperWidthMillimetres),
            Background = Brushes.White,
            Padding = new Thickness(Mm(PaddingMillimetres)),
            Child = body,
            FlowDirection = FlowDirection.LeftToRight,
            Language = XmlLanguage.GetLanguage("ar-EG"),
            SnapsToDevicePixels = true
        };
    }

    /// <summary>
    /// The physical pages handed to <see cref="PrintDialog"/>: exactly 80 mm wide and as tall as the
    /// content, so a roll printer cuts right under the last line. A factory builds a fresh tree per
    /// copy because a WPF visual can only have one parent.
    /// </summary>
    public static FixedDocument BuildDocument(Func<FrameworkElement> paper, double printableWidthDips, int copies = 1)
    {
        if (paper is null) throw new ArgumentNullException(nameof(paper));
        if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies), "عدد النسخ يجب أن يكون أكبر من صفر.");

        var pageWidth = Mm(PaperWidthMillimetres);
        var contentWidth = printableWidthDips > 0 ? Math.Min(printableWidthDips, pageWidth) : pageWidth;

        var document = new FixedDocument();
        for (var copy = 0; copy < copies; copy++)
        {
            var visual = paper();
            visual.Measure(new Size(contentWidth, double.PositiveInfinity));
            var height = Math.Max(Mm(30), Math.Ceiling(visual.DesiredSize.Height) + 2);

            var page = new FixedPage
            {
                Width = pageWidth,
                Height = height,
                Background = Brushes.White,
                Language = XmlLanguage.GetLanguage("ar-EG")
            };
            var host = new Border
            {
                Width = pageWidth,
                Height = height,
                Background = Brushes.White,
                Child = visual
            };
            FixedPage.SetLeft(host, 0);
            FixedPage.SetTop(host, 0);
            page.Children.Add(host);

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        return document;
    }

    /// <summary>
    /// Test seam: the smoke tests run headless, so they substitute a fake printer here instead of
    /// opening the system print dialog. Production code always leaves it null.
    /// </summary>
    public static Func<Func<FrameworkElement>, string, int, ReceiptPrintResult>? InteractivePrinter { get; set; }

    /// <summary>Asks the user for a printer, then prints; or uses the test seam when one is set.</summary>
    public static ReceiptPrintResult PrintInteractive(Func<FrameworkElement> paper, string jobName, int copies)
    {
        if (InteractivePrinter is { } printer) return printer(paper, jobName, copies);

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return ReceiptPrintResult.Cancelled();
        return Print(paper, dialog, jobName, copies);
    }

    /// <summary>
    /// Measures the selected printer, builds the pages and sends them. Returns Arabic result text
    /// instead of throwing so the preview window can show it in its status line.
    /// </summary>
    public static ReceiptPrintResult Print(Func<FrameworkElement> paper, PrintDialog dialog, string jobName, int copies = 1)
    {
        try
        {
            var ticket = dialog.PrintTicket ?? new PrintTicket();
            ticket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket = ticket;

            var printableWidth = Mm(PaperWidthMillimetres);
            if (dialog.PrintQueue is { } queue && queue.GetPrintCapabilities(ticket) is { } capabilities
                && capabilities.PageImageableArea is { ExtentWidth: > 0 } area)
            {
                printableWidth = area.ExtentWidth;
            }

            // A printer that cannot feed 40 mm cannot print a receipt roll at all.
            if (printableWidth < Mm(40))
                return ReceiptPrintResult.Failed("الطابعة المختارة لا تدعم عرض الإيصالات الحراري (80 مم). اختر طابعة الإيصالات.");

            var document = BuildDocument(paper, printableWidth, copies);
            dialog.PrintDocument(document.DocumentPaginator, jobName);
            var printer = dialog.PrintQueue?.FullName ?? "الطابعة الافتراضية";
            return ReceiptPrintResult.Printed(printer, copies);
        }
        catch (Exception ex)
        {
            return ReceiptPrintResult.Failed($"تعذّرت طباعة الفاتورة: {ex.Message}");
        }
    }
}

/// <summary>Outcome of a receipt print request.</summary>
public sealed record ReceiptPrintResult(bool Succeeded, bool WasCancelled, string PrinterName, int Copies, string Message)
{
    public static ReceiptPrintResult Cancelled() => new(false, true, string.Empty, 0, string.Empty);
    public static ReceiptPrintResult Printed(string printer, int copies) => new(true, false, printer, copies, string.Empty);
    public static ReceiptPrintResult Failed(string message) => new(false, false, string.Empty, 0, message);
}
