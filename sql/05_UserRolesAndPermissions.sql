-- جداول المستخدمين والأدوار والصلاحيات
-- يربط الجدول Users الموجود في 01_CreateDatabase.sql بالأدوار والصلاحيات التفصيلية لكل شاشة
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRoles
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserRoles PRIMARY KEY,
        RoleName NVARCHAR(50) NOT NULL CONSTRAINT UQ_UserRoles_RoleName UNIQUE,
        RoleNameAr NVARCHAR(50) NOT NULL,
        Description NVARCHAR(255) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_UserRoles_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_UserRoles_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;

IF OBJECT_ID(N'dbo.UserPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPermissions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserPermissions PRIMARY KEY,
        UserId INT NOT NULL CONSTRAINT FK_UserPermissions_UserId FOREIGN KEY REFERENCES dbo.Users(Id),
        Module NVARCHAR(50) NOT NULL,
        ModuleNameAr NVARCHAR(100) NOT NULL,
        CanView BIT NOT NULL CONSTRAINT DF_UserPermissions_CanView DEFAULT (0),
        CanAdd BIT NOT NULL CONSTRAINT DF_UserPermissions_CanAdd DEFAULT (0),
        CanEdit BIT NOT NULL CONSTRAINT DF_UserPermissions_CanEdit DEFAULT (0),
        CanDelete BIT NOT NULL CONSTRAINT DF_UserPermissions_CanDelete DEFAULT (0),
        CanPrint BIT NOT NULL CONSTRAINT DF_UserPermissions_CanPrint DEFAULT (0),
        IsActive BIT NOT NULL CONSTRAINT DF_UserPermissions_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_UserPermissions_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_UserPermissions_UserModule UNIQUE (UserId, Module)
    );
END;

COMMIT TRANSACTION;
GO
