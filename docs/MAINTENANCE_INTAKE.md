# شاشة أمر صيانة (استلام الأجهزة) — الفحص الشامل والربط الكامل بقاعدة البيانات

`src/ProjectZ/Views/MaintenanceIntakeView.xaml` + `ViewModels/MaintenanceIntakeViewModel.cs`.
هذه الشاشة كانت **الأكثر بقاءً على «وضع التصميم»**: جزء من الكتابة كان يصل إلى SQL Server بالفعل
(`CreateMaintenanceOrder`، وعربون الاستلام، وسجل أول نشاط)، لكن الهيدر ظل يقول
«تصميم الواجهة — بدون Backend»، وكان رقم الأمر القادم مُخمَّنًا من الذاكرة، وكان العميل الجديد
يُضاف بمعرّف وهمي لا وجود له في `dbo.Customers`، وكانت الطباعة تعيد كتابة الأمر بالكامل.
هذا المستند يسجّل الفحص بندًا ببند، ثم ما يُقرأ ويُكتب في القاعدة، ثم العيوب الحقيقية التي أُصلحت.

## 1) تفكيك الشاشة بندًا ببند (Audit)

الشاشة `UserControl` واحدة بثلاثة صفوف: هيدر، مساحة عمل بعمودين (قائمة الاستلامات الأخيرة 365px
+ نموذج الاستلام)، وشريط حالة سفلي.

| الصف | العنصر | السلوك | الأمر / الخاصية |
| --- | --- | --- | --- |
| 0 | «معاينة آخر إيصال (MNT-…)» | يفتح معاينة إيصال آخر أمر محفوظ ويُسجّل نشاط طباعة | `LastSavedOrderButtonLabel` / `HasLastSavedOrder` / `PreviewLastSavedCommand` |
| 0 | «تفريغ النموذج» | يرجع كل الحقول إلى قيمها الابتدائية ويجلب رقم الأمر القادم من جديد | `ResetFormCommand` |
| 0 | «تحديث» (جديد) | يعيد تحميل أوامر الصيانة ودليل العملاء من SQL ويحدّث ختم المزامنة في الشريط السفلي | `RefreshFromDatabaseCommand` |
| 0 | عنوان الشاشة | «أمر صيانة — استلام أجهزة الصيانة» | نص ثابت |
| 0 | شارة رقم الأمر القادم | `MNT-YYYY-NNNN` مقروءًا من `dbo.DocumentSequences`، مع لاحقة «(تقريبي)» عند تعذّر القراءة | `NextOrderNumberDisplay` |
| 0 | شارة حالة البيانات | «متصل بقاعدة البيانات — SQL Server» (أخضر) أو «وضع عرض — بدون قاعدة بيانات» (أحمر) | `DataConnectionBadgeText` / `IsDatabaseBacked` |
| 2 | ختم المزامنة | «آخر مزامنة: yyyy-MM-dd HH:mm:ss» أو «لم تتم المزامنة بعد» في أقصى يمين الشريط السفلي | `LastSyncedDisplay` |
| 1 يسار | عنوان القائمة | «أحدث الأجهزة المستلمة للصيانة» | نص ثابت |
| 1 يسار | 3 بطاقات مصغّرة | «مستلم اليوم»، «قيد العمل»، «عربون اليوم» محسوبة من الأوامر المحمّلة | `TodayCountDisplay` / `ActiveOrdersCountDisplay` / `TodayDepositsDisplay` |
| 1 يسار | شرائح التصفية | «الكل» / «اليوم» / «مستعجل» | `SetRecentFilterCommand` + `IsRecentAllFilter` / `IsRecentTodayFilter` / `IsRecentUrgentFilter` |
| 1 يسار | البحث | رقم الأمر، العميل، الهاتف، الموديل، الماركة، السيريال، العطل | `RecentSearchText` |
| 1 يسار | بطاقة الأمر | الرقم + وقت الاستلام + شارة الأولوية (مستعجل/ضمان) + الجهاز + العميل والهاتف + العطل + ملخص العربون + الحالة | `FilteredRecentOrders` + خصائص `MaintenanceOrderDesignItem` |
| 1 يسار | أزرار البطاقة | «إيصال» (معاينة وطباعة)، «نفس العميل» (بدء استلام جديد لعميل الأمر) | `PreviewOrderReceiptCommand` / `CopyCustomerForNewOrderCommand` |
| 1 يسار | حالة الفراغ | «لا توجد أوامر صيانة مطابقة للبحث الحالي.» | `HasNoFilteredRecentOrders` |
| 1 يمين §1 | العميل | بحث + منتقي عملاء + بطاقة العميل المختار + «تغيير» + «+ عميل جديد» + بانر «لم يُعثر على عميل» | `CustomerSearchText` / `ToggleCustomerPickerCommand` / `FilteredCustomers` / `SelectCustomerCommand` / `ClearCustomerSelectionCommand` / `OpenAddCustomerWindowCommand` / `HasNoFilteredCustomers` |
| 1 يمين §1 | هاتف التواصل * | حقل رقمي إلزامي، افتراضيًا من العميل المختار | `ContactPhone` |
| 1 يمين §2 | نوع الجهاز | شرائح: «هاتف ذكي» وغيرها | `DeviceTypeOptions` / `SelectDeviceTypeCommand` / `DeviceType` |
| 1 يمين §2 | الماركة / الموديل * / اللون / السيريال | الماركة قائمة قابلة للكتابة، الموديل إلزامي ويستقبل التركيز عند الخطأ | `DeviceBrands` / `DeviceModel` / `DeviceColor` / `SerialOrImei` / `FocusModelInputRequested` |
| 1 يمين §2 | قفل الشاشة | «بدون» / «رمز أو كلمة مرور» / «نمط» + حقل الرمز + تلميح الحماية | `SetPasscodeNone/Pin/PatternCommand` / `NeedsPasscodeInput` / `PasscodeInputLabel` / `PasscodeInputHint` / `PasscodeValue` / `PasscodeProtectionHint` |
| 1 يمين §3 | الأعطال الشائعة | شرائح تُضاف إلى وصف العطل دون تكرار | `CommonFaults` / `AppendCommonFaultCommand` / `ReportedFault` |
| 1 يمين §3 | الحالة الظاهرية | قائمة مزدوجة + ملاحظة حرة + معاينة نصية | `ConditionChecklist` / `ToggleCommand` / `CustomConditionNote` / `ConditionSummaryPreview` |
| 1 يمين §3 | الملحقات | قائمة اختيار + معاينة نصية | `AccessoryChecklist` / `AccessoriesSummaryPreview` |
| 1 يمين §3 | ملاحظات الاستلام | حقل حر | `IntakeNotes` |
| 1 يمين §4 | الأولوية | عادي / مستعجل / إعادة فحص ضمان | `SetNormal/Urgent/WarrantyPriorityCommand` / `Priority` |
| 1 يمين §4 | الفني + موعد التسليم | قائمة فنيين + `DatePicker` + شرائح المواعيد (اليوم مساءً / غدًا / خلال 48 ساعة) | `Technicians` / `AssignedTechnician` / `ExpectedDeliveryDate` / `SelectDeliverySlotCommand` |
| 1 يمين §4 | التكلفة المبدئية | «يُحدد بعد الفحص» أو مبلغ رقمي + المتبقي المتوقع + العربون المحصل | `IsEstimatePendingInspection` / `EstimatedCostText` / `EstimatedRemainingDisplay` / `TotalDepositPaidDisplay` |
| 1 يمين §4 | العربون | بدون / كاش / فودافون كاش / إنستاباي / مقسّم + مبالغ كل قناة | `Set*DepositModeCommand` / `DepositCashText` / `DepositVodafoneText` / `DepositInstaPayText` |
| 1 يمين | أزرار الحفظ | «حفظ أمر الصيانة وفتح استلام جديد» و«حفظ ومعاينة طباعة الإيصال والملصق» | `SaveOrderCommand` / `SaveAndPrintCommand` |
| 2 | شريط الحالة | رسالة نجاح (أخضر) أو خطأ (أحمر) مع اسم الأمر الحقيقي | `StatusMessage` / `IsErrorStatus` / `HasStatusMessage` |

## 2) ما يُقرأ من قاعدة البيانات

| البيانات | المصدر | ملاحظات |
| --- | --- | --- |
| أوامر الصيانة (القائمة، البطاقات، البطاقات المصغّرة) | `LoadMaintenanceOrdersInto` → `dbo.MaintenanceOrders` + `Users` | الترتيب `ReceivedAtUtc DESC, Id DESC`؛ البطاقات محسوبة من نفس الصفوف |
| دليل العملاء | `SalesRepository.LoadCustomersInto` → `dbo.Customers` | نفس المجموعة المشتركة مع المبيعات وشاشة العملاء، وتُعاد قراءتها من زر «تحديث من القاعدة» |
| رقم الأمر القادم | `PeekNextOrderNumber` → `dbo.DocumentSequences` | `ISNULL(LastValue,0)+1` للسنة الحالية، دون استهلاك الرقم |
| تكرار السيريال | `HasOpenOrderWithSerial` → `dbo.MaintenanceOrders` | يمنع استلام نفس الجهاز مرتين بينما أمره السابق غير مسلَّم/ملغى |
| تكرار هاتف العميل | `SalesRepository.PhoneExists` → `dbo.Customers` | نفس منطق `UX_Customers_Phone` قبل فتح «حفظ» في نافذة العميل |
| رمز الجهاز | `PasscodeValueProtected` | يُفك تشفيره عند القراءة (انظر §5) |

## 3) ما يُكتب في قاعدة البيانات

| الإجراء | العملية | معاملة |
| --- | --- | --- |
| «حفظ أمر الصيانة» | `INSERT dbo.MaintenanceOrders` (الحالة `Received`) + سطر `dbo.MaintenanceActivities` «استلام جهاز صيانة» + عربون `dbo.MaintenancePayments` (Stage=1، Kind=1) لكل قناة + حركة `dbo.FinancialTransactions` + تحديث أرصدة `dbo.FinancialAccounts` عبر `EnsureAccounts` | `CreateMaintenanceOrder` — معاملة واحدة، والرقم من `NextDocumentNumber` |
| «+ عميل جديد» | `INSERT dbo.Customers` ثم إعادة تحميل الدليل المشترك | `SalesRepository.CreateCustomer` |
| معاينة/طباعة إيصال | `INSERT dbo.MaintenanceActivities` فقط | `AppendMaintenanceActivity` (جديد) |
| «تحديث من القاعدة» | قراءة فقط (أوامر + عملاء + رقم قادم) | لا كتابة |

## 4) العيوب الحقيقية التي كانت تمنع العمل على SQL — وأُصلحت

1. **العميل الجديد كان يُضاف بمعرّف وهمي.** `RequestOpenAddCustomer` كان يحسب
   `Max(Id)+1` في الذاكرة ويضيف الصف إلى المجموعة المشتركة دون أي `INSERT`. عند الحفظ يُرسل
   `CustomerId` غير موجود في `dbo.Customers`، فيفشل الأمر بـ`FK_MaintenanceOrders_Customer` (547)
   أو يُربط العميل بصف خاطئ. الآن يُكتب العميل في القاعدة أولًا (`createCustomer`)، ثم يُستدعى
   نفس الصف المحفوظ من الدليل المشترك؛ ولا يُستخدم معرّف مُصنَّع في وضع SQL إطلاقًا.
2. **رقم الأمر القادم كان مخترعًا.** كان `MNT-2026-{MaxId+1}` مع سنة مثبتة على 2026، وقد يختلف
   تمامًا عن الرقم الذي سيمنحه `dbo.DocumentSequences` (بعد مسح/دمج/تغيير سنة). الآن الشارة تقرأ
   `LastValue+1` للسنة الحالية، وتُرجع «(تقريبي)» عند تعذّر القراءة فقط.
3. **الطباعة كانت تعيد كتابة الأمر بالكامل.** زر المعاينة نادى `UpdateMaintenanceOrder(order, [entry], [])`
   الذي يُحدّث كل أعمدة الأمر ويعيد مزامنة بنود قطع الغيار (`DELETE`+`INSERT`) لمجرد تسجيل طباعة.
   أُضيف `AppendMaintenanceActivity` الذي يُدخل سطر السجل فقط.
4. **شارة «تصميم الواجهة — بدون Backend»** كانت تنفي الربط عن الشاشة رغم أنها تكتب في SQL منذ
   البداية. صارت الشارة ديناميكية (`IsDatabaseBacked`) مع وقت آخر مزامنة وزر تحديث.
5. **لا تحقق من أطوال الأعمدة.** كان إدخال نص أطول من عرض العمود (موديل 150، عطل 2000، ملاحظات 1500،
   لون 80، فني 150، موعد 200، رمز 100) يصل إلى SQL فيرجع الخطأ 8152 برسالة غير مفهومة.
   الآن `ValidateFieldLengths` يفحص كل حقل قبل البناء برسالة عربية تحدّد الحقل وحدّه.
6. **لا تحقق من منطقية الهاتف.** كان أي نص يُقبل في «هاتف التواصل». الآن يلزم 7 أرقام على الأقل
   وبحد أقصى 30 حرفًا (عرض العمود).
7. **استلام نفس الجهاز مرتين.** لم يكن هناك أي فحص للسيريال/IMEI، فيمكن فتح أمرين مفتوحين لنفس
   الجهاز. الآن يُرفض الحفظ ويُذكر أن هناك أمرًا مفتوحًا بنفس السيريال (المسلَّم والملغى مستثنيان).
8. **رمز الجهاز كان يُحفظ نصًا صريحًا** في عمود اسمه `PasscodeValueProtected`، خلافًا لما يفرضه
   `docs/DATABASE.md`. الآن يُشفَّر AES-GCM بمفتاح خارج القاعدة (§5)، ومع غياب المفتاح تظهر تحذيرًا
   واضحًا في النموذج بدل الادّعاء بأنه محمي.

## 5) حماية رمز الجهاز

- الخوارزمية: `AES-GCM` (مفتاح 256 بت، nonce عشوائي 12 بايت، tag 16 بايت)، والصيغة المخزّنة
  `enc:v1:` + `base64(nonce|tag|cipher)` في `PasscodeValueProtected` (`NVARCHAR(1000)` تكفي).
- مصدر المفتاح: متغيّر البيئة `PROJECTZ_PASSCODE_KEY` (أولوية) أو `PasscodeKey` في
  `connectionsettings.json`، بصيغة base64 لـ32 بايت. المفتاح **لا** يُخزَّن في قاعدة البيانات.
- بلا مفتاح: يُحفظ الرمز كنص عادي (سلوك النسخ السابقة) ويظهر في النموذج:
  «تنبيه: مفتاح تشفير الرموز غير مُهيّأ…». وبمفتاح مختلف على جهاز آخر: تُعرض قيمة بديلة واضحة
  («تعذّر فك تشفير رمز الجهاز») بدل نص مشوّه، ولا يتعطّل الإيصال أو نافذة التفاصيل.
- الصفوف القديمة غير المشفّرة تبقى مقروءة كما هي (لا ترحيل إجباري).

## 6) ملف SQL الجديد

`sql/14_EnableMaintenanceIntake.sql` (idempotent، لا يحذف بيانات):

- فهرس مفلتر `IX_MaintenanceOrders_Serial_Open` على `SerialOrImei` مع `INCLUDE (OrderStatus, ReceivedAtUtc)`
  وشرط `SerialOrImei <> N''` لخدمة فحص تكرار الجهاز دون تكبير الجدول بلا داعٍ.
- تحقّق أن `PasscodeValueProtected` بعرض 1000 حرف (يكفي الحمولة المشفّرة).
- إعادة تأكيد صلاحيات `ProjectZClient` التي تحتاجها الشاشة (Customers، MaintenanceOrders،
  MaintenanceActivities، MaintenancePayments، DocumentSequences، FinancialAccounts،
  FinancialTransactions، Users).
- استعلامات تحقّق نهائية: وجود الفهرس، عرض عمود الرمز، وعدد سنوات تسلسل `MaintenanceOrder`.
  لا يُنشأ فهرس مكرر لـ`IX_MaintenanceOrders_Status_Received` أو `IX_MaintenancePayments_Order_Date`
  الموجودين في `sql/06`.

## 7) حدود معروفة

- التصفية والبحث في قائمة «أحدث الأجهزة» يجريان في الذاكرة على الأوامر المحمّلة (نمط باقي الشاشات)،
  والسجل يُحمَّل كاملًا بلا صفحات.
- شارة رقم الأمر القادم تقديرية بطبيعتها: قد يتغيّر الرقم إذا سجّل عميل آخر أمرًا بين القراءة والحفظ؛
  الرقم النهائي دائمًا هو ما يعيده `NextDocumentNumber` داخل معاملة الحفظ ويُعرض في رسالة النجاح.
- فحص تكرار السيريال يمنع الحفظ فقط عندما يكون الأمر السابق غير مسلَّم وغير ملغى؛ ولذلك يكفي تسليم
  أو إلغاء الأمر السابق (أو تعديل السيريال) للمتابعة.
- تعديل «هاتف التواصل» لا يغيّر هاتف العميل في `dbo.Customers`؛ يُحفظ في لقطة الأمر
  (`CustomerPhoneSnapshot`) فقط، احترامًا لكون الدليل مشتركًا مع المبيعات.
- العربية فقط في الواجهة، والطباعة معاينة (لا طابعة حرارية).
- تشفير الرمز يعتمد على مفتاح واحد لكل التثبيت؛ من يفقد المفتاح يفقد إمكانية قراءة الرموز المشفّرة
  (تبقى الصفوف القديمة غير المشفّرة مقروءة).

## 8) خطوات القبول على Windows + SQL Server 2016 SP1+

1. نفّذ `sql/06` ثم `sql/09` ثم `sql/13` ثم `sql/14` بحساب إداري، ثم `sql/03_VerifyEnvironment.sql`
   (المستوى 130).
2. افتح «أمر صيانة»: الشارة خضراء «متصل بقاعدة البيانات — SQL Server»، ورقم الأمر القادم يطابق:
   ```sql
   SELECT DocumentType, SequenceYear, LastValue FROM dbo.DocumentSequences WHERE DocumentType = N'MaintenanceOrder';
   ```
3. «+ عميل جديد» برقم جديد: يجب أن يظهر صف في `dbo.Customers` ويُختار تلقائيًا، ثم حفظ أمر له ينجح
   (`CustomerId` ليس `NULL`).
4. عرّف مفتاحًا (`setx PROJECTZ_PASSCODE_KEY <base64-32-bytes>` ثم أعد تشغيل البرنامج)، سجّل أمرًا
   برمز جهاز، ثم:
   ```sql
   SELECT OrderNumber, PasscodeType, LEFT(PasscodeValueProtected, 7) AS Prefix FROM dbo.MaintenanceOrders ORDER BY Id DESC;
   ```
   يجب أن يكون `Prefix = enc:v1:` وأن يظهر الرمز صحيحًا في معاينة الإيصال ونافذة التفاصيل.
5. سجّل أمرًا بعربون مقسّم (كاش + فودافون): تحقّق من `dbo.MaintenancePayments` و`dbo.FinancialTransactions`
   وأرصدة `dbo.FinancialAccounts`.
6. أعد تسجيل نفس السيريال بينما الأمر الأول مفتوح: يجب أن يرفض الحفظ برسالة عربية، ثم ينجح بعد
   «تسليم» أو «إلغاء» الأمر الأول.
7. جرّب إدخال موديل أطول من 150 حرفًا: رسالة عربية تحدّد الحقل بدل خطأ SQL 8152.
8. اطبع الإيصال من البطاقة وتحقق من سطر واحد جديد فقط في `dbo.MaintenanceActivities` دون تغيّر
   `UpdatedAtUtc` للأمر أو بنود القطع.
9. أوقف SQL Server ثم اضغط «حفظ»: رسالة خطأ عربية بلا كشف تفاصيل الاتصال، واضغط «تحديث من القاعدة»
   لتظهر الرسالة نفسها، ثم أعد الخادم وحدّث فتظهر البيانات.

## 9) الملفات المتأثرة

- `src/ProjectZ/Views/MaintenanceIntakeView.xaml` — شارة البيانات الحقيقية، زر «تحديث»،
  ختم المزامنة في الشريط السفلي، تلميح حماية رمز الجهاز.
- `src/ProjectZ/Views/MaintenanceIntakeView.xaml.cs` — نافذة العميل الجديد بوضع SQL (نصوص + فحص تكرار).
- `src/ProjectZ/ViewModels/MaintenanceIntakeViewModel.cs` — حالة الاتصال، رقم الأمر من القاعدة،
  المزامنة اليدوية، حفظ العميل في SQL، تحقق الأطوال/الهاتف/السيريال، سجل النشاط الخفيف.
- `src/ProjectZ/Models/ConnectionSettings.cs` و`Services/ConnectionSettingsProvider.cs` — `PasscodeKey`
  وقارئ المفتاح من البيئة أو الملف.
- `src/ProjectZ/Services/PasscodeProtection.cs` (جديد) — AES-GCM لحماية رمز الجهاز.
- `src/ProjectZ/Data/MaintenanceRepository.cs` — `PeekNextOrderNumber`، `HasOpenOrderWithSerial`،
  `AppendMaintenanceActivity`، تشفير/فك تشفير الرمز.
- `src/ProjectZ/Data/SalesRepository.cs` — `PhoneExists`.
- `src/ProjectZ/ViewModels/MainViewModel.cs` — ربط الشاشة بإنشاء العميل وفحص الهاتف وإعادة تحميل الدليل.
- `sql/14_EnableMaintenanceIntake.sql` (جديد) و`docs/DATABASE.md` و`docs/ACCEPTANCE.md`.
