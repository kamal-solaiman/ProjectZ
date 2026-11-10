using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PurchaseInvoicePrintPreviewViewModel : ObservableObject
{
    private bool isThermalMode;
    private string statusMessage = "";

    public PurchaseInvoiceDesignItem Invoice { get; }

    public bool IsA4Mode
    {
        get => !isThermalMode;
        set
        {
            if (isThermalMode == !value) return;
            isThermalMode = !value;
            Changed();
            Changed(nameof(IsThermalMode));
            Changed(nameof(CurrentModeLabel));
        }
    }

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
        }
    }

    public string CurrentModeLabel => IsThermalMode ? "إيصال توريد 80 مم" : "فاتورة مشتريات A4 رسمية";

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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public Action? RequestClose { get; set; }
    public ActionCommand SelectA4ModeCommand { get; }
    public ActionCommand SelectThermalModeCommand { get; }
    public ActionCommand PrintCommand { get; }
    public ActionCommand CloseCommand { get; }

    public PurchaseInvoicePrintPreviewViewModel(PurchaseInvoiceDesignItem invoice, Action<string>? onPrintPreview = null)
    {
        Invoice = invoice;
        SelectA4ModeCommand = new ActionCommand(() => IsA4Mode = true);
        SelectThermalModeCommand = new ActionCommand(() => IsThermalMode = true);
        PrintCommand = new ActionCommand(() =>
        {
            var msg = $"مرحلة التصميم (بدون طابعة Backend): تمت معاينة طباعة فاتورة المشتريات {Invoice.InvoiceNumber} بصيغة «{CurrentModeLabel}».";
            StatusMessage = msg;
            onPrintPreview?.Invoke(CurrentModeLabel);
        });
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }
}
