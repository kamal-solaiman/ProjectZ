-- Grant the least-privilege LAN client exactly what the POS sales screen needs.
-- Run once after 06_CreateFullSchema.sql / 07_CreateReportingViews.sql / 08_VerifyFullSchema.sql.
-- Idempotent: GRANT repeats safely and no table, column or row is modified here.
-- The script does NOT create cash/wallet accounts; the application creates the
-- default cash, Vodafone Cash and InstaPay accounts on first sale (see docs/DATABASE.md).

USE [ProjectZ];
GO
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.SalesInvoices', N'U') IS NULL
    THROW 50040, N'dbo.SalesInvoices is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.FinancialAccounts', N'U') IS NULL
    THROW 50041, N'dbo.FinancialAccounts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50042, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
GO

/* Customers: POS reads the directory, saves a new customer and can refresh snapshots. */
IF OBJECT_ID(N'dbo.Customers', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Customers TO [ProjectZClient];
GO

/* Document numbering: the invoice/return number comes from DocumentSequences, never from IDENTITY. */
IF OBJECT_ID(N'dbo.DocumentSequences', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.DocumentSequences TO [ProjectZClient];
GO

/* Cash and wallet balances plus the unified financial ledger written with every sale payment. */
IF OBJECT_ID(N'dbo.FinancialAccounts', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
IF OBJECT_ID(N'dbo.FinancialTransactions', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
GO

/* Sales document: invoice header, lines, serialized units, payments and activity history. */
IF OBJECT_ID(N'dbo.SalesInvoices', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.SalesInvoices TO [ProjectZClient];
IF OBJECT_ID(N'dbo.SalesInvoiceLines', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.SalesInvoiceLines TO [ProjectZClient];
IF OBJECT_ID(N'dbo.SalesInvoiceLineUnits', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.SalesInvoiceLineUnits TO [ProjectZClient];
IF OBJECT_ID(N'dbo.SalesPayments', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.SalesPayments TO [ProjectZClient];
IF OBJECT_ID(N'dbo.SalesInvoiceActivities', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.SalesInvoiceActivities TO [ProjectZClient];
GO

/* Customer returns created from the invoice screen. */
IF OBJECT_ID(N'dbo.ReturnExchanges', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchanges TO [ProjectZClient];
IF OBJECT_ID(N'dbo.ReturnExchangeLines', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchangeLines TO [ProjectZClient];
IF OBJECT_ID(N'dbo.ReturnExchangeUnits', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchangeUnits TO [ProjectZClient];
GO

/* Inventory and audit grants stay as created by 04; re-stated so a partial run is repaired. */
IF OBJECT_ID(N'dbo.InventoryUnits', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.InventoryUnits TO [ProjectZClient];
IF OBJECT_ID(N'dbo.StockMovements', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.StockMovements TO [ProjectZClient];
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NOT NULL
    GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

PRINT N'SUCCESS: ProjectZClient can now post POS sales, customer returns, payments and financial ledger rows.';
GO
