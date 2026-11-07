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
- `src/FitohPhone/connectionsettings.json`: إعدادات خارجية؛ يلزم استبدال عنوان الخادم.
- [تجهيز الجهاز الرئيسي](docs/SERVER.md) · [العملاء وتغيير الخادم والنشر](docs/CLIENTS.md).
- `.github/workflows/windows-build.yml`: بناء Windows واختبارات hashing ونشر artifact؛ لا يحل محل اختبار SQL/WPF الفعلي.

## إغلاق التطبيق وعملية Windows

`App.xaml` يضبط `ShutdownMode="OnLastWindowClose"`؛ كان مضبوطًا على `OnExplicitShutdown`، وهو ما كان يُبقي العملية شغالة في الخلفية بعد إغلاق النافذة لأن الإغلاق كان معتمدًا على شرط `MainWindow` قد لا يتحقق عند تبديل النوافذ. الآن `App.xaml.cs` يتتبع كل نافذة يفتحها البرنامج، ويعرض النافذة البديلة **قبل** إغلاق القديمة (حتى لا يصل عدد النوافذ إلى صفر أثناء الدخول أو تسجيل الخروج)، ثم ينادي `Shutdown()` مرة واحدة فقط عند إغلاق آخر نافذة. محاولات الدخول الجارية تُلغى، ولا يُسمح بفتح نافذة جديدة بعد بدء الإغلاق أو لاكتمال استعلام SQL متأخر، وتُنظّف مصادر الإلغاء في `OnExit`. لا تُخفى أي نافذة (`Hide`)؛ تُغلق دائمًا.

للتحقق يدويًا على Windows: أغلق النافذة بـ X أو Alt+F4 ثم تأكد من اختفاء `FitohPhone.exe` من إدارة المهام، وكرر ذلك بعد تسجيل الدخول وبعد تسجيل الخروج وأثناء اتصال بطيء (الاختباران 16 و17 في [سجل القبول](docs/ACCEPTANCE.md)). هذه مراجعة مصدر وفحص بنيوي فقط؛ لم يُشغّل WPF فعليًا في بيئة العمل الحالية.

## بناء ونشر من Windows

```powershell
dotnet run --project tests/FitohPhone.Security.Tests -c Release
dotnet build FitohPhone.sln -c Release
dotnet publish src/FitohPhone/FitohPhone.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
```

النتيجة المتوقعة بعد نجاح الأمر: `publish/win-x64/FitohPhone.exe` و`connectionsettings.json`. انسخ **محتويات مجلد النشر بالكامل**. ملف الإعدادات مقصود أن يبقى منفصلًا. يمكن أيضًا استخدام ملف تعريف FolderProfile من Visual Studio. من GitHub Actions بعد دفع الفرع وتشغيل workflow، نزّل artifact باسم `FitohPhone-win-x64`؛ لم يتم تشغيله أو رفعه في هذه الجلسة.

Microsoft.Data.SqlClient مثبت على **6.1.3** (خط 6.1 LTS ويدعم SQL Server 2016؛ تأكيد اتصال SP1 الفعلي ما زال مطلوبًا). النقل يستخدم SqlConnection وSqlCommand وSqlDataReader وSqlParameter؛ اسم المستخدم parameter من نوع NVARCHAR(100). PBKDF2-HMAC-SHA256 بعدد 600,000 دورة، salt عشوائي 16 بايت، ناتج 32 بايت، ومقارنة ثابتة الزمن. لا تُرسل كلمة مرور مستخدم التطبيق إلى SQL؛ يُجلب hash ويُفحص محليًا. حساب SQL للاتصال مختلف عن حساب التطبيق admin.

لا تحفظ كلمة مرور المستخدم في ViewModel؛ تُقرأ PasswordBox عند الضغط وتُمسح فورًا. كأي API يعتمد string، لا يمكن ضمان محو نسخة كلمة المرور فورًا من ذاكرة CLR. الجلسة بالذاكرة فقط، وتُزال عند الخروج، ولا يتم حفظ بيانات دخول أو إظهار تفاصيل SqlException للمستخدم.
