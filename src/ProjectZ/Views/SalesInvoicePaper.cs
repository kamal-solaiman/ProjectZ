using System.Windows;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Views;

/// <summary>
/// The classic 80 mm sales receipt as the shop prints it: store letterhead, centred invoice number and
/// date, the item table with serials, centred totals and settlement lines, then the store's thank-you
/// note. There is no cashier line, no customer block, no signature row and no legal footer — the paper
/// carries only what the owner asked for.
/// </summary>
public static class SalesInvoicePaper
{
    public static FrameworkElement Build(SalesInvoiceDesignItem invoice)
    {
        var blocks = new List<FrameworkElement>
        {
            ClassicReceipt.Header(),
            ClassicReceipt.DoubleRule(),
            ClassicReceipt.Title("فاتورة مبيعات")
        };

        // Invoice identity — centred, never pushed to one side.
        blocks.Add(ClassicReceipt.CenteredLine($"رقم الفاتورة: {invoice.InvoiceNumber}", bold: true));
        blocks.Add(ClassicReceipt.CenteredLine($"التاريخ: {invoice.IssuedFullDisplay}"));
        blocks.Add(ClassicReceipt.ThinRule(1.5, 1));

        // Items
        blocks.Add(ClassicReceipt.CenteredSectionTitle("الأصناف"));
        blocks.Add(ClassicReceipt.ItemTableHeader("الصنف", "الكمية", "السعر", "الإجمالي"));
        foreach (var line in invoice.Items.OrderBy(line => line.LineNumber))
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

        // Counts, totals and settlement — every line centred.
        blocks.Add(ClassicReceipt.CenteredLine($"عدد الأصناف: {invoice.TotalLineCount}   ·   عدد القطع: {invoice.TotalUnitsCount}"));
        blocks.Add(ClassicReceipt.CenteredLine($"الإجمالي الفرعي: {invoice.SubtotalDisplay}"));
        if (invoice.HasDiscount)
            blocks.Add(ClassicReceipt.CenteredLine($"الخصم: - {invoice.EffectiveDiscountDisplay}"));
        blocks.Add(ClassicReceipt.CenteredGrandTotal("الصافي المطلوب", invoice.NetTotalDisplay));
        if (invoice.HasReturnedAmount)
            blocks.Add(ClassicReceipt.CenteredLine($"قيمة المرتجع: - {invoice.ReturnedTotalDisplay}"));
        if (invoice.HasReturnedAmount)
            blocks.Add(ClassicReceipt.CenteredLine($"صافي البيع بعد المرتجع: {invoice.EffectiveNetSalesDisplay}", bold: true));

        blocks.Add(ClassicReceipt.CenteredSectionTitle("تفاصيل السداد"));
        if (invoice.HasCashPaid)
            blocks.Add(ClassicReceipt.CenteredLine($"المدفوع كاش: {invoice.CashPaidDisplay}"));
        if (invoice.HasVodafoneCash)
            blocks.Add(ClassicReceipt.CenteredLine($"المدفوع فودافون كاش: {invoice.VodafoneCashPaidDisplay}"));
        if (invoice.HasInstaPay)
            blocks.Add(ClassicReceipt.CenteredLine($"المدفوع إنستاباي: {invoice.InstaPayPaidDisplay}"));
        blocks.Add(ClassicReceipt.CenteredLine($"إجمالي المسدد: {invoice.GrossPaidDisplay}", bold: true));
        if (invoice.HasRefundedAmount)
            blocks.Add(ClassicReceipt.CenteredLine($"المردود للعميل: - {invoice.RefundedAmountDisplay}"));
        blocks.Add(ClassicReceipt.CenteredLine($"صافي المحصل: {invoice.NetCollectedDisplay}", bold: true));
        if (invoice.HasChange)
            blocks.Add(ClassicReceipt.CenteredLine($"الباقي المرتجع: {invoice.ChangeAmountDisplay}"));
        if (invoice.HasRemaining)
            blocks.Add(ClassicReceipt.CenteredLine($"المتبقي الآجل: {invoice.RemainingAmountDisplay}", bold: true));
        blocks.Add(ClassicReceipt.CenteredLine($"طريقة السداد: {invoice.PaymentMethodSummary}"));

        if (invoice.HasNotes)
            blocks.Add(ClassicReceipt.CenteredLine($"ملاحظات: {invoice.Notes}"));

        blocks.Add(ClassicReceipt.Footer(StoreIdentity.Current.FooterNote, includePrintedDisclaimer: false));
        return ClassicReceipt.Paper(blocks);
    }
}
