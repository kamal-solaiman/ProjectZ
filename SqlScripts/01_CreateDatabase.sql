-- ============================================================
-- FitohPhone - Script 01: Create Database
-- Target: Microsoft SQL Server 2016 SP1 Express (64-bit)
-- Product Version 13.0.4001.0 - Compatibility Level 130
-- All syntax below is fully compatible with SQL Server 2016.
-- ============================================================

IF DB_ID(N'FitohPhone') IS NULL
BEGIN
    CREATE DATABASE FitohPhone;
END
GO

-- Force compatibility level 130 (SQL Server 2016)
ALTER DATABASE FitohPhone SET COMPATIBILITY_LEVEL = 130;
GO

PRINT 'Database FitohPhone created with COMPATIBILITY_LEVEL = 130.';
GO
