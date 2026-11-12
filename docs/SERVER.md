# تجهيز الجهاز الرئيسي — SQL Server 2016 SP1 Express

هذا الدليل لا يطلب ترقية SQL Server. بيئة القبول المستهدفة: Express x64، ProductVersion = `13.0.4001.0`، ProductLevel = `SP1`، compatibility = `130`.

## 1. تجهيز القاعدة

إذا كانت لديك قاعدة سابقة باسم `FitohPhone` وتريد الاحتفاظ بالبيانات، خذ نسخة احتياطية وأوقف التطبيق وكل الاتصالات، ثم شغّل `sql/05_RenameLegacyDatabaseToProjectZ.sql` أولًا. السكربت `01` يوقف الإعداد إذا وجد القاعدة القديمة كي لا ينشئ قاعدة `ProjectZ` فارغة بجانبها.

1. افتح SSMS على الخادم بحساب إداري. للتثبيت الجديد شغّل `sql/01_CreateDatabase.sql` ثم `sql/02_SeedAdmin.sql` بالترتيب. بعدهما شغّل `sql/06_CreateFullSchema.sql` ثم `sql/07_CreateReportingViews.sql` لإنشاء قاعدة الأعمال الكاملة. لا تشغّل السكربتات عبر SqlCommand كما هي؛ `GO` فاصل دفعات خاص بـ SSMS/sqlcmd وليس T-SQL.
2. السكربتات تعيد التشغيل بأمان، ولا تحذف بيانات، ولا تعيد تعيين admin موجود. إذا كانت القاعدة موجودة مسبقًا بجدول مختلف، راجع بنيته؛ السكربت لا يقوم بترحيل جدول غير مطابق تلقائيًا.
3. شغّل `sql/03_VerifyEnvironment.sql` ثم `sql/08_VerifyFullSchema.sql` واحفظ النتائج في سجل القبول. يجب ألا يعرض التحقق الكامل جداول أو طرق عرض ناقصة أو مفاتيح أجنبية معطلة/غير موثوقة. راجع [تصميم قاعدة البيانات](DATABASE.md).
4. الحساب التجريبي: `admin` / `Admin@123`، الاسم `System Administrator`، نشط. المخزن hash وليس نص كلمة المرور. كلمة المرور الافتراضية معلومة؛ لا تضع النسخة في استخدام إنتاجي حساس قبل استبدال hash بواسطة مسؤول النظام بأداة موثوقة تنتج الصيغة الموثقة في PasswordHasher. لا توجد شاشة تغيير كلمة مرور ضمن النطاق المطلوب.

## 2. تمكين LAN

1. من **SQL Server Configuration Manager**، تحت SQL Server Network Configuration، فعّل TCP/IP للـ instance المستخدمة.
2. لضبط الاتصال: TCP/IP Properties → IP Addresses → IPAll، أفرغ TCP Dynamic Ports، وحدد TCP Port ثابتًا مثل `1433` (أو منفذًا متاحًا تختاره المؤسسة). راجع أيضًا عناوين IP المفعلة. أعد تشغيل خدمة instance.
3. افتح في Windows Firewall **TCP للمنفذ المحدد فقط** ولعناوين شبكة LAN الموثوقة، لا الإنترنت.
4. يفضل عنوان ثابت أو DHCP reservation للخادم. استخدم `192.168.1.10,1433` أو `MAIN-PC,1433` للعملاء.
5. عند استعمال `MAIN-PC\SQLEXPRESS` بدل منفذ صريح: يلزم حل اسم الجهاز وتشغيل SQL Server Browser وفتح UDP 1434 بالإضافة إلى منفذ SQL TCP. IP مع instance مدعوم أيضًا عند توفر Browser. المنفذ الثابت يتجنب الاعتماد عليه.
6. اختبر من العميل: `Test-NetConnection 192.168.1.10 -Port 1433`. نجاح TCP وحده لا يثبت صلاحيات SQL أو TLS.

## 3. هوية الاتصال وصلاحياتها

عند استخدام SQL Authentication، شغّل `sql/04_SetupLanSqlAuth.sql` بعد تجهيز قاعدة `ProjectZ`؛ ينشئ حساب الاتصال `ProjectZClient` بصلاحية `SELECT` فقط. إذا غُيّرت كلمة مرور SQL محليًا، حدّثها في السكربت وملف الإعدادات معًا.

**Windows Authentication** مفضلة في domain: امنح مجموعة Windows مخصصة login وuser مع SELECT فقط على `dbo.Users`. لا يكفي أن حساب Windows المحلي يحمل الاسم نفسه على جهازين. في workgroup يمكن استخدام SQL Authentication.

مثال ينفذه مسؤول SQL بعد استبدال الاسم باسم مجموعة حقيقية (ليست جداول إضافية):

```sql
USE [master];
CREATE LOGIN [DOMAIN\ProjectZUsers] FROM WINDOWS;
GO
USE [ProjectZ];
CREATE USER [DOMAIN\ProjectZUsers] FOR LOGIN [DOMAIN\ProjectZUsers];
GRANT SELECT ON OBJECT::dbo.Users TO [DOMAIN\ProjectZUsers];
GO
```

**SQL Authentication** عند الحاجة: فعّل Mixed Mode من Server Properties → Security ثم أعد تشغيل الخدمة. أنشئ login مخصصًا بكلمة قوية وسياسة كلمات المرور، واربطه بـ user في ProjectZ وامنحه `SELECT` على dbo.Users فقط كما أعلاه. لا تستخدم `sa` أو db_owner، ولا تضع الحساب الإداري في ملف الإعدادات. توليد كلمة السر وإدخالها يتم محليًا لدى المسؤول؛ لا ترسلها في المحادثات أو Git.

## 4. التشفير

الإعداد المجهز للشبكة المحلية مع SQL Server 2016 SP1 Express هو `Encrypt=true` و`TrustServerCertificate=true` لأن SQL Server Express يولّد شهادة TLS ذاتية التوقيع افتراضيًا، ومكتبة `Microsoft.Data.SqlClient` ترفض الاتصال عند ضبط `TrustServerCertificate=false` ما لم تُثبَّت شهادة موثوقة من جهة إصدار (CA).

في البيئات الإنتاجية التي تملك شهادة CA داخلية أو عامة: ثبّت الشهادة على SQL Server وسلسلة الثقة على أجهزة العملاء بحيث يطابق اسم الخادم حقل SAN، ثم اضبط `TrustServerCertificate=false` لمنع هجمات الوسيط. لا تعالج فشل TLS بإطفاء التشفير (`Encrypt=false`). لا نحتاج خصائص TDS أو `Encrypt=Strict` الخاصة بالخوادم الحديثة.

## 5. حدود أمان الاتصال المباشر

كل عميل يملك حق SELECT يمكنه قراءة hashes، ويمكن تعديل تطبيق العميل محليًا؛ تسجيل الدخول هنا ليس حاجز صلاحيات ضد مالك جهاز عميل خبيث. هذا أثر بنيوي للاتصال المباشر المطلوب. احمِ LAN والأجهزة والملف، وقيد صلاحيات SQL، وخذ نسخًا احتياطية. لا توجد كتابة إلى قاعدة البيانات أثناء تسجيل الدخول أو الخروج، ولا جداول جلسات أو سجلات إضافية.
