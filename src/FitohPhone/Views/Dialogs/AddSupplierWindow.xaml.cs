using System.Windows;
using FitohPhone.Data;

namespace FitohPhone.Views.Dialogs;

public partial class AddSupplierWindow : Window
{
    private readonly SupplierRepository _repo;

    public AddSupplierWindow()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _repo = app.SupplierRepo;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NameInput.Text?.Trim();
            var phone = PhoneInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone)) return;

            await _repo.AddAsync(new FitohPhone.Models.Supplier(
                0, name, phone,
                ContactPersonInput.Text?.Trim(), AddressInput.Text?.Trim(),
                null, // city not in XAML
                InstaPayInput.Text?.Trim(),
                0, 0, 0, true, System.DateTime.Now, null),
                System.Threading.CancellationToken.None);

            DialogResult = true;
            Close();
        }
        catch
        {
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
