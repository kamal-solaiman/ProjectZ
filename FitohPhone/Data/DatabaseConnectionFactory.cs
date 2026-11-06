using Microsoft.Data.SqlClient;
using FitohPhone.Helpers;

namespace FitohPhone.Data;

/// <summary>
/// Builds SqlConnection instances from the external appsettings.json.
/// Supports server name, IP address and named instances (SERVER\INSTANCE).
/// Integrated Security and SQL Server Authentication are both supported.
/// </summary>
public static class DatabaseConnectionFactory
{
    private static ConnectionSettings? _settings;

    public static ConnectionSettings Settings => _settings ??= ConnectionSettings.Load();

    /// <summary>Reloads settings from disk (useful after the user edits appsettings.json).</summary>
    public static void Reload() => _settings = ConnectionSettings.Load();

    public static SqlConnection CreateConnection()
    {
        var s = Settings;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = s.Server,                 // e.g. MAIN-PC, 192.168.1.10 or MAIN-PC\SQLEXPRESS
            InitialCatalog = s.Database,
            IntegratedSecurity = s.IntegratedSecurity,
            TrustServerCertificate = s.TrustServerCertificate,
            ConnectTimeout = s.ConnectTimeout,
            Encrypt = true
        };

        if (!s.IntegratedSecurity)
        {
            builder.UserID = s.UserId;
            builder.Password = s.Password;
        }

        return new SqlConnection(builder.ConnectionString);
    }
}
