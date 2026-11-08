# FitohPhone — تسجيل الدخول والخروج

مشروع C# / WPF يستهدف **.NET 10 LTS**، باتصال مباشر إلى **SQL Server 2016 SP1 Express (13.0.4001.0)**، ومستوى توافق **130**. لا توجد API أو ORM أو قواعد محلية أو وظائف إضافية.

## حالة التسليم — غير مكتمل الاعتماد

تم إعداد المصدر والسكربتات وإعدادات النشر والأدلة. **لم يُبنَ EXE في هذه البيئة، ولم يتم تشغيل WPF أو اختبار SQL Server 2016 SP1 فعليًا أو جهازين على LAN.** البيئة Linux بلا .NET SDK أو SQL Server. لا يعد المشروع مكتملًا حتى تنفيذ [سجل القبول](docs/ACCEPTANCE.md) على البيئة المطلوبة. فحص XML أو مراجعة SQL لا يعادل اختبار تشغيل.

## الملفات

- `FitohPhone.sln`: الحل، افتحه ببرنامج Visual Studio يدعم .NET 10 وعبء عمل **.NET desktop development**.
- `src/FitohPhone/`: Models / Data / Services / Views / ViewModels / Resources / Helpers.
- `sql/01_CreateDatabase.sql`: القاعدة والجدول الوحيد `dbo.Users`.
- `sql/02_SeedAdmin.sql`: إضافة admin دون إعادة تعيين حساب موجود.
- `sql/03_VerifyEnvironment.sql`: استعلامات التحقق فقط.
- `sql/04_SetupLanSqlAuth.sql`: تفعيل Mixed Mode وإنشاء حساب اتصال الشبكة `FitohPhoneClient` بصلاحية `SELECT` فقط.
- `src/FitohPhone/connectionsettings.json`: إعدادات خارجية؛ يلزم استبدال عنوان الخادم.
- [تجهيز الجهاز الرئيسي](docs/SERVER.md) · [العملاء وتغيير الخادم والنشر](docs/CLIENTS.md).
- `.github/workflows/windows-build.yml`: بناء Windows واختبارات hashing ونشر artifact؛ لا يحل محل اختبار SQL/WPF الفعلي.

## إغلاق التطبيق وعملية Windows

يضبط التطبيق `ShutdownMode="OnLastWindowClose"` ويتتبع النوافذ المفتوحة، فينهي WPF عند إغلاق آخر نافذة. أثناء تسجيل الدخول أو الخروج، تُعرض النافذة البديلة قبل إغلاق الحالية. تُلغى محاولات الدخول الجارية عند الإغلاق، ولا يُسمح لنتيجة SQL متأخرة بفتح نافذة جديدة بعد إغلاق نافذة الدخول. للتحقق على Windows، أغلق بـ X أو Alt+F4 قبل الدخول وبعده وبعد تسجيل الخروج، وتأكد من اختفاء `FitohPhone.exe` من Task Manager؛ راجع الاختبارين 16 و17 في [سجل القبول](docs/ACCEPTANCE.md). لم يُشغّل WPF فعليًا في بيئة العمل الحالية.

## واجهة عربية من اليمين إلى اليسار

تُعرض الواجهات باللغة العربية وباتجاه RTL (`ar-EG`)؛ عناوين النوافذ ومحاذاة النصوص وترتيب لوحة تسجيل الدخول ملائمة للقراءة من اليمين إلى اليسار. يبقى إدخال اسم المستخدم وكلمة المرور LTR لأنهما مُعرّفان، مع بقاء التسميات والرسائل والأزرار بالعربية.

## بناء ونشر من Windows

```powershell
dotnet run --project tests/FitohPhone.Security.Tests -c Release
dotnet build FitohPhone.sln -c Release
dotnet publish src/FitohPhone/FitohPhone.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
```

النتيجة المتوقعة بعد نجاح الأمر: `publish/win-x64/FitohPhone.exe` و`connectionsettings.json`. انسخ **محتويات مجلد النشر بالكامل**. ملف الإعدادات مقصود أن يبقى منفصلًا. يمكن أيضًا استخدام ملف تعريف FolderProfile من Visual Studio. من GitHub Actions بعد دفع الفرع وتشغيل workflow، نزّل artifact باسم `FitohPhone-win-x64`؛ لم يتم تشغيله أو رفعه في هذه الجلسة.

Microsoft.Data.SqlClient مثبت على **6.1.3** (خط 6.1 LTS ويدعم SQL Server 2016؛ تأكيد اتصال SP1 الفعلي ما زال مطلوبًا). النقل يستخدم SqlConnection وSqlCommand وSqlDataReader وSqlParameter؛ اسم المستخدم parameter من نوع NVARCHAR(100). PBKDF2-HMAC-SHA256 بعدد 600,000 دورة، salt عشوائي 16 بايت، ناتج 32 بايت، ومقارنة ثابتة الزمن. لا تُرسل كلمة مرور مستخدم التطبيق إلى SQL؛ يُجلب hash ويُفحص محليًا. حساب SQL للاتصال مختلف عن حساب التطبيق admin.

لا تحفظ كلمة مرور المستخدم في ViewModel؛ تُقرأ PasswordBox عند الضغط وتُمسح فورًا. كأي API يعتمد string، لا يمكن ضمان محو نسخة كلمة المرور فورًا من ذاكرة CLR. الجلسة بالذاكرة فقط، وتُزال عند الخروج، ولا يتم حفظ بيانات دخول أو إظهار تفاصيل SqlException للمستخدم.
