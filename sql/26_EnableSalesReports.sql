/* SQL Server 2016 SP1+, level 130. After current 07/22. Read-only reporting; no backfill or business DML.
   One SSMS batch: a failed prerequisite or grant cannot leave a partial installer. */
USE [ProjectZ];
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
BEGIN
    RAISERROR(N'استخدم نافذة إدارية جديدة دون معاملة مفتوحة أو ضمنية.',16,1);
    RETURN;
END;
SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL OR OBJECT_ID(N'dbo.vwSalesLineValuation',N'V') IS NULL
    OR OBJECT_ID(N'dbo.vwSalesInvoiceSummary',N'V') IS NULL OR COL_LENGTH(N'dbo.ReturnExchangeLines',N'NetAmount') IS NULL
    THROW 50260,N'أكمل مخطط المبيعات وحساب الاتصال والنسختين المحدثتين من 07 و22 أولًا.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.SalesPayments'') AND name=N''IX_SalesPayments_ReportDate'')
        CREATE INDEX IX_SalesPayments_ReportDate ON dbo.SalesPayments(PaidAtUtc,SalesInvoiceId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.ReturnExchanges'') AND name=N''IX_ReturnExchanges_ReportDate'')
        CREATE INDEX IX_ReturnExchanges_ReportDate ON dbo.ReturnExchanges(CreatedAtUtc,SalesInvoiceId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.AuditLog'') AND name=N''IX_AuditLog_ActionDate'')
        CREATE INDEX IX_AuditLog_ActionDate ON dbo.AuditLog(Module,ActionName,OccurredAtUtc) INCLUDE(EntityType,EntityId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.ReturnExchanges'') AND name=N''IX_ReturnExchanges_ReportInvoice'')
        CREATE INDEX IX_ReturnExchanges_ReportInvoice ON dbo.ReturnExchanges(SalesInvoiceId,CreatedAtUtc);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N''dbo.ReturnExchangeLines'') AND name=N''IX_ReturnExchangeLines_Operation'')
        CREATE INDEX IX_ReturnExchangeLines_Operation ON dbo.ReturnExchangeLines(ReturnExchangeId);';
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.ReadSalesReport @FromUtc datetime2(7), @UntilUtc datetime2(7)
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
    BEGIN
        RAISERROR(N''اقرأ التقرير في اتصال مستقل خارج معاملة الحفظ.'',16,1);
        RETURN;
    END;
    SET XACT_ABORT ON;
    IF @FromUtc IS NULL OR @UntilUtc IS NULL OR @UntilUtc<=@FromUtc OR DATEDIFF_BIG(day,@FromUtc,@UntilUtc)>367
        THROW 50261,N''اختر فترة صحيحة لا تتجاوز سنة.'',1;
    -- One consistent read of document values, lines and events. Release locks BEFORE streaming rows to a till.
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT s.SalesInvoiceId AS Id,s.InvoiceNumber,s.CustomerId,s.CustomerNameSnapshot AS Customer,
            s.CashierUserId,u.DisplayName AS Cashier,s.IssuedAtUtc,s.InvoiceStatus,s.Subtotal,s.DiscountAmount,
            s.NetSales,s.NetCost,s.CollectedAmount,s.RefundedAmount,s.RemainingAmount
        INTO #Docs FROM dbo.vwSalesInvoiceSummary s JOIN dbo.Users u ON u.Id=s.CashierUserId
        WHERE s.IssuedAtUtc>=@FromUtc AND s.IssuedAtUtc<@UntilUtc;
        CREATE UNIQUE CLUSTERED INDEX DocId ON #Docs(Id);

        SELECT l.SalesInvoiceId,l.LineNumber,l.ProductId,l.ProductNameSnapshot,l.CategoryNameSnapshot,l.BarcodeSnapshot,
            l.UnitNameSnapshot,l.IsExchangeReplacement,l.Quantity,l.ReturnedQuantity,l.UnitPrice,l.UnitCost,
            v.GrossAmount,v.NetLineAmount,v.ReturnedNetAmount,
            CONVERT(decimal(19,2),CASE WHEN d.InvoiceStatus IN (3,4) THEN 0 ELSE v.NetLineAmount-v.ReturnedNetAmount END) AS NetSales,
            CONVERT(decimal(19,2),CASE WHEN d.InvoiceStatus IN (3,4) THEN 0 ELSE v.RemainingCost END) AS NetCost
        INTO #Lines FROM #Docs d JOIN dbo.SalesInvoiceLines l ON l.SalesInvoiceId=d.Id
        JOIN dbo.vwSalesLineValuation v ON v.Id=l.Id;

        -- Keep both axes: all events of the issued cohort AND events in range on older invoices.
        -- Collect facts before joining the header: never join payments to item quantities or serials.
        ;WITH facts(InvoiceId,Kind,SourceId,Position,AtUtc,ActorId,Reference,Detail,Quantity,SalesDelta,CostDelta,AccountId,Received,Refunded) AS (
            SELECT d.Id,0,d.Id,l.LineNumber,d.IssuedAtUtc,d.CashierUserId,d.InvoiceNumber,l.ProductNameSnapshot,l.Quantity,
                l.NetLineAmount,l.Quantity*l.UnitCost,NULL,CONVERT(decimal(19,2),0),CONVERT(decimal(19,2),0)
            FROM #Docs d LEFT JOIN #Lines l ON l.SalesInvoiceId=d.Id AND l.IsExchangeReplacement=0
            UNION ALL
            SELECT o.SalesInvoiceId,CASE WHEN o.OperationType=0 THEN 1 WHEN l.LineKind=2 THEN 3 ELSE 2 END,o.Id,l.Id,
                o.CreatedAtUtc,o.ActorUserId,o.OperationNumber,CONCAT(l.ProductNameSnapshot,CASE WHEN l.UnitCondition=1 THEN N'' (هالك)'' ELSE N'''' END,N'' · '',o.Reason),
                CASE WHEN l.LineKind=2 THEN l.Quantity ELSE -l.Quantity END,
                CASE WHEN l.LineKind=2 THEN l.NetAmount ELSE -l.NetAmount END,
                CASE WHEN l.LineKind=2 THEN l.Quantity*l.UnitCost ELSE -l.Quantity*l.UnitCost END,NULL,0,0
            FROM dbo.ReturnExchanges o LEFT JOIN dbo.ReturnExchangeLines l ON l.ReturnExchangeId=o.Id
            WHERE (o.CreatedAtUtc>=@FromUtc AND o.CreatedAtUtc<@UntilUtc) OR EXISTS(SELECT 1 FROM #Docs d WHERE d.Id=o.SalesInvoiceId)
            UNION ALL
            -- Cancellation has no ReturnExchanges header. Use its typed audit event, not UpdatedAtUtc,
            -- refund amount or localized activity text. A legacy audit without a saved net stays NULL.
            SELECT k.InvoiceId,4,a.Id,0,a.OccurredAtUtc,a.ActorUserId,CONVERT(nvarchar(50),a.Id),N''إلغاء فاتورة البيع'',
                -c.Qty,-k.OldNet,CASE WHEN k.OldNet IS NOT NULL THEN -c.Cost END,NULL,0,0
            FROM dbo.AuditLog a CROSS APPLY (SELECT TRY_CONVERT(int,a.EntityId),TRY_CONVERT(decimal(19,2),JSON_VALUE(a.OldValuesJson,''$.net''))) k(InvoiceId,OldNet)
            OUTER APPLY (SELECT SUM(CONVERT(bigint,l.Quantity)) AS Qty,SUM(l.Quantity*l.UnitCost) AS Cost FROM dbo.SalesInvoiceLines l
                         WHERE l.SalesInvoiceId=k.InvoiceId AND NOT EXISTS(SELECT 1 FROM dbo.ReturnExchanges r WHERE r.SalesInvoiceId=k.InvoiceId)) c
            WHERE a.Module=N''Sales'' AND a.ActionName=N''CancelSale'' AND a.EntityType=N''SalesInvoice''
              AND ((a.OccurredAtUtc>=@FromUtc AND a.OccurredAtUtc<@UntilUtc) OR EXISTS(SELECT 1 FROM #Docs d WHERE d.Id=k.InvoiceId))
            UNION ALL
            SELECT p.SalesInvoiceId,CASE WHEN p.PaymentKind=1 THEN 5 ELSE 6 END,p.Id,0,p.PaidAtUtc,p.ActorUserId,
                CONVERT(nvarchar(50),p.Id),p.Note,NULL,NULL,NULL,p.FinancialAccountId,
                CASE WHEN p.PaymentKind=1 THEN p.Amount ELSE 0 END,CASE WHEN p.PaymentKind=2 THEN p.Amount ELSE 0 END
            FROM dbo.SalesPayments p WHERE (p.PaidAtUtc>=@FromUtc AND p.PaidAtUtc<@UntilUtc) OR EXISTS(SELECT 1 FROM #Docs d WHERE d.Id=p.SalesInvoiceId)
        )
        SELECT i.Id,i.InvoiceNumber,i.CustomerId,i.CustomerNameSnapshot AS Customer,i.CashierUserId,u.DisplayName AS Cashier,
            f.Kind,f.SourceId,f.Position,f.AtUtc,f.ActorId,COALESCE(actor.DisplayName,N''غير مسجل'') AS Actor,f.Reference,f.Detail,
            CONVERT(bigint,f.Quantity) AS Quantity,CONVERT(decimal(19,2),f.SalesDelta) AS SalesDelta,CONVERT(decimal(19,2),f.CostDelta) AS CostDelta,
            f.AccountId,fa.AccountKind,fa.Alias,f.Received,f.Refunded
        INTO #Events FROM facts f JOIN dbo.SalesInvoices i ON i.Id=f.InvoiceId JOIN dbo.Users u ON u.Id=i.CashierUserId
        LEFT JOIN dbo.Users actor ON actor.Id=f.ActorId LEFT JOIN dbo.FinancialAccounts fa ON fa.Id=f.AccountId OPTION(RECOMPILE);
        DECLARE @ReadAtUtc datetime2(7)=SYSUTCDATETIME();
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    -- Result contracts for SalesReportRepository: headers, valued lines, dated facts, snapshot time.
    SELECT Id,InvoiceNumber,CustomerId,Customer,CashierUserId,Cashier,IssuedAtUtc,InvoiceStatus,Subtotal,DiscountAmount,
           NetSales,NetCost,CollectedAmount,RefundedAmount,RemainingAmount FROM #Docs ORDER BY IssuedAtUtc DESC,Id DESC;
    SELECT SalesInvoiceId,LineNumber,ProductId,ProductNameSnapshot,CategoryNameSnapshot,BarcodeSnapshot,UnitNameSnapshot,
           IsExchangeReplacement,Quantity,ReturnedQuantity,UnitPrice,UnitCost,GrossAmount,NetLineAmount,ReturnedNetAmount,NetSales,NetCost FROM #Lines ORDER BY SalesInvoiceId,LineNumber;
    SELECT Id,InvoiceNumber,CustomerId,Customer,CashierUserId,Cashier,Kind,SourceId,Position,AtUtc,ActorId,Actor,Reference,Detail,
           Quantity,SalesDelta,CostDelta,AccountId,AccountKind,Alias,Received,Refunded FROM #Events ORDER BY AtUtc DESC,Kind,SourceId,Position;
    SELECT @ReadAtUtc AS ReadAtUtc;
END;';
    EXEC sys.sp_executesql N'GRANT EXECUTE ON OBJECT::dbo.ReadSalesReport TO [ProjectZClient];';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
PRINT N'اكتمل تثبيت تقرير المبيعات للقراءة فقط.';
GO
