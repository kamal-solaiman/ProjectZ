using ProjectZ.Data;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

// One commit/refresh boundary for all four delivery entry points. A dialog returns a request,
// never a paid or delivered ticket. A reload can change the selection (or even fail after commit).
internal static class MaintenancePosting
{
    internal static bool Save(IMaintenanceSettlement? repository, MaintenanceOrderDesignItem shown, Func<byte[]> post,
        Action<MaintenanceOrderDesignItem> accept, Action<string, bool> status, string success)
    {
        if (repository is null)
        {
            status("هذه شاشة عرض فقط. يلزم اتصال بقاعدة البيانات لحفظ إجراء الصيانة.", true);
            return false;
        }

        byte[] posted;
        try { posted = post(); }
        catch (Exception ex)
        {
            status($"تعذّر تأكيد الإجراء على {shown.OrderNumber}: {ex.Message} حدّث الأمر قبل المحاولة مجددًا.", true);
            return false;
        }

        try
        {
            var saved = repository.LoadTicket(shown.Id);
            if (saved.Id != shown.Id || saved.DatabaseRowVersion is null || !posted.SequenceEqual(saved.DatabaseRowVersion))
                throw new MaintenanceDataException("تغيّرت نسخة الأمر مجددًا.");
            accept(saved);
        }
        catch (Exception ex)
        {
            status($"اعتُمد إجراء {shown.OrderNumber} لكن تعذّر عرض نسخته: {ex.Message} حدّث السجل ولا تكرر العملية.", true);
            return false;
        }
        status(success, false);
        return true;
    }

    internal static void Deliver(MaintenanceDeliveryViewModel dialog, IMaintenanceSettlement? repository,
        Func<MaintenanceDeliveryViewModel, bool?>? show, Action<MaintenanceOrderDesignItem> accept,
        Action<MaintenanceReceiptPreviewViewModel>? preview, string actor, Action<string, bool> status)
    {
        if (show?.Invoke(dialog) != true || !dialog.IsConfirmed || dialog.Request is not { } request) return;
        MaintenanceOrderDesignItem? receipt = null;
        if (!Save(repository, dialog.Order, () => repository!.Deliver(request), saved =>
            {
                if (!saved.IsDelivered) throw new MaintenanceDataException("لم تُقرأ حالة التسليم.");
                accept(saved);
                receipt = saved;
            }, status, $"تم تسليم الجهاز وحفظ التسوية على الأمر {dialog.Order.OrderNumber}.")) return;

        if (dialog.ShouldOpenPrintAfterClose && receipt is not null)
            Preview(receipt, repository, actor, preview, status, MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt);
    }

    internal static void Preview(MaintenanceOrderDesignItem order, IMaintenanceSettlement? repository, string actor,
        Action<MaintenanceReceiptPreviewViewModel>? preview, Action<string, bool> status, MaintenanceReceiptPrintMode? mode = null)
    {
        if (repository is not null)
        {
            try
            {
                var latest = repository.LoadTicket(order.Id);
                if (order.DatabaseRowVersion is null || latest.DatabaseRowVersion is null || !order.DatabaseRowVersion.SequenceEqual(latest.DatabaseRowVersion))
                    throw new MaintenanceDataException("تغيّر الأمر. افتحه مجددًا من السجل.");
                order = latest;
            }
            catch (Exception ex) { status($"تعذّر فتح مستند مطابق: {ex.Message}", true); return; }
        }

        var key = MaintenanceTicketKey.Open(order);
        preview?.Invoke(new MaintenanceReceiptPreviewViewModel(order, (printMode, label) =>
        {
            // Called only after the receipt actually reached the printer, so the log never claims a
            // print that the user cancelled or that the printer refused.
            try
            {
                var at = repository?.AppendMaintenanceActivity(key, (byte)printMode) ?? DateTime.Now;
                order.ActivityLog.Add(new MaintenanceActivityEntry
                {
                    Timestamp = at,
                    ActorName = actor,
                    ActionTitle = "طباعة مستند صيانة",
                    Details = $"طُبع {label} للأمر {order.OrderNumber}."
                });
                status($"تمت طباعة {label} للأمر {order.OrderNumber}.", false);
            }
            catch (Exception ex)
            {
                status($"تعذّر تسجيل طباعة {label}: {ex.Message}", true);
                throw;
            }
        }, mode ?? (order.IsDelivered ? MaintenanceReceiptPrintMode.DeliveryAndWarrantyReceipt : MaintenanceReceiptPrintMode.CustomerIntakeReceipt)));
    }
}
