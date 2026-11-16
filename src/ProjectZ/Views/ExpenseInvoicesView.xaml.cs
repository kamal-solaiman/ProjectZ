using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class ExpenseInvoicesView : UserControl
{
    public ExpenseInvoicesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ExpenseInvoicesViewModel? ViewModel => DataContext as ExpenseInvoicesViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ExpenseInvoicesViewModel oldVm)
            oldVm.OpenDetailsDialogRequested = null;

        if (e.NewValue is ExpenseInvoicesViewModel newVm)
            newVm.OpenDetailsDialogRequested = ShowDetailsDialog;
    }

    private void ShowDetailsDialog(ExpenseInvoiceDetailsViewModel viewModel)
    {
        var dialog = new ExpenseInvoiceDetailsWindow(viewModel);
        if (Window.GetWindow(this) is { } owner && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        dialog.ShowDialog();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "تصدير فواتير المصروفات",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = $"Expense-Invoices-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true) return;

        File.WriteAllLines(dialog.FileName, ViewModel.CreateCsvLines(), new UTF8Encoding(true));
        MessageBox.Show(
            $"تم تصدير {ViewModel.ShownCount} مصروف إلى الملف المحدد.",
            "تصدير CSV",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        dialog.PrintVisual(PrintArea, "فواتير المصروفات");
    }
}
