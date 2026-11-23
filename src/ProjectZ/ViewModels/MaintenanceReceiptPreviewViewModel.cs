using System.Globalization;
using System.Windows;
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

/// <summary>
/// Print preview for the three maintenance documents. The intake receipt, the delivery/warranty
/// invoice and the 50 × 25 mm sticker are all printed for real on black-and-white paper; nothing here
/// writes to SQL by itself — the caller records the activity after a successful print.
/// </summary>
public sealed class MaintenanceReceiptPreviewViewModel : ObservableObject
{
    private MaintenanceReceiptPrintMode mode;
    private string copiesText = "1";
    private string statusMessage = "";
    private bool isErrorStatus;

    public MaintenanceOrderDesignItem Order { get; }

    public bool IsCustomerReceiptMode => mode == MaintenanceReceiptPrintMode.CustomerIntakeReceipt;
    public bool IsDeviceStickerMode => mode == MaintenanceReceiptPrintMode.DeviceSticker;
    public bool IsDeliveryReceiptMode => mode == MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt;

    public string CurrentModeLabel => mode switch
    {
        MaintenanceReceiptPrintMode.DeviceSticker => "ملصق أمر الصيانة (50 × 25 مم)",
        MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt => "فاتورة تسليم وضمان (80 مم)",
        _ => "إيصال استلام (80 مم)"
    };

    /// <summary>Every mode now prints for real, so the button always says so.</summary>
    public string PrintButtonLabel => "طباعة الآن";

    /// <summary>What leaves the printer in the current mode, quoted under the button.</summary>
    public string PrintHint => IsDeviceStickerMode
        ? $"يُطبع على ملصق 50 × 25 مم: اسم المحل، اسم العميل، كود الأمر {Order.OrderNumber}."
        : $"يُطبع على ورق حراري 80 مم بالأبيض والأسود: {CurrentModeLabel} — الأمر {Order.OrderNumber}.";

    /// <summary>The classic paper for the selected mode; the sticker builds its own 50 × 25 mm label.</summary>
    public FrameworkElement PaperPreview => mode switch
    {
        MaintenanceReceiptPrintMode.DeviceSticker => StickerPreview(),
        MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt => MaintenanceReceiptPaper.BuildDelivery(Order),
        _ => MaintenanceReceiptPaper.BuildIntake(Order)
    };

    public bool UsesThermalRoll => !IsDeviceStickerMode;

    public string PaperCaption => IsDeviceStickerMode
        ? "ملصق 50 × 25 مم · اسم المحل ثم اسم العميل ثم كود الأمر"
        : "ورق حراري 80 مم · أبيض وأسود · يحتفظ ارتفاع الصفحة بآخر سطر ليقطعها الجهاز عنده";

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
            ? MaintenanceReceiptPrintMode.CustomerIntakeReceipt
            : initialMode;

        SelectCustomerReceiptModeCommand = new ActionCommand(() => SetMode(MaintenanceReceiptPrintMode.CustomerIntakeReceipt));
        SelectDeviceStickerModeCommand = new ActionCommand(() => SetMode(MaintenanceReceiptPrintMode.DeviceSticker));
        SelectDeliveryReceiptModeCommand = new ActionCommand(() => SetMode(MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt), () => Order.IsDelivered);
        PrintCommand = new ActionCommand(() => Print(onPrintPreview));
        CloseCommand = new ActionCommand(() => RequestClose?.Invoke());
    }

    private void Print(Action<MaintenanceReceiptPrintMode, string>? onPrintPreview)
    {
        if (!int.TryParse(CopiesText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies)
            || copies is < 1 or > 20)
        {
            SetStatus("أدخل عدد نسخ صحيحًا من 1 إلى 20.", true);
            return;
        }

        // The sticker prints on its own 50 × 25 mm paper; both receipts share the 80 mm roll.
        if (IsDeviceStickerMode)
        {
            var sticker = BarcodeStickerPrinting.ForMaintenanceOrder(Order, StoreIdentity.Current.Name, out var stickerError);
            if (sticker is null)
            {
                SetStatus(stickerError ?? "تعذّر تجهيز ملصق أمر الصيانة.", true);
                return;
            }

            var stickerResult = BarcodeStickerPrinting.Print(sticker, copies);
            if (stickerResult.WasCancelled)
            {
                SetStatus("أُلغيت الطباعة قبل اختيار الطابعة.", false);
                return;
            }

            if (!stickerResult.Succeeded)
            {
                SetStatus(stickerResult.Message, true);
                return;
            }

            SetStatus($"طُبع {copies} ملصق للأمر {Order.OrderNumber} على «{stickerResult.PrinterName}».", false);
            RecordActivity(onPrintPreview, MaintenanceReceiptPrintMode.DeviceSticker);
            return;
        }

        var delivery = IsDeliveryReceiptMode;
        var result = ClassicReceipt.PrintInteractive(
            () => delivery ? MaintenanceReceiptPaper.BuildDelivery(Order) : MaintenanceReceiptPaper.BuildIntake(Order),
            $"{CurrentModeLabel} — {Order.OrderNumber}", copies);
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

        SetStatus($"تم إرسال {CurrentModeLabel} للأمر {Order.OrderNumber} ({copies} نسخة) إلى «{result.PrinterName}».", false);
        RecordActivity(onPrintPreview, mode);
    }

    private void RecordActivity(Action<MaintenanceReceiptPrintMode, string>? onPrintPreview, MaintenanceReceiptPrintMode printed)
    {
        try { onPrintPreview?.Invoke(printed, CurrentModeLabel); }
        catch (Exception ex) { SetStatus($"تمت الطباعة لكن تعذّر تسجيلها في سجل الأمر: {ex.Message}", true); }
    }

    private FrameworkElement StickerPreview()
    {
        var sticker = BarcodeStickerPrinting.ForMaintenanceOrder(Order, StoreIdentity.Current.Name, out _)
            ?? new BarcodeSticker(StickerKind.MaintenanceOrder, StoreIdentity.Current.Name, Order.CustomerName, Order.OrderNumber);
        return BarcodeStickerPrinting.BuildPreview(sticker);
    }

    private void SetStatus(string? message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message ?? "";
    }

    private void SetMode(MaintenanceReceiptPrintMode nextMode)
    {
        if (mode == nextMode || (nextMode == MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt && !Order.IsDelivered)) return;
        mode = nextMode;
        Changed(nameof(IsCustomerReceiptMode));
        Changed(nameof(IsDeviceStickerMode));
        Changed(nameof(IsDeliveryReceiptMode));
        Changed(nameof(CurrentModeLabel));
        Changed(nameof(PaperPreview));
        Changed(nameof(UsesThermalRoll));
        Changed(nameof(PaperCaption));
        Changed(nameof(PrintButtonLabel));
        Changed(nameof(PrintHint));
        StatusMessage = "";
        IsErrorStatus = false;
    }
}
