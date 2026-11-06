# دليل تجهيز SQL Server 2016 SP1 Express على الجهاز الرئيسي

## 1. المتطلبات
- جهاز رئيسي يعمل بنظام Windows (10/11 أو Windows Server).
- Microsoft SQL Server 2016 SP1 Express Edition (64-bit) — Product Version 13.0.4001.0.
- SQL Server Management Studio (SSMS) ANY version that can connect to 2016 (e.g. SSMS 17/18).

## 2. خطوات التثبيت
1. نزّل SQL Server 2016 SP1 Express من موقع Microsoft الرسمي.
2. اختر **New SQL Server stand-alone installation**.
3. فعّل **SQL Server Authentication** (Mixed Mode) إذا كنت تريد استخدام SQL Login، أو اترك Windows Authentication فقط.
4. أكمل التثبيت مع الإعدادات الافتراضية.
5. تأكد من تشغيل الخدمة:
   - افتح **SQL Server Configuration Manager** ← **SQL Server Services** ← تأكد أن **SQL Server (SQLEXPRESS)** في حالة **Running** (إذا اخترت named instance SQLEXPRESS) أو **SQL Server (MSSQLSERVER)** للإinstance الافتراضية.

## 3. تفعيل TCP/IP (مطلوب لاتصالات أجهزة الشبكة)
1. في **SQL Server Configuration Manager** اذهب إلى **SQL Server Network Configuration** ← **Protocols for SQLEXPRESS** (أو MSSQLSERVER).
2. فعّل **TCP/IP** (Enabled).
3. افتح خصائص TCP/IP ← تبويب **IP Addresses** ← في **IPAll**Weak put **TCP Port = 1433** (أو اتركها ديناميكية مع تفعيل **SQL Server Browser**).
4. أعد تشغيل خدمة SQL Server.

## 4. فتح المنفذ في جدار الحماية
افتح PowerShell كمسؤول ونفّذ:
```powershell
New-NetFirewallRule -DisplayName "SQL Server 2016" -Direction Inbound -Protocol TCP -LocalPort 1433 -Action Allow
```

## 5. إنشاء قاعدة البيانات
1. افتح SSMS واتصل بالجهاز الرئيسي: `localhost\SQLEXPRESS` (أو `localhost`).
2. نفّذ السكربتات بالترتيب من مجلد `SqlScripts`:
   - `01_CreateDatabase.sql`
   - `02_CreateUsersTable.sql`
   - `03_SeedAdminUser.sql`
3. تأكد من أن Compatibility Level = 130:
```sql
SELECT name, compatibility_level FROM sys.databases WHERE name = N'FitohPhone';
```

## 6. إنشاء Login خاص بالتطبيق (اختياري - SQL Authentication)
```sql
USE master;
GO
CREATE LOGIN FitohPhoneUser WITH PASSWORD = N'StrongPassword123!';
GO
USE FitohPhone;
GO
CREATE USER FitohPhoneUser FOR LOGIN FitohPhoneUser;
GO
ALTER ROLE db_datareader ADD MEMBER FitohPhoneUser;
ALTER ROLE db_datawriter ADD MEMBER FitohPhoneUser;
GO
```

## 7. التحقق من الاتصال من جهاز آخر
من جهاز عميل في نفس الشبكة:
```powershell
Test-NetConnection -ComputerName MAIN-PC-NAME -Port 1433
```
(استبدل MAIN-PC-NAME باسم الجهاز الرئيسي أو استخدم IP مثل 192.168.1.10)
