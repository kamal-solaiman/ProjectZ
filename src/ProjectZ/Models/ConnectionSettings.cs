namespace ProjectZ.Models;

public sealed class ConnectionSettings
{
    public string Server { get; set; } = "";
    public string Database { get; set; } = "ProjectZ";
    public bool IntegratedSecurity { get; set; } = true;
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Base64 AES-256 key used to protect device passcodes stored in dbo.MaintenanceOrders.
    /// Kept outside the database on purpose; the PROJECTZ_PASSCODE_KEY environment variable wins
    /// over this value when both are set. Empty means "no protection" (the UI warns about it).
    /// </summary>
    public string PasscodeKey { get; set; } = "";
}
