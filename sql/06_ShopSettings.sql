-- جدول إعدادات المحل والهوية التجارية (قابلة للتعديل لكل محل بحرية)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ShopSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShopSettings
    (
        Id INT NOT NULL CONSTRAINT PK_ShopSettings PRIMARY KEY DEFAULT (1),
        ShopNameAr NVARCHAR(200) NOT NULL CONSTRAINT DF_ShopSettings_ShopNameAr DEFAULT (N'فيتوح فون'),
        ShopNameEn NVARCHAR(200) NOT NULL CONSTRAINT DF_ShopSettings_ShopNameEn DEFAULT (N'FitohPhone Store'),
        Phone1 NVARCHAR(20) NOT NULL CONSTRAINT DF_ShopSettings_Phone1 DEFAULT (N'01012345678'),
        Phone2 NVARCHAR(20) NULL,
        WhatsAppNumber NVARCHAR(20) NULL,
        Address NVARCHAR(500) NULL,
        TaxNumber NVARCHAR(50) NULL,
        CommercialRegisterNumber NVARCHAR(50) NULL,
        LogoPath NVARCHAR(500) NULL,
        ReceiptHeaderText NVARCHAR(1000) NULL,
        ReceiptFooterText NVARCHAR(1000) NULL,
        DefaultCurrency NVARCHAR(20) NOT NULL CONSTRAINT DF_ShopSettings_DefaultCurrency DEFAULT (N'ج.م (EGP)'),
        ThermalPrinterName NVARCHAR(200) NULL,
        BarcodePrinterName NVARCHAR(200) NULL,
        AutoPrintInvoice BIT NOT NULL CONSTRAINT DF_ShopSettings_AutoPrintInvoice DEFAULT (1),
        LowStockAlert BIT NOT NULL CONSTRAINT DF_ShopSettings_LowStockAlert DEFAULT (1),
        TransferLimitWarning BIT NOT NULL CONSTRAINT DF_ShopSettings_TransferLimitWarning DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ShopSettings_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NULL,
        CONSTRAINT CHK_ShopSettings_SingleRow CHECK (Id = 1)
    );
END;

COMMIT TRANSACTION;
GO
