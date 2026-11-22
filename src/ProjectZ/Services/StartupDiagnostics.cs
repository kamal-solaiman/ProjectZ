using System.IO;

namespace ProjectZ.Services;

/// <summary>Persist unexpected startup failures without recording usernames, passwords or SQL commands.</summary>
public sealed class MainWindowInitializationException(string stage, Exception innerException)
    : Exception($"تم التحقق من بيانات الدخول، لكن فشلت تهيئة النافذة الرئيسية أثناء: {stage}.", innerException)
{
    public string Stage { get; } = stage;
}

public static class StartupDiagnostics
{
    public static string LogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectZ", "Diagnostics", "startup-errors.log");

    public static void Record(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(LogFilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent the login error from being shown.
        }
    }
}
