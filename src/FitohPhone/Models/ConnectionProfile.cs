namespace FitohPhone.Models;

/// One connection target inside connectionsettings.json: either the shared
/// "Default" section, or a per-machine override. Same keys as the original
/// single-machine file, so existing deployments keep working.
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
