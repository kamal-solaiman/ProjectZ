-- Run in SSMS on the primary server (DESKTOP-M1T0F31 / 192.168.1.4) as sysadmin.
-- Compatible with SQL Server 2016 SP1 Express (13.0.4001.0, Compatibility Level 130).
-- NOTE: Change the password below (and in connectionsettings.json) before sensitive production deployment.

IF DB_ID(N'ProjectZ') IS NULL
BEGIN
    THROW 50001, N'Database [ProjectZ] does not exist. Run sql/01_CreateDatabase.sql and sql/02_SeedAdmin.sql first.', 1;
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
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'ProjectZClient')
BEGIN
    CREATE LOGIN [ProjectZClient]
        WITH PASSWORD = N'Fitoh@LanClient#2026!',
             DEFAULT_DATABASE = [ProjectZ],
             CHECK_EXPIRATION = OFF,
             CHECK_POLICY = ON;
END
ELSE
BEGIN
    ALTER LOGIN [ProjectZClient]
        WITH PASSWORD = N'Fitoh@LanClient#2026!',
             DEFAULT_DATABASE = [ProjectZ],
             CHECK_EXPIRATION = OFF,
             CHECK_POLICY = ON;
END;

ALTER LOGIN [ProjectZClient] ENABLE;
GO

-- 3. Map the login to [ProjectZ] and grant only login + inventory permissions
USE [ProjectZ];
GO

IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
BEGIN
    CREATE USER [ProjectZClient] FOR LOGIN [ProjectZClient];
END
ELSE
BEGIN
    ALTER USER [ProjectZClient] WITH LOGIN = [ProjectZClient];
END;

GRANT CONNECT TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.Users TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.Categories TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.Suppliers TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Products TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryUnits TO [ProjectZClient];
GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.StockMovements TO [ProjectZClient];
GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
-- Read-only history checks prevent deleting units/products referenced by documents.
GRANT SELECT ON OBJECT::dbo.SalesInvoiceLineUnits TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceLineUnits TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.ReturnExchangeUnits TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.SalesInvoiceLines TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceLines TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.MaintenanceOrderParts TO [ProjectZClient];
GO

PRINT N'SUCCESS: ProjectZClient login/user configured and Mixed Mode enabled.';
PRINT N'IMPORTANT: Right-click the server in SSMS Object Explorer -> Restart (once) so Mixed Mode takes effect.';
GO
