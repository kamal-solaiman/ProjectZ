/* SQL Server 2016 SP1 / compatibility 130. Run after current 07 and purchase settlement 25.
   One atomic SSMS batch. Supporting indexes and read procedure only: no business writes or backfill. */
USE [ProjectZ];
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
BEGIN
    RAISERROR(N'استخدم نافذة إدارية جديدة دون معاملة مفتوحة أو ضمنية.',16,1);
    RETURN;
END;
SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL OR OBJECT_ID(N'dbo.SaleShare',N'FN') IS NULL
    OR COL_LENGTH(N'dbo.vwPurchaseInvoiceSummary',N'NeedsReview') IS NULL OR OBJECT_ID(N'dbo.vwSupplierCreditBalances',N'V') IS NULL
    OR OBJECT_ID(N'dbo.ReceiveSupplierCredit',N'P') IS NULL OR OBJECT_ID(N'dbo.DeleteUnsettledPurchase',N'P') IS NULL
    THROW 50262,N'أكمل مخطط المشتريات وحساب الاتصال والنسختين المحدثتين من 07 و25 أولًا.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.PurchaseInvoices'') AND name=N''IX_PurchaseInvoices_ReportDate'')
    CREATE INDEX IX_PurchaseInvoices_ReportDate ON dbo.PurchaseInvoices(IssuedAtUtc);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.PurchaseInvoices'') AND name=N''IX_PurchaseInvoices_ReportCreation'')
    CREATE INDEX IX_PurchaseInvoices_ReportCreation ON dbo.PurchaseInvoices(CreatedAtUtc);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.PurchasePayments'') AND name=N''IX_PurchasePayments_ReportDate'')
    CREATE INDEX IX_PurchasePayments_ReportDate ON dbo.PurchasePayments(PaidAtUtc,PurchaseInvoiceId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.PurchaseReturns'') AND name=N''IX_PurchaseReturns_ReportDate'')
    CREATE INDEX IX_PurchaseReturns_ReportDate ON dbo.PurchaseReturns(ReturnedAtUtc,PurchaseInvoiceId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.PurchaseReturns'') AND name=N''IX_PurchaseReturns_ReportInvoice'')
    CREATE INDEX IX_PurchaseReturns_ReportInvoice ON dbo.PurchaseReturns(PurchaseInvoiceId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.SupplierCreditApplications'') AND name=N''IX_SupplierCreditApplications_ReportDate'')
    CREATE INDEX IX_SupplierCreditApplications_ReportDate ON dbo.SupplierCreditApplications(AppliedAtUtc,InvoiceId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.AuditLog'') AND name=N''IX_AuditLog_ActionDate'')
    CREATE INDEX IX_AuditLog_ActionDate ON dbo.AuditLog(Module,ActionName,OccurredAtUtc) INCLUDE(EntityType,EntityId);';
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.ReadPurchaseReport @FromUtc datetime2(7), @UntilUtc datetime2(7)
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
    BEGIN
        RAISERROR(N''افتح التقرير خارج معاملة الحفظ.'',16,1);
        RETURN;
    END;
    SET XACT_ABORT ON;
    IF @FromUtc IS NULL OR @UntilUtc IS NULL OR @UntilUtc<=@FromUtc OR DATEDIFF_BIG(second,@FromUtc,@UntilUtc)>366*86400+3600
        THROW 50263,N''اختر فترة صحيحة لا تتجاوز 366 يومًا.'',1;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT s.PurchaseInvoiceId Id,s.IssuedAtUtc,i.CreatedAtUtc,s.InvoiceStatus,s.Subtotal,s.DiscountAmount,
            CONVERT(decimal(19,2),CASE WHEN s.InvoiceStatus=3 THEN 0 ELSE s.ActiveGross END) AS ActiveGross,
            s.NetPurchases,s.PaidAmount,s.SupplierRefundReceived,s.CreditIssued,s.CreditApplied,s.AvailableCredit,s.RemainingPayable,s.NeedsReview
        INTO #Docs FROM dbo.vwPurchaseInvoiceSummary s JOIN dbo.PurchaseInvoices i ON i.Id=s.PurchaseInvoiceId
        WHERE s.IssuedAtUtc>=@FromUtc AND s.IssuedAtUtc<@UntilUtc;
        CREATE UNIQUE CLUSTERED INDEX DocId ON #Docs(Id);

        -- Analytical allocation ONLY: distribute the effective header discount on surviving gross,
        -- never on a returned/cancelled unit. Differences of cumulative cent shares telescope to the header.
        ;WITH base AS (
            SELECT l.PurchaseInvoiceId,l.LineNumber,l.ProductId,l.ProductNameSnapshot,l.CategoryNameSnapshot,l.BarcodeSnapshot,l.UnitNameSnapshot,
                l.Quantity,l.ReturnedQuantity,l.UnitPurchaseCost,CASE WHEN d.InvoiceStatus=3 THEN 0 ELSE l.Quantity-l.ReturnedQuantity END AS Kept,
                d.ActiveGross,d.ActiveGross-d.NetPurchases AS EffectiveDiscount
            FROM #Docs d JOIN dbo.PurchaseInvoiceLines l ON l.PurchaseInvoiceId=d.Id
        ), parts AS (
            SELECT b.*, CONVERT(decimal(19,2),Kept*UnitPurchaseCost) AS LineGross,
                CONVERT(decimal(19,0),SUM(Kept*UnitPurchaseCost) OVER(PARTITION BY PurchaseInvoiceId ORDER BY LineNumber ROWS UNBOUNDED PRECEDING)*100) AS ToCents
            FROM base b
        )
        SELECT PurchaseInvoiceId,LineNumber,ProductId,ProductNameSnapshot,CategoryNameSnapshot,BarcodeSnapshot,UnitNameSnapshot,
            Quantity,ReturnedQuantity,Kept,UnitPurchaseCost,LineGross,
            dbo.SaleShare(EffectiveDiscount,ToCents,ActiveGross*100)-dbo.SaleShare(EffectiveDiscount,ToCents-LineGross*100,ActiveGross*100) AS DiscountShare
        INTO #Lines FROM parts;

        -- One row per underlying event; quantities/payment splits/physical units do not multiply money.
        CREATE TABLE #Facts(Kind int NOT NULL, SourceId int NOT NULL, Origin int NOT NULL DEFAULT 0,
            InvoiceId int NOT NULL, RelatedId int NULL, AtUtc datetime2(7) NOT NULL, ActorId int NULL,
            Reference nvarchar(150) NOT NULL, Detail nvarchar(2000) NULL, Delta decimal(19,2) NULL,
            DebtReduction decimal(19,2) NULL, Amount decimal(19,2) NULL, AccountId int NULL, CreditId int NULL,
            LedgerId int NULL, Review bit NOT NULL DEFAULT 0);
        -- New create/edit/delete store the actual change, independent of the editable supplier document date.
        -- The typed audit survives physical deletion. Never substitute today''s values or parse free-text money.
        ;WITH edits AS (
            SELECT Id FROM dbo.AuditLog WHERE Module=N''Purchasing'' AND EntityType=N''PurchaseInvoice'' AND OccurredAtUtc>=@FromUtc AND OccurredAtUtc<@UntilUtc
                AND ActionName IN(N''CreatePurchaseInvoice'',N''UpdatePurchaseInvoice'',N''EditPurchaseInvoice'',N''DeletePurchaseInvoice'',N''CancelPurchaseInvoice'')
            UNION SELECT a.Id FROM #Docs d JOIN dbo.AuditLog a ON a.EntityId=CONVERT(nvarchar(100),d.Id) AND a.EntityType=N''PurchaseInvoice'' AND a.Module=N''Purchasing''
                AND a.ActionName IN(N''CreatePurchaseInvoice'',N''UpdatePurchaseInvoice'',N''EditPurchaseInvoice'',N''DeletePurchaseInvoice'',N''CancelPurchaseInvoice'')
        )
        INSERT #Facts(Kind,SourceId,Origin,InvoiceId,AtUtc,ActorId,Reference,Detail,Delta,Review)
        SELECT CASE a.ActionName WHEN N''CreatePurchaseInvoice'' THEN 0 WHEN N''DeletePurchaseInvoice'' THEN 4 WHEN N''CancelPurchaseInvoice'' THEN 3 ELSE 1 END,
            a.Id,1,k.InvoiceId,a.OccurredAtUtc,a.ActorUserId,CONCAT(N''تدقيق #'',a.Id),
            COALESCE(JSON_VALUE(a.NewValuesJson,''$.Details''),JSON_VALUE(a.NewValuesJson,''$.details''),N''حذف مستند غير مسوّى''),k.Delta,
            CASE WHEN k.Delta IS NULL THEN 1 ELSE 0 END
        FROM edits JOIN dbo.AuditLog a ON a.Id=edits.Id CROSS APPLY (SELECT TRY_CONVERT(int,a.EntityId),
            CASE WHEN a.ActionName=N''DeletePurchaseInvoice'' THEN -TRY_CONVERT(decimal(19,2),JSON_VALUE(a.OldValuesJson,''$.NetPurchases''))
                 ELSE TRY_CONVERT(decimal(19,2),JSON_VALUE(a.NewValuesJson,''$.DocumentDelta'')) END) k(InvoiceId,Delta)
        WHERE k.InvoiceId IS NOT NULL AND (a.ActionName<>N''CancelPurchaseInvoice'' OR k.Delta IS NULL); -- pre-25 cancellation had no return document

        -- Older/imported documents without a saved creation amount: date is known, value is not.
        INSERT #Facts(Kind,SourceId,Origin,InvoiceId,AtUtc,ActorId,Reference,Detail,Review)
        SELECT 0,i.Id,2,i.Id,i.CreatedAtUtc,i.BuyerUserId,i.InvoiceNumber,N''إنشاء بلا قيمة ترحيل تاريخية'',1
        FROM dbo.PurchaseInvoices i WHERE ((i.CreatedAtUtc>=@FromUtc AND i.CreatedAtUtc<@UntilUtc) OR EXISTS(SELECT 1 FROM #Docs d WHERE d.Id=i.Id))
            AND NOT EXISTS(SELECT 1 FROM dbo.AuditLog a WHERE a.Module=N''Purchasing'' AND a.EntityType=N''PurchaseInvoice''
                AND a.EntityId=CONVERT(nvarchar(100),i.Id) AND a.ActionName=N''CreatePurchaseInvoice'');

        ;WITH ids AS (
            SELECT Id FROM dbo.PurchaseReturns WHERE ReturnedAtUtc>=@FromUtc AND ReturnedAtUtc<@UntilUtc
            UNION SELECT r.Id FROM #Docs d JOIN dbo.PurchaseReturns r ON r.PurchaseInvoiceId=d.Id
        ) SELECT r.Id,r.ReturnNumber,r.PurchaseInvoiceId,r.ReturnedAtUtc,r.ActorUserId,r.Reason,r.NetReduction,r.DebtReduction,
            -- In 25 cancellation is terminal and posts the final PRT; preceding partial returns are still returns.
            CASE WHEN i.InvoiceStatus=3 AND r.NetReduction IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.PurchaseReturns later WHERE later.PurchaseInvoiceId=i.Id AND later.Id>r.Id) THEN 3 ELSE 2 END AS Kind
        INTO #Returns FROM ids JOIN dbo.PurchaseReturns r ON r.Id=ids.Id JOIN dbo.PurchaseInvoices i ON i.Id=r.PurchaseInvoiceId;
        CREATE UNIQUE CLUSTERED INDEX ReturnId ON #Returns(Id);
        INSERT #Facts(Kind,SourceId,InvoiceId,AtUtc,ActorId,Reference,Detail,Delta,DebtReduction,Review)
        SELECT r.Kind,r.Id,r.PurchaseInvoiceId,r.ReturnedAtUtc,r.ActorUserId,r.ReturnNumber,r.Reason,-r.NetReduction,r.DebtReduction,
            CASE WHEN r.NetReduction IS NULL OR r.DebtReduction IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.PurchaseReturnLines l WHERE l.PurchaseReturnId=r.Id) THEN 1 ELSE 0 END
        FROM #Returns r;
        SELECT r.Id AS ReturnId,r.PurchaseInvoiceId,r.ReturnNumber,r.ReturnedAtUtc,l.ProductId,l.ProductNameSnapshot,l.CategoryNameSnapshot,l.BarcodeSnapshot,
            x.Quantity,x.UnitPurchaseCost,u.Units,u.Damaged
        INTO #ReturnLines FROM #Returns r JOIN dbo.PurchaseReturnLines x ON x.PurchaseReturnId=r.Id JOIN dbo.PurchaseInvoiceLines l ON l.Id=x.PurchaseInvoiceLineId
        CROSS APPLY (SELECT COUNT_BIG(*) AS Units,COUNT_BIG(CASE WHEN ru.WasDamaged=1 THEN 1 END) AS Damaged FROM dbo.PurchaseReturnUnits ru WHERE ru.PurchaseReturnLineId=x.Id) u;
        INSERT #Facts(Kind,SourceId,InvoiceId,AtUtc,ActorId,Reference,Detail,Amount,CreditId)
        SELECT 8,c.Id,r.PurchaseInvoiceId,r.ReturnedAtUtc,r.ActorUserId,r.ReturnNumber,N''إشعار لصالح المحل • لا حركة مال'',c.Amount,c.Id
        FROM #Returns r JOIN dbo.SupplierCreditNotes c ON c.SourceReturnId=r.Id;
        SELECT b.Id,b.SourceInvoiceId,b.ReturnNumber,r.ReturnedAtUtc,b.Amount,b.AppliedAmount,b.ReceivedAmount,b.AvailableAmount
        INTO #Notes FROM #Docs d JOIN dbo.vwSupplierCreditBalances b ON b.SourceInvoiceId=d.Id JOIN dbo.PurchaseReturns r ON r.Id=b.SourceReturnId;

        ;WITH ids AS (
            SELECT Id FROM dbo.SupplierCreditApplications WHERE AppliedAtUtc>=@FromUtc AND AppliedAtUtc<@UntilUtc
            UNION SELECT a.Id FROM #Docs d JOIN dbo.SupplierCreditApplications a ON a.InvoiceId=d.Id
            UNION SELECT a.Id FROM #Notes n JOIN dbo.SupplierCreditApplications a ON a.CreditId=n.Id
        ) INSERT #Facts(Kind,SourceId,InvoiceId,RelatedId,AtUtc,ActorId,Reference,Detail,Amount,CreditId)
        SELECT 9,a.Id,a.InvoiceId,r.PurchaseInvoiceId,a.AppliedAtUtc,a.ActorUserId,r.ReturnNumber,N''مقاصة على فاتورة الدين • لا حركة مال'',a.Amount,c.Id
        FROM ids JOIN dbo.SupplierCreditApplications a ON a.Id=ids.Id JOIN dbo.SupplierCreditNotes c ON c.Id=a.CreditId JOIN dbo.PurchaseReturns r ON r.Id=c.SourceReturnId;

        ;WITH ids AS (
            SELECT Id FROM dbo.PurchasePayments WHERE PaidAtUtc>=@FromUtc AND PaidAtUtc<@UntilUtc
            UNION SELECT p.Id FROM #Docs d JOIN dbo.PurchasePayments p ON p.PurchaseInvoiceId=d.Id
        ) INSERT #Facts(Kind,SourceId,InvoiceId,AtUtc,ActorId,Reference,Detail,Amount,AccountId,CreditId,LedgerId,Review)
        SELECT CASE WHEN p.PaymentKind=1 THEN 5 WHEN v.Proven=1 THEN 6 ELSE 7 END,p.Id,p.PurchaseInvoiceId,p.PaidAtUtc,p.ActorUserId,
            COALESCE(x.Evidence,CONCAT(N''دفعة #'',p.Id)),p.Note,p.Amount,p.FinancialAccountId,x.CreditId,x.LedgerId,
            CASE WHEN p.PaymentKind=2 AND v.Proven=0 THEN 1 ELSE 0 END
        FROM ids JOIN dbo.PurchasePayments p ON p.Id=ids.Id LEFT JOIN dbo.SupplierCreditReceipts x ON x.PaymentId=p.Id
        LEFT JOIN dbo.FinancialTransactions t ON t.Id=x.LedgerId LEFT JOIN dbo.SupplierCreditNotes c ON c.Id=x.CreditId
        LEFT JOIN dbo.PurchaseReturns r ON r.Id=c.SourceReturnId
        CROSS APPLY (SELECT CASE WHEN x.SupplierId=c.SupplierId AND r.PurchaseInvoiceId=p.PurchaseInvoiceId AND t.IsIncoming=1 AND t.Amount=p.Amount
            AND t.FinancialAccountId=p.FinancialAccountId AND t.OccurredAtUtc=p.PaidAtUtc AND t.SourceEntityType=N''PurchaseInvoice'' AND t.SourceEntityId=p.PurchaseInvoiceId THEN 1 ELSE 0 END) v(Proven);

        ;WITH keysInScope AS (SELECT Id FROM #Docs UNION SELECT InvoiceId FROM #Facts UNION SELECT RelatedId FROM #Facts WHERE RelatedId IS NOT NULL)
        SELECT k.Id,COALESCE(i.InvoiceNumber,JSON_VALUE(gone.OldValuesJson,''$.InvoiceNumber''),CONCAT(N''#'',k.Id)) AS Number,
            COALESCE(i.SupplierInvoiceNumber,JSON_VALUE(gone.OldValuesJson,''$.SupplierInvoiceNumber''),N'''') AS SupplierReference,
            COALESCE(i.SupplierId,TRY_CONVERT(int,JSON_VALUE(gone.OldValuesJson,''$.SupplierId''))) AS SupplierId,
            COALESCE(i.SupplierNameSnapshot,JSON_VALUE(gone.OldValuesJson,''$.SupplierNameSnapshot''),N''غير موثق'') AS Supplier,
            COALESCE(i.BuyerUserId,created.ActorUserId) AS BuyerId,COALESCE(u.DisplayName,N''غير موثق'') AS Buyer,
            CONVERT(bit,CASE WHEN i.Id IS NULL THEN 0 ELSE 1 END) AS CanOpen,CONVERT(bit,COALESCE(s.IsActive,0)) AS CanOpenSupplier
        INTO #Identity FROM keysInScope k LEFT JOIN dbo.PurchaseInvoices i ON i.Id=k.Id
        OUTER APPLY (SELECT TOP(1) a.OldValuesJson FROM dbo.AuditLog a WHERE i.Id IS NULL AND a.EntityType=N''PurchaseInvoice'' AND a.EntityId=CONVERT(nvarchar(100),k.Id)
            AND a.Module=N''Purchasing'' AND a.ActionName=N''DeletePurchaseInvoice'' ORDER BY a.Id DESC) gone
        OUTER APPLY (SELECT TOP(1) a.ActorUserId FROM dbo.AuditLog a WHERE i.Id IS NULL AND a.EntityType=N''PurchaseInvoice'' AND a.EntityId=CONVERT(nvarchar(100),k.Id)
            AND a.Module=N''Purchasing'' AND a.ActionName=N''CreatePurchaseInvoice'' ORDER BY a.Id) created
        LEFT JOIN dbo.Users u ON u.Id=COALESCE(i.BuyerUserId,created.ActorUserId)
        LEFT JOIN dbo.Suppliers s ON s.Id=COALESCE(i.SupplierId,TRY_CONVERT(int,JSON_VALUE(gone.OldValuesJson,''$.SupplierId'')));
        SELECT f.Kind,f.SourceId,f.Origin,f.InvoiceId,f.RelatedId,f.AtUtc,f.ActorId,COALESCE(u.DisplayName,N''غير مسجل'') AS Actor,
            f.Reference,COALESCE(f.Detail,N'''') AS Detail,f.Delta,f.DebtReduction,f.Amount,f.AccountId,a.AccountKind,a.Alias,f.CreditId,f.LedgerId,f.Review
        INTO #Events FROM #Facts f LEFT JOIN dbo.Users u ON u.Id=f.ActorId LEFT JOIN dbo.FinancialAccounts a ON a.Id=f.AccountId;
        DECLARE @ReadAtUtc datetime2(7)=SYSUTCDATETIME();
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    -- Stream only staged data, after releasing locks. Stable result contracts for PurchaseReportRepository.
    SELECT Id,Number,SupplierReference,SupplierId,Supplier,BuyerId,Buyer,CanOpen,CanOpenSupplier FROM #Identity ORDER BY Id;
    SELECT Id,IssuedAtUtc,CreatedAtUtc,InvoiceStatus,Subtotal,DiscountAmount,ActiveGross,NetPurchases,PaidAmount,SupplierRefundReceived,CreditIssued,CreditApplied,AvailableCredit,RemainingPayable,NeedsReview FROM #Docs ORDER BY IssuedAtUtc DESC,Id DESC;
    SELECT PurchaseInvoiceId,LineNumber,ProductId,ProductNameSnapshot,CategoryNameSnapshot,BarcodeSnapshot,UnitNameSnapshot,Quantity,ReturnedQuantity,Kept,UnitPurchaseCost,LineGross,DiscountShare FROM #Lines ORDER BY PurchaseInvoiceId,LineNumber;
    SELECT Kind,SourceId,Origin,InvoiceId,RelatedId,AtUtc,ActorId,Actor,Reference,Detail,Delta,DebtReduction,Amount,AccountId,AccountKind,Alias,CreditId,LedgerId,Review FROM #Events ORDER BY AtUtc DESC,Kind,SourceId,Origin;
    SELECT ReturnId,PurchaseInvoiceId,ReturnNumber,ReturnedAtUtc,ProductId,ProductNameSnapshot,CategoryNameSnapshot,BarcodeSnapshot,Quantity,UnitPurchaseCost,Units,Damaged FROM #ReturnLines ORDER BY ReturnedAtUtc DESC,ReturnId,ProductId;
    SELECT Id,SourceInvoiceId,ReturnNumber,ReturnedAtUtc,Amount,AppliedAmount,ReceivedAmount,AvailableAmount FROM #Notes ORDER BY ReturnedAtUtc DESC,Id DESC;
    SELECT @ReadAtUtc AS ReadAtUtc;
END;';
    EXEC sys.sp_executesql N'GRANT EXECUTE ON OBJECT::dbo.ReadPurchaseReport TO [ProjectZClient];';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
PRINT N'اكتمل تثبيت تقرير المشتريات للقراءة فقط.';
GO
