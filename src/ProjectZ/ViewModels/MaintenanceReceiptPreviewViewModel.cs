using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceReceiptPreviewViewModel : ObservableObject
{
    private bool isStickerMode;
    private string statusMessage = "";

    public MaintenanceOrderDesignItem Order { get; }

    public bool IsCustomerReceiptMode
    {
        get => !isStickerMode;
        set
        {
            if (isStickerMode == !value) return;
            isStickerMode = !value;
            Changed();
            Changed(nameof(IsDeviceStickerMode));
            Changed(nameof(CurrentModeLabel));
        }
    }

    public bool IsDeviceStickerMode
    {
        get => isStickerMode;
        set
        {
            if (isStickerMode == value) return;
            isStickerMode = value;
            Changed();
            Changed(nameof(IsCustomerReceiptMode));
            Changed(nameof(CurrentModeLabel));
        }
    }

    public string CurrentModeLabel => IsDeviceStickerMode
        ? "ملصق تذكرة الجهاز للفني"
        : "إيصال استلام الصيانة للعميل (80 مم)";

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
    public ActionCommand PrintCommand { get; }
    public ActionCommand CloseCommand { get; }

    public MaintenanceReceiptPreviewViewModel(
        MaintenanceOrderDesignItem order,
        Action<string>? onPrintPreview = null)
    {
        Order = order;
        SelectCustomerReceiptModeCommand = new ActionCommand(() => IsCustomerReceiptMode = true);
        SelectDeviceStickerModeCommand = new ActionCommand(() => IsDeviceStickerMode = true);
        PrintCommand = new ActionCommand(() =>
        {
            StatusMessage = $"مرحلة التصميم (بدون طابعة Backend): تمت معاينة طباعة أمر الصيانة {Order.OrderNumber} بصيغة «{CurrentModeLabel}».";
            onPrintPreview?.Invoke(CurrentModeLabel);
        });
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }
}
