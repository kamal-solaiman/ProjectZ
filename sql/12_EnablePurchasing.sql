/* ============================================================================
   ProjectZ — صلاحيات شاشتي «شراء فاتورة» و«فواتير المشتريات» ودليل الموردين
   ----------------------------------------------------------------------------
   يمنح حساب الشبكة ProjectZClient ما تحتاجه شاشتا المشتريات فقط:
     • دليل الموردين: قراءة/إضافة/تعديل/حذف (الحذف يُستبدل بالأرشفة عند وجود سجل).
     • فاتورة الشراء: رأس الفاتورة وبنودها وربط الوحدات، والمدفوعات والأنشطة.
     • المرتجع للمورد: PurchaseReturns + PurchaseReturnLines.
     • المخزون الناتج عن التوريد/المرتجع: InventoryUnits + StockMovements.
     • الحركة المالية للسداد والاسترداد: FinancialAccounts + FinancialTransactions.
     • أرقام المستندات من DocumentSequences، والتدقيق في AuditLog.

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

/* 2) فاتورة الشراء: الرأس والبنود وربط الوحدات. */
IF OBJECT_ID(N'dbo.PurchaseInvoices', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.PurchaseInvoices TO [ProjectZClient];
IF OBJECT_ID(N'dbo.PurchaseInvoiceLines', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.PurchaseInvoiceLines TO [ProjectZClient];
IF OBJECT_ID(N'dbo.PurchaseInvoiceLineUnits', N'U') IS NOT NULL
    GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.PurchaseInvoiceLineUnits TO [ProjectZClient];
GO

/* 3) السداد والاسترداد وسجل الأنشطة. */
IF OBJECT_ID(N'dbo.PurchasePayments', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.PurchasePayments TO [ProjectZClient];
IF OBJECT_ID(N'dbo.PurchaseInvoiceActivities', N'U') IS NOT NULL
    GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.PurchaseInvoiceActivities TO [ProjectZClient];
GO

/* 4) المرتجع للمورد: رأس المرتجع وبنوده. */
IF OBJECT_ID(N'dbo.PurchaseReturns', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.PurchaseReturns TO [ProjectZClient];
IF OBJECT_ID(N'dbo.PurchaseReturnLines', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.PurchaseReturnLines TO [ProjectZClient];
GO

/* 5) المخزون: وحدات التوريد وحركاتها، وتحديث تكلفة الصنف. */
IF OBJECT_ID(N'dbo.InventoryUnits', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryUnits TO [ProjectZClient];
IF OBJECT_ID(N'dbo.StockMovements', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.StockMovements TO [ProjectZClient];
IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Products TO [ProjectZClient];
IF OBJECT_ID(N'dbo.Categories', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.Categories TO [ProjectZClient];
GO

/* 6) الحركة المالية: خصم السداد من الخزائن/المحافظ وإضافة الاسترداد. */
IF OBJECT_ID(N'dbo.FinancialAccounts', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
IF OBJECT_ID(N'dbo.FinancialTransactions', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
GO

/* 7) أرقام المستندات (PUR / PRT) والتدقيق. */
IF OBJECT_ID(N'dbo.DocumentSequences', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.DocumentSequences TO [ProjectZClient];
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NOT NULL
    GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.Users TO [ProjectZClient];
GO

PRINT N'SUCCESS: ProjectZClient can now post purchase invoices, supplier payments, returns and their stock/finance rows.';
GO
