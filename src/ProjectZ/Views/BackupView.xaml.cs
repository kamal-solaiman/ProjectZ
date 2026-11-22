using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // DataContext can already be inherited when the control loads; the event above is the normal
        // path, and Loaded is a second chance so the browse/confirm buttons are never dead.
        Loaded += (_, _) => Attach(DataContext as BackupViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BackupViewModel oldVm)
        {
            oldVm.BrowseBackupFolderRequested = null;
            oldVm.BrowseRestoreFileRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }

        Attach(e.NewValue as BackupViewModel);
    }

    private void Attach(BackupViewModel? viewModel)
    {
        if (viewModel is null) return;

        viewModel.BrowseBackupFolderRequested = onSelected => BrowseBackupFolder(onSelected);
        viewModel.BrowseRestoreFileRequested = onSelected => BrowseRestoreFile(onSelected);
        viewModel.OpenConfirmDialogRequested = (vm, onConfirmed) =>
        {
            if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true)
                onConfirmed();
        };
    }

    private void BrowseBackupFolder(Action<string> onSelected)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "اختر مجلد حفظ النسخة الاحتياطية",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            onSelected(dialog.FolderName);
    }

    private void BrowseRestoreFile(Action<string> onSelected)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختر ملف النسخة الاحتياطية للاسترجاع",
            Filter = "ملفات النسخ الاحتياطي (*.bak)|*.bak|كل الملفات (*.*)|*.*",
            FilterIndex = 1,
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
            onSelected(dialog.FileName);
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } window)
            dialog.Owner = window;

        return dialog.ShowDialog();
    }
}
