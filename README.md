# ProjectZ — تسجيل الدخول والخروج

مشروع C# / WPF يستهدف **.NET 10 LTS**، باتصال مباشر إلى **SQL Server 2016 SP1 Express (13.0.4001.0)**، ومستوى توافق **130**. لا توجد API أو ORM أو قواعد محلية أو وظائف إضافية.

## حالة التسليم — غير مكتمل الاعتماد

تم إعداد المصدر والسكربتات وإعدادات النشر والأدلة. **لم يُبنَ EXE في هذه البيئة، ولم يتم تشغيل WPF أو اختبار SQL Server 2016 SP1 فعليًا أو جهازين على LAN.** البيئة Linux بلا .NET SDK أو SQL Server. لا يعد المشروع مكتملًا حتى تنفيذ [سجل القبول](docs/ACCEPTANCE.md) على البيئة المطلوبة. فحص XML أو مراجعة SQL لا يعادل اختبار تشغيل.

## الملفات

- `ProjectZ.sln`: الحل، افتحه ببرنامج Visual Studio يدعم .NET 10 وعبء عمل **.NET desktop development**.
- `src/ProjectZ/`: Models / Data / Services / Views / ViewModels / Resources / Helpers.
- `sql/01_CreateDatabase.sql`: إنشاء القاعدة وجدول الدخول الأصلي `dbo.Users`.
- `sql/02_SeedAdmin.sql`: إضافة admin دون إعادة تعيين حساب موجود.
- `sql/06_CreateFullSchema.sql`: مخطط الأعمال الكامل والعلاقات والقيود والفهارس دون تعديل ربط التطبيق الحالي.
- `sql/07_CreateReportingViews.sql`: طرق عرض المخزون والأرباح والمتبقي والمدفوعات واستخدام الحدود.
- `sql/08_VerifyFullSchema.sql`: تحقق قراءة فقط من الجداول والعلاقات وطرق العرض.
- [تصميم قاعدة البيانات](docs/DATABASE.md): خريطة الجداول وقرارات الاتساق وترتيب التنفيذ.
- `sql/03_VerifyEnvironment.sql`: استعلامات التحقق من البيئة فقط.
- `sql/04_SetupLanSqlAuth.sql`: تفعيل Mixed Mode وإنشاء حساب اتصال الشبكة `ProjectZClient` بصلاحية `SELECT` فقط.
- `src/ProjectZ/connectionsettings.json`: إعدادات خارجية؛ يلزم استبدال عنوان الخادم.
- [تجهيز الجهاز الرئيسي](docs/SERVER.md) · [العملاء وتغيير الخادم والنشر](docs/CLIENTS.md).
- `.github/workflows/windows-build.yml`: بناء Windows واختبارات hashing ونشر artifact؛ لا يحل محل اختبار SQL/WPF الفعلي.

## إغلاق التطبيق وعملية Windows

يضبط التطبيق `ShutdownMode="OnLastWindowClose"` ويتتبع النوافذ المفتوحة، فينهي WPF عند إغلاق آخر نافذة. أثناء تسجيل الدخول أو الخروج، تُعرض النافذة البديلة قبل إغلاق الحالية. تُلغى محاولات الدخول الجارية عند الإغلاق، ولا يُسمح لنتيجة SQL متأخرة بفتح نافذة جديدة بعد إغلاق نافذة الدخول. للتحقق على Windows، أغلق بـ X أو Alt+F4 قبل الدخول وبعده وبعد تسجيل الخروج، وتأكد من اختفاء `ProjectZ.exe` من Task Manager؛ راجع الاختبارين 16 و17 في [سجل القبول](docs/ACCEPTANCE.md). لم يُشغّل WPF فعليًا في بيئة العمل الحالية.

## واجهة ProjectZ العربية

اسم التطبيق وملف التشغيل `ProjectZ.exe` واسم قاعدة البيانات `ProjectZ`. إذا كانت لديك قاعدة قديمة باسم `FitohPhone`، خذ نسخة احتياطية وأغلق الاتصالات ثم شغّل `sql/05_RenameLegacyDatabaseToProjectZ.sql`؛ لا تشغّل إعداد قاعدة جديدة فوق قاعدة قديمة لم تُرحّل.

الواجهة باللغة العربية وباتجاه RTL (`ar-EG`). شريط عنوان Windows يبقى LTR حتى تظهر أزرار التصغير والتكبير والإغلاق على اليمين، بينما محتوى النوافذ RTL. يبقى إدخال اسم المستخدم وكلمة المرور LTR لأنهما مُعرّفان. بعد تسجيل الدخول تظهر قائمة جانبية قابلة للتمرير بفئات المبيعات والمشتريات والصيانة والمالية والعملاء والموردين والمستخدمين والإعدادات. عناصرها للتحديد فقط حاليًا؛ صفحات الوظائف لم تُربط بعد، وتعرض مساحة العمل توضيحًا بذلك بدل ادعاء تنفيذها.

## بناء ونشر من Windows

```powershell
dotnet run --project tests/ProjectZ.Security.Tests -c Release
dotnet build ProjectZ.sln -c Release
dotnet publish src/ProjectZ/ProjectZ.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
```

النتيجة المتوقعة بعد نجاح الأمر: `publish/win-x64/ProjectZ.exe` و`connectionsettings.json`. انسخ **محتويات مجلد النشر بالكامل**. ملف الإعدادات مقصود أن يبقى منفصلًا. يمكن أيضًا استخدام ملف تعريف FolderProfile من Visual Studio. من GitHub Actions بعد دفع الفرع وتشغيل workflow، نزّل artifact باسم `ProjectZ-win-x64`؛ لم يتم تشغيله أو رفعه في هذه الجلسة.

Microsoft.Data.SqlClient مثبت على **6.1.3** (خط 6.1 LTS ويدعم SQL Server 2016؛ تأكيد اتصال SP1 الفعلي ما زال مطلوبًا). النقل يستخدم SqlConnection وSqlCommand وSqlDataReader وSqlParameter؛ اسم المستخدم parameter من نوع NVARCHAR(100). PBKDF2-HMAC-SHA256 بعدد 600,000 دورة، salt عشوائي 16 بايت، ناتج 32 بايت، ومقارنة ثابتة الزمن. لا تُرسل كلمة مرور مستخدم التطبيق إلى SQL؛ يُجلب hash ويُفحص محليًا. حساب SQL للاتصال مختلف عن حساب التطبيق admin.

لا تحفظ كلمة مرور المستخدم في ViewModel؛ تُقرأ PasswordBox عند الضغط وتُمسح فورًا. كأي API يعتمد string، لا يمكن ضمان محو نسخة كلمة المرور فورًا من ذاكرة CLR. الجلسة بالذاكرة فقط، وتُزال عند الخروج، ولا يتم حفظ بيانات دخول أو إظهار تفاصيل SqlException للمستخدم.
