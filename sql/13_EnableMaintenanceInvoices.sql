-- Enable the maintenance invoices register (فواتير الصيانة) on SQL Server 2016 SP1 / level 130.
-- Run once after 06_CreateFullSchema.sql and 09_EnablePosSales.sql. Idempotent: safe to re-run.
--
-- What this script changes:
--   1. Repairs CK_MaintenanceOrderParts_Source. The original definition demanded a POS product
--      (dbo.Products) for every non-manual part line, so issuing a workshop spare part
--      (dbo.MaintenanceSpareParts) failed with FK/CHECK error 547. A part line is now valid when
--      it references either a spare part (SparePartId) or a POS product (ProductId); the desktop
--      client writes SparePartId and keeps ProductId NULL for spare-part lines.
--   2. Adds the covering indexes the invoices screen uses: the newest-first register order, the
--      delivered-date filter, and the batched child reads of part lines and activities (one query
--      per table instead of three queries per order). Existing indexes in sql/06 already cover the
--      status lookup and the payment rows, so no duplicate index is created.
--   3. Re-states the least-privilege grants ProjectZClient needs to read and write maintenance
--      orders, their part lines, payment history and activity log, and to resolve the receiver
--      and actor display names shown on each invoice.
--
-- No table, column or row is dropped. Existing data is preserved.

USE [ProjectZ];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
/* The delivered-date index is filtered, so the documented SET options must be ON. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF OBJECT_ID(N'dbo.MaintenanceOrders', N'U') IS NULL
    THROW 50050, N'dbo.MaintenanceOrders is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenanceOrderParts', N'U') IS NULL
    THROW 50051, N'dbo.MaintenanceOrderParts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenancePayments', N'U') IS NULL
    THROW 50052, N'dbo.MaintenancePayments is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenanceActivities', N'U') IS NULL
    THROW 50053, N'dbo.MaintenanceActivities is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF COL_LENGTH(N'dbo.MaintenanceOrderParts', N'SparePartId') IS NULL
    THROW 50054, N'dbo.MaintenanceOrderParts.SparePartId is missing. Run sql/06_CreateFullSchema.sql first.', 1;
GO

BEGIN TRANSACTION;

/* 1) Part-line source constraint: allow a spare-part line (SparePartId, no POS product) and a
      catalog line (ProductId), and keep manual lines free of both links. The definition stays
      permissive for rows written by earlier builds, which stored the spare-part id in ProductId. */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
             AND name = N'CK_MaintenanceOrderParts_Source')
    ALTER TABLE dbo.MaintenanceOrderParts DROP CONSTRAINT CK_MaintenanceOrderParts_Source;

ALTER TABLE dbo.MaintenanceOrderParts WITH CHECK ADD CONSTRAINT CK_MaintenanceOrderParts_Source
    CHECK (
        (IsManualPart = 1 AND SparePartId IS NULL)
        OR (IsManualPart = 0 AND (ProductId IS NOT NULL OR SparePartId IS NOT NULL)));

/* 2) Covering indexes for the register and its batched child reads.
      The existing IX_MaintenanceOrders_Status_Received / IX_MaintenancePayments_Order_Date from
      sql/06 already cover status lookups and the payment rows, so only the missing paths are
      added here and no duplicate index is created. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders') AND name = N'IX_MaintenanceOrders_ReceivedAtUtc')
    CREATE INDEX IX_MaintenanceOrders_ReceivedAtUtc
        ON dbo.MaintenanceOrders(ReceivedAtUtc DESC, Id DESC)
        INCLUDE (OrderStatus, Priority, AssignedTechnician, CustomerNameSnapshot, DeliveredAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders') AND name = N'IX_MaintenanceOrders_DeliveredAtUtc')
    CREATE INDEX IX_MaintenanceOrders_DeliveredAtUtc
        ON dbo.MaintenanceOrders(DeliveredAtUtc DESC)
        INCLUDE (OrderStatus, Priority)
        WHERE DeliveredAtUtc IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts') AND name = N'IX_MaintenanceOrderParts_Order')
    CREATE INDEX IX_MaintenanceOrderParts_Order
        ON dbo.MaintenanceOrderParts(MaintenanceOrderId)
        INCLUDE (SparePartId, ProductId, Quantity, UnitCost, UnitSellPrice, IsManualPart);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceActivities') AND name = N'IX_MaintenanceActivities_Order')
    CREATE INDEX IX_MaintenanceActivities_Order
        ON dbo.MaintenanceActivities(MaintenanceOrderId, OccurredAtUtc)
        INCLUDE (ActionTitle, Amount);

COMMIT TRANSACTION;
GO

/* 3) Least-privilege grants. Repeats sql/04 + sql/09 so a partially prepared database is repaired.
      MaintenancePayments and MaintenanceActivities stay append-only: the invoices screen reads
      them and records new rows, never edits history. */
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50055, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;

GRANT SELECT ON OBJECT::dbo.Users TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MaintenanceOrderParts TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.MaintenancePayments TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.MaintenanceActivities TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.DocumentSequences TO [ProjectZClient];
GO

/* 4) Read-only verification of the repaired constraint and the new indexes. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
                 AND name = N'CK_MaintenanceOrderParts_Source')
    THROW 50056, N'CK_MaintenanceOrderParts_Source was not created. Review the earlier SQL error.', 1;

IF EXISTS (
    SELECT 1
    FROM (VALUES
            (N'MaintenanceOrders', N'IX_MaintenanceOrders_ReceivedAtUtc'),
            (N'MaintenanceOrders', N'IX_MaintenanceOrders_DeliveredAtUtc'),
            (N'MaintenanceOrderParts', N'IX_MaintenanceOrderParts_Order'),
            (N'MaintenanceActivities', N'IX_MaintenanceActivities_Order')
         ) AS expected(table_name, index_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'dbo.' + expected.table_name)
          AND i.name = expected.index_name))
    THROW 50057, N'One or more maintenance invoice indexes are missing. Review the earlier SQL error.', 1;

SELECT
    (SELECT COUNT(*) FROM dbo.MaintenanceOrders) AS MaintenanceOrders,
    (SELECT COUNT(*) FROM dbo.MaintenanceOrderParts) AS OrderParts,
    (SELECT COUNT(*) FROM dbo.MaintenancePayments) AS MaintenancePayments,
    (SELECT COUNT(*) FROM dbo.MaintenanceActivities) AS MaintenanceActivities,
    (SELECT COUNT(*) FROM dbo.MaintenanceSpareParts WHERE IsActive = 1) AS ActiveSpareParts;

SELECT name, definition
FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
  AND name = N'CK_MaintenanceOrderParts_Source';
GO

PRINT N'SUCCESS: maintenance invoice constraints, indexes and grants are in place (sql/13_EnableMaintenanceInvoices.sql).';
GO
