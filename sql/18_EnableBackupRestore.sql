-- Enable BACKUP DATABASE / RESTORE DATABASE for the application's SQL login.
-- Run in SSMS on the SQL Server as a member of sysadmin (or a role with ALTER ANY SERVER ROLE
-- and db_owner on the target database). SQL Server 2016 SP1 compatible.
-- Idempotent: it only adds the missing role memberships; it never drops a role or a permission.
--
-- The application connects as ProjectZClient. If your connectionsettings.json uses another SQL
-- login or a different database name, replace ProjectZClient / ProjectZ below consistently.
--
-- Security note: db_owner is required to isolate/restore the target database, and dbcreator is
-- required because RESTORE needs CREATE DATABASE permission on the server. These are broad rights;
-- use a dedicated backup account if your policy does not allow them for the application login.

USE [master];
GO

IF DB_ID(N'ProjectZ') IS NULL
    THROW 50080, N'Database [ProjectZ] does not exist. Run sql/01_CreateDatabase.sql first.', 1;
IF SUSER_ID(N'ProjectZClient') IS NULL
    THROW 50081, N'Login [ProjectZClient] does not exist. Run sql/04_SetupLanSqlAuth.sql first.', 1;
GO

-- RESTORE DATABASE needs CREATE DATABASE at server scope; this is the dbcreator membership.
IF ISNULL(IS_SRVROLEMEMBER(N'dbcreator', N'ProjectZClient'), 0) <> 1
BEGIN
    ALTER SERVER ROLE [dbcreator] ADD MEMBER [ProjectZClient];
    PRINT N'Granted: ProjectZClient is now a member of the dbcreator server role.';
END
ELSE
    PRINT N'Already granted: ProjectZClient is a member of the dbcreator server role.';
GO

USE [ProjectZ];
GO

IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50082, N'User [ProjectZClient] is missing from [ProjectZ]. Run sql/04_SetupLanSqlAuth.sql first.', 1;
GO

-- BACKUP DATABASE and ALTER DATABASE ... SINGLE_USER/MULTI_USER need db_owner on this database.
IF ISNULL(IS_ROLEMEMBER(N'db_owner', N'ProjectZClient'), 0) <> 1
BEGIN
    ALTER ROLE [db_owner] ADD MEMBER [ProjectZClient];
    PRINT N'Granted: ProjectZClient is now a member of the db_owner database role.';
END
ELSE
    PRINT N'Already granted: ProjectZClient is a member of the db_owner database role.';
GO

SELECT
    SUSER_NAME() AS ExecutedBy,
    DB_NAME() AS DatabaseName,
    IS_SRVROLEMEMBER(N'dbcreator', N'ProjectZClient') AS IsDbCreator,
    IS_ROLEMEMBER(N'db_owner', N'ProjectZClient') AS IsDbOwner;
GO

PRINT N'SUCCESS: ProjectZClient can BACKUP and RESTORE [ProjectZ].';
PRINT N'If a backup still fails, verify that the folder/UNC path in the app exists on the SQL Server service account, not only on this client.';
GO
