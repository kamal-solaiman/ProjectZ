-- Grant the LAN client the category rights the categories screen needs.
-- Run once after 06_CreateFullSchema.sql / 04_SetupLanSqlAuth.sql / 09_EnablePosSales.sql.
-- Idempotent: GRANT repeats safely and no table, column or row is created here.
-- DELETE is granted only on dbo.Categories; the screen refuses to delete a category that still has products,
-- and dbo.Products keeps its foreign key to the category row.

USE [ProjectZ];
GO
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
    THROW 50050, N'dbo.Categories is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
    THROW 50051, N'dbo.Products is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50052, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
GO

/* The screen loads the list, adds, renames and deletes unused categories; the add-product combo box reads the same rows. */
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Categories TO [ProjectZClient];
GO

/* Counting the products of a category needs SELECT on dbo.Products, which 04 already grants; re-stated for a partial run. */
GRANT SELECT ON OBJECT::dbo.Products TO [ProjectZClient];
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NOT NULL
    GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

PRINT N'SUCCESS: ProjectZClient can now load, add, rename and delete unused categories; the add-product category list uses the same rows.';
GO
