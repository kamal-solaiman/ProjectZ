using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.Views;

namespace ProjectZ.ViewModels;

public enum MaintenanceReceiptPrintMode
{
    CustomerIntakeReceipt = 0,
    DeviceSticker = 1,
    DeliveryAndWarrantyReceipt = 2
}

public sealed class MaintenanceReceiptPreviewViewModel : ObservableObject
{
    private MaintenanceReceiptPrintMode mode;
    private string statusMessage = "";
    private bool isErrorStatus;

    public MaintenanceOrderDesignItem Order { get; }

    public bool IsCustomerReceiptMode => mode == MaintenanceReceiptPrintMode.CustomerIntakeReceipt;
    public bool IsDeviceStickerMode => mode == MaintenanceReceiptPrintMode.DeviceSticker;
    public bool IsDeliveryReceiptMode => mode == MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt;

    public string CurrentModeLabel => mode switch
    {
        MaintenanceReceiptPrintMode.DeviceSticker => "ملصق أمر الصيانة",
        MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt => "إيصال التسليم والضمان للعميل (80 مم)",
        _ => "إيصال استلام الصيانة للعميل (80 مم)"
    };

    /// <summary>Button caption: the sticker is printed for real, the two receipts are still previews.</summary>
    public string PrintButtonLabel => IsDeviceStickerMode ? "طباعة الملصق الآن" : "معاينة الطباعة";

    public string StickerHint => $"يُطبع على ملصق 50 × 25 مم: اسم المحل، اسم العميل، كود الأمر {Order.OrderNumber}.";

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
    public ActionCommand SelectCustomerReceiptModeCommand { get; }
    public ActionCommand SelectDeviceStickerModeCommand { get; }
    public ActionCommand SelectDeliveryReceiptModeCommand { get; }
    public ActionCommand PrintCommand { get; }
    public ActionCommand CloseCommand { get; }

    public MaintenanceReceiptPreviewViewModel(
        MaintenanceOrderDesignItem order,
        Action<MaintenanceReceiptPrintMode, string>? onPrintPreview = null,
        MaintenanceReceiptPrintMode initialMode = MaintenanceReceiptPrintMode.CustomerIntakeReceipt)
    {
        Order = order;
        mode = initialMode == MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt && !Order.IsDelivered
            ? MaintenanceReceiptPrintMode.CustomerIntakeReceipt : initialMode;

        SelectCustomerReceiptModeCommand = new ActionCommand(() => SetMode(MaintenanceReceiptPrintMode.CustomerIntakeReceipt));
        SelectDeviceStickerModeCommand = new ActionCommand(() => SetMode(MaintenanceReceiptPrintMode.DeviceSticker));
        SelectDeliveryReceiptModeCommand = new ActionCommand(() => SetMode(MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt), () => Order.IsDelivered);
        PrintCommand = new ActionCommand(() => Print(onPrintPreview));
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void Print(Action<MaintenanceReceiptPrintMode, string>? onPrintPreview)
    {
        if (!IsDeviceStickerMode)
        {
            // The 80 mm receipts keep the previous preview-only behaviour in this delivery.
            try
            {
                onPrintPreview?.Invoke(mode, CurrentModeLabel);
                SetStatus($"تمت معاينة {CurrentModeLabel} للأمر {Order.OrderNumber} (طباعة الإيصالات لم تُفعّل بعد).", false);
            }
            catch (Exception ex) { SetStatus($"تعذّر تأكيد المعاينة: {ex.Message}", true); }
            return;
        }

        var sticker = BarcodeStickerPrinting.ForMaintenanceOrder(Order, StoreIdentity.Current.Name, out var error);
        if (sticker is null)
        {
            SetStatus(error!, true);
            return;
        }

        var result = BarcodeStickerPrinting.Print(sticker, 1);
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

        SetStatus($"طُبع ملصق الأمر {Order.OrderNumber} للعميل «{Order.CustomerName}» على «{result.PrinterName}».", false);
        try { onPrintPreview?.Invoke(MaintenanceReceiptPrintMode.DeviceSticker, CurrentModeLabel); }
        catch (Exception ex) { SetStatus($"تمت الطباعة لكن تعذّر تسجيلها في سجل الأمر: {ex.Message}", true); }
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }

    private void SetMode(MaintenanceReceiptPrintMode nextMode)
    {
        if (mode == nextMode || (nextMode == MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt && !Order.IsDelivered)) return;
        mode = nextMode;
        Changed(nameof(IsCustomerReceiptMode));
        Changed(nameof(IsDeviceStickerMode));
        Changed(nameof(IsDeliveryReceiptMode));
        Changed(nameof(CurrentModeLabel));
        Changed(nameof(PrintButtonLabel));
        StatusMessage = "";
        IsErrorStatus = false;
    }
}
