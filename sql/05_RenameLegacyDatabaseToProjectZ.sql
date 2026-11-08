-- Rename the legacy database without rebuilding its tables or losing user data.
-- Run in SSMS as a database administrator after backing up the database and
-- closing ProjectZ (and any other application connected to the legacy database).
USE [master];
GO

IF DB_ID(N'ProjectZ') IS NOT NULL AND DB_ID(N'FitohPhone') IS NOT NULL
BEGIN
    ;THROW 50002, N'Both [ProjectZ] and legacy [FitohPhone] databases exist. Review them manually; no rename was performed.', 1;
END;

IF DB_ID(N'ProjectZ') IS NOT NULL
BEGIN
    PRINT N'Database [ProjectZ] already exists; no rename is needed.';
END
ELSE IF DB_ID(N'FitohPhone') IS NOT NULL
BEGIN
    ALTER DATABASE [FitohPhone] MODIFY NAME = [ProjectZ];
    PRINT N'Legacy database renamed to [ProjectZ].';
END
ELSE
BEGIN
    PRINT N'No legacy database found. Run sql/01_CreateDatabase.sql for a fresh installation.';
END;
GO
