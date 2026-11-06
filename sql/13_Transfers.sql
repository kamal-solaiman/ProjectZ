-- جداول التحويلات المالية (فودافون كاش / إنستاباي) مع الحدود اليومية والشهرية لكل خط
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.TransferLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TransferLines
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransferLines PRIMARY KEY,
        Provider NVARCHAR(30) NOT NULL,
        ProviderAr NVARCHAR(50) NOT NULL,
        PhoneNumber NVARCHAR(20) NOT NULL CONSTRAINT UQ_TransferLines_PhoneNumber UNIQUE,
        CurrentBalance DECIMAL(18,4) NOT NULL CONSTRAINT DF_TransferLines_CurrentBalance DEFAULT (0),
        DailyLimit DECIMAL(18,4) NOT NULL CONSTRAINT DF_TransferLines_DailyLimit DEFAULT (0),
        MonthlyLimit DECIMAL(18,4) NOT NULL CONSTRAINT DF_TransferLines_MonthlyLimit DEFAULT (0),
        DailyUsed DECIMAL(18,4) NOT NULL CONSTRAINT DF_TransferLines_DailyUsed DEFAULT (0),
        MonthlyUsed DECIMAL(18,4) NOT NULL CONSTRAINT DF_TransferLines_MonthlyUsed DEFAULT (0),
        LastDailyResetDate DATE NULL,
        LastMonthlyResetDate DATE NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_TransferLines_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_TransferLines_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NULL,
        CONSTRAINT CHK_TransferLines_Provider CHECK (Provider IN (N'VodafoneCash', N'InstaPay')),
        CONSTRAINT CHK_TransferLines_Limits_NonNegative CHECK (DailyLimit >= 0 AND MonthlyLimit >= 0)
    );
END;

IF OBJECT_ID(N'dbo.TransferTransactions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TransferTransactions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransferTransactions PRIMARY KEY,
        TransferDate DATETIME2(7) NOT NULL CONSTRAINT DF_TransferTransactions_TransferDate DEFAULT (SYSUTCDATETIME()),
        FromLineId INT NULL CONSTRAINT FK_TransferTransactions_FromLine FOREIGN KEY REFERENCES dbo.TransferLines(Id),
        ToLineId INT NULL CONSTRAINT FK_TransferTransactions_ToLine FOREIGN KEY REFERENCES dbo.TransferLines(Id),
        ToPhoneNumber NVARCHAR(20) NULL,
        Amount DECIMAL(18,4) NOT NULL CONSTRAINT DF_TransferTransactions_Amount DEFAULT (0),
        Commission DECIMAL(18,4) NOT NULL CONSTRAINT DF_TransferTransactions_Commission DEFAULT (0),
        TransferType NVARCHAR(20) NOT NULL,
        TransferTypeAr NVARCHAR(50) NOT NULL,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_TransferTransactions_Status DEFAULT (N'نجاح'),
        ReferenceNumber NVARCHAR(100) NULL,
        UserId INT NOT NULL CONSTRAINT FK_TransferTransactions_UserId FOREIGN KEY REFERENCES dbo.Users(Id),
        Notes NVARCHAR(500) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_TransferTransactions_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CHK_TransferTransactions_Type CHECK (TransferType IN (N'Outgoing', N'Incoming', N'Internal')),
        CONSTRAINT CHK_TransferTransactions_Amount_Positive CHECK (Amount > 0),
        CONSTRAINT CHK_TransferTransactions_Endpoints CHECK ((FromLineId IS NOT NULL OR ToPhoneNumber IS NOT NULL) AND (ToLineId IS NOT NULL OR FromLineId IS NOT NULL))
    );
END;

CREATE INDEX IX_TransferLines_PhoneNumber ON dbo.TransferLines(PhoneNumber);
CREATE INDEX IX_TransferLines_Provider ON dbo.TransferLines(Provider);
CREATE INDEX IX_TransferLines_IsActive ON dbo.TransferLines(IsActive);
CREATE INDEX IX_TransferTransactions_TransferDate ON dbo.TransferTransactions(TransferDate);
CREATE INDEX IX_TransferTransactions_FromLineId ON dbo.TransferTransactions(FromLineId);
CREATE INDEX IX_TransferTransactions_ToLineId ON dbo.TransferTransactions(ToLineId);
CREATE INDEX IX_TransferTransactions_ToPhone ON dbo.TransferTransactions(ToPhoneNumber);
CREATE INDEX IX_TransferTransactions_UserId ON dbo.TransferTransactions(UserId);

COMMIT TRANSACTION;
GO
