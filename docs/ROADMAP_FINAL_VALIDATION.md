# تقرير التحقق النهائي لخارطة طريق نظام إدارة محل الموبايل — Final Validation Report

> **المستند المُراجَع:** `docs/MOBILE_SHOP_DEVELOPMENT_ROADMAP.md`
> **المستند الحالي:** `docs/ROADMAP_FINAL_VALIDATION.md`
> **التاريخ:** 2026-11-07 (Africa/Cairo)
> **الفرع:** `arena/3f5c2194-projectz` — **HEAD:** `cfd470cbe5b645066e8bbf52bb077240ee69a9e9`
> **المستودع:** `kamal-solaiman/ProjectZ`
> **نوع العمل:** تحقق وتصحيح توثيقي فقط — **لم يُكتب كود تطبيقي، ولم تُعدّل ملفات المصدر، ولم تُغيّر قاعدة بيانات، ولم يُنفَّذ أي سكربت، ولم يبدأ أي تنفيذ**

---

## 0. ملخص تنفيذي

| البند | النتيجة |
|---|---|
| هوية المستودع/الفرع/الـ commit | **مُتحقَّق منها**، لكن أساس التدقيق الأصلي في الخارطة كان قديمًا وصُحِّح |
| وجود حل باسم MobileShop أو API أو Flutter | **لا يوجد** في هذا المستودع ولا في فروع `origin` الثلاثين ولا في مستودعات المالك؛ الاسم «Mobile Shop» وصف لنطاق العمل لا لمشروع آخر |
| SQL Server 2016 SP1 كاعتمادية إنتاجية | **مرفوض تقنيًا**: خارج الدعم؛ التوصية SQL Server 2022 أو 2025 مع الإبقاء على `COMPATIBILITY_LEVEL = 130` |
| القرارات المفتوحة | أُعيد فحص **37** قرارًا؛ اكتُشفت فجوة «ربحية الصيانة» فأُضيف `OD-FIN-24` → **38** قرارًا، وصُحِّح `OD-ARC-08/09` و`OD-DEP-04` |
| المراحل والمتطلبات | **20 مرحلة × 15 بندًا** و**419 متطلبًا** و**419 صفًا** في مصفوفة Q — سليمة بعد التصحيح |
| نظام الدخول العامل | **محفوظ**؛ السلسلة ما زالت قائمة في المصدر، ولا يوجد أي مقترح لاستبدالها |
| التصحيحات المطبَّقة | **87 تعديلًا موجّهًا** على مستند الخارطة فقط، مُوثّقة في القسم 6 |
| قرار الاعتماد | **يلزم توضيح إضافي قبل أي تنفيذ** — التفصيل والبيان الصريح في القسم 9 |

---

## 1. التحقق من هوية المستودع والفرع والـ commit (البند 1)

### 1.1 حقائق مُتحقَّق منها بأوامر فعلية

| الفحص | الأمر | النتيجة المُتحقَّق منها |
|---|---|---|
| الفرع | `git branch --show-current` | `arena/3f5c2194-projectz` |
| الـ commit | `git rev-parse HEAD` | `cfd470cbe5b645066e8bbf52bb077240ee69a9e9` |
| عنوان الـ commit | `git log --oneline -4` | `cfd470c Merge pull request #35 from kamal-solaiman/arena/2a3f5cb6-projectz` ← `3d13b89` إضافة الخارطة ← `35f6043` دمج PR #34 ← `8e964a9` دمج PR #33 |
| عدد الملفات | `git ls-tree -r --name-only HEAD` مع عدّ الأسطر | **44** ملفًا متتبّعًا (43 دون مستند الخارطة) |
| الحل | `grep '^Project(' FitohPhone.sln` | مشروع WPF واحد `FitohPhone` + مشروع `FitohPhone.Configuration.Tests`؛ لا يوجد مشروع API |
| التقنية | `global.json`, `FitohPhone.csproj`, `README.md:3` | C# / `net10.0-windows` / `UseWPF` / `Microsoft.Data.SqlClient 6.1.3` / SDK `10.0.100` |
| قاعدة البيانات الموثّقة | `sql/01_CreateDatabase.sql`, `docs/SERVER.md:1-3` | قاعدة `FitohPhone` بجدول واحد `dbo.Users` و`COMPATIBILITY_LEVEL = 130`؛ المستندات تستهدف `13.0.4001.0 / SP1` |
| إعدادات الاتصال | `src/FitohPhone/connectionsettings.json` | قسم `Default` بـ `192.168.1.46,1433` و`IntegratedSecurity=false` و`UserId=FitohPhoneClient` و**`Password` بنص صريح داخل Git**، مع استثناء `MAIN-PC` |
| سجل القبول | `docs/ACCEPTANCE.md:18-38` | **21 اختبارًا**، كلها «لم يُختبر» |
| الفحوص البنيوية | `python3 tools/static_checks.py` (قراءة فقط) | كلها `PASS` + سطر صريح `NOT RUN: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish` |
| سلفية الـ commits | `git merge-base --is-ancestor` | `35f6043` (PR #34) ⇒ `YES`؛ `8e964a9` (PR #33) ⇒ `YES`؛ لا انحراف بين الفرع و`origin/main` |
| التاريخ الكامل | `git fetch --unshallow origin` ثم `git rev-list --all --count` | 41 commit؛ استُخدمت للمقارنة التاريخية فقط |

### 1.2 حل التعارض: `FitohPhone` مقابل «Mobile Shop / API / Flutter»

> **قاعدة العمل المتبعة:** لم يُفترض أن المشروعين نفس المشروع. طُلب البحث عن مكوّنات تحمل اسم `MobileShop` أو تقنيات `Flutter`/`Dart`/`API` قبل إصدار أي حكم.

| مستوى البحث | الأداة | النتيجة |
|---|---|---|
| شجرة العمل الحالية | `find` + `git ls-tree -r --name-only HEAD` | لا يوجد `pubspec.yaml`، لا `.dart`، لا `*.Api.csproj`، لا `Controllers/`، لا `Startup.cs`، لا `WebApi`؛ الملفان الوحيدان باسم `Program.cs` هما مشروعا اختبار console |
| محتوى الشجرة | `git grep -in -E` بتعابير `MobileShop` و`Flutter` و`ASP.NET` و`WebApi` عدا مستند الخارطة | **صفر نتيجة** |
| كل فروع `origin` | `git fetch origin '+refs/heads/*:refs/remotes/audit/*'` ⇒ 30 فرعًا + `git ls-tree` لكل فرع | لا يوجد أي مسار `MobileShop`/`flutter`/`.dart`/`pubspec`/`Api.csproj`/`Controllers/`؛ الفروع التاريخية (PR #9–#31) كانت شاشات WPF/XAML وجداول SQL وأُزيلت بـ PR #32 |
| مستودعات المالك | `gh repo list kamal-solaiman` | مستودعان فقط: `ProjectZ` (C#) و`Unpack-client` (C++) |
| بحث مستودعات باسم MobileShop | `gh search repos MobileShop --owner kamal-solaiman` | `[]` |
| بحث كود Flutter داخل المستودع | `gh api search/code -f q='Flutter repo:kamal-solaiman/ProjectZ'` | `total_count = 0` |

**الاستنتاج المُثبت:** في هذا المستودع، «Mobile Shop» هو **وصف نطاق العمل التجاري** (محل بيع وصيانة هواتف) واسم ملف الخارطة، وليس اسم حل أو مشروع برمجي منفصل. اسم الحل/التجميع/المشروع هو `FitohPhone` في كل المراجع. **لا توجد مكوّنات API أو Flutter أو MobileShop solution في أي مرجع مُتفحّص.** وبالتالي تدقيق الخارطة يخصّ المشروع الصحيح من حيث المعمارية، لكنه كان مبنيًا على commit قديم؛ وهذا ما صُحِّح.

**ما لا يمكن إثباته من مساحة العمل هذه:** وجود مستودع آخر خارج حساب `kamal-solaiman` أو خارج `/home/user` يحمل اسم MobileShop ويحتوي API/Flutter. لم يُزوَّد الفريق بأي رابط أو مسار له. لذلك يبقى **تأكيد المستخدم لهوية المشروع المقصود شرطًا قبل التنفيذ** (القسم 9).

### 1.3 التصنيف الإلزامي: حقائق / افتراضات / قرارات مفتوحة

| التصنيف | العناصر |
|---|---|
| **حقائق مُتحقَّق منها** | كل ما في 1.1 و1.2؛ سلسلة الدخول؛ عدد الملفات 44؛ 21 اختبارًا غير مُنفّذ؛ كلمة مرور نص صريح في Git؛ `COMPATIBILITY_LEVEL=130`؛ نهاية دعم SQL Server 2016 (مصادر القسم 2) |
| **افتراضات لم تُثبت** | أن بيئة الإنتاج الفعلية ما زالت على SP1؛ أن `192.168.1.46` هو عنوان السيرفر الحقيقي؛ توافق `Microsoft.Data.SqlClient 6.1.3` مع SQL Server 2025 دون تحديث تعريف؛ بقاء الحجم داخل حدود Express؛ أن نظام الدخول «يعمل فعليًا» بمعنى مُختبَر على Windows (المصدر يثبت التنفيذ المنطقي فقط، و`docs/ACCEPTANCE.md` ينفي الاختبار الفعلي)؛ أن المقصود بـ «Mobile Shop» هو هذا المستودع |
| **قرارات مفتوحة** | 38 قرارًا `OD-*` في القسم 3؛ لا يجوز تحويل أي منها إلى قاعدة نهائية ضمن هذا التقرير |

---

## 2. إعادة تقييم اعتمادية SQL Server 2016 SP1 (البند 2)

### 2.1 الحقائق الخارجية الموثّقة

| الحقيقة | المصدر |
|---|---|
| SQL Server 2016 اتّبع سياسة دورة حياة ثابتة؛ انتهى الدعم الأساسي في 2021 وانتهى **الدعم الموسّع في 14/15 يوليو 2026** | [1](https://learn.microsoft.com/en-us/lifecycle/products/sql-server-2016) |
| **Service Pack 1 تحديدًا انتهى دعمه في 10 يوليو 2019**؛ آخر حزمة خدمة SP3 انتهت مع نهاية الدعم الموسّع | [1](https://learn.microsoft.com/en-us/lifecycle/products/sql-server-2016) |
| بعد نهاية الدعم الموسّع لا تُصدر Microsoft تصحيحات أو تحديثات أمنية مجانية؛ ESU برنامج **مدفوع ومؤقت** يقتصر على التحديثات المصنّفة Critical وينتهي في 17 يوليو 2029 | [3](https://learn.microsoft.com/en-us/sql/sql-server/end-of-support/extended-security-updates-frequently-asked-questions?view=sql-server-ver17), [4](https://learn.microsoft.com/en-us/lifecycle/faq/sql-server-extended-security-updates) |
| ESU «ليس حلًا طويل الأمد» ولا يوفّر دعمًا فنيًا أو ميزات جديدة، بل جسرًا مؤقتًا أثناء الترقية | [4](https://learn.microsoft.com/en-us/lifecycle/faq/sql-server-extended-security-updates) |
| SQL Server 2022: دعم أساسي حتى 2028 ودعم موسّع حتى 2033؛ SQL Server 2025: حتى 2031 / 2036 — كلاهما **مدعوم حاليًا** | [2](https://learn.microsoft.com/en-us/sql/sql-server/end-of-support/sql-server-end-of-support-overview?view=sql-server-ver17) |
| مستوى التوافق `130` **مدعوم على المحركات الأحدث**: SQL Server 2022 يدعم 160/150/140/**130**/120/110، وSQL Server 2025 يدعم 170/160/150/140/**130**/120/110 | [5](https://learn.microsoft.com/en-us/intune/configmgr/core/plan-design/configs/support-for-sql-server-versions), [6](https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql?view=sql-server-ver17) |
| قاعدة منقولة إلى 2022/2025 تحتفظ بمستوى توافقها السابق، ويُرفع المستوى لاحقًا كخطوة منفصلة مُختبرة | [7](https://www.codefirst.co.uk/end-of-life/sql-server-2016/), [8](https://learn.microsoft.com/en-us/sql/relational-databases/databases/view-or-change-the-compatibility-level-of-a-database?view=sql-server-ver17) |
| Express 2022: 10GB لكل قاعدة؛ Express 2025: **50GB**؛ كلاهما ~1.4GB buffer pool و4 أنوية، و**بلا SQL Server Agent** | [9](https://codewithsql.com/guides/sql-server-express), [10](https://codewithsql.com/compare/sql-server-express-vs-standard) |
| `READ_COMMITTED_SNAPSHOT` خيار على مستوى قاعدة البيانات يستخدم row versioning وليس مقصورًا على طبعة معينة | [11](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17) |
| بعض دوال T-SQL مرتبطة بإصدار المحرك لا بمستوى التوافق فقط: `STRING_AGG` متاحة بأي مستوى على محرك 2017+، بينما `TRIM` تتطلب 140+ | [12](https://learn.microsoft.com/en-us/sql/t-sql/functions/string-agg-transact-sql?view=sql-server-ver17) |

> تاريخ اليوم 2026-11-07 ⇒ نهاية الدعم الموسّع **حدثت بالفعل** قبل هذا التقرير بأشهر، وليست خطرًا مستقبليًا.

### 2.2 الأثر على المشروع (حقائق محلية)

- `README.md:3` و`docs/SERVER.md:1-3` و`docs/ACCEPTANCE.md:3,18,32` ما زالت تستهدف `SQL Server 2016 SP1 Express (13.0.4001.0)` صراحةً. هذه **مستندات قائمة لم تُعدّل** لأن المطلوب عدم تعديل ملفات المصدر، لكنها أصبحت غير صالحة كمرجع إنتاجي وسُجّلت كـ `TN-18` و`TN-19`.
- `sql/01_CreateDatabase.sql` يضبط `COMPATIBILITY_LEVEL = 130` و`tools/static_checks.py` يفرض «جدول واحد + مستوى 130»؛ لذلك **الحفاظ على المستوى 130 شرط تقني قابل للتحقق** وليس مجرد تفضيل.
- لا توجد بيئة Windows/SQL فعلية في مساحة العمل هذه؛ لا يمكن قياس ترقية فعلية هنا. كل ما يلي توصية تصميمية لا ادّعاء تنفيذ.

### 2.3 التوصية المعتمدة (تحل محل اعتمادية 2016 SP1)

| العنصر | التوصية | التبرير |
|---|---|---|
| **الإصدار الافتراضي** | **SQL Server 2022** (أحدث CU) مع `COMPATIBILITY_LEVEL = 130` | مدعوم حتى 2033، ناضج، واسع توافق التعريفات، ويقبل المستوى 130 [2](https://learn.microsoft.com/en-us/sql/sql-server/end-of-support/sql-server-end-of-support-overview?view=sql-server-ver17) [5](https://learn.microsoft.com/en-us/intune/configmgr/core/plan-design/configs/support-for-sql-server-versions) |
| **البديل عند الحاجة لسعة أكبر** | **SQL Server 2025** مع المستوى 130 (Express يرفع الحد إلى 50GB) | دعم حتى 2036 وسعة Express خمسة أضعاف [2](https://learn.microsoft.com/en-us/sql/sql-server/end-of-support/sql-server-end-of-support-overview?view=sql-server-ver17) [9](https://codewithsql.com/guides/sql-server-express) |
| **الطبعة** | Express للتجربة/محل صغير، Standard عند تعدد المستخدمين الكثيف أو الحاجة لـ SQL Agent وضغط النسخ | Express بلا Agent وذاكرة ~1.4GB [9](https://codewithsql.com/guides/sql-server-express) |
| **الحفاظ على التوافق** | نقل/استعادة القاعدة مع الإبقاء على `130`، ثم رفع تدريجي إلى `160` (2022) أو `170` (2025) بعد اختبارات انحدار، في مرحلة منفصلة ضمن `OD-ARC-09` | المستوى 130 مدعوم على المحركين، والرفع خطوة منفصلة مُختبرة [5](https://learn.microsoft.com/en-us/intune/configmgr/core/plan-design/configs/support-for-sql-server-versions) [7](https://www.codefirst.co.uk/end-of-life/sql-server-2016/) |
| **ما لا يتغير** | `dbo.Users`، صيغة PBKDF2، استعلام `UserRepository.FindAsync` المُعامل، `LoginWindow`، منطق الدخول، المخطط المقترح في E | لا استبدال للتقنية ولا إعادة بناء؛ تغيير نسخة المحرك فقط |
| **جدولة النسخ** | Windows Task Scheduler أو `BackupService` داخل الـ API بدل SQL Server Agent على Express | Express لا يملك Agent [9](https://codewithsql.com/guides/sql-server-express) |
| **التحقق قبل P01** | قياس اتصال `Microsoft.Data.SqlClient 6.1.3` بالإصدار المختار على Windows CI/بيئة laboratory، وتحديث أدلة القبول | افتراض غير مُثبت حاليًا |

**مرفوض صراحةً:** اعتبار ESU بديلًا عن الترقية؛ تثبيت 2016 SP1 على سيرفر إنتاج جديد؛ رفع مستوى التوافق تلقائيًا أثناء الترقية؛ أو تغيير `sql/01`/`static_checks.py` خارج نطاق P03 المُخطط.

### 2.4 ما صُحِّح في الخارطة نتيجة ذلك

أُضيف `TN-18` و`TN-19` و`RISK-26`، ووُسِّع `OD-ARC-09` ليشمل الإصدار/الطبعة ومسار رفع المستوى، وصُحِّحت مخططات C.2/J وE.1/E.2/E.6 وP03/P19 وL/N/P/R والملحق، مع إبقاء كل إشارة لـ 2016 SP1 موسومة بـ «حالة قديمة خارج الدعم» لا «منصة مستهدفة». كذلك صُحِّح تعميم «تعارض `READ_COMMITTED_SNAPSHOT` مع Express» إلى «أثره على `tempdb` والذاكرة» [11](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17)، وصُحِّح تعميم منع `STRING_AGG`/`TRIM`/JSON إلى صياغة دقيقة مبنية على توثيق كل دالة [12](https://learn.microsoft.com/en-us/sql/t-sql/functions/string-agg-transact-sql?view=sql-server-ver17).

---

## 3. إعادة فحص القرارات المفتوحة (البند 3)

### 3.1 النتيجة الإجمالية

| المؤشر | القيمة |
|---|---|
| القرارات الواردة في الخارطة قبل المراجعة | **37** معرّفًا فريدًا |
| معرّفات مُشار إليها بلا تعريف | **0** |
| معرّفات معرّفة بلا أي ارتباط خارج جدول H.3 قبل المراجعة | **2** (`OD-DEP-02`, `OD-DEP-04`) ⇒ صُحِّح بربطهما بـ P19 وO.4 |
| فجوة موضوعية مكتشفة | **ربحية الصيانة** غير مغطاة بأي قرار ⇒ أُضيف `OD-FIN-24` |
| القرارات بعد المراجعة | **38**، كلها معرّفة ومرتبطة، **بلا أي معادلة مالية مُبتدعة** |
| فجوات الترقيم | `OD-ARC-02/03/05/06/07` غير مستخدمة؛ لا توجد إشارة إليها، فلا تُغيّر حفاظًا على استقرار التتبع |

### 3.2 الجدول الشامل للقرارات الـ 38

| ID | الموضوع | المالك | المرحلة | الارتباط بالمتطلبات | نتيجة إعادة الفحص |
|---|---|---|---|---|---|
| `OD-FIN-01` | تعريف الإيراد ووقت الاعتراف | صاحب عمل + محاسب | P13, P14 | `R-FIN-01`, `R-M01-01` | مفتوح صحيح، بلا معادلة |
| `OD-FIN-02` | الربح وطريقة التكلفة | محاسب | P13, P14, M13 | `R-FIN-02`, `R-M03-15`, `R-M13-19` | مفتوح صحيح؛ يمتد أثره إلى `OD-FIN-24` |
| `OD-FIN-03` | صافي نتيجة اليوم والفترة | محاسب | P14 | `R-FIN-03`, `R-M01-07` | مفتوح صحيح |
| `OD-FIN-04` | تصنيف المصروفات ومعاملة المشتريات | محاسب | P13 | `R-FIN-04`, `R-M01-04` | مفتوح صحيح |
| `OD-FIN-05` | توزيعات الدفع والمتبقي المسموح والدفع المختلط | صاحب عمل | P08, P13 | `R-FIN-09`, `R-M02-21…26`, `R-M05-05…10` | مفتوح صحيح؛ لم تُبتدع قاعدة مطابقة |
| `OD-FIN-06` | إنشاء دين العميل والسداد الجزئي | صاحب عمل | P08, P13 | `R-FIN-05`, `R-M02-36`, `R-M08-06…11` | مفتوح صحيح |
| `OD-FIN-07` | ديون الموردين والسداد | صاحب عمل | P07, P13 | `R-FIN-06`, `R-M05-05…14` | مفتوح صحيح |
| `OD-FIN-08` | تفاعل خصم الفاتورة مع أسعار الأصناف | صاحب عمل | P08 | `R-M02-27…32` | مفتوح صحيح |
| `OD-FIN-09` | أرصدة الصندوق النقدي وتعدد الصناديق | محاسب | P13 | `R-FIN-07`, `R-M01-14` | مفتوح صحيح |
| `OD-FIN-10` | أرصدة المحافظ وعلاقتها بالتحويلات الخارجية | محاسب | P11, P13 | `R-FIN-08`, `R-M01-15…19`, `R-M09-*` | مفتوح صحيح |
| `OD-FIN-11` | أثر تعديل/إلغاء فاتورة الشراء | محاسب | P07, P09 | `R-M05-14`, `R-FIN-06` | مفتوح صحيح |
| `OD-FIN-12` | حساب الحدود، العكس، العمليات الفاشلة | صاحب عمل | P11 | `R-M09-09` | مفتوح صحيح |
| `OD-FIN-13` | قيمة المردودات وعلاقتها بالخصم الأصلي | محاسب | P10 | `R-FIN-10`, `R-M07-*` | مفتوح صحيح |
| `OD-FIN-14` | مسودات POS ومدة بقائها | صاحب عمل | P08 | `R-M02-04` | مفتوح صحيح |
| `OD-FIN-15` | عمولات التحويلات واحتسابها في M01 | محاسب | P11, P14 | `R-FIN-11` | مفتوح صحيح |
| `OD-FIN-16` | فروق المبادلات ووسيلة تسويتها | صاحب عمل | P10 | `R-FIN-12`, `R-M07-*` | مفتوح صحيح |
| `OD-FIN-17` | البيع بـ IMEI فردي أم كمية | صاحب عمل | P03, P05, P08 | `R-M03-06`, `OD-ARC-11` | مفتوح صحيح |
| `OD-FIN-18` | المعالجة المحاسبية للهالك | محاسب | P06, P13 | `R-FIN-13`, `R-M04-*` | مفتوح صحيح |
| `OD-FIN-19` | الإقفال اليومي/الشهري وقفل الفترات | محاسب | P13, P14 | `R-FIN-14` | مفتوح صحيح |
| `OD-FIN-20` | الأرصدة الافتتاحية وترحيل البيانات | محاسب | P03, P13 | `R-FIN-15`, `R-M01-18` | مفتوح صحيح |
| `OD-FIN-21` | التقريب والعملة والضريبة إن طُبّقت | محاسب | P13, P15, P16 | `R-FIN-16` | مفتوح صحيح |
| `OD-FIN-22` | الاعتراف بدخل الصيانة عند التسليم أم الدفع | محاسب | P12, P14 | `R-FIN-17`, `R-M10-*` | مفتوح صحيح؛ **لا يغطي الربحية** ⇒ أُضيف `OD-FIN-24` |
| `OD-FIN-23` | سياسة عكس القيود وتصحيح الأخطاء | محاسب | P09, P10, P13 | `R-FIN-18` | مفتوح صحيح |
| `OD-FIN-24` | **ربحية الصيانة**: تحميل تكلفة القطع وأجرة العمل وعمولة الفني وإعادة العمل والهالك، وإدراجها في أرباح M01/M13 | محاسب + صاحب عمل | P12, P13, P14 | `R-M10-07`, `R-M13-19`, `OD-FIN-02`, `OD-FIN-22` | **قرار جديد لسد فجوة**؛ لا قاعدة مُبتدعة |
| `OD-ARC-01` | منفذ HTTPS وأسماء الأجهزة/الشهادات | مسؤول شبكة | P01, P17 | `R-ARC-*` | مفتوح صحيح |
| `OD-ARC-04` | EF Core أم البقاء على `Microsoft.Data.SqlClient` | مهندس معماري | P01, P03 | `R-DB-*`, `R-ARC-*` | مفتوح صحيح؛ التوصية الأولية البقاء على SqlClient |
| `OD-ARC-08` | تفعيل `READ_COMMITTED_SNAPSHOT` | DBA | P03, P17 | `R-DB-*`, `R-XBR-*` | **صُحِّح**: مدعوم على مستوى القاعدة، يُقيّم أثره على `tempdb` لا «تعارضه مع Express» |
| `OD-ARC-09` | إصدار/طبعة SQL Server المدعومة، الإبقاء على 130، رفع تدريجي إلى 160/170 | صاحب عمل + DBA | P00, P01, P03, P17, P19 | `R-DEP-*`, `R-DB-*` | **وُسِّع** وفق القسم 2 |
| `OD-ARC-10` | تعدد المخازن أم مخزن واحد | صاحب عمل | P03, P06 | `R-DB-12` | مفتوح صحيح |
| `OD-ARC-11` | تفعيل تتبع IMEI ومدى إلزاميته | صاحب عمل | P03, P05, P12 | `R-DB-11` | مفتوح صحيح |
| `OD-SEC-01` | شهادة CA داخلية وسياسة تدويرها | مسؤول شبكة | P04, P17 | `R-SEC-*` | مفتوح صحيح |
| `OD-SEC-02` | ترقية صيغة hash تدريجيًا أم الإبقاء | مهندس أمن | P04 | `R-SEC-*` | مفتوح صحيح؛ لا rehash إجباري |
| `OD-SEC-03` | شكل الجلسة المركزية ومدة الصلاحية | مهندس أمن | P04 | `R-SEC-*`, `R-ARC-*` | مفتوح صحيح |
| `OD-DEP-01` | نمط التثبيت MSI / MSIX / ClickOnce | مسؤول نشر | P19 | `R-DEP-*` | مفتوح صحيح |
| `OD-DEP-02` | آلية التحديث والرجوع للإصدار السابق | مسؤول نشر | P19 | `R-DEP-*` | **أُضيف الربط** في P19 بند 4 وO.4 |
| `OD-DEP-03` | RPO/RTO وجدول FULL/DIFF/LOG | صاحب عمل | P03, P19 | `R-DEP-*` | مفتوح صحيح |
| `OD-DEP-04` | تثبيت SQL Server 2022/2025 ضمن الحزمة أم مسبقًا | مسؤول نشر | P19 | `R-DEP-*` | **صُحِّح** اسم الإصدار + **أُضيف الربط** |
| `OD-DEP-05` | سياسة الاحتفاظ والأرشفة | محاسب + DBA | P03, P19 | `R-DEP-*` | مفتوح صحيح |

### 3.3 فحص البنود الثمانية التي نصّ الطلب على التركيز عليها

| البند محل الفحص | القرارات | المتطلبات الداعمة | هل توجد معادلة مُبتدعة؟ | ملاحظة التصحيح |
|---|---|---|---|---|
| القواعد المالية العامة | `OD-FIN-01…04`, `19…21`, `23` | `R-FIN-01…04`, `14…16`, `18`, `R-M01-01…08` | **لا** | بقيت «تعريف + مالك + مرحلة» فقط؛ مؤشرات M01 Placeholder حتى التوقيع |
| الدفع المختلط | `OD-FIN-05`, `09`, `10` | `R-M02-21…26` (500/300/200)، `R-M05-05…10` (10000/3000/2000/1000/4000)، `R-FIN-09` | **لا** | الأمثلة مُسجّلة كسيناريوهات اختبار وبيانات، لا كقاعدة توزيع نهائية |
| ديون العملاء | `OD-FIN-06`, `OD-FIN-13` | `R-FIN-05`, `R-M02-36`, `R-M08-06…11` | **لا** | السداد الجزئي وأثر المرتجع على الدين مُرحّلان للقرار |
| ديون الموردين | `OD-FIN-07`, `11` | `R-FIN-06`, `R-M05-05…14` | **لا** | أثر تعديل/إلغاء فاتورة الشراء على الرصيد والمخزون مفتوح |
| أرصدة المحافظ | `OD-FIN-10`, `OD-FIN-15` | `R-M01-14…19`, `R-M09-*`, `R-FIN-08` | **لا** | الحسابات النموذجية الأربعة مُعامَلة كبيانات أولية لا ثوابت؛ السجل الداخلي ليس إثبات تحويل خارجي |
| المرتجعات | `OD-FIN-13`, `18`, `23` | `R-M07-09…17`, `R-FIN-10` | **لا** | سليم ⇒ مخزون قابل للبيع، تالف ⇒ مخزون تالف؛ القيمة والعكس مفتوحان |
| المبادلات | `OD-FIN-16`, `23` | `R-M07-*`, `R-FIN-12` | **لا** | جهة الفرق ووسيلته وتسجيله مفتوحة |
| **ربحية الصيانة** | `OD-FIN-22` ثم **`OD-FIN-24`** | `R-M10-07` (أجرة العمل/تكلفة القطع/سعر البيع/الفني)، `R-M13-19` (تقرير ربحية المنتج) | **لا** | **كانت فجوة**: لا أي قرار يربط تكاليف الإصلاح بالربح؛ أُضيف `OD-FIN-24` وربط بـ P12/P13/P14 وQ.4 |

---

## 4. التحقق من المراحل العشرين والمتطلبات الـ 419 (البند 4)

### 4.1 قياسات آلية أُجريت على الملف بعد التصحيح

| الفحص | النتيجة |
|---|---|
| الأقسام | `A B C D E F G H I J K L M N O P Q R` كاملة + B.13 جديد + ملحق |
| المراحل | **20** (`P00`…`P19`)، ولكل مرحلة **15 بندًا** بالترتيب 1…15 بلا نقص أو تكرار |
| جرد المتطلبات في D | **419** سطرًا `- [ ] **R-XXX-NN**` بمعرّفات فريدة |
| صفوف مصفوفة Q | **419** صفًا للمعرّفات نفسها؛ `set(D) == set(Q)` ⇒ **True** |
| المراحل المستخدمة في Q | `P00`…`P19` كلها موجودة، بلا مرحلة يتيمة |
| التوزيع | `DB:52, M13:42, M02:40, M01:29, M12:21, FIN:19, M04:18, SEC:18, M07:17, ARC:16, M14:16, M03:15, M05:14, M09:14, M10:14, XBR:13, M08:11, M11:11, M06:10, DEP:10, TST:10, UI:9` = 419 |
| التقارير | 41 سطرًا بكلمة «تقرير» داخل `R-M13-*` + بند قواعد رسمية = 42 |
| السمات | 8 تعريفات مستقلة (`السمة 1:` … `السمة 8:`) + جدولا الصفات |
| مخططات Mermaid | 16 مخططًا، وعدد fences زوجي (38) ⇒ متوازنة |
| اتساق جداول Markdown | **0** جدول بعدد أعمدة غير متسق |
| القرارات | 38 معرّفًا، **0** مُشار إليه بلا تعريف، **0** معرّف بلا ارتباط |
| المخاطر | **26** بندًا (`RISK-01…26`) |
| شوائب لغوية | صفر حروف CJK، وصُحِّحت `bcompat`, `RPO/RPO`, فاصلة منقوطة بعرض كامل |

### 4.2 سلامة الربط بعد التصحيح

- كل متطلب في D له صف في Q بالأعمدة السبعة الإلزامية: `Requirement ID → Feature → Phase → DB → Backend → Screens → Acceptance Test`.
- إضافة `OD-FIN-24` **لم تُضف متطلبًا جديدًا**، بل ربطت متطلبين قائمين (`R-M10-07`, `R-M13-19`) بقرار مفتوح؛ لذلك بقي الإجمالي 419.
- التصحيحات غيّرت بيانات تدقيق وأسماء إصدارات ومعايير قبول، ولم تحذف أو تدمج أي متطلب أو مرحلة أو بند من بنود الـ 15.
- معايير القبول في P00 وP01 وP03 وP19 أصبحت قابلة للقياس على إصدار SQL Server مدعوم بدل إصدار خارج الدعم.

---

## 5. الحفاظ على نظام الدخول العامل (البند 5)

| العنصر | الدليل من المصدر عند `HEAD` | الحكم |
|---|---|---|
| `LoginViewModel.LoginCommand` | `ViewModels/LoginViewModel.cs` — قراءة كلمة المرور ومسحها، تحقق الأطوال، استدعاء `SignInAsync`، رسائل خطأ عربية | موجود |
| `AuthenticationService.SignInAsync` | `Services/AuthenticationService.cs` — `DummyHash` + `PasswordHasher.Verify` + `IsActive` | موجود، بلا تغيير |
| `UserRepository.FindAsync` | `Data/UserRepository.cs` — `SELECT ... WHERE Username = @Username` بـ `SqlParameter` و`NVARCHAR(100)` | موجود، مُعامل |
| `PasswordHasher` | `Services/PasswordHasher.cs` — `PBKDF2-SHA256$600000$salt$hash`، `FixedTimeEquals`، دعم legacy | موجود، بلا rehash |
| `ConnectionSettingsProvider` | `Services/ConnectionSettingsProvider.cs` — `Default`/`Machines` وتوافق رجعي للملف المسطّح | **مُوسَّع في PR #34** لكنه لا يغيّر سلسلة المصادقة |
| دورة النوافذ | `App.xaml` (`OnLastWindowClose`) + `App.xaml.cs` | مُصلحة في PR #33 ومحفوظة |
| الخلاصة | الخارطة B.7.2 تُبقي `PasswordHasher` و`dbo.Users` و`LoginWindow` واستعلام `FindAsync` كما هي، وتغيّر «موفر البيانات» فقط تدريجيًا | **لا استبدال، لا إعادة تصميم، لا rehash إجباري** |

> **تنبيه دقة:** «يعمل فعليًا» في هذا التقرير يعني أن منطق السلسلة موجود ومتماسك في المصدر وبناء Windows CI ناجح تاريخيًا؛ **لا يعني** أنه مُختبَر على Windows/SQL في هذه الجلسة، لأن `docs/ACCEPTANCE.md` يسجّل 21 اختبارًا بحالة «لم يُختبر».

---

## 6. التصحيحات المطبَّقة على الخارطة (البند 6)

### 6.1 ملخص التصحيحات (87 تعديلًا موجّهًا، كلها على `docs/MOBILE_SHOP_DEVELOPMENT_ROADMAP.md`)

| # | التصحيح | التبرير التقني |
|---|---|---|
| 1 | ترويسة المستند: الفرع `arena/3f5c2194-projectz` و`HEAD cfd470c` مع الإبقاء على `8e964a9` كأساس تاريخي | `git rev-parse HEAD` و`git branch --show-current` |
| 2 | A.4/B.1/B.3/P.1: عدد الملفات 44 بدل 40، وتحديث شجرة B.3 بأسماء ملفات PR #34 | `git ls-tree -r --name-only HEAD` مع عدّ الأسطر |
| 3 | B.11 + `TN-17` + `RISK-22`: PR #34/#35 مدمجان فعلًا ولا انحراف مع `main` | `git merge-base --is-ancestor` ⇒ YES لكلا الـ commitين |
| 4 | `TN-01`: تحويل «قد تُودَع أسرار» إلى «كلمة مرور نص صريح مؤكدة في Git»؛ `RISK-22` يعاد توجيهه لهذا الخطر | `src/FitohPhone/connectionsettings.json` |
| 5 | B.2/B.5/B.8.2/B.9: تحديث إعدادات الاتصال إلى `Default`/`Machines` و`192.168.1.46,1433` وSQL Authentication، ومشروعَي الاختبار، و21 اختبار قبول | قراءة الملفات و`docs/ACCEPTANCE.md` |
| 6 | إضافة **B.13** «إعادة التحقق على HEAD: حقائق مقابل افتراضات» | تمييز صريح بين الحقائق والافتراضات والقرارات المفتوحة (البند 7) |
| 7 | A.5 + `TN-18` + `RISK-26` + E.1/E.2/E.6 + C.2/J + P00/P01/P03/P19 + L/N/P/R + O.4 + الملحق: استبدال اعتمادية 2016 SP1 بـ 2022/2025 مع المستوى 130 | نهاية الدعم الموسّع، ودعم المستوى 130 على المحركات الأحدث [1](https://learn.microsoft.com/en-us/lifecycle/products/sql-server-2016) [5](https://learn.microsoft.com/en-us/intune/configmgr/core/plan-design/configs/support-for-sql-server-versions) |
| 8 | `TN-19`: بقاء أدلة القبول على SP1 و21 اختبارًا غير مُنفّذ | `docs/ACCEPTANCE.md`, `docs/SERVER.md` |
| 9 | `OD-ARC-08` وP03 بند 11: تصحيح «تعارض RCSI مع Express» إلى «أثره على `tempdb`» | خاصية على مستوى القاعدة [11](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17) |
| 10 | E.2/E.6: تصحيح تعميم منع `STRING_AGG`/`TRIM`/JSON إلى صياغة مبنية على توثيق كل دالة | [12](https://learn.microsoft.com/en-us/sql/t-sql/functions/string-agg-transact-sql?view=sql-server-ver17) |
| 11 | إضافة `OD-FIN-24` لربحية الصيانة، وتوسيع `OD-ARC-09`، وتصحيح `OD-DEP-04`، وربط `OD-DEP-02/04` | فجوة موضوعية + تغيّر الإصدار + قرارات بلا ارتباط |
| 12 | R.2/R.3/R.5/Q.4/الملحق: تحديث العدد إلى 38 قرارًا و26 خطرًا، وإضافة سطر تتبع لربحية الصيانة | اتساق الأرقام مع المحتوى |
| 13 | تصحيح `bcompat` ← «توافق رجعي»، `RPO/RPO` ← `RPO/RTO`، فاصلة بعرض كامل | تدقيق لغوي |

### 6.2 ما **لم** يُعدَّل وما **لم** يُنفَّذ

| البند | الحالة |
|---|---|
| ملفات `src/**`, `sql/**`, `tests/**`, `tools/**`, workflow, publish profiles, `README.md`, `docs/SERVER.md`, `docs/ACCEPTANCE.md`, `docs/CLIENTS.md`, `docs/LAN-SETUP.md` | **لم تُلمس**؛ `git status` يُظهر مستند الخارطة المُعدَّل وهذا التقرير الجديد فقط |
| كود تطبيقي / DDL / DML / migrations / شاشات Placeholder | **لم يُكتب** |
| استبدال WPF أو .NET أو SqlClient أو `PasswordHasher` أو `dbo.Users` | **لم يُقترح**؛ التغيير المقترح إضافة طبقة API تدريجيًا فقط |
| المعادلات المالية والقواعد غير المحسومة | **لم تُبتدع**؛ بقيت 38 قرارًا مفتوحًا |
| المخطط المقترح في القسم E | بقي تصميمًا على الورق؛ لم يُنشأ جدول ولم يُشغّل سكربت |

---

## 7. تمييز الحقائق عن الافتراضات والقرارات غير المحسومة (البند 7)

> وُضعت العلامات التالية صراحةً في الخارطة (B.13) وفي هذا التقرير، ولا يُخلط بينها:

| العلامة | المعنى | أمثلة |
|---|---|---|
| **حقيقة مُتحقَّق منها** | نتيجة أمر أو ملف مُسمّى أو مصدر رسمي موثّق | 44 ملفًا؛ `cfd470c`؛ PBKDF2 600000؛ كلمة مرور نص صريح؛ المستوى 130؛ نهاية دعم 2016 في 2026-07-14 |
| **افتراض غير مُثبت** | يُذكر كافتراض ويُحسم في P00 بقياس أو تأكيد | بيئة الإنتاج الفعلية؛ عنوان السيرفر الحقيقي؛ توافق تعريف SqlClient مع 2025؛ أن المستودع المقصود هو ProjectZ؛ أن الدخول مُختبَر تشغيليًا |
| **قرار مفتوح `OD-*`** | قاعدة تجارية/معمارية تحتاج توقيع صاحب صلاحية؛ لا يجوز تحويلها إلى حكم في التحقق | 38 قرارًا، أهمها `OD-FIN-01…24` و`OD-ARC-09` |
| **حقيقة خارجية مؤثقة بمصدر** | معلومة من Microsoft Lifecycle/توثيق المنتج تدخل في التصحيح ولا تُعدّ «رأيًا» | تواريخ الدعم [1](https://learn.microsoft.com/en-us/lifecycle/products/sql-server-2016)، المستويات المدعومة [5](https://learn.microsoft.com/en-us/intune/configmgr/core/plan-design/configs/support-for-sql-server-versions)، حدود Express [9](https://codewithsql.com/guides/sql-server-express) |
| **خطر `RISK-*` / دين `TN-*`** | تقييم مبني على حقيقة، لا حقيقة بذاته | `TN-18`, `RISK-26` |

---

## 8. قائمة التحقق النهائية (أُجريت قبل حفظ هذا التقرير)

- [x] إعادة قراءة الطلب الأصلي ومقارنة بنوده السبعة واحدًا واحدًا
- [x] التحقق من `pwd` والفرع والـ commit وعدد الملفات وسلفية PR #34/#35
- [x] البحث عن `MobileShop`/`Flutter`/`Dart`/`API` في الشجرة و30 فرعًا ومستودعات المالك وبحث كود GitHub
- [x] توثيق نهاية دعم SQL Server 2016 SP1 بمصادر رسمية، واقتراح إصدار مدعوم مع الحفاظ على المستوى 130
- [x] إعادة فحص كل قرار مفتوح (37 ⇒ 38) مع جدول مالك/مرحلة/ارتباط، وفحص البنود الثمانية الحرجة
- [x] إعادة قياس 20 مرحلة × 15 بندًا، و419 متطلبًا، و419 صف مصفوفة، واتساق الجداول والمخططات
- [x] التأكد من بقاء سلسلة الدخول في المصدر ومن عدم وجود مقترح استبدال لها
- [x] تطبيق 87 تصحيحًا موجّهًا على الخارطة only مع التحقق من وجود كل نص قبل الاستبدال
- [x] إعادة تشغيل فحوص الاتساق بعد التصحيح (صفر أخطاء)
- [x] التأكد من خلو النص من الشوائب اللغوية ومن المعادلات المالية المُبتدعة
- [x] `python3 tools/static_checks.py` كقراءة فقط، وتسجيل أن اختبارات التشغيل `NOT RUN`
- [x] التأكد من أن `git status` لا يحتوي أي ملف مصدر معدَّل
- [x] **لم يبدأ أي تنفيذ، ولم يُنشأ أي كود أو جدول أو سكربت**

---

## 9. قرار الاعتماد: Phase 00 / Phase 01

### 9.1 المعوّقات التي تحول دون اعتماد التنفيذ الآن

| # | المعوّق | يؤثر على | لماذا يُعدّ مانعًا |
|---|---|---|---|
| 1 | **تأكيد هوية المشروع المقصود**: لم يُعثر على حل `MobileShop`/API/Flutter، والمستودع الحالي هو `FitohPhone` WPF | P00 وP01 | الطلب صريح: «لا تفترض أنهما نفس المشروع»؛ إن كان المقصود مستودعًا آخر فيجب تزويد الفريق برابطه قبل أي تخطيط تنفيذي |
| 2 | **اختيار إصدار/طبعة SQL Server المدعومة** (`OD-ARC-09` المُوسَّع) | P01 (وعمليًا P00) | المعمارية، حساب الخدمة، TLS/CA، النسخ، برنامج التثبيت، وتعريف SqlClient تتوقف على هذا القرار؛ لا يجوز البناء على 2016 SP1 |
| 3 | **تدوير السر النصي وإزالته من Git** (`TN-01`, `RISK-22`) | P01 | P01 ينص على نقل الأسرار للسيرفر؛ وجود سر مُودَع يُخالف هدف المرحلة قبل بدئها |
| 4 | **اعتماد القواعد المالية** (`OD-FIN-01…24`) | P13 فما بعده (ويُفضّل بدء ورشتها في P00) | ليست مانعًا تقنيًا لـ P01 لكنها تُبقي التقارير/اللوحة غير قابلة للتنفيذ؛ تأخيرها يضرب المسار الحرج |
| 5 | **بيئة Windows + SQL Server فعلي وجهازان** (`TN-19`, `RISK-23`) | P00/P01/P17/P18 | 21 اختبارًا «لم يُختبر»؛ معايير قبول P01 تتضمن اختبار جهازين و`/health/ready`، ولا تتحقق هنا |
| 6 | **الأدلة الحالية تستهدف SP1** | P00 | `docs/SERVER.md` و`docs/ACCEPTANCE.md` تحتاج تحديثًا بعد القرار 2؛ لا يُعدّلان في هذه المراجعة احترامًا لشرط عدم تعديل الملفات القائمة |

### 9.2 البيان الصريح

> **القرار: يلزم توضيح إضافي (Further clarification is required). الخارطة _غير معتمدة_ للتنفيذ في P00 أو P01 كحزمة واحدة في هذه اللحظة.**
>
> - **P00 — Phase 00 (Audit):** تُعتمد **مشروطًا** كمرحلة **قراءة وتدقيق وتوثيق فقط بلا أي كتابة كود**، بعد أن يؤكد صاحب القرار أن المستودع المقصود هو `kamal-solaiman/ProjectZ` (`FitohPhone`) لا مشروع MobileShop/API/Flutter آخر، وبعد تسمية إصدار SQL Server المدعوم المطلوب اعتماده. عند استيفاء الشرطين تصبح P00 جاهزة للانطلاق.
> - **P01 — Phase 01 (Architecture & Technical Foundation):** **غير معتمدة للبدء** حتى تُغلق المعوّقات 1 و2 و3 وتُهيَّأ بيئة Windows/SQL فعلية (المعوّق 5). بدء P01 قبل ذلك سيُثبّت معمارية على اعتمادية قاعدة بيانات خارج الدعم أو على سر مُسرَّب، وسيخالف معايير القبول نفسها الواردة في الخارطة.
> - لا يُبدأ أي تنفيذ ضمن هذه المراجعة، ولا يُكتب كود، ولا تُعدّل ملفات المصدر، ولا تُغيّر قاعدة البيانات.

**بعبارة مختصرة:** «التحقق اكتمل والتصحيحات طُبِّقت، لكن **التوضيح مطلوب قبل الاعتماد**؛ P00 مشروطة، وP01 موقوفة.»

---

## الملحق أ — أوامر إعادة إنتاج التحقق

```bash
cd /home/user/ProjectZ
git branch --show-current && git rev-parse HEAD && git status -sb
git ls-tree -r --name-only HEAD | wc -l
git merge-base --is-ancestor 35f6043af385ef0f4c75576bd827f86a165a410f HEAD && echo PR34_ANCESTOR=YES
git merge-base --is-ancestor 8e964a9ff953fee794e911e7748d64740f3fc612 HEAD && echo PR33_ANCESTOR=YES
git fetch --quiet origin '+refs/heads/*:refs/remotes/audit/*'
for r in $(git for-each-ref --format='%(refname:short)' refs/remotes/audit); do
  git ls-tree -r --name-only "$r" | grep -icE 'mobileshop|flutter|\.dart$|pubspec|Api\.csproj|Controllers/|Startup\.cs' || true
done
git grep -in -E 'MobileShop|Flutter|ASP\.NET|WebApi' HEAD -- . ':!docs/MOBILE_SHOP_DEVELOPMENT_ROADMAP.md'
gh repo list kamal-solaiman --limit 50 --json name,primaryLanguage
gh search repos MobileShop --owner kamal-solaiman --json name,url
gh api -X GET search/code -f q='Flutter repo:kamal-solaiman/ProjectZ' --jq '.total_count'
python3 tools/static_checks.py
```

## الملحق ب — المصادر الخارجية المستخدمة في إعادة التقييم

| # | المصدر | الاستخدام |
|---|---|---|
| [1](https://learn.microsoft.com/en-us/lifecycle/products/sql-server-2016) | Microsoft Lifecycle — SQL Server 2016 | نهاية الدعم الأساسي/الموسّع، تاريخ انتهاء SP1 (2019-07-10)، ESU |
| [2](https://learn.microsoft.com/en-us/sql/sql-server/end-of-support/sql-server-end-of-support-overview?view=sql-server-ver17) | Microsoft — SQL Server end-of-support options | سنوات دعم 2022/2025/2016 |
| [3](https://learn.microsoft.com/en-us/sql/sql-server/end-of-support/extended-security-updates-frequently-asked-questions?view=sql-server-ver17) | Microsoft — ESU FAQ | تاريخ 2026-07-14 ونهاية ESU 2029-07-17 |
| [4](https://learn.microsoft.com/en-us/lifecycle/faq/sql-server-extended-security-updates) | Microsoft Lifecycle — ESU FAQ | ESU مدفوع ومؤقت ويقتصر على Critical |
| [5](https://learn.microsoft.com/en-us/intune/configmgr/core/plan-design/configs/support-for-sql-server-versions) | Microsoft — Supported SQL Server versions | المستويات المدعومة 160…110 على 2022 و170…110 على 2025 |
| [6](https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql?view=sql-server-ver17) | Microsoft — ALTER DATABASE | صياغة `COMPATIBILITY_LEVEL` من 100 إلى 170 |
| [7](https://www.codefirst.co.uk/end-of-life/sql-server-2016/) | CodeFirst — SQL Server 2016 EOL | احتفاظ القاعدة المنقولة بمستوى توافقها، والرفع كخطوة منفصلة |
| [8](https://learn.microsoft.com/en-us/sql/relational-databases/databases/view-or-change-the-compatibility-level-of-a-database?view=sql-server-ver17) | Microsoft — View/change compatibility level | المستوى 160 لـ 2022 وآلية التغيير |
| [9](https://codewithsql.com/guides/sql-server-express) | SQL Server Express limits | 10GB مقابل 50GB، 1410MB buffer pool، غياب SQL Agent |
| [10](https://codewithsql.com/compare/sql-server-express-vs-standard) | Express مقابل Standard | حدود الذاكرة/المعالج والنسخ |
| [11](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17) | Microsoft — SET TRANSACTION ISOLATION LEVEL | `READ_COMMITTED_SNAPSHOT` خيار قاعدة بيانات بـ row versioning |
| [12](https://learn.microsoft.com/en-us/sql/t-sql/functions/string-agg-transact-sql?view=sql-server-ver17) | Microsoft — STRING_AGG | الدالة متاحة بأي مستوى توافق على محرك 2017+ |

> كل معلومة من مصادر خارجية مُشار إليها برقمها في المتن؛ ما عداها فمصدره أوامر `git`/`gh` وملفات المستودع المُسمّاة، وهو **حقيقة محلية مُتحقَّق منها**.
