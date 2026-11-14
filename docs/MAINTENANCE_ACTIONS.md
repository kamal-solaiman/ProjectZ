# شاشة إجراءات الصيانة — الفحص الشامل والربط الكامل بقاعدة البيانات

`src/ProjectZ/Views/MaintenanceActionsView.xaml` + `ViewModels/MaintenanceActionsViewModel.cs`
+ `ViewModels/MaintenanceActionsDialogViewModels.cs` (نافذتا التسليم والإلغاء).

الشاشة كانت **مربوطة بقاعدة البيانات جزئيًا**: نقل الحالة، تقرير الفحص، التسعير، صرف/إرجاع قطع
الغيار، التحصيل الإضافي، التسليم والإلغاء كانت كلها تُكتب فعلًا عبر
`MaintenanceRepository.UpdateMaintenanceOrder` داخل معاملة واحدة. لكن كان فيها عيوبًا حقيقية تمنع
العمل الآمن على SQL Server في بيئة شبكة (أكثر من عميل): لا كشف تعارض، قائمة فنيين ثابتة من بيانات
التصميم، اختيار التذكرة يقفز بعد كل حفظ، أخطاء التحميل تُبتلع بلا رسالة، والطباعة كانت تعيد كتابة
التذكرة بالكامل. هذا المستند يسجّل الفحص بندًا ببند ثم ما أُصلح.

## 1) تفكيك الشاشة بندًا ببند (Audit)

`UserControl` بثلاثة صفوف: هيدر، مساحة عمل بعمودين (قائمة التذاكر 390px + لوحة تحكم الورشة)،
وشريط حالة سفلي.

| الصف | العنصر | السلوك | الأمر / الخاصية |
| --- | --- | --- | --- |
| 0 | «+ استلام جهاز جديد (أمر صيانة)» | ينتقل إلى شاشة الاستلام | `GoToIntakeCommand` |
| 0 | «تحديث القائمة» | يعيد تحميل التذاكر ومخزون قطع الغيار وقائمة الفنيين من SQL | `RefreshDataCommand` → `NotifyExternalDataChanged()` |
| 0 | عنوان الشاشة | «إجراءات الصيانة — متابعة وتحكم كامل بالورشة» | نص ثابت |
| 0 | شارات الهيدر | نشطة، مستعجل، متبقي متوقع، **وحالة البيانات (جديد)** | `ActiveOrdersCount` / `UrgentActiveCount` / `ExpectedCollectionDisplay` / `DataConnectionBadgeText` + `IsDatabaseBacked` |
| 1 يسار | البحث | رقم التذكرة، العميل، الهاتف، الجهاز، IMEI، العطل | `SearchText` |
| 1 يسار | 3 فلاتر | المرحلة (9 قيم)، الأولوية، الفني (من SQL) | `StageFilterOptions` / `SelectedStageFilter` / `SelectedPriorityFilter` / `TechnicianFilterOptions` / `SelectedTechnicianFilter` |
| 1 يسار | بطاقة التذكرة | الرقم + شارة الحالة + شارة الأولوية + الجهاز + العميل والهاتف + الفني + التكلفة + «مستعجل» | `FilteredOrders` + خصائص `MaintenanceOrderDesignItem` |
| 1 يسار | حالة الفراغ | «لا توجد أجهزة مطابقة…» | `HasNoFilteredOrders` |
| 1 يمين | شريط الإجراءات | «التالي: …» (يحوّل للمرحلة التالية، ويفتح نافذة التسليم إن كانت التالية تسليمًا)، «إعادة فتح للمراجعة»، «إلغاء / لا يُصلح»، «طباعة الإيصال / الملصق / الضمان» | `AdvanceToNextStageCommand` / `ReopenOrderCommand` / `OpenCancelDialogCommand` / `OpenReceiptPreviewCommand` |
| 1 يمين | رأس التذكرة | الرقم، الحالة، الأولوية، الجهاز، العميل والهاتف، رمز الجهاز | خصائص `SelectedOrder` |
| 1 يمين | تحويل مباشر للحالة | 5 أزرار حالة + «تسليم العميل وتحصيل المتبقي» | `SetDirectStatusCommand` + `OpenDeliveryDialogCommand` |
| 1 يمين | بار الورشة | الأولوية (عادي/مستعجل/ضمان)، الفني (من SQL)، تاريخ التسليم المتوقع، «حفظ بيانات الورشة» | `SetEdit*PriorityCommand` / `Technicians` / `EditAssignedTechnician` / `EditExpectedDeliveryDate` / `SaveWorkshopUpdatesCommand` |
| 1 يمين | تبويب 0 | تفاصيل الاستلام الموثقة، إضافات سريعة للتشخيص، محرر تقرير الفحص، «حفظ تقرير الفحص» | `DiagnosisSnippets` / `AppendDiagnosisSnippetCommand` / `EditDiagnosisNotes` |
| 1 يمين | تبويب 1 | صرف قطع الغيار (من المخزون أو بند يدوي)، قائمة البنود مع «حذف / إرجاع»، إجمالي القطع، المصنعية، الخصم، «اعتماد التسعير»، 4 بطاقات (الإجمالي/المحصل/المتبقي/الربح) | `SwitchTo*PartModeCommand` / `AddCatalogPartDirectCommand` / `AddManualPartCommand` / `RemoveUsedPartCommand` / `SavePricingOnlyCommand` |
| 1 يمين | تبويب 2 | ملخص العربون والتحصيل النهائي، تحصيل دفعة إضافية (كاش/فودافون/إنستاباي)، الضمان وحالة التسليم/الإلغاء | `CollectExtraDepositCommand` / `SetExtraDeposit*Command` / خصائص `SelectedOrder` |
| 1 يمين | تبويب 3 | السجل الزمني الكامل مع المنفّذ والمبلغ | `SelectedOrder.ActivityLog` |
| 2 | شريط الحالة | رسالة نجاح/خطأ + **ختم آخر مزامنة (جديد)** | `StatusMessage` / `IsErrorStatus` / `HasStatusMessage` / `LastSyncedDisplay` |

نافذة التسليم (`MaintenanceDeliveryWindow`): تثبيت المصنعية والخصم، ملخص المتبقي بعد العربون،
اختيار قناة السداد والتحصيل، رد فرق العربون أو الاحتفاظ به، أيام الضمان (أو مخصصة)، ملاحظات
التسليم، ثم «تأكيد التسليم» أو «تأكيد وطباعة».

نافذة الإلغاء (`MaintenanceCancelOrderWindow`): سبب من قائمة أو سبب مخصص، إرجاع قطع الغيار
للمخزون، استرداد عربون بمبلغ محدود بصافي العربون، ثم تأكيد.

## 2) ما يُقرأ من قاعدة البيانات

| البيانات | المصدر | ملاحظات |
| --- | --- | --- |
| التذاكر (الرأس + RowVersion) | `LoadMaintenanceOrdersInto` → `dbo.MaintenanceOrders` + `Users` | الترتيب `ReceivedAtUtc DESC, Id DESC`؛ `RowVersion` جديد يُستخدم لكشف التعارض |
| بنود قطع الغيار | `dbo.MaintenanceOrderParts` (قراءة مجمّعة) | `SparePartId` للقطع و`ProductId` يبقى `NULL` |
| المدفوعات | `dbo.MaintenancePayments` + `FinancialAccounts` | تُعاد بناء `Deposit*`/`FinalPayment*`/`RefundedDepositAmount` |
| السجل الزمني | `dbo.MaintenanceActivities` + `Users` | اسم المنفّذ الحقيقي |
| قائمة الفنيين | `LoadTechnicianDirectory` → `DISTINCT AssignedTechnician` ∪ `dbo.Users.DisplayName` | تُستخدم في القائمة المنسدلة والفلتر (لم تكن كذلك) |
| مخزون قطع الغيار | شاشة «قطع غيار الصيانة» على نفس المجموعة المشتركة | «تحديث القائمة» يعيد تحميله الآن |

## 3) ما يُكتب في قاعدة البيانات

| الإجراء | العملية | معاملة |
| --- | --- | --- |
| حفظ بيانات الورشة / تقرير الفحص / التسعير | تحديث أعمدة الأمر (`OrderStatus`, `Priority`, `AssignedTechnician`, `ExpectedDelivery*`, `DiagnosisNotes`, `LaborCost`, `DiscountAmount`, `EstimatedCost`, `FinalServiceCost`, `HasWorkshopPricing`, `UpdatedAtUtc`) + سطر نشاط + مزامنة بنود القطع | `UpdateMaintenanceOrder` — معاملة واحدة |
| تحويل الحالة (زر التالي أو التحويل المباشر) | الحالة + سطر نشاط «تغيير الحالة» | نفسها |
| إعادة الفتح للمراجعة | الحالة «قيد الفحص» + تصفير `DeliveredAtUtc` و`CancellationReason` + نشاط | نفسها |
| صرف قطعة غيار | خصم `dbo.MaintenanceSpareParts.Quantity` بحارس `RowVersion` وإدراج بند في `MaintenanceOrderParts` | `DeductSparePartStock` |
| إرجاع قطعة غيار / حذف بند | إرجاع الكمية للمخزون ثم إعادة مزامنة البنود | `RestoreSparePartStock` + `UpdateMaintenanceOrder` |
| تحصيل دفعة إضافية | `MaintenancePayments` (Stage=1، Kind=1) + رصيد `FinancialAccounts` + `FinancialTransactions` + نشاط | `UpdateMaintenanceOrder` |
| التسليم | الحالة «تم التسليم»، `DeliveredAtUtc`، `WarrantyDays`، `DeliveryNotes`، `FinalServiceCost`، دفعات Stage=2، ورد فرق العربون (Stage=2، Kind=2) + نشاط | نفسها |
| الإلغاء | الحالة «ملغى»، `CancellationReason`، إرجاع القطع، استرداد عربون (Stage=1، Kind=2) + نشاط | نفسها |
| طباعة/معاينة | سطر نشاط واحد فقط بلا إعادة كتابة التذكرة | `AppendMaintenanceActivity` (جديد) |

كل دفعة تُنشئ سطر `FinancialTransactions` بـ`SourceEntityType = N'MaintenanceOrder'`، فتُسوّى
الصيانة مع شاشات المالية.

## 4) العيوب الحقيقية التي أُصلحت

1. **لا يوجد كشف تعارض بين عميلين (Lost update).** كان `UPDATE dbo.MaintenanceOrders ... WHERE Id = @Id`
   بلا أي حارس، فإذا فتح موظفان التذكرة نفسها كتب الثاني فوق تعديلات الأول بصمت، وقد يمحو
   عربونًا أو تسعيرًا أو تسليمًا. الآن يُقرأ `RowVersion` مع التذكرة، ويُرسل مع كل تحديث:
   ```sql
   UPDATE dbo.MaintenanceOrders SET ... OUTPUT inserted.RowVersion
   WHERE Id = @Id AND (@RowVersion IS NULL OR RowVersion = @RowVersion);
   ```
   فإن عاد صف فارغًا يُرفع خطأ عربي واضح («تم تعديل أمر الصيانة … من مستخدم آخر») ويُعاد تحميل
   التذكرة من القاعدة قبل عرض الرسالة، فلا تبقى الواجهة تعرض حالة غير محفوظة. `CreateMaintenanceOrder`
   يُرجع `RowVersion` الجديد أيضًا، فتبقى التذكرة قابلة للتعديل بعد الاستلام مباشرة.
2. **قائمة الفنيين كانت من بيانات التصميم** (`MaintenanceDesignSeedData.Technicians`) بينما
   `AssignedTechnician` عمود `NOT NULL`؛ فإن كان الفني المسجّل على التذكرة غير موجود في القائمة
   يفرّغه `ComboBox` إلى `NULL` وقد يفشل الحفظ. الآن القائمة تُبنى من SQL (فنيو الأوامر + مستخدمو
   النظام النشطون)، ويُضاف اسم التذكرة نفسها إليها دائمًا، والحفظ يمنع الكتابة الفارغة.
3. **الاختيار كان يقفز إلى أحدث تذكرة بعد كل حفظ.** `onOrdersSaved` يعيد تحميل كل شاشات الصيانة،
   والتحميل يستبدل كل عناصر المجموعة، فيفقد الاختيار مرجعه. الآن يُحفظ معرّف التذكرة ويُستعاد بعد
   التحميل، مع تعطيل التحديث المتكرر أثناء التحميل المجمّع (كان يُنفَّذ لكل صف يُضاف).
4. **أخطاء التحميل كانت تُبتلع بلا رسالة**، و«تحديث القائمة» يقول «تم التحديث» حتى لو فشل الاتصال،
   ولم يكن يعيد تحميل مخزون قطع الغيار رغم ذكر ذلك في نصه. الآن تفشل العملية برسالة عربية صريحة
   وتُظهر ختم آخر مزامنة ناجح فقط.
5. **الطباعة كانت تعيد كتابة التذكرة بالكامل** (كل الأعمدة + `DELETE`/`INSERT` لبنود القطع + زيادة
   `RowVersion`) لمجرد تسجيل سطر سجل. الآن `AppendMaintenanceActivity` يكتب السطر وحده.
6. **فشل الحفظ المسبق لم يكن يمنع تغيير الحالة:** `ApplyStatusTransition` كانت تتجاهل نتيجة
   `SaveWorkshopUpdates`، فلو كان مبلغ المصنعية غير صالح تُنقل المرحلة في الواجهة دون حفظها.
   الآن التحويل يتوقف ويُعرض سبب الفشل.
7. **فقدان مخزون عند إرجاع قطعة غير موجودة في القائمة المحمّلة:** كانت `RestorePartStockToInventory`
   لا تُرجع شيئًا إن لم تجد القطعة محليًا، بينما البند يُحذف من التذكرة. الآن الإرجاع يعتمد على
   `EffectiveSparePartId` من البند نفسه، ولا يُحذف البند (ولا يُلغى الأمر) إذا فشل إرجاع المخزون.
8. **لا تحقق من أطوال الحقول:** تقرير الفحص 2000، الفني 150، موعد التسليم 200، اسم البند اليدوي 250،
   ملاحظات التسليم 1500، سبب الإلغاء 1500. الآن تُفحص قبل الحفظ برسالة عربية تحدّد الحقل، بدل
   خطأ SQL 8152.

## 5) ملف SQL الجديد

`sql/15_EnableMaintenanceActions.sql` (idempotent، لا يحذف بيانات):

- التحقق من وجود `MaintenanceOrders.RowVersion` و`MaintenanceOrderParts.SparePartId`.
- `IX_MaintenanceOrders_Technician` (مفلتر على غير الفارغ) لخدمة دليل الفنيين والفلتر.
- `IX_MaintenanceOrderParts_SparePart` (مفلتر على غير `NULL`) لخدمة إرجاع القطع وفحص ارتباطها.
- إعادة تأكيد صلاحيات `ProjectZClient` التي تحتاجها الشاشة.
- استعلامات تحقق: وجود الأعمدة والفهارس، وعدد أسماء الفنيين المتاحين.

## 6) حدود معروفة

- عمليات الإرجاع/الاسترداد تُسجّل دائمًا على حساب **الكاش** (المبلغ يُرد نقدًا للعميل)، حتى لو كان
  العربون مقبوضًا عبر فودافون كاش أو إنستاباي؛ هذا سلوك مقصود ومكتوب في سطر النشاط.
- صرف/إرجاع قطعة الغيار يحدث في معاملة مستقلة عن حفظ التذكرة (`UpdateMaintenanceOrder`)، فإذا فشل
  الثاني بعد نجاح الأول تبقى الكمية قد تحركت؛ الشاشة تُظهر الخطأ وتعيد تحميل التذكرة من القاعدة،
  والبنود تُعاد ترقيمها 1..N في أول حفظ ناجح.
- التصفية والبحث في الذاكرة على التذاكر المحمّلة (نمط باقي الشاشات)، بلا صفحات.
- إغلاق تذكرة بمتبقٍ غير محصّل مسموح (بيع بالأجل) ويُسجَّل في سطر النشاط.
- بيانات الاستلام (الجهاز/العطل/العميل) للقراءة فقط هنا؛ تُعدَّل من شاشة الاستلام فقط.
- الطباعة معاينة (لا طابعة حرارية)، ولا يوجد تصدير Excel لهذه الشاشة.

## 7) خطوات القبول على Windows + SQL Server 2016 SP1+

1. نفّذ `sql/06` ثم `sql/09` ثم `sql/13` ثم `sql/15` بحساب إداري، ثم `sql/03_VerifyEnvironment.sql`.
2. افتح «إجراءات الصيانة»: الشارة «متصل بقاعدة البيانات — SQL Server»، والقائمة تعرض تذاكر القاعدة
   بأسمائها الحقيقية، وقائمة الفنيين تطابق:
   ```sql
   SELECT DISTINCT AssignedTechnician FROM dbo.MaintenanceOrders WHERE AssignedTechnician <> N'';
   SELECT DisplayName FROM dbo.Users WHERE IsActive = 1;
   ```
3. غيّر حالة تذكرة ثم تحقق:
   ```sql
   SELECT Id, OrderNumber, OrderStatus, AssignedTechnician, LaborCost, DiscountAmount, FinalServiceCost,
          HasWorkshopPricing, WarrantyDays, DeliveredAtUtc, CancellationReason, RowVersion
   FROM dbo.MaintenanceOrders ORDER BY Id DESC;
   SELECT MaintenanceOrderId, ActionTitle, OccurredAtUtc FROM dbo.MaintenanceActivities ORDER BY Id DESC;
   ```
4. احفظ تقرير فحص وتسعيرًا: يجب أن يتغيّر `DiagnosisNotes` و`LaborCost`/`DiscountAmount` و
   `FinalServiceCost` و`HasWorkshopPricing`، ويظهر سطر نشاط واحد لكل حفظ.
5. اصرف قطعة غيار: ينقص `dbo.MaintenanceSpareParts.Quantity` ويظهر بند في
   `dbo.MaintenanceOrderParts` (`SparePartId` مملوء، `ProductId` فارغ). ثم «حذف / إرجاع»: يعود
   الرصيد ويُحذف البند.
6. تحصّل دفعة إضافية: صف في `MaintenancePayments` (Stage=1) وحركة في `FinancialTransactions`
   وتغيّر رصيد `FinancialAccounts`.
7. سلّم تذكرة: `OrderStatus = 5`، `DeliveredAtUtc` و`WarrantyDays` مملوءان، دفعات Stage=2، ورد فرق
   العربون (Kind=2) إن وُجد.
8. ألغِ تذكرة: `OrderStatus = 6` مع `CancellationReason`، وإرجاع القطع، واسترداد العربون.
9. **اختبار عميلين:** افتح التذكرة نفسها على جهازين، احفظ من الأول، ثم احفظ من الثاني: يجب أن يظهر
   على الثاني خطأ عربي واضح بأن الأمر عُدّل من مستخدم آخر، وتُعاد التذكرة لحالتها المحفوظة.
10. اطبع الإيصال: سطر واحد جديد في `MaintenanceActivities` دون تغيّر `RowVersion` أو بنود القطع.
11. أوقف SQL Server واضغط «تحديث القائمة»: رسالة خطأ عربية بلا كشف تفاصيل الاتصال، والبيانات
    السابقة تبقى معروضة.

## 8) الملفات المتأثرة

- `src/ProjectZ/Views/MaintenanceActionsView.xaml` — شارة حالة البيانات، ختم آخر مزامنة.
- `src/ProjectZ/ViewModels/MaintenanceActionsViewModel.cs` — كشف التعارض وإعادة التحميل بعد الفشل،
  دليل الفنيين من SQL، استعادة الاختيار بالمعرّف، تعطيل التحديث أثناء التحميل المجمّع، سجل طباعة
  خفيف، تحقق الأطوال، إجهاض التحويل عند فشل الحفظ، إرجاع المخزون الآمن.
- `src/ProjectZ/ViewModels/MaintenanceActionsDialogViewModels.cs` — تحقق أطوال ملاحظات التسليم وسبب الإلغاء.
- `src/ProjectZ/Data/MaintenanceRepository.cs` — `RowVersion` قراءة/كتابة وحارس التحديث، إرجاع
  `RowVersion` من الإنشاء، استعمال `AppendMaintenanceActivity`.
- `src/ProjectZ/Models/MaintenanceDesignModels.cs` — `MaintenanceOrderDesignItem.DatabaseRowVersion`.
- `sql/15_EnableMaintenanceActions.sql` (جديد)، `docs/DATABASE.md`، `docs/ACCEPTANCE.md`، `README.md`.
