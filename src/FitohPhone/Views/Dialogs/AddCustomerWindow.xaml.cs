using System.Windows;
using FitohPhone.Data;

namespace FitohPhone.Views.Dialogs;

public partial class AddCustomerWindow : Window
{
    private readonly CustomerRepository _repo;

    public AddCustomerWindow()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _repo = app.CustomerRepo;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NameInput.Text?.Trim();
            var phone = PhoneInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone))
            {
                // يمكن عرض خطأ في واجهة المستخدم لاحقًا
                return;
            }

            var existing = await _repo.GetByPhoneAsync(phone, System.Threading.CancellationToken.None);
            if (existing != null) return;

            await _repo.AddAsync(new FitohPhone.Models.Customer(
                0, name, phone,
                AltPhoneInput.Text?.Trim(), AddressInput.Text?.Trim(),
                CityInput.Text?.Trim(), null, // البريد الإلكتروني غير موجود في XAML حالياً
                0, 0, 0, null, true, System.DateTime.Now, null),
                System.Threading.CancellationToken.None);

            DialogResult = true;
            Close();
        }
        catch
        {
            // يمكن تسجيل الخطأ لاحقًا
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
