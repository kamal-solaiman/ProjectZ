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
                     invoice.InvoiceNumber, invoice.IssuedFullDisplay,
                     invoice.SubtotalDisplay, invoice.NetTotalDisplay, invoice.GrossPaidDisplay,
                     invoice.PaymentMethodSummary, "الإجمالي الفرعي", "الصافي المطلوب", "تفاصيل السداد",
                     "الأصناف", "فاتورة مبيعات"
                 })
        {
            check(text.Contains(expected, StringComparison.Ordinal), $"The invoice prints «{expected}»");
        }

        // The owner removed these blocks from the sales receipt on purpose.
        foreach (var removed in new[]
                 {
                     "الكاشير", "بيانات العميل", "فئة السعر", "حالة الفاتورة",
                     "توقيع الكاشير", "توقيع العميل", "لا يُعتمد إلا بختم المحل", "ضريبية مبسطة"
                 })
        {
            check(!text.Contains(removed, StringComparison.Ordinal),
                $"The sales receipt no longer prints «{removed}»");
        }

        // Centred, not left-aligned: every meta and money line sits in the middle of the paper.
        var centredLines = Descendants(paper).OfType<TextBlock>()
            .Where(t => t.TextAlignment == TextAlignment.Center)
            .Select(t => t.Text)
            .ToList();
        foreach (var centred in new[] { "رقم الفاتورة", "التاريخ", "الأصناف", "عدد الأصناف", "الإجمالي الفرعي" })
        {
            check(centredLines.Any(line => line.Contains(centred, StringComparison.Ordinal)),
                $"«{centred}» is centred on the paper");
        }

        check(text.Contains(invoice.Items[0].ProductName, StringComparison.Ordinal),
            "Every sold line prints its product name");
        check(invoice.HasDiscount == text.Contains("الخصم", StringComparison.Ordinal),
            "The discount row appears exactly when the invoice has a discount");
        check(invoice.HasRemaining == text.Contains("المتبقي الآجل", StringComparison.Ordinal),
            "The credit row appears exactly when money is still owed");

        // --- No internal ids on the paper ---------------------------------------------------
        var lineWithIds = InvoiceDesignSeedData.CreateSharedSampleInvoices(
                PosDesignSeedData.CreateSharedSampleCatalog(),
                InventoryDesignSeedData.CreateSharedSampleRegistrations(PosDesignSeedData.CreateSharedSampleCatalog()))
            .SelectMany(row => row.Items)
            .FirstOrDefault(row => row.UnitRegistrationIds.Count > 0);
        if (lineWithIds is null)
        {
            check(false, "The sample data still carries inventory unit ids for the no-ids probe");
        }
        else
        {
            check(lineWithIds.SerialSummary.Contains("ID", StringComparison.Ordinal)
                  || lineWithIds.SerialSummary.Contains("IDs", StringComparison.Ordinal),
                "The internal summary still has ids (that is why the receipt needs its own line)");
            check(!lineWithIds.PrintedSerialSummary.Contains("ID", StringComparison.Ordinal)
                  && !lineWithIds.PrintedSerialSummary.Contains("#", StringComparison.Ordinal),
                "The printed serial line drops every inventory id, keeping only IMEI/SN");

            var idFreePaper = TextOf(SalesInvoicePaper.Build(invoice));
            foreach (var internalId in lineWithIds.UnitRegistrationIds)
            {
                check(!idFreePaper.Contains($"#{internalId}", StringComparison.Ordinal)
                      && !idFreePaper.Contains($"ID: #{internalId}", StringComparison.Ordinal),
                    $"The printed invoice never shows the internal id #{internalId}");
            }

            check(!idFreePaper.Contains("IDs:", StringComparison.Ordinal)
                  && !idFreePaper.Contains("ID:", StringComparison.Ordinal),
                "No «IDs:» label survives anywhere on the sales receipt");
        }

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

        // --- POS: the button saves, and only a committed sale opens the receipt --------------
        check(typeof(PosViewModel).GetProperty("SaveInvoiceCommand") is not null
              && typeof(PosViewModel).GetProperty("CompleteInvoicePreviewCommand") is null,
            "The checkout button is «حفظ الفاتورة», not a preview command");

        var posCustomers = new System.Collections.ObjectModel.ObservableCollection<PosCustomerDesignItem>(
            PosDesignSeedData.CreateSampleCustomers());

        // A refused save (nothing in the cart) must never open a receipt window.
        var posRefused = new PosViewModel("اختبار", catalog, _ => "INV-2026-9001", posCustomers, useDesignSamples: false);
        var posRefusedWindows = 0;
        posRefused.OpenPrintWindowRequested = _ => posRefusedWindows++;
        posRefused.SaveInvoiceCommand.Execute(null);
        check(posRefused.IsErrorStatus && posRefusedWindows == 0,
            "An empty cart is refused and no receipt window opens");

        // The saved snapshot the register hands back after COMMIT.
        var savedSnapshot = new SalesInvoiceDesignItem
        {
            InvoiceNumber = "INV-2026-9001",
            IssuedAt = DateTime.Today.AddHours(12),
            CashierName = "اختبار",
            Status = SalesInvoiceStatus.Completed,
            CashPaid = 1465m
        };

        // The design samples start on fully-settled invoices, exactly like a real fast checkout.
        var pos = new PosViewModel("اختبار", catalog, _ => savedSnapshot.InvoiceNumber, posCustomers,
            useDesignSamples: true,
            savedSales: number => savedSnapshot.InvoiceNumber == number ? savedSnapshot : null);
        check(!pos.ActiveInvoice.IsEmpty && !pos.ActiveInvoice.HasRemaining,
            "The demo checkout carries a settled cart, so the save reaches the commit callback");
        var openedFor = new List<SalesInvoiceDesignItem>();
        pos.OpenPrintWindowRequested = invoice => openedFor.Add(invoice);
        pos.SaveInvoiceCommand.Execute(null);
        check(openedFor.Count == 1 && openedFor[0].InvoiceNumber == savedSnapshot.InvoiceNumber
              && openedFor[0].Status == SalesInvoiceStatus.Completed,
            "A committed sale opens the receipt window once with the saved invoice");

        // A refused or failed save keeps the cart and leaves the window closed.
        var failing = new PosViewModel("اختبار", catalog, _ => throw new InvalidOperationException("رفض الخادم"),
            posCustomers, useDesignSamples: true, savedSales: _ => savedSnapshot);
        var failingWindows = 0;
        failing.OpenPrintWindowRequested = _ => failingWindows++;
        var failingInvoice = failing.ActiveInvoice;
        var openBeforeSave = failing.Invoices.Count;
        failingInvoice.SetSplitAmounts(cash: failingInvoice.NetTotal, vodafoneCash: 0m, instaPay: 0m);
        failing.SaveInvoiceCommand.Execute(null);
        check(failing.IsErrorStatus && failingWindows == 0 && failing.Invoices.Count == openBeforeSave
              && ReferenceEquals(failing.ActiveInvoice, failingInvoice),
            "A failed save never opens the receipt window and keeps the invoice open for a retry");

        // --- The two maintenance documents -------------------------------------------------
        var orders = MaintenanceDesignSeedData.CreateSharedSampleOrders(PosDesignSeedData.CreateSampleCustomers());
        check(orders.Count > 0, "The sample workshop supplies an order to print");
        var delivered = orders.FirstOrDefault(o => o.IsDelivered) ?? orders[0];
        var active = orders.FirstOrDefault(o => !o.IsDelivered) ?? orders[0];

        var intake = MaintenanceReceiptPaper.BuildIntake(active);
        var intakeText = TextOf(intake);
        foreach (var expected in new[]
                 {
                     "إيصال استلام جهاز", active.OrderNumber, active.ReceivedFullDisplay, active.CustomerName,
                     active.CustomerPhoneDisplay, active.DeviceFullTitle, active.ReportedFault,
                     active.EstimatedCostDisplay, "شروط الاستلام"
                 })
        {
            check(intakeText.Contains(expected, StringComparison.Ordinal), $"The intake receipt prints «{expected}»");
        }

        check(intakeText.Contains(active.TotalDepositPaidDisplay, StringComparison.Ordinal),
            "The intake receipt prints what the customer actually paid as a deposit");

        // The owner trimmed the intake receipt down to what he needs at the counter.
        foreach (var removed in new[]
                 {
                     "المستلم", "الفني المسؤول", "الأولوية", "النوع", "السيريال / IMEI", "كود الجهاز",
                     "الحالة الظاهرية", "الملحقات", "التسليم المتوقع", "توقيع العميل", "ختم المحل",
                     "لا يُعتمد إلا بختم المحل"
                 })
        {
            check(!intakeText.Contains(removed, StringComparison.Ordinal),
                $"The intake receipt no longer prints «{removed}»");
        }

        // Everything on the intake receipt is centred, exactly like the sales receipt.
        var intakeBlocks = Descendants(intake).OfType<TextBlock>()
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .ToList();
        check(intakeBlocks.Count > 0 && intakeBlocks.All(t => t.TextAlignment == TextAlignment.Center),
            "Every line of the intake receipt is centred");

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

        // --- Purchase invoice: the same classic paper ------------------------------------
        var purchaseInvoices = PurchaseDesignSeedData.CreateSharedSamplePurchaseInvoices(
            catalog,
            InventoryDesignSeedData.CreateSharedSampleRegistrations(catalog),
            PurchaseDesignSeedData.CreateSharedSampleSuppliers());
        check(purchaseInvoices.Count > 0, "The sample register supplies a purchase invoice to print");
        var purchase = purchaseInvoices[0];

        var purchasePaper = PurchaseInvoicePaper.BuildThermal(purchase);
        purchasePaper.Measure(new Size(ClassicReceipt.Mm(80), double.PositiveInfinity));
        var purchaseText = TextOf(purchasePaper);
        foreach (var expected in new[]
                 {
                     "فاتورة شراء", purchase.InvoiceNumber, purchase.IssuedFullDisplay,
                     purchase.SupplierInvoiceNumberDisplay, purchase.SupplierName, purchase.SupplierPhoneDisplay,
                     purchase.Items[0].ProductName, purchase.SubtotalDisplay, purchase.NetTotalDisplay,
                     purchase.TotalPaidDisplay, purchase.PaymentMethodSummary, purchase.StatusTitle,
                     "الأصناف", "تفاصيل السداد", "المورد"
                 })
        {
            check(purchaseText.Contains(expected, StringComparison.Ordinal), $"The purchase invoice prints «{expected}»");
        }

        // The owner chose: supplier name and phone only — the company and address fields stay off the paper.
        if (!string.IsNullOrWhiteSpace(purchase.SupplierCompany))
            check(!purchaseText.Contains(purchase.SupplierCompany, StringComparison.Ordinal),
                "The purchase invoice hides the supplier company field");
        if (!string.IsNullOrWhiteSpace(purchase.SupplierAddress))
            check(!purchaseText.Contains(purchase.SupplierAddress, StringComparison.Ordinal),
                "The purchase invoice hides the supplier address field");
        if (!string.IsNullOrWhiteSpace(purchase.SupplierInvoiceNumber))
            check(purchaseText.Contains(purchase.SupplierInvoiceNumber, StringComparison.Ordinal),
                "The purchase invoice keeps the supplier's own invoice number");
        foreach (var removed in new[]
                 {
                     "توقيع", "ختم", "لا يُعتمد إلا بختم المحل", "مستند مطبوع من نظام إدارة المحل", "ID مخزون"
                 })
        {
            check(!purchaseText.Contains(removed, StringComparison.Ordinal),
                $"The purchase invoice no longer prints «{removed}»");
        }

        // Pure black on white, square corners, everything centred.
        var purchaseInk = Descendants(purchasePaper).OfType<TextBlock>().Select(t => t.Foreground)
            .Concat(Descendants(purchasePaper).OfType<Border>().Select(b => b.Background).OfType<Brush>())
            .Concat(Descendants(purchasePaper).OfType<Border>().Select(b => b.BorderBrush).OfType<Brush>());
        check(purchaseInk.All(b => Equals(b, Brushes.Black) || Equals(b, Brushes.White)),
            "The purchase invoice is black and white only");
        check(!Descendants(purchasePaper).OfType<Border>().Any(b => b.CornerRadius.TopLeft > 0),
            "The purchase invoice has square corners");
        // The two table headers/rows are columns by nature; everything outside the table is centred.
        var purchaseLines = Descendants(purchasePaper).OfType<TextBlock>()
            .Where(t => !string.IsNullOrWhiteSpace(t.Text) && !IsInsideTable(t)).ToList();
        check(purchaseLines.Count > 0
              && purchaseLines.All(t => t.TextAlignment == TextAlignment.Center),
            "Every non-table line of the purchase invoice is centred");
        check(Descendants(purchasePaper).OfType<TextBlock>()
                .Any(t => t.Text == purchase.Items[0].QuantityDisplay && !IsInsideTable(t) == false),
            "The item table still prints its quantity column");

        // The thermal page is exactly 80 mm wide.
        var purchaseDocument = ClassicReceipt.BuildDocument(
            () => PurchaseInvoicePaper.BuildThermal(purchase), ClassicReceipt.Mm(72), copies: 2);
        check(purchaseDocument.Pages.Count == 2
              && purchaseDocument.Pages[0].Child is FixedPage page80
              && Math.Abs(page80.Width - ClassicReceipt.Mm(80)) < 0.001,
            "The purchase receipt prints two independent 80 mm pages");

        // The A4 mode exists and carries the same numbers on A4 paper.
        var a4Document = PurchaseInvoicePaper.BuildA4Document(purchase, ClassicReceipt.Mm(190), ClassicReceipt.Mm(277), copies: 1);
        check(a4Document.Pages.Count == 1 && a4Document.Pages[0].Child is FixedPage a4Page
              && Math.Abs(a4Page.Width - ClassicReceipt.Mm(190)) < 0.001,
            "The A4 purchase invoice fills the printer's printable area");

        var purchaseVm = new PurchaseInvoicePrintPreviewViewModel(purchase, _ => { });
        check(purchaseVm.IsThermalMode && purchaseVm.CurrentModeLabel.Contains("80", StringComparison.Ordinal),
            "The purchase window opens on the 80 mm receipt");
        purchaseVm.SelectA4ModeCommand.Execute(null);
        check(purchaseVm.IsA4Mode && purchaseVm.CurrentModeLabel.Contains("A4", StringComparison.Ordinal)
              && purchaseVm.PaperPreview is not null,
            "Switching to A4 swaps the preview to the A4 document");
        purchaseVm.SelectThermalModeCommand.Execute(null);
        purchaseVm.CopiesText = "0";
        purchaseVm.PrintCommand.Execute(null);
        check(purchaseVm.IsErrorStatus && purchaseVm.StatusMessage.Contains("1 إلى 20", StringComparison.Ordinal),
            "A zero copy count is refused before any printer dialog");

        var purchasePrinted = new List<string>();
        var purchasePrintVm = new PurchaseInvoicePrintPreviewViewModel(purchase, purchasePrinted.Add);
        ClassicReceipt.InteractivePrinter = (_, _, count) => ReceiptPrintResult.Printed("طابعة الاختبار", count);
        try
        {
            purchasePrintVm.CopiesText = "2";
            purchasePrintVm.PrintCommand.Execute(null);
        }
        finally
        {
            ClassicReceipt.InteractivePrinter = null;
        }
        check(!purchasePrintVm.IsErrorStatus && purchasePrinted.Count == 1
              && purchasePrintVm.StatusMessage.Contains(purchase.InvoiceNumber, StringComparison.Ordinal),
            "A successful purchase print is reported once, and only then is it recorded");

        var purchaseRefused = new PurchaseInvoicePrintPreviewViewModel(purchase, purchasePrinted.Add);
        ClassicReceipt.InteractivePrinter = (_, _, _) => ReceiptPrintResult.Failed("تعذّرت الطباعة: الطابعة غير متصلة");
        try
        {
            purchaseRefused.PrintCommand.Execute(null);
        }
        finally
        {
            ClassicReceipt.InteractivePrinter = null;
        }
        check(purchaseRefused.IsErrorStatus && purchasePrinted.Count == 1,
            "A failed purchase print shows the printer error and records nothing");

        // --- The preview window shows the printable paper itself -------------------------
        // A Window in a headless test never applies its template, so the contract is read from the
        // logical tree instead of the visual tree.
        var windowVm = new InvoicePrintPreviewViewModel(invoice, _ => { });
        var salesWindow = new InvoicePrintPreviewWindow(windowVm);
        check(ReferenceEquals(salesWindow.DataContext, windowVm), "The sales window is bound to its print view model");
        var salesContent = salesWindow.Content as DependencyObject;
        check(salesContent is not null
              && LogicalDescendants(salesContent).OfType<ContentControl>()
                  .Any(c => c.GetBindingExpression(ContentControl.ContentProperty)?.ParentBinding.Path.Path
                      == nameof(InvoicePrintPreviewViewModel.PaperPreview)),
            "The sales window shows the printable paper through a ContentControl, not a mock-up card");
        check(salesContent is not null
              && LogicalDescendants(salesContent).OfType<TextBlock>()
                  .Any(t => t.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path
                      == nameof(InvoicePrintPreviewViewModel.InvoiceNumberDisplay)),
            "The sales window header names the invoice being printed");

        var maintenanceVm = new MaintenanceReceiptPreviewViewModel(active);
        var maintenanceWindow = new MaintenanceReceiptPreviewWindow(maintenanceVm);
        check(ReferenceEquals(maintenanceWindow.DataContext, maintenanceVm), "The maintenance window is bound to its print view model");
        var maintenanceContent = maintenanceWindow.Content as DependencyObject;
        check(maintenanceContent is not null
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


    /// <summary>True when the text block is a cell of the ruled item table (header or row).</summary>
    private static bool IsInsideTable(FrameworkElement text)
    {
        var parent = VisualTreeHelper.GetParent(text);
        while (parent is not null)
        {
            if (parent is StackPanel panel && panel.Children.OfType<FrameworkElement>()
                    .Any(child => child is Border { Height: 0.7, Background: not null }))
                return true;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }

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
