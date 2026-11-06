-- جدول مخزون الهالك (منتجات تالفة من المرتجعات أو التلف العام)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DamagedStock', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DamagedStock
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DamagedStock PRIMARY KEY,
        ProductId INT NOT NULL CONSTRAINT FK_DamagedStock_ProductId FOREIGN KEY REFERENCES dbo.Products(Id),
        Quantity INT NOT NULL CONSTRAINT DF_DamagedStock_Quantity DEFAULT (1),
        DamageReason NVARCHAR(500) NULL,
        DamageDate DATETIME2(7) NOT NULL CONSTRAINT DF_DamagedStock_DamageDate DEFAULT (SYSUTCDATETIME()),
        ReportedByUserId INT NOT NULL CONSTRAINT FK_DamagedStock_ReportedBy FOREIGN KEY REFERENCES dbo.Users(Id),
        SourceType NVARCHAR(30) NOT NULL CONSTRAINT DF_DamagedStock_SourceType DEFAULT (N'عام'),
        SourceReferenceId INT NULL,
        Disposition NVARCHAR(30) NOT NULL CONSTRAINT DF_DamagedStock_Disposition DEFAULT (N'تحت الإجراء'),
        DispositionDate DATETIME2(7) NULL,
        DisposedByUserId INT NULL CONSTRAINT FK_DamagedStock_DisposedBy FOREIGN KEY REFERENCES dbo.Users(Id),
        LossValue DECIMAL(18,4) NOT NULL CONSTRAINT DF_DamagedStock_LossValue DEFAULT (0),
        Notes NVARCHAR(1000) NULL,
        CONSTRAINT CHK_DamagedStock_Qty_Positive CHECK (Quantity > 0),
        CONSTRAINT CHK_DamagedStock_Disposition CHECK (Disposition IN (N'تحت الإجراء', N'رد للمخزون السليم', N'رد للمورد', N'إعدام', N'إصلاح وإعادة بيع'))
    );
END;

CREATE INDEX IX_DamagedStock_ProductId ON dbo.DamagedStock(ProductId);
CREATE INDEX IX_DamagedStock_DamageDate ON dbo.DamagedStock(DamageDate);
CREATE INDEX IX_DamagedStock_Disposition ON dbo.DamagedStock(Disposition);
CREATE INDEX IX_DamagedStock_SourceReference ON dbo.DamagedStock(SourceType, SourceReferenceId);

COMMIT TRANSACTION;
GO
