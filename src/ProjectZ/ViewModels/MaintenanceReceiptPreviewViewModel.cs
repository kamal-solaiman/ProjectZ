using ProjectZ.Helpers;
using ProjectZ.Models;

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

    public MaintenanceOrderDesignItem Order { get; }

    public bool IsCustomerReceiptMode => mode == MaintenanceReceiptPrintMode.CustomerIntakeReceipt;
    public bool IsDeviceStickerMode => mode == MaintenanceReceiptPrintMode.DeviceSticker;
    public bool IsDeliveryReceiptMode => mode == MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt;

    public string CurrentModeLabel => mode switch
    {
        MaintenanceReceiptPrintMode.DeviceSticker => "ملصق تذكرة الجهاز للفني",
        MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt => "إيصال التسليم والضمان للعميل (80 مم)",
        _ => "إيصال استلام الصيانة للعميل (80 مم)"
    };

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
        PrintCommand = new ActionCommand(() =>
        {
            try
            {
                onPrintPreview?.Invoke(mode, CurrentModeLabel);
                StatusMessage = $"تمت معاينة {CurrentModeLabel} للأمر {Order.OrderNumber} (لا توجد طابعة موصولة).";
            }
            catch (Exception ex) { StatusMessage = $"تعذّر تأكيد المعاينة: {ex.Message}"; }
        });
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void SetMode(MaintenanceReceiptPrintMode nextMode)
    {
        if (mode == nextMode || (nextMode == MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt && !Order.IsDelivered)) return;
        mode = nextMode;
        Changed(nameof(IsCustomerReceiptMode));
        Changed(nameof(IsDeviceStickerMode));
        Changed(nameof(IsDeliveryReceiptMode));
        Changed(nameof(CurrentModeLabel));
    }
}
