using System.Windows;

namespace FitohPhone.Views.Dialogs;

public partial class AddCustomerWindow : Window
{
    public AddCustomerWindow() => InitializeComponent();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
