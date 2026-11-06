-- جداول حركة المخزون وسجل التدقيق (لتتبع كل تغيير في المخزون والنظام)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.StockMovements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockMovements
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
        MovementDate DATETIME2(7) NOT NULL CONSTRAINT DF_StockMovements_MovementDate DEFAULT (SYSUTCDATETIME()),
        MovementType NVARCHAR(30) NOT NULL,
        MovementTypeAr NVARCHAR(50) NOT NULL,
        ProductId INT NOT NULL CONSTRAINT FK_StockMovements_ProductId FOREIGN KEY REFERENCES dbo.Products(Id),
        QuantityChange INT NOT NULL,
        ReferenceType NVARCHAR(30) NULL,
        ReferenceId INT NULL,
        UserId INT NOT NULL CONSTRAINT FK_StockMovements_UserId FOREIGN KEY REFERENCES dbo.Users(Id),
        Notes NVARCHAR(500) NULL,
        CONSTRAINT CHK_StockMovements_Type CHECK (MovementType IN (N'Sale', N'Purchase', N'Return', N'Adjustment', N'Damage', N'RepairPart', N'Transfer')),
        CONSTRAINT CHK_StockMovements_Qty_NotZero CHECK (QuantityChange <> 0)
    );
END;

IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
        EventDate DATETIME2(7) NOT NULL CONSTRAINT DF_AuditLog_EventDate DEFAULT (SYSUTCDATETIME()),
        UserId INT NULL CONSTRAINT FK_AuditLog_UserId FOREIGN KEY REFERENCES dbo.Users(Id),
        Action NVARCHAR(50) NOT NULL,
        ActionAr NVARCHAR(100) NOT NULL,
        TableName NVARCHAR(100) NOT NULL,
        RecordId INT NULL,
        OldValues NVARCHAR(MAX) NULL,
        NewValues NVARCHAR(MAX) NULL,
        IPAddress NVARCHAR(45) NULL,
        Notes NVARCHAR(1000) NULL
    );
END;

CREATE INDEX IX_StockMovements_MovementDate ON dbo.StockMovements(MovementDate);
CREATE INDEX IX_StockMovements_ProductId ON dbo.StockMovements(ProductId);
CREATE INDEX IX_StockMovements_Reference ON dbo.StockMovements(ReferenceType, ReferenceId);
CREATE INDEX IX_StockMovements_UserId ON dbo.StockMovements(UserId);
CREATE INDEX IX_AuditLog_EventDate ON dbo.AuditLog(EventDate);
CREATE INDEX IX_AuditLog_UserId ON dbo.AuditLog(UserId);
CREATE INDEX IX_AuditLog_TableRecord ON dbo.AuditLog(TableName, RecordId);

COMMIT TRANSACTION;
GO
