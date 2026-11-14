-- Grant the LAN client the rights the general settings screen needs, and seed the store identity rows.
-- Run once after 06_CreateFullSchema.sql / 04_SetupLanSqlAuth.sql.
-- Idempotent: GRANT repeats safely and the seed only inserts keys that are missing; no existing value is
-- overwritten, so a store name already customised by the user is never reset.
-- No table or column is created here: dbo.SystemSettings already exists in the full schema.

USE [ProjectZ];
GO
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NULL
    THROW 50070, N'dbo.SystemSettings is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50071, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
GO

/* Read the whole settings sheet, create missing keys, and save the store data. */
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.SystemSettings TO [ProjectZClient];
GO

IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NOT NULL
    GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

/* Store identity and general defaults. Defaults mirror ProjectZ so a fresh install looks unchanged. */
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'StoreName')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'StoreName', N'ProjectZ', N'string', N'اسم المحل واسم البرنامج في كل الشاشات والمطبوعات');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'StoreTagline')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'StoreTagline', N'للموبايل والإكسسوارات', N'string', N'وصف مختصر يظهر بجوار اسم المحل');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'StoreAddress')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'StoreAddress', N'', N'string', N'عنوان المحل في المطبوعات');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'StorePhone')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'StorePhone', N'', N'string', N'هاتف المحل');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'StorePhoneAlt')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'StorePhoneAlt', N'', N'string', N'رقم هاتف إضافي');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'StoreEmail')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'StoreEmail', N'', N'string', N'بريد المحل الإلكتروني');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'ServiceCenterName')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'ServiceCenterName', N'مركز الصيانة والخدمة', N'string', N'اسم قسم الصيانة في إيصالات الاستلام');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'PrintFooterNote')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'PrintFooterNote', N'شكرًا لتعاملكم معنا · يُرجى الاحتفاظ بالفاتورة للضمان والاستبدال', N'string',
            N'عبارة أسفل الفواتير والإيصالات');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'DefaultLowStockAlert')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description)
    VALUES (N'DefaultLowStockAlert', N'5', N'integer', N'حد التنبيه الافتراضي للمخزون عند إضافة صنف');
GO

PRINT N'SUCCESS: ProjectZClient can read and save general settings, and the store identity keys exist in dbo.SystemSettings.';
GO
