-- ============================================================
-- FitohPhone - Script 03: Seed default admin user
-- Target: Microsoft SQL Server 2016 SP1 Express (64-bit)
--
-- Default account:
--   Username    : admin
--   Password    : Admin@123
--   DisplayName : System Administrator
--   IsActive    : 1
--
-- Password is stored as PBKDF2-SHA256 hash (never plain text).
-- Hash format: PBKDF2SHA256$iterations$saltBase64$hashBase64
-- This hash was generated with 100,000 iterations, SHA-256,
-- and matches the PasswordHasher class in the WPF application.
-- ============================================================

USE FitohPhone;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = N'admin')
BEGIN
    INSERT INTO dbo.Users (Username, PasswordHash, DisplayName, IsActive, CreatedAt)
    VALUES (
        N'admin',
        N'PBKDF2SHA256$100000$AAECAwQFBgcICQoLDA0ODw==$zSgEyjn+zUBGRlBk35ROLSGNjbk2Ys0/R6eaIsRtiKY=',
        N'System Administrator',
        1,
        SYSUTCDATETIME()
    );
END
GO

PRINT 'Default user [admin] created (password: Admin@123).';
GO
