using Microsoft.Data.SqlClient;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.Services;

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    AccountDisabled,
    DatabaseUnavailable
}

public sealed class LoginResult
{
    public LoginStatus Status { get; init; }
    public User? User { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public static LoginResult Ok(User user) => new() { Status = LoginStatus.Success, User = user };

    public static LoginResult Fail(LoginStatus status, string message) =>
        new() { Status = status, ErrorMessage = message };
}

/// <summary>
/// Authentication logic: verifies username/password against dbo.Users.
/// </summary>
public sealed class AuthService
{
    private readonly UserRepository _userRepository;

    public AuthService() : this(new UserRepository()) { }

    public AuthService(UserRepository userRepository) => _userRepository = userRepository;

    public LoginResult Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return LoginResult.Fail(LoginStatus.InvalidCredentials, "اسم المستخدم أو كلمة المرور غير صحيحة.");

        User? user;
        try
        {
            user = _userRepository.GetByUsername(username.Trim());
        }
        catch (SqlException)
        {
            return LoginResult.Fail(
                LoginStatus.DatabaseUnavailable,
                "تعذر الاتصال بقاعدة البيانات. تأكد من أن السيرفر الرئيسي يعمل وأن عنوان السيرفر صحيح في ملف appsettings.json.");
        }
        catch (InvalidOperationException)
        {
            return LoginResult.Fail(
                LoginStatus.DatabaseUnavailable,
                "تعذر الاتصال بقاعدة البيانات. تأكد من إعدادات الاتصال في ملف appsettings.json.");
        }

        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            return LoginResult.Fail(LoginStatus.InvalidCredentials, "اسم المستخدم أو كلمة المرور غير صحيحة.");

        if (!user.IsActive)
            return LoginResult.Fail(LoginStatus.AccountDisabled, "هذا الحساب غير مفعّل. تواصل مع مسؤول النظام.");

        return LoginResult.Ok(user);
    }
}
