# FitohPhone — نظام تسجيل دخول WPF

برنامج WPF حديث يعمل على several أجهزة داخل الشبكة المحلية (LAN)، يتصل مباشرة بقاعدة بيانات Microsoft SQL Server 2016 SP1 Express على الجهاز الرئيسي.

## التقنيات
- C# / WPF / XAML — .NET 10 LTS
- MVVM بسيط (بدون أطر خارجية)
- Microsoft.Data.SqlClient — اتصال مباشر (SqlConnection / SqlCommand / SqlParameter / SqlDataReader)
- SQL Server 2016 SP1 Express — Compatibility Level 130
- تخزين كلمات المرور بتشفير PBKDF2-SHA256 (100,000 iterations)

## بنية المشروع
```
FitohPhone.sln
FitohPhone/
├── Models/          → User.cs
├── Data/            → DatabaseConnectionFactory.cs, UserRepository.cs
├── Services/        → AuthService.cs
├── ViewModels/      → ViewModelBase, LoginViewModel, MainViewModel
├── Views/           → LoginWindow, MainWindow (XAML)
├── Resources/       → Styles.xaml (ألوان و أنماط ERP)
├── Helpers/         → RelayCommand, PasswordHasher, ConnectionSettings
├── appsettings.json → إعدادات الاتصال الخارجية (عنوان السيرفر)
SqlScripts/
├── 01_CreateDatabase.sql
├── 02_CreateUsersTable.sql
└── 03_SeedAdminUser.sql
Docs/
├── 01_SQLServer2016_Setup_Guide.md
├── 02_ClientMachines_Guide.md
└── 03_ChangeServerAddress_Guide.md
```

## بيانات الدخول الافتراضية
- Username: `admin`
- Password: `Admin@123`

## النشر (Publish)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
النتيجة: ملف `FitohPhone.exe` واحد يعمل مباشرة على أي جهاز Windows بدون تثبيت .NET أو SQL Server.

## القيود العامة
- لا يُسمح بإضافة أي وظيفة خارج تسجيل الدخول وتسجيل الخروج.
- جميع سكربتات SQL متوافقة مع SQL Server 2016 SP1 فقط (Compatibility Level 130).
