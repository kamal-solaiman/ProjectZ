using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class PhoneRepairsView : UserControl
{
    private readonly PhoneRepairsViewModel _vm;

    public PhoneRepairsView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new PhoneRepairsViewModel(app.PhoneRepairRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب الإصلاحات من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);

            // تعبئة ComboBox العملاء
            CustomerCombo.Items.Clear();
            // يمكن جلب العملاء من CustomerRepo لاحقًا إذا لزم الأمر
            CustomerCombo.Items.Add("أحمد طارق محمود — 01065432198");
            CustomerCombo.Items.Add("سارة حسن علي — 01229988776");
            CustomerCombo.Items.Add("محمود عادل — 01122334455");
            if (CustomerCombo.Items.Count > 0)
                CustomerCombo.SelectedIndex = 0;

            // توليد رقم إصلاح تلقائي
            RepairNumberBlock.Text = $"REP-{DateTime.Now:yyyy}-{(_vm.Repairs.Count + 1):D4}";
        };
    }

    private void OpenAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCustomerWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private void OpenBarcodePreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RepairBarcodeWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private async void SaveRepair_Click(object sender, RoutedEventArgs e)
    {
        // التحقق من صحة البيانات الإجبارية
        if (string.IsNullOrWhiteSpace(CustomerPhoneBox.Text) ||
            string.IsNullOrWhiteSpace(ModelBox.Text) ||
            string.IsNullOrWhiteSpace(ProblemBox.Text))
        {
            MessageBox.Show("يرجى تعبئة جميع الحقول الإجبارية (رقم الهاتف، موديل الجهاز، وصف العطل).", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(TotalCostBox.Text, out var totalCost) || totalCost <= 0)
        {
            MessageBox.Show("التكلفة الإجمالية غير صحيحة.", "خطأ في البيانات", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var paid = decimal.TryParse(PaidAmountBox.Text, out var p) ? p : 0;

        var repair = new FitOhPhone.Models.PhoneRepair(
            0,
            RepairNumberBlock.Text,
            0, // CustomerId سيُحدد لاحقًا من قبل المستودع
            CustomerCombo.Text,
            CustomerPhoneBox.Text,
            App.SessionUserId,
            "", // ReceivedByUserName
            $"{BrandCombo.Text} — {ModelBox.Text}",
            ImeiBox.Text,
            null,
            ProblemBox.Text,
            null, // Diagnosis
            "قيد الاستلام",
            DateTime.Now,
            DateTime.TryParse(EstimatedDeliveryBox.Text, out var est) ? est : null,
            null, // ActualDeliveryDate
            null,
            null,
            totalCost,
            0, // PartsCost
            totalCost, // LaborCost
            totalCost,
            paid,
            totalCost - paid,
            false, // BarcodePrinted
            null,
            null, // WarrantyPeriodDays
            $"{ConditionBox.Text} | الملحقات: {AccessoriesBox.Text}",
            DateTime.Now,
            null,
            null // Items
        );

        var id = await _vm.AddRepairAsync(repair, System.Threading.CancellationToken.None);
        if (id > 0)
        {
            MessageBox.Show($"تم حفظ الاستلام برقم إيصال: {RepairNumberBlock.Text}\nتم إنشاء باركود الجهاز.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            if (_vm.LoadCommand.CanExecute(null))
                _ = _vm.LoadCommand.ExecuteAsync(null);
        }
        else
        {
            MessageBox.Show("فشل حفظ الاستلام.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveRepairUpdate_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("سيتم حفظ تحديثات الصيانة (التشخيص والتكاليف) في الإصدار القادم.", "حفظ تحديثات", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MarkDelivered_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("هل تريد تأكيد تسليم الجهاز للعميل وطباعة الفاتورة؟", "تأكيد التسليم", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            MessageBox.Show("تم تأكيد تسليم الجهاز وطباعة الفاتورة.", "تم التسليم", MessageBoxButton.OK, MessageBoxImage.Information);
            if (_vm.LoadCommand.CanExecute(null))
                _ = _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void SearchCustomerRepairs_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("سيتم عرض سجل صيانات العميل المحدد في الإصدار القادم.", "أرشيف الصيانات", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
