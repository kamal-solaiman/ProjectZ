using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Views;

/// <summary>
/// The purchase invoice printed with the same classic look as the sales receipt: store letterhead,
/// centred invoice data, the item table with serials, then centred totals and settlement lines. It is
/// built for two papers from one source — an 80 mm thermal receipt and an A4 sheet — so both carry the
/// same centred numbers.
/// </summary>
public static class PurchaseInvoicePaper
{
    /// <summary>The 80 mm thermal roll receipt.</summary>
    public static FrameworkElement BuildThermal(PurchaseInvoiceDesignItem invoice) =>
        ClassicReceipt.Paper(Blocks(invoice, compact: true));

    /// <summary>The A4 sheet: same text, roomier margins and rules for an office printer.</summary>
    public static FixedDocument BuildA4Document(PurchaseInvoiceDesignItem invoice, double printableWidthDips, double printableHeightDips, int copies = 1)
    {
        if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies), "عدد النسخ يجب أن يكون أكبر من صفر.");

        const double a4Width = 210;
        const double a4Height = 297;
        var width = printableWidthDips > 0 ? printableWidthDips : ClassicReceipt.Mm(a4Width);
        var height = printableHeightDips > 0 ? printableHeightDips : ClassicReceipt.Mm(a4Height);

        var document = new FixedDocument();
        var pageSize = new Size(width, height);
        for (var copy = 0; copy < copies; copy++)
        {
            var paper = A4Paper(Blocks(invoice, compact: false));
            paper.Measure(new Size(width, height));

            var page = new FixedPage
            {
                Width = width,
                Height = height,
                Background = Brushes.White,
                Language = XmlLanguage.GetLanguage("ar-EG")
            };
            FixedPage.SetLeft(paper, 0);
            FixedPage.SetTop(paper, 0);
            page.Children.Add(paper);

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        document.DocumentPaginator.PageSize = pageSize;
        return document;
    }

    // ------------------------------------------------------------------ content

    private static List<FrameworkElement> Blocks(PurchaseInvoiceDesignItem invoice, bool compact)
    {
        var title = compact ? 12 : 18;
        var size = compact ? 9 : 11;
        var small = compact ? 8.2 : 10;

        var blocks = new List<FrameworkElement>
        {
            compact ? ClassicReceipt.Header() : A4Header(title),
            compact ? ClassicReceipt.DoubleRule() : ClassicReceipt.ThinRule(3, 3),
            ClassicReceipt.Centered("فاتورة شراء", title, FontWeights.Bold, title + 6),
            ClassicReceipt.CenteredLine($"رقم الفاتورة: {invoice.InvoiceNumber}", bold: true, size: size + 0.4),
            ClassicReceipt.CenteredLine($"التاريخ: {invoice.IssuedFullDisplay}", size: size),
            ClassicReceipt.CenteredLine($"فاتورة المورد: {invoice.SupplierInvoiceNumberDisplay}", size: size)
        };

        // The supplier: name and phone only — no company, address or id.
        blocks.Add(ClassicReceipt.CenteredSectionTitle("المورد"));
        blocks.Add(ClassicReceipt.CenteredLine($"الاسم: {invoice.SupplierName}", bold: true, size: size));
        blocks.Add(ClassicReceipt.CenteredLine($"الهاتف: {invoice.SupplierPhoneDisplay}", size: size));

        blocks.Add(ClassicReceipt.CenteredSectionTitle("الأصناف"));
        blocks.Add(ClassicReceipt.ItemTableHeader("الصنف", "الكمية", "السعر", "الإجمالي"));
        foreach (var line in invoice.Items.OrderBy(line => line.LineNumber))
        {
            // Purchase quotes the supplier cost; the inventory ids never print.
            var serials = line.SerialNumbers.Count > 0
                ? "سيريال: " + string.Join("، ", line.SerialNumbers)
                : null;
            if (string.IsNullOrWhiteSpace(serials)) serials = null;
            if (line.ReturnedQuantity > 0)
                serials = (serials is null ? "" : serials + " · ") + $"مرتجع {line.ReturnedQuantity} من {line.Quantity}";

            blocks.Add(ClassicReceipt.ItemTableRow(
                line.ProductName,
                line.QuantityDisplay,
                line.UnitPurchaseCostDisplay,
                line.LineTotalDisplay,
                serials));
        }

        blocks.Add(ClassicReceipt.ThinRule(1.5, 0.5));

        blocks.Add(ClassicReceipt.CenteredLine(
            $"عدد الأصناف: {invoice.TotalLineCount}   ·   عدد القطع: {invoice.TotalUnitsCount}", size: small));
        blocks.Add(ClassicReceipt.CenteredLine($"الإجمالي الفرعي: {invoice.SubtotalDisplay}", size: size));
        if (invoice.HasReturnedGoods)
            blocks.Add(ClassicReceipt.CenteredLine($"قيمة المرتجع: - {invoice.ReturnedGoodsTotalDisplay}", size: size));
        if (invoice.HasDiscount)
            blocks.Add(ClassicReceipt.CenteredLine($"الخصم: - {invoice.EffectiveDiscountDisplay}", size: size));
        blocks.Add(ClassicReceipt.CenteredGrandTotal("الصافي المطلوب", invoice.NetTotalDisplay));

        blocks.Add(ClassicReceipt.CenteredSectionTitle("تفاصيل السداد"));
        if (invoice.HasCashPaid)
            blocks.Add(ClassicReceipt.CenteredLine($"المدفوع كاش: {invoice.CashPaidDisplay}", size: size));
        if (invoice.VodafoneCashPaid > 0m)
            blocks.Add(ClassicReceipt.CenteredLine($"المدفوع فودافون كاش: {invoice.VodafoneCashPaidDisplay}", size: size));
        if (invoice.InstaPayPaid > 0m)
            blocks.Add(ClassicReceipt.CenteredLine($"المدفوع إنستاباي: {invoice.InstaPayPaidDisplay}", size: size));
        if (invoice.CreditApplied > 0m)
            blocks.Add(ClassicReceipt.CenteredLine($"مقاصة من المرتجعات: {PosFormatting.FormatCurrency(invoice.CreditApplied)}", size: size));
        blocks.Add(ClassicReceipt.CenteredLine($"إجمالي المسدد: {invoice.TotalPaidDisplay}", bold: true, size: size));
        if (invoice.HasSupplierRefundReceived)
            blocks.Add(ClassicReceipt.CenteredLine($"المسترد من المورد: - {invoice.SupplierRefundReceivedDisplay}", size: size));
        blocks.Add(ClassicReceipt.CenteredLine($"صافي الصرف: {invoice.NetCashOutflowDisplay}", bold: true, size: size));
        if (invoice.HasRemainingPayable)
            blocks.Add(ClassicReceipt.CenteredLine($"المتبقي الآجل للمورد: {invoice.RemainingPayableDisplay}", bold: true, size: size));
        blocks.Add(ClassicReceipt.CenteredLine($"طريقة السداد: {invoice.PaymentMethodSummary}", size: size));
        blocks.Add(ClassicReceipt.CenteredLine($"حالة الفاتورة: {invoice.StatusTitle}", size: size));

        if (invoice.HasNotes)
            blocks.Add(ClassicReceipt.CenteredLine($"ملاحظات: {invoice.Notes}", size: small));

        blocks.Add(ClassicReceipt.Footer(StoreIdentity.Current.FooterNote, includePrintedDisclaimer: false));
        return blocks;
    }

    private static FrameworkElement A4Header(double title)
    {
        var panel = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        panel.Children.Add(ClassicReceipt.Centered(StoreIdentity.Current.Name, title + 4, FontWeights.Bold, 30));
        panel.Children.Add(ClassicReceipt.Centered(StoreIdentity.Current.ServiceCenterHeader, title - 4, FontWeights.SemiBold, 20));
        if (StoreIdentity.Current.HasContactLine)
            panel.Children.Add(ClassicReceipt.Centered(StoreIdentity.Current.ContactLine, title - 6, FontWeights.Normal, 18, wrap: true));
        return panel;
    }

    /// <summary>The A4 page: A4 width with generous margins, the same centred content.</summary>
    private static FrameworkElement A4Paper(IReadOnlyList<FrameworkElement> blocks)
    {
        var body = new StackPanel { FlowDirection = FlowDirection.LeftToRight };
        foreach (var block in blocks) body.Children.Add(block);

        return new Border
        {
            Width = ClassicReceipt.Mm(210),
            Background = Brushes.White,
            Padding = new Thickness(ClassicReceipt.Mm(18)),
            Child = body,
            FlowDirection = FlowDirection.LeftToRight,
            Language = XmlLanguage.GetLanguage("ar-EG"),
            SnapsToDevicePixels = true
        };
    }
}
