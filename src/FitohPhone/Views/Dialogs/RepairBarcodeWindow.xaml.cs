using System.Windows;

namespace FitohPhone.Views.Dialogs;

public partial class RepairBarcodeWindow : Window
{
    public RepairBarcodeWindow()
    {
        InitializeComponent();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        // طباعة الباركود تُنفّذ هنا (ستُربط بطابعة الملصقات في phase لاحقة)
        DialogResult = true;
        Close();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
