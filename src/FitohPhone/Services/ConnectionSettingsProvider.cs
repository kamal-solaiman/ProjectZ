using System.IO;
using System.Text.Json;
using FitohPhone.Models;
using Microsoft.Data.SqlClient;
namespace FitohPhone.Services;
public sealed class ConnectionSettingsProvider
{
    public string GetConnectionString()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "connectionsettings.json");
            var settings = JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException();
            if (string.IsNullOrWhiteSpace(settings.Server) || settings.Server.Contains("SERVER_NAME", StringComparison.OrdinalIgnoreCase)
                || settings.Database != "FitohPhone" || settings.ConnectTimeoutSeconds is < 1 or > 60
                || (!settings.IntegratedSecurity && string.IsNullOrWhiteSpace(settings.UserId)))
                throw new InvalidDataException();
            return new SqlConnectionStringBuilder
            {
                DataSource = settings.Server.Trim(), InitialCatalog = settings.Database,
                IntegratedSecurity = settings.IntegratedSecurity, UserID = settings.IntegratedSecurity ? "" : settings.UserId,
                Password = settings.IntegratedSecurity ? "" : settings.Password,
                Encrypt = settings.Encrypt, TrustServerCertificate = settings.TrustServerCertificate,
                ConnectTimeout = settings.ConnectTimeoutSeconds, ApplicationName = "FitohPhone",
                PersistSecurityInfo = false
            }.ConnectionString;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new InvalidOperationException("تعذر قراءة إعدادات الاتصال. راجع ملف connectionsettings.json بجوار البرنامج وحدد عنوان الخادم وبيانات الاتصال الصحيحة.", ex);
        }
    }
}
