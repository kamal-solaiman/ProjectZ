-- بيانات أولية مرجعية للنظام (تصنيفات، ماركات، أدوار، إعدادات المحل، خطوط التحويل)
USE [FitohPhone];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- تصنيفات المنتجات الأساسية
IF NOT EXISTS (SELECT 1 FROM dbo.ProductCategories WHERE Name = N'Mobiles')
    INSERT INTO dbo.ProductCategories (Name, NameAr, Description) VALUES (N'Mobiles', N'هواتف محمولة', N'جميع أنواع الهواتف المحمولة والأجهزة الذكية');
IF NOT EXISTS (SELECT 1 FROM dbo.ProductCategories WHERE Name = N'Accessories')
    INSERT INTO dbo.ProductCategories (Name, NameAr, Description) VALUES (N'Accessories', N'إكسسوارات', N'شواحن، سماعات، غطاء، بطاريات، كابلات');
IF NOT EXISTS (SELECT 1 FROM dbo.ProductCategories WHERE Name = N'SpareParts')
    INSERT INTO dbo.ProductCategories (Name, NameAr, Description) VALUES (N'SpareParts', N'قطع غيار', N'قطع غيار الهواتف والشاشات');

-- الماركات الأساسية
IF NOT EXISTS (SELECT 1 FROM dbo.ProductBrands WHERE Name = N'Samsung')
    INSERT INTO dbo.ProductBrands (Name, NameAr, Description) VALUES (N'Samsung', N'سامسونج', N'منتجات شركة سامسونج');
IF NOT EXISTS (SELECT 1 FROM dbo.ProductBrands WHERE Name = N'Apple')
    INSERT INTO dbo.ProductBrands (Name, NameAr, Description) VALUES (N'Apple', N'أبل', N'منتجات شركة أبل');
IF NOT EXISTS (SELECT 1 FROM dbo.ProductBrands WHERE Name = N'Xiaomi')
    INSERT INTO dbo.ProductBrands (Name, NameAr, Description) VALUES (N'Xiaomi', N'شاومي', N'منتجات شركة شاومي');
IF NOT EXISTS (SELECT 1 FROM dbo.ProductBrands WHERE Name = N'Oppo')
    INSERT INTO dbo.ProductBrands (Name, NameAr, Description) VALUES (N'Oppo', N'أوبو', N'منتجات شركة أوبو');
IF NOT EXISTS (SELECT 1 FROM dbo.ProductBrands WHERE Name = N'Generic')
    INSERT INTO dbo.ProductBrands (Name, NameAr, Description) VALUES (N'Generic', N'غير ماركة', N'منتجات غير ماركة أوeneric');

-- أدوار النظام
IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE RoleName = N'Administrator')
    INSERT INTO dbo.UserRoles (RoleName, RoleNameAr, Description) VALUES (N'Administrator', N'مدير النظام', N'صلاحية كاملة على جميع الشاشات والبيانات');
IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE RoleName = N'Cashier')
    INSERT INTO dbo.UserRoles (RoleName, RoleNameAr, Description) VALUES (N'Cashier', N'كاشير مبيعات', N'صلاحية تشغيل نقطة البيع وإدارة الفواتير');
IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE RoleName = N'Technician')
    INSERT INTO dbo.UserRoles (RoleName, RoleNameAr, Description) VALUES (N'Technician', N'فني صيانة', N'صلاحية إدارة صيانة الهواتف وأجهزة العملاء');
IF NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE RoleName = N'StoreKeeper')
    INSERT INTO dbo.UserRoles (RoleName, RoleNameAr, Description) VALUES (N'StoreKeeper', N'أمين مخزن', N'صلاحية إدارة المخزون والمشتريات ومخزون الهالك');

-- إعدادات المحل الافتراضية (سطر واحد فقط dzięki CHECK Constraint على Id=1)
IF NOT EXISTS (SELECT 1 FROM dbo.ShopSettings WHERE Id = 1)
    INSERT INTO dbo.ShopSettings (Id, ShopNameAr, ShopNameEn, Phone1, Phone2, WhatsAppNumber, Address, DefaultCurrency, ThermalPrinterName, BarcodePrinterName, AutoPrintInvoice, LowStockAlert, TransferLimitWarning)
    VALUES (1, N'فيتوح فون للهواتف والصيانة', N'FitohPhone Store', N'01012345678', N'01098765432', N'01098765432', N'كفر الشيخ - الشارع الرئيسي بجوار السنترال', N'ج.م (EGP)', N'Xprinter XP-80C', N'Xprinter XP-235B', 1, 1, 1);

-- خطوط التحويل الافتراضية (فودافون كاش وإنستاباي) من connectionsettings.json والبيانات التجريبية
IF NOT EXISTS (SELECT 1 FROM dbo.TransferLines WHERE PhoneNumber = N'01012345678')
    INSERT INTO dbo.TransferLines (Provider, ProviderAr, PhoneNumber, CurrentBalance, DailyLimit, MonthlyLimit, DailyUsed, MonthlyUsed, IsActive)
    VALUES (N'VodafoneCash', N'فودافون كاش', N'01012345678', 3000, 5000, 50000, 0, 0, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.TransferLines WHERE PhoneNumber = N'01098765432')
    INSERT INTO dbo.TransferLines (Provider, ProviderAr, PhoneNumber, CurrentBalance, DailyLimit, MonthlyLimit, DailyUsed, MonthlyUsed, IsActive)
    VALUES (N'VodafoneCash', N'فودافون كاش', N'01098765432', 5000, 8000, 80000, 0, 0, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.TransferLines WHERE PhoneNumber = N'01100032165')
    INSERT INTO dbo.TransferLines (Provider, ProviderAr, PhoneNumber, CurrentBalance, DailyLimit, MonthlyLimit, DailyUsed, MonthlyUsed, IsActive)
    VALUES (N'InstaPay', N'إنستاباي', N'01100032165', 10000, 15000, 150000, 0, 0, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.TransferLines WHERE PhoneNumber = N'012985411111')
    INSERT INTO dbo.TransferLines (Provider, ProviderAr, PhoneNumber, CurrentBalance, DailyLimit, MonthlyLimit, DailyUsed, MonthlyUsed, IsActive)
    VALUES (N'InstaPay', N'إنستاباي', N'012985411111', 1000, 3000, 30000, 0, 0, 1);

COMMIT TRANSACTION;
GO
