/* Read-only reporting projections. Run after 06_CreateFullSchema.sql. */
USE [ProjectZ];
GO
SET NOCOUNT ON;
GO

CREATE OR ALTER VIEW dbo.vwProductStock
AS
SELECT
    p.Id AS ProductId,
    p.Name AS ProductName,
    p.Barcode,
    p.MinimumStockAlert,
    COALESCE(sm.QuantityOnHand, 0) AS QuantityOnHand,
    COALESCE(u.AvailableUnits, 0) AS AvailableSerializedUnits,
    COALESCE(u.DamagedUnits, 0) AS DamagedSerializedUnits
FROM dbo.Products AS p
OUTER APPLY
(
    SELECT SUM(m.QuantityDelta) AS QuantityOnHand
    FROM dbo.StockMovements AS m
    WHERE m.ProductId = p.Id
) AS sm
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN iu.UnitStatus = 0 THEN 1 ELSE 0 END) AS AvailableUnits,
        SUM(CASE WHEN iu.UnitStatus = 2 THEN 1 ELSE 0 END) AS DamagedUnits
    FROM dbo.InventoryUnits AS iu
    WHERE iu.ProductId = p.Id
) AS u;
GO

CREATE OR ALTER VIEW dbo.vwSalesInvoiceSummary
AS
SELECT
    si.Id AS SalesInvoiceId,
    si.InvoiceNumber,
    si.IssuedAtUtc,
    si.InvoiceStatus,
    si.CustomerId,
    si.CustomerNameSnapshot,
    si.CashierUserId,
    COALESCE(lines.Subtotal, 0) AS Subtotal,
    si.DiscountAmount,
    COALESCE(lines.NetSales, 0) - si.DiscountAmount AS NetSales,
    COALESCE(lines.NetCost, 0) AS NetCost,
    COALESCE(lines.NetSales, 0) - si.DiscountAmount - COALESCE(lines.NetCost, 0) AS NetProfit,
    COALESCE(payments.Collected, 0) AS CollectedAmount,
    COALESCE(payments.Refunded, 0) AS RefundedAmount,
    COALESCE(payments.Collected, 0) - COALESCE(payments.Refunded, 0) AS NetCollected,
    COALESCE(lines.NetSales, 0) - si.DiscountAmount - COALESCE(payments.Collected, 0) + COALESCE(payments.Refunded, 0) AS RemainingAmount
FROM dbo.SalesInvoices AS si
OUTER APPLY
(
    SELECT
        SUM(sl.UnitPrice * sl.Quantity) AS Subtotal,
        SUM(sl.UnitPrice * (sl.Quantity - sl.ReturnedQuantity)) AS NetSales,
        SUM(sl.UnitCost * (sl.Quantity - sl.ReturnedQuantity)) AS NetCost
    FROM dbo.SalesInvoiceLines AS sl
    WHERE sl.SalesInvoiceId = si.Id
) AS lines
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN sp.PaymentKind = 1 THEN sp.Amount ELSE 0 END) AS Collected,
        SUM(CASE WHEN sp.PaymentKind = 2 THEN sp.Amount ELSE 0 END) AS Refunded
    FROM dbo.SalesPayments AS sp
    WHERE sp.SalesInvoiceId = si.Id
) AS payments;
GO

CREATE OR ALTER VIEW dbo.vwPurchaseInvoiceSummary
AS
SELECT
    pi.Id AS PurchaseInvoiceId,
    pi.InvoiceNumber,
    pi.SupplierInvoiceNumber,
    pi.IssuedAtUtc,
    pi.InvoiceStatus,
    pi.SupplierId,
    pi.SupplierNameSnapshot,
    COALESCE(lines.Subtotal, 0) AS Subtotal,
    pi.DiscountAmount,
    COALESCE(lines.NetPurchases, 0) - pi.DiscountAmount AS NetPurchases,
    COALESCE(payments.Paid, 0) AS PaidAmount,
    COALESCE(payments.RefundReceived, 0) AS SupplierRefundReceived,
    COALESCE(payments.Paid, 0) - COALESCE(payments.RefundReceived, 0) AS NetPaid,
    COALESCE(lines.NetPurchases, 0) - pi.DiscountAmount - COALESCE(payments.Paid, 0) + COALESCE(payments.RefundReceived, 0) AS RemainingPayable
FROM dbo.PurchaseInvoices AS pi
OUTER APPLY
(
    SELECT
        SUM(pl.UnitPurchaseCost * pl.Quantity) AS Subtotal,
        SUM(pl.UnitPurchaseCost * (pl.Quantity - pl.ReturnedQuantity)) AS NetPurchases
    FROM dbo.PurchaseInvoiceLines AS pl
    WHERE pl.PurchaseInvoiceId = pi.Id
) AS lines
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN pp.PaymentKind = 1 THEN pp.Amount ELSE 0 END) AS Paid,
        SUM(CASE WHEN pp.PaymentKind = 2 THEN pp.Amount ELSE 0 END) AS RefundReceived
    FROM dbo.PurchasePayments AS pp
    WHERE pp.PurchaseInvoiceId = pi.Id
) AS payments;
GO

CREATE OR ALTER VIEW dbo.vwMaintenanceOrderSummary
AS
SELECT
    mo.Id AS MaintenanceOrderId,
    mo.OrderNumber,
    mo.OrderStatus,
    mo.ReceivedAtUtc,
    mo.CustomerId,
    mo.CustomerNameSnapshot,
    mo.DeviceType,
    mo.DeviceBrand,
    mo.DeviceModel,
    mo.LaborCost,
    COALESCE(parts.PartsTotal, 0) AS PartsTotal,
    COALESCE(parts.PartsCostTotal, 0) AS PartsCostTotal,
    mo.DiscountAmount,
    CASE
        WHEN mo.HasWorkshopPricing = 1 AND mo.FinalServiceCost IS NOT NULL THEN mo.FinalServiceCost
        WHEN mo.HasWorkshopPricing = 1 AND mo.LaborCost + COALESCE(parts.PartsTotal, 0) - mo.DiscountAmount < 0 THEN 0
        WHEN mo.HasWorkshopPricing = 1 THEN mo.LaborCost + COALESCE(parts.PartsTotal, 0) - mo.DiscountAmount
        ELSE mo.EstimatedCost
    END AS ServiceTotal,
    COALESCE(payments.DepositCollected, 0) AS DepositCollected,
    COALESCE(payments.DepositRefunded, 0) AS DepositRefunded,
    COALESCE(payments.FinalCollected, 0) AS FinalCollected,
    COALESCE(payments.Collected, 0) - COALESCE(payments.Refunded, 0) AS NetCollected
FROM dbo.MaintenanceOrders AS mo
OUTER APPLY
(
    SELECT
        SUM(mp.Quantity * mp.UnitSellPrice) AS PartsTotal,
        SUM(mp.Quantity * mp.UnitCost) AS PartsCostTotal
    FROM dbo.MaintenanceOrderParts AS mp
    WHERE mp.MaintenanceOrderId = mo.Id
) AS parts
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN mp.PaymentKind = 1 THEN mp.Amount ELSE 0 END) AS Collected,
        SUM(CASE WHEN mp.PaymentKind = 2 THEN mp.Amount ELSE 0 END) AS Refunded,
        SUM(CASE WHEN mp.PaymentStage = 1 AND mp.PaymentKind = 1 THEN mp.Amount ELSE 0 END) AS DepositCollected,
        SUM(CASE WHEN mp.PaymentStage = 1 AND mp.PaymentKind = 2 THEN mp.Amount ELSE 0 END) AS DepositRefunded,
        SUM(CASE WHEN mp.PaymentStage = 2 AND mp.PaymentKind = 1 THEN mp.Amount ELSE 0 END) AS FinalCollected
    FROM dbo.MaintenancePayments AS mp
    WHERE mp.MaintenanceOrderId = mo.Id
) AS payments;
GO

CREATE OR ALTER VIEW dbo.vwFinancialAccountDailyUsage
AS
SELECT
    ft.FinancialAccountId,
    CONVERT(date, ft.OccurredAtUtc) AS UsageDateUtc,
    SUM(CASE WHEN ft.IsIncoming = 1 AND ft.CountsTowardLimits = 1 THEN ft.Amount ELSE 0 END) AS DepositedAmount,
    SUM(CASE WHEN ft.IsIncoming = 0 AND ft.CountsTowardLimits = 1 THEN ft.Amount ELSE 0 END) AS WithdrawnAmount
FROM dbo.FinancialTransactions AS ft
GROUP BY ft.FinancialAccountId, CONVERT(date, ft.OccurredAtUtc);
GO

PRINT N'ProjectZ reporting views created/verified successfully.';
GO
