namespace FitohPhone.Models;
public sealed class ConnectionSettings
{
    public string Server { get; set; } = "";
    public string Database { get; set; } = "FitohPhone";
    public bool IntegratedSecurity { get; set; } = true;
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 8;
}
