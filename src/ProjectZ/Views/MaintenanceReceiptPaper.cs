using System.Windows;
using System.Windows.Controls;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Views;

/// <summary>
/// The two classic 80 mm maintenance documents.
/// <para>
/// <b>Intake (استلام):</b> device and fault exactly as received, the passcode policy, the deposit
/// paid, what remains, and the customer's signature accepting the inspection terms.
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
            ClassicReceipt.Title("إيصال استلام جهاز", "استلام جهاز للصيانة"),
            ClassicReceipt.Field("رقم أمر الصيانة", order.OrderNumber, bold: true),
            ClassicReceipt.Field("تاريخ الاستلام", order.ReceivedFullDisplay),
            ClassicReceipt.Field("المستلم", order.ReceivedByName),
            ClassicReceipt.Field("الفني المسؤول", order.AssignedTechnicianDisplay),
            ClassicReceipt.Field("الأولوية", order.PriorityTitle)
        };

        blocks.Add(ClassicReceipt.SectionTitle("بيانات العميل"));
        blocks.Add(ClassicReceipt.Field("العميل", order.CustomerName, bold: true));
        blocks.Add(ClassicReceipt.Field("الهاتف", order.CustomerPhoneDisplay));
        blocks.Add(ClassicReceipt.Field("هاتف إضافي", order.CustomerSecondaryPhone));
        blocks.Add(ClassicReceipt.Field("العنوان", order.CustomerAddress));

        blocks.Add(ClassicReceipt.SectionTitle("الجهاز كما استُلم"));
        blocks.Add(ClassicReceipt.Field("الجهاز", order.DeviceFullTitle, bold: true));
        blocks.Add(ClassicReceipt.Field("النوع", order.DeviceType));
        blocks.Add(ClassicReceipt.Field("السيريال / IMEI", order.SerialOrImeiDisplay));
        blocks.Add(ClassicReceipt.Field("كود الجهاز", order.PasscodeDisplay));
        blocks.Add(ClassicReceipt.Field("الحالة الظاهرية", order.PhysicalConditionDisplay));
        blocks.Add(ClassicReceipt.Field("الملحقات", order.AccessoriesDisplay));
        blocks.Add(ClassicReceipt.Field("العطل كما ذكره العميل", order.ReportedFault, bold: true));
        if (order.HasIntakeNotes)
            blocks.Add(ClassicReceipt.Field("ملاحظات الاستلام", order.IntakeNotes));
        blocks.Add(ClassicReceipt.Field("التسليم المتوقع", order.ExpectedDeliveryDisplay));

        // Money: only what the customer actually paid is a fact; the cost is still an estimate.
        blocks.Add(ClassicReceipt.SectionTitle("العربون والتكلفة"));
        var money = new StackPanel();
        money.Children.Add(ClassicReceipt.TotalRow("التكلفة المبدئية", order.EstimatedCostDisplay));
        if (order.HasDeposit)
        {
            money.Children.Add(ClassicReceipt.TotalRow("العربون المدفوع", order.TotalDepositPaidDisplay, bold: true));
            if (order.HasDepositCash)
                money.Children.Add(ClassicReceipt.TotalRow("كاش", order.DepositCashPaidDisplay));
            if (order.HasDepositVodafone)
                money.Children.Add(ClassicReceipt.TotalRow("فودافون كاش", order.DepositVodafonePaidDisplay));
            if (order.HasDepositInstaPay)
                money.Children.Add(ClassicReceipt.TotalRow("إنستاباي", order.DepositInstaPayPaidDisplay));
        }
        else
        {
            money.Children.Add(ClassicReceipt.TotalRow("العربون المدفوع", "بدون عربون"));
        }

        if (order.HasEstimatedRemaining)
            money.Children.Add(ClassicReceipt.TotalRow("المتبقي المتوقع عند التسليم", order.EstimatedRemainingDisplay, bold: true));
        blocks.Add(ClassicReceipt.Box("", money));

        blocks.Add(ClassicReceipt.Box("شروط الاستلام",
            ClassicReceipt.Paragraph(
                "• يُرجى الاحتفاظ بهذا الإيصال واستحضاره عند استلام الجهاز.\n" +
                "• المحل غير مسؤول عن الأجهزة المتروكة أكثر من 30 يومًا من تاريخ إبلاغ العميل بجهوزيتها.\n" +
                "• لا يشمل الفحص أو الإصلاح أي أعطال أخرى تظهر بعد الفحص، ويُبلَّغ العميل بها قبل التنفيذ.\n" +
                "• الضمان — إن وُجد — يبدأ من تاريخ التسليم ويخص العطل المُصلح فقط.")));

        blocks.Add(ClassicReceipt.Signatures("توقيع العميل", "ختم المحل"));
        blocks.Add(ClassicReceipt.Footer(StoreIdentity.Current.FooterNote));
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
