using Microsoft.Data.SqlClient;
using static SqlTest;

internal static class SettlementInstallerTests
{
    // Unlike the normal deployment runner, simulate plain SSMS: submit EVERY GO batch after an error.
    private static List<int> Errors(SqlConnection db, string script)
    {
        var errors = new List<int>();
        foreach (var batch in Batches(script))
        {
            try { Execute(db, batch); }
            catch (SqlException ex) { errors.AddRange(ex.Errors.Cast<SqlError>().Where(e => e.Class >= 11).Select(e => e.Number)); }
        }
        return errors;
    }

    private static void OnlyError(SqlConnection db, string script, int number)
    {
        var errors = Errors(db, script);
        Check(errors.SequenceEqual([number]), $"Expected only SQL {number} even in a continue-on-error client; got [{string.Join(", ", errors)}]");
    }

    // Include existing module definitions and DCL, not just a count of tables.
    private static string Catalog(SqlConnection db) => Scalar<string>(db, """
        SELECT CONCAT((SELECT o.object_id,o.name,o.type,OBJECT_DEFINITION(o.object_id) AS definition FROM sys.objects o
                       WHERE o.is_ms_shipped=0 ORDER BY o.object_id FOR JSON PATH),
          (SELECT object_id,column_id,name FROM sys.columns WHERE OBJECT_SCHEMA_NAME(object_id)=N'dbo' ORDER BY object_id,column_id FOR JSON PATH),
          (SELECT object_id,index_id,name FROM sys.indexes WHERE OBJECT_SCHEMA_NAME(object_id)=N'dbo' ORDER BY object_id,index_id FOR JSON PATH),
          (SELECT name FROM sys.types WHERE is_user_defined=1 ORDER BY name FOR JSON PATH),
          (SELECT name FROM sys.database_principals WHERE type='R' ORDER BY name FOR JSON PATH),
          (SELECT class,major_id,minor_id,type,state FROM sys.database_permissions WHERE grantee_principal_id=USER_ID(N'ProjectZClient')
           ORDER BY class,major_id,minor_id,type FOR JSON PATH), @@TRANCOUNT);
        """);

    internal static void Run(SqlConnection db, string directory)
    {
        string Read(string file) => File.ReadAllText(Path.Combine(directory, file));
        void Run(string script) { foreach (var batch in Batches(script)) Execute(db, batch); }
        var maintenance = Read("24_EnableMaintenanceSettlement.sql");
        var purchase = Read("25_EnablePurchaseSettlement.sql");
        var backup = Read("18_EnableBackupRestore.sql");
        var preReport = Catalog(db); // 22 has not installed the saved return values yet.
        OnlyError(db, Read("26_EnableSalesReports.sql"), 50260);
        OnlyError(db, Read("27_EnablePurchaseReports.sql"), 50262);
        OnlyError(db, Read("28_EnableMaintenanceReports.sql"), 50264);
        Check(Catalog(db) == preReport, "An out-of-order report install leaves schema and permissions intact");
        var report = Read("admin/SeparateLegacyBackupRoles.sql");
        var demote = report.Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;", StringComparison.Ordinal);

        // The optional backup setup must not grant a server role before it verifies the DB user mapping.
        Execute(db, $"CREATE LOGIN [ProjectZBackupAdmin] WITH PASSWORD='Aa1!{Guid.NewGuid():N}', CHECK_POLICY=OFF;");
        OnlyError(db, backup, 50081);
        Check(Scalar<int>(db, "SELECT IS_SRVROLEMEMBER('dbcreator','ProjectZBackupAdmin');") == 0, "No premature restore grant");
        Execute(db, "CREATE USER [ProjectZBackupAdmin] FOR LOGIN [ProjectZBackupAdmin];");
        Run(backup);
        Run(backup);
        Check(Scalar<int>(db, "SELECT IS_ROLEMEMBER('db_owner','ProjectZBackupAdmin') + IS_SRVROLEMEMBER('dbcreator','ProjectZBackupAdmin');") == 2
           && Scalar<int>(db, "SELECT IS_ROLEMEMBER('db_owner','ProjectZClient') + IS_SRVROLEMEMBER('dbcreator','ProjectZClient');") == 0,
           "Only the separate backup administrator gets the two restore memberships");

        void RefuseBoth()
        {
            var before = Catalog(db);
            OnlyError(db, maintenance, 50248);
            OnlyError(db, purchase, 50250);
            Check(Catalog(db) == before, "Blocked installs preserve columns, indexes, types, roles, modules and cashier grants");
        }
        Execute(db, "ALTER ROLE [db_owner] ADD MEMBER [ProjectZClient];");
        RefuseBoth(); // first install
        Execute(db, "ALTER ROLE [db_owner] DROP MEMBER [ProjectZClient]; ALTER SERVER ROLE [dbcreator] ADD MEMBER [ProjectZClient];");
        // Representative leftovers from an older interrupted setup. Recovery must not require deleting them.
        Execute(db, "CREATE PROCEDURE dbo.LockMaintenanceTicket AS SELECT 24 AS OldPartial;");
        Execute(db, "CREATE PROCEDURE dbo.PayPurchaseDocument AS SELECT 25 AS OldPartial;");
        Execute(db, "GRANT EXECUTE ON OBJECT::dbo.PayPurchaseDocument TO [ProjectZClient];");
        RefuseBoth(); // dbcreator alone is sufficient to refuse
        Execute(db, "ALTER ROLE [db_owner] ADD MEMBER [ProjectZClient];");
        Run(report);
        Check(Scalar<int>(db, "SELECT IS_ROLEMEMBER('db_owner','ProjectZClient') + IS_SRVROLEMEMBER('dbcreator','ProjectZClient');") == 2,
            "Recovery file is read-only by default");
        Execute(db, "ALTER SERVER ROLE [sysadmin] ADD MEMBER [ProjectZClient];");
        OnlyError(db, demote, 50084); // never modify an unexpected administrative account
        Execute(db, "ALTER SERVER ROLE [sysadmin] DROP MEMBER [ProjectZClient];");
        Run(demote);
        Run(demote);
        Check(Scalar<int>(db, "SELECT IS_ROLEMEMBER('db_owner','ProjectZClient') + IS_SRVROLEMEMBER('dbcreator','ProjectZClient');") == 0
           && Scalar<int>(db, "SELECT IS_ROLEMEMBER('db_owner','ProjectZBackupAdmin') + IS_SRVROLEMEMBER('dbcreator','ProjectZBackupAdmin');") == 2,
            "Explicit legacy demotion is repeatable and leaves the separate backup path intact");

        // An inherited database role is not one of the two direct grants made by the old installer.
        Execute(db, "CREATE ROLE TestLegacyGroup; ALTER ROLE [db_owner] ADD MEMBER [TestLegacyGroup]; ALTER ROLE [TestLegacyGroup] ADD MEMBER [ProjectZClient];");
        RefuseBoth();
        OnlyError(db, demote, 50084);
        Execute(db, "ALTER ROLE [TestLegacyGroup] DROP MEMBER [ProjectZClient]; ALTER ROLE [db_owner] DROP MEMBER [TestLegacyGroup]; DROP ROLE [TestLegacyGroup];");
        Execute(db, "ALTER SERVER ROLE [sysadmin] ADD MEMBER [ProjectZClient];");
        RefuseBoth();
        Execute(db, "ALTER SERVER ROLE [sysadmin] DROP MEMBER [ProjectZClient];");

        // Fail in the actual final GRANT of each installer, after the new schema AND earlier grants.
        Execute(db, """
            CREATE TRIGGER TestSettlementGrant ON DATABASE FOR GRANT_DATABASE AS
            BEGIN
                DECLARE @sql nvarchar(max)=EVENTDATA().value('(/EVENT_INSTANCE/TSQLCommand/CommandText)[1]','nvarchar(max)');
                IF @sql LIKE N'%GRANT EXECUTE ON OBJECT::dbo.ReopenMaintenanceTicket%'
                   OR @sql LIKE N'%GRANT EXECUTE ON OBJECT::dbo.DeleteUnsettledPurchase%'
                    THROW 50989,N'Test failure in settlement permissions',1;
            END;
            """);
        foreach (var installer in new[] { maintenance, purchase })
        {
            var before = Catalog(db);
            var errors = Errors(db, installer);
            Check(errors.Contains(50989), $"Reached the injected late DCL error; got [{string.Join(", ", errors)}]");
            Check(Catalog(db) == before && Scalar<int>(db, "SELECT @@TRANCOUNT;") == 0,
                "A failed grant rolls back all installer DDL/DCL, restores the old module bodies and leaves no open transaction");
        }
        Execute(db, "DROP TRIGGER TestSettlementGrant ON DATABASE;");
        // Program.cs now installs both unmodified scripts over these leftovers, exercises them using the
        // restricted cashier connection, and reinstalls over populated tickets, credits and payments.
    }
}
