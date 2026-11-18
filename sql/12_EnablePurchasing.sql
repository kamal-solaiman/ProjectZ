/* ============================================================================
   ProjectZ — صلاحيات شاشتي «شراء فاتورة» و«فواتير المشتريات» ودليل الموردين
   ----------------------------------------------------------------------------
   يمنح حساب الشبكة ProjectZClient ما تحتاجه شاشتا المشتريات فقط:
     • دليل الموردين: قراءة/إضافة/تعديل/حذف (الحذف يُستبدل بالأرشفة عند وجود سجل).
     • قراءة مستندات المشتريات وسدادها ومرتجعاتها وبيانات المخزون والحسابات.
     • تسوية المستندات تتم عبر إجراءات sql/25 فقط، لا عبر كتابة مباشرة من الكاشير.

   Idempotent: كل GRANT يُعاد تنفيذه بأمان، ولا يُنشئ جدولًا ولا يعدّل صفًا.
   يُنفَّذ بحساب إداري بعد sql/06 ثم sql/09 (أو 11).
   ========================================================================== */
SET NOCOUNT ON;
GO

USE [ProjectZ];
GO

IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50070, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
IF OBJECT_ID(N'dbo.PurchaseInvoices', N'U') IS NULL
    THROW 50071, N'dbo.PurchaseInvoices is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.PurchaseReturns', N'U') IS NULL
    THROW 50072, N'dbo.PurchaseReturns is missing. Run sql/06_CreateFullSchema.sql first.', 1;
GO

/* 1) دليل الموردين: الشاشة تقرأ وتضيف وتعدّل، والحذف النهائي مسموح لسجل بلا فواتير فقط. */
IF OBJECT_ID(N'dbo.Suppliers', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Suppliers TO [ProjectZClient];
GO

/* 2–4) The client reads the purchase ledger. sql/25 owns all document writes. */
GRANT SELECT ON OBJECT::dbo.PurchaseInvoices TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceLines TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceLineUnits TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchasePayments TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceActivities TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseReturns TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseReturnLines TO [ProjectZClient];
GO

/* 5) Remaining reads for the directory/screens. Stock and cash posting is owned by 25.
      Do not grant new direct ledger access here, or revoke shared permissions of other modules. */
GRANT SELECT ON OBJECT::dbo.InventoryUnits TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.StockMovements TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.Products TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.Categories TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.Users TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.MaintenanceOrderParts TO [ProjectZClient];
GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO
PRINT N'SUCCESS: purchase directory and read permissions ready. Apply sql/25_EnablePurchaseSettlement.sql before opening tills.';
GO
