-- Run in SSMS/sqlcmd using a database administrator. SQL Server 2016 SP1+ syntax only.
USE [master];
GO
IF DB_ID(N'FitohPhone') IS NULL
BEGIN
    CREATE DATABASE [FitohPhone];
END;
GO
ALTER DATABASE [FitohPhone] SET COMPATIBILITY_LEVEL = 130;
GO
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL CONSTRAINT UQ_Users_Username UNIQUE,
        PasswordHash NVARCHAR(500) NOT NULL,
        DisplayName NVARCHAR(150) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END;
COMMIT TRANSACTION;
GO
