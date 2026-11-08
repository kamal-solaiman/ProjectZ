using System.IO;
using System.Text.Json;
using FitohPhone.Models;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Services;

public sealed class ConnectionConfigurationException(string userMessage, Exception? innerException = null)
    : InvalidOperationException(userMessage, innerException);

public sealed class ConnectionSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string GetConnectionString()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "connectionsettings.json");
        if (!File.Exists(path))
        {
            throw new ConnectionConfigurationException(
                "ملف الإعدادات connectionsettings.json غير موجود بجوار البرنامج. تأكد من وجوده في نفس مجلد التشغيل.");
        }

        ConnectionSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ConnectionConfigurationException(
                "تعذر قراءة ملف connectionsettings.json. تأكد من صحة صيغة JSON (واكتب الشرطة العكسية مزدوجة \\\\ عند وجود Instance).", ex);
        }

        if (settings is null || string.IsNullOrWhiteSpace(settings.Server)
            || settings.Server.Contains("SERVER_NAME", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConnectionConfigurationException(
                "عنوان الخادم (Server) غير مضبوط في ملف connectionsettings.json. حدد اسم الخادم مثل DESKTOP-M1T0F31.");
        }

        if (string.IsNullOrWhiteSpace(settings.Database))
        {
            throw new ConnectionConfigurationException(
                "اسم قاعدة البيانات (Database) غير محدد في ملف connectionsettings.json.");
        }

        if (settings.ConnectTimeoutSeconds is < 1 or > 60)
        {
            throw new ConnectionConfigurationException(
                "مهلة الاتصال (ConnectTimeoutSeconds) في connectionsettings.json يجب أن تكون بين 1 و60 ثانية.");
        }

        if (!settings.IntegratedSecurity && (string.IsNullOrWhiteSpace(settings.UserId) || string.IsNullOrWhiteSpace(settings.Password)))
        {
            throw new ConnectionConfigurationException(
                "عند ضبط IntegratedSecurity على false يجب تحديد اسم مستخدم SQL (UserId) وكلمة المرور (Password) في connectionsettings.json.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = settings.Server.Trim(),
                InitialCatalog = settings.Database.Trim(),
                IntegratedSecurity = settings.IntegratedSecurity,
                Encrypt = settings.Encrypt,
                TrustServerCertificate = settings.TrustServerCertificate,
                ConnectTimeout = settings.ConnectTimeoutSeconds,
                ApplicationName = "FitohPhone",
                PersistSecurityInfo = false
            };

            if (!settings.IntegratedSecurity)
            {
                builder.UserID = settings.UserId.Trim();
                builder.Password = settings.Password ?? "";
            }

            return builder.ConnectionString;
        }
        catch (ArgumentException ex)
        {
            throw new ConnectionConfigurationException(
                "توجد قيمة غير صالحة في إعدادات الاتصال داخل ملف connectionsettings.json.", ex);
        }
    }
}
