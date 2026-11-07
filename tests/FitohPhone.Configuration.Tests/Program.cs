using System.Text.Json;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

// Proves the multi-device LAN contract without Windows or SQL Server:
// one shared folder, several PCs, each resolving the right section of
// connectionsettings.json, plus compatibility with the old flat file.
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

string temp = Path.Combine(Path.GetTempPath(), "fitohphone-conf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    // ---------- 1. Legacy flat file (already deployed clients) ----------
    string legacyPath = Path.Combine(temp, "legacy.json");
    File.WriteAllText(legacyPath, """
    {
      "Server": "192.168.1.10,1433",
      "Database": "FitohPhone",
      "IntegratedSecurity": false,
      "UserId": "FitohPhoneClient",
      "Password": "secret",
      "Encrypt": true,
      "TrustServerCertificate": true,
      "ConnectTimeoutSeconds": 15
    }
    """);
    var legacy = new ConnectionSettingsProvider("PC-A", legacyPath);
    var legacyProfile = legacy.GetProfile();
    Check(legacyProfile.Server == "192.168.1.10,1433", "legacy flat file still opens the same server");
    Check(legacy.GetTarget().ProfileKey == "Default", "legacy flat file is treated as Default");
    var legacyBuilder = new SqlConnectionStringBuilder(legacy.GetConnectionString());
    Check(legacyBuilder.ApplicationName == "FitohPhone", "connection string keeps ApplicationName for session tracking");
    Check(legacyBuilder.DataSource == "192.168.1.10,1433", "legacy DataSource parsed with port, comma syntax");
    Check(legacyBuilder.IntegratedSecurity == false && legacyBuilder.UserID == "FitohPhoneClient",
        "legacy SQL authentication preserved");

    // ---------- 2. Shared Default + per-machine override ----------
    string lanPath = Path.Combine(temp, "lan.json");
    File.WriteAllText(lanPath, """
    {
      "Default": {
        "Server": "192.168.1.10,1433",
        "Database": "FitohPhone",
        "IntegratedSecurity": false,
        "UserId": "FitohPhoneClient",
        "Password": "secret",
        "Encrypt": true,
        "TrustServerCertificate": true,
        "ConnectTimeoutSeconds": 15
      },
      "Machines": {
        "MAIN-PC": {
          "Server": "192.168.1.10,1433",
          "Database": "FitohPhone",
          "IntegratedSecurity": true,
          "UserId": "",
          "Password": "",
          "Encrypt": true,
          "TrustServerCertificate": true,
          "ConnectTimeoutSeconds": 15
        }
      }
    }
    """);

    var mainPc = new ConnectionSettingsProvider("MAIN-PC", lanPath);
    var client1 = new ConnectionSettingsProvider("CLIENT-01", lanPath);
    var client2 = new ConnectionSettingsProvider("CLIENT-02", lanPath);

    Check(mainPc.GetTarget().ProfileKey == "MAIN-PC", "MAIN-PC uses its own section");
    Check(mainPc.GetProfile().IntegratedSecurity, "MAIN-PC can use Windows authentication");
    Check(client1.GetTarget().ProfileKey == "Default", "CLIENT-01 falls back to the shared section");
    Check(client2.GetTarget().ProfileKey == "Default", "CLIENT-02 falls back to the shared section");
    Check(!client1.GetProfile().IntegratedSecurity, "clients use the SQL login from Default");
    Check(client1.GetTarget().Server == client2.GetTarget().Server
          && client1.GetTarget().Server == mainPc.GetTarget().Server,
        "all three devices resolve the same server: 192.168.1.10,1433");
    Check(client1.GetTarget().Database == mainPc.GetTarget().Database, "all three devices resolve FitohPhone");
    Check(new ConnectionSettingsProvider("main-pc", lanPath).GetTarget().ProfileKey == "MAIN-PC",
        "machine-name lookup is case-insensitive");
    Check(new ConnectionSettingsProvider(" MAIN-PC ", lanPath).GetTarget().ProfileKey == "MAIN-PC",
        "machine-name lookup ignores surrounding whitespace");

    // A client may override only the server, keeping Default for the rest.
    string partialPath = Path.Combine(temp, "partial.json");
    File.WriteAllText(partialPath, """
    {
      "Default": {
        "Server": "192.168.1.10,1433", "Database": "FitohPhone",
        "IntegratedSecurity": false, "UserId": "FitohPhoneClient", "Password": "secret"
      },
      "Machines": {
        "LAPTOP-HR": {
          "Server": "192.168.1.11,1433", "Database": "FitohPhone",
          "IntegratedSecurity": false, "UserId": "FitohPhoneClient", "Password": "secret"
        }
      }
    }
    """);
    var laptop = new ConnectionSettingsProvider("LAPTOP-HR", partialPath);
    Check(laptop.GetTarget().Server == "192.168.1.11,1433", "a device can point at a second server (override)");
    Check(laptop.GetProfile().ConnectTimeoutSeconds == 15, "unspecified fields fall back to model defaults");

    // ---------- 3. Failures stay readable and secret-free ----------
    string noServer = Path.Combine(temp, "noserver.json");
    File.WriteAllText(noServer, """{ "Default": { "Server": "", "Database": "FitohPhone" } }""");
    var brokenTarget = new ConnectionSettingsProvider("CLIENT-01", noServer);
    Check(Throws<ConnectionConfigurationException>(() => brokenTarget.GetProfile()),
        "empty Server is rejected");
    string emptyMessage = CaptureMessage(() => brokenTarget.GetProfile());
    Check(emptyMessage.Contains("CLIENT-01"), "error names the device so LAN setup can be fixed");
    Check(emptyMessage.Contains("Default"), "error lists the sections present");

    string badJson = Path.Combine(temp, "bad.json");
    File.WriteAllText(badJson, "{ not json ");
    Check(Throws<ConnectionConfigurationException>(() => new ConnectionSettingsProvider("PC", badJson).GetProfile()),
        "invalid JSON is reported as a configuration error");

    string missing = Path.Combine(temp, "missing.json");
    Check(Throws<ConnectionConfigurationException>(() => new ConnectionSettingsProvider("PC", missing).GetProfile()),
        "missing file is reported as a configuration error");

    string badTimeout = Path.Combine(temp, "timeout.json");
    File.WriteAllText(badTimeout, """{ "Default": { "Server": "s", "Database": "d", "ConnectTimeoutSeconds": 0 } }""");
    Check(Throws<ConnectionConfigurationException>(() => new ConnectionSettingsProvider("PC", badTimeout).GetProfile()),
        "out-of-range ConnectTimeoutSeconds rejected");

    string noUser = Path.Combine(temp, "nouser.json");
    File.WriteAllText(noUser, """{ "Default": { "Server": "s", "Database": "d", "IntegratedSecurity": false, "UserId": "" } }""");
    Check(Throws<ConnectionConfigurationException>(() => new ConnectionSettingsProvider("PC", noUser).GetProfile()),
        "SQL authentication requires UserId");

    // Serializing the model must round-trip, so a generated/template file stays valid.
    var roundTrip = JsonSerializer.Deserialize<FitohPhone.Models.ConnectionSettings>(
        JsonSerializer.Serialize(new FitohPhone.Models.ConnectionSettings
        {
            Default = new FitohPhone.Models.ConnectionProfile { Server = "192.168.1.10,1433" },
            Machines = new() { ["PC-1"] = new FitohPhone.Models.ConnectionProfile { Server = "192.168.1.12,1433" } }
        }))!;
    Check(roundTrip.Machines!["PC-1"].Server == "192.168.1.12,1433", "model round-trips Machines entries");

    Console.WriteLine("ALL PASS: multi-device connection settings");
}
finally
{
    Directory.Delete(temp, true);
}

static bool Throws<T>(Action action) where T : Exception
{
    try { action(); return false; }
    catch (T) { return true; }
}

static string CaptureMessage(Action action)
{
    try { action(); return ""; }
    catch (Exception ex) { return ex.Message; }
}
