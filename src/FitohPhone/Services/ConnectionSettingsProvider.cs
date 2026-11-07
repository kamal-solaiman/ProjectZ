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

/// Resolves the connection profile for THIS device out of one shared settings file.
///
/// File shape (see docs/LAN-SETUP.md):
///   { "Default": { Server, Database, ... }, "Machines": { "MAIN-PC": { ... } } }
///
/// Rules:
///   * A "Machines" entry named like this PC is layered ON TOP of "Default", so a
///     device overrides only what differs and inherits the rest.
///   * A device with no entry uses "Default" unchanged — the normal LAN case.
///   * A legacy flat file (Server at the root) is treated as "Default".
/// The file is read on every call, so fixing it is picked up on the next sign-in
/// attempt without restarting the application.
public sealed class ConnectionSettingsProvider
{
    public const string DefaultKey = "Default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string? machineNameOverride;
    private readonly string? settingsPathOverride;

    // The overrides exist so tests can resolve a fixture without depending on the
    // name of the machine running them; the application uses the defaults.
    public ConnectionSettingsProvider(string? machineNameOverride = null, string? settingsPathOverride = null)
    {
        this.machineNameOverride = machineNameOverride;
        this.settingsPathOverride = settingsPathOverride;
    }

    /// Windows computer name of this device; "Default" is the shared section.
    public string MachineName =>
        string.IsNullOrWhiteSpace(machineNameOverride) ? Environment.MachineName : machineNameOverride.Trim();

    public string SettingsPath => settingsPathOverride ?? Path.Combine(AppContext.BaseDirectory, "connectionsettings.json");

    public ConnectionProfile GetProfile() => Resolve().Profile;

    public ConnectionTarget GetTarget()
    {
        var resolved = Resolve();
        var profile = resolved.Profile;
        return new ConnectionTarget(MachineName, resolved.Key, profile.Server.Trim(), profile.Database.Trim(), profile.IntegratedSecurity);
    }

    public string GetConnectionString() => BuildConnectionString(Resolve().Profile);

    private (ConnectionProfile Profile, string Key) Resolve()
    {
        var path = SettingsPath;
        var sections = ReadSections(path);

        // Device-specific section first: merged over the shared one. The reported key
        // is the section name as written in the file, so the login window matches it.
        if (sections.Own is JsonElement own)
        {
            var merged = Merge(sections.Shared, own);
            if (HasServer(merged)) return (Validate(merged, sections.OwnName), sections.OwnName);
        }

        if (sections.Shared is JsonElement shared)
        {
            var profile = ToProfile(shared);
            if (HasServer(profile)) return (Validate(profile, DefaultKey), DefaultKey);
        }

        // One message for every failure shape: it names the device (so the operator
        // knows which PC to fix), points at Default as the usual fix, and lists the
        // sections that exist. No connection string or secret is included.
        var known = new List<string> { DefaultKey };
        known.AddRange(sections.MachineNames);
        throw new ConnectionConfigurationException(
            $"لا يوجد قسم اتصال صالح لهذا الجهاز ({MachineName}) في {path}. " +
            $"اضبط Server في القسم \"{DefaultKey}\" (يُستخدم على كل الأجهزة) " +
            "أو أضف قسمًا باسم هذا الجهاز داخل Machines. " +
            $"الأقسام الموجودة: {string.Join("، ", known)}.");
    }

    private sealed record Sections(JsonElement? Shared, JsonElement? Own, string OwnName, List<string> MachineNames);

    private Sections ReadSections(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConnectionConfigurationException(
                "ملف الإعدادات connectionsettings.json غير موجود بجوار البرنامج. تأكد من وجوده في نفس مجلد التشغيل.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConnectionConfigurationException(
                "تعذر قراءة ملف connectionsettings.json. راجع صلاحيات الملف.", ex);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ConnectionConfigurationException(
                "ملف connectionsettings.json فارغ. أضف قسم \"Default\" يحمل عنوان الخادم.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ConnectionConfigurationException(
                    "صيغة connectionsettings.json غير صحيحة: يجب أن يكون كائن JSON يحتوي \"Default\".");
            }

            JsonElement? shared = null;
            if (TryFindProperty(root, DefaultKey, out var defaultElement) && defaultElement.ValueKind == JsonValueKind.Object)
            {
                shared = defaultElement.Clone();
            }
            else if (!TryFindProperty(root, "Machines", out _))
            {
                // Backwards compatibility: the original single-machine file keeps
                // Server/Database/... at the root. Treat that object as "Default" so
                // already-deployed client folders work after an upgrade untouched.
                shared = root.Clone();
            }

            JsonElement? own = null;
            var ownName = "";
            var machineNames = new List<string>();
            var machineName = MachineName;
            if (TryFindProperty(root, "Machines", out var machines) && machines.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in machines.EnumerateObject())
                {
                    var name = entry.Name.Trim();
                    if (name.Length == 0 || entry.Value.ValueKind != JsonValueKind.Object) continue;
                    machineNames.Add(name);
                    if (string.Equals(name, machineName, StringComparison.OrdinalIgnoreCase))
                    {
                        own = entry.Value.Clone();
                        ownName = name;
                    }
                }
            }

            return new Sections(shared, own, ownName, machineNames);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new ConnectionConfigurationException(
                "تعذر تحليل ملف connectionsettings.json. تأكد من صحة صيغة JSON (واكتب الشرطة العكسية مزدوجة \\\\ عند وجود Instance).", ex);
        }
    }

    private static bool TryFindProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // Overlay the device's properties on the shared ones. Only keys the device
    // section actually writes win; null means "inherit". Without this inheritance a
    // section setting just "Server" would silently take the model defaults — e.g.
    // Windows authentication instead of the shared SQL login.
    private static ConnectionProfile Merge(JsonElement? shared, JsonElement own)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (shared is JsonElement sharedSection)
        {
            foreach (var property in sharedSection.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Null) merged[property.Name] = property.Value;
            }
        }

        foreach (var property in own.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Null) merged[property.Name] = property.Value;
        }

        return merged.Count == 0 ? new ConnectionProfile() : ToProfile(JsonSerializer.SerializeToElement(merged));
    }

    private static ConnectionProfile ToProfile(JsonElement element)
    {
        try
        {
            return element.Deserialize<ConnectionProfile>(JsonOptions) ?? new ConnectionProfile();
        }
        catch (JsonException ex)
        {
            // Wrong JSON *types* (e.g. "Encrypt": "true" instead of true) are a common
            // hand-editing mistake; report them as configuration, never as a crash.
            throw new ConnectionConfigurationException(
                "توجد قيمة بنوع غير صحيح في connectionsettings.json. راجع أنواع الحقول: " +
                "Encrypt وTrustServerCertificate وIntegratedSecurity منطقية (true/false)، وConnectTimeoutSeconds رقم.", ex);
        }
    }

    private static bool HasServer(ConnectionProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.Server)
        && !profile.Server.Contains("SERVER_NAME", StringComparison.OrdinalIgnoreCase);

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
