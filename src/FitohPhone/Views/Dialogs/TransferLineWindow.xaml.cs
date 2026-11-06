using System.Windows;
using FitohPhone.Data;

namespace FitohPhone.Views.Dialogs;

public partial class TransferLineWindow : Window
{
    private readonly TransferRepository _repo;

    public TransferLineWindow()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _repo = app.TransferRepo;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var provider = ProviderCombo.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string tag } ? tag : "VodafoneCash";
            var phone = PhoneInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(phone)) return;

            var balance = decimal.TryParse(BalanceInput.Text, out var b) ? b : 0;
            var dailyLimit = decimal.TryParse(DailyLimitInput.Text, out var dl) ? dl : 0;
            var monthlyLimit = decimal.TryParse(MonthlyLimitInput.Text, out var ml) ? ml : 0;

            await _repo.AddLineAsync(new FitohPhone.Models.TransferLine(
                0, provider, provider == "VodafoneCash" ? "فودافون كاش" : "إنستاباي",
                phone, balance, dailyLimit, monthlyLimit,
                0, 0, null, null, true, System.DateTime.Now, null),
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
