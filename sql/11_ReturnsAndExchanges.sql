-- جداول المرتجع والاستبدال مع ربط الفاتورة الأصلية وتحديد حالة القطعة (سليمة / هالكة)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ReturnsExchanges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReturnsExchanges
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReturnsExchanges PRIMARY KEY,
        ReturnNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_ReturnsExchanges_ReturnNumber UNIQUE,
        ReturnDate DATETIME2(7) NOT NULL CONSTRAINT DF_ReturnsExchanges_ReturnDate DEFAULT (SYSUTCDATETIME()),
        OriginalInvoiceId INT NULL CONSTRAINT FK_ReturnsExchanges_OriginalInvoiceId FOREIGN KEY REFERENCES dbo.SalesInvoices(Id),
        CustomerId INT NOT NULL CONSTRAINT FK_ReturnsExchanges_CustomerId FOREIGN KEY REFERENCES dbo.Customers(Id),
        UserId INT NOT NULL CONSTRAINT FK_ReturnsExchanges_UserId FOREIGN KEY REFERENCES dbo.Users(Id),
        Reason NVARCHAR(500) NULL,
        TotalAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_ReturnsExchanges_TotalAmount DEFAULT (0),
        RefundAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_ReturnsExchanges_RefundAmount DEFAULT (0),
        ExchangeSettlementAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_ReturnsExchanges_ExchangeSettlementAmount DEFAULT (0),
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_ReturnsExchanges_Status DEFAULT (N'جديد'),
        Notes NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ReturnsExchanges_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;

IF OBJECT_ID(N'dbo.ReturnExchangeItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReturnExchangeItems
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReturnExchangeItems PRIMARY KEY,
        ReturnId INT NOT NULL CONSTRAINT FK_ReturnExchangeItems_ReturnId FOREIGN KEY REFERENCES dbo.ReturnsExchanges(Id) ON DELETE CASCADE,
        OriginalInvoiceItemId INT NULL CONSTRAINT FK_ReturnExchangeItems_OriginalInvoiceItemId FOREIGN KEY REFERENCES dbo.SalesInvoiceItems(Id),
        ProductId INT NOT NULL CONSTRAINT FK_ReturnExchangeItems_ProductId FOREIGN KEY REFERENCES dbo.Products(Id),
        Quantity INT NOT NULL CONSTRAINT DF_ReturnExchangeItems_Quantity DEFAULT (1),
        ItemCondition NVARCHAR(20) NOT NULL CONSTRAINT DF_ReturnExchangeItems_ItemCondition DEFAULT (N'سليم'),
        IsDamaged BIT NOT NULL CONSTRAINT DF_ReturnExchangeItems_IsDamaged DEFAULT (0),
        UnitPrice DECIMAL(18,4) NOT NULL CONSTRAINT DF_ReturnExchangeItems_UnitPrice DEFAULT (0),
        LineTotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_ReturnExchangeItems_LineTotal DEFAULT (0),
        ReplacementProductId INT NULL CONSTRAINT FK_ReturnExchangeItems_ReplacementProduct FOREIGN KEY REFERENCES dbo.Products(Id),
        ReplacementQuantity INT NULL,
        PriceDifference DECIMAL(18,4) NOT NULL CONSTRAINT DF_ReturnExchangeItems_PriceDifference DEFAULT (0),
        CONSTRAINT CHK_ReturnExchangeItems_Qty_Positive CHECK (Quantity > 0),
        CONSTRAINT CHK_ReturnExchangeItems_Condition CHECK (ItemCondition IN (N'سليم', N'هالك', N'يعمل parcialmente'))
    );
END;

CREATE INDEX IX_ReturnsExchanges_ReturnNumber ON dbo.ReturnsExchanges(ReturnNumber);
CREATE INDEX IX_ReturnsExchanges_ReturnDate ON dbo.ReturnsExchanges(ReturnDate);
CREATE INDEX IX_ReturnsExchanges_CustomerId ON dbo.ReturnsExchanges(CustomerId);
CREATE INDEX IX_ReturnsExchanges_OriginalInvoiceId ON dbo.ReturnsExchanges(OriginalInvoiceId);
CREATE INDEX IX_ReturnExchangeItems_ReturnId ON dbo.ReturnExchangeItems(ReturnId);
CREATE INDEX IX_ReturnExchangeItems_ProductId ON dbo.ReturnExchangeItems(ProductId);

COMMIT TRANSACTION;
GO
