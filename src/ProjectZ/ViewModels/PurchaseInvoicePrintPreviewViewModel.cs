using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Views;

namespace ProjectZ.ViewModels;

/// <summary>
/// Preview and real printing for a purchase invoice. Two papers share one design: the 80 mm thermal
/// receipt (same look as the sales receipt) and an A4 sheet. Every figure comes from the invoice — the
/// supplier cost, the discount, what was actually paid and what is still owed to the supplier.
/// </summary>
public sealed class PurchaseInvoicePrintPreviewViewModel : ObservableObject
{
    private bool isThermalMode = true;
    private string copiesText = "1";
    private string statusMessage = "";
    private bool isErrorStatus;

    public PurchaseInvoiceDesignItem Invoice { get; }

    /// <summary>The document exactly as it will print: 80 mm paper or an A4 sheet.</summary>
    public FrameworkElement PaperPreview => IsThermalMode
        ? PurchaseInvoicePaper.BuildThermal(Invoice)
        : A4Preview();

    public bool IsThermalMode
    {
        get => isThermalMode;
        set
        {
            if (isThermalMode == value) return;
            isThermalMode = value;
            Changed();
            Changed(nameof(IsA4Mode));
            Changed(nameof(CurrentModeLabel));
            Changed(nameof(PaperCaption));
            Changed(nameof(PaperPreview));
            StatusMessage = "";
        }
    }

    public bool IsA4Mode
    {
        get => !isThermalMode;
        set => IsThermalMode = !value;
    }

    public string CurrentModeLabel => IsThermalMode ? "إيصال شراء 80 مم" : "فاتورة شراء A4";

    public string PaperCaption => IsThermalMode
        ? "ورق حراري 80 مم · أبيض وأسود · ارتفاع الصفحة يساوي ارتفاع المحتوى"
        : "ورق A4 · أبيض وأسود · نفس التصميم بمقاس المكتب";

    public string CopiesText
    {
        get => copiesText;
        set
        {
            if (copiesText == value) return;
            copiesText = value;
            Changed();
            StatusMessage = "";
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value) return;
            statusMessage = value;
            Changed();
            Changed(nameof(HasStatusMessage));
        }
    }

    public bool IsErrorStatus
    {
        get => isErrorStatus;
        private set
        {
            if (isErrorStatus == value) return;
            isErrorStatus = value;
            Changed();
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public Action? RequestClose { get; set; }
    public ActionCommand SelectA4ModeCommand { get; }
    public ActionCommand SelectThermalModeCommand { get; }
    public ActionCommand PrintCommand { get; }
    public ActionCommand CloseCommand { get; }

    public PurchaseInvoicePrintPreviewViewModel(PurchaseInvoiceDesignItem invoice, Action<string>? onPrinted = null)
    {
        Invoice = invoice;
        SelectA4ModeCommand = new ActionCommand(() => IsA4Mode = true);
        SelectThermalModeCommand = new ActionCommand(() => IsThermalMode = true);
        PrintCommand = new ActionCommand(() => Print(onPrinted));
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void Print(Action<string>? onPrinted)
    {
        if (!int.TryParse(CopiesText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies)
            || copies is < 1 or > 20)
        {
            SetStatus("أدخل عدد نسخ صحيحًا من 1 إلى 20.", true);
            return;
        }

        var result = IsThermalMode
            ? ClassicReceipt.PrintInteractive(
                () => PurchaseInvoicePaper.BuildThermal(Invoice), $"فاتورة شراء {Invoice.InvoiceNumber}", copies)
            : PrintA4(copies);

        if (result.WasCancelled)
        {
            SetStatus("أُلغيت الطباعة قبل اختيار الطابعة.", false);
            return;
        }

        if (!result.Succeeded)
        {
            SetStatus(result.Message, true);
            return;
        }

        SetStatus($"تم إرسال {CurrentModeLabel} للفاتورة {Invoice.InvoiceNumber} ({copies} نسخة) إلى «{result.PrinterName}».", false);
        onPrinted?.Invoke(CurrentModeLabel);
    }

    private ReceiptPrintResult PrintA4(int copies)
    {
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return ReceiptPrintResult.Cancelled();

            var ticket = dialog.PrintTicket ?? new PrintTicket();
            ticket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket = ticket;

            var width = ClassicReceipt.Mm(210);
            var height = ClassicReceipt.Mm(297);
            if (dialog.PrintQueue is { } queue && queue.GetPrintCapabilities(ticket) is { } capabilities
                && capabilities.PageImageableArea is { ExtentWidth: > 0, ExtentHeight: > 0 } area)
            {
                width = area.ExtentWidth;
                height = area.ExtentHeight;
            }

            var document = PurchaseInvoicePaper.BuildA4Document(Invoice, width, height, copies);
            dialog.PrintDocument(document.DocumentPaginator, $"فاتورة شراء A4 {Invoice.InvoiceNumber}");
            return ReceiptPrintResult.Printed(dialog.PrintQueue?.FullName ?? "الطابعة الافتراضية", copies);
        }
        catch (Exception ex)
        {
            return ReceiptPrintResult.Failed($"تعذّرت طباعة الفاتورة: {ex.Message}");
        }
    }

    /// <summary>The on-screen A4 preview is the same document, shown page by page.</summary>
    private FrameworkElement A4Preview()
    {
        var host = new ContentControl
        {
            Content = new DocumentViewer
            {
                Document = PurchaseInvoicePaper.BuildA4Document(Invoice, ClassicReceipt.Mm(210), ClassicReceipt.Mm(297)),
                HorizontalAlignment = HorizontalAlignment.Center
            },
            Width = ClassicReceipt.Mm(210),
            Height = ClassicReceipt.Mm(280)
        };
        return host;
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
