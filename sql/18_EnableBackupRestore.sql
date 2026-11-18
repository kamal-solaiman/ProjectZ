/* OPTIONAL. For sites that delegate both BACKUP and RESTORE to a dedicated administrator.
   Skip this file if the DBA already manages backups. Never put this login on a till.
   Provision ProjectZBackupAdmin and its matching database user separately (no password in this file).
   An older version targeted ProjectZClient. This file does NOT revoke existing memberships:
   see sql/admin/SeparateLegacyBackupRoles.sql and docs/BACKUP.md before migrations 24/25. */
USE [ProjectZ];
IF @@TRANCOUNT <> 0 OR (@@OPTIONS & 2) <> 0
BEGIN
    RAISERROR(N'استخدم اتصالًا إداريًا جديدًا بدون معاملة مفتوحة أو ضمنية.', 16, 1);
    RETURN;
END;
SET XACT_ABORT ON;
IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1 OR ORIGINAL_LOGIN()=N'ProjectZClient'
    THROW 50080,N'يلزم اتصال مسؤول مستقل لإعداد النسخ والاسترجاع.',1;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals u JOIN sys.server_principals l ON l.sid=u.sid
               WHERE u.name=N'ProjectZBackupAdmin' AND l.name=N'ProjectZBackupAdmin')
    THROW 50081,N'هذا الملف اختياري. جهّز تسجيل دخول ProjectZBackupAdmin ومستخدمًا مطابقًا له في ProjectZ، أو استخدم مسؤول النسخ الحالي خارج البرنامج. لا تستخدم حساب الكاشير.',1;

-- These are broad restore rights, not prerequisites for settlement or for a backup-only job.
BEGIN TRY
    BEGIN TRANSACTION;
    IF IS_SRVROLEMEMBER(N'dbcreator',N'ProjectZBackupAdmin')=0
        EXEC master.sys.sp_executesql N'ALTER SERVER ROLE [dbcreator] ADD MEMBER [ProjectZBackupAdmin];';
    IF IS_ROLEMEMBER(N'db_owner',N'ProjectZBackupAdmin')=0
        ALTER ROLE [db_owner] ADD MEMBER [ProjectZBackupAdmin];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
SELECT N'ProjectZBackupAdmin' AS BackupLogin, DB_NAME() AS DatabaseName,
       IS_SRVROLEMEMBER(N'dbcreator',N'ProjectZBackupAdmin') AS IsDbCreator,
       IS_ROLEMEMBER(N'db_owner',N'ProjectZBackupAdmin') AS IsDbOwner;
GO
