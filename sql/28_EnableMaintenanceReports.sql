/* SQL Server 2016 SP1 / compatibility 130. After maintenance settlement 24.
   Single guarded SSMS batch: read endpoint, supporting date indexes, EXECUTE only. */
USE [ProjectZ];
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
BEGIN
    RAISERROR(N'استخدم نافذة إدارية جديدة دون معاملة مفتوحة أو ضمنية.',16,1); RETURN;
END;
SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL OR OBJECT_ID(N'dbo.SettleMaintenanceTicket',N'P') IS NULL
    OR COL_LENGTH(N'dbo.MaintenancePayments',N'RefundOfPaymentId') IS NULL OR OBJECT_ID(N'dbo.MaintenanceSparePartMovements',N'U') IS NULL
    THROW 50264,N'أكمل إعداد هوية الاتصال ومخزون الورشة وتسوية الصيانة 24 قبل تثبيت التقرير.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MaintenancePayments') AND name=N'IX_MaintenancePayments_ReportDate')
        EXEC(N'CREATE INDEX IX_MaintenancePayments_ReportDate ON dbo.MaintenancePayments(PaidAtUtc,MaintenanceOrderId);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MaintenanceActivities') AND name=N'IX_MaintenanceActivities_ReportDate')
        EXEC(N'CREATE INDEX IX_MaintenanceActivities_ReportDate ON dbo.MaintenanceActivities(OccurredAtUtc,MaintenanceOrderId);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MaintenanceSparePartMovements') AND name=N'IX_MaintenanceSparePartMovements_ReportDate')
        EXEC(N'CREATE INDEX IX_MaintenanceSparePartMovements_ReportDate ON dbo.MaintenanceSparePartMovements(OccurredAtUtc,SparePartId);');
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.ReadMaintenanceReport @FromUtc datetime2(7), @UntilUtc datetime2(7)
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
    BEGIN
        RAISERROR(N''افتح التقرير خارج معاملة الحفظ.'',16,1); RETURN;
    END;
    SET XACT_ABORT ON;
    IF @FromUtc IS NULL OR @UntilUtc IS NULL OR @FromUtc>=@UntilUtc OR DATEDIFF_BIG(second,@FromUtc,@UntilUtc)>366*86400+3600
        THROW 50265,N''اختر فترة صحيحة لا تتجاوز 366 يومًا.'',1;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- Current ticket cohorts; a reopened ticket no longer belongs to its previous delivery cohort.
        SELECT Id INTO #Cohort FROM dbo.MaintenanceOrders WHERE ReceivedAtUtc>=@FromUtc AND ReceivedAtUtc<@UntilUtc
        UNION SELECT Id FROM dbo.MaintenanceOrders WHERE OrderStatus=5 AND DeliveredAtUtc>=@FromUtc AND DeliveredAtUtc<@UntilUtc;
        CREATE UNIQUE CLUSTERED INDEX CohortId ON #Cohort(Id);
        ;WITH ids AS (
            SELECT Id FROM dbo.MaintenancePayments WHERE PaidAtUtc>=@FromUtc AND PaidAtUtc<@UntilUtc
            UNION SELECT p.Id FROM #Cohort c JOIN dbo.MaintenancePayments p ON p.MaintenanceOrderId=c.Id
        ) SELECT p.Id,p.MaintenanceOrderId,p.PaidAtUtc,p.PaymentStage,p.PaymentKind,p.Amount,p.FinancialAccountId,a.Alias,a.AccountKind,
                 p.ActorUserId,u.DisplayName,p.RefundOfPaymentId,
                 CONVERT(bit,CASE WHEN p.PaymentKind=2 AND (o.Id IS NULL OR o.PaymentKind<>1 OR o.MaintenanceOrderId<>p.MaintenanceOrderId OR o.FinancialAccountId<>p.FinancialAccountId
                    OR o.Amount<(SELECT SUM(r.Amount) FROM dbo.MaintenancePayments r WHERE r.RefundOfPaymentId=o.Id)) THEN 1 ELSE 0 END) AS RefundNeedsReview
        INTO #Money FROM ids JOIN dbo.MaintenancePayments p ON p.Id=ids.Id JOIN dbo.FinancialAccounts a ON a.Id=p.FinancialAccountId
            JOIN dbo.Users u ON u.Id=p.ActorUserId LEFT JOIN dbo.MaintenancePayments o ON o.Id=p.RefundOfPaymentId;
        -- Match by ticket, actual account and direction across its full history. The ledger is evidence,
        -- NOT a second source of receipts; an unmatched historical refund remains a recorded outflow.
        SELECT m.MaintenanceOrderId,m.FinancialAccountId,m.PaymentKind INTO #Unmatched FROM
            (SELECT DISTINCT MaintenanceOrderId,FinancialAccountId,PaymentKind FROM #Money) m
        WHERE (SELECT SUM(p.Amount) FROM dbo.MaintenancePayments p WHERE p.MaintenanceOrderId=m.MaintenanceOrderId AND p.FinancialAccountId=m.FinancialAccountId AND p.PaymentKind=m.PaymentKind)
            <>COALESCE((SELECT SUM(t.Amount) FROM dbo.FinancialTransactions t WHERE t.SourceEntityType=N''MaintenanceOrder'' AND t.SourceEntityId=m.MaintenanceOrderId
                AND t.FinancialAccountId=m.FinancialAccountId AND t.IsIncoming=CASE WHEN m.PaymentKind=1 THEN 1 ELSE 0 END),0);
        ;WITH ids AS (
            SELECT Id FROM dbo.MaintenanceActivities WHERE OccurredAtUtc>=@FromUtc AND OccurredAtUtc<@UntilUtc
            UNION SELECT a.Id FROM #Cohort c JOIN dbo.MaintenanceActivities a ON a.MaintenanceOrderId=c.Id
        ) SELECT a.Id,a.MaintenanceOrderId,a.OccurredAtUtc,a.ActorUserId,u.DisplayName,k.Kind,h.RecordedStatus
        INTO #Activity FROM ids JOIN dbo.MaintenanceActivities a ON a.Id=ids.Id JOIN dbo.Users u ON u.Id=a.ActorUserId
        -- Return only canonical classifications, not old free-text titles/details which can contain a price or a secret.
        CROSS APPLY (VALUES(CASE WHEN a.ActionTitle LIKE N''تغيير الحالة إلى:%'' THEN 4 WHEN a.ActionTitle LIKE N''طباعة:%'' THEN 11
            ELSE CASE a.ActionTitle WHEN N''استلام جهاز صيانة'' THEN 3 WHEN N''تحديث بيانات الورشة والفحص'' THEN 4
                WHEN N''تسليم الجهاز وإغلاق أمر الصيانة'' THEN 5 WHEN N''إلغاء أمر الصيانة'' THEN 6 WHEN N''إعادة فتح بإذن المشرف'' THEN 7 WHEN N''إعادة فتح أمر الصيانة'' THEN 7
                WHEN N''صرف قطعة غيار'' THEN 8 WHEN N''صرف قطعة غيار من مخزون الصيانة'' THEN 8 WHEN N''إضافة بند يدوي'' THEN 9 WHEN N''إضافة قطعة غيار يدوية'' THEN 9
                WHEN N''إزالة بند صيانة'' THEN 10 WHEN N''حذف بند قطعة غيار'' THEN 10 WHEN N''إرجاع قطعة غيار لمخزون الصيانة'' THEN 10
                WHEN N''معاينة / طباعة مستند صيانة'' THEN 11 WHEN N''معاينة / طباعة تذكرة صيانة'' THEN 11
                WHEN N''تحصيل دفعة تحت الحساب'' THEN 12 WHEN N''تحصيل دفعة من فاتورة الصيانة'' THEN 12 ELSE 13 END END)) k(Kind)
        -- 24 stamps activity and audit together. Use a single matching typed stage, never today''s stage in its place.
        OUTER APPLY (SELECT CASE WHEN COUNT_BIG(*)=1 THEN MIN(TRY_CONVERT(tinyint,JSON_VALUE(log.NewValuesJson,''$.OrderStatus''))) END AS RecordedStatus
            FROM dbo.AuditLog log WHERE log.EntityType=N''MaintenanceOrder'' AND log.EntityId=CONVERT(nvarchar(100),a.MaintenanceOrderId) AND log.OccurredAtUtc=a.OccurredAtUtc
                AND log.ActorUserId=a.ActorUserId AND log.Module=N''Maintenance'' AND log.ActionName=CASE k.Kind WHEN 3 THEN N''Intake'' WHEN 4 THEN N''Workshop''
                    WHEN 5 THEN N''Deliver'' WHEN 6 THEN N''Cancel'' WHEN 7 THEN N''Reopen'' WHEN 8 THEN N''ChangePart'' WHEN 9 THEN N''ChangePart'' WHEN 10 THEN N''ChangePart'' WHEN 12 THEN N''CollectDeposit'' END) h;
        ;WITH ids AS (
            SELECT Id FROM dbo.MaintenanceSparePartMovements WHERE OccurredAtUtc>=@FromUtc AND OccurredAtUtc<@UntilUtc
            UNION SELECT m.Id FROM #Cohort c JOIN dbo.MaintenanceSparePartMovements m ON m.MaintenanceOrderId=c.Id
        ) SELECT m.Id,m.SparePartId,m.MovementKind,m.Quantity,m.QuantityBefore,m.QuantityAfter,m.OccurredAtUtc,m.MaintenanceOrderId,
                m.SupplierId,s.Name AS SupplierName,CONVERT(bit,COALESCE(s.IsActive,0)) AS SupplierActive,m.ActorUserId,u.DisplayName,
                m.CustomerId,c.Name AS CustomerName,CONVERT(bit,COALESCE(c.IsActive,0)) AS CustomerActive
        INTO #Moves FROM ids JOIN dbo.MaintenanceSparePartMovements m ON m.Id=ids.Id JOIN dbo.Users u ON u.Id=m.ActorUserId LEFT JOIN dbo.Suppliers s ON s.Id=m.SupplierId LEFT JOIN dbo.Customers c ON c.Id=m.CustomerId;
        SELECT p.Id,p.Name,p.Category,COALESCE(p.Barcode,N'''') AS Barcode,p.Unit,p.Quantity,p.MinimumStockAlert,p.ReorderQuantity,p.StorageLocation,p.IsActive,
            p.SupplierId,s.Name AS SupplierName,CONVERT(bit,COALESCE(s.IsActive,0)) AS SupplierActive
        INTO #Catalog FROM dbo.MaintenanceSpareParts p LEFT JOIN dbo.Suppliers s ON s.Id=p.SupplierId;
        ;WITH scope AS (SELECT Id FROM #Cohort UNION SELECT MaintenanceOrderId FROM #Money UNION SELECT MaintenanceOrderId FROM #Activity
            UNION SELECT MaintenanceOrderId FROM #Moves WHERE MaintenanceOrderId IS NOT NULL)
        SELECT o.Id,o.OrderNumber,o.ReceivedAtUtc,o.DeliveredAtUtc,o.ExpectedDeliveryAtUtc,o.OrderStatus,o.Priority,o.AssignedTechnician,
            o.ReceivedByUserId,u.DisplayName,o.CustomerId,o.CustomerNameSnapshot,CONVERT(bit,COALESCE(c.IsActive,0)) AS CustomerActive,
            o.DeviceType,o.DeviceBrand,o.DeviceModel,o.SerialOrImei,o.WarrantyDays
        INTO #Orders FROM scope JOIN dbo.MaintenanceOrders o ON o.Id=scope.Id JOIN dbo.Users u ON u.Id=o.ReceivedByUserId LEFT JOIN dbo.Customers c ON c.Id=o.CustomerId;
        SELECT l.MaintenanceOrderId,l.LineNumber,l.ProductNameSnapshot,l.BarcodeSnapshot,l.Quantity,l.IsManualPart,l.SparePartId,l.ProductId
        INTO #Lines FROM #Cohort c JOIN dbo.MaintenanceOrderParts l ON l.MaintenanceOrderId=c.Id;
        DECLARE @ReadUtc datetime2(7)=SYSUTCDATETIME();
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH;
    -- Stable contracts; stream only staged data after releasing the business locks.
    SELECT Id,OrderNumber,ReceivedAtUtc,DeliveredAtUtc,ExpectedDeliveryAtUtc,OrderStatus,Priority,AssignedTechnician,ReceivedByUserId,DisplayName,CustomerId,CustomerNameSnapshot,CustomerActive,DeviceType,DeviceBrand,DeviceModel,SerialOrImei,WarrantyDays FROM #Orders ORDER BY ReceivedAtUtc DESC,Id DESC;
    SELECT MaintenanceOrderId,LineNumber,ProductNameSnapshot,BarcodeSnapshot,Quantity,IsManualPart,SparePartId,ProductId FROM #Lines ORDER BY MaintenanceOrderId,LineNumber;
    SELECT m.Id,m.MaintenanceOrderId,m.PaidAtUtc,m.PaymentStage,m.PaymentKind,m.Amount,m.FinancialAccountId,m.Alias,m.AccountKind,m.ActorUserId,m.DisplayName,m.RefundOfPaymentId,
        m.RefundNeedsReview,CONVERT(bit,CASE WHEN x.MaintenanceOrderId IS NULL THEN 0 ELSE 1 END) AS LedgerNeedsReview FROM #Money m
        LEFT JOIN #Unmatched x ON x.MaintenanceOrderId=m.MaintenanceOrderId AND x.FinancialAccountId=m.FinancialAccountId AND x.PaymentKind=m.PaymentKind ORDER BY m.PaidAtUtc DESC,m.Id DESC;
    SELECT Id,MaintenanceOrderId,OccurredAtUtc,ActorUserId,DisplayName,Kind,RecordedStatus FROM #Activity ORDER BY OccurredAtUtc DESC,Id DESC;
    SELECT Id,Name,Category,Barcode,Unit,Quantity,MinimumStockAlert,ReorderQuantity,StorageLocation,IsActive,SupplierId,SupplierName,SupplierActive FROM #Catalog ORDER BY Name,Id;
    SELECT Id,SparePartId,MovementKind,Quantity,QuantityBefore,QuantityAfter,OccurredAtUtc,MaintenanceOrderId,SupplierId,SupplierName,SupplierActive,ActorUserId,DisplayName,CustomerId,CustomerName,CustomerActive FROM #Moves ORDER BY OccurredAtUtc DESC,Id DESC;
    SELECT @ReadUtc AS ReadAtUtc;
END;';
    EXEC(N'GRANT EXECUTE ON OBJECT::dbo.ReadMaintenanceReport TO [ProjectZClient];');
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK; THROW;
END CATCH;
PRINT N'اكتمل تثبيت تقارير الصيانة.';
GO
