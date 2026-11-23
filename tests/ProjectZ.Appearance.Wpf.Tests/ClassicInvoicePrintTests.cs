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
/// Guards the classic invoice look the owner asked for: pure black on white, no accent colours,
/// no rounded cards or pills, and every figure present — on an 80 mm page that is sold as
/// «فواتير كلاسيكية». The tests read the real visual trees, not the XAML text, so a coloured
/// brush that sneaks back into a totals row fails the build.
/// </summary>
internal static class ClassicInvoicePrintTests
{
    public static void Run(Action<bool, string> check)
    {
        var catalog = PosDesignSeedData.CreateSharedSampleCatalog();
        var invoices = InvoiceDesignSeedData.CreateSharedSampleInvoices(
            catalog, InventoryDesignSeedData.CreateSharedSampleRegistrations(catalog));
        check(invoices.Count > 0, "The sample register supplies a sales invoice to print");
        var invoice = invoices[0];

        // --- Paper geometry: exactly 80 mm, one page per requested copy ---------------------
        check(Math.Abs(ClassicReceipt.Mm(80) - 302.362) < 0.01,
            "80 mm is 302.36 DIP at 96 DPI, the width the roll printer expects");

        var document = ClassicReceipt.BuildDocument(() => SalesInvoicePaper.Build(invoice), ClassicReceipt.Mm(72), copies: 3);
        check(document.Pages.Count == 3, "Three copies produce three physical pages");
        var firstPage = document.Pages[0].Child as FixedPage;
        check(firstPage is not null && Math.Abs(firstPage.Width - ClassicReceipt.Mm(80)) < 0.001,
            "Every page is exactly 80 mm wide");
        check(firstPage!.Height > ClassicReceipt.Mm(80),
            "The page grows with the content so the roll is cut under the last line");
        check(document.Pages[1].Child is FixedPage second && Math.Abs(second.Width - firstPage.Width) < 0.001,
            "Copies are independent pages with identical geometry");

        // --- Content: everything a classic invoice shows is on the paper --------------------
        var paper = SalesInvoicePaper.Build(invoice);
        paper.Measure(new Size(ClassicReceipt.Mm(80), double.PositiveInfinity));
        var text = TextOf(paper);
        foreach (var expected in new[]
                 {
                     invoice.InvoiceNumber, invoice.CustomerName, invoice.CashierName,
                     invoice.SubtotalDisplay, invoice.NetTotalDisplay, invoice.GrossPaidDisplay,
                     invoice.PaymentMethodSummary, "الإجمالي الفرعي", "الصافي المطلوب", "السداد",
                     "توقيع الكاشير", "توقيع العميل", "فاتورة مبيعات"
                 })
        {
            check(text.Contains(expected, StringComparison.Ordinal), $"The invoice prints «{expected}»");
        }

        check(text.Contains(invoice.Items[0].ProductName, StringComparison.Ordinal),
            "Every sold line prints its product name");
        check(invoice.HasDiscount == text.Contains("الخصم", StringComparison.Ordinal),
            "The discount row appears exactly when the invoice has a discount");
        check(invoice.HasRemaining == text.Contains("المتبقي الآجل", StringComparison.Ordinal),
            "The credit row appears exactly when money is still owed");

        // --- Colour: nothing but black ink on white paper ----------------------------------
        var brushes = Descendants(paper).OfType<Control>().Select(c => c.Foreground).ToList();
        brushes.AddRange(Descendants(paper).OfType<TextBlock>().Select(t => t.Foreground));
        brushes.AddRange(Descendants(paper).OfType<Border>().Select(b => b.Background)
            .Where(b => b is not null && !Equals(b, Brushes.Transparent))!);
        brushes.AddRange(Descendants(paper).OfType<Border>().Select(b => b.BorderBrush)
            .Where(b => b is not null && !Equals(b, Brushes.Transparent))!);
        check(brushes.All(b => Equals(b, Brushes.Black) || Equals(b, Brushes.White)),
            "The whole invoice uses black ink and white paper only — no accent colour survives");
        check(!Descendants(paper).OfType<Border>().Any(b => b.CornerRadius.TopLeft > 0),
            "No rounded card or pill remains anywhere on the paper");
        check(!Descendants(paper).OfType<FrameworkElement>().Any(e => e is Control and not TextBox && e.Opacity < 1),
            "No faded grey text: every line is solid black");

        // --- The two maintenance documents -------------------------------------------------
        var orders = MaintenanceDesignSeedData.CreateSharedSampleOrders(PosDesignSeedData.CreateSampleCustomers());
        check(orders.Count > 0, "The sample workshop supplies an order to print");
        var delivered = orders.FirstOrDefault(o => o.IsDelivered) ?? orders[0];
        var active = orders.FirstOrDefault(o => !o.IsDelivered) ?? orders[0];

        var intake = MaintenanceReceiptPaper.BuildIntake(active);
        var intakeText = TextOf(intake);
        foreach (var expected in new[]
                 {
                     "إيصال استلام جهاز", active.OrderNumber, active.CustomerName, active.DeviceFullTitle,
                     active.ReportedFault, active.PhysicalConditionDisplay, active.AccessoriesDisplay,
                     active.EstimatedCostDisplay, "شروط الاستلام", "توقيع العميل"
                 })
        {
            check(intakeText.Contains(expected, StringComparison.Ordinal), $"The intake receipt prints «{expected}»");
        }

        check(intakeText.Contains(active.TotalDepositPaidDisplay, StringComparison.Ordinal),
            "The intake receipt prints what the customer actually paid as a deposit");

        var delivery = MaintenanceReceiptPaper.BuildDelivery(delivered);
        var deliveryText = TextOf(delivery);
        foreach (var expected in new[]
                 {
                     "فاتورة صيانة وتسليم جهاز", delivered.OrderNumber, delivered.CustomerName,
                     delivered.DeviceFullTitle, delivered.PartsTotalDisplay, delivered.LaborCostDisplay,
                     delivered.EstimatedCostDisplay, delivered.TotalCollectedAllDisplay,
                     "الضمان", delivered.WarrantySummaryDisplay, "توقيع العميل"
                 })
        {
            check(deliveryText.Contains(expected, StringComparison.Ordinal),
                $"The delivery invoice prints «{expected}»");
        }

        foreach (var (label, view) in new[] { ("intake", intake), ("delivery", delivery) })
        {
            var ink = Descendants(view).OfType<TextBlock>().Select(t => t.Foreground)
                .Concat(Descendants(view).OfType<Border>().Select(b => b.Background).OfType<Brush>())
                .Concat(Descendants(view).OfType<Border>().Select(b => b.BorderBrush).OfType<Brush>());
            check(ink.All(b => Equals(b, Brushes.Black) || Equals(b, Brushes.White)),
                $"The maintenance {label} document is black and white only");
            check(!Descendants(view).OfType<Border>().Any(b => b.CornerRadius.TopLeft > 0),
                $"The maintenance {label} document has square corners");
        }

        // --- View models: copies are validated in Arabic before any dialog ------------------
        var printedLabels = new List<string>();
        var salesVm = new InvoicePrintPreviewViewModel(invoice, printedLabels.Add);
        salesVm.CopiesText = "0";
        salesVm.PrintCommand.Execute(null);
        check(salesVm.IsErrorStatus && salesVm.StatusMessage.Contains("1 إلى 20", StringComparison.Ordinal),
            "A zero copy count is refused in Arabic before the system print dialog opens");
        salesVm.CopiesText = "abc";
        salesVm.PrintCommand.Execute(null);
        check(salesVm.IsErrorStatus, "A non-numeric copy count is refused in Arabic");

        // A successful send is what records the activity — never the mere opening of a preview.
        var copiesSeen = 0;
        ClassicReceipt.InteractivePrinter = (_, job, count) =>
        {
            copiesSeen = count;
            return ReceiptPrintResult.Printed("طابعة الاختبار", count);
        };
        try
        {
            salesVm.CopiesText = "2";
            salesVm.PrintCommand.Execute(null);
        }
        finally
        {
            ClassicReceipt.InteractivePrinter = null;
        }

        check(!salesVm.IsErrorStatus && copiesSeen == 2 && printedLabels.Count == 1
              && salesVm.StatusMessage.Contains(invoice.InvoiceNumber, StringComparison.Ordinal),
            "Printing sends the requested two copies and records one print activity");

        var refused = new InvoicePrintPreviewViewModel(invoice, printedLabels.Add);
        ClassicReceipt.InteractivePrinter = (_, _, _) => ReceiptPrintResult.Failed("تعذّرت الطباعة: الطابعة غير متصلة");
        try
        {
            refused.PrintCommand.Execute(null);
        }
        finally
        {
            ClassicReceipt.InteractivePrinter = null;
        }

        check(refused.IsErrorStatus && refused.StatusMessage.Contains("الطابعة غير متصلة", StringComparison.Ordinal)
              && printedLabels.Count == 1,
            "A printer failure is shown in Arabic and records no print activity");

        // Copy counts are validated before the system dialog opens, so the test never blocks on a printer.
        var receiptVm = new MaintenanceReceiptPreviewViewModel(active);
        receiptVm.CopiesText = "21";
        receiptVm.PrintCommand.Execute(null);
        check(receiptVm.IsErrorStatus && receiptVm.StatusMessage.Contains("1 إلى 20", StringComparison.Ordinal),
            "The maintenance receipt refuses a copy count above 20 before any printer dialog");
        receiptVm.CopiesText = "";
        receiptVm.PrintCommand.Execute(null);
        check(receiptVm.IsErrorStatus, "An empty copy count is refused in Arabic too");

        // The delivery document cannot be selected for an order that is not delivered.
        var guard = new MaintenanceReceiptPreviewViewModel(active);
        guard.SelectDeliveryReceiptModeCommand.Execute(null);
        check(!guard.IsDeliveryReceiptMode, "A device that is still in the workshop cannot print a delivery invoice");
        check(guard.PrintButtonLabel == "طباعة الآن" && guard.PrintHint.Contains(active.OrderNumber, StringComparison.Ordinal),
            "The print button names the document and the order");

        // --- The preview window shows the printable paper itself -------------------------
        // A Window in a headless test never applies its template, so the contract is read from the
        // logical tree instead of the visual tree.
        var windowVm = new InvoicePrintPreviewViewModel(invoice, _ => { });
        var salesWindow = new InvoicePrintPreviewWindow(windowVm);
        check(ReferenceEquals(salesWindow.DataContext, windowVm), "The sales window is bound to its print view model");
        check(salesWindow.Content is { } salesContent
              && LogicalDescendants(salesContent).OfType<ContentControl>()
                  .Any(c => c.GetBindingExpression(ContentControl.ContentProperty)?.ParentBinding.Path.Path
                      == nameof(InvoicePrintPreviewViewModel.PaperPreview)),
            "The sales window shows the printable paper through a ContentControl, not a mock-up card");
        check(LogicalText(salesWindow).Contains(invoice.InvoiceNumber, StringComparison.Ordinal),
            "The sales window header names the invoice being printed");

        var maintenanceVm = new MaintenanceReceiptPreviewViewModel(active);
        var maintenanceWindow = new MaintenanceReceiptPreviewWindow(maintenanceVm);
        check(ReferenceEquals(maintenanceWindow.DataContext, maintenanceVm), "The maintenance window is bound to its print view model");
        check(maintenanceWindow.Content is { } maintenanceContent
              && LogicalDescendants(maintenanceContent).OfType<ContentControl>()
                  .Any(c => c.GetBindingExpression(ContentControl.ContentProperty)?.ParentBinding.Path.Path
                      == nameof(MaintenanceReceiptPreviewViewModel.PaperPreview)),
            "The maintenance window shows the printable paper itself");
        check(LogicalDescendants(maintenanceWindow).OfType<Button>()
                .Any(b => b.GetBindingExpression(System.Windows.Controls.Primitives.ButtonBase.CommandProperty)
                    ?.ParentBinding.Path.Path == nameof(MaintenanceReceiptPreviewViewModel.PrintCommand)),
            "The maintenance window carries the real print button");
    }

    /// <summary>Logical children of a window's content — available before any layout pass.</summary>
    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child)) yield return descendant;
        }
    }

    private static string LogicalText(DependencyObject root) => string.Join(" ",
        LogicalDescendants(root).OfType<TextBlock>().Select(t => t.Text));

    /// <summary>All visible text under a visual, in visual-tree order.</summary>
    private static string TextOf(DependencyObject root) => string.Join(" ",
        Descendants(root).OfType<TextBlock>().Select(t => t.Text));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
