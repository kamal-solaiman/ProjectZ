using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class ReturnExchangeView : UserControl
{
    public ReturnExchangeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ReturnExchangeViewModel oldVm)
            oldVm.OpenPrintPreviewRequested = null;

        if (e.NewValue is ReturnExchangeViewModel newVm)
            newVm.OpenPrintPreviewRequested = vm => ShowOwnedDialog(new InvoicePrintPreviewWindow(vm));
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;

        return dialog.ShowDialog();
    }
}
