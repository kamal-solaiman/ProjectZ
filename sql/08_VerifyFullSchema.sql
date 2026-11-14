/* Read-only structural verification after 06 and 07. */
USE [ProjectZ];
GO
SET NOCOUNT ON;

DECLARE @ExpectedTables TABLE (name SYSNAME PRIMARY KEY);
INSERT @ExpectedTables(name) VALUES
(N'Users'), (N'Roles'), (N'Permissions'), (N'UserRoles'), (N'RolePermissions'),
(N'SystemSettings'), (N'DocumentSequences'), (N'Customers'), (N'Suppliers'),
(N'Categories'), (N'Products'), (N'FinancialAccounts'), (N'CustomerTransfers'),
(N'FinancialTransactions'), (N'InventoryUnits'), (N'StockMovements'),
(N'PurchaseInvoices'), (N'PurchaseInvoiceLines'), (N'PurchaseInvoiceLineUnits'),
(N'PurchaseReturns'), (N'PurchaseReturnLines'), (N'PurchasePayments'),
(N'PurchaseInvoiceActivities'), (N'SalesInvoices'), (N'SalesInvoiceLines'),
(N'SalesInvoiceLineUnits'), (N'SalesPayments'), (N'SalesInvoiceActivities'),
(N'ReturnExchanges'), (N'ReturnExchangeLines'), (N'ReturnExchangeUnits'),
(N'MaintenanceOrders'), (N'MaintenanceOrderParts'), (N'MaintenancePayments'),
(N'MaintenanceActivities'), (N'MaintenanceSpareParts'), (N'MaintenanceSparePartMovements'),
(N'AuditLog');

SELECT e.name AS MissingTable
FROM @ExpectedTables AS e
LEFT JOIN sys.tables AS t ON t.name = e.name AND t.schema_id = SCHEMA_ID(N'dbo')
WHERE t.object_id IS NULL;

SELECT expected.v AS MissingView
FROM (VALUES (N'vwProductStock'), (N'vwSalesInvoiceSummary'), (N'vwPurchaseInvoiceSummary'),
             (N'vwMaintenanceOrderSummary'), (N'vwFinancialAccountDailyUsage')) AS expected(v)
LEFT JOIN sys.views AS sv ON sv.name = expected.v AND sv.schema_id = SCHEMA_ID(N'dbo')
WHERE sv.object_id IS NULL;

SELECT expected.ColumnName AS MissingInventoryColumn
FROM (VALUES (N'PurchaseInvoiceNumber'), (N'SaleInvoiceNumber')) AS expected(ColumnName)
LEFT JOIN sys.columns AS c
 ON c.object_id = OBJECT_ID(N'dbo.InventoryUnits') AND c.name = expected.ColumnName
WHERE c.object_id IS NULL;

SELECT expected.ColumnName AS MissingSparePartColumn
FROM (VALUES (N'SupplierId'), (N'Barcode'), (N'Unit'), (N'ReorderQuantity'),
             (N'StorageLocation'), (N'Notes'), (N'LastPurchaseAtUtc'), (N'LastIssueAtUtc')) AS expected(ColumnName)
LEFT JOIN sys.columns AS c
 ON c.object_id = OBJECT_ID(N'dbo.MaintenanceSpareParts') AND c.name = expected.ColumnName
WHERE c.object_id IS NULL;

SELECT fk.name, fk.is_disabled, fk.is_not_trusted
FROM sys.foreign_keys AS fk
WHERE fk.is_disabled = 1 OR fk.is_not_trusted = 1;

SELECT t.name AS TableName, c.column_id, c.name AS ColumnName
FROM sys.tables AS t
JOIN sys.columns AS c ON c.object_id = t.object_id
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name IN (N'CustomerTransfers', N'FinancialTransactions')
  AND c.name LIKE N'%Reference%';

SELECT COUNT(*) AS ActualBusinessTableCount
FROM sys.tables
WHERE schema_id = SCHEMA_ID(N'dbo') AND is_ms_shipped = 0;

SELECT t.name AS TableName, COUNT(c.column_id) AS ColumnCount
FROM sys.tables AS t
JOIN sys.columns AS c ON c.object_id = t.object_id
WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.is_ms_shipped = 0
GROUP BY t.name
ORDER BY t.name;

SELECT fk.name AS ForeignKeyName, OBJECT_NAME(fk.parent_object_id) AS ChildTable,
       OBJECT_NAME(fk.referenced_object_id) AS ParentTable, fk.delete_referential_action_desc
FROM sys.foreign_keys AS fk
ORDER BY ChildTable, ForeignKeyName;
GO
