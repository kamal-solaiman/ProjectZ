using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InstapayRecordsView : UserControl
{
    public InstapayRecordsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private InstapayRecordsViewModel? ViewModel => DataContext as InstapayRecordsViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InstapayRecordsViewModel oldVm)
        {
            oldVm.DetailsRequested = null; oldVm.EditRequested = null; oldVm.ConfirmRequested = null;
        }
        if (e.NewValue is not InstapayRecordsViewModel vm) return;
        vm.DetailsRequested = operation => ShowOwnedDialog(new InstapayRecordDetailsWindow { DataContext = operation });
        vm.EditRequested = () => ShowOwnedDialog(new InstapayRecordEditWindow { DataContext = vm });
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
        if (ViewModel is not null && sender is FrameworkElement { DataContext: InstapayTransferOperation operation })
            ViewModel.SelectedOperation = operation;
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var dialog = new SaveFileDialog { Title = "تصدير سجلات إنستاباي", Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = $"Instapay-Records-{DateTime.Now:yyyyMMdd-HHmmss}.csv", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllLines(dialog.FileName, ViewModel.CreateCsvLines(), new UTF8Encoding(true));
        MessageBox.Show("تم تصدير السجلات المفلترة بنجاح.", "تصدير CSV", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(PrintArea, "سجلات إنستاباي");
    }
}
