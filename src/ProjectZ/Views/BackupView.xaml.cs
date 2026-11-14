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
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BackupViewModel oldVm)
        {
            oldVm.BrowseBackupFolderRequested = null;
            oldVm.BrowseRestoreFileRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }

        if (e.NewValue is BackupViewModel newVm)
        {
            newVm.BrowseBackupFolderRequested = onSelected => BrowseBackupFolder(onSelected);
            newVm.BrowseRestoreFileRequested = onSelected => BrowseRestoreFile(onSelected);
            newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
            {
                if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true)
                    onConfirmed();
            };
        }
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
