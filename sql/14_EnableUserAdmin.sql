-- Grant the LAN client the login-account rights the users screen needs.
-- Run once after 06_CreateFullSchema.sql / 04_SetupLanSqlAuth.sql.
-- Idempotent: GRANT repeats safely and no table, column or row is created here.
-- dbo.Users stays the single login table; this script only widens the rights of ProjectZClient on it.
-- Password hashes are written by the application (PBKDF2-HMAC-SHA256, 600,000 iterations); no plain
-- password ever reaches SQL Server through this grant.

USE [ProjectZ];
GO
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 50060, N'dbo.Users is missing. Run sql/01_CreateDatabase.sql first.', 1;
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
    THROW 50061, N'dbo.AuditLog is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50062, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
GO

/* The screen lists accounts, adds, edits (username/display name/activation), resets the hash and deletes. */
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Users TO [ProjectZClient];
GO

/* Counting the invoices, stock moves and audit lines that keep a user row referenced needs read-only
   access to those tables; 04/09/12 already grant most of them, re-stated here for a partial run. */
IF OBJECT_ID(N'dbo.SalesInvoices', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.SalesInvoices TO [ProjectZClient];
IF OBJECT_ID(N'dbo.PurchaseInvoices', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.PurchaseInvoices TO [ProjectZClient];
IF OBJECT_ID(N'dbo.StockMovements', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.StockMovements TO [ProjectZClient];
IF OBJECT_ID(N'dbo.FinancialTransactions', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
IF OBJECT_ID(N'dbo.MaintenanceOrders', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
IF OBJECT_ID(N'dbo.CustomerTransfers', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.CustomerTransfers TO [ProjectZClient];
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

/* The users screen does not use roles, but a pre-existing link row would block a delete.
   The application clears it inside the same transaction; this grant lets that cleanup run. */
IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NOT NULL
    GRANT SELECT, DELETE ON OBJECT::dbo.UserRoles TO [ProjectZClient];
GO

PRINT N'SUCCESS: ProjectZClient can now list, add, edit, disable and delete login accounts in dbo.Users.';
GO
