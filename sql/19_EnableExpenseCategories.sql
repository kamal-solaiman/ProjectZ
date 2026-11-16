-- Create dbo.ExpenseCategories and grant the LAN client the rights the
-- «تصنيفات المصروفات» screen needs.
-- Run once after 06_CreateFullSchema.sql / 04_SetupLanSqlAuth.sql on an existing database.
-- Idempotent: the table is created only when missing, the six starter sections are inserted
-- only into an empty table, and GRANT repeats safely without touching existing rows.
-- No other table, column or row is modified.

USE [ProjectZ];
GO
SET NOCOUNT ON;

IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50060, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
    THROW 50061, N'dbo.AuditLog is missing. Run sql/06_CreateFullSchema.sql first.', 1;
GO

IF OBJECT_ID(N'dbo.ExpenseCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExpenseCategories
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseCategories PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        Description NVARCHAR(500) NOT NULL CONSTRAINT DF_ExpenseCategories_Description DEFAULT N'',
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ExpenseCategories_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ExpenseCategories_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_ExpenseCategories_Name UNIQUE (Name)
    );
    PRINT N'Created dbo.ExpenseCategories.';
END
GO

/* Starter sections: inserted only when the screen has no rows yet, so edited names are never overwritten. */
IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories)
BEGIN
    INSERT dbo.ExpenseCategories(Name, Description) VALUES
        (N'إيجار', N'إيجار المحل أو المخزن الشهري'),
        (N'فواتير', N'كهرباء ومياه وإنترنت واتصالات'),
        (N'مرتبات', N'أجور الموظفين والمستحقات'),
        (N'مواصلات', N'انتقالات ونقل وشحن'),
        (N'صيانة', N'صيانة الأجهزة والمحل'),
        (N'أخرى', N'مصروفات متنوعة غير مصنفة');
    PRINT N'Inserted the six starter expense sections.';
END
GO

/* The screen lists, adds, renames and deletes sections. */
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ExpenseCategories TO [ProjectZClient];
GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

IF OBJECT_ID(N'dbo.ExpenseCategories', N'U') IS NULL
    THROW 50062, N'dbo.ExpenseCategories was not created. Check the database and rerun this script.', 1;
GO

PRINT N'SUCCESS: ProjectZClient can now load, add, rename and delete expense sections from «تصنيفات المصروفات».';
GO
