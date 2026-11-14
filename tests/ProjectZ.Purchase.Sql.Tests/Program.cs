using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;

// This harness is deliberately unable to use the application's LAN settings. Run.ps1 creates
// a disposable LocalDB instance; both the instance name and Windows authentication are checked.
var adminConnectionString = Environment.GetEnvironmentVariable("PROJECTZ_TEST_ADMIN_CONNECTION")
    ?? throw new InvalidOperationException("Run tests/ProjectZ.Purchase.Sql.Tests/Run.ps1 on Windows.");
var adminBuilder = new SqlConnectionStringBuilder(adminConnectionString);
if (!adminBuilder.DataSource.Equals(@"(localdb)\ProjectZPurchaseTests", StringComparison.OrdinalIgnoreCase)
    || !adminBuilder.IntegratedSecurity
    || !adminBuilder.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("SQL tests only accept the disposable ProjectZPurchaseTests LocalDB instance.");

using var admin = new SqlConnection(adminConnectionString);
admin.Open();
var repositoryReady = false;
try
{
    foreach (var script in new[] { "01_CreateDatabase.sql", "02_SeedAdmin.sql", "06_CreateFullSchema.sql", "07_CreateReportingViews.sql" })
        RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", script));

    // No production login/password is read or changed. This login exists only in the disposable DB.
    var password = "Aa1!" + Guid.NewGuid().ToString("N") + "#Z";
    Execute(admin, $"CREATE LOGIN [ProjectZClient] WITH PASSWORD = '{password.Replace("'", "''")}', CHECK_POLICY = OFF;");
    Execute(admin, "CREATE USER [ProjectZClient] FOR LOGIN [ProjectZClient]; GRANT CONNECT TO [ProjectZClient];");
    foreach (var script in new[] { "09_EnablePosSales.sql", "11_EnableReturnExchange.sql", "12_EnablePurchasing.sql" })
        RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", script));

    var userId = Scalar<int>(admin, "SELECT Id FROM dbo.Users WHERE Username=N'admin';");
    var categoryId = Convert.ToInt32(Scalar<object>(admin, @"
INSERT dbo.Categories(Name) OUTPUT inserted.Id VALUES(N'اختبار المشتريات');"));
    var ordinaryProductId = Convert.ToInt32(Scalar<object>(admin, @"
INSERT dbo.Products(CategoryId,Barcode,Name,UnitName,CostPrice,TrackSerialNumber)
OUTPUT inserted.Id VALUES(@Category,N'PUR-TEST-ORDINARY',N'صنف شراء عادي',N'قطعة',10,0);", ("@Category", categoryId)));
    var serialProductId = Convert.ToInt32(Scalar<object>(admin, @"
INSERT dbo.Products(CategoryId,Barcode,Name,UnitName,CostPrice,TrackSerialNumber)
OUTPUT inserted.Id VALUES(@Category,N'PUR-TEST-SERIAL',N'صنف شراء بسيريال',N'قطعة',20,1);", ("@Category", categoryId)));

    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "connectionsettings.json"), JsonSerializer.Serialize(new ConnectionSettings
    {
        Server = adminBuilder.DataSource,
        Database = "ProjectZ",
        IntegratedSecurity = false,
        UserId = "ProjectZClient",
        Password = password,
        Encrypt = true,
        TrustServerCertificate = true,
        ConnectTimeoutSeconds = 30
    }));
    var settings = new ConnectionSettingsProvider();
    var repository = new PurchaseRepository(settings, userId);
    repositoryReady = true;
    var supplier = repository.CreateSupplier("مورد اختبار المشتريات", "01000000000", "", "شركة اختبار",
        "هواتف", "عنوان اختبار", "01000000000", "test@instapay", "بيانات اختبار مؤقتة");
    Check(supplier.Id > 0, "Supplier can be created with the limited SQL client grants");

    var ordinary = new PurchaseLineRequest(ordinaryProductId, "PUR-TEST-ORDINARY", "صنف شراء عادي",
        "اختبار المشتريات", "قطعة", false, 2, 100.25m);
    var serialized = new PurchaseLineRequest(serialProductId, "PUR-TEST-SERIAL", "صنف شراء بسيريال",
        "اختبار المشتريات", "قطعة", true, 1, 200.50m);
    PurchaseInvoiceRequest Request(decimal discount, decimal cash, decimal vodafone, decimal instaPay, string notes) =>
        new(supplier.Id, "SUP-REF-001", new DateTime(2026, 11, 14, 10, 30, 0), discount,
            [ordinary, serialized], cash, vodafone, instaPay, notes);

    // Exercise the actual Microsoft.Data.SqlClient session, not an SSMS/sqlcmd session whose SET
    // defaults differ. Print only option names/values, never the connection string or password.
    using (var raw = new SqlConnection(settings.GetConnectionString()))
    {
        raw.Open();
        using var options = raw.CreateCommand();
        options.CommandText = "DBCC USEROPTIONS;";
        using var reader = options.ExecuteReader();
        Console.WriteLine("INFO: Microsoft.Data.SqlClient session defaults:");
        while (reader.Read()) Console.WriteLine($"  {reader.GetString(0)} = {reader.GetString(1)}");
    }
    SqlConnection.ClearAllPools();

    var partial = repository.CreateInvoice(Request(10m, 50m, 25m, 0m, "ملاحظة شراء عربية"));
    Check(partial.InvoiceId > 0 && partial.InvoiceNumber == $"PUR-{DateTime.Now.Year}-0001", "Purchase saves with the driver's normal session and gets a real sequence number");
    Check(partial.NetTotal == 391m && partial.PaidTotal == 75m && partial.RemainingPayable == 316m, "Discount, split payment and remaining payable are exact");
    VerifyInvoice(admin, partial.InvoiceId, supplier.Id, userId, 391m, 75m, 316m, PurchaseInvoiceStatus.PartialCredit);
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == -75m, "Cash and wallet balances are debited once");
    Check(Scalar<decimal>(admin, $"SELECT CostPrice FROM dbo.Products WHERE Id={ordinaryProductId};") == 100.25m, "Product cost is synchronized");
    Console.WriteLine("PASS: header, lines, units, links, stock movements, payments, ledger, activity, audit and balances agree");

    var credit = repository.CreateInvoice(Request(0m, 0m, 0m, 0m, "آجل بالكامل"));
    VerifyInvoice(admin, credit.InvoiceId, supplier.Id, userId, 401m, 0m, 401m, PurchaseInvoiceStatus.FullCredit);
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.PurchasePayments WHERE PurchaseInvoiceId={credit.InvoiceId};") == 0, "Full-credit purchase creates no zero-value payment");
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == -75m, "Full-credit purchase does not change balances");

    var paid = repository.CreateInvoice(Request(1m, 100m, 100m, 200m, "سداد كامل"));
    VerifyInvoice(admin, paid.InvoiceId, supplier.Id, userId, 400m, 400m, 0m, PurchaseInvoiceStatus.Paid);
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == -475m, "All three payment channels are posted exactly once");

    var invoices = new ObservableCollection<PurchaseInvoiceDesignItem>();
    repository.LoadInvoicesInto(invoices);
    Check(invoices.Count == 3 && invoices.All(i => i.Items.Count == 2 && i.Items.Sum(l => l.UnitRegistrationIds.Count) == 3), "Saved purchases reload with their real line/unit links");
    Check(invoices.Single(i => i.Id == partial.InvoiceId).Notes == "ملاحظة شراء عربية", "Arabic notes survive the SQL round trip");

    // Edit an existing line: this exercises the separate UPDATE command's @Id binding, adds one
    // stock unit, and posts only the payment difference through the shared ledger/activity helpers.
    var loadedPartial = invoices.Single(i => i.Id == partial.InvoiceId);
    var editedLines = loadedPartial.Items.Select(l => new PurchaseLineEdit(
        l.DatabaseLineId, l.ProductId, l.Barcode, l.ProductName, l.Category, l.Unit, l.TrackSerialNumber,
        l.ProductId == ordinaryProductId ? 3 : l.Quantity,
        l.ProductId == ordinaryProductId ? 110m : l.UnitPurchaseCost)).ToList();
    repository.UpdateInvoice(new PurchaseInvoiceEdit(partial.InvoiceId, supplier.Id, "SUP-REF-EDIT",
        "ملاحظة بعد التعديل", 10.5m, 100m, 50m, 0m, editedLines));
    Check(Scalar<int>(admin, $"SELECT Quantity FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId={partial.InvoiceId} AND ProductId={ordinaryProductId};") == 3
        && Scalar<decimal>(admin, $"SELECT UnitPurchaseCost FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId={partial.InvoiceId} AND ProductId={ordinaryProductId};") == 110m,
        "Editing an existing purchase line binds its real Id and persists quantity/cost");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.PurchaseInvoiceLineUnits lu JOIN dbo.PurchaseInvoiceLines l ON l.Id=lu.PurchaseInvoiceLineId WHERE l.PurchaseInvoiceId={partial.InvoiceId};") == 4,
        "Editing quantity adds exactly one linked stock unit");
    Check(Scalar<decimal>(admin, $"SELECT SUM(CASE WHEN PaymentKind=1 THEN Amount ELSE -Amount END) FROM dbo.PurchasePayments WHERE PurchaseInvoiceId={partial.InvoiceId};") == 150m,
        "Editing payments posts only the channel differences");
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == -550m,
        "Edited payment differences adjust balances exactly once");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.PurchaseInvoiceActivities WHERE PurchaseInvoiceId={partial.InvoiceId} AND ActorUserId={userId};") == 2,
        "Edit activity binds the acting user");

    repository.PayInvoice(new PurchasePaymentRequest(credit.InvoiceId, 100.25m, 0m, 0m, "سداد لاحق"));
    Check(Scalar<decimal>(admin, $"SELECT SUM(Amount) FROM dbo.PurchasePayments WHERE PurchaseInvoiceId={credit.InvoiceId} AND PaymentKind=1;") == 100.25m,
        "A later supplier payment is persisted");
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == -650.25m,
        "A later supplier payment debits the account once");

    // A pooled physical connection can retain incompatible SET options. Every repository checkout
    // must restore the options required by the filtered indexes on Products/InventoryUnits.
    using (var raw = new SqlConnection(settings.GetConnectionString()))
    {
        raw.Open();
        Execute(raw, "SET ANSI_NULLS OFF; SET QUOTED_IDENTIFIER OFF; SET ANSI_PADDING OFF; SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SET CONCAT_NULL_YIELDS_NULL OFF; SET NUMERIC_ROUNDABORT ON;");
    }
    var afterBadSession = repository.CreateInvoice(Request(0m, 0m, 0m, 0m, "اختبار خيارات الجلسة"));
    VerifyInvoice(admin, afterBadSession.InvoiceId, supplier.Id, userId, 401m, 0m, 401m, PurchaseInvoiceStatus.FullCredit);
    Console.WriteLine("PASS: purchase repairs incompatible SET options on a reused pooled connection");

    // Fail at the LAST write, after stock and money rows exist, to prove the entire invoice is atomic.
    var beforeFailure = Counts(admin);
    var balanceBeforeFailure = Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;");
    var sequenceBeforeFailure = Scalar<long>(admin, $"SELECT LastValue FROM dbo.DocumentSequences WHERE DocumentType=N'PurchaseInvoice' AND SequenceYear={DateTime.Now.Year};");
    Execute(admin, @"
CREATE TRIGGER dbo.TR_PurchaseTest_FailLastWrite ON dbo.PurchaseInvoiceActivities AFTER INSERT AS
BEGIN
    THROW 50099, N'Purchase integration test: deliberate final-write failure.', 1;
END;");
    try
    {
        var failed = false;
        try { repository.CreateInvoice(Request(0m, 10m, 0m, 0m, "يجب ألا تُحفظ جزئيًا")); }
        catch (PurchaseDataException ex)
        {
            failed = ex.InnerException is SqlException { Number: 50099 };
            Console.WriteLine($"INFO: late failure reported as: {ex.Message}");
        }
        Check(failed, "A late SQL failure is surfaced as a purchase error with the original SQL error attached");
        Check(Counts(admin) == beforeFailure, "Late failure rolls back every invoice, stock, payment, ledger and audit row");
        Check(Scalar<long>(admin, $"SELECT LastValue FROM dbo.DocumentSequences WHERE DocumentType=N'PurchaseInvoice' AND SequenceYear={DateTime.Now.Year};") == sequenceBeforeFailure, "Late failure also rolls back the document sequence");
        Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == balanceBeforeFailure, "Late failure restores every account balance");
    }
    finally { Execute(admin, "DROP TRIGGER dbo.TR_PurchaseTest_FailLastWrite;"); }

    Execute(admin, "DENY INSERT ON OBJECT::dbo.PurchaseInvoiceActivities TO [ProjectZClient];");
    try
    {
        var beforeDenied = Counts(admin);
        var denied = false;
        try { repository.CreateInvoice(Request(0m, 0m, 0m, 0m, "اختبار الصلاحية")); }
        catch (PurchaseDataException ex)
        {
            denied = ex.InnerException is SqlException { Number: 229 or 230 };
            Check(ex.Message.Contains("sql/12_EnablePurchasing.sql", StringComparison.Ordinal), "Missing grant points to the purchasing permissions script");
        }
        Check(denied && Counts(admin) == beforeDenied, "Missing INSERT permission fails atomically with an actionable message");
    }
    finally
    {
        Execute(admin, "REVOKE INSERT ON OBJECT::dbo.PurchaseInvoiceActivities FROM [ProjectZClient];");
        RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", "12_EnablePurchasing.sql"));
    }
    var afterRepair = repository.CreateInvoice(Request(0m, 0m, 0m, 0m, "بعد استعادة الصلاحية"));
    Check(afterRepair.InvoiceId > 0, "Purchase succeeds after the least-privilege grant is restored");
    Console.WriteLine("PASS: all purchase SQL integration checks passed on a disposable SQL Server instance");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    if (ex.InnerException is SqlException sql)
        Console.Error.WriteLine($"SQL {sql.Number}, state {sql.State}, line {sql.LineNumber}: {sql.Message}");
    throw;
}
finally
{
    SqlConnection.ClearAllPools();
    if (repositoryReady)
    {
        admin.ChangeDatabase("master");
        Execute(admin, "IF DB_ID(N'ProjectZ') IS NOT NULL BEGIN ALTER DATABASE [ProjectZ] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [ProjectZ]; END;");
        Execute(admin, "IF EXISTS (SELECT 1 FROM sys.sql_logins WHERE name=N'ProjectZClient') DROP LOGIN [ProjectZClient];");
    }
    var temporarySettings = Path.Combine(AppContext.BaseDirectory, "connectionsettings.json");
    if (File.Exists(temporarySettings)) File.Delete(temporarySettings);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS: " + name);
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectZ.sln")))
        directory = directory.Parent;
    return directory?.FullName ?? throw new InvalidOperationException("ProjectZ repository root not found.");
}

static void RunScript(SqlConnection connection, string path)
{
    foreach (var batch in Regex.Split(File.ReadAllText(path), @"(?m)^\s*GO\s*(?:--.*)?$", RegexOptions.IgnoreCase))
    {
        if (!string.IsNullOrWhiteSpace(batch)) Execute(connection, batch);
    }
    Console.WriteLine("PASS: executed " + Path.GetFileName(path));
}

static void Execute(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
{
    using var command = connection.CreateCommand();
    command.CommandTimeout = 120;
    command.CommandText = sql;
    foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
    command.ExecuteNonQuery();
}

static T Scalar<T>(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
{
    using var command = connection.CreateCommand();
    command.CommandTimeout = 120;
    command.CommandText = sql;
    foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
    return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
}

static string Counts(SqlConnection connection) => string.Join(",", new[]
{
    "PurchaseInvoices", "PurchaseInvoiceLines", "PurchaseInvoiceLineUnits", "InventoryUnits", "StockMovements",
    "PurchasePayments", "FinancialTransactions", "PurchaseInvoiceActivities", "AuditLog", "DocumentSequences"
}.Select(table => Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.{table};")));

static void VerifyInvoice(SqlConnection connection, int invoiceId, int supplierId, int userId,
    decimal net, decimal paid, decimal remaining, PurchaseInvoiceStatus status)
{
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.PurchaseInvoices WHERE Id={invoiceId} AND SupplierId={supplierId} AND BuyerUserId={userId} AND InvoiceStatus={(byte)status};") == 1, $"Invoice {invoiceId}: header/status/actor");
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId={invoiceId};") == 2, $"Invoice {invoiceId}: two lines");
    Check(Scalar<decimal>(connection, $"SELECT SUM(Quantity * UnitPurchaseCost) FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId={invoiceId};") - Scalar<decimal>(connection, $"SELECT DiscountAmount FROM dbo.PurchaseInvoices WHERE Id={invoiceId};") == net, $"Invoice {invoiceId}: net total");
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.PurchaseInvoiceLineUnits lu JOIN dbo.PurchaseInvoiceLines l ON l.Id=lu.PurchaseInvoiceLineId JOIN dbo.InventoryUnits u ON u.Id=lu.InventoryUnitId WHERE l.PurchaseInvoiceId={invoiceId} AND u.ProductId=l.ProductId AND u.PurchaseCost=l.UnitPurchaseCost AND u.UnitStatus=0;") == 3, $"Invoice {invoiceId}: three correctly linked stock units");
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.InventoryUnits WHERE PurchaseInvoiceNumber=(SELECT InvoiceNumber FROM dbo.PurchaseInvoices WHERE Id={invoiceId}) AND SerialNumber IS NOT NULL;") == 1, $"Invoice {invoiceId}: one unique generated serial");
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.StockMovements WHERE SourceEntityType=N'PurchaseInvoice' AND SourceEntityId={invoiceId} AND MovementType=N'Supply' AND QuantityDelta=1 AND InventoryUnitId IS NOT NULL;") == 3, $"Invoice {invoiceId}: three stock receipts");
    var paidSql = $"SELECT ISNULL(SUM(CASE WHEN PaymentKind=1 THEN Amount ELSE -Amount END),0) FROM dbo.PurchasePayments WHERE PurchaseInvoiceId={invoiceId}";
    Check(Scalar<decimal>(connection, paidSql) == paid, $"Invoice {invoiceId}: payment total");
    Check(net - paid == remaining, $"Invoice {invoiceId}: remaining payable");
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.FinancialTransactions WHERE SourceEntityType=N'PurchaseInvoice' AND SourceEntityId={invoiceId} AND IsIncoming=0;") == (paid == 0m ? 0 : paid == 75m ? 2 : 3), $"Invoice {invoiceId}: ledger rows are paired to payment channels");
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.PurchaseInvoiceActivities WHERE PurchaseInvoiceId={invoiceId} AND ActorUserId={userId};") == 1, $"Invoice {invoiceId}: activity trail");
    Check(Scalar<int>(connection, $"SELECT COUNT(*) FROM dbo.AuditLog WHERE Module=N'Purchasing' AND ActionName=N'CreatePurchaseInvoice' AND EntityType=N'PurchaseInvoice' AND EntityId=CONVERT(NVARCHAR(100),{invoiceId});") == 1, $"Invoice {invoiceId}: audit trail");
}
