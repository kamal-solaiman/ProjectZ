using System.ComponentModel;
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
            {
                Error = "أدخل اسم المستخدم (حتى 100 حرف) وكلمة المرور (حتى 1024 حرفًا).";
                return;
            }

            IsBusy = true;
            try
            {
                var user = await service.SignInAsync(Username.Trim(), password, token);
                if (user is null) Error = "اسم المستخدم أو كلمة المرور غير صحيحة، أو الحساب غير نشط.";
                else signedIn(user);
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested) Error = "انتهت مهلة الاتصال. حاول مرة أخرى.";
            }
            catch (ConnectionConfigurationException ex)
            {
                Error = ex.Message;
            }
            catch (SqlException ex)
            {
                Error = DescribeSqlError(ex);
            }
            catch (InvalidOperationException)
            {
                Error = "تعذر فتح الاتصال. راجع ملف connectionsettings.json وعنوان الخادم وإعدادات الأمان.";
            }
            catch (Exception)
            {
                Error = "حدث خطأ غير متوقع أثناء تسجيل الدخول. أعد المحاولة أو تواصل مع مسؤول النظام.";
            }
            finally
            {
                password = "";
                IsBusy = false;
            }
        });
    }

    private static string DescribeSqlError(SqlException ex)
    {
        int win32Code = (ex.InnerException as Win32Exception)?.NativeErrorCode ?? 0;
        int code = ex.Number != 0 ? ex.Number : win32Code;

        if (code is -2146893019 or -2146893022
            || ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
        {
            return "رفض الاتصال بسبب شهادة التشفير (TLS) غير الموثوقة. اضبط TrustServerCertificate على true في ملف connectionsettings.json.";
        }

        if (code is -2146893042 or -2146893055
            || ex.Message.Contains("SSPI", StringComparison.OrdinalIgnoreCase))
        {
            return "تعذر التحقق من هوية Windows عبر الشبكة (SSPI). خارج بيئة Domain، أنشئ حساب SQL Login واضبط IntegratedSecurity على false.";
        }

        return code switch
        {
            26 => "تعذر العثور على نسخة SQL المحددة (خطأ 26). راجع اسم Server في connectionsettings.json أو شغّل خدمة SQL Server Browser عند استخدام Instance مسماة.",
            10061 => "رفض الخادم اتصال الشبكة (TCP). تأكد من تشغيل خدمة SQL Server وتفعيل بروتوكول TCP/IP على المنفذ 1433 ثم إعادة تشغيل الخدمة.",
            2 or 40 or 53 or 64 or 233 or 10060 or 11001 =>
                "تعذر الوصول إلى خادم SQL Server. تحقق من اسم الخادم في connectionsettings.json، وتشغيل خدمة SQL Server، وتفعيل TCP/IP، وفتح المنفذ في جدار الحماية.",
            4060 => "تعذر فتح قاعدة البيانات FitohPhone (خطأ 4060). تأكد من إنشاء القاعدة على هذا الخادم ومنح حساب الدخول صلاحية الوصول إليها.",
            18452 => "نوع المصادقة غير مفعّل على الخادم (خطأ 18452). راجع إعداد IntegratedSecurity أو فعّل Mixed Mode في SQL Server.",
            18456 => "رفض خادم SQL تسجيل دخول حساب الاتصال (خطأ 18456). تحقق من صلاحيات حساب Windows في SQL Server أو بيانات UserId/Password.",
            208 => "جدول المستخدمين (dbo.Users) غير موجود في قاعدة البيانات FitohPhone. نفّذ sql/01_CreateDatabase.sql ثم sql/02_SeedAdmin.sql في SSMS.",
            229 => "حساب الاتصال لا يملك صلاحية القراءة (SELECT) على جدول dbo.Users (خطأ 229).",
            -2 or 258 => "انتهت مهلة الاتصال بقاعدة البيانات. تحقق من استجابة الخادم والشبكة وجدار الحماية.",
            _ => $"تعذر الاتصال بقاعدة البيانات (رمز خطأ SQL: {code}). تحقق من تشغيل SQL Server وإعدادات connectionsettings.json وصلاحيات القاعدة."
        };
    }
}
