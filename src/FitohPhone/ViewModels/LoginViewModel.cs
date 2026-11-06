using FitohPhone.Helpers;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;
namespace FitohPhone.ViewModels;
public sealed class LoginViewModel : ObservableObject
{
    private string username = "", error = "";
    private bool busy;
    public string Username { get => username; set { username = value; Changed(); } }
    public string Error { get => error; private set { error = value; Changed(); } }
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(IsIdle)); Changed(nameof(ButtonText)); } }
    public bool IsIdle => !IsBusy;
    public string ButtonText => IsBusy ? "جارٍ تسجيل الدخول…" : "تسجيل الدخول";
    public AsyncCommand LoginCommand { get; }
    public LoginViewModel(AuthenticationService service, Func<string> readPassword, Action clearPassword,
        Action<UserSession> signedIn, CancellationToken token)
    {
        LoginCommand = new AsyncCommand(async () =>
        {
            Error = "";
            string password = readPassword();
            clearPassword();
            if (string.IsNullOrWhiteSpace(Username) || Username.Trim().Length > 100 || password.Length is 0 or > 1024)
            { Error = "أدخل اسم المستخدم (حتى 100 حرف) وكلمة المرور (حتى 1024 حرفًا)."; return; }
            IsBusy = true;
            try
            {
                var user = await service.SignInAsync(Username.Trim(), password, token);
                if (user is null) Error = "اسم المستخدم أو كلمة المرور غير صحيحة، أو الحساب غير نشط.";
                else signedIn(user);
            }
            catch (OperationCanceledException) { if (!token.IsCancellationRequested) Error = "انتهت مهلة الاتصال. حاول مرة أخرى."; }
            catch (SqlException) { Error = "تعذر الاتصال بقاعدة البيانات. تحقق من الشبكة وتشغيل الخادم وإعدادات الاتصال وصلاحيات قاعدة البيانات."; }
            catch (InvalidOperationException) { Error = "تعذر فتح الاتصال. راجع ملف connectionsettings.json وعنوان الخادم وإعدادات الأمان."; }
            catch (Exception) { Error = "حدث خطأ غير متوقع أثناء تسجيل الدخول. أعد المحاولة أو تواصل مع مسؤول النظام."; }
            finally { password = ""; IsBusy = false; }
        });
    }
}
