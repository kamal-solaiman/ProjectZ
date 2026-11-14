using System.IO;
using System.Text.Json;
using ProjectZ.Models;

namespace ProjectZ.Services;

public sealed record AppearanceLoadResult(AppearanceSettings Settings, string? Warning = null);

/// <summary>
/// Atomic, per-Windows-user persistence. Deliberately independent of SQL and the roaming profile:
/// a disconnected client still opens with its last saved theme, and display preferences do not
/// change other devices or enter database backups.
/// </summary>
public sealed class AppearanceStore(string? filePath = null)
{
    private const long MaximumFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectZ", "appearance.json");

    public string FilePath { get; } = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
    private string BackupPath => FilePath + ".bak";

    public AppearanceLoadResult Load()
    {
        if (!File.Exists(FilePath))
            return File.Exists(BackupPath) ? ReadBackup("الملف الأساسي غير موجود؛ تم استرجاع آخر مظهر محفوظ.") : new(new());

        try
        {
            return new(Read(FilePath));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return ReadBackup("تعذرت قراءة إعدادات المظهر؛ تم استرجاع النسخة الاحتياطية أو المظهر الافتراضي.");
        }
    }

    /// <summary>Validates before touching disk. The previous good file is retained as .bak.</summary>
    public void Save(AppearanceSettings settings)
    {
        ThemeCatalog.Validate(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
                File.Replace(temporary, FilePath, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(temporary, FilePath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (IsRecoverable(ex)) { /* A later save can clean up its own temporary file. */ }
        }
    }

    private AppearanceLoadResult ReadBackup(string warning)
    {
        try { return new(Read(BackupPath), warning); }
        catch (Exception ex) when (IsRecoverable(ex)) { return new(new(), warning); }
    }

    private static AppearanceSettings Read(string path)
    {
        if (new FileInfo(path).Length > MaximumFileBytes)
            throw new InvalidDataException("ملف المظهر أكبر من الحد المسموح.");
        var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("ملف المظهر فارغ.");
        ThemeCatalog.Validate(settings);
        return settings with { CustomPalette = NormalizePalette(settings.CustomPalette) };
    }

    private static ThemePalette NormalizePalette(ThemePalette palette)
    {
        static string Normal(string color) => color.ToUpperInvariant();
        return new(Normal(palette.Background), Normal(palette.Surface), Normal(palette.Text), Normal(palette.MutedText),
            Normal(palette.Accent), Normal(palette.Border), Normal(palette.Highlight), Normal(palette.Sidebar));
    }

    private static bool IsRecoverable(Exception exception) => exception is IOException or UnauthorizedAccessException
        or JsonException or FormatException or ArgumentException or InvalidDataException;
}
