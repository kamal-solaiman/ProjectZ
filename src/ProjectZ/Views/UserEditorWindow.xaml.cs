using System.Windows;
using ProjectZ.Data;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

/// <summary>
/// Add / edit / change-password dialog for one login account.
/// The PasswordBox controls hold the only copy of the plain password; they are cleared as soon as the
/// view model has hashed and saved it, and again when the window closes.
/// </summary>
public partial class UserEditorWindow : Window
{
    private readonly UserEditorViewModel viewModel;

    public UserEditorWindow(
        string mode,
        UserRepository repository,
        UserRecord? existing,
        Action<UserRecord> onSaved)
    {
        InitializeComponent();

        viewModel = new UserEditorViewModel(
            mode,
            repository,
            existing,
            () => PasswordInput.Password,
            () => PasswordConfirmationInput.Password,
            ClearPasswords,
            saved =>
            {
                DialogResult = true;
                Close();
                onSaved(saved);
            },
            () =>
            {
                DialogResult = false;
                Close();
            });

        DataContext = viewModel;
        Loaded += (_, _) => FocusFirstField();
        Closed += (_, _) =>
        {
            ClearPasswords();
            DataContext = null;
        };
    }

    public void ClearPasswords()
    {
        PasswordInput.Clear();
        PasswordConfirmationInput.Clear();
    }

    private void FocusFirstField()
    {
        if (viewModel.ShowIdentityFields)
        {
            UsernameInput.Focus();
            UsernameInput.SelectAll();
        }
        else
        {
            PasswordInput.Focus();
        }
    }
}
