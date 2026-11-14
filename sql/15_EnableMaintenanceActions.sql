-- Enable the maintenance actions screen (إجراءات الصيانة) fully on SQL Server 2016 SP1 / level 130.
-- Run once after 06_CreateFullSchema.sql, 09_EnablePosSales.sql and 13_EnableMaintenanceInvoices.sql.
-- Idempotent: safe to re-run. No table, column or row is dropped.
--
-- What this script changes:
--   1. Verifies the two columns the screen now relies on: MaintenanceOrders.RowVersion (optimistic
--      concurrency for the workshop form, delivery and cancellation) and the part-line link that
--      returns a spare part to stock (MaintenanceOrderParts.SparePartId).
--   2. Adds the indexes behind the two new SQL reads:
--        * the technician directory and technician filter (DISTINCT AssignedTechnician from the
--          orders, unioned with the active display names in dbo.Users);
--        * releasing a spare part from a ticket (lookup of the order lines that still reference a
--          given dbo.MaintenanceSpareParts row).
--      Existing indexes from sql/06 and sql/13 already cover the queue filters, the payment history
--      and the batched child reads, so nothing is created twice.
--   3. Re-states the least-privilege grants the screen needs (orders, part lines, spare parts,
--      payments, activities, accounts, ledger and user directory).
--
-- Write path used by the screen (all inside one transaction in MaintenanceRepository):
--   * UpdateMaintenanceOrder: the editable order columns + the RowVersion guard + new activity rows
--     + new payment rows (deposit / final / refund with FinancialAccounts and FinancialTransactions)
--     + a full re-sync of dbo.MaintenanceOrderParts.
--   * DeductSparePartStock / RestoreSparePartStock: atomic stock +/- with the RowVersion guard on
--     dbo.MaintenanceSpareParts.
--   * AppendMaintenanceActivity: one activity row for a print preview, without rewriting the ticket.

USE [ProjectZ];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.MaintenanceOrders', N'U') IS NULL
    THROW 50070, N'dbo.MaintenanceOrders is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenanceOrderParts', N'U') IS NULL
    THROW 50071, N'dbo.MaintenanceOrderParts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenanceSpareParts', N'U') IS NULL
    THROW 50072, N'dbo.MaintenanceSpareParts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF COL_LENGTH(N'dbo.MaintenanceOrders', N'RowVersion') IS NULL
    THROW 50073, N'dbo.MaintenanceOrders.RowVersion is missing; the screen needs it for optimistic concurrency.', 1;
IF COL_LENGTH(N'dbo.MaintenanceOrderParts', N'SparePartId') IS NULL
    THROW 50074, N'dbo.MaintenanceOrderParts.SparePartId is missing. Run sql/13_EnableMaintenanceInvoices.sql first.', 1;
GO

BEGIN TRANSACTION;

/* 1) Technician directory / filter: DISTINCT AssignedTechnician over the open and closed tickets. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders')
                 AND name = N'IX_MaintenanceOrders_Technician')
    CREATE INDEX IX_MaintenanceOrders_Technician
        ON dbo.MaintenanceOrders(AssignedTechnician)
        INCLUDE (OrderStatus, ReceivedAtUtc)
        WHERE AssignedTechnician <> N'';

/* 2) Returning a spare part to stock: find the ticket lines that still reference the part. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
                 AND name = N'IX_MaintenanceOrderParts_SparePart')
    CREATE INDEX IX_MaintenanceOrderParts_SparePart
        ON dbo.MaintenanceOrderParts(SparePartId)
        INCLUDE (MaintenanceOrderId, Quantity)
        WHERE SparePartId IS NOT NULL;

COMMIT TRANSACTION;
GO

/* 3) Least-privilege grants for the actions screen (identical to 09/13; re-running is safe). */
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ProjectZClient' AND type = N'R')
BEGIN
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MaintenanceOrderParts TO [ProjectZClient];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MaintenanceSpareParts TO [ProjectZClient];
    GRANT SELECT, INSERT ON OBJECT::dbo.MaintenanceActivities TO [ProjectZClient];
    GRANT SELECT, INSERT ON OBJECT::dbo.MaintenancePayments TO [ProjectZClient];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
    GRANT SELECT, INSERT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
    GRANT SELECT ON OBJECT::dbo.Users TO [ProjectZClient];
END
GO

/* Verification: the concurrency column, the two indexes and the technician directory query. */
SELECT N'MaintenanceOrders.RowVersion' AS ObjectName,
       CASE WHEN COL_LENGTH(N'dbo.MaintenanceOrders', N'RowVersion') IS NOT NULL
            THEN N'OK' ELSE N'MISSING' END AS Status
UNION ALL
SELECT N'IX_MaintenanceOrders_Technician',
       CASE WHEN EXISTS (SELECT 1 FROM sys.indexes
                         WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders')
                           AND name = N'IX_MaintenanceOrders_Technician')
            THEN N'OK' ELSE N'MISSING' END
UNION ALL
SELECT N'IX_MaintenanceOrderParts_SparePart',
       CASE WHEN EXISTS (SELECT 1 FROM sys.indexes
                         WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
                           AND name = N'IX_MaintenanceOrderParts_SparePart')
            THEN N'OK' ELSE N'MISSING' END;
GO

SELECT COUNT(DISTINCT TechnicianName) AS TechnicianDirectoryCount
FROM (
    SELECT LTRIM(RTRIM(AssignedTechnician)) AS TechnicianName FROM dbo.MaintenanceOrders
    UNION
    SELECT LTRIM(RTRIM(DisplayName)) FROM dbo.Users WHERE IsActive = 1
) AS names
WHERE TechnicianName <> N'';
GO
