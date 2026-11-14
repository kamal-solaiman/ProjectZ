-- Enable the maintenance spare-parts screen (قطع الغيار) fully on SQL Server 2016 SP1 / level 130.
-- Run once after 06_CreateFullSchema.sql, 09_EnablePosSales.sql and 13_EnableMaintenanceInvoices.sql.
-- Idempotent: safe to re-run. No table or row is dropped and no existing data is changed except
-- adding the opening-balance audit rows described in step 3.
--
-- What this script changes:
--   1. Extends dbo.MaintenanceSpareParts with the fields the screen needs: a barcode/part number,
--      a unit of measure, a preferred supplier (dbo.Suppliers), a reorder threshold, a storage
--      location, notes and the last purchase / issue timestamps. Every column is added only when
--      missing, so an older database is upgraded in place.
--   2. Creates dbo.MaintenanceSparePartMovements, the append-only audit trail behind every stock
--      change: opening balance, purchase from a supplier, issue to a maintenance order, return to
--      stock, and manual increase/decrease. The row links the part to its supplier, the maintenance
--      order, the customer and the user, so the screen can show the full chain (مورد ← قطعة ← عميل).
--   3. Backfills one OpeningBalance row for every existing active part that has stock and no
--      movement yet, using the first available user as the actor. This keeps the ledger complete
--      from the moment the feature is enabled without inventing purchases.
--   4. Adds the covering indexes used by the register, the low-stock filter, supplier lookups and
--      the movement history, plus the least-privilege grants ProjectZClient needs.
--
-- Delete behaviour: a part with order lines or movements is deactivated (IsActive = 0) instead of
-- being deleted, so the audit trail and the historical maintenance invoices stay intact.

USE [ProjectZ];
GO
/* Filtered indexes require these session options at creation and at every write. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.MaintenanceSpareParts', N'U') IS NULL
    THROW 50070, N'dbo.MaintenanceSpareParts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenanceOrderParts', N'U') IS NULL
    THROW 50071, N'dbo.MaintenanceOrderParts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.MaintenanceOrders', N'U') IS NULL
    THROW 50072, N'dbo.MaintenanceOrders is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.Suppliers', N'U') IS NULL
    THROW 50073, N'dbo.Suppliers is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
    THROW 50074, N'dbo.Customers is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 50075, N'dbo.Users is missing. Run sql/01_CreateDatabase.sql first.', 1;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50076, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
GO

BEGIN TRANSACTION;

/* 1) Spare-part master fields. Each ALTER is guarded so a re-run is a no-op. */
IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'SupplierId') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD SupplierId INT NULL
        CONSTRAINT FK_MaintenanceSpareParts_Suppliers FOREIGN KEY REFERENCES dbo.Suppliers(Id);

IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'Barcode') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD Barcode NVARCHAR(100) NULL;

IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'Unit') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD Unit NVARCHAR(30) NOT NULL
        CONSTRAINT DF_MaintenanceSpareParts_Unit DEFAULT N'قطعة';

IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'ReorderQuantity') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD ReorderQuantity INT NOT NULL
        CONSTRAINT DF_MaintenanceSpareParts_Reorder DEFAULT 0;

IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'StorageLocation') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD StorageLocation NVARCHAR(120) NOT NULL
        CONSTRAINT DF_MaintenanceSpareParts_Storage DEFAULT N'';

IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'Notes') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD Notes NVARCHAR(1000) NOT NULL
        CONSTRAINT DF_MaintenanceSpareParts_Notes DEFAULT N'';

IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'LastPurchaseAtUtc') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD LastPurchaseAtUtc DATETIME2(7) NULL;

IF COL_LENGTH(N'dbo.MaintenanceSpareParts', N'LastIssueAtUtc') IS NULL
    ALTER TABLE dbo.MaintenanceSpareParts ADD LastIssueAtUtc DATETIME2(7) NULL;
GO

/* Repair a half-applied upgrade: the column exists but its foreign key is missing. */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
                 AND name = N'FK_MaintenanceSpareParts_Suppliers')
    ALTER TABLE dbo.MaintenanceSpareParts WITH CHECK ADD CONSTRAINT FK_MaintenanceSpareParts_Suppliers
        FOREIGN KEY (SupplierId) REFERENCES dbo.Suppliers(Id);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
                 AND name = N'CK_MaintenanceSpareParts_Reorder')
    ALTER TABLE dbo.MaintenanceSpareParts WITH CHECK ADD CONSTRAINT CK_MaintenanceSpareParts_Reorder
        CHECK (ReorderQuantity >= 0);

/* 1b) Make sure an older order-parts table can reference a spare part. New installations get this
      column from sql/06; this repair keeps an in-place upgrade from sql/13-only consistent. */
IF COL_LENGTH(N'dbo.MaintenanceOrderParts', N'SparePartId') IS NULL
    ALTER TABLE dbo.MaintenanceOrderParts ADD SparePartId INT NULL
        CONSTRAINT FK_MaintenanceParts_SpareParts FOREIGN KEY REFERENCES dbo.MaintenanceSpareParts(Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
                 AND name = N'FK_MaintenanceParts_SpareParts')
    ALTER TABLE dbo.MaintenanceOrderParts WITH CHECK ADD CONSTRAINT FK_MaintenanceParts_SpareParts
        FOREIGN KEY (SparePartId) REFERENCES dbo.MaintenanceSpareParts(Id);

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
             AND name = N'CK_MaintenanceOrderParts_Source')
    ALTER TABLE dbo.MaintenanceOrderParts DROP CONSTRAINT CK_MaintenanceOrderParts_Source;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
                 AND name = N'CK_MaintenanceOrderParts_Source')
    ALTER TABLE dbo.MaintenanceOrderParts WITH CHECK ADD CONSTRAINT CK_MaintenanceOrderParts_Source
        CHECK (
            (IsManualPart = 1 AND SparePartId IS NULL)
            OR (IsManualPart = 0 AND (ProductId IS NOT NULL OR SparePartId IS NOT NULL)));

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrderParts')
                 AND name = N'IX_MaintenanceOrderParts_SparePart')
    CREATE INDEX IX_MaintenanceOrderParts_SparePart
        ON dbo.MaintenanceOrderParts(SparePartId)
        INCLUDE (MaintenanceOrderId, Quantity, UnitSellPrice)
        WHERE SparePartId IS NOT NULL;

/* 2) Append-only stock ledger. */
IF OBJECT_ID(N'dbo.MaintenanceSparePartMovements', N'U') IS NULL
CREATE TABLE dbo.MaintenanceSparePartMovements
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaintenanceSparePartMovements PRIMARY KEY,
    SparePartId INT NOT NULL
        CONSTRAINT FK_MaintenanceSparePartMovements_Parts FOREIGN KEY REFERENCES dbo.MaintenanceSpareParts(Id),
    MovementKind TINYINT NOT NULL,
    Quantity INT NOT NULL,
    QuantityBefore INT NOT NULL,
    QuantityAfter INT NOT NULL,
    UnitCost DECIMAL(19,2) NOT NULL CONSTRAINT DF_MaintenanceSparePartMovements_UnitCost DEFAULT 0,
    SupplierId INT NULL
        CONSTRAINT FK_MaintenanceSparePartMovements_Suppliers FOREIGN KEY REFERENCES dbo.Suppliers(Id),
    MaintenanceOrderId INT NULL
        CONSTRAINT FK_MaintenanceSparePartMovements_Orders FOREIGN KEY REFERENCES dbo.MaintenanceOrders(Id),
    CustomerId INT NULL
        CONSTRAINT FK_MaintenanceSparePartMovements_Customers FOREIGN KEY REFERENCES dbo.Customers(Id),
    Reference NVARCHAR(100) NOT NULL CONSTRAINT DF_MaintenanceSparePartMovements_Reference DEFAULT N'',
    Note NVARCHAR(1000) NOT NULL CONSTRAINT DF_MaintenanceSparePartMovements_Note DEFAULT N'',
    ActorUserId INT NOT NULL
        CONSTRAINT FK_MaintenanceSparePartMovements_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    OccurredAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_MaintenanceSparePartMovements_Occurred DEFAULT SYSUTCDATETIME(),
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_MaintenanceSparePartMovements_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_MaintenanceSparePartMovements_Kind CHECK (MovementKind BETWEEN 1 AND 6),
    CONSTRAINT CK_MaintenanceSparePartMovements_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_MaintenanceSparePartMovements_Balance CHECK (QuantityBefore >= 0 AND QuantityAfter >= 0),
    CONSTRAINT CK_MaintenanceSparePartMovements_Values CHECK (UnitCost >= 0)
);

/* 3) Opening balance for parts that existed before the ledger. Only rows with quantity > 0
      produce a movement because CK_Quantity requires a positive quantity. */
DECLARE @OpeningActor INT = (SELECT TOP (1) Id FROM dbo.Users ORDER BY Id);
IF @OpeningActor IS NOT NULL
BEGIN
    INSERT dbo.MaintenanceSparePartMovements
        (SparePartId, MovementKind, Quantity, QuantityBefore, QuantityAfter, UnitCost, SupplierId,
         MaintenanceOrderId, CustomerId, Reference, Note, ActorUserId, OccurredAtUtc)
    SELECT sp.Id, 1, sp.Quantity, 0, sp.Quantity, sp.CostPrice, sp.SupplierId,
           NULL, NULL, ISNULL(sp.Barcode, N''), N'رصيد افتتاحي عند تفعيل سجل حركات قطع الغيار.',
           @OpeningActor, SYSUTCDATETIME()
    FROM dbo.MaintenanceSpareParts AS sp
    WHERE sp.IsActive = 1
      AND sp.Quantity > 0
      AND NOT EXISTS (SELECT 1 FROM dbo.MaintenanceSparePartMovements AS m WHERE m.SparePartId = sp.Id);
END

/* 4) Indexes. Filtered unique barcode lets several parts have no barcode while rejecting a
      duplicate real barcode. */
/* Name is unique among ACTIVE parts only, so a deactivated part cannot reserve its name. */
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE parent_object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
             AND name = N'UQ_MaintenanceSpareParts_Name')
    ALTER TABLE dbo.MaintenanceSpareParts DROP CONSTRAINT UQ_MaintenanceSpareParts_Name;
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
             AND name = N'UX_MaintenanceSpareParts_Name'
             AND ISNULL(filter_definition, N'') NOT LIKE N'%IsActive%')
    DROP INDEX UX_MaintenanceSpareParts_Name ON dbo.MaintenanceSpareParts;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
                 AND name = N'UX_MaintenanceSpareParts_Name')
    CREATE UNIQUE INDEX UX_MaintenanceSpareParts_Name
        ON dbo.MaintenanceSpareParts(Name)
        WHERE IsActive = 1;

/* Repair an index created by an earlier build without the IsActive filter: a deactivated
   part must not keep its barcode reserved forever. */
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
             AND name = N'UX_MaintenanceSpareParts_Barcode'
             AND ISNULL(filter_definition, N'') NOT LIKE N'%IsActive%')
    DROP INDEX UX_MaintenanceSpareParts_Barcode ON dbo.MaintenanceSpareParts;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
                 AND name = N'UX_MaintenanceSpareParts_Barcode')
    CREATE UNIQUE INDEX UX_MaintenanceSpareParts_Barcode
        ON dbo.MaintenanceSpareParts(Barcode)
        WHERE Barcode IS NOT NULL AND Barcode <> N'' AND IsActive = 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
                 AND name = N'IX_MaintenanceSpareParts_Supplier')
    CREATE INDEX IX_MaintenanceSpareParts_Supplier
        ON dbo.MaintenanceSpareParts(SupplierId, Name)
        INCLUDE (Quantity, MinimumStockAlert, ReorderQuantity, CostPrice, SellPrice, Barcode, IsActive);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts')
                 AND name = N'IX_MaintenanceSpareParts_LowStock')
    CREATE INDEX IX_MaintenanceSpareParts_LowStock
        ON dbo.MaintenanceSpareParts(Quantity, MinimumStockAlert)
        INCLUDE (Name, Category, SupplierId, ReorderQuantity)
        WHERE IsActive = 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSparePartMovements')
                 AND name = N'IX_MaintenanceSparePartMovements_Part_Date')
    CREATE INDEX IX_MaintenanceSparePartMovements_Part_Date
        ON dbo.MaintenanceSparePartMovements(SparePartId, OccurredAtUtc DESC, Id DESC)
        INCLUDE (MovementKind, Quantity, QuantityBefore, QuantityAfter, UnitCost, SupplierId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSparePartMovements')
                 AND name = N'IX_MaintenanceSparePartMovements_Supplier_Date')
    CREATE INDEX IX_MaintenanceSparePartMovements_Supplier_Date
        ON dbo.MaintenanceSparePartMovements(SupplierId, OccurredAtUtc DESC)
        INCLUDE (SparePartId, Quantity, UnitCost)
        WHERE SupplierId IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSparePartMovements')
                 AND name = N'IX_MaintenanceSparePartMovements_Order')
    CREATE INDEX IX_MaintenanceSparePartMovements_Order
        ON dbo.MaintenanceSparePartMovements(MaintenanceOrderId)
        INCLUDE (SparePartId, Quantity, QuantityBefore, QuantityAfter)
        WHERE MaintenanceOrderId IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MaintenanceSparePartMovements')
                 AND name = N'IX_MaintenanceSparePartMovements_Customer_Date')
    CREATE INDEX IX_MaintenanceSparePartMovements_Customer_Date
        ON dbo.MaintenanceSparePartMovements(CustomerId, OccurredAtUtc DESC)
        INCLUDE (SparePartId, MaintenanceOrderId, Quantity)
        WHERE CustomerId IS NOT NULL;

COMMIT TRANSACTION;
GO

/* 5) Least-privilege grants. Movements stay append-only: the screen and the maintenance actions
      insert history but never edit or delete it. Parts can be deleted only when they have no
      history; the repository deactivates the rest. */
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MaintenanceSpareParts TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.MaintenanceSparePartMovements TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.Suppliers TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.Customers TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.Users TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MaintenanceOrderParts TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
GO

/* 6) Verification: the columns, table, constraints and indexes must all exist. */
IF EXISTS (
    SELECT expected.column_name
    FROM (VALUES
            (N'SupplierId'), (N'Barcode'), (N'Unit'), (N'ReorderQuantity'),
            (N'StorageLocation'), (N'Notes'), (N'LastPurchaseAtUtc'), (N'LastIssueAtUtc')
         ) AS expected(column_name)
    WHERE COL_LENGTH(N'dbo.MaintenanceSpareParts', expected.column_name) IS NULL)
    THROW 50077, N'One or more dbo.MaintenanceSpareParts columns are missing. Review the earlier SQL error.', 1;

IF OBJECT_ID(N'dbo.MaintenanceSparePartMovements', N'U') IS NULL
    THROW 50078, N'dbo.MaintenanceSparePartMovements was not created. Review the earlier SQL error.', 1;

IF EXISTS (
    SELECT expected.column_name
    FROM (VALUES
            (N'SparePartId'), (N'MovementKind'), (N'Quantity'), (N'QuantityBefore'), (N'QuantityAfter'),
            (N'UnitCost'), (N'SupplierId'), (N'MaintenanceOrderId'), (N'CustomerId'), (N'Reference'),
            (N'Note'), (N'ActorUserId'), (N'OccurredAtUtc')
         ) AS expected(column_name)
    WHERE COL_LENGTH(N'dbo.MaintenanceSparePartMovements', expected.column_name) IS NULL)
    THROW 50078, N'dbo.MaintenanceSparePartMovements is missing a required column. Review the earlier SQL error.', 1;

IF EXISTS (
    SELECT 1
    FROM (VALUES
            (N'UX_MaintenanceSpareParts_Barcode'),
            (N'UX_MaintenanceSpareParts_Name'),
            (N'IX_MaintenanceSpareParts_Supplier'),
            (N'IX_MaintenanceSpareParts_LowStock'),
            (N'IX_MaintenanceSparePartMovements_Part_Date'),
            (N'IX_MaintenanceSparePartMovements_Supplier_Date'),
            (N'IX_MaintenanceSparePartMovements_Order'),
            (N'IX_MaintenanceSparePartMovements_Customer_Date'),
            (N'IX_MaintenanceOrderParts_SparePart')
         ) AS expected(index_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.name = expected.index_name
          AND i.object_id IN (OBJECT_ID(N'dbo.MaintenanceSpareParts'),
                              OBJECT_ID(N'dbo.MaintenanceSparePartMovements'),
                              OBJECT_ID(N'dbo.MaintenanceOrderParts'))))
    THROW 50079, N'One or more spare-part indexes are missing. Review the earlier SQL error.', 1;

SELECT
    (SELECT COUNT(*) FROM dbo.MaintenanceSpareParts WHERE IsActive = 1) AS ActiveSpareParts,
    (SELECT COUNT(*) FROM dbo.MaintenanceSpareParts WHERE IsActive = 0) AS ArchivedSpareParts,
    (SELECT COUNT(*) FROM dbo.MaintenanceSparePartMovements) AS SparePartMovements,
    (SELECT COUNT(*) FROM dbo.MaintenanceSparePartMovements WHERE MovementKind = 2) AS PurchaseMovements,
    (SELECT COUNT(*) FROM dbo.MaintenanceSparePartMovements WHERE MovementKind = 3) AS IssueMovements,
    (SELECT COUNT(*) FROM dbo.MaintenanceSpareParts WHERE SupplierId IS NOT NULL) AS PartsWithSupplier,
    (SELECT COUNT(*) FROM dbo.MaintenanceSpareParts WHERE IsActive = 1 AND Quantity <= MinimumStockAlert) AS LowOrOutParts;

SELECT i.name AS IndexName, OBJECT_NAME(i.object_id) AS TableName
FROM sys.indexes AS i
WHERE i.name IN (N'UX_MaintenanceSpareParts_Barcode', N'UX_MaintenanceSpareParts_Name', N'IX_MaintenanceSpareParts_Supplier',
                 N'IX_MaintenanceSpareParts_LowStock', N'IX_MaintenanceSparePartMovements_Part_Date',
                 N'IX_MaintenanceSparePartMovements_Supplier_Date', N'IX_MaintenanceSparePartMovements_Order',
                 N'IX_MaintenanceSparePartMovements_Customer_Date', N'IX_MaintenanceOrderParts_SparePart')
ORDER BY TableName, IndexName;
GO

PRINT N'SUCCESS: maintenance spare-parts columns, movement ledger, indexes and grants are in place (sql/15_EnableMaintenanceSpareParts.sql).';
GO
