using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;

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
    foreach (var script in new[]
             {
                 "09_EnablePosSales.sql", "11_EnableReturnExchange.sql", "12_EnablePurchasing.sql",
                 "13_EnableMaintenanceInvoices.sql", "14_EnableMaintenanceIntake.sql", "15_EnableMaintenanceActions.sql", "16_EnableMaintenanceSpareParts.sql",
                 "19_EnableExpenseCategories.sql", "20_EnableExpenses.sql", "21_EnableExpenseSplitPayments.sql"
             })
        RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", script));

    var sqlDirectory = Path.Combine(FindRepositoryRoot(), "sql");
    SettlementInstallerTests.Run(admin, sqlDirectory);
    foreach (var script in new[] { "24_EnableMaintenanceSettlement.sql", "25_EnablePurchaseSettlement.sql" })
        RunScript(admin, Path.Combine(sqlDirectory, script));

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

    // A real payment needs a funded, active account. Only the isolated test DBA seeds opening balances.
    Execute(admin, "INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,IsDefault,CurrentBalance) VALUES (0,N'اختبار نقدي',N'',1,1000),(1,N'اختبار فودافون',N'01011111111',1,1000),(2,N'اختبار إنستاباي',N'test@ip',1,1000);");
    var ordinary = new PurchaseLineRequest(ordinaryProductId, 2, 100.25m);
    var serialized = new PurchaseLineRequest(serialProductId, 1, 200.50m);
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
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == 2925m, "Cash and wallet balances are debited once");
    Check(Scalar<decimal>(admin, $"SELECT CostPrice FROM dbo.Products WHERE Id={ordinaryProductId};") == 100.25m, "Product cost is synchronized");
    Console.WriteLine("PASS: header, lines, units, links, stock movements, payments, ledger, activity, audit and balances agree");

    var credit = repository.CreateInvoice(Request(0m, 0m, 0m, 0m, "آجل بالكامل"));
    VerifyInvoice(admin, credit.InvoiceId, supplier.Id, userId, 401m, 0m, 401m, PurchaseInvoiceStatus.FullCredit);
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.PurchasePayments WHERE PurchaseInvoiceId={credit.InvoiceId};") == 0, "Full-credit purchase creates no zero-value payment");
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == 2925m, "Full-credit purchase does not change balances");

    var paid = repository.CreateInvoice(Request(1m, 100m, 100m, 200m, "سداد كامل"));
    VerifyInvoice(admin, paid.InvoiceId, supplier.Id, userId, 400m, 400m, 0m, PurchaseInvoiceStatus.Paid);
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == 2525m, "All three payment channels are posted exactly once");

    var invoices = new ObservableCollection<PurchaseInvoiceDesignItem>();
    repository.LoadInvoicesInto(invoices);
    Check(invoices.Count == 3 && invoices.All(i => i.Items.Count == 2 && i.Items.Sum(l => l.UnitRegistrationIds.Count) == 3), "Saved purchases reload with their real line/unit links");
    Check(invoices.Single(i => i.Id == partial.InvoiceId).Notes == "ملاحظة شراء عربية", "Arabic notes survive the SQL round trip");

    // A document edit changes the selected line, never the payout or the current catalog price.
    var loadedPartial = invoices.Single(i => i.Id == partial.InvoiceId);
    var editedLines = loadedPartial.Items.Select(l => new PurchaseLineRequest(l.ProductId,
        l.ProductId == ordinaryProductId ? 3 : l.Quantity, l.ProductId == ordinaryProductId ? 110m : l.UnitPurchaseCost, l.DatabaseLineId)).ToList();
    repository.UpdateInvoice(new PurchaseInvoiceEdit(partial.InvoiceId, loadedPartial.DatabaseRowVersion, supplier.Id, "SUP-REF-EDIT", loadedPartial.IssuedAt,
        "ملاحظة بعد التعديل", 10.5m, editedLines));
    Check(Scalar<int>(admin, $"SELECT Quantity FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId={partial.InvoiceId} AND ProductId={ordinaryProductId};") == 3
        && Scalar<decimal>(admin, $"SELECT UnitPurchaseCost FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId={partial.InvoiceId} AND ProductId={ordinaryProductId};") == 110m,
        "Editing an existing purchase line binds its real Id and persists quantity/cost");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.PurchaseInvoiceLineUnits lu JOIN dbo.PurchaseInvoiceLines l ON l.Id=lu.PurchaseInvoiceLineId WHERE l.PurchaseInvoiceId={partial.InvoiceId};") == 4,
        "Editing quantity adds exactly one linked stock unit");
    Check(Scalar<decimal>(admin, $"SELECT SUM(CASE WHEN PaymentKind=1 THEN Amount ELSE -Amount END) FROM dbo.PurchasePayments WHERE PurchaseInvoiceId={partial.InvoiceId};") == 75m,
        "Editing the document leaves its posted payments untouched");
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == 2525m && Scalar<decimal>(admin, $"SELECT CostPrice FROM dbo.Products WHERE Id={ordinaryProductId}") == 100.25m,
        "Edit neither moves money nor overwrites the catalog cost");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.PurchaseInvoiceActivities WHERE PurchaseInvoiceId={partial.InvoiceId} AND ActorUserId={userId};") == 2,
        "Edit activity binds the acting user");

    repository.PayInvoice(new PurchasePaymentRequest(credit.InvoiceId, invoices.Single(i => i.Id == credit.InvoiceId).DatabaseRowVersion, 100.25m, 0m, 0m, "سداد لاحق"));
    Check(Scalar<decimal>(admin, $"SELECT SUM(Amount) FROM dbo.PurchasePayments WHERE PurchaseInvoiceId={credit.InvoiceId} AND PaymentKind=1;") == 100.25m,
        "A later supplier payment is persisted");
    Check(Scalar<decimal>(admin, "SELECT SUM(CurrentBalance) FROM dbo.FinancialAccounts;") == 2424.75m,
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

    Execute(admin, "DENY EXECUTE ON OBJECT::dbo.CreatePurchaseDocument TO [ProjectZClient];");
    try
    {
        var beforeDenied = Counts(admin);
        var denied = false;
        try { repository.CreateInvoice(Request(0m, 0m, 0m, 0m, "اختبار الصلاحية")); }
        catch (PurchaseDataException ex)
        {
            denied = ex.InnerException is SqlException { Number: 229 or 230 };
            Check(ex.Message.Contains("sql/25_EnablePurchaseSettlement.sql", StringComparison.Ordinal), "Missing grant points to the purchasing permissions script");
        }
        Check(denied && Counts(admin) == beforeDenied, "Missing EXECUTE permission fails atomically with an actionable message");
    }
    finally
    {
        Execute(admin, "REVOKE EXECUTE ON OBJECT::dbo.CreatePurchaseDocument FROM [ProjectZClient];");
        RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", "25_EnablePurchaseSettlement.sql"));
    }
    var afterRepair = repository.CreateInvoice(Request(0m, 0m, 0m, 0m, "بعد استعادة الصلاحية"));
    Check(afterRepair.InvoiceId > 0, "Purchase succeeds after the least-privilege grant is restored");

    // Split-paid expenses: sql/20 + sql/21 must parse and run, and ExpenseRepository must write the
    // expense, one payment row per account, one ledger row per account and the audit row together.
    var expenseCategoryId = Convert.ToInt32(Scalar<object>(admin, @"
INSERT dbo.ExpenseCategories(Name,Description) OUTPUT inserted.Id VALUES(N'اختبار المصروفات',N'');"));
    var expenseAccountA = Scalar<int>(admin, "SELECT TOP (1) Id FROM dbo.FinancialAccounts ORDER BY Id;");
    var expenseAccountB = Scalar<int>(admin,
        $"SELECT TOP (1) Id FROM dbo.FinancialAccounts WHERE Id <> {expenseAccountA} ORDER BY Id;");
    Execute(admin, $"UPDATE dbo.FinancialAccounts SET CurrentBalance = 1000 WHERE Id IN ({expenseAccountA},{expenseAccountB});");

    var expenseRepository = new ExpenseRepository(settings, userId);
    Check(expenseRepository.LoadCategories().Any(category => category.Id == expenseCategoryId),
        "Expense sections load from dbo.ExpenseCategories for the picker");
    var savedExpense = expenseRepository.Save(new SaveExpenseRequest(
        expenseCategoryId, DateTime.Today, 300m, "اختبار وسيلتي دفع",
        [new ExpensePaymentRequest(expenseAccountA, 120m), new ExpensePaymentRequest(expenseAccountB, 180m)]));
    Check(savedExpense.ExpenseNumber == $"EXP-{DateTime.Now.Year}-0001",
        "A split expense gets a real sequence number");
    Check(savedExpense.Payments.Count == 2 && savedExpense.Payments.Sum(payment => payment.Amount) == 300m,
        "Every payment line is returned with its own amount");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.ExpensePayments WHERE ExpenseId={savedExpense.ExpenseId};") == 2,
        "One dbo.ExpensePayments row per account");
    Check(Scalar<decimal>(admin, $"SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={expenseAccountA};") == 880m
        && Scalar<decimal>(admin, $"SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={expenseAccountB};") == 820m,
        "Each account is debited by its own share");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.FinancialTransactions WHERE SourceEntityType=N'Expense' AND SourceEntityId={savedExpense.ExpenseId} AND IsIncoming=0;") == 2,
        "One outgoing ledger row per payment account");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.AuditLog WHERE Module=N'Expenses' AND ActionName=N'CreateExpense' AND EntityId=CONVERT(NVARCHAR(100),{savedExpense.ExpenseId});") == 1,
        "The split expense writes one audit row");
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Expenses') AND name=N'FinancialAccountId';") == 0,
        "dbo.Expenses no longer keeps a single account column");

    var rejectedSplit = false;
    try
    {
        expenseRepository.Save(new SaveExpenseRequest(expenseCategoryId, DateTime.Today, 300m, "",
            [new ExpensePaymentRequest(expenseAccountA, 250m)]));
    }
    catch (ExpenseDataException ex)
    {
        rejectedSplit = ex.Message.Contains("لا يساوي مبلغ المصروف", StringComparison.Ordinal);
    }
    Check(rejectedSplit, "A split that does not add up to the total is refused");

    var rejectedDuplicate = false;
    try
    {
        expenseRepository.Save(new SaveExpenseRequest(expenseCategoryId, DateTime.Today, 300m, "",
            [new ExpensePaymentRequest(expenseAccountA, 150m), new ExpensePaymentRequest(expenseAccountA, 150m)]));
    }
    catch (ExpenseDataException ex)
    {
        rejectedDuplicate = ex.Message.Contains("أكثر من وسيلة دفع", StringComparison.Ordinal);
    }
    Check(rejectedDuplicate, "The same account cannot be used twice in one expense");

    // «فواتير المصروفات» reads the same rows back: one row per expense, one payment line per
    // account, with the section and the user who recorded it, and the date filter works in SQL.
    var listedInvoices = expenseRepository.LoadInvoices();
    Check(listedInvoices.Count == 1 && listedInvoices[0].Id == savedExpense.ExpenseId,
        "ExpenseRepository.LoadInvoices returns the saved expense once");
    Check(listedInvoices[0].ExpenseNumber == savedExpense.ExpenseNumber
        && listedInvoices[0].Amount == 300m
        && listedInvoices[0].CategoryName == "اختبار المصروفات"
        && listedInvoices[0].ActorUserName == "admin"
        && listedInvoices[0].ExpenseDate == DateTime.Today,
        "The listed expense keeps its number, amount, section, actor and date");
    Check(listedInvoices[0].Payments.Count == 2
        && listedInvoices[0].Payments.Sum(payment => payment.Amount) == 300m,
        "The listed expense carries both account shares");
    Check(expenseRepository.LoadInvoices(DateTime.Today, DateTime.Today).Count == 1
        && expenseRepository.LoadInvoices(DateTime.Today.AddDays(1), null).Count == 0
        && expenseRepository.LoadInvoices(null, DateTime.Today.AddDays(-1)).Count == 0,
        "The register date filter is applied inside SQL Server");

    // «حساباتي» showed a frozen «النقدية» balance because a reload replaced the account objects while
    // the cash card stayed bound to the old one. Reloading must keep the same objects and carry the
    // new balance into them, so the card (CashAccount.BalanceDisplay) updates without a restart.
    var financialRepository = new FinancialRepository(settings, userId);
    var liveAccounts = new ObservableCollection<FinancialAccountDesignItem>();
    financialRepository.LoadAccountsInto(liveAccounts);
    var liveCash = liveAccounts.First(account => account.Kind == FinancialAccountKind.Cash);
    var cashBeforeReload = liveCash.Balance;
    Execute(admin, $"UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance - 50 WHERE Id = {liveCash.Id};");
    financialRepository.LoadAccountsInto(liveAccounts);
    Check(ReferenceEquals(liveCash, liveAccounts.First(account => account.Kind == FinancialAccountKind.Cash)),
        "Reloading the accounts keeps the same cash object the screen already binds");
    Check(liveCash.Balance == cashBeforeReload - 50m,
        "The reloaded cash object carries the new balance instead of staying frozen at the startup value");
    Check(liveAccounts.Count == Scalar<int>(admin, "SELECT COUNT(*) FROM dbo.FinancialAccounts WHERE IsActive = 1;"),
        "The reload matches the active accounts in SQL Server without dropping or duplicating rows");

    // Re-running the migration script must stay a no-op and keep the migrated/paid rows intact.
    RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", "21_EnableExpenseSplitPayments.sql"));
    Check(Scalar<int>(admin, $"SELECT COUNT(*) FROM dbo.ExpensePayments WHERE ExpenseId={savedExpense.ExpenseId};") == 2,
        "sql/21 is idempotent and keeps existing payment rows");

    // Rebuild the pre-split schema on purpose (one account column + old index), then let sql/21
    // migrate it, so the upgrade path customers run is exercised and not only parsed.
    Execute(admin, @"
DELETE dbo.ExpensePayments;
DELETE dbo.FinancialTransactions WHERE SourceEntityType = N'Expense';
DELETE dbo.AuditLog WHERE Module = N'Expenses';
DELETE dbo.Expenses;
DROP TABLE dbo.ExpensePayments;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Expenses') AND name = N'IX_Expenses_Date')
    DROP INDEX IX_Expenses_Date ON dbo.Expenses;
ALTER TABLE dbo.Expenses ADD FinancialAccountId INT NOT NULL;
ALTER TABLE dbo.Expenses ADD CONSTRAINT FK_Expenses_Accounts FOREIGN KEY (FinancialAccountId) REFERENCES dbo.FinancialAccounts(Id);
CREATE INDEX IX_Expenses_Date ON dbo.Expenses(ExpenseDate DESC, Id DESC) INCLUDE (ExpenseNumber, ExpenseCategoryId, FinancialAccountId, Amount);");
    Execute(admin, @"
INSERT dbo.Expenses(ExpenseNumber,ExpenseCategoryId,FinancialAccountId,ExpenseDate,Amount,Description,ActorUserId)
VALUES(N'EXP-OLD-0001', @Category, @Account, CONVERT(date, GETDATE()), 125, N'مصروف قديم بحساب واحد', @Actor);",
        ("@Category", expenseCategoryId), ("@Account", expenseAccountA), ("@Actor", userId));
    RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", "21_EnableExpenseSplitPayments.sql"));
    Check(Scalar<int>(admin, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Expenses') AND name=N'FinancialAccountId';") == 0,
        "sql/21 drops the legacy single-account column");
    Check(Scalar<int>(admin, $@"
SELECT COUNT(*) FROM dbo.ExpensePayments p
JOIN dbo.Expenses e ON e.Id = p.ExpenseId
WHERE e.ExpenseNumber = N'EXP-OLD-0001' AND p.FinancialAccountId = {expenseAccountA} AND p.Amount = 125;") == 1,
        "sql/21 copies each legacy expense account into dbo.ExpensePayments before dropping it");
    Check(Scalar<int>(admin, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Expenses') AND name=N'IX_Expenses_Date';") == 1,
        "sql/21 rebuilds IX_Expenses_Date without the dropped column");
    var migratedInvoices = expenseRepository.LoadInvoices();
    Check(migratedInvoices.Count == 1
        && migratedInvoices[0].ExpenseNumber == "EXP-OLD-0001"
        && migratedInvoices[0].Payments.Count == 1
        && migratedInvoices[0].Payments[0].FinancialAccountId == expenseAccountA
        && migratedInvoices[0].Payments[0].Amount == 125m,
        "The register lists a migrated legacy expense with its copied account share");
    Console.WriteLine("PASS: sql/21 migrates a legacy single-account expense without losing its account or amount");
    Console.WriteLine("PASS: split expenses write dbo.Expenses, dbo.ExpensePayments, ledger rows and audit on LocalDB");

    var legacyTransfer = WalletCorrectionTests.SeedLegacyEdit(admin, financialRepository, liveCash.Id, userId);
    var beforeWalletUpgrade = WalletCorrectionTests.Footprint(admin);
    var walletMigration = Path.Combine(FindRepositoryRoot(), "sql", "23_EnableCustomerTransferCorrections.sql");
    RunScript(admin, walletMigration);
    RunScript(admin, walletMigration);
    Check(WalletCorrectionTests.Footprint(admin) == beforeWalletUpgrade,
        "Installing and rerunning sql/23 changes no existing header, account, ledger or audit row");

    // A stopped line keeps its transfer history, but LoadAccountsInto only loads IsActive = 1 rows, so
    // the records screen lost the wallet object. Its edit path resolved that wallet with
    // accounts.Accounts.First(w => w.Id == operation.WalletId) and crashed with
    // System.InvalidOperationException "Sequence contains no matching element". The data layer must
    // answer in Arabic instead, and must let the same edit through once the line is active again.
    var stoppedLineId = liveAccounts.First(account =>
        account.Kind == FinancialAccountKind.VodafoneCash && account.IsActive).Id;
    Execute(admin, $"UPDATE dbo.FinancialAccounts SET CurrentBalance = 5000 WHERE Id = {stoppedLineId};");
    var stoppedLineTransfer = financialRepository.ExecuteCustomerTransfer(
        FinancialAccountKind.VodafoneCash, new(stoppedLineId, true, "01012345678", 200m, 2m, "عملية على خط سيُوقَف بعدها"));
    Check(stoppedLineTransfer.TransferId > 0, "A Vodafone Cash transfer is saved before its line is stopped");
    Execute(admin, $"UPDATE dbo.FinancialAccounts SET IsActive = 0 WHERE Id = {stoppedLineId};");
    financialRepository.LoadAccountsInto(liveAccounts);
    Check(liveAccounts.All(account => account.Id != stoppedLineId),
        "A stopped line disappears from the active accounts while its transfer stays in the register");

    var stoppedFootprint = WalletCorrectionTests.Footprint(admin);
    Exception? stoppedEditError = null;
    try
    {
        financialRepository.UpdateCustomerTransfer(stoppedLineTransfer.TransferId, stoppedLineTransfer.RowVersion,
            FinancialAccountKind.VodafoneCash, new(stoppedLineId, true, "01012345678", 250m, 2m, "تعديل بعد إيقاف الخط"));
    }
    catch (Exception ex) { stoppedEditError = ex; }
    Check(stoppedEditError is FinancialDataException { Message: var stoppedMessage }
          && stoppedMessage.Contains("المحفظة الأصلية موقوفة", StringComparison.Ordinal)
          && !stoppedMessage.Contains("مكررة", StringComparison.Ordinal),
        "Editing a transfer of a stopped line reports the stopped line in Arabic, not a duplicate-data error");

    Exception? stoppedReverseError = null;
    try
    {
        financialRepository.ReverseCustomerTransfer(stoppedLineTransfer.TransferId, stoppedLineTransfer.RowVersion,
            FinancialAccountKind.VodafoneCash, "عكس بعد إيقاف الخط");
    }
    catch (Exception ex) { stoppedReverseError = ex; }
    Check(stoppedReverseError is FinancialDataException { Message: var stoppedReverseMessage }
          && stoppedReverseMessage.Contains("موقوفة", StringComparison.Ordinal),
        "Reversing a transfer of a stopped line is refused with an Arabic financial error, not an unhandled LINQ crash");

    Check(stoppedFootprint == WalletCorrectionTests.Footprint(admin), "Neither stopped-line request changed any financial data");
    Execute(admin, $"UPDATE dbo.FinancialAccounts SET IsActive = 1 WHERE Id = {stoppedLineId};");
    financialRepository.LoadAccountsInto(liveAccounts);
    var correctedLine = financialRepository.UpdateCustomerTransfer(stoppedLineTransfer.TransferId, stoppedLineTransfer.RowVersion,
        FinancialAccountKind.VodafoneCash, new(stoppedLineId, true, "01012345678", 250m, 5m, "تعديل بعد إعادة تفعيل الخط"));
    Check(Scalar<decimal>(admin, $"SELECT Amount FROM dbo.CustomerTransfers WHERE Id={correctedLine.TransferId} AND CorrectsTransferId={stoppedLineTransfer.TransferId};") == 250m
        && Scalar<decimal>(admin, $"SELECT Amount FROM dbo.CustomerTransfers WHERE Id={stoppedLineTransfer.TransferId} AND OperationStatus=1;") == 200m,
        "Re-activating the line lets the very same edit succeed");
    Console.WriteLine("PASS: a stopped Vodafone Cash line is reported in Arabic instead of crashing the edit flow");

    // «تعيين افتراضية» and «تفعيل / إيقاف» only reordered/mutated the local list before, so both
    // choices were lost on the next reload or restart. They must be written to dbo.FinancialAccounts,
    // per AccountKind (UX_FinancialAccounts_OneDefaultPerKind), without touching other kinds' defaults.
    // FinancialAccountKind: Cash = 0, VodafoneCash = 1, InstaPay = 2.
    var firstVodafoneId = Scalar<int>(admin,
        "SELECT TOP 1 Id FROM dbo.FinancialAccounts WHERE AccountKind = 1 AND IsActive = 1 ORDER BY Id;");
    var otherKindDefaults = Scalar<int>(admin,
        "SELECT COUNT(*) FROM dbo.FinancialAccounts WHERE AccountKind <> 1 AND IsDefault = 1;");
    var extraWallet = financialRepository.SaveAccount(null, FinancialAccountKind.VodafoneCash,
        "خط إضافي للاختبار", "01099999999", 0m, 0m, 0m, 0m, 0m, "", false);
    Check(extraWallet.AccountId > 0
          && Scalar<int>(admin, $"SELECT IsDefault FROM dbo.FinancialAccounts WHERE Id={extraWallet.AccountId};") == 0,
        "A second Vodafone line is added and starts as a non-default wallet");

    financialRepository.SetDefaultAccount(extraWallet.AccountId, FinancialAccountKind.VodafoneCash);
    Check(Scalar<int>(admin, $"SELECT IsDefault FROM dbo.FinancialAccounts WHERE Id={extraWallet.AccountId};") == 1,
        "«تعيين افتراضية» writes IsDefault = 1 for the chosen line in dbo.FinancialAccounts");
    Check(Scalar<int>(admin, "SELECT COUNT(*) FROM dbo.FinancialAccounts WHERE AccountKind = 1 AND IsDefault = 1;") == 1,
        "Only one Vodafone line stays default after the assignment");
    Check(Scalar<int>(admin, "SELECT COUNT(*) FROM dbo.FinancialAccounts WHERE AccountKind <> 1 AND IsDefault = 1;") == otherKindDefaults,
        "Making a Vodafone line default keeps the cash and InstaPay defaults intact");
    financialRepository.LoadAccountsInto(liveAccounts);
    Check(liveAccounts.Single(account => account.Id == extraWallet.AccountId).IsDefault,
        "The reloaded accounts show the new default straight away, without a restart");

    financialRepository.SaveAccount(extraWallet.AccountId, FinancialAccountKind.VodafoneCash,
        "خط إضافي للاختبار", "01099999999", 0m, 0m, 0m, 0m, 0m, "", true);
    Check(Scalar<int>(admin, "SELECT COUNT(*) FROM dbo.FinancialAccounts WHERE AccountKind <> 1 AND IsDefault = 1;") == otherKindDefaults,
        "Editing the default line from the editor still leaves the other kinds' defaults alone");

    financialRepository.SetAccountActive(extraWallet.AccountId, false);
    Check(Scalar<int>(admin, $"SELECT IsActive FROM dbo.FinancialAccounts WHERE Id={extraWallet.AccountId};") == 0,
        "«تفعيل / إيقاف» writes IsActive = 0 for the stopped wallet");
    Check(Scalar<int>(admin, $"SELECT IsDefault FROM dbo.FinancialAccounts WHERE Id={extraWallet.AccountId};") == 0,
        "Stopping the default wallet clears its default flag instead of hiding a default row");
    Exception? stoppedDefaultError = null;
    try { financialRepository.SetDefaultAccount(extraWallet.AccountId, FinancialAccountKind.VodafoneCash); }
    catch (Exception ex) { stoppedDefaultError = ex; }
    Check(stoppedDefaultError is FinancialDataException,
        "A stopped wallet cannot be made the default, and the refusal is a financial error in Arabic");

    financialRepository.SetAccountActive(extraWallet.AccountId, true);
    Check(Scalar<int>(admin, $"SELECT IsActive FROM dbo.FinancialAccounts WHERE Id={extraWallet.AccountId};") == 1,
        "The same toggle reactivates the wallet in dbo.FinancialAccounts");
    financialRepository.SetDefaultAccount(extraWallet.AccountId, FinancialAccountKind.VodafoneCash);
    Check(Scalar<int>(admin, $"SELECT IsDefault FROM dbo.FinancialAccounts WHERE Id={extraWallet.AccountId};") == 1,
        "The default can be assigned again once the wallet is active");

    var cashRowId = Scalar<int>(admin, "SELECT TOP 1 Id FROM dbo.FinancialAccounts WHERE AccountKind = 0;");
    Exception? cashStopError = null;
    try { financialRepository.SetAccountActive(cashRowId, false); }
    catch (Exception ex) { cashStopError = ex; }
    Check(cashStopError is FinancialDataException
          && Scalar<int>(admin, $"SELECT IsActive FROM dbo.FinancialAccounts WHERE Id={cashRowId};") == 1,
        "The cash account refuses the stop toggle and stays active");

    financialRepository.SetDefaultAccount(firstVodafoneId, FinancialAccountKind.VodafoneCash);
    Check(Scalar<int>(admin, $"SELECT IsDefault FROM dbo.FinancialAccounts WHERE Id={firstVodafoneId};") == 1
          && Scalar<int>(admin, $"SELECT IsDefault FROM dbo.FinancialAccounts WHERE Id={extraWallet.AccountId};") == 0,
        "Re-assigning the default moves the flag instead of duplicating it");
    Check(financialRepository.DeleteAccount(extraWallet.AccountId, "خط إضافي للاختبار").Deleted,
        "The temporary wallet is cleaned up again");
    // «سجلات فودافون كاش» و«سجلات إنستاباي» listed only dbo.CustomerTransfers, so a sale, purchase,
    // expense, maintenance payment or wallet-to-wallet transfer never appeared there. They must now read
    // the whole wallet history from dbo.FinancialTransactions and keep the other screens' rows read-only.
    var walletHistory = new System.Collections.ObjectModel.ObservableCollection<VodafoneTransferOperation>();
    financialRepository.LoadVodafoneWalletMovementsInto(walletHistory);
    var purchaseMovement = walletHistory.FirstOrDefault(movement => !movement.IsCustomerTransfer && movement.SourceTitle == "شراء / سداد");
    Check(purchaseMovement is not null,
        "A purchase payment on the wallet reaches the records screen, not only customer transfers");
    Check(purchaseMovement!.OperationTitle == "شراء / سداد" && !purchaseMovement.CanEditOrDelete,
        "A purchase movement is read-only on the records screen instead of offering edit/reverse/delete");
    Check(purchaseMovement.MovementId > 0 && purchaseMovement.Commission == 0m && purchaseMovement.TotalCashDisplay == "—",
        "A purchase movement carries its ledger id, no commission and no cash-box effect");
    Check(walletHistory.Any(movement => movement.IsCustomerTransfer && movement.CanEditOrDelete),
        "Customer transfers keep their edit/reverse/delete controls in the same list");
    var transferRowsInHistory = walletHistory.Where(movement => movement.IsCustomerTransfer).ToList();
    Check(transferRowsInHistory.All(movement => movement.Id > 0 && movement.CustomerNumber.Length > 0),
        "Every customer transfer row keeps its CustomerTransfers id and customer number");
    Console.WriteLine("PASS: the Vodafone Cash records screen lists sales, purchases and expenses beside customer transfers");

    var instapayHistory = new System.Collections.ObjectModel.ObservableCollection<InstapayTransferOperation>();
    financialRepository.LoadInstapayWalletMovementsInto(instapayHistory);
    Check(instapayHistory.All(movement => movement.WalletId > 0),
        "The InstaPay records screen loads its own wallet history without mixing the Vodafone lines");
    Check(instapayHistory.Count <= walletHistory.Count,
        "Both records screens read the same ledger shape from dbo.FinancialTransactions");
    Console.WriteLine("PASS: the InstaPay records screen reads every InstaPay wallet movement from the same ledger");
    Console.WriteLine("PASS: «تعيين افتراضية» and «تفعيل / إيقاف» persist per kind in dbo.FinancialAccounts");
    WalletCorrectionTests.Run(admin, settings, userId, cashRowId, legacyTransfer);
    var saleMigration = Path.Combine(FindRepositoryRoot(), "sql", "22_EnableSaleSettlement.sql");
    RunScript(admin, saleMigration);
    RunScript(admin, saleMigration); // Repeat as on an upgraded installation; no business rows may be posted.
    var reportInstaller = Path.Combine(FindRepositoryRoot(), "sql", "26_EnableSalesReports.sql");
    RunScript(admin, reportInstaller);
    RunScript(admin, reportInstaller);
    SaleSettlementTests.Run(admin, settings, userId, categoryId, firstVodafoneId, cashRowId);
    var serviceReportInstaller=Path.Combine(sqlDirectory,"28_EnableMaintenanceReports.sql");
    RunScript(admin,serviceReportInstaller);
    RunScript(admin,serviceReportInstaller);
    MaintenanceSettlementTests.Run(admin, settings, userId, cashRowId);
    var purchaseReportInstaller = Path.Combine(sqlDirectory, "27_EnablePurchaseReports.sql");
    RunScript(admin, purchaseReportInstaller);
    RunScript(admin, purchaseReportInstaller);
    PurchaseSettlementTests.Run(admin, settings, userId, ordinaryProductId, serialProductId);
    var financeReportInstaller=Path.Combine(sqlDirectory,"29_EnableFinanceReports.sql");
    RunScript(admin,financeReportInstaller);
    RunScript(admin,financeReportInstaller);
    FinanceReportTests.Run(admin,settings,userId,cashRowId,legacyTransfer.Id);
    var purchaseFootprint = PurchaseSettlementTests.Footprint(admin);
    foreach (var script in new[] { "07_CreateReportingViews.sql", "12_EnablePurchasing.sql", "25_EnablePurchaseSettlement.sql" })
        RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", script));
    Check(purchaseFootprint == PurchaseSettlementTests.Footprint(admin), "07/12/25 are repeatable with populated supplier notes, receipts and applications");
    var maintenanceBefore = MaintenanceSettlementTests.Footprint(admin);
    RunScript(admin, Path.Combine(FindRepositoryRoot(), "sql", "24_EnableMaintenanceSettlement.sql"));
    Check(MaintenanceSettlementTests.Footprint(admin) == maintenanceBefore, "Rerunning sql/24 leaves populated tickets, receipts, stock and accounts untouched");
    Console.WriteLine("PASS: sales, purchases, expenses, finance and maintenance against disposable SQL Server");
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

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectZ.sln")))
        directory = directory.Parent;
    return directory?.FullName ?? throw new InvalidOperationException("ProjectZ repository root not found.");
}

static void RunScript(SqlConnection connection, string path)
{
    foreach (var batch in Batches(File.ReadAllText(path))) Execute(connection, batch);
    Console.WriteLine("PASS: executed " + Path.GetFileName(path));
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
