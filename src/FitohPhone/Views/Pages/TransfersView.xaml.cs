using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class TransfersView : UserControl
{
    private readonly TransfersViewModel _vm;

    public TransfersView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new TransfersViewModel(app.TransferRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب الخطوط والتحويلات من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);

            // تعبئة ComboBox الخطوط المتاحة
            SourceLineCombo.Items.Clear();
            foreach (var line in _vm.Lines.Where(l => l.IsActive))
            {
                SourceLineCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{line.PhoneNumber} — متاح: {line.CurrentBalance:N0} ج (باقي يومي {line.DailyLimit - line.DailyUsed:N0})",
                    Tag = line.Id
                });
            }
            if (SourceLineCombo.Items.Count > 0)
                SourceLineCombo.SelectedIndex = 0;
        };
    }

    private void OpenTransferLine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferLineWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            // إعادة تحميل الخطوط بعد إضافة خط جديد
            if (_vm.LoadCommand.CanExecute(null))
                _ = _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void EditTransferLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int lineId })
        {
            var line = _vm.Lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null) return;

            var dialog = new TransferLineWindow(line) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                if (_vm.LoadCommand.CanExecute(null))
                    _ = _vm.LoadCommand.ExecuteAsync(null);
            }
        }
    }

    private void TransferType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // يمكن تخصيص الحقول حسب اتجاه التحويل لاحقًا
    }

    private void SourceLine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // يمكن عرض رصيد الخط المحدد لاحقًا
    }

    private void ExecuteTransfer_Click(object sender, RoutedEventArgs e)
    {
        // التحقق من صحة البيانات
        if (SourceLineCombo.SelectedItem is not ComboBoxItem selectedLine ||
            string.IsNullOrWhiteSpace(CustomerPhoneBox.Text) ||
            string.IsNullOrWhiteSpace(AmountBox.Text))
        {
            MessageBox.Show("يرجى تعبئة جميع الحقول الإجبارية.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var lineId = (int)selectedLine.Tag!;
        var line = _vm.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return;

        if (!decimal.TryParse(AmountBox.Text, out var amount) || amount <= 0)
        {
            MessageBox.Show("مبلغ التحويل غير صحيح.", "خطأ في البيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (amount > line.DailyLimit - line.DailyUsed)
        {
            MessageBox.Show("المبلغ يتجاوز الحد اليومي المتاح لهذا الخط.", "تجاوز الحد", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var commission = decimal.TryParse(CommissionBox.Text, out var comm) ? comm : 0;

        var result = MessageBox.Show(
            $"هل تريد تنفيذ تحويل بمبلغ {amount:N0} ج.م مع عمولة {commission:N0} ج.م؟",
            "تأكيد التحويل", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var txn = new FitohPhone.Models.TransferTransaction(
                0, System.DateTime.Now, lineId, line.PhoneNumber, null, CustomerPhoneBox.Text,
                amount, commission, "Outbound", "إرسال صادر", "Pending", null,
                App.SessionUserId, "", null, System.DateTime.Now);

            var ok = _vm.CreateTransactionAsync(txn, System.Threading.CancellationToken.None).Result;
            if (ok > 0)
            {
                MessageBox.Show("تم تنفيذ التحويل وحفظه بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                if (_vm.LoadCommand.CanExecute(null))
                    _ = _vm.LoadCommand.ExecuteAsync(null);
            }
            else
            {
                MessageBox.Show("فشل حفظ التحويل.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void EditTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int txnId })
        {
            MessageBox.Show($"تعديل عملية التحويل رقم: {txnId}", "تعديل تحويل", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeleteTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int txnId })
        {
            var result = MessageBox.Show($"هل تريد حذف عملية التحويل رقم: {txnId}؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var ok = _vm.DeleteLineAsync(txnId, System.Threading.CancellationToken.None).Result;
                if (ok)
                {
                    MessageBox.Show("تم حذف العملية.", "حذف", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (_vm.LoadCommand.CanExecute(null))
                        _ = _vm.LoadCommand.ExecuteAsync(null);
                }
                else
                {
                    MessageBox.Show("فشل حذف العملية.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
