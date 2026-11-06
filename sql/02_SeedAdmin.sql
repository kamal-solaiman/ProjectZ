-- Password: Admin@123. PBKDF2-HMAC-SHA256, 600000 iterations, random 16-byte salt.
-- Idempotent: existing accounts/passwords are NEVER overwritten.
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Username = N'admin')
BEGIN
    INSERT INTO dbo.Users (Username, PasswordHash, DisplayName, IsActive)
    VALUES (N'admin', N'PBKDF2-SHA256$600000$x5WAFq/7zUJDY2+DJ1UrNg==$7VWytRxWmltTzxexDmw3lc4XPLcXWoRgve3NMtqgtDE=', N'System Administrator', 1);
END;
COMMIT TRANSACTION;
GO
