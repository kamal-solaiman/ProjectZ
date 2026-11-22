# شاشة المخزون — الربط الكامل بقاعدة البيانات

شاشة المخزون تعمل الآن من SQL Server بالكامل: كل صنف ووحدة ومورد وتصنيف وحركة مخزون يُقرأ من القاعدة عند الدخول وعند الضغط على «🔄 تحديث المخزون»، وكل إجراء كتابة يمر عبر `InventoryRepository` داخل معاملة `Serializable` مع تدقيق في `AuditLog`.

## ما يُقرأ من قاعدة البيانات

| العنصر | المصدر | متى |
| --- | --- | --- |
| الأصناف والأسعار والمخزون المتاح والهالك | `dbo.Products` + `dbo.InventoryUnits` | فتح الشاشة، زر التحديث، بعد أي كتابة |
| التسجيلات (كل وحدة بمعرّفها وسيريالها وموردها وفاتورتها) | `dbo.InventoryUnits` + `dbo.Suppliers` + `dbo.PurchaseInvoices` | نفس الأوقات |
| فلاتر التصنيفات وقائمة التصنيف في «تعديل الصنف» | `dbo.Categories` عبر `CategoryRepository.LoadNamesInto` | فتح الشاشة، زر التحديث، تغيير في شاشة التصنيفات |
| اقتراحات الموردين في نافذة التوريد وفلاتر الموردين | `dbo.Suppliers` (`WHERE IsActive=1`) | فتح الشاشة، زر التحديث |
| سجل حركات المخزون | `dbo.StockMovements` + `dbo.Users` (اسم المنفّذ) | فتح الشاشة، زر التحديث، زر «حركات» لأي صنف/وحدة |

لم تُعد الشاشة تستخدم `InventoryDesignSeedData.DefaultSuppliers` ولا `PosDesignSeedData.ProductCategories`؛ هذه البيانات لم تبقَ إلا لمسار التصميم بلا قاعدة بيانات (`repository is null`).

## ما يُكتب في قاعدة البيانات

| الإجراء | العملية في SQL |
| --- | --- |
| توريد كمية | إدراج وحدات في `dbo.InventoryUnits` (مع المورد والسعر وفاتورة الشراء) + `StockMovements(Supply)` + تحديث `CostPrice` + `AuditLog`. المورد الجديد يُنشأ تلقائيًا في `dbo.Suppliers` عبر `EnsureSupplier`. |
| تعديل صنف | `UPDATE dbo.Products` لكل الحقول (الاسم، الباركود، الوحدة، الأسعار الأربعة، حد النواقص، تفعيل البيع، تتبع السيريال، ملاحظات) مع فحص `RowVersion`. |
| تسجيل سيريال | `UPDATE dbo.InventoryUnits SET SerialNumber` بشرط عدم التكرار + تدقيق. |
| تسجيل هالك (كمية أو وحدة محددة) | تحويل الوحدات إلى `UnitStatus=2` مع السبب والتاريخ + `StockMovements(Damage)` + تدقيق. |
| تعديل بيانات الهالك (جديد) | `UPDATE dbo.InventoryUnits SET DamageReason / DamageSettlementStatus` + تدقيق. |
| استرجاع للمتاح | إعادة الوحدة إلى `UnitStatus=0` + `StockMovements(DamageRestore)` + تدقيق. |
| إرجاع للمورد (وحدة أو «إرجاع كل الهالك للمورد») | `DamageSettlementStatus = N'مرتجع للمورد'` + تدقيق لكل وحدة. |
| حذف سجل هالك | حذف الوحدة إن لم يكن لها سجل مرتبط، وإلا رسالة واضحة. |
| حذف/أرشفة صنف | حذف فعلي عند عدم وجود وحدات أو فواتير، وإلا أرشفة `IsActiveForSale=0` (يُبلَّغ بأي الحالتين). |

## الربط بباقي الشاشات

أي كتابة ناجحة تستدعي `ReloadAfterWrite()`:

1. `RefreshFromDatabase()` — إعادة قراءة الأصناف والوحدات والموردين والحركات.
2. `InventoryChanged` — حدث يلتقطه `MainViewModel.OnInventoryChanged` ثم:
   - `Pos.NotifyExternalInventoryChanged()` — إعادة ربط فواتير البيع المفتوحة بالكتالوج المُحدَّث وتحديث فلاتر نقطة البيع.
   - `AddProduct.NotifyInventoryLoaded()` — تحديث قائمة الأصناف والباركود المقترح في شاشة إضافة منتج.
   - `Invoices.NotifyExternalInvoicesChanged()` — تحديث شاشة الفواتير.
   - `Suppliers.NotifyExternalDataChanged()` — تحديث كشف الموردين وأرصدتهم.

شاشة التصنيفات بدورها تنادي `Inventory.RefreshFromDatabase()` عند أي إضافة/تعديل/حذف، فتتزامن فلاتر المخزون مع القاعدة فورًا (بما فيها إعادة تسمية تصنيف).

## الأوامر الظاهرة في الواجهة (لا أزرار تصميم)

- `GoToAddProductCommand`, `RefreshInventoryCommand` (زر «تحديث المخزون»).
- `OpenRegistrationsCommand`, `OpenSupplyCommand`, `OpenEditCommand`, `OpenBarcodePrintCommand`, `OpenMarkDamagedCommand`, `ShowProductMovementsCommand`, `DeleteProductCommand`.
- تبويب الهالك: `RestoreDamagedUnitCommand`, `MarkReturnedToSupplierCommand`, `EditDamagedUnitCommand`, `DeleteDamagedUnitCommand`, `MarkAllDamagedReturnedCommand`.
- الفلاتر: `SelectedCategoryFilter` / `SelectedSupplierFilter` / `OnlyLowStock` / `ToggleLowStockCommand` / `ClearCatalogFiltersCommand` / `ClearDamagedFiltersCommand` / `SelectedSettlementFilter`.
- نافذة التسجيلات: زر «حركات» لكل وحدة يعرض `dbo.StockMovements` مفلترة على `InventoryUnitId`.
- شارة الهيدر: «متصل بقاعدة البيانات — SQL Server» بدل شارة «تصميم الواجهة — بدون Backend».

## الاختبار اليدوي المطلوب على Windows + SQL Server 2016 SP1+

نفّذ `sql/06_CreateFullSchema.sql` ثم `sql/09` و`sql/10` بحساب إداري، وسجّل الدخول بـ`ProjectZClient`.

1. **العرض**: افتح «المخزون» — تظهر شارة «متصل بقاعدة البيانات»، والأصناف والعدادات (إجمالي الأصناف/الكمية/النواقص/الهالك) مطابقة لـ`SELECT COUNT(*) FROM dbo.Products` و`dbo.InventoryUnits WHERE UnitStatus=0`.
2. **التوريد**: «توريد كمية» لصنف موجود، اختر موردًا من الاقتراحات (يجب أن تكون أسماء `dbo.Suppliers`). بعد الحفظ تحقق من `dbo.InventoryUnits` (صفوف جديدة) و`dbo.StockMovements` (سطر `Supply`) و`dbo.AuditLog`.
3. **مورد جديد**: اكتب اسم مورد غير مسجل وأتمّ التوريد، ثم `SELECT * FROM dbo.Suppliers WHERE Name=...` — يجب أن يوجد صف جديد.
4. **تعديل صنف**: غيّر السعر والتصنيف (القائمة يجب أن تعرض تصنيفات `dbo.Categories` فقط) واحفظ، ثم أعد فتح الشاشة وتأكد من بقاء القيم.
5. **السيريال**: من «التسجيلات» أدخل سيريالًا لوحدة، ثم جرّب نفس السيريال لوحدة أخرى — يجب أن يُرفض برسالة تكرار.
6. **الهالك**: سجّل هالك كمية، وتأكد من نقص المتاح وزيادة الهالك، ثم «تعديل» سجل الهالك وغيّر السبب/التسوية وتأكد من `dbo.InventoryUnits` و`AuditLog`.
7. **الاسترجاع والإرجاع للمورد**: «استرجاع للمتاح» يعيد الوحدة للمتاح (`StockMovements(DamageRestore)`)، و«مرتجع» أو «إرجاع كل الهالك للمورد» يضبط `DamageSettlementStatus`.
8. **سجل الحركات**: كل صنف له زر «حركات» يعرض آخر حركاته بالوقت والكمية والسبب والمنفّذ؛ ولوحة أسفل الشاشة تعرض آخر 100 حركة على مستوى المخزن.
9. **الربط بنقطة البيع**: بعد التوريد، افتح «نقطة البيع» فورًا دون إعادة تشغيل — الكمية المتاحة الجديدة تظهر، وأضف الصنف لفاتورة وأكمل البيع، ثم ارجع للمخزون واضغط «تحديث المخزون»: الكمية نقصت وسجل الحركات يعرض `Sale`.
10. **الربط بإضافة منتج والفواتير**: بعد تعديل سعر صنف من المخزون، ابحث عنه في «إضافة منتج» (السعر الجديد ظاهر) وافتح «الفواتير» (البيانات محدَّثة).
11. **التصنيفات**: أضف تصنيفًا وأعد تسمية آخر من شاشة التصنيفات، ثم افتح فلتر التصنيف في المخزون — يجب أن يعكس القاعدة فورًا.
12. **الحذف/الأرشفة**: احذف صنفًا بلا فواتير (يُحذف فعليًا)، وصنفًا له فواتير (يُؤرشف `IsActiveForSale=0`) وتأكد من الرسالة المناسبة في شريط الحالة.

## التحقق الآلي

- `python3 tools/static_checks.py` — يتحقق من ربط كل أزرار الشاشة بأوامر، ومن غياب شارة «بدون Backend»، ومن قراءة الموردين/التصنيفات/الحركات من القاعدة، ومن إشعار بقية الشاشات بعد كل كتابة، ومن وجود النوافذ والتدقيق.
- Windows CI (`.github/workflows`) — بناء + اختبارات + نشر على Windows.

آخر تشغيل للفحص الثابت: كل الفحوص `PASS`، والفحص لا يغطي تشغيل WPF ولا SQL Server فعليًا (لذلك بند الاختبار اليدوي أعلاه إلزامي).
