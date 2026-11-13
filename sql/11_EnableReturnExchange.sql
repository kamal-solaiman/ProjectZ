/* ============================================================================
   ProjectZ — صلاحيات شاشة «المرتجع والاستبدال»
   ----------------------------------------------------------------------------
   يمنح حساب الشبكة ProjectZClient ما تحتاجه شاشة المرتجع والاستبدال فقط:
   قراءة/كتابة عملية المرتجع أو الاستبدال وبنودها ووحداتها، وقراءة الفواتير
   والمدفوعات والأنشطة، وتحديث الوحدات وحركات المخزون.
   Idempotent: كل GRANT يُعاد تنفيذه بأمان، ولا يُنشئ جدولًا ولا يعدّل صفًا.
   يُنفَّذ بحساب إداري بعد sql/06 ثم sql/09.
   ========================================================================== */
SET NOCOUNT ON;
GO

USE [ProjectZ];
GO

IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50060, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
IF OBJECT_ID(N'dbo.ReturnExchanges', N'U') IS NULL
    THROW 50061, N'dbo.ReturnExchanges is missing. Run sql/06_CreateFullSchema.sql first.', 1;
GO

/* 1) عمليات المرتجع والاستبدال (رأس العملية + البنود + الوحدات). */
IF OBJECT_ID(N'dbo.ReturnExchanges', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchanges TO [ProjectZClient];
IF OBJECT_ID(N'dbo.ReturnExchangeLines', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchangeLines TO [ProjectZClient];
IF OBJECT_ID(N'dbo.ReturnExchangeUnits', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchangeUnits TO [ProjectZClient];
GO

/* 2) الفواتير التي تُقرأ وتُعدَّل بالمرتجع (الكميات المرتجعة والحالة والتسديد). */
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

/* 3) الحركة المالية لفرق الاستبدال (تحصيل أو رد) والحسابات. */
IF OBJECT_ID(N'dbo.FinancialAccounts', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
IF OBJECT_ID(N'dbo.FinancialTransactions', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
GO

/* 4) المخزون: إعادة الوحدة للمتاح أو تحويلها لهالك + حركة المخزون. */
IF OBJECT_ID(N'dbo.InventoryUnits', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.InventoryUnits TO [ProjectZClient];
IF OBJECT_ID(N'dbo.StockMovements', N'U') IS NOT NULL
    GRANT SELECT, INSERT ON OBJECT::dbo.StockMovements TO [ProjectZClient];
IF OBJECT_ID(N'dbo.DocumentSequences', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.DocumentSequences TO [ProjectZClient];
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NOT NULL
    GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

PRINT N'ProjectZ: صلاحيات شاشة المرتجع والاستبدال جاهزة (ReturnExchanges, ReturnExchangeLines, ReturnExchangeUnits).';
GO
