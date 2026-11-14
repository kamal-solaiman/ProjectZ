using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the settings layer; the message is safe to show in the UI.</summary>
public sealed class SettingsDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>
/// Reads and writes the store identity and general defaults in dbo.SystemSettings.
/// The settings live in key/value rows, so adding a field later needs no schema change; the store name key
/// (<c>StoreName</c>) is the same value every window uses as the program name.
/// </summary>
public sealed class SettingsRepository(ConnectionSettingsProvider settings, int actorUserId = 0)
{
    public const string StoreNameKey = "StoreName";
    public const string StoreTaglineKey = "StoreTagline";
    public const string StoreAddressKey = "StoreAddress";
    public const string StorePhoneKey = "StorePhone";
    public const string StorePhoneAltKey = "StorePhoneAlt";
    public const string StoreEmailKey = "StoreEmail";
    public const string ServiceCenterKey = "ServiceCenterName";
    public const string FooterNoteKey = "PrintFooterNote";
    public const string CurrencyLabelKey = "CurrencyNameAr";
    public const string CurrencyCodeKey = "CurrencyCode";
    public const string LowStockAlertKey = "DefaultLowStockAlert";

    private const int MaxNameLength = 60;
    private const int MaxTaglineLength = 80;
    private const int MaxAddressLength = 200;
    private const int MaxPhoneLength = 30;
    private const int MaxEmailLength = 150;
    private const int MaxServiceCenterLength = 100;
    private const int MaxFooterLength = 200;
    private const int MaxCurrencyLabelLength = 10;

    // -------------------------------------------------------------- read side

    /// <summary>
    /// Loads the store profile. Missing keys fall back to the ProjectZ defaults, so the screen works on a
    /// database where sql/15_EnableGeneralSettings.sql was not run yet (saving is what needs the grant).
    /// </summary>
    public StoreProfile Load()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SettingKey, SettingValue FROM dbo.SystemSettings;";
            using var reader = command.ExecuteReader();

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
                values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);

            string Text(string key, string fallback, int maxLength) =>
                values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
                    ? Clamp(raw.Trim(), maxLength)
                    : fallback;

            var lowStock = StoreProfile.DefaultLowStockAlertValue;
            if (values.TryGetValue(LowStockAlertKey, out var rawLow)
                && int.TryParse(rawLow, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLow)
                && parsedLow >= 0 && parsedLow <= 100_000)
                lowStock = parsedLow;

            return new StoreProfile
            {
                StoreName = Text(StoreNameKey, StoreProfile.DefaultStoreName, MaxNameLength),
                Tagline = Text(StoreTaglineKey, StoreProfile.DefaultTagline, MaxTaglineLength),
                Address = Text(StoreAddressKey, StoreProfile.DefaultAddress, MaxAddressLength),
                Phone = Text(StorePhoneKey, StoreProfile.DefaultPhone, MaxPhoneLength),
                PhoneAlt = Text(StorePhoneAltKey, StoreProfile.DefaultPhoneAlt, MaxPhoneLength),
                Email = Text(StoreEmailKey, StoreProfile.DefaultEmail, MaxEmailLength),
                ServiceCenterName = Text(ServiceCenterKey, StoreProfile.DefaultServiceCenter, MaxServiceCenterLength),
                FooterNote = Text(FooterNoteKey, StoreProfile.DefaultFooterNote, MaxFooterLength),
                CurrencyLabel = Text(CurrencyLabelKey, StoreProfile.DefaultCurrencyLabel, MaxCurrencyLabelLength),
                CurrencyCode = Text(CurrencyCodeKey, StoreProfile.DefaultCurrencyCode, 10),
                DefaultLowStockAlert = lowStock
            };
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SettingsDataException))
        {
            throw Describe(ex, "تعذر تحميل الإعدادات من قاعدة البيانات.");
        }
    }

    // ------------------------------------------------------------- write side

    /// <summary>
    /// Saves the whole settings sheet in one transaction. Only rows whose value actually changed are
    /// written, so re-saving an untouched form is a no-op and the audit line names the real changes.
    /// </summary>
    public StoreProfile Save(StoreProfile profile)
    {
        var clean = Validate(profile);
        var changes = new List<string>();

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var stored = LoadValues(connection, transaction);

                Upsert(connection, transaction, stored, changes, StoreNameKey, clean.StoreName, "string",
                    "اسم المحل واسم البرنامج في كل الشاشات والمطبوعات");
                Upsert(connection, transaction, stored, changes, StoreTaglineKey, clean.Tagline, "string",
                    "وصف مختصر يظهر بجوار اسم المحل");
                Upsert(connection, transaction, stored, changes, StoreAddressKey, clean.Address, "string",
                    "عنوان المحل في المطبوعات");
                Upsert(connection, transaction, stored, changes, StorePhoneKey, clean.Phone, "string", "هاتف المحل");
                Upsert(connection, transaction, stored, changes, StorePhoneAltKey, clean.PhoneAlt, "string",
                    "رقم هاتف إضافي");
                Upsert(connection, transaction, stored, changes, StoreEmailKey, clean.Email, "string",
                    "بريد المحل الإلكتروني");
                Upsert(connection, transaction, stored, changes, ServiceCenterKey, clean.ServiceCenterName, "string",
                    "اسم قسم الصيانة في إيصالات الاستلام");
                Upsert(connection, transaction, stored, changes, FooterNoteKey, clean.FooterNote, "string",
                    "عبارة أسفل الفواتير والإيصالات");
                Upsert(connection, transaction, stored, changes, CurrencyLabelKey, clean.CurrencyLabel, "string",
                    "Arabic currency label");
                Upsert(connection, transaction, stored, changes, CurrencyCodeKey, clean.CurrencyCode, "string",
                    "ISO currency code");
                Upsert(connection, transaction, stored, changes, LowStockAlertKey,
                    clean.DefaultLowStockAlert.ToString(CultureInfo.InvariantCulture), "integer",
                    "حد التنبيه الافتراضي للمخزون عند إضافة صنف");

                if (changes.Count > 0)
                    Audit(connection, transaction, clean, changes);

                transaction.Commit();
                return clean;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SettingsDataException))
        {
            throw Describe(ex, "تعذر حفظ الإعدادات في قاعدة البيانات.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static Dictionary<string, string?> LoadValues(SqlConnection connection, SqlTransaction transaction)
    {
        using var command = CreateCommand(connection, transaction,
            "SELECT SettingKey, SettingValue FROM dbo.SystemSettings WITH (UPDLOCK, HOLDLOCK);");
        using var reader = command.ExecuteReader();

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return values;
    }

    private void Upsert(
        SqlConnection connection,
        SqlTransaction transaction,
        Dictionary<string, string?> stored,
        List<string> changes,
        string key,
        string value,
        string valueType,
        string description)
    {
        var exists = stored.TryGetValue(key, out var current);
        if (exists && string.Equals((current ?? "").Trim(), value, StringComparison.Ordinal))
            return;

        using var command = CreateCommand(connection, transaction, exists
            ? @"UPDATE dbo.SystemSettings
SET SettingValue=@Value, ValueType=@Type, UpdatedAtUtc=SYSUTCDATETIME(), UpdatedByUserId=@Actor
WHERE SettingKey=@Key;"
            : @"INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description, UpdatedByUserId)
VALUES(@Key, @Value, @Type, @Description, @Actor);");

        command.Parameters.Add("@Key", SqlDbType.NVarChar, 150).Value = key;
        command.Parameters.Add("@Value", SqlDbType.NVarChar, -1).Value = value;
        command.Parameters.Add("@Type", SqlDbType.NVarChar, 30).Value = valueType;
        object actor = actorUserId > 0 ? actorUserId : DBNull.Value;
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actor;

        if (!exists)
            command.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = description;

        command.ExecuteNonQuery();
        stored[key] = value;
        changes.Add($"{key} = {value}");
    }

    private void Audit(SqlConnection connection, SqlTransaction transaction, StoreProfile saved, List<string> changes)
    {
        var details = Clamp("تعديل الإعدادات العامة: " + string.Join(" · ", changes), 1000);
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'Settings',N'UpdateGeneralSettings',N'SystemSetting',N'General',
       JSON_MODIFY(N'{}',N'$.storeName',@StoreName));");
        object actor = actorUserId > 0 ? actorUserId : DBNull.Value;
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actor;
        command.Parameters.Add("@StoreName", SqlDbType.NVarChar, 160).Value = details;
        command.ExecuteNonQuery();
    }

    private static StoreProfile Validate(StoreProfile profile)
    {
        var name = Require(profile.StoreName, MaxNameLength, "اسم المحل", allowEmpty: false);
        return new StoreProfile
        {
            StoreName = name,
            Tagline = Require(profile.Tagline, MaxTaglineLength, "الوصف المختصر", allowEmpty: true),
            Address = Require(profile.Address, MaxAddressLength, "العنوان", allowEmpty: true),
            Phone = Require(profile.Phone, MaxPhoneLength, "رقم الهاتف", allowEmpty: true),
            PhoneAlt = Require(profile.PhoneAlt, MaxPhoneLength, "رقم الهاتف الإضافي", allowEmpty: true),
            Email = Require(profile.Email, MaxEmailLength, "البريد الإلكتروني", allowEmpty: true),
            ServiceCenterName = Require(profile.ServiceCenterName, MaxServiceCenterLength, "اسم قسم الصيانة",
                allowEmpty: true),
            FooterNote = Require(profile.FooterNote, MaxFooterLength, "عبارة أسفل الفاتورة", allowEmpty: true),
            CurrencyLabel = Require(profile.CurrencyLabel, MaxCurrencyLabelLength, "رمز العملة", allowEmpty: true),
            CurrencyCode = Require(profile.CurrencyCode, 10, "كود العملة", allowEmpty: true),
            DefaultLowStockAlert = profile.DefaultLowStockAlert
        };
    }

    private static string Require(string? value, int maxLength, string label, bool allowEmpty)
    {
        var trimmed = (value ?? "").Trim();
        if (!allowEmpty && trimmed.Length == 0)
            throw new SettingsDataException($"يرجى إدخال {label} أولًا.");
        if (trimmed.Length > maxLength)
            throw new SettingsDataException($"{label} يجب ألا يتجاوز {maxLength} حرفًا.");
        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
            throw new SettingsDataException($"{label} يجب أن يكون في سطر واحد.");
        return trimmed;
    }

    private static string Clamp(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static SettingsDataException Describe(Exception exception, string fallback)
    {
        if (exception is SettingsDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                229 or 230 => new SettingsDataException(
                    "حساب الاتصال لا يملك صلاحية قراءة/حفظ الإعدادات. شغّل sql/15_EnableGeneralSettings.sql على الخادم ثم أعد المحاولة.",
                    sql),
                1205 => new SettingsDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                208 => new SettingsDataException(
                    "جدول dbo.SystemSettings غير موجود. نفّذ sql/06_CreateFullSchema.sql على الخادم.", sql),
                >= 50000 => new SettingsDataException(sql.Message, sql),
                _ => new SettingsDataException(fallback, sql)
            };
        }

        return new SettingsDataException(fallback, exception);
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.GetConnectionString());
        connection.Open();
        return connection;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };
}
