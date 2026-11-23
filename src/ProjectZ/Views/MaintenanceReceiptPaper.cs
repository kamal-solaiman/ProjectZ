using System.Windows;
using System.Windows.Controls;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Views;

/// <summary>
/// The two classic 80 mm maintenance documents.
/// <para>
/// <b>Intake (استلام):</b> customer, device title, the fault as reported, the deposit paid and what
/// remains — every line centred, with no signatures, no stamp line and no legal footer.
/// </para>
/// <para>
/// <b>Delivery (تسليم):</b> the final invoice — parts, labour and discount, the grand total, what was
/// collected across all channels, the remaining balance and the warranty that starts on delivery.
/// </para>
/// </summary>
public static class MaintenanceReceiptPaper
{
    public static FrameworkElement BuildIntake(MaintenanceOrderDesignItem order)
    {
        var blocks = new List<FrameworkElement>
        {
            ClassicReceipt.Header(),
            ClassicReceipt.DoubleRule(),
            ClassicReceipt.Title("إيصال استلام جهاز"),

            // Order identity — centred, like the sales receipt.
            ClassicReceipt.CenteredLine($"رقم أمر الصيانة: {order.OrderNumber}", bold: true),
            ClassicReceipt.CenteredLine($"تاريخ الاستلام: {order.ReceivedFullDisplay}"),
            ClassicReceipt.ThinRule(1.5, 1)
        };

        blocks.Add(ClassicReceipt.CenteredSectionTitle("بيانات العميل"));
        blocks.Add(ClassicReceipt.CenteredLine($"العميل: {order.CustomerName}", bold: true));
        blocks.Add(ClassicReceipt.CenteredLine($"الهاتف: {order.CustomerPhoneDisplay}"));
        if (!string.IsNullOrWhiteSpace(order.CustomerSecondaryPhone))
            blocks.Add(ClassicReceipt.CenteredLine($"هاتف إضافي: {order.CustomerSecondaryPhone}"));
        if (!string.IsNullOrWhiteSpace(order.CustomerAddress))
            blocks.Add(ClassicReceipt.CenteredLine($"العنوان: {order.CustomerAddress}"));

        // The device title, the fault and the intake notes are what the customer is accepting.
        blocks.Add(ClassicReceipt.CenteredSectionTitle("الجهاز والعطل"));
        blocks.Add(ClassicReceipt.CenteredLine($"الجهاز: {order.DeviceFullTitle}", bold: true));
        blocks.Add(ClassicReceipt.CenteredLine($"العطل كما ذكره العميل: {order.ReportedFault}", bold: true));
        if (order.HasIntakeNotes)
            blocks.Add(ClassicReceipt.CenteredLine($"ملاحظات الاستلام: {order.IntakeNotes}"));

        // Money: only what the customer actually paid is a fact; the cost is still an estimate.
        blocks.Add(ClassicReceipt.CenteredSectionTitle("العربون والتكلفة"));
        blocks.Add(ClassicReceipt.CenteredLine($"التكلفة المبدئية: {order.EstimatedCostDisplay}"));
        if (order.HasDeposit)
        {
            blocks.Add(ClassicReceipt.CenteredLine($"العربون المدفوع: {order.TotalDepositPaidDisplay}", bold: true));
            if (order.HasDepositCash)
                blocks.Add(ClassicReceipt.CenteredLine($"كاش: {order.DepositCashPaidDisplay}"));
            if (order.HasDepositVodafone)
                blocks.Add(ClassicReceipt.CenteredLine($"فودافون كاش: {order.DepositVodafonePaidDisplay}"));
            if (order.HasDepositInstaPay)
                blocks.Add(ClassicReceipt.CenteredLine($"إنستاباي: {order.DepositInstaPayPaidDisplay}"));
        }
        else
        {
            blocks.Add(ClassicReceipt.CenteredLine("العربون المدفوع: بدون عربون", bold: true));
        }

        if (order.HasEstimatedRemaining)
            blocks.Add(ClassicReceipt.CenteredGrandTotal("المتبقي المتوقع عند التسليم", order.EstimatedRemainingDisplay));

        blocks.Add(ClassicReceipt.CenteredSectionTitle("شروط الاستلام"));
        blocks.Add(ClassicReceipt.CenteredParagraph(
            "• يُرجى الاحتفاظ بهذا الإيصال واستحضاره عند استلام الجهاز.\n" +
            "• المحل غير مسؤول عن الأجهزة المتروكة أكثر من 30 يومًا من تاريخ إبلاغ العميل بجهوزيتها.\n" +
            "• لا يشمل الفحص أو الإصلاح أي أعطال أخرى تظهر بعد الفحص، ويُبلَّغ العميل بها قبل التنفيذ.\n" +
            "• الضمان — إن وُجد — يبدأ من تاريخ التسليم ويخص العطل المُصلح فقط."));

        // No signature or stamp block and no printed disclaimer on this receipt.
        blocks.Add(ClassicReceipt.Footer(StoreIdentity.Current.FooterNote, includePrintedDisclaimer: false));
        return ClassicReceipt.Paper(blocks);
    }

    public static FrameworkElement BuildDelivery(MaintenanceOrderDesignItem order)
    {
        var blocks = new List<FrameworkElement>
        {
            ClassicReceipt.Header(),
            ClassicReceipt.DoubleRule(),
            ClassicReceipt.Title("فاتورة صيانة وتسليم جهاز", "تسليم الجهاز وضمانه"),
            ClassicReceipt.Field("رقم أمر الصيانة", order.OrderNumber, bold: true),
            ClassicReceipt.Field("تاريخ الاستلام", order.ReceivedFullDisplay),
            ClassicReceipt.Field("تاريخ التسليم", order.DeliveredAtDisplay),
            ClassicReceipt.Field("الفني المسؤول", order.AssignedTechnicianDisplay),
            ClassicReceipt.Field("حالة الأمر", order.StatusTitle)
        };

        blocks.Add(ClassicReceipt.SectionTitle("العميل والجهاز"));
        blocks.Add(ClassicReceipt.Field("العميل", order.CustomerName, bold: true));
        blocks.Add(ClassicReceipt.Field("الهاتف", order.CustomerPhoneDisplay));
        blocks.Add(ClassicReceipt.Field("الجهاز", order.DeviceFullTitle, bold: true));
        blocks.Add(ClassicReceipt.Field("السيريال / IMEI", order.SerialOrImeiDisplay));
        if (order.HasDiagnosisNotes)
            blocks.Add(ClassicReceipt.Field("ما تم تنفيذه", order.DiagnosisNotes));

        // Parts table — the actual replaced parts with their selling price.
        if (order.HasUsedParts)
        {
            blocks.Add(ClassicReceipt.SectionTitle("قطع الغيار المستبدلة"));
            blocks.Add(ClassicReceipt.ItemTableHeader("القطعة", "الكمية", "السعر", "الإجمالي"));
            foreach (var part in order.UsedParts)
                blocks.Add(ClassicReceipt.ItemTableRow(
                    part.ProductName,
                    part.QuantityDisplay,
                    part.UnitSellPriceDisplay,
                    part.LineTotalDisplay));
        }

        // Final invoice totals.
        blocks.Add(ClassicReceipt.SectionTitle("المحاسبة النهائية"));
        var totals = new StackPanel();
        totals.Children.Add(ClassicReceipt.TotalRow("قطع الغيار", order.PartsTotalDisplay));
        totals.Children.Add(ClassicReceipt.TotalRow("المصنعية", order.LaborCostDisplay));
        if (order.HasDiscount)
            totals.Children.Add(ClassicReceipt.TotalRow("الخصم", "- " + order.DiscountAmountDisplay));
        totals.Children.Add(ClassicReceipt.GrandTotal("الإجمالي النهائي", order.EstimatedCostDisplay));
        totals.Children.Add(ClassicReceipt.TotalRow("إجمالي المسدد (عربون + تسليم)", order.TotalCollectedAllDisplay));
        if (order.HasRefundedDeposit)
            totals.Children.Add(ClassicReceipt.TotalRow("المسترد من العربون", order.RefundedDepositAmountDisplay));
        if (order.HasEstimatedRemaining)
            totals.Children.Add(ClassicReceipt.TotalRow("المتبقي المستحق", order.EstimatedRemainingDisplay, bold: true));
        blocks.Add(ClassicReceipt.Box("", totals));

        // Payment channels, from the real payment rows.
        var channels = new StackPanel();
        if (order.HasAnyCashCollected)
            channels.Children.Add(ClassicReceipt.TotalRow("كاش", order.TotalCashCollectedDisplay));
        if (order.HasAnyVodafoneCollected)
            channels.Children.Add(ClassicReceipt.TotalRow("فودافون كاش", order.TotalVodafoneCollectedDisplay));
        if (order.HasAnyInstaPayCollected)
            channels.Children.Add(ClassicReceipt.TotalRow("إنستاباي", order.TotalInstaPayCollectedDisplay));
        channels.Children.Add(ClassicReceipt.TotalRow("طريقة السداد", order.PaymentChannelsSummaryText));
        blocks.Add(ClassicReceipt.Box("طريقة السداد", channels));

        // Warranty box — starts at delivery.
        blocks.Add(ClassicReceipt.Box("الضمان",
            ClassicReceipt.Paragraph(
                order.WarrantySummaryDisplay + "\n" +
                "يسري الضمان على العطل المُصلح فقط، ولا يشمل الكسر أو دخول السوائل أو سوء الاستخدام.")));

        blocks.Add(ClassicReceipt.Signatures("توقيع العميل", "ختم المحل"));
        blocks.Add(ClassicReceipt.Footer(StoreIdentity.Current.FooterNote));
        return ClassicReceipt.Paper(blocks);
    }
}
