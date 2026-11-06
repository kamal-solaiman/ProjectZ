-- جداول فواتير المبيعات (نقطة البيع POS) مع التعدد في الفواتير وطرق الدفع المختلطة
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.SalesInvoices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesInvoices
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoices PRIMARY KEY,
        InvoiceNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_SalesInvoices_InvoiceNumber UNIQUE,
        InvoiceDate DATETIME2(7) NOT NULL CONSTRAINT DF_SalesInvoices_InvoiceDate DEFAULT (SYSUTCDATETIME()),
        CustomerId INT NULL CONSTRAINT FK_SalesInvoices_CustomerId FOREIGN KEY REFERENCES dbo.Customers(Id),
        UserId INT NOT NULL CONSTRAINT FK_SalesInvoices_UserId FOREIGN KEY REFERENCES dbo.Users(Id),
        SubTotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoices_SubTotal DEFAULT (0),
        DiscountAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoices_DiscountAmount DEFAULT (0),
        TaxAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoices_TaxAmount DEFAULT (0),
        GrandTotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoices_GrandTotal DEFAULT (0),
        PaidAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoices_PaidAmount DEFAULT (0),
        RemainingAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoices_RemainingAmount DEFAULT (0),
        PaymentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_SalesInvoices_PaymentStatus DEFAULT (N'غير مدفوع'),
        PriceTier NVARCHAR(20) NOT NULL CONSTRAINT DF_SalesInvoices_PriceTier DEFAULT (N'قطاعي'),
        Notes NVARCHAR(1000) NULL,
        IsCancelled BIT NOT NULL CONSTRAINT DF_SalesInvoices_IsCancelled DEFAULT (0),
        CancelledAt DATETIME2(7) NULL,
        CancelledByUserId INT NULL CONSTRAINT FK_SalesInvoices_CancelledBy FOREIGN KEY REFERENCES dbo.Users(Id),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_SalesInvoices_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CHK_SalesInvoices_Amounts_NonNegative CHECK (SubTotal >= 0 AND DiscountAmount >= 0 AND GrandTotal >= 0)
    );
END;

IF OBJECT_ID(N'dbo.SalesInvoiceItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesInvoiceItems
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoiceItems PRIMARY KEY,
        InvoiceId INT NOT NULL CONSTRAINT FK_SalesInvoiceItems_InvoiceId FOREIGN KEY REFERENCES dbo.SalesInvoices(Id) ON DELETE CASCADE,
        ProductId INT NOT NULL CONSTRAINT FK_SalesInvoiceItems_ProductId FOREIGN KEY REFERENCES dbo.Products(Id),
        Quantity INT NOT NULL CONSTRAINT DF_SalesInvoiceItems_Quantity DEFAULT (1),
        UnitPrice DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoiceItems_UnitPrice DEFAULT (0),
        DiscountAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoiceItems_DiscountAmount DEFAULT (0),
        LineTotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoiceItems_LineTotal DEFAULT (0),
        PriceTierUsed NVARCHAR(20) NOT NULL CONSTRAINT DF_SalesInvoiceItems_PriceTierUsed DEFAULT (N'قطاعي'),
        CONSTRAINT CHK_SalesInvoiceItems_Qty_NonNegative CHECK (Quantity > 0),
        CONSTRAINT CHK_SalesInvoiceItems_Prices_NonNegative CHECK (UnitPrice >= 0 AND DiscountAmount >= 0 AND LineTotal >= 0)
    );
END;

IF OBJECT_ID(N'dbo.SalesInvoicePayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesInvoicePayments
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoicePayments PRIMARY KEY,
        InvoiceId INT NOT NULL CONSTRAINT FK_SalesInvoicePayments_InvoiceId FOREIGN KEY REFERENCES dbo.SalesInvoices(Id) ON DELETE CASCADE,
        PaymentMethod NVARCHAR(50) NOT NULL,
        PaymentMethodAr NVARCHAR(50) NOT NULL,
        Amount DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesInvoicePayments_Amount DEFAULT (0),
        ReferenceNumber NVARCHAR(100) NULL,
        PaidAt DATETIME2(7) NOT NULL CONSTRAINT DF_SalesInvoicePayments_PaidAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CHK_SalesInvoicePayments_Amount_Positive CHECK (Amount > 0)
    );
END;

CREATE INDEX IX_SalesInvoices_InvoiceNumber ON dbo.SalesInvoices(InvoiceNumber);
CREATE INDEX IX_SalesInvoices_InvoiceDate ON dbo.SalesInvoices(InvoiceDate);
CREATE INDEX IX_SalesInvoices_CustomerId ON dbo.SalesInvoices(CustomerId);
CREATE INDEX IX_SalesInvoices_UserId ON dbo.SalesInvoices(UserId);
CREATE INDEX IX_SalesInvoices_PaymentStatus ON dbo.SalesInvoices(PaymentStatus);
CREATE INDEX IX_SalesInvoiceItems_InvoiceId ON dbo.SalesInvoiceItems(InvoiceId);
CREATE INDEX IX_SalesInvoiceItems_ProductId ON dbo.SalesInvoiceItems(ProductId);
CREATE INDEX IX_SalesInvoicePayments_InvoiceId ON dbo.SalesInvoicePayments(InvoiceId);

COMMIT TRANSACTION;
GO
