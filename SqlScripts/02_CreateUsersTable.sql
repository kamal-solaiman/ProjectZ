-- ============================================================
-- FitohPhone - Script 02: Create Users table (the ONLY table)
-- Target: Microsoft SQL Server 2016 SP1 Express (64-bit)
-- Compatible with Compatibility Level 130.
-- Data types used (INT, NVARCHAR, BIT, DATETIME2) all exist in 2016.
-- ============================================================

USE FitohPhone;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id           INT            IDENTITY(1,1) NOT NULL,
        Username     NVARCHAR(100)  NOT NULL,
        PasswordHash NVARCHAR(500)  NOT NULL,
        DisplayName  NVARCHAR(150)  NULL,
        IsActive     BIT            NOT NULL CONSTRAINT DF_Users_IsActive   DEFAULT (1),
        CreatedAt    DATETIME2      NOT NULL CONSTRAINT DF_Users_CreatedAt  DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Users_Username UNIQUE (Username)
    );
END
GO

PRINT 'Table dbo.Users created.';
GO
