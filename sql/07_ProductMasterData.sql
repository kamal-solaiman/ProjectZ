-- جداول المنتجات والتصنيفات والماركات (إضافة منتجات / المخزون / مشتملات الفواتير)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductCategories
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductCategories PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL CONSTRAINT UQ_ProductCategories_Name UNIQUE,
        NameAr NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_ProductCategories_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ProductCategories_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;

IF OBJECT_ID(N'dbo.ProductBrands', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductBrands
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductBrands PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL CONSTRAINT UQ_ProductBrands_Name UNIQUE,
        NameAr NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_ProductBrands_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ProductBrands_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Products
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
        Barcode NVARCHAR(100) NOT NULL CONSTRAINT UQ_Products_Barcode UNIQUE,
        NameAr NVARCHAR(200) NOT NULL,
        NameEn NVARCHAR(200) NULL,
        CategoryId INT NULL CONSTRAINT FK_Products_CategoryId FOREIGN KEY REFERENCES dbo.ProductCategories(Id),
        BrandId INT NULL CONSTRAINT FK_Products_BrandId FOREIGN KEY REFERENCES dbo.ProductBrands(Id),
        CostPrice DECIMAL(18,4) NOT NULL CONSTRAINT DF_Products_CostPrice DEFAULT (0),
        PriceWholesale DECIMAL(18,4) NOT NULL CONSTRAINT DF_Products_PriceWholesale DEFAULT (0),
        PriceSpecial DECIMAL(18,4) NOT NULL CONSTRAINT DF_Products_PriceSpecial DEFAULT (0),
        PriceRetail DECIMAL(18,4) NOT NULL CONSTRAINT DF_Products_PriceRetail DEFAULT (0),
        MinStock INT NOT NULL CONSTRAINT DF_Products_MinStock DEFAULT (0),
        CurrentStock INT NOT NULL CONSTRAINT DF_Products_CurrentStock DEFAULT (0),
        Unit NVARCHAR(20) NOT NULL CONSTRAINT DF_Products_Unit DEFAULT (N'قطعة'),
        WarrantyMonths INT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Products_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NULL,
        CONSTRAINT CHK_Products_Prices_NonNegative CHECK (CostPrice >= 0 AND PriceWholesale >= 0 AND PriceSpecial >= 0 AND PriceRetail >= 0)
    );
END;

CREATE INDEX IX_Products_Barcode ON dbo.Products(Barcode);
CREATE INDEX IX_Products_CategoryId ON dbo.Products(CategoryId);
CREATE INDEX IX_Products_BrandId ON dbo.Products(BrandId);
CREATE INDEX IX_Products_IsActive ON dbo.Products(IsActive);

COMMIT TRANSACTION;
GO
