using System.Text.Json;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

// Proves the multi-device LAN contract without Windows or SQL Server: one shared
// folder, several PCs, each resolving the right section of connectionsettings.json,
// with per-device overrides inheriting everything they do not write.
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

// Local functions cannot be overloaded, so this one helper covers every case.
// Only configuration errors are expected here: anything else must escape and fail
// the run loudly instead of being mistaken for a handled error.
bool Throws(Action action)
{
    try { action(); return false; }
    catch (ConnectionConfigurationException) { return true; }
}

string Message(Action action)
{
    try { action(); return ""; }
    catch (Exception ex) { return ex.Message; }
}

string temp = Path.Combine(Path.GetTempPath(), "fitohphone-conf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);

string Write(string name, string json)
{
    var path = Path.Combine(temp, name + ".json");
    File.WriteAllText(path, json);
    return path;
}

const string SharedSql = """
  "Server": "192.168.1.10,1433",
  "Database": "FitohPhone",
  "IntegratedSecurity": false,
  "UserId": "FitohPhoneClient",
  "Password": "secret",
  "Encrypt": true,
  "TrustServerCertificate": true,
  "ConnectTimeoutSeconds": 15
""";

try
{
    // ---------- 1. Legacy flat file (already-deployed client folder) ----------
    var legacy = new ConnectionSettingsProvider("PC-A", Write("legacy", "{" + SharedSql + "}"));
    var legacyTarget = legacy.GetTarget();
    Check(legacyTarget.ProfileKey == ConnectionSettingsProvider.DefaultKey, "legacy flat file is treated as Default");
    Check(legacyTarget.Server == "192.168.1.10,1433", "legacy flat file keeps the same server");
    var legacyBuilder = new SqlConnectionStringBuilder(legacy.GetConnectionString());
    Check(legacyBuilder.ApplicationName == "FitohPhone", "connection string keeps ApplicationName for session tracking");
    Check(legacyBuilder.DataSource == "192.168.1.10,1433", "IP,port syntax parses to DataSource");
    Check(!legacyBuilder.IntegratedSecurity && legacyBuilder.UserID == "FitohPhoneClient",
        "legacy SQL authentication preserved");

    // ---------- 2. Shared Default + per-machine override ----------
    var lanPath = Write("lan", $$"""
    {
      "Default": { {{SharedSql}} },
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
    Check(client1.GetTarget().ProfileKey == ConnectionSettingsProvider.DefaultKey, "CLIENT-01 falls back to Default");
    Check(client2.GetTarget().ProfileKey == ConnectionSettingsProvider.DefaultKey, "CLIENT-02 falls back to Default");
    Check(!client1.GetProfile().IntegratedSecurity, "clients use the SQL login from Default");
    Check(client1.GetTarget().Server == client2.GetTarget().Server && client1.GetTarget().Server == mainPc.GetTarget().Server,
        "all three devices resolve the same server: 192.168.1.10,1433");
    Check(client1.GetTarget().Database == mainPc.GetTarget().Database, "all three devices resolve FitohPhone");
    Check(new ConnectionSettingsProvider("main-pc", lanPath).GetTarget().ProfileKey == "MAIN-PC",
        "machine-name lookup is case-insensitive and reports the section as written");
    Check(new ConnectionSettingsProvider("main-pc", lanPath).GetTarget().Server == "192.168.1.10,1433",
        "case-insensitive match still uses that section's server");
    Check(new ConnectionSettingsProvider("  MAIN-PC  ", lanPath).GetTarget().ProfileKey == "MAIN-PC",
        "machine-name lookup ignores surrounding whitespace");

    // ---------- 3. An override inherits everything it does not write ----------
    // Regression: setting only Server used to fall back to the model defaults, which
    // meant IntegratedSecurity=true (Windows auth) and a broken client PC.
    var overridePath = Write("override", $$"""
    {
      "Default": { {{SharedSql}} },
      "Machines": { "LAPTOP-HR": { "Server": "192.168.1.11,1433" } }
    }
    """);
    var laptop = new ConnectionSettingsProvider("LAPTOP-HR", overridePath);
    Check(laptop.GetTarget().Server == "192.168.1.11,1433", "a device can point at a different server");
    Check(!laptop.GetProfile().IntegratedSecurity, "override inherits the shared SQL login, not Windows auth");
    Check(laptop.GetProfile().UserId == "FitohPhoneClient", "override inherits UserId from Default");
    Check(laptop.GetProfile().ConnectTimeoutSeconds == 15, "override inherits the shared timeout");
    Check(laptop.GetTarget().ProfileKey == "LAPTOP-HR", "the device section is reported as used");

    var overrideBuilder = new SqlConnectionStringBuilder(laptop.GetConnectionString());
    Check(overrideBuilder.DataSource == "192.168.1.11,1433" && overrideBuilder.UserID == "FitohPhoneClient",
        "override connection string combines own server with inherited login");
    Check(overrideBuilder.Password == "secret", "override connection string keeps the shared secret");

    // A null value means "inherit" and must not wipe the shared value.
    var nullPath = Write("nulls", $$"""
    {
      "Default": { {{SharedSql}} },
      "Machines": { "PC-NULL": { "Server": "192.168.1.12,1433", "UserId": null } }
    }
    """);
    Check(new ConnectionSettingsProvider("PC-NULL", nullPath).GetProfile().UserId == "FitohPhoneClient",
        "null in a device section inherits instead of clearing");

    // An empty Server in the device section falls back to Default.
    var emptyPath = Write("empty", $$"""
    {
      "Default": { {{SharedSql}} },
      "Machines": { "MAIN-PC": { "Server": "", "IntegratedSecurity": true } }
    }
    """);
    var emptyOwn = new ConnectionSettingsProvider("MAIN-PC", emptyPath);
    Check(emptyOwn.GetTarget().ProfileKey == ConnectionSettingsProvider.DefaultKey,
        "device section with an empty Server falls back to Default");

    // ---------- 4. Device section without Default ----------
    var onlyMachine = Write("onlymachine", """
    {
      "Machines": {
        "CLIENT-09": {
          "Server": "192.168.1.10,1433", "Database": "FitohPhone",
          "IntegratedSecurity": false, "UserId": "u", "Password": "p"
        }
      }
    }
    """);
    Check(new ConnectionSettingsProvider("CLIENT-09", onlyMachine).GetTarget().ProfileKey == "CLIENT-09",
        "a device-only file works for the listed device");
    Check(Throws(() => new ConnectionSettingsProvider("CLIENT-10", onlyMachine).GetProfile()),
        "a device-only file rejects an unlisted device");

    // ---------- 5. Failures stay readable and secret-free ----------
    var noServer = Write("noserver", """{ "Default": { "Server": "", "Database": "FitohPhone" } }""");
    string noServerMessage = Message(() => new ConnectionSettingsProvider("CLIENT-01", noServer).GetProfile());
    Check(noServerMessage.Length > 0, "empty Server is rejected");
    Check(noServerMessage.Contains("CLIENT-01"), "error names the device so LAN setup can be fixed");
    Check(!noServerMessage.Contains("secret"), "error never contains a password");

    Check(Throws(() => new ConnectionSettingsProvider("PC", Write("bad", "{ not json ")).GetProfile()),
        "invalid JSON is reported as a configuration error");
    Check(Throws(() => new ConnectionSettingsProvider("PC", Path.Combine(temp, "missing.json")).GetProfile()),
        "missing file is reported as a configuration error");
    Check(Throws(() => new ConnectionSettingsProvider("PC", Write("empty_file", "   ")).GetProfile()),
        "empty file is reported as a configuration error");
    Check(Throws(() => new ConnectionSettingsProvider("PC", Write("array", "[]")).GetProfile()),
        "non-object JSON is reported as a configuration error");
    Check(Throws(() => new ConnectionSettingsProvider("PC",
            Write("timeout", """{ "Default": { "Server": "s", "Database": "d", "ConnectTimeoutSeconds": 0 } }""")).GetProfile()),
        "out-of-range ConnectTimeoutSeconds rejected");
    Check(Throws(() => new ConnectionSettingsProvider("PC",
            Write("nouser", """{ "Default": { "Server": "s", "Database": "d", "IntegratedSecurity": false, "UserId": "" } }""")).GetProfile()),
        "SQL authentication requires UserId");
    Check(Throws(() => new ConnectionSettingsProvider("PC",
            Write("placeholder", """{ "Default": { "Server": "SERVER_NAME", "Database": "FitohPhone" } }""")).GetProfile()),
        "the SERVER_NAME placeholder is still refused");

    Check(Throws(() => new ConnectionSettingsProvider("PC",
            Write("wrongtype", """{ "Default": { "Server": "192.168.1.10,1433", "Encrypt": "yes" } }""")).GetProfile()),
        "wrong JSON value type is a configuration error, not a crash");

    // Broken Machines entries must not take the whole file down.
    var junk = Write("junk", $$"""
    {
      "Default": { {{SharedSql}} },
      "Machines": { "   ": { "Server": "10.0.0.9" }, "JUNK": "not-an-object", "PC-1": { "Server": "" } }
    }
    """);
    Check(new ConnectionSettingsProvider("PC-1", junk).GetTarget().ProfileKey == ConnectionSettingsProvider.DefaultKey,
        "blank/junk Machines entries are ignored, not fatal");

    // ---------- 6. Model round-trip ----------
    var profile = JsonSerializer.Deserialize<ConnectionProfile>(JsonSerializer.Serialize(
        new ConnectionProfile { Server = "192.168.1.12,1433" }))!;
    Check(profile.Server == "192.168.1.12,1433" && profile.Database == "FitohPhone",
        "profile round-trips with model defaults intact");

    Console.WriteLine("ALL PASS: multi-device connection settings");
}
finally
{
    Directory.Delete(temp, true);
}
