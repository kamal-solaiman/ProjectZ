-- جداول فواتير الشراء من التجار مع الدفع أجزاء (نقدي / فودافون كاش / إنستاباي / آجل)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.PurchaseInvoices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseInvoices
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoices PRIMARY KEY,
        InvoiceNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_PurchaseInvoices_InvoiceNumber UNIQUE,
        InvoiceDate DATETIME2(7) NOT NULL CONSTRAINT DF_PurchaseInvoices_InvoiceDate DEFAULT (SYSUTCDATETIME()),
        SupplierId INT NOT NULL CONSTRAINT FK_PurchaseInvoices_SupplierId FOREIGN KEY REFERENCES dbo.Suppliers(Id),
        UserId INT NOT NULL CONSTRAINT FK_PurchaseInvoices_UserId FOREIGN KEY REFERENCES dbo.Users(Id),
        SubTotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_PurchaseInvoices_SubTotal DEFAULT (0),
        GrandTotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_PurchaseInvoices_GrandTotal DEFAULT (0),
        PaidAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_PurchaseInvoices_PaidAmount DEFAULT (0),
        RemainingAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_PurchaseInvoices_RemainingAmount DEFAULT (0),
        PaymentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_PurchaseInvoices_PaymentStatus DEFAULT (N'غير مدفوع'),
        Notes NVARCHAR(1000) NULL,
        IsCancelled BIT NOT NULL CONSTRAINT DF_PurchaseInvoices_IsCancelled DEFAULT (0),
        CancelledAt DATETIME2(7) NULL,
        CancelledByUserId INT NULL CONSTRAINT FK_PurchaseInvoices_CancelledBy FOREIGN KEY REFERENCES dbo.Users(Id),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_PurchaseInvoices_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CHK_PurchaseInvoices_Amounts_NonNegative CHECK (SubTotal >= 0 AND GrandTotal >= 0)
    );
END;

IF OBJECT_ID(N'dbo.PurchaseInvoiceItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseInvoiceItems
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoiceItems PRIMARY KEY,
        InvoiceId INT NOT NULL CONSTRAINT FK_PurchaseInvoiceItems_InvoiceId FOREIGN KEY REFERENCES dbo.PurchaseInvoices(Id) ON DELETE CASCADE,
        ProductId INT NOT NULL CONSTRAINT FK_PurchaseInvoiceItems_ProductId FOREIGN KEY REFERENCES dbo.Products(Id),
        Quantity INT NOT NULL CONSTRAINT DF_PurchaseInvoiceItems_Quantity DEFAULT (1),
        UnitCost DECIMAL(18,4) NOT NULL CONSTRAINT DF_PurchaseInvoiceItems_UnitCost DEFAULT (0),
        LineTotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_PurchaseInvoiceItems_LineTotal DEFAULT (0),
        CONSTRAINT CHK_PurchaseInvoiceItems_Qty_NonNegative CHECK (Quantity > 0),
        CONSTRAINT CHK_PurchaseInvoiceItems_Cost_NonNegative CHECK (UnitCost >= 0 AND LineTotal >= 0)
    );
END;

IF OBJECT_ID(N'dbo.PurchaseInvoicePayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseInvoicePayments
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoicePayments PRIMARY KEY,
        InvoiceId INT NOT NULL CONSTRAINT FK_PurchaseInvoicePayments_InvoiceId FOREIGN KEY REFERENCES dbo.PurchaseInvoices(Id) ON DELETE CASCADE,
        PaymentMethod NVARCHAR(50) NOT NULL,
        PaymentMethodAr NVARCHAR(50) NOT NULL,
        Amount DECIMAL(18,4) NOT NULL CONSTRAINT DF_PurchaseInvoicePayments_Amount DEFAULT (0),
        PaidAt DATETIME2(7) NOT NULL CONSTRAINT DF_PurchaseInvoicePayments_PaidAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CHK_PurchaseInvoicePayments_Amount_Positive CHECK (Amount > 0)
    );
END;

CREATE INDEX IX_PurchaseInvoices_InvoiceNumber ON dbo.PurchaseInvoices(InvoiceNumber);
CREATE INDEX IX_PurchaseInvoices_InvoiceDate ON dbo.PurchaseInvoices(InvoiceDate);
CREATE INDEX IX_PurchaseInvoices_SupplierId ON dbo.PurchaseInvoices(SupplierId);
CREATE INDEX IX_PurchaseInvoices_UserId ON dbo.PurchaseInvoices(UserId);
CREATE INDEX IX_PurchaseInvoiceItems_InvoiceId ON dbo.PurchaseInvoiceItems(InvoiceId);
CREATE INDEX IX_PurchaseInvoiceItems_ProductId ON dbo.PurchaseInvoiceItems(ProductId);
CREATE INDEX IX_PurchaseInvoicePayments_InvoiceId ON dbo.PurchaseInvoicePayments(InvoiceId);

COMMIT TRANSACTION;
GO
