# شاشة فواتير الصيانة — الفحص الشامل والربط الكامل بقاعدة البيانات

`src/ProjectZ/Views/MaintenanceInvoicesView.xaml` لم تبقَ شاشة تصميم: صارت تقرأ كل فاتورة من
SQL Server وتعرض قطع الغيار والمدفوعات والسجل الزمني لكل أمر، وتكتب التحصيل والتسليم وسجل
الطباعة داخل معاملة واحدة عبر `src/ProjectZ/Data/MaintenanceRepository.cs`. مسار التصميم
(`repository is null`) ما زال يعمل ببيانات نموذجية حتى يمكن فتح التطبيق بلا قاعدة بيانات.

## 1) تفكيك الشاشة بندًا ببند (Audit)

الشاشة `UserControl` واحدة بـ 5 صفوف، وكل صف معروض أدناه مع ربطه البرمجي.

| الصف | العنصر | السلوك | الأمر/الخاصية |
| --- | --- | --- | --- |
| 0 | شريط العنوان | «فواتير الصيانة — السجل الشامل والتحليلات والفلاتر» | نص ثابت |
| 0 | شارة الحالة | «متصل بقاعدة البيانات — SQL Server» عند وجود `MaintenanceRepository`، و«معاينة تصميم — بدون قاعدة بيانات» بدونه | `BackendStatusText` / `IsDatabaseConnected` / `BackendStatusDetail` (ToolTip) |
| 0 | «+ أمر صيانة جديد» | ينتقل إلى شاشة `امر صيانة` | `GoToIntakeCommand` |
| 0 | «تحديث» | يعيد قراءة الفواتير والفنيين من SQL ويحدّث الشارة الزمنية والتحليلات | `RefreshDataCommand` → `NotifyExternalDataChanged()` |
| 0 | «إعادة ضبط الفلاتر» | يعيد كل فلتر إلى قيمته الابتدائية | `ClearFiltersCommand` |
| 1 | سطر ملخص السجل | سطر مضغوط (لا بطاقات KPI، احترامًا لإزالة البطاقات السابقة): عدد المعروض من الإجمالي، نشطة، مسلّمة، ملغاة، بانتظار تحصيل، ثم محصّل/متبقي/تكلفة قطع/أرباح تقديرية، ووقت آخر تحميل | `RegisterCountSummaryText`, `RegisterMoneySummaryText`, `LastRefreshDisplay` |
| 2 | أساس التاريخ | «تاريخ الاستلام» أو «تاريخ التسليم» (يستبعد ما لم يُسلَّم) | `SelectedDateBasis` / `IsReceivedDateBasis` / `IsDeliveryDateBasis` / `SelectDateBasisCommand` |
| 2 | فترات جاهزة | الكل، اليوم، آخر 7 أيام، هذا الشهر، فترة مخصصة | `SelectedDatePreset` + `SelectDatePresetCommand` |
| 2 | فترة مخصصة | منتقيا تاريخ من/إلى يظهران عند اختيار «فترة مخصصة» فقط | `CustomFromDate` / `CustomToDate` / `IsCustomDatePreset` |
| 2 | البحث الشامل | رقم الفاتورة، العميل، الهاتف، الجهاز، السيريال/IMEI، العطل، التشخيص، الفني، اسم/باركود قطعة الغيار | `SearchText` + `ClearSearchCommand` |
| 2 | 6 قوائم تصفية | الحالة (9 قيم)، الأولوية (3)، الفني (من SQL)، طريقة الدفع (6)، التسوية المالية (5)، الترتيب (6) | `SelectedStatusFilter`, `SelectedPriorityFilter`, `SelectedTechnicianFilter`, `SelectedPaymentFilter`, `SelectedSettlementFilter`, `SelectedSortOption` |
| 3 | سجل الفواتير | 10 أعمدة: الرقم + شارات مستعجل/ضمان، الاستلام/التسليم/المتوقع، العميل والهاتف، الجهاز والعطل، الفني، الإجمالي/القطع والمصنعية، المحصّل/المتبقي/المرتجع، وسيلة الدفع، شارة الحالة، أزرار التحكم | `FilteredOrders` + خصائص `MaintenanceOrderDesignItem` |
| 3 | أزرار الصف | «تفاصيل» (نافذة كاملة)، «طباعة» (معاينة إيصال)، «تسليم» (نافذة تسليم وتحصيل، تُعطَّل لغير النشط)، «إجراءات» (شاشة إجراءات الصيانة) | `OpenInvoiceDetailsCommand`, `OpenPrintPreviewCommand`, `OpenDeliverDialogCommand`, `OpenInMaintenanceActionsCommand` |
| 3 | حالات الفراغ | «قاعدة البيانات متصلة ولا توجد أوامر بعد» مع زر إنشاء أمر، أو «لا توجد فواتير مطابقة» مع زر إعادة الضبط | `IsDatabaseEmpty` / `ShowNoMatchEmptyState` |
| 4 | شريط الحالة | رسالة نجاح (أخضر) أو خطأ (أحمر) لكل قراءة/كتابة | `StatusMessage` / `IsErrorStatus` / `HasStatusMessage` |

النافذة التفصيلية `MaintenanceInvoiceDetailsWindow` (تُفتح من «تفاصيل») تعرض: بيانات الجهاز
والعميل، تقرير الفحص، جدول القطع، **جدول حركات التحصيل والاسترداد** (جديد)، السجل الزمني،
وأزرار الطباعة والتسليم والتحصيل (كاش/فودافون/إنستاباي) وفتح الشاشة في «إجراءات الصيانة».

## 2) ما يُقرأ من قاعدة البيانات

`MaintenanceRepository.LoadMaintenanceOrdersInto` صار يقرأ بأربعة استعلامات مجموعة واحدة فقط
بدل 3 استعلامات لكل أمر:

| البيانات | الاستعلام | ملاحظات |
| --- | --- | --- |
| رؤوس الأوامر + اسم المستلم | `dbo.MaintenanceOrders` `LEFT JOIN dbo.Users` | `ReceivedByName` كان يبقى فارغًا في الواجهة، الآن يأتي من `Users.DisplayName` |
| بنود قطع الغيار | `dbo.MaintenanceOrderParts` مرة واحدة لكل الأوامر | `SparePartId` و`ProductId` و`IsManualPart` كما هي في القاعدة |
| السجل الزمني + اسم المنفّذ | `dbo.MaintenanceActivities` `LEFT JOIN dbo.Users` | `ActorName` كان فارغًا، الآن اسم المستخدم الحقيقي |
| حركات المدفوعات | `dbo.MaintenancePayments` `JOIN dbo.FinancialAccounts` `LEFT JOIN dbo.Users` | تُجمَّع في `Deposit*`/`FinalPayment*`/`RefundedDepositAmount` وتُعرض سطرًا سطرًا في `PaymentHistory` |
| قائمة الفنيين في الفلتر | `DISTINCT AssignedTechnician` من الأوامر + `DisplayName` للمستخدمين النشطين | `LoadTechnicianDirectory()` |

الأرقام المالية نفسها محسوبة من النموذج (`CurrentEffectiveCost`، `EstimatedRemaining`،
`EstimatedProfit`) اعتمادًا على الصفوف المقروءة، فما يظهر في الشاشة يطابق ما في القاعدة.

## 3) ما يُكتب في قاعدة البيانات

| الإجراء | العملية | معاملة |
| --- | --- | --- |
| تحصيل دفعة من النافذة التفصيلية | `dbo.MaintenancePayments` (Stage=1) + تحديث أرصدة `dbo.FinancialAccounts` + سطر `dbo.FinancialTransactions` | `UpdateMaintenanceOrder` — معاملة واحدة |
| تسليم وتحصيل (من الصف أو من النافذة) | تحديث حالة الأمر و`DeliveredAtUtc` والضمان وملاحظات التسليم + دفعات Stage=2 + استرداد فروق العربون (Kind=2) + سطر نشاط | نفس المعاملة |
| تسجيل طباعة الإيصال | سطر في `dbo.MaintenanceActivities` بمن قام بالطباعة ونوع الإيصال | نفس المعاملة |
| إعادة ضبط بنود القطع | `DELETE` ثم `INSERT` لبنود الأمر في `dbo.MaintenanceOrderParts` مع `SparePartId` الصحيح و`ProductId = NULL` لبنود قطع الغيار | نفس المعاملة |

كل كتابة تُنشئ `FinancialTransactions` مرتبطًا بـ`SourceEntityType = N'MaintenanceOrder'`، فتُسوّى
مدفوعات الصيانة مع شاشات المالية.

## 4) أخطاء حقيقية كانت تمنع «العمل على SQL» وتم إصلاحها

1. **بنود قطع الغيار كانت تُكتب في عمود خاطئ.** كانت تُحفظ `SparePartId = ProductId = معرّف قطعة الصيانة`،
   و`ProductId` مفتاح خارجي إلى `dbo.Products`، فإما يفشل الحفظ أو يُربط البند بمنتج غير صحيح.
   أُضيف `MaintenancePartUsageItem.SparePartId` و`EffectiveSparePartId`، ويُكتب الآن
   `SparePartId` للقطعة و`ProductId = NULL`، مع قراءة القيمة من العمود الصحيح.
2. **قيد `CK_MaintenanceOrderParts_Source` كان يرفض صرف قطعة غيار** (`ProductId` مطلوب لكل بند غير يدوي)
   فيفشل `DeductSparePartStock` بالخطأ 547. صار القيد يقبل بند قطعة غيار أو بند منتج أو بند يدوي،
   ويُطبَّق الإصلاح على القواعد القائمة عبر `sql/13_EnableMaintenanceInvoices.sql`.
3. **شارة «تصميم الواجهة — بدون Backend»** كانت تُظهر للمستخدم أن الشاشة بلا قاعدة بيانات رغم ربطها.
   صارت الشارة ديناميكية حسب وجود الاتصال.
4. **أسماء المستلم والمنفّذ كانت فارغة** بعد التحميل من SQL. الآن تأتي من `dbo.Users` عبر استعلامات JOIN.
5. **الأداء: N+1.** كان كل أمر يفتح 3 استعلامات (قطع/أنشطة/مدفوعات). الآن 4 استعلامات لكامل السجل،
   مع فهارس تغطية في `sql/13`.
6. **اختلاط الكتابات المعلّقة بين الفواتير.** كانت قائمة الأنشطة/الدفعات المعلّقة واحدة لكل الشاشة:
   لو فشل حفظ فاتورة ثم حُفظت أخرى، تُكتب أنشطة الأولى على الثانية. الآن كل مدخل مرتبط بمعرّف أمره
   ولا يُفرَّغ إلا عند حفظه هو.
7. **قائمة الفنيين كانت ثابتة من بيانات التصميم**، فقد يقترح الفلتر اسمًا غير موجود في القاعدة.
   الآن تُبنى من `dbo.MaintenanceOrders` و`dbo.Users`.
8. **رسائل الأخطاء لم تكن تفرّق بين مخطط ناقص وصلاحية مفقودة.** أُضيفت معالجة للخطأ 207 (عمود/جدول ناقص)
   وتوضيح تشغيل `sql/06` و`sql/13` بجانب الخطأ 229/230 للصلاحيات.

## 5) ملف SQL الجديد

`sql/13_EnableMaintenanceInvoices.sql` (idempotent، لا يحذف بيانات):

- إعادة بناء `CK_MaintenanceOrderParts_Source` ليسمح ببند قطعة غيار.
- فهارس تغطية للسجل والقراءة المجمّعة: `IX_MaintenanceOrders_ReceivedAtUtc`،
  `IX_MaintenanceOrders_DeliveredAtUtc` (مفلتر على غير الفارغ)، `IX_MaintenanceOrderParts_Order`،
  `IX_MaintenanceActivities_Order`. لا يُنشأ فهرس مكرر للفلاتر أو المدفوعات لأن
  `IX_MaintenanceOrders_Status_Received` و`IX_MaintenancePayments_Order_Date` من `sql/06` يغطيانها.
- إعادة تأكيد صلاحيات `ProjectZClient`: `SELECT` على `Users` و`FinancialAccounts`،
  `SELECT/INSERT/UPDATE` على `MaintenanceOrders`، `SELECT/INSERT/UPDATE/DELETE` على
  `MaintenanceOrderParts`، `SELECT/INSERT` على `MaintenancePayments` و`MaintenanceActivities`
  و`DocumentSequences` (المدفوعات والسجل تاريخ لا يُعدَّل).
- نهايته استعلامات تحقق من القيد والفهارس وأعداد الصفوف، ثم `PRINT` للنجاح أو `THROW`.

## 6) حدود معروفة

- التصفية والترتيب يجريان في الذاكرة على الأوامر المحمّلة (نمط باقي شاشات المشروع)، لا في SQL،
  ولا توجد صفحات (Paging)؛ السجل يُحمَّل كاملًا. عند كبر عدد الأوامر جدًا يلزم نقل التصفية إلى SQL.
- إجمالي المرتجعات يُعرض كرقم واحد (`RefundedDepositAmount`) كما كان، بينما يظهر كل استرداد سطرًا
  في `PaymentHistory`.
- حفظ بنود القطع يعيد كتابة بنود الأمر بالكامل (`DELETE`+`INSERT`) ولا يضبط `RowVersion`،
  فإن عدّل مستخدمان الأمر نفسه في اللحظة ذاتها تُحفظ آخر كتابة.
- الطباعة معاينة فقط (لا طابعة حرارية)، والتصدير إلى Excel غير موجود.
- عند فشل الحفظ تبقى الواجهة محدَّثة «تفاؤليًا» (السجل والدفعة معروضة) مع رسالة خطأ حمراء،
  والصفوف المعلّقة تبقى محفوظة لإعادة المحاولة؛ اضغط «تحديث» لإعادة المزامنة من القاعدة.
- الشاشة لا تُلغي الأمر بنفسها: الإلغاء يبقى في «إجراءات الصيانة» عبر زر «إجراءات».

## 7) خطوات القبول على Windows + SQL Server 2016 SP1+

1. نفّذ `sql/06_CreateFullSchema.sql` ثم `sql/09_EnablePosSales.sql` ثم `sql/13_EnableMaintenanceInvoices.sql`
   بحساب إداري، ثم `sql/03_VerifyEnvironment.sql` (يجب أن يظهر المستوى 130).
2. سجّل أول أمر صيانة من شاشة «امر صيانة» بعربون، ثم افتح «فواتير الصيانة»:
   الشارة «متصل بقاعدة البيانات — SQL Server»، والفاتورة تظهر بالرقم `MNT-YYYY-NNNN` واسم المستلم الحقيقي.
3. قارن الأرقام مع SQL:
   ```sql
   SELECT OrderNumber, OrderStatus, ReceivedAtUtc, DeliveredAtUtc FROM dbo.MaintenanceOrders ORDER BY Id DESC;
   SELECT MaintenanceOrderId, PaymentStage, PaymentKind, Amount FROM dbo.MaintenancePayments ORDER BY Id DESC;
   SELECT MaintenanceOrderId, ActionTitle, OccurredAtUtc FROM dbo.MaintenanceActivities ORDER BY Id DESC;
   ```
4. «تحصيل» دفعة كاش من «تفاصيل»: يجب أن يظهر سطر جديد في `PaymentHistory` وفي `dbo.MaintenancePayments`
   مع رصيد `dbo.FinancialAccounts` وسطر `dbo.FinancialTransactions`.
5. «تسليم» من الصف: تُحدَّث الحالة إلى «تم التسليم» مع الضمان و`DeliveredAtUtc`، وتظهر الدفعات
   النهائية في `MaintenancePayments` (Stage=2)، ويُسجَّل نشاط التسليم.
6. اطبع إيصالًا ثم تحقق من سطر `طباعة:` في `MaintenanceActivities`.
7. اصرف قطعة غيار على الأمر من «إجراءات الصيانة» ثم افتح «فواتير الصيانة»: يظهر بند القطعة،
   وتحقّق أن `dbo.MaintenanceOrderParts.SparePartId` مملوء و`ProductId` فارغ:
   ```sql
   SELECT MaintenanceOrderId, SparePartId, ProductId, IsManualPart FROM dbo.MaintenanceOrderParts ORDER BY Id DESC;
   ```
8. جرّب الفلاتر والترتيب والبحث ومقارنة النتائج مع استعلامات SQL؛ يجب أن يتطابق سطر الملخص
   (العدد، النشطة، المسلّمة، المحصّل، المتبقي) مع مجموع الصفوف المعروضة.
9. أوقف الخادم ثم اضغط «تحديث»: رسالة خطأ عربية واضحة دون كشف تفاصيل الاتصال، والبيانات السابقة تبقى معروضة.

## 8) الملفات المتأثرة

- `src/ProjectZ/Views/MaintenanceInvoicesView.xaml` — شارة الحالة الديناميكية، سطر ملخص السجل، حالتا الفراغ.
- `src/ProjectZ/ViewModels/MaintenanceInvoicesViewModel.cs` — حالة الاتصال، سطر الملخص، الفنيون من SQL،
  ربط الأنشطة/الدفعات بمعرّف الأمر، وسجل المدفوعات.
- `src/ProjectZ/Data/MaintenanceRepository.cs` — قراءة مجمّعة، أسماء المستخدمين، `LoadTechnicianDirectory`،
  إصلاح كتابة بنود قطع الغيار، رسائل مخطط/صلاحيات أوضح.
- `src/ProjectZ/Models/MaintenanceDesignModels.cs` — `SparePartId`/`EffectiveSparePartId` و
  `MaintenancePaymentHistoryItem` و`PaymentHistory`.
- `src/ProjectZ/ViewModels/MaintenanceInvoiceDetailsViewModel.cs` و`Views/MaintenanceInvoiceDetailsWindow.xaml`
  — جدول حركات التحصيل والاسترداد وربط الدفعات الجديدة.
- `src/ProjectZ/ViewModels/MaintenanceActionsViewModel.cs` — صرف/إرجاع القطعة بمعرّف قطعة الغيار الصحيح.
- `sql/06_CreateFullSchema.sql` و`sql/13_EnableMaintenanceInvoices.sql`.
