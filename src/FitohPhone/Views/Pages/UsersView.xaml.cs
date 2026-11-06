using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class UsersView : UserControl
{
    private readonly UsersViewModel _vm;
    private int? _editingUserId;

    public UsersView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new UsersViewModel(app.UserRepo, app.UserRoleRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب المستخدمين والأدوار من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);
        };
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        _editingUserId = null;
        DisplayNameBox.Text = "";
        UsernameBox.Text = "";
        PasswordBox.Password = "";
        RoleCombo.SelectedIndex = 0;
        IsActiveCheck.IsChecked = true;
        MessageBox.Show("أدخل بيانات المستخدم الجديد ثم اضغط حفظ التعديلات.", "إضافة مستخدم", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveUser_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) || string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            MessageBox.Show("يرجى تعبئة الاسم المعروض واسم الدخول.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedRole = RoleCombo.SelectedItem as FitohPhone.Models.UserRole;
        if (selectedRole is null)
        {
            MessageBox.Show("يرجى اختيار دور وظيفي.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isNew = _editingUserId is null;
        var action = isNew ? "إضافة" : "تعديل";

        var result = MessageBox.Show($"هل تريد {action} المستخدم: {DisplayNameBox.Text}؟", $"تأكيد {action}", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            MessageBox.Show($"تم {action} المستخدم بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            if (_vm.LoadCommand.CanExecute(null))
                _ = _vm.LoadCommand.ExecuteAsync(null);
            _editingUserId = null;
        }
    }

    private void DeleteUser_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("سيتم حذف المستخدم المحدد في الإصدار القادم.", "حذف مستخدم", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void EditUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int userId })
        {
            _editingUserId = userId;
            var user = _vm.Users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return;

            DisplayNameBox.Text = user.DisplayName;
            UsernameBox.Text = user.Username;
            PasswordBox.Password = "";
            RoleCombo.SelectedValue = user.Id;
            IsActiveCheck.IsChecked = true;

            MessageBox.Show($"تم تحميل بيانات المستخدم: {user.DisplayName}\nعدّل البيانات ثم اضغط حفظ التعديلات.", "تعديل مستخدم", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void PermissionsUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int userId })
        {
            _vm.SelectedUserId = userId;
            var user = _vm.Users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return;

            PermissionsTitle.Text = $"تعديل صلاحيات المستخدم: {user.DisplayName} ({user.Username})";

            var loadTask = _vm.LoadPermissionsForUserAsync(userId, System.Threading.CancellationToken.None);
            _ = loadTask.ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_vm.Permissions.Count == 0)
                        MessageBox.Show("لا توجد صلاحيات محددة لهذا المستخدم.", "صلاحيات", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }, System.Threading.CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);
        }
    }

    private void SavePermissions_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedUserId <= 0)
        {
            MessageBox.Show("يرجى اختيار مستخدم أولًا لعرض وتعديل صلاحياته.", "اختيار مستخدم", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show("هل تريد حفظ واعتماد صلاحيات المستخدم المحدد؟", "تأكيد حفظ الصلاحيات", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            var ok = _vm.SavePermissionsCommand.CanExecute(null);
            if (ok)
            {
                var saved = _vm.SavePermissionsCommand.ExecuteAsync(null).Result;
                if (saved)
                {
                    MessageBox.Show("تم حفظ واعتماد صلاحيات المستخدم بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("فشل حفظ الصلاحيات.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void SelectAllPermissions_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _vm.Permissions)
        {
            p.CanView = true;
            p.CanAdd = true;
            p.CanEdit = true;
            p.CanDelete = true;
            p.CanPrint = true;
        }
    }

    private void SelectNonePermissions_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _vm.Permissions)
        {
            p.CanView = false;
            p.CanAdd = false;
            p.CanEdit = false;
            p.CanDelete = false;
            p.CanPrint = false;
        }
    }
}
