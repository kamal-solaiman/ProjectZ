using ProjectZ.Data;
using ProjectZ.Helpers;

namespace ProjectZ.ViewModels;

/// <summary>
/// The add/edit dialog for one login account.
/// The password is read through a delegate from the window's PasswordBox at save time, so neither this
/// view model nor the bindings ever hold it. An existing account's password is only replaced through the
/// explicit reset action, never by opening the profile.
/// </summary>
public sealed class UserEditorViewModel : ObservableObject
{
    /// <summary>"add", "edit" or "password"; decides the header and which fields are shown.</summary>
    private readonly string mode;
    private readonly UserRepository repository;
    private readonly UserRecord? original;
    private readonly Func<string> readPassword;
    private readonly Func<string> readPasswordConfirmation;
    private readonly Action clearPasswords;
    private readonly Action<UserRecord> onSaved;
    private readonly Action onCancelled;

    private string username = "";
    private string displayName = "";
    private bool isActive = true;
    private string error = "";
    private string success = "";

    public UserEditorViewModel(
        string mode,
        UserRepository repository,
        UserRecord? existing,
        Func<string> readPassword,
        Func<string> readPasswordConfirmation,
        Action clearPasswords,
        Action<UserRecord> onSaved,
        Action onCancelled,
        bool isOnline = true)
    {
        this.mode = mode;
        this.repository = repository;
        original = existing;
        this.readPassword = readPassword;
        this.readPasswordConfirmation = readPasswordConfirmation;
        this.clearPasswords = clearPasswords;
        this.onSaved = onSaved;
        this.onCancelled = onCancelled;
        IsOnline = isOnline;

        if (existing is not null)
        {
            username = existing.Username;
            displayName = existing.DisplayName;
            isActive = existing.IsActive;
        }

        SaveCommand = new ActionCommand(Save);
        CancelCommand = new ActionCommand(() => this.onCancelled());
    }

    public bool IsOnline { get; }
    public string Mode => mode;
    public bool IsCreateMode => mode == "add";
    public bool IsEditMode => mode == "edit";
    public bool IsPasswordMode => mode == "password";

    /// <summary>The username is never edited in password mode; it is shown read-only as context.</summary>
    public bool ShowIdentityFields => !IsPasswordMode;
    public bool ShowPasswordFields => !IsEditMode;
    public bool ShowActiveField => !IsPasswordMode;

    public string WindowTitle => mode switch
    {
        "add" => "ProjectZ | إضافة مستخدم جديد",
        "password" => "ProjectZ | تغيير كلمة المرور",
        _ => "ProjectZ | تعديل بيانات المستخدم"
    };

    public string HeaderTitle => mode switch
    {
        "add" => "إضافة مستخدم جديد",
        "password" => "تغيير كلمة المرور",
        _ => "تعديل بيانات المستخدم"
    };

    public string HeaderSubtitle => mode switch
    {
        "add" => "حساب دخول جديد إلى برنامج ProjectZ",
        "password" => $"تعيين كلمة مرور جديدة للحساب ({original?.Username})",
        _ => $"تحديث بيانات الدخول للحساب ({original?.Username})"
    };

    public string BannerHintText => mode switch
    {
        "add" => "يُحفظ الحساب في جدول dbo.Users فورًا ويستطيع صاحبه تسجيل الدخول بعد الحفظ. كلمة المرور تُحفظ مشفّرة (PBKDF2) ولا تُعرض مرة أخرى.",
        "password" => "تُحفظ كلمة المرور الجديدة مشفّرة (PBKDF2) في dbo.Users. لا يمكن عرضها مرة أخرى، ويمكن تعيين كلمة مرور جديدة في أي وقت.",
        _ => "تعديل بيانات الدخول لا يغيّر كلمة المرور. لتعيين كلمة مرور جديدة استخدم زر «تغيير كلمة المرور» من قائمة المستخدمين."
    };

    public string SaveButtonText => mode switch
    {
        "add" => "حفظ المستخدم",
        "password" => "حفظ كلمة المرور",
        _ => "حفظ التعديلات"
    };

    public string Username
    {
        get => username;
        set
        {
            if (username == value) return;
            username = value;
            Changed();
            if (!string.IsNullOrWhiteSpace(Error)) Error = "";
        }
    }

    public string DisplayName
    {
        get => displayName;
        set
        {
            if (displayName == value) return;
            displayName = value;
            Changed();
            if (!string.IsNullOrWhiteSpace(Error)) Error = "";
        }
    }

    public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive == value) return;
            isActive = value;
            Changed();
        }
    }

    public string Error
    {
        get => error;
        private set
        {
            if (error == value) return;
            error = value;
            Changed();
            Changed(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public string Success
    {
        get => success;
        private set
        {
            if (success == value) return;
            success = value;
            Changed();
            Changed(nameof(HasSuccess));
        }
    }

    public bool HasSuccess => !string.IsNullOrWhiteSpace(Success);

    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    private void Save()
    {
        Error = "";
        Success = "";

        // The profile of an existing account never contains a password field.
        if (IsEditMode)
        {
            SaveProfile();
            return;
        }

        var password = readPassword();
        var confirmation = readPasswordConfirmation();

        try
        {
            if (string.IsNullOrEmpty(password))
            {
                Error = "يرجى إدخال كلمة المرور.";
                return;
            }

            if (password != confirmation)
            {
                Error = "كلمتا المرور غير متطابقتين.";
                return;
            }

            if (!IsOnline)
            {
                // Design previews stay explorable without a database; nothing is written.
                Success = IsCreateMode ? "وضع العرض التجريبي: لم يُحفظ المستخدم." : "وضع العرض التجريبي: لم تُحفظ كلمة المرور.";
                return;
            }

            if (IsCreateMode)
            {
                var created = repository.Create(Username, DisplayName, password, IsActive);
                clearPasswords();
                onSaved(created);
                return;
            }

            repository.ResetPassword(original!.Id, password);
            clearPasswords();
            onSaved(original);
        }
        catch (Exception ex)
        {
            Error = ex is UserDataException data
                ? data.Message
                : "تعذر حفظ بيانات المستخدم في قاعدة البيانات. تحقق من الاتصال وأعد المحاولة.";
        }
        finally
        {
            password = "";
            confirmation = "";
        }
    }

    private void SaveProfile()
    {
        if (!IsOnline)
        {
            Success = "وضع العرض التجريبي: لم يُحفظ التعديل.";
            return;
        }

        try
        {
            var saved = repository.Update(original!.Id, Username, DisplayName, IsActive);
            onSaved(saved);
        }
        catch (Exception ex)
        {
            Error = ex is UserDataException data
                ? data.Message
                : "تعذر تعديل المستخدم في قاعدة البيانات. تحقق من الاتصال وأعد المحاولة.";
        }
    }
}
