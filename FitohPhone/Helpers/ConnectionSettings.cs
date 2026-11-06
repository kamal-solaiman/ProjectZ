using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FitohPhone.Helpers;

/// <summary>
/// Connection settings loaded from the external appsettings.json file
/// that sits next to the EXE. The server address is NOT hard-coded in code.
/// </summary>
public sealed class ConnectionSettings
{
    private const string SettingsFileName = "appsettings.json";

    [JsonPropertyName("Server")]
    public string Server { get; set; } = @"localhost\SQLEXPRESS";

    [JsonPropertyName("Database")]
    public string Database { get; set; } = "FitohPhone";

    /// <summary>True = Windows Integrated Security, False = SQL Server Authentication.</summary>
    [JsonPropertyName("IntegratedSecurity")]
    public bool IntegratedSecurity { get; set; } = true;

    [JsonPropertyName("UserId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("Password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("TrustServerCertificate")]
    public bool TrustServerCertificate { get; set; } = true;

    [JsonPropertyName("ConnectTimeout")]
    public int ConnectTimeout { get; set; } = 15;

    /// <summary>Wrapper matching the "ConnectionSettings" root key in appsettings.json.</summary>
    private sealed class SettingsFile
    {
        [JsonPropertyName("ConnectionSettings")]
        public ConnectionSettings? ConnectionSettings { get; set; }
    }

    public static ConnectionSettings Load(string? baseDirectory = null)
    {
        string dir = baseDirectory ?? AppContext.BaseDirectory;
        string path = Path.Combine(dir, SettingsFileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Connection settings file '{SettingsFileName}' was not found next to the executable. " +
                $"Expected location: {path}", path);
        }

        string json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<SettingsFile>(json)
                   ?? throw new InvalidDataException($"Invalid JSON in {path}");

        return file.ConnectionSettings
               ?? throw new InvalidDataException($"Missing 'ConnectionSettings' section in {path}");
    }
}
