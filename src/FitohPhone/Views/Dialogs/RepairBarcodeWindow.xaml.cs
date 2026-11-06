using System.Windows;

namespace FitohPhone.Views.Dialogs;

public partial class RepairBarcodeWindow : Window
{
    public RepairBarcodeWindow() => InitializeComponent();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
