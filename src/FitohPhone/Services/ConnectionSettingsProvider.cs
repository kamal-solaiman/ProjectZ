using System.IO;
using System.Text.Json;
using FitohPhone.Models;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Services;

public sealed class ConnectionConfigurationException(string userMessage, Exception? innerException = null)
    : InvalidOperationException(userMessage, innerException);

/// Describes which section of connectionsettings.json this machine resolved to.
/// Shown on the login window so a LAN deployment can be diagnosed without SSMS.
public sealed record ConnectionTarget(string MachineName, string ProfileKey, string Server, string Database, bool IntegratedSecurity);

public sealed class ConnectionSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string? machineNameOverride;
    private readonly string? settingsPathOverride;

    // machineNameOverride/settingsPathOverride exist so tests can resolve a fixture
    // without relying on this machine's name; the app uses the defaults.
    public ConnectionSettingsProvider(string? machineNameOverride = null, string? settingsPathOverride = null)
    {
        this.machineNameOverride = machineNameOverride;
        this.settingsPathOverride = settingsPathOverride;
    }

    /// Windows computer name of this device. The shared section is "Default".
    public string MachineName =>
        string.IsNullOrWhiteSpace(machineNameOverride) ? Environment.MachineName : machineNameOverride.Trim();

    public string SettingsPath => settingsPathOverride ?? Path.Combine(AppContext.BaseDirectory, "connectionsettings.json");

    /// Resolves the profile for this PC. The file is read on every call, so a corrected
    /// connectionsettings.json is picked up on the next sign-in attempt without a restart.
    public ConnectionProfile GetProfile() => Resolve().Profile;

    public ConnectionTarget GetTarget()
    {
        var resolved = Resolve();
        var profile = resolved.Profile;
        return new ConnectionTarget(MachineName, resolved.Key, profile.Server.Trim(), profile.Database.Trim(), profile.IntegratedSecurity);
    }

    /// Backwards-compatible entry point used by UserRepository.
    public string GetConnectionString() => BuildConnectionString(Resolve().Profile);

    private (ConnectionProfile Profile, string Key) Resolve()
    {
        var root = ReadSettings(out var path);
        var machines = new Dictionary<string, ConnectionProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in root.Machines ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null) machines[pair.Key.Trim()] = pair.Value;
        }

        // A PC-specific section wins over the shared one, so a client that must use a
        // different login or port can override only what differs. Everything else, and
        // every machine without its own section, uses Default — same folder on every PC.
        if (machines.TryGetValue(MachineName, out var own) && HasServer(own)) return (Validate(own, MachineName), MachineName);
        if (root.Default is not null && HasServer(root.Default)) return (Validate(root.Default, ConnectionSettings.DefaultKey), ConnectionSettings.DefaultKey);

        // One message for every failure shape: it names the device (so the operator
        // knows which PC to fix), points at Default as the usual fix, and lists the
        // sections that do exist. No connection string or secret is included.
        throw new ConnectionConfigurationException(
            $"لا يوجد قسم اتصال صالح لهذا الجهاز ({MachineName}) في {path}. " +
            $"اضبط Server في القسم \"{ConnectionSettings.DefaultKey}\" (يُستخدم على كل الأجهزة) " +
            "أو أضف قسمًا باسم هذا الجهاز داخل Machines. " +
            $"الأقسام الموجودة: {DescribeKeys(root)}.");
    }

    private static string DescribeKeys(ConnectionSettings root)
    {
        var keys = new List<string>();
        if (root.Default is not null) keys.Add(ConnectionSettings.DefaultKey);
        keys.AddRange((root.Machines ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key)).Select(p => p.Key.Trim()));
        return keys.Count == 0 ? "لا توجد" : string.Join("، ", keys);
    }

    private static bool HasServer(ConnectionProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.Server)
        && !profile.Server.Contains("SERVER_NAME", StringComparison.OrdinalIgnoreCase);

    private ConnectionSettings ReadSettings(out string path)
    {
        path = SettingsPath;
        if (!File.Exists(path))
        {
            throw new ConnectionConfigurationException(
                "ملف الإعدادات connectionsettings.json غير موجود بجوار البرنامج. تأكد من وجوده في نفس مجلد التشغيل.");
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<ConnectionSettings>(json, JsonOptions)
                ?? throw new ConnectionConfigurationException(
                    "ملف connectionsettings.json فارغ أو غير صالح. أضف قسم Default يحمل عنوان الخادم.");

            // Backwards compatibility: files written for the original single-machine
            // layout keep Server/Database/... at the root. Treat that as Default so an
            // already-deployed client folder does not need editing after an upgrade,
            // and so GetConnectionString() keeps returning the same string as before.
            if (root.Default is null && (root.Machines is null || root.Machines.Count == 0))
            {
                var legacy = JsonSerializer.Deserialize<ConnectionProfile>(json, JsonOptions);
                if (legacy is not null && !string.IsNullOrWhiteSpace(legacy.Server)) root.Default = legacy;
            }

            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ConnectionConfigurationException(
                "تعذر قراءة ملف connectionsettings.json. تأكد من صحة صيغة JSON (واكتب الشرطة العكسية مزدوجة \\\\ عند وجود Instance).", ex);
        }
    }

    private static ConnectionProfile Validate(ConnectionProfile settings, string profileKey)
    {
        if (string.IsNullOrWhiteSpace(settings.Database))
        {
            throw new ConnectionConfigurationException(
                $"اسم قاعدة البيانات (Database) غير محدد في القسم [{profileKey}] داخل ملف connectionsettings.json.");
        }

        if (settings.ConnectTimeoutSeconds is < 1 or > 60)
        {
            throw new ConnectionConfigurationException(
                $"مهلة الاتصال (ConnectTimeoutSeconds) في القسم [{profileKey}] يجب أن تكون بين 1 و60 ثانية.");
        }

        if (!settings.IntegratedSecurity && string.IsNullOrWhiteSpace(settings.UserId))
        {
            throw new ConnectionConfigurationException(
                $"عند ضبط IntegratedSecurity على false في القسم [{profileKey}] يجب تحديد اسم مستخدم SQL (UserId) وكلمة المرور في connectionsettings.json.");
        }

        return settings;
    }

    private static string BuildConnectionString(ConnectionProfile settings)
    {
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
