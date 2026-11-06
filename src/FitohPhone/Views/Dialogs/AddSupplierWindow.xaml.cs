using System.Windows;

namespace FitohPhone.Views.Dialogs;

public partial class AddSupplierWindow : Window
{
    public AddSupplierWindow() => InitializeComponent();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
