using System.Data;
using System.Globalization;
using System.IO;
using ProjectZ.Models;
using ProjectZ.Services;
using Microsoft.Data.SqlClient;

namespace ProjectZ.Data;

/// <summary>خطأ يُعرض لك المستخدم في شاشة النسخ الاحتياطي والاسترجاع.</summary>
public sealed class BackupDataException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>
/// نسخ احتياطي لقاعدة بيانات ProjectZ واسترجاعها مباشرة من SQL Server عبر أوامر
/// <c>BACKUP DATABASE</c> و <c>RESTORE DATABASE</c>. مسار الملف يُرسل إلى SQL Server ويُقيَّم هناك،
/// لذا يعمل مع مسار محلي على الخادم أو مشاركة UNC؛ واسم القاعدة يُثبت من الإعدادات ولا يُقبل من المستخدم.
/// </summary>
public sealed class BackupRepository(ConnectionSettingsProvider settings)
{
    // لا مهلة للأوامر: أخذ النسخة والاسترجاع قد يستغرقان وقتًا طويلًا حسب حجم القاعدة.
    private const int CommandTimeoutSeconds = 0;

    /// <summary>
    /// اسم القاعدة الفعلي من connectionsettings.json، وليس ثابتًا. كل شاشات البرنامج تتصل
    /// بالقاعدة Configured هناك، والنسخ الاحتياطي يجب أن يتبعها بدل قاعدة ProjectZ مفترضة.
    /// </summary>
    private string DatabaseName
    {
        get
        {
            var builder = new SqlConnectionStringBuilder(settings.GetConnectionString());
            var name = builder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(name))
                throw new BackupDataException("اسم قاعدة البيانات (Database) غير محدد في connectionsettings.json.");

            return name.Trim();
        }
    }

    /// <summary>يضع اسم القاعدة بين أقواس SQL مع تهريب القوس المغلق؛ الاسم مقروء من الإعدادات لا من المستخدم.</summary>
    private static string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    /// <summary>معلومات عن قاعدة البيانات الحالية للعرض في الشاشة.</summary>
    public sealed class DatabaseInfo
    {
        public long SizeBytes { get; init; }
        public string SizeDisplay => FormatSize(SizeBytes);
        // backup_start_date is written by SQL Server in the server's local time, not UTC. Converting
        // it again adds the client offset and shows a misleading timestamp.
        public DateTime? LastBackupAt { get; init; }
        public string LastBackupDisplay => LastBackupAt is { } last
            ? last.ToString("yyyy/MM/dd HH:mm", CultureInfo.CurrentCulture)
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

        DateTime? lastBackupAt = null;
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
                lastBackupAt = reader.GetDateTime(0);
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            // جدول msdb.dbo.backupset قد يكون غير متاح لهذا الحساب؛ لا تُوقف الشاشة بسببه، بل تُعرض "غير معروف".
        }

        return new DatabaseInfo { SizeBytes = sizeBytes, LastBackupAt = lastBackupAt };
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

            // اسم القاعدة مقروء من connectionsettings.json؛ المسار يمر كمعامل سيكور (SqlParameter) لحماية المسار من حقن الأوامر.
            using var command = connection.CreateCommand();
            command.CommandText = @$"
BACKUP DATABASE {QuoteIdentifier(DatabaseName)}
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
            // The target database is about to go SINGLE_USER and be replaced. A connection whose
            // Initial Catalog is the configured database would itself keep that database busy, so
            // the restore runs from master exactly like SSMS does.
            var masterConnectionString = new SqlConnectionStringBuilder(settings.GetConnectionString())
            {
                InitialCatalog = "master"
            }.ConnectionString;
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync(cancellationToken);

            try
            {
                // قطع الاتصالات النشطة حتى لا تفشل الاستعادة بسبب قفل القاعدة.
                using (var singleUser = connection.CreateCommand())
                {
                    singleUser.CommandText = @$"
ALTER DATABASE {QuoteIdentifier(DatabaseName)}
SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
                    singleUser.CommandTimeout = CommandTimeoutSeconds;
                    await singleUser.ExecuteNonQueryAsync(cancellationToken);
                }

                using (var restore = connection.CreateCommand())
                {
                    restore.CommandText = @$"
RESTORE DATABASE {QuoteIdentifier(DatabaseName)}
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
ALTER DATABASE {QuoteIdentifier(DatabaseName)}
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

    /// <summary>يتحقق من شكل المسار ويضمن امتداد .bak؛ وجود المجلد يُترك لـ SQL Server.</summary>
    private static string NormalizeBackupPath(string backupFilePath, bool requireExistingDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath))
            throw new BackupDataException("يرجى تحديد مسار ملف النسخة الاحتياطية أولاً.");

        var path = backupFilePath.Trim();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new BackupDataException("مسار الملف غير صالح.");

        // Kept for source compatibility: paths are evaluated by SQL Server, not by this client.
        _ = requireExistingDirectory;

        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".bak", StringComparison.OrdinalIgnoreCase))
            throw new BackupDataException("يجب أن ينتهي اسم ملف النسخة الاحتياطية بامتداد .bak.");

        return path;
    }

    private static BackupDataException Describe(Exception exception, string fallback)
    {
        if (exception is BackupDataException data) return data;
        if (exception is ConnectionConfigurationException configuration)
            return new BackupDataException(configuration.Message, configuration);
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                229 or 230 or 262 => new BackupDataException(
                    "حساب الاتصال لا يملك صلاحية النسخ الاحتياطي أو الاسترجاع على قاعدة البيانات. " +
                    "منح حساب ProjectZClient دور db_owner على قاعدة البيانات المضبوطة ودور dbcreator على الخادم (انظر docs/BACKUP.md).",
                    sql),
                3101 => new BackupDataException("فشل النسخ الاحتياطي: لا يمكن كتابة ملف النسخة في المسار المحدد.", sql),
                3201 => new BackupDataException(
                    "تعذّر على SQL Server فتح مسار ملف النسخة. المسار يُقرأ على جهاز SQL Server نفسه، وليس على جهاز العميل؛ " +
                    "تأكد من وجود المجلد على الخادم أو من مشاركة UNC يصل إليها حساب خدمة SQL Server.", sql),
                3202 => new BackupDataException("تعذّر على SQL Server قراءة ملف النسخة الاحتياطية. تحقق من المسار وصلاحية القراءة.", sql),
                3204 => new BackupDataException("تعذّر تجهيز جهاز النسخ الاحتياطي على SQL Server. تحقق من المسار والمساحة المتاحة.", sql),
                3203 => new BackupDataException("فشل استرجاع النسخة: ملف النسخة الاحتياطي تالف أو غير صحيح.", sql),
                3013 => new BackupDataException($"فشلت عملية SQL Server: {sql.Message}", sql),
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
