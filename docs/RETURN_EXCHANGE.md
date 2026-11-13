# شاشة المرتجع والاستبدال — الربط الكامل بقاعدة البيانات وباقي الشاشات

الشاشة كانت تعمل سابقًا على بيانات في الذاكرة فقط (مرحلة تصميم). الآن كل مرتجع أو استبدال يُنفَّذ داخل معاملة واحدة على SQL Server، ويُقرأ السجل من القاعدة، وتُحدَّث الفواتير والمخزون ونقطة البيع والمالية من نفس الصفوف.

## ما يُقرأ من قاعدة البيانات

| العنصر | المصدر | متى |
| --- | --- | --- |
| الفواتير القابلة للمرتجع/الاستبدال | `dbo.SalesInvoices` + `dbo.SalesInvoiceLines` (مع `ReturnedQuantity`) | فتح الشاشة، زر «🔄 تحديث من قاعدة البيانات»، بعد أي عملية |
| الكميات المسموح إرجاعها لكل بند | `Quantity - ReturnedQuantity` من القاعدة (وليس من الذاكرة) | عند بناء مساحة العمل |
| الكتالوج المتاح كبديل | `dbo.Products` + `dbo.InventoryUnits` (`AvailableStock`) | التحديث، بعد كل عملية |
| سجل العمليات | `dbo.ReturnExchanges` + `dbo.ReturnExchangeLines` + `dbo.ReturnExchangeUnits` + `dbo.Users` | فتح تبويب السجل، بعد كل عملية |
| المدفوعات والأنشطة داخل مساحة العمل | `dbo.SalesPayments` + `dbo.SalesInvoiceActivities` | مع تحميل الفواتير |

## ما يُكتب في قاعدة البيانات

### مرتجع (`CreateReturn`)
- `dbo.ReturnExchanges` (رأس العملية برقم `RET-YYYY-0000` من `dbo.DocumentSequences`).
- `dbo.ReturnExchangeLines` لكل بند (مع `UnitCondition`: سليم/هالك — ويُدعم الآن **سليم + هالك في نفس البند**).
- `dbo.ReturnExchangeUnits` لكل وحدة معرّفة، و`SalesInvoiceLineUnits.AllocationStatus = 1`.
- `dbo.InventoryUnits`: الوحدة السليمة ترجع `UnitStatus=0`، والهالكة `UnitStatus=2` مع السبب والتاريخ.
- `dbo.StockMovements`: `Return` لكل وحدة سليمة، و`Damage` لكل وحدة هالكة.
- `dbo.SalesInvoices.InvoiceStatus` → 2 (جزئي) أو 3 (كلي) حسب البنود، و`SalesInvoiceLines.ReturnedQuantity`.
- `dbo.SalesPayments` (نوع 2 = رد) + `dbo.FinancialTransactions` + رصيد `dbo.FinancialAccounts` عند الرد النقدي.
- `dbo.SalesInvoiceActivities` + `dbo.AuditLog`.

### استبدال (`CreateExchange` — جديد بالكامل)
- كل ما سبق للوحدات الراجعة، ثم:
- `dbo.SalesInvoiceLines` سطر جديد لكل صنف بديل بـ`IsExchangeReplacement = 1` وبالسعر حسب فئة السعر المختارة.
- `dbo.SalesInvoiceLineUnits` لوحدات البديل + `dbo.InventoryUnits` → `UnitStatus=1`، و`dbo.StockMovements` بحركة `ExchangeOut`.
- `dbo.ReturnExchangeLines` بنوع `LineKind = 2` لكل بديل (بدون سطر بيع أصلي، حسب قيد `CK_ReturnExchangeLines_Source`).
- التسوية: فرق موجب → `dbo.SalesPayments` (نوع 1) موزّعًا على كاش/فودافون/إنستاباي + `dbo.FinancialTransactions`، وأي جزء غير مدفوع يُرحَّل آجلًا ويظهر في `RemainingAmount`. فرق سالب → رد من الحسابات التي حصّلت الفاتورة. الفرق الصفري → استبدال متساوٍ.
- `dbo.SalesInvoices.InvoiceStatus = 5` (Exchanged) + سطر نشاط + `AuditLog`.

كل العمليات في معاملة `Serializable` واحدة مع `UPDLOCK/HOLDLOCK` على الوحدات، فأي فشل يتراجع بالكامل.

## الربط بباقي الشاشات

| الشاشة | كيف تتأثر |
| --- | --- |
| الفواتير | `CreateReturn`/`CreateExchange` تُحدّث `ReturnedQuantity` والحالة، ثم الشاشة تعيد `LoadInvoicesInto` فتظهر الكميات المرتجعة والأنشطة من القاعدة |
| المخزون | بعد العملية يُنادى `ReloadInventoryOnly` (في `MainViewModel`) فيُعاد تحميل الأصناف والوحدات من SQL — المتاح يزيد بالوحدات السليمة والهالك يزيد بالهالكة |
| نقطة البيع | `Pos.NotifyExternalInventoryChanged()` يربط الفواتير المفتوحة بالكتالوج المحدَّث |
| إضافة منتج | `AddProduct.NotifyInventoryLoaded()` يحدّث القائمة والأرصدة |
| المالية | رد/تحصيل الفرق يغيّر `FinancialAccounts.CurrentBalance` ويضيف سطرًا في `FinancialTransactions` |
| المرتجع والاستبدال | تبويب السجل يقرأ من `dbo.ReturnExchanges` بعد كل عملية (لا إدخال يدوي في الذاكرة) |

كذلك عندما تُلغى فاتورة أو يُسجَّل مرتجع من شاشة **الفواتير**، تُستدعى `ReloadStockFromDatabase` فتُحدَّث الأرصدة في نقطة البيع والمخزون من نفس الصفوف.

## الأوامر والأزرار (لا شيء «تصميم»)

- `RefreshFromDatabaseCommand` (زر «تحديث من قاعدة البيانات»)، والشارة تعرض `DataSourceLabel`: «متصل بقاعدة البيانات — SQL Server».
- `ConfirmOperationCommand` → `ExecuteDatabaseOperation` (يحفظ ثم يعيد القراءة).
- `OpenOperationInvoiceCommand` لفتح فاتورة العملية، `PrintSelectedInvoiceCommand` للمعاينة والطباعة.
- كل أزرار الكميات (`AllGoodCommand`, `AllDamagedCommand`, `Increment*`, `Decrement*`)، فئات الأسعار، وتعبئة الفرق (`FillDiffCashCommand`, `FillDiffVodafoneCommand`, `FillDiffInstaPayCommand`).
- تبويب السجل يعرض `HistorySourceLabel`: «السجل مقروء من dbo.ReturnExchanges».

## ما تحتاجه قبل التشغيل

```sql
-- بحساب إداري على الخادم بعد sql/06 و sql/09
:r sql/11_EnableReturnExchange.sql
```

السكربت idempotent ولا يُنشئ جداول: يمنح `ProjectZClient` صلاحيات `SELECT/INSERT` على `dbo.ReturnExchanges` و`dbo.ReturnExchangeLines` و`dbo.ReturnExchangeUnits`، مع `UPDATE` على الفواتير وبنودها والوحدات والحسابات، و`INSERT` على المدفوعات والأنشطة والحركات والتدقيق. لا يمنح `DELETE` على أي جدول.

## الاختبار اليدوي المطلوب على Windows + SQL Server 2016 SP1+

1. شغّل `sql/06`, `sql/09`, `sql/11`، ثم سجّل الدخول.
2. افتح شاشة المرتجع والاستبدال: تظهر الشارة «متصل بقاعدة البيانات — SQL Server»، والفواتير المحفوظة في القاعدة فقط (لا بيانات تجريبية).
3. **مرتجع كلي سليم**: اختر فاتورة، «الكل سليم»، اكتب السبب، أكّد. تحقق: `dbo.ReturnExchanges` صف جديد، `dbo.ReturnExchangeUnits` بعدد الوحدات، `dbo.InventoryUnits` عادت `UnitStatus=0`، `dbo.StockMovements` حركة `Return` لكل وحدة، `SalesInvoices.InvoiceStatus = 3`.
4. **مرتجع جزئي مختلط**: بند واحد 2 سليم + 1 هالك، أكّد. تحقق: وحدة واحدة `UnitStatus=2` مع `DamageReason`، وحركة `Damage`، والحالة 2.
5. **استبدال بفرق على العميل**: أعد وحدة رخيصة واستبدلها بأغلى، أدخل فرق 100 كاش، أكّد. تحقق: سطر بيع جديد `IsExchangeReplacement=1`، `SalesPayments` نوع 1 بقيمة 100، رصيد الكاش زاد 100، `InvoiceStatus = 5`، والسجل يعرض العملية برقم `EXC-`.
6. **استبدال بفرق للعميل**: الحالة العكسية → `SalesPayments` نوع 2 ورصيد الكاش نقص.
7. **متساوي القيمة**: لا حركة مالية، والسجل يقول «استبدال متساوي القيمة».
8. **منع التجاوز**: حاول إرجاع كمية أكبر من `RemainingQuantity`، وحاول استبدال صنف رصيده أقل من المطلوب — يجب أن تُرفض العملية برسالة عربية ولا يتغير أي صف.
9. **الربط**: بعد كل عملية افتح «المخزون» (الأرصدة محدَّثة)، و«نقطة البيع» (الأرصدة الجديدة)، و«الفواتير» (الكميات المرتجعة)، و«حساباتي» (الرصيد بعد التحصيل/الرد).
10. **السجل**: تبويب السجل يعرض العمليات من القاعدة بعد إعادة تشغيل البرنامج أيضًا (وليس فقط في الجلسة الحالية).
11. **مرتجع من شاشة الفواتير**: نفّذ مرتجعًا من هناك، ثم افتح «المرتجع والاستبدال» → يظهر في السجل (مصدر واحد).

## التحقق الآلي

- `python3 tools/static_checks.py` — يتحقق من غياب «مرحلة التصميم (بدون Backend)»، ومن مرور الحفظ عبر `CreateReturn`/`CreateExchange`، ومن قراءة السجل من `dbo.ReturnExchanges`، ومن إعادة تحميل المخزون بين الشاشات، ومن سكربت الصلاحيات.
- Windows CI: بناء + اختبارات + نشر.

آخر تشغيل: الفحص الثابت PASS، والبناء أخضر. لم يُشغّل WPF ولا SQL Server في بيئة العمل، لذا بند القبول اليدوي إلزامي.
