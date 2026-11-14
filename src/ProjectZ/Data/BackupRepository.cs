using System.Globalization;
using System.IO;
using System.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using Microsoft.Data.SqlClient;

namespace ProjectZ.Data;

/// <summary>خطأ يُعرض لك المستخدم في شاشة النسخ الاحتياطي والاسترجاع.</summary>
public sealed class BackupDataException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>
/// نسخ احتياطي لقاعدة بيانات ProjectZ واسترجاعها مباشرة من SQL Server عبر أوامر
/// <c>BACKUP DATABASE</c> و <c>RESTORE DATABASE</c>. جميع مسارات الملفات تُتحقق منها من جانب العميل
/// قبل إرسال أي أمر إلى قاعدة البيانات، واسم القاعدة يُثبت من الإعدادات ولا يُقبل من المستخدم.
/// </summary>
public sealed class BackupRepository(ConnectionSettingsProvider settings)
{
    private const string DatabaseName = "ProjectZ";
    // لا مهلة للأوامر: أخذ النسخة والاسترجاع قد يستغرقان وقتًا طويلًا حسب حجم القاعدة.
    private const int CommandTimeoutSeconds = 0;

    /// <summary>معلومات عن قاعدة البيانات الحالية للعرض في الشاشة.</summary>
    public sealed class DatabaseInfo
    {
        public long SizeBytes { get; init; }
        public string SizeDisplay => FormatSize(SizeBytes);
        public DateTime? LastBackupUtc { get; init; }
        public string LastBackupDisplay => LastBackupUtc is { } last
            ? last.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.CurrentCulture)
            : "غير معروف";
    }

    /// <summary>ملف نسخ احتياطي موجود في مجلد النسخ.</summary>
    public sealed class BackupFileInfo
    {
        public string FullPath { get; init; } = "";
        public string Name { get; init; } = "";
        public DateTime LastWriteTime { get; init; }
        public long Length { get; init; }
        public string LengthDisplay => FormatSize(Length);
        public string LastWriteDisplay => LastWriteTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.CurrentCulture);
    }

    private static string FormatSize(long bytes)
    {
        var sizes = new[] { "بايت", "كيلوبايت", "ميجابايت", "جيجابايت", "تيرابايت" };
        var order = 0;
        var size = (double)bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            size /= 1024;
            order++;
        }

        return $"{size:N2} {sizes[order]}";
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.GetConnectionString());
        connection.Open();
        return connection;
    }

    /// <summary>حجم قاعدة البيانات وآخر نسخة احتياطية مسجلة في جدول msdb.dbo.backupset (إن كان متاحًا).</summary>
    public DatabaseInfo GetDatabaseInfo()
    {
        long sizeBytes;
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT ISNULL(SUM(size * 8192), 0)
FROM sys.database_files;";
            command.CommandTimeout = CommandTimeoutSeconds;
            sizeBytes = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر قراءة حجم قاعدة البيانات من SQL Server.");
        }

        DateTime? lastBackupUtc = null;
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT TOP 1 backup_start_date
FROM msdb.dbo.backupset
WHERE database_name = @DatabaseName
ORDER BY backup_start_date DESC;";
            command.CommandTimeout = CommandTimeoutSeconds;
            command.Parameters.Add(new SqlParameter("@DatabaseName", SqlDbType.NVarChar, 128) { Value = DatabaseName });
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var value = reader.GetDateTime(0);
                lastBackupUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            // جدول msdb.dbo.backupset قد يكون غير متاح لهذا الحساب؛ لا تُوقف الشاشة بسببه، بل تُعرض "غير معروف".
        }

        return new DatabaseInfo { SizeBytes = sizeBytes, LastBackupUtc = lastBackupUtc };
    }

    /// <summary>ملفات النسخ الاحتياطي (*.bak) في المجلد المحدد، الأحدث أولاً.</summary>
    public List<BackupFileInfo> GetAvailableBackups(string folderPath)
    {
        var files = new List<BackupFileInfo>();
        try
        {
            if (!Directory.Exists(folderPath)) return files;

            foreach (var file in Directory.GetFiles(folderPath, "*.bak", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                files.Add(new BackupFileInfo
                {
                    FullPath = file,
                    Name = info.Name,
                    LastWriteTime = info.LastWriteTime,
                    Length = info.Length
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // المجلد غير متاح: تُعرض القائمة فارغة ويُبلغ المستخدم عند محاولة الاسترجاع.
        }

        files.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
        return files;
    }

    /// <summary>
    /// يأخذ نسخة احتياطية كاملة من قاعدة البيانات إلى المسار المحدد، مع استبدال أي ملف موجود بنفس الاسم.
    /// </summary>
    public async Task BackupDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizeBackupPath(backupFilePath, requireExistingDirectory: true);

        try
        {
            await using var connection = new SqlConnection(settings.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            // اسم القاعدة ثابت من الإعدادات؛ المسار يمر كمعامل سيكور (SqlParameter) لحماية المسار من حقن الأوامر.
            using var command = connection.CreateCommand();
            command.CommandText = @$"
BACKUP DATABASE [{DatabaseName}]
TO DISK = @Path
WITH INIT, FORMAT, CHECKSUM;";
            command.CommandTimeout = CommandTimeoutSeconds;
            command.Parameters.Add(new SqlParameter("@Path", SqlDbType.NVarChar, 260) { Value = path });

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw Describe(ex, "تعذر أخذ النسخة الاحتياطية من قاعدة البيانات.");
        }
    }

    /// <summary>
    /// يسترجع قاعدة البيانات من ملف النسخ الاحتياطي المحدد. يُعزل القاعدة أولاً (SINGLE_USER)
    /// ثم يُسترجع الملف بـ REPLACE, RECOVERY وتُعاد القاعدة إلى وضع مستخدمين متعددين عند الانتهاء.
    /// </summary>
    public async Task RestoreDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        var path = NormalizeBackupPath(backupFilePath, requireExistingDirectory: false);

        try
        {
            await using var connection = new SqlConnection(settings.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            try
            {
                // قطع الاتصالات النشطة حتى لا تفشل الاستعادة بسبب قفل القاعدة.
                using (var singleUser = connection.CreateCommand())
                {
                    singleUser.CommandText = @$"
ALTER DATABASE [{DatabaseName}]
SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
                    singleUser.CommandTimeout = CommandTimeoutSeconds;
                    await singleUser.ExecuteNonQueryAsync(cancellationToken);
                }

                using (var restore = connection.CreateCommand())
                {
                    restore.CommandText = @$"
RESTORE DATABASE [{DatabaseName}]
FROM DISK = @Path
WITH REPLACE, RECOVERY;";
                    restore.CommandTimeout = CommandTimeoutSeconds;
                    restore.Parameters.Add(new SqlParameter("@Path", SqlDbType.NVarChar, 260) { Value = path });
                    await restore.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            finally
            {
                try
                {
                    using var multiUser = connection.CreateCommand();
                    multiUser.CommandText = @$"
ALTER DATABASE [{DatabaseName}]
SET MULTI_USER;";
                    multiUser.CommandTimeout = CommandTimeoutSeconds;
                    await multiUser.ExecuteNonQueryAsync(cancellationToken);
                }
                catch
                {
                    // إذا تعثر إعادة القاعدة إلى وضع مستخدمين متعددين، الاستعادة نفسها قد تكون ناجحة؛
                    // تُهمل هذه الخطوة لئلا تُخفي الخطأ الأصلي.
                }
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw Describe(ex, "تعذر استرجاع النسخة الاحتياطية من قاعدة البيانات.");
        }
    }

    /// <summary>يتحقق من المسار ويضمن امتداد .bak وم Presence المجلد عند المطالبة.</summary>
    private static string NormalizeBackupPath(string backupFilePath, bool requireExistingDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath))
            throw new BackupDataException("يرجى تحديد مسار ملف النسخة الاحتياطية أولاً.");

        var path = backupFilePath.Trim();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new BackupDataException("مسار الملف غير صالح.");

        if (requireExistingDirectory && !Directory.Exists(directory))
            throw new BackupDataException($"المجلد غير موجود: {directory}");

        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".bak", StringComparison.OrdinalIgnoreCase))
            throw new BackupDataException("يجب أن ينتهي اسم ملف النسخة الاحتياطية بامتداد .bak.");

        return path;
    }

    private static BackupDataException Describe(Exception exception, string fallback)
    {
        if (exception is BackupDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                229 or 230 => new BackupDataException(
                    "حساب الاتصال لا يملك صلاحية النسخ الاحتياطي أو الاسترجاع على قاعدة البيانات. " +
                    "منح حساب ProjectZClient دور db_owner على قاعدة ProjectZ ودور dbcreator على الخادم (انظر docs/BACKUP.md).",
                    sql),
                3101 => new BackupDataException("فشل النسخ الاحتياطي: لا يمكن كتابة ملف النسخة في المسار المحدد.", sql),
                3203 => new BackupDataException("فشل استرجاع النسخة: ملف النسخة الاحتياطي تالف أو غير صحيح.", sql),
                3159 => new BackupDataException("فشل الاسترجاع لأن القاعدة مستخدمة من مستخدمين آخرين. أغلق الشاشات الأخرى وحاول مرة أخرى.", sql),
                18456 => new BackupDataException("فشل تسجيل الدخول إلى SQL Server. تأكد من اسم المستخدم وكلمة المرور في connectionsettings.json.", sql),
                2 => new BackupDataException("تعذر الاتصال بخادم SQL Server. تأكد من عنوان الخادم (Server) في connectionsettings.json.", sql),
                53 => new BackupDataException("تعذر الوصول إلى خادم SQL Server. تأكد من تشغيل الخدمة وعنوان الخادم صحيح.", sql),
                _ => new BackupDataException(fallback, sql)
            };
        }

        return new BackupDataException(fallback, exception);
    }
}
