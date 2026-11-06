-- جداول صيانة الهواتف (استلام، باركود، تكاليف، تسليم، أرشيف الصيانات لكل عميل)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.PhoneRepairs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PhoneRepairs
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PhoneRepairs PRIMARY KEY,
        RepairNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_PhoneRepairs_RepairNumber UNIQUE,
        CustomerId INT NOT NULL CONSTRAINT FK_PhoneRepairs_CustomerId FOREIGN KEY REFERENCES dbo.Customers(Id),
        ReceivedByUserId INT NOT NULL CONSTRAINT FK_PhoneRepairs_ReceivedBy FOREIGN KEY REFERENCES dbo.Users(Id),
        DeviceModel NVARCHAR(200) NOT NULL,
        DeviceIMEI NVARCHAR(50) NULL,
        DeviceSerialNumber NVARCHAR(100) NULL,
        ProblemDescription NVARCHAR(1000) NOT NULL,
        Diagnosis NVARCHAR(1000) NULL,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_PhoneRepairs_Status DEFAULT (N'قيد الاستلام'),
        ReceivedDate DATETIME2(7) NOT NULL CONSTRAINT DF_PhoneRepairs_ReceivedDate DEFAULT (SYSUTCDATETIME()),
        EstimatedDeliveryDate DATETIME2(7) NULL,
        ActualDeliveryDate DATETIME2(7) NULL,
        DeliveredByUserId INT NULL CONSTRAINT FK_PhoneRepairs_DeliveredBy FOREIGN KEY REFERENCES dbo.Users(Id),
        RepairCost DECIMAL(18,4) NOT NULL CONSTRAINT DF_PhoneRepairs_RepairCost DEFAULT (0),
        PartsCost DECIMAL(18,4) NOT NULL CONSTRAINT DF_PhoneRepairs_PartsCost DEFAULT (0),
        LaborCost DECIMAL(18,4) NOT NULL CONSTRAINT DF_PhoneRepairs_LaborCost DEFAULT (0),
        TotalCost DECIMAL(18,4) NOT NULL CONSTRAINT DF_PhoneRepairs_TotalCost DEFAULT (0),
        PaidAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_PhoneRepairs_PaidAmount DEFAULT (0),
        RemainingAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_PhoneRepairs_RemainingAmount DEFAULT (0),
        BarcodePrinted BIT NOT NULL CONSTRAINT DF_PhoneRepairs_BarcodePrinted DEFAULT (0),
        BarcodePrintedAt DATETIME2(7) NULL,
        WarrantyPeriodDays INT NULL,
        Notes NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_PhoneRepairs_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NULL,
        CONSTRAINT CHK_PhoneRepairs_Costs_NonNegative CHECK (RepairCost >= 0 AND PartsCost >= 0 AND LaborCost >= 0 AND TotalCost >= 0),
        CONSTRAINT CHK_PhoneRepairs_Status CHECK (Status IN (N'قيد الاستلام', N'قيد الصيانة', N'انتظار قطع غيار', N'جاهز للتسليم', N'تم التسليم', N'ملغي'))
    );
END;

IF OBJECT_ID(N'dbo.RepairItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepairItems
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RepairItems PRIMARY KEY,
        RepairId INT NOT NULL CONSTRAINT FK_RepairItems_RepairId FOREIGN KEY REFERENCES dbo.PhoneRepairs(Id) ON DELETE CASCADE,
        ItemType NVARCHAR(30) NOT NULL,
        ItemTypeAr NVARCHAR(50) NOT NULL,
        ItemName NVARCHAR(200) NOT NULL,
        ItemCost DECIMAL(18,4) NOT NULL CONSTRAINT DF_RepairItems_ItemCost DEFAULT (0),
        Quantity INT NOT NULL CONSTRAINT DF_RepairItems_Quantity DEFAULT (1),
        IsCustomerSupplied BIT NOT NULL CONSTRAINT DF_RepairItems_IsCustomerSupplied DEFAULT (0),
        Notes NVARCHAR(500) NULL,
        CONSTRAINT CHK_RepairItems_ItemType CHECK (ItemType IN (N'Part', N'Labor', N'Accessory')),
        CONSTRAINT CHK_RepairItems_Qty_Positive CHECK (Quantity > 0),
        CONSTRAINT CHK_RepairItems_Cost_NonNegative CHECK (ItemCost >= 0)
    );
END;

CREATE INDEX IX_PhoneRepairs_RepairNumber ON dbo.PhoneRepairs(RepairNumber);
CREATE INDEX IX_PhoneRepairs_CustomerId ON dbo.PhoneRepairs(CustomerId);
CREATE INDEX IX_PhoneRepairs_Status ON dbo.PhoneRepairs(Status);
CREATE INDEX IX_PhoneRepairs_ReceivedDate ON dbo.PhoneRepairs(ReceivedDate);
CREATE INDEX IX_PhoneRepairs_IMEI ON dbo.PhoneRepairs(DeviceIMEI);
CREATE INDEX IX_RepairItems_RepairId ON dbo.RepairItems(RepairId);

COMMIT TRANSACTION;
GO
