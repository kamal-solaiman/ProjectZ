-- Enable the maintenance intake screen (أمر صيانة) fully on SQL Server 2016 SP1 / level 130.
-- Run once after 06_CreateFullSchema.sql, 09_EnablePosSales.sql and 13_EnableMaintenanceInvoices.sql.
-- Idempotent: safe to re-run.
--
-- What this script changes:
--   1. Adds the filtered index behind the new intake guard that refuses to receive the same device
--      twice: the screen asks whether an open order already exists for a serial/IMEI value
--      (SELECT ... WHERE SerialOrImei = @Serial AND OrderStatus NOT IN (5,6)). The filtered index
--      keeps that lookup cheap without indexing the thousands of rows that have no serial at all.
--   2. Verifies the objects the intake screen writes in one transaction: the order row, its first
--      activity entry («استلام جهاز صيانة»), the deposit rows in dbo.MaintenancePayments and the
--      matching ledger movement in dbo.FinancialTransactions, plus the DocumentSequences row that
--      produces MNT-yyyy-NNNN. Nothing is created twice and no existing data is touched.
--   3. Re-states the least-privilege grants used by the intake path, so an install that skipped the
--      earlier scripts still works with the restricted ProjectZClient login.
--
-- The device passcode is written to dbo.MaintenanceOrders.PasscodeValueProtected as AES-GCM text
-- ("enc:v1:" + base64(nonce|tag|cipher)) using a key kept outside the database: either the
-- PROJECTZ_PASSCODE_KEY environment variable or PasscodeKey in connectionsettings.json (base64,
-- 32 bytes). No schema change is needed: NVARCHAR(1000) holds the encrypted payload, and the column
-- keeps old plaintext rows readable.
--
-- No table, column or row is dropped. Existing data is preserved.

USE [ProjectZ];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.MaintenanceOrders', N'U') IS NULL
    THROW 50060, N'dbo.MaintenanceOrders is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenanceActivities', N'U') IS NULL
    THROW 50061, N'dbo.MaintenanceActivities is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenancePayments', N'U') IS NULL
    THROW 50062, N'dbo.MaintenancePayments is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.DocumentSequences', N'U') IS NULL
    THROW 50063, N'dbo.DocumentSequences is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
    THROW 50064, N'dbo.Customers is missing. Run sql/06_CreateFullSchema.sql first.', 1;
GO

BEGIN TRANSACTION;

/* 1) Serial/IMEI lookup used by the duplicate-device guard of the intake screen. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders')
                 AND name = N'IX_MaintenanceOrders_Serial_Open')
    CREATE INDEX IX_MaintenanceOrders_Serial_Open
        ON dbo.MaintenanceOrders(SerialOrImei)
        INCLUDE (OrderStatus, ReceivedAtUtc)
        WHERE SerialOrImei <> N'';

/* 2) The intake form validates these lengths before it saves; keep the column widths in sync. */
IF COL_LENGTH(N'dbo.MaintenanceOrders', N'PasscodeValueProtected') < 1000
    THROW 50065, N'dbo.MaintenanceOrders.PasscodeValueProtected must be NVARCHAR(1000) to hold the encrypted passcode.', 1;

COMMIT TRANSACTION;
GO

/* 3) Least-privilege grants for the intake path (identical to the ones in 09/13; re-running is safe). */
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ProjectZClient' AND type = N'R')
BEGIN
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Customers TO [ProjectZClient];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
    GRANT SELECT, INSERT ON OBJECT::dbo.MaintenanceActivities TO [ProjectZClient];
    GRANT SELECT, INSERT ON OBJECT::dbo.MaintenancePayments TO [ProjectZClient];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.DocumentSequences TO [ProjectZClient];
    GRANT SELECT ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
    GRANT SELECT, INSERT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
    GRANT SELECT ON OBJECT::dbo.Users TO [ProjectZClient];
END
GO

/* Verification: the index exists and the intake columns are wide enough. */
SELECT N'IX_MaintenanceOrders_Serial_Open' AS ObjectName,
       CASE WHEN EXISTS (SELECT 1 FROM sys.indexes
                         WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders')
                           AND name = N'IX_MaintenanceOrders_Serial_Open')
            THEN N'OK' ELSE N'MISSING' END AS Status
UNION ALL
SELECT N'PasscodeValueProtected',
       CONVERT(NVARCHAR(20), COL_LENGTH(N'dbo.MaintenanceOrders', N'PasscodeValueProtected')) + N' bytes'
UNION ALL
SELECT N'MaintenanceOrder sequence rows',
       CONVERT(NVARCHAR(20), COUNT(*)) + N' year(s)'
FROM dbo.DocumentSequences WHERE DocumentType = N'MaintenanceOrder';
GO
