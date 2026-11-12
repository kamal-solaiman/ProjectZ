using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class VodafoneRecordsView : UserControl
{
    public VodafoneRecordsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private VodafoneRecordsViewModel? ViewModel => DataContext as VodafoneRecordsViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VodafoneRecordsViewModel oldVm)
        {
            oldVm.DetailsRequested = null; oldVm.EditRequested = null; oldVm.ConfirmRequested = null;
        }
        if (e.NewValue is not VodafoneRecordsViewModel vm) return;
        vm.DetailsRequested = operation => ShowOwnedDialog(new VodafoneRecordDetailsWindow { DataContext = operation });
        vm.EditRequested = () => ShowOwnedDialog(new VodafoneRecordEditWindow { DataContext = vm });
        vm.ConfirmRequested = (confirm, action) =>
        {
            if (ShowOwnedDialog(new ConfirmActionWindow(confirm)) == true) action();
        };
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner) dialog.Owner = owner;
        return dialog.ShowDialog();
    }

    private void RecordCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not null && sender is FrameworkElement { DataContext: VodafoneTransferOperation operation })
            ViewModel.SelectedOperation = operation;
    }

    private void CopyRowReference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VodafoneTransferOperation operation }) return;
        if (ViewModel is not null) ViewModel.SelectedOperation = operation;
        Clipboard.SetText(operation.Reference);
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var dialog = new SaveFileDialog { Title = "تصدير سجلات فودافون كاش", Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = $"Vodafone-Records-{DateTime.Now:yyyyMMdd-HHmmss}.csv", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllLines(dialog.FileName, ViewModel.CreateCsvLines(), new UTF8Encoding(true));
        MessageBox.Show("تم تصدير السجلات المفلترة بنجاح.", "تصدير CSV", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(PrintArea, "سجلات فودافون كاش");
    }
}
