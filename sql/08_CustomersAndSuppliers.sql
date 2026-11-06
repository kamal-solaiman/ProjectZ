-- جداول العملاء والموردين مع كشف الحساب والتقارير الشاملة
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customers
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        Phone NVARCHAR(20) NOT NULL CONSTRAINT UQ_Customers_Phone UNIQUE,
        AlternatePhone NVARCHAR(20) NULL,
        Address NVARCHAR(500) NULL,
        City NVARCHAR(100) NULL,
        Email NVARCHAR(200) NULL,
        TotalPurchases DECIMAL(18,4) NOT NULL CONSTRAINT DF_Customers_TotalPurchases DEFAULT (0),
        TotalReturns DECIMAL(18,4) NOT NULL CONSTRAINT DF_Customers_TotalReturns DEFAULT (0),
        OutstandingBalance DECIMAL(18,4) NOT NULL CONSTRAINT DF_Customers_OutstandingBalance DEFAULT (0),
        LastVisit DATETIME2(7) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NULL
    );
END;

IF OBJECT_ID(N'dbo.Suppliers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Suppliers
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        Phone NVARCHAR(20) NOT NULL CONSTRAINT UQ_Suppliers_Phone UNIQUE,
        ContactPerson NVARCHAR(200) NULL,
        Address NVARCHAR(500) NULL,
        City NVARCHAR(100) NULL,
        Email NVARCHAR(200) NULL,
        TotalPurchases DECIMAL(18,4) NOT NULL CONSTRAINT DF_Suppliers_TotalPurchases DEFAULT (0),
        TotalPaid DECIMAL(18,4) NOT NULL CONSTRAINT DF_Suppliers_TotalPaid DEFAULT (0),
        OutstandingBalance DECIMAL(18,4) NOT NULL CONSTRAINT DF_Suppliers_OutstandingBalance DEFAULT (0),
        IsActive BIT NOT NULL CONSTRAINT DF_Suppliers_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Suppliers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NULL
    );
END;

CREATE INDEX IX_Customers_Phone ON dbo.Customers(Phone);
CREATE INDEX IX_Customers_IsActive ON dbo.Customers(IsActive);
CREATE INDEX IX_Suppliers_Phone ON dbo.Suppliers(Phone);
CREATE INDEX IX_Suppliers_IsActive ON dbo.Suppliers(IsActive);

COMMIT TRANSACTION;
GO
