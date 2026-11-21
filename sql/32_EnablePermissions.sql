/*
    Permission management for ProjectZ.
    Run after sql/06_CreateFullSchema.sql and the existing feature installers.
    Existing non-admin users intentionally receive no role (deny by default).
    The seeded admin account is assigned the protected Administrator role.
*/
USE [ProjectZ];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
    THROW 50320, N'Run sql/06_CreateFullSchema.sql before enabling permissions.', 1;
IF USER_ID(N'ProjectZClient') IS NULL
    THROW 50323, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;

/* Enforce the chosen one-role-per-user policy without removing any existing assignment. */
IF EXISTS (SELECT UserId FROM dbo.UserRoles GROUP BY UserId HAVING COUNT(*) > 1)
    THROW 50321, N'Existing users have multiple roles. Resolve duplicate assignments before running this installer.', 1;
IF EXISTS
(
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'dbo.UserRoles') AND i.name=N'UX_UserRoles_OneRolePerUser'
      AND (i.is_unique=0 OR i.has_filter=1
        OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)<>1
        OR NOT EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                      WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND c.name=N'UserId'))
)
    THROW 50324, N'UX_UserRoles_OneRolePerUser exists with an incompatible definition; remove it before running this installer.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserRoles') AND name = N'UX_UserRoles_OneRolePerUser')
    CREATE UNIQUE INDEX UX_UserRoles_OneRolePerUser ON dbo.UserRoles(UserId);

DECLARE @Catalog TABLE (Code NVARCHAR(100) NOT NULL PRIMARY KEY, Name NVARCHAR(150) NOT NULL, Module NVARCHAR(100) NOT NULL);
INSERT @Catalog(Code,Name,Module) VALUES
(N'SECURITY.MANAGE',N'إدارة الأدوار والصلاحيات',N'الأمان'),
(N'SCREEN.POS',N'فتح شاشة نقطة البيع',N'المبيعات'),
(N'POS.COMPLETE',N'إتمام فاتورة البيع',N'المبيعات'),
(N'POS.DISCOUNT',N'تغيير الخصم والسعر',N'المبيعات'),
(N'SCREEN.PRODUCTS',N'فتح شاشة إضافة منتج',N'المخزون'),
(N'PRODUCTS.CREATE',N'إضافة منتج',N'المخزون'),
(N'PRODUCTS.EDIT',N'تعديل منتج',N'المخزون'),
(N'PRODUCTS.DELETE',N'حذف منتج',N'المخزون'),
(N'PRODUCTS.BARCODE',N'طباعة باركود',N'المخزون'),
(N'SCREEN.INVENTORY',N'فتح شاشة المخزون',N'المخزون'),
(N'INVENTORY.SUPPLY',N'توريد مخزون',N'المخزون'),
(N'INVENTORY.DAMAGE',N'تسجيل هالك',N'المخزون'),
(N'INVENTORY.DAMAGE_EDIT',N'تعديل تفاصيل الهالك والتسوية',N'المخزون'),
(N'INVENTORY.DAMAGE_DELETE',N'حذف سجل وحدة هالكة',N'المخزون'),
(N'INVENTORY.RESTORE',N'استرجاع هالك للمخزون',N'المخزون'),
(N'INVENTORY.RETURN',N'إرجاع هالك للمورد',N'المخزون'),
(N'SCREEN.CATEGORIES',N'فتح شاشة تصنيفات المنتجات',N'المخزون'),
(N'CATEGORIES.CREATE',N'إضافة تصنيف منتج',N'المخزون'),
(N'CATEGORIES.EDIT',N'تعديل تصنيف منتج',N'المخزون'),
(N'CATEGORIES.DELETE',N'حذف تصنيف منتج',N'المخزون'),
(N'SCREEN.SALES_INVOICES',N'فتح شاشة فواتير البيع',N'المبيعات'),
(N'SCREEN.RETURN_EXCHANGE',N'فتح شاشة المرتجع والاستبدال',N'المبيعات'),
(N'SALES_INVOICES.PRINT',N'طباعة فاتورة بيع',N'المبيعات'),
(N'SALES_INVOICES.RETURN',N'مرتجع واستبدال مبيعات',N'المبيعات'),
(N'SALES_INVOICES.COLLECT',N'تحصيل متبقي فاتورة بيع',N'المبيعات'),
(N'SALES_INVOICES.CANCEL',N'إلغاء فاتورة بيع',N'المبيعات'),
(N'SALES_INVOICES.NOTES',N'حفظ ملاحظات فاتورة البيع',N'المبيعات'),
(N'SCREEN.PURCHASE_ENTRY',N'فتح شاشة شراء فاتورة',N'المشتريات'),
(N'PURCHASE.CREATE',N'إنشاء واعتماد فاتورة شراء',N'المشتريات'),
(N'PURCHASE.EDIT',N'تعديل فاتورة شراء',N'المشتريات'),
(N'PURCHASE.RETURN',N'مرتجع للمورد',N'المشتريات'),
(N'PURCHASE.PAY',N'سداد فاتورة شراء',N'المشتريات'),
(N'PURCHASE.CANCEL',N'إلغاء فاتورة شراء',N'المشتريات'),
(N'PURCHASE.DELETE',N'حذف فاتورة شراء نهائيًا',N'المشتريات'),
(N'PURCHASE.PRINT',N'طباعة فاتورة شراء',N'المشتريات'),
(N'SCREEN.PURCHASE_INVOICES',N'فتح شاشة فواتير المشتريات',N'المشتريات'),
(N'SCREEN.MAINTENANCE_INTAKE',N'فتح شاشة أمر صيانة',N'الصيانة'),
(N'MAINTENANCE.CREATE',N'إنشاء أمر صيانة',N'الصيانة'),
(N'SCREEN.MAINTENANCE_INVOICES',N'فتح شاشة فواتير الصيانة',N'الصيانة'),
(N'MAINTENANCE.COLLECT',N'تحصيل عربون أو مستحقات صيانة',N'الصيانة'),
(N'MAINTENANCE.DELIVER',N'اعتماد تسليم جهاز',N'الصيانة'),
(N'MAINTENANCE.CANCEL',N'إلغاء أمر صيانة',N'الصيانة'),
(N'MAINTENANCE.REOPEN',N'إعادة فتح أمر صيانة',N'الصيانة'),
(N'MAINTENANCE.PRINT',N'طباعة إيصال أو أمر صيانة',N'الصيانة'),
(N'SCREEN.MAINTENANCE_ACTIONS',N'فتح شاشة إجراءات الصيانة',N'الصيانة'),
(N'MAINTENANCE.WORKSHOP',N'تغيير حالة وإجراءات الورشة',N'الصيانة'),
(N'MAINTENANCE.PARTS_ISSUE',N'صرف قطع غيار لأمر صيانة',N'الصيانة'),
(N'SCREEN.MAINTENANCE_PARTS',N'فتح شاشة قطع الغيار',N'الصيانة'),
(N'MAINTENANCE_PARTS.CREATE',N'إضافة قطعة غيار',N'الصيانة'),
(N'MAINTENANCE_PARTS.EDIT',N'تعديل قطعة غيار',N'الصيانة'),
(N'MAINTENANCE_PARTS.DELETE',N'حذف قطعة غيار',N'الصيانة'),
(N'MAINTENANCE_PARTS.RESTOCK',N'توريد قطع غيار',N'الصيانة'),
(N'SCREEN.FINANCIAL_ACCOUNTS',N'فتح شاشة حساباتي',N'المالية'),
(N'FINANCE.ACCOUNTS_CREATE',N'إضافة حساب مالي',N'المالية'),
(N'FINANCE.ACCOUNTS_EDIT',N'تعديل بيانات الحساب المالي وحدوده',N'المالية'),
(N'FINANCE.ACCOUNTS_DEFAULT',N'تعيين الحساب المالي الافتراضي',N'المالية'),
(N'FINANCE.ACCOUNTS_ACTIVATE',N'تنشيط أو إيقاف حساب مالي',N'المالية'),
(N'FINANCE.ACCOUNTS_DELETE',N'حذف حساب مالي',N'المالية'),
(N'FINANCE.TRANSACTIONS',N'تسجيل حركة مالية',N'المالية'),
(N'SCREEN.VODAFONE_TRANSFER',N'فتح شاشة تحويل فودافون كاش',N'المالية'),
(N'SCREEN.VODAFONE_RECORDS',N'فتح سجلات فودافون كاش',N'المالية'),
(N'FINANCE.VODAFONE_OPERATION',N'تنفيذ عملية فودافون كاش',N'المالية'),
(N'FINANCE.VODAFONE_EDIT',N'تعديل عملية فودافون كاش',N'المالية'),
(N'FINANCE.VODAFONE_REVERSE',N'عكس عملية فودافون كاش',N'المالية'),
(N'FINANCE.VODAFONE_DELETE',N'حذف عملية فودافون كاش',N'المالية'),
(N'FINANCE.VODAFONE_EXPORT',N'تصدير وطباعة سجلات فودافون كاش',N'المالية'),
(N'SCREEN.INSTAPAY_TRANSFER',N'فتح شاشة تحويل إنستاباي',N'المالية'),
(N'SCREEN.INSTAPAY_RECORDS',N'فتح سجلات إنستاباي',N'المالية'),
(N'FINANCE.INSTAPAY_OPERATION',N'تنفيذ عملية انستاباي',N'المالية'),
(N'FINANCE.INSTAPAY_EDIT',N'تعديل عملية انستاباي',N'المالية'),
(N'FINANCE.INSTAPAY_REVERSE',N'عكس عملية انستاباي',N'المالية'),
(N'FINANCE.INSTAPAY_DELETE',N'حذف عملية انستاباي',N'المالية'),
(N'FINANCE.INSTAPAY_EXPORT',N'تصدير وطباعة سجلات انستاباي',N'المالية'),
(N'SCREEN.CUSTOMERS',N'فتح شاشة العملاء',N'العملاء والموردون'),
(N'CUSTOMERS.CREATE',N'إضافة عميل',N'العملاء والموردون'),
(N'CUSTOMERS.EDIT',N'تعديل عميل',N'العملاء والموردون'),
(N'CUSTOMERS.DELETE',N'حذف عميل',N'العملاء والموردون'),
(N'SCREEN.SUPPLIERS',N'فتح شاشة الموردين',N'العملاء والموردون'),
(N'SUPPLIERS.CREATE',N'إضافة مورد',N'العملاء والموردون'),
(N'SUPPLIERS.EDIT',N'تعديل مورد',N'العملاء والموردون'),
(N'SUPPLIERS.DELETE',N'حذف مورد',N'العملاء والموردون'),
(N'SCREEN.USERS',N'فتح شاشة المستخدمين',N'إدارة النظام'),
(N'USERS.CREATE',N'إضافة مستخدم',N'إدارة النظام'),
(N'USERS.EDIT',N'تعديل مستخدم',N'إدارة النظام'),
(N'USERS.PASSWORD',N'تغيير كلمة مرور مستخدم',N'إدارة النظام'),
(N'USERS.ACTIVATE',N'تنشيط أو تعطيل مستخدم',N'إدارة النظام'),
(N'USERS.DELETE',N'حذف مستخدم',N'إدارة النظام'),
(N'SCREEN.EXPENSES',N'فتح شاشة إضافة مصروف',N'المصروفات'),
(N'EXPENSES.CREATE',N'حفظ مصروف',N'المصروفات'),
(N'SCREEN.EXPENSE_INVOICES',N'فتح شاشة فواتير المصروفات',N'المصروفات'),
(N'EXPENSE_INVOICES.EXPORT',N'تصدير وطباعة المصروفات',N'المصروفات'),
(N'SCREEN.EXPENSE_CATEGORIES',N'فتح شاشة تصنيفات المصروفات',N'المصروفات'),
(N'EXPENSE_CATEGORIES.CREATE',N'إضافة تصنيف مصروف',N'المصروفات'),
(N'EXPENSE_CATEGORIES.EDIT',N'تعديل تصنيف مصروف',N'المصروفات'),
(N'EXPENSE_CATEGORIES.DELETE',N'حذف تصنيف مصروف',N'المصروفات'),
(N'SCREEN.REPORTS_SALES',N'فتح تقارير المبيعات',N'التقارير'),
(N'SCREEN.REPORTS_PURCHASE',N'فتح تقارير المشتريات',N'التقارير'),
(N'SCREEN.REPORTS_MAINTENANCE',N'فتح تقارير الصيانة',N'التقارير'),
(N'SCREEN.REPORTS_FINANCE',N'فتح تقارير المالية',N'التقارير'),
(N'SCREEN.REPORTS_EXPENSES',N'فتح تقارير المصروفات',N'التقارير'),
(N'SCREEN.REPORTS_OVERALL',N'فتح إجمالي التقارير',N'التقارير'),
(N'REPORTS.EXPORT',N'تصدير أو طباعة التقارير',N'التقارير'),
(N'SCREEN.GENERAL_SETTINGS',N'فتح الإعدادات العامة',N'الإعدادات'),
(N'GENERAL_SETTINGS.EDIT',N'تعديل الإعدادات العامة',N'الإعدادات'),
(N'SCREEN.APPEARANCE',N'فتح إعدادات المظهر',N'الإعدادات'),
(N'APPEARANCE.EDIT',N'حفظ إعدادات المظهر',N'الإعدادات'),
(N'SCREEN.BACKUP',N'فتح شاشة النسخ الاحتياطي',N'الإعدادات'),
(N'BACKUP.CREATE',N'إنشاء نسخة احتياطية',N'الإعدادات'),
(N'BACKUP.RESTORE',N'استعادة نسخة احتياطية',N'الإعدادات');

INSERT dbo.Permissions(Code,Name,Module)
SELECT c.Code,c.Name,c.Module FROM @Catalog c
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions p WHERE p.Code=c.Code);

/* Existing names are reserved roles; do not overwrite their metadata or permissions other than seeding initial defaults. */
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name=N'Administrator')
    INSERT dbo.Roles(Name,Description,IsSystem) VALUES(N'Administrator',N'صلاحيات مدير النظام الكاملة',1);
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name=N'Cashier')
    INSERT dbo.Roles(Name,Description,IsSystem) VALUES(N'Cashier',N'صلاحيات تشغيل نقطة البيع',1);
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name=N'Technician')
    INSERT dbo.Roles(Name,Description,IsSystem) VALUES(N'Technician',N'صلاحيات فني الصيانة',1);
UPDATE dbo.Roles SET IsSystem=1 WHERE Name IN(N'Administrator',N'Cashier',N'Technician');

DECLARE @AdminRoleId INT=(SELECT Id FROM dbo.Roles WHERE Name=N'Administrator');
DECLARE @CashierRoleId INT=(SELECT Id FROM dbo.Roles WHERE Name=N'Cashier');
DECLARE @TechnicianRoleId INT=(SELECT Id FROM dbo.Roles WHERE Name=N'Technician');
INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT @AdminRoleId,p.Id FROM dbo.Permissions p
WHERE EXISTS(SELECT 1 FROM @Catalog c WHERE c.Code=p.Code)
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@AdminRoleId AND rp.PermissionId=p.Id);
/* Useful least-privilege starting points; users are not assigned automatically. */
INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT @CashierRoleId,p.Id FROM dbo.Permissions p JOIN @Catalog c ON c.Code=p.Code
WHERE c.Code IN (N'SCREEN.POS',N'POS.COMPLETE',N'SCREEN.SALES_INVOICES',N'SALES_INVOICES.PRINT',N'SALES_INVOICES.COLLECT',N'SCREEN.CUSTOMERS',N'CUSTOMERS.CREATE',N'CUSTOMERS.EDIT')
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@CashierRoleId AND rp.PermissionId=p.Id);
INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT @TechnicianRoleId,p.Id FROM dbo.Permissions p JOIN @Catalog c ON c.Code=p.Code
WHERE c.Code IN (N'SCREEN.MAINTENANCE_INTAKE',N'MAINTENANCE.CREATE',N'SCREEN.MAINTENANCE_INVOICES',N'SCREEN.MAINTENANCE_ACTIONS',N'MAINTENANCE.WORKSHOP',N'MAINTENANCE.PARTS_ISSUE',N'SCREEN.MAINTENANCE_PARTS')
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=@TechnicianRoleId AND rp.PermissionId=p.Id);

/* Ensure exactly one role for the pre-existing admin; leave every other user unassigned. */
IF EXISTS(SELECT 1 FROM dbo.Users WHERE Username=N'admin')
BEGIN
    DECLARE @AdminUserId INT=(SELECT Id FROM dbo.Users WHERE Username=N'admin');
    DELETE dbo.UserRoles WHERE UserId=@AdminUserId AND RoleId<>@AdminRoleId;
    IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@AdminUserId AND RoleId=@AdminRoleId)
        INSERT dbo.UserRoles(UserId,RoleId) VALUES(@AdminUserId,@AdminRoleId);
END;

/* Required application grants; this installer does not grant database DML or bypasses. */
IF USER_ID(N'ProjectZClient') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Roles TO ProjectZClient;
    GRANT SELECT ON dbo.Permissions TO ProjectZClient;
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.UserRoles TO ProjectZClient;
    GRANT SELECT, INSERT, DELETE ON dbo.RolePermissions TO ProjectZClient;
    GRANT INSERT ON dbo.AuditLog TO ProjectZClient;
END;

COMMIT TRANSACTION;
PRINT N'Permission catalog installed. Existing non-admin users remain unassigned (deny by default).';
GO
