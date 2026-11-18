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
    COALESCE(u.DamagedUnits, 0) AS DamagedSerializedUnits,
    COALESCE(sm.QuantityOnlyDamaged, 0) AS QuantityOnlyDamaged
FROM dbo.Products AS p
OUTER APPLY
(
    SELECT SUM(m.QuantityDelta) AS QuantityOnHand,
           SUM(CASE WHEN m.InventoryUnitId IS NULL AND m.MovementType=N'Damage' AND m.SourceEntityType=N'SalesInvoice'
               THEN -m.QuantityDelta ELSE 0 END) AS QuantityOnlyDamaged
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

-- Round a cumulative non-negative share to cents, without rounding a unit price or
-- losing the final cent. Integer remainder avoids decimal division near a half cent.
CREATE OR ALTER FUNCTION dbo.SaleShare(@Amount decimal(19,2), @Part decimal(19,0), @Whole decimal(19,0))
RETURNS decimal(19,2)
AS
BEGIN
    IF @Whole = 0 RETURN 0;
    DECLARE @Numerator decimal(38,0) = CONVERT(decimal(19,0), @Amount * 100) * @Part;
    DECLARE @Remainder decimal(19,0) = @Numerator % @Whole;
    DECLARE @Cents decimal(19,0) = (@Numerator - @Remainder) / @Whole;
    RETURN (@Cents + CASE WHEN 2 * @Remainder >= @Whole THEN 1 ELSE 0 END) * 0.01;
END;
GO

-- Only immutable facts from the original sale take part in discount allocation.
-- Adding a replacement or returning a unit never redistributes an earlier line's discount.
CREATE OR ALTER VIEW dbo.vwSalesLineValuation
AS
WITH gross AS (
    SELECT l.*, CONVERT(decimal(19,2), l.UnitPrice * l.Quantity) AS GrossAmount,
           CASE WHEN l.IsExchangeReplacement=0 THEN l.UnitPrice * l.Quantity ELSE 0 END AS OriginalGross
    FROM dbo.SalesInvoiceLines l
), shares AS (
    SELECT g.*, SUM(OriginalGross) OVER (PARTITION BY SalesInvoiceId) AS Basis,
           SUM(OriginalGross) OVER (PARTITION BY SalesInvoiceId ORDER BY LineNumber ROWS UNBOUNDED PRECEDING) AS ToHere
    FROM gross g
)
SELECT s.Id, s.SalesInvoiceId, s.GrossAmount, n.NetLineAmount,
       dbo.SaleShare(n.NetLineAmount, s.ReturnedQuantity, s.Quantity) AS ReturnedNetAmount,
       CONVERT(decimal(19,2),s.UnitCost * (s.Quantity - s.ReturnedQuantity)) AS RemainingCost
FROM shares s JOIN dbo.SalesInvoices i ON i.Id=s.SalesInvoiceId
CROSS APPLY (SELECT CONVERT(decimal(19,0),s.Basis * 100) AS Whole,
                    CONVERT(decimal(19,0),s.ToHere * 100) AS ToCents,
                    CONVERT(decimal(19,0),s.OriginalGross * 100) AS LineCents) c
CROSS APPLY (SELECT CONVERT(decimal(19,2),s.GrossAmount -
    (dbo.SaleShare(i.DiscountAmount,c.ToCents,c.Whole) -
     dbo.SaleShare(i.DiscountAmount,c.ToCents-c.LineCents,c.Whole))) AS NetLineAmount) n;
GO

CREATE OR ALTER VIEW dbo.vwSalesInvoiceSummary
AS
SELECT si.Id AS SalesInvoiceId, si.InvoiceNumber, si.IssuedAtUtc, si.InvoiceStatus, si.CustomerId,
       si.CustomerNameSnapshot, si.CashierUserId, COALESCE(lines.Subtotal,0) AS Subtotal, si.DiscountAmount,
       n.NetSales, n.NetCost, n.NetSales - n.NetCost AS NetProfit,
       COALESCE(payments.Collected,0) AS CollectedAmount, COALESCE(payments.Refunded,0) AS RefundedAmount,
       n.NetCollected, n.NetSales - n.NetCollected AS RemainingAmount
FROM dbo.SalesInvoices si
OUTER APPLY (
    SELECT SUM(v.GrossAmount) AS Subtotal, SUM(v.NetLineAmount-v.ReturnedNetAmount) AS NetSales,
           SUM(v.RemainingCost) AS NetCost FROM dbo.vwSalesLineValuation v WHERE v.SalesInvoiceId=si.Id
) lines
OUTER APPLY (
    SELECT CONVERT(decimal(19,2),SUM(CASE WHEN PaymentKind=1 THEN Amount ELSE 0 END)) AS Collected,
           CONVERT(decimal(19,2),SUM(CASE WHEN PaymentKind=2 THEN Amount ELSE 0 END)) AS Refunded
    FROM dbo.SalesPayments WHERE SalesInvoiceId=si.Id
) payments
CROSS APPLY (SELECT CONVERT(decimal(19,2),CASE WHEN si.InvoiceStatus IN (3,4) THEN 0 ELSE COALESCE(lines.NetSales,0) END) AS NetSales,
                    CONVERT(decimal(19,2),CASE WHEN si.InvoiceStatus IN (3,4) THEN 0 ELSE COALESCE(lines.NetCost,0) END) AS NetCost,
                    CONVERT(decimal(19,2),COALESCE(payments.Collected,0)-COALESCE(payments.Refunded,0)) AS NetCollected) n;
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
    price.ServiceTotal,
    COALESCE(payments.DepositCollected, 0) AS DepositCollected,
    COALESCE(payments.DepositRefunded, 0) AS DepositRefunded,
    COALESCE(payments.FinalCollected, 0) AS FinalCollected,
    paid.NetCollected,
    CASE WHEN mo.OrderStatus<>6 AND price.ServiceTotal>paid.NetCollected THEN price.ServiceTotal-paid.NetCollected ELSE 0 END AS RemainingDue,
    CASE WHEN mo.OrderStatus<>6 AND (mo.HasWorkshopPricing=1 OR mo.IsEstimatePendingInspection=0) AND paid.NetCollected>price.ServiceTotal
         THEN paid.NetCollected-price.ServiceTotal ELSE 0 END AS ExcessPaid
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
        SUM(CASE WHEN mp.PaymentKind = 1 THEN mp.Amount ELSE -mp.Amount END) AS Net,
        SUM(CASE WHEN mp.PaymentStage = 1 AND mp.PaymentKind = 1 THEN mp.Amount ELSE 0 END) AS DepositCollected,
        SUM(CASE WHEN mp.PaymentStage = 1 AND mp.PaymentKind = 2 THEN mp.Amount ELSE 0 END) AS DepositRefunded,
        SUM(CASE WHEN mp.PaymentStage = 2 AND mp.PaymentKind = 1 THEN mp.Amount ELSE 0 END) AS FinalCollected
    FROM dbo.MaintenancePayments AS mp
    WHERE mp.MaintenanceOrderId = mo.Id
) AS payments
CROSS APPLY (SELECT CONVERT(decimal(19,2), COALESCE(payments.Net,0)) AS NetCollected) AS paid
CROSS APPLY (SELECT mo.LaborCost + CONVERT(decimal(19,2),COALESCE(parts.PartsTotal,0)) - mo.DiscountAmount AS WorkshopTotal) AS calc
CROSS APPLY (SELECT CONVERT(decimal(19,2), CASE
    WHEN mo.HasWorkshopPricing=1 AND mo.OrderStatus>=5 AND mo.FinalServiceCost IS NOT NULL THEN mo.FinalServiceCost
    WHEN mo.HasWorkshopPricing=1 THEN CASE WHEN calc.WorkshopTotal<0 THEN 0 ELSE calc.WorkshopTotal END
    WHEN mo.IsEstimatePendingInspection=1 THEN 0 ELSE mo.EstimatedCost END) AS ServiceTotal) AS price;
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
