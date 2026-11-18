/* Recovery for the old sql/18 grants; not part of the numbered migrations.
   Default = report only. On a restored test instance first, stop tills, confirm an INDEPENDENT
   administrator can back up and restore, and read docs/BACKUP.md before opting in. */
USE [ProjectZ];
DECLARE @Apply bit = 0; -- اتركه صفرًا للفحص. 1 يسحب العضويتين المباشرتين القديمتين فقط.
IF @@TRANCOUNT <> 0 OR (@@OPTIONS & 2) <> 0
BEGIN
    RAISERROR(N'استخدم نافذة جديدة بدون معاملة مفتوحة أو ضمنية.',16,1);
    RETURN;
END;
IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1 OR ORIGINAL_LOGIN()=N'ProjectZClient' OR SUSER_SNAME()=N'ProjectZClient'
    THROW 50083,N'افتح اتصالًا بحساب مسؤول SQL مستقل، لا بهوية الكاشير.',1;

DECLARE @LoginSid varbinary(85) = SUSER_SID(N'ProjectZClient'),
        @UserSid varbinary(85) = (SELECT sid FROM sys.database_principals WHERE name=N'ProjectZClient'),
        @OwnerSid varbinary(85) = (SELECT owner_sid FROM sys.databases WHERE database_id=DB_ID()),
        @DbOwner int = IS_ROLEMEMBER(N'db_owner',N'ProjectZClient'),
        @DbCreator int = IS_SRVROLEMEMBER(N'dbcreator',N'ProjectZClient'),
        @Sysadmin int = IS_SRVROLEMEMBER(N'sysadmin',N'ProjectZClient');
DECLARE @DirectDbOwner bit = CASE WHEN EXISTS (SELECT 1 FROM sys.database_role_members
        WHERE role_principal_id=DATABASE_PRINCIPAL_ID(N'db_owner') AND member_principal_id=DATABASE_PRINCIPAL_ID(N'ProjectZClient')) THEN 1 ELSE 0 END,
        @DirectDbCreator bit = CASE WHEN EXISTS (SELECT 1 FROM sys.server_role_members m
        JOIN sys.server_principals r ON r.principal_id=m.role_principal_id
        JOIN sys.server_principals l ON l.principal_id=m.member_principal_id WHERE r.name=N'dbcreator' AND l.name=N'ProjectZClient') THEN 1 ELSE 0 END;
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, ORIGINAL_LOGIN() AS AdminLogin,
       CONVERT(bit,CASE WHEN @UserSid=@LoginSid THEN 1 ELSE 0 END) AS LoginMatchesUser,
       SUSER_SNAME(@OwnerSid) AS DatabaseOwner, @Sysadmin AS IsSysadmin, @DbOwner AS IsDbOwner, @DbCreator AS IsDbCreator,
       @DirectDbOwner AS DirectDbOwner, @DirectDbCreator AS DirectDbCreator;
IF @Apply=0 RETURN;

IF @UserSid IS NULL OR @LoginSid IS NULL OR @UserSid<>@LoginSid OR @OwnerSid IS NULL OR @OwnerSid=@LoginSid
   OR ISNULL(@Sysadmin,-1)<>0 OR @DbOwner IS NULL OR @DbCreator IS NULL
   OR (@DbOwner=1 AND @DirectDbOwner=0) OR (@DbCreator=1 AND @DirectDbCreator=0)
    THROW 50084,N'لا تكفي إزالة منح 18 المباشرة. راجع مالك القاعدة وربط المستخدم والأدوار الموروثة وsysadmin يدويًا. لم يُعدّل شيء.',1;

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF @DirectDbOwner=1 ALTER ROLE [db_owner] DROP MEMBER [ProjectZClient];
    IF @DirectDbCreator=1 EXEC master.sys.sp_executesql N'ALTER SERVER ROLE [dbcreator] DROP MEMBER [ProjectZClient];';
    IF ISNULL(IS_ROLEMEMBER(N'db_owner',N'ProjectZClient'),-1)<>0 OR ISNULL(IS_SRVROLEMEMBER(N'dbcreator',N'ProjectZClient'),-1)<>0
        THROW 50084,N'بقيت عضوية غير مباشرة. أُلغيت محاولة التغيير؛ راجع الصلاحيات مع مسؤول SQL.',1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
PRINT N'لم تعد عضويتا 18 موجودتين. أعد الفحص باتصال جديد قبل تشغيل 24 و25.';
GO
