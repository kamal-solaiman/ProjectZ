using System.Text.Json.Serialization;

namespace FitohPhone.Models;

// One file, several clients (LAN): a shared "Default" section plus optional
// per-machine overrides keyed by Windows computer name. A machine without an
// override uses Default, so every PC on the network runs the same folder.
public sealed class ConnectionSettings
{
    public const string DefaultKey = "Default";

    public ConnectionProfile? Default { get; set; }

    // Keys are Windows computer names, case-insensitive. Lookup is try-exact,
    // then fall back to "Default".
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ConnectionProfile>? Machines { get; set; }
}

// Mirrors the original flat connectionsettings.json file so an existing
// single-machine deployment keeps working unchanged.
public sealed class ConnectionProfile
{
    public string Server { get; set; } = "";
    public string Database { get; set; } = "FitohPhone";
    public bool IntegratedSecurity { get; set; } = true;
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectTimeoutSeconds { get; set; } = 15;
}
