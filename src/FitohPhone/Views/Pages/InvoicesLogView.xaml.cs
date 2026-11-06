using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class InvoicesLogView : UserControl
{
    private readonly InvoicesLogViewModel _vm;

    public InvoicesLogView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new InvoicesLogViewModel(app.SalesInvoiceRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب الفواتير من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LoadCommand.CanExecute(null))
            await _vm.LoadCommand.ExecuteAsync(null);
    }

    private async void ViewInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int invoiceId })
        {
            await _vm.LoadInvoiceDetailsAsync(invoiceId);
        }
    }

    private void EditInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int invoiceId })
        {
            MessageBox.Show($"تعديل الفاتورة رقم: {invoiceId}", "تعديل فاتورة", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void PrintInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int invoiceId })
        {
            MessageBox.Show($"طباعة الفاتورة رقم: {invoiceId}", "طباعة فاتورة", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void ReturnInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int invoiceId })
        {
            var result = MessageBox.Show($"هل تريد إنشاء مرتجع للفاتورة رقم: {invoiceId}؟", "إنشاء مرتجع", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                // يمكن فتح نافذة المرتجع لاحقًا مع تمرير invoiceId
                MessageBox.Show($"سيتم فتح نافذة المرتجع للفاتورة: {invoiceId}", "مرتجع", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void CancelInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int invoiceId })
        {
            var result = MessageBox.Show($"هل أنت متأكد من إلغاء الفاتورة رقم: {invoiceId}؟", "تأكيد الإلغاء", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var ok = await _vm.CancelInvoiceAsync(invoiceId, System.Threading.CancellationToken.None);
                if (ok)
                {
                    MessageBox.Show("تم إلغاء الفاتورة بنجاح.", "إلغاء فاتورة", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (_vm.LoadCommand.CanExecute(null))
                        await _vm.LoadCommand.ExecuteAsync(null);
                }
                else
                {
                    MessageBox.Show("فشل إلغاء الفاتورة.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
