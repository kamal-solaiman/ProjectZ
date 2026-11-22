using System.Windows;
using System.Windows.Controls;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Views;

/// <summary>
/// The classic 80 mm sales invoice: letterhead, invoice meta, customer block, item table with
/// serials, totals box, payment breakdown and signature. Every figure is the invoice's own
/// (subtotal, discount, net, collected, refunded, remaining) — no estimates are added.
/// </summary>
public static class SalesInvoicePaper
{
    public static FrameworkElement Build(SalesInvoiceDesignItem invoice)
    {
        var blocks = new List<FrameworkElement>
        {
            ClassicReceipt.Header(),
            ClassicReceipt.DoubleRule(),
            ClassicReceipt.Title("فاتورة مبيعات", "إيصال مبيعات ضريبية مبسطة"),
            ClassicReceipt.DottedRule()
        };

        // Invoice identity block, printed in a fixed order so it never re-orders in Arabic.
        blocks.Add(ClassicReceipt.Field("رقم الفاتورة", invoice.InvoiceNumber, bold: true));
        blocks.Add(ClassicReceipt.Field("التاريخ", invoice.IssuedFullDisplay));
        blocks.Add(ClassicReceipt.Field("الكاشير", invoice.CashierName));

        blocks.Add(ClassicReceipt.SectionTitle("بيانات العميل"));
        blocks.Add(ClassicReceipt.Field("العميل", invoice.CustomerName, bold: true));
        blocks.Add(ClassicReceipt.Field("الهاتف", invoice.CustomerPhoneDisplay));
        if (!invoice.IsWalkInCustomer)
            blocks.Add(ClassicReceipt.Field("العنوان", invoice.CustomerAddressDisplay));
        blocks.Add(ClassicReceipt.Field("فئة السعر", invoice.DefaultPriceTierTitle));
        blocks.Add(ClassicReceipt.Field("حالة الفاتورة", invoice.StatusTitle));

        blocks.Add(ClassicReceipt.SectionTitle("الأصناف"));
        blocks.Add(ClassicReceipt.ItemTableHeader("الصنف", "الكمية", "السعر", "الإجمالي"));
        var lines = invoice.Items.OrderBy(line => line.LineNumber).ToList();
        foreach (var line in lines)
        {
            var detail = string.IsNullOrWhiteSpace(line.SerialSummary) ? null : line.SerialSummary;
            if (line.ReturnedQuantity > 0)
                detail = (detail is null ? "" : detail + " · ") + $"مرتجع {line.ReturnedQuantity} من {line.Quantity}";
            if (line.IsExchangeReplacement)
                detail = (detail is null ? "" : detail + " · ") + "بديل استبدال";
            blocks.Add(ClassicReceipt.ItemTableRow(
                line.ProductName,
                line.QuantityDisplay,
                line.UnitPriceDisplay,
                line.LineGrossTotalDisplay,
                detail));
        }

        blocks.Add(ClassicReceipt.ThinRule(1.5, 0.5));
        blocks.Add(ClassicReceipt.TotalRow("عدد الأصناف / القطع", $"{invoice.TotalLineCount} / {invoice.TotalUnitsCount}"));

        // Totals box: subtotal, discount, returns, then the grand total.
        var totals = new StackPanel();
        totals.Children.Add(ClassicReceipt.TotalRow("الإجمالي الفرعي", invoice.SubtotalDisplay));
        if (invoice.HasDiscount)
            totals.Children.Add(ClassicReceipt.TotalRow("الخصم", "- " + invoice.EffectiveDiscountDisplay));
        totals.Children.Add(ClassicReceipt.GrandTotal("الصافي المطلوب", invoice.NetTotalDisplay));
        if (invoice.HasReturnedAmount)
            totals.Children.Add(ClassicReceipt.TotalRow("قيمة المرتجع", "- " + invoice.ReturnedTotalDisplay));
        if (invoice.HasReturnedAmount)
            totals.Children.Add(ClassicReceipt.TotalRow("صافي البيع بعد المرتجع", invoice.EffectiveNetSalesDisplay, bold: true));
        blocks.Add(ClassicReceipt.Box("", totals));

        // Payment breakdown — only the channels that actually carry money.
        var payment = new StackPanel();
        if (invoice.HasCashPaid)
            payment.Children.Add(ClassicReceipt.TotalRow("المدفوع كاش", invoice.CashPaidDisplay));
        if (invoice.HasVodafoneCash)
            payment.Children.Add(ClassicReceipt.TotalRow("المدفوع فودافون كاش", invoice.VodafoneCashPaidDisplay));
        if (invoice.HasInstaPay)
            payment.Children.Add(ClassicReceipt.TotalRow("المدفوع إنستاباي", invoice.InstaPayPaidDisplay));
        payment.Children.Add(ClassicReceipt.TotalRow("إجمالي المسدد", invoice.GrossPaidDisplay, bold: true));
        if (invoice.HasRefundedAmount)
            payment.Children.Add(ClassicReceipt.TotalRow("المردود للعميل", "- " + invoice.RefundedAmountDisplay));
        payment.Children.Add(ClassicReceipt.TotalRow("صافي المحصل", invoice.NetCollectedDisplay, bold: true));
        if (invoice.HasChange)
            payment.Children.Add(ClassicReceipt.TotalRow("الباقي المرتجع", invoice.ChangeAmountDisplay));
        if (invoice.HasRemaining)
            payment.Children.Add(ClassicReceipt.TotalRow("المتبقي الآجل", invoice.RemainingAmountDisplay, bold: true));
        payment.Children.Add(ClassicReceipt.TotalRow("طريقة السداد", invoice.PaymentMethodSummary));
        blocks.Add(ClassicReceipt.Box("السداد", payment));

        if (invoice.HasNotes)
            blocks.Add(ClassicReceipt.Box("ملاحظات", ClassicReceipt.Paragraph(invoice.Notes)));

        blocks.Add(ClassicReceipt.Signatures("توقيع الكاشير", "توقيع العميل"));
        blocks.Add(ClassicReceipt.Footer(StoreIdentityNote()));
        return ClassicReceipt.Paper(blocks);
    }


    private static string StoreIdentityNote() => StoreIdentity.Current.FooterNote;
}
