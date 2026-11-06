-- Run in SSMS on the primary server (DESKTOP-M1T0F31 / 192.168.1.4) as sysadmin.
-- Compatible with SQL Server 2016 SP1 Express (13.0.4001.0, Compatibility Level 130).
-- NOTE: Change the password below (and in connectionsettings.json) before sensitive production deployment.

IF DB_ID(N'FitohPhone') IS NULL
BEGIN
    THROW 50001, N'Database [FitohPhone] does not exist. Run sql/01_CreateDatabase.sql and sql/02_SeedAdmin.sql first.', 1;
END;
GO

USE [master];
GO

-- 1. Enable SQL Server and Windows Authentication mode (Mixed Mode = 2)
EXEC sys.xp_instance_regwrite
    N'HKEY_LOCAL_MACHINE',
    N'Software\Microsoft\MSSQLServer\MSSQLServer',
    N'LoginMode',
    REG_DWORD,
    2;
GO

-- 2. Create or update the least-privilege SQL login used by LAN clients
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'FitohPhoneClient')
BEGIN
    CREATE LOGIN [FitohPhoneClient]
        WITH PASSWORD = N'Fitoh@LanClient#2026!',
             DEFAULT_DATABASE = [FitohPhone],
             CHECK_EXPIRATION = OFF,
             CHECK_POLICY = ON;
END
ELSE
BEGIN
    ALTER LOGIN [FitohPhoneClient]
        WITH PASSWORD = N'Fitoh@LanClient#2026!',
             DEFAULT_DATABASE = [FitohPhone],
             CHECK_EXPIRATION = OFF,
             CHECK_POLICY = ON;
END;

ALTER LOGIN [FitohPhoneClient] ENABLE;
GO

-- 3. Map the login to [FitohPhone] and grant SELECT-only access on dbo.Users
USE [FitohPhone];
GO

IF DATABASE_PRINCIPAL_ID(N'FitohPhoneClient') IS NULL
BEGIN
    CREATE USER [FitohPhoneClient] FOR LOGIN [FitohPhoneClient];
END
ELSE
BEGIN
    ALTER USER [FitohPhoneClient] WITH LOGIN = [FitohPhoneClient];
END;

GRANT CONNECT TO [FitohPhoneClient];
GRANT SELECT ON OBJECT::dbo.Users TO [FitohPhoneClient];
GO

PRINT N'SUCCESS: FitohPhoneClient login/user configured and Mixed Mode enabled.';
PRINT N'IMPORTANT: Right-click the server in SSMS Object Explorer -> Restart (once) so Mixed Mode takes effect.';
GO
