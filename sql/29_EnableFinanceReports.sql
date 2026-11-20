/* SQL Server 2016 SP1 / compatibility 130. After wallet corrections (23) and supplier receipts (25).
   This installer adds indexes and a restricted read endpoint. No operational data is backfilled. */
USE [ProjectZ];
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
BEGIN
    RAISERROR(N'افتح نافذة إدارية جديدة خارج أي معاملة.',16,1); RETURN;
END;
SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL OR OBJECT_ID(N'dbo.PostCustomerTransfer',N'P') IS NULL
    OR COL_LENGTH(N'dbo.CustomerTransfers',N'CashAccountId') IS NULL OR COL_LENGTH(N'dbo.SupplierCreditReceipts',N'LedgerId') IS NULL
    THROW 50266,N'أكمل تجهيز الحساب المحدود وتسوية التحويلات 23 والمشتريات 25 قبل تركيب تقرير المالية.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FinancialTransactions') AND name=N'IX_FinancialTransactions_ReportDate')
        EXEC(N'CREATE INDEX IX_FinancialTransactions_ReportDate ON dbo.FinancialTransactions(OccurredAtUtc,FinancialAccountId) INCLUDE (Amount,IsIncoming,CountsTowardLimits);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerTransfers') AND name=N'IX_CustomerTransfers_ReportIssue')
        EXEC(N'CREATE INDEX IX_CustomerTransfers_ReportIssue ON dbo.CustomerTransfers(OccurredAtUtc,Id);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerTransfers') AND name=N'IX_CustomerTransfers_ReportUndo')
        EXEC(N'CREATE INDEX IX_CustomerTransfers_ReportUndo ON dbo.CustomerTransfers(ReversedAtUtc,Id);');
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.ReadFinanceReport @FromUtc datetime2(7), @UntilUtc datetime2(7)
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
    BEGIN
        RAISERROR(N''حمّل التقرير خارج معاملة تسجيل حركة مالية.'',16,1); RETURN;
    END;
    SET XACT_ABORT ON;
    IF @FromUtc IS NULL OR @UntilUtc IS NULL OR @FromUtc>=@UntilUtc OR DATEDIFF_BIG(second,@FromUtc,@UntilUtc)>366*86400+3600
        THROW 50267,N''اختر فترة صحيحة لا تتجاوز 366 يومًا.'',1;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- One snapshot of the book, including stopped accounts. Opening capital is not a receipt.
        SELECT a.Id,a.AccountKind,
            CASE WHEN a.Alias COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9٠-٩۰-۹@+]%'' OR NULLIF(a.Identifier,N'''') IS NOT NULL AND CHARINDEX(a.Identifier,a.Alias)>0
                 THEN N''حساب'' ELSE a.Alias END AS SafeAlias,
            CASE WHEN a.AccountKind=0 OR LEN(a.Identifier)=0 THEN N'''' WHEN LEN(a.Identifier)<=4 THEN N''••••''
                 WHEN CHARINDEX(N''@'',a.Identifier)>0 THEN N''••••@…'' ELSE N''••••''+RIGHT(a.Identifier,4) END AS MaskedIdentifier,
            a.IsActive,a.IsDefault,a.CreatedAtUtc,a.UpdatedAtUtc,a.CurrentBalance,a.DailyDepositLimit,a.DailyWithdrawalLimit,a.MonthlyDepositLimit,a.MonthlyWithdrawalLimit,
            previous.BalanceAfter AS PreviousClose, later.BeforeBalance AS NextOpen, latest.BalanceAfter AS LastBookBalance
        INTO #Accounts FROM dbo.FinancialAccounts a
        OUTER APPLY (SELECT TOP(1) BalanceAfter FROM dbo.FinancialTransactions WHERE FinancialAccountId=a.Id AND OccurredAtUtc<@FromUtc ORDER BY OccurredAtUtc DESC,Id DESC) previous
        OUTER APPLY (SELECT TOP(1) BalanceAfter-CASE WHEN IsIncoming=1 THEN Amount ELSE -Amount END AS BeforeBalance FROM dbo.FinancialTransactions
                     WHERE FinancialAccountId=a.Id AND OccurredAtUtc>=@UntilUtc ORDER BY OccurredAtUtc,Id) later
        OUTER APPLY (SELECT TOP(1) BalanceAfter FROM dbo.FinancialTransactions WHERE FinancialAccountId=a.Id ORDER BY OccurredAtUtc DESC,Id DESC) latest;
        CREATE UNIQUE CLUSTERED INDEX AccountsId ON #Accounts(Id);
        -- Only saved ledger legs carry money here. No raw identifiers, counterparty, notes, arbitrary category, or invoice/customer snapshots leave SQL.
        SELECT t.Id,t.FinancialAccountId,t.OccurredAtUtc,t.IsIncoming,t.Amount,t.BalanceAfter,t.CountsTowardLimits,t.ActorUserId,
            CASE t.SourceEntityType WHEN N''SalesInvoice'' THEN 1 WHEN N''ReturnExchange'' THEN 2 WHEN N''PurchaseInvoice'' THEN 3 WHEN N''MaintenanceOrder'' THEN 4
                WHEN N''Expense'' THEN 5 WHEN N''CustomerTransfer'' THEN 6 ELSE CASE WHEN t.TransferGroupId IS NOT NULL THEN 7 WHEN t.SourceEntityType IS NULL THEN 0 ELSE 8 END END AS SourceKind,
            t.SourceEntityId,CONVERT(bit,CASE WHEN COALESCE(si.Id,rx.Id,pi.Id,mo.Id,ex.Id,ct.Id) IS NOT NULL THEN 1 ELSE 0 END) AS HasDocument,
            CASE WHEN n.DocNumber COLLATE Latin1_General_100_BIN2 LIKE N''[A-Z][A-Z][A-Z]-[0-9][0-9][0-9][0-9]-[0-9]%'' AND LEN(n.DocNumber) BETWEEN 10 AND 17
                 AND SUBSTRING(n.DocNumber,10,50) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9]%'' THEN n.DocNumber END AS DocNumber,
            CASE WHEN proof.LedgerId IS NOT NULL THEN N''قبض مورد موثّق'' WHEN t.SourceEntityType=N''PurchaseInvoice'' AND t.IsIncoming=1 THEN N''قيد وارد من المشتريات بلا ربط بسند قبض''
                 WHEN t.Category IN(N''إيداع'',N''سحب'',N''تعديل إيجابي'',N''تعديل سلبي'',N''تحويل صادر'',N''تحويل وارد'',N''عكس تحويل'',N''عكس تعديل'',N''تعديل سجل'',N''سحب للعميل'',N''تحويل للعميل'',N''تحويل من العميل'',N''رد دفعة صيانة'',N''تحصيل صيانة'',N''تحصيل مبيعات'',N''سداد مشتريات'') THEN t.Category
                 WHEN t.SourceEntityType=N''Expense'' THEN N''دفع مصروف'' ELSE N'''' END AS Purpose,t.TransferGroupId
        INTO #Book FROM dbo.FinancialTransactions t
        LEFT JOIN dbo.SalesInvoices si ON t.SourceEntityType=N''SalesInvoice'' AND si.Id=t.SourceEntityId
        LEFT JOIN dbo.ReturnExchanges rx ON t.SourceEntityType=N''ReturnExchange'' AND rx.Id=t.SourceEntityId
        LEFT JOIN dbo.PurchaseInvoices pi ON t.SourceEntityType=N''PurchaseInvoice'' AND pi.Id=t.SourceEntityId
        LEFT JOIN dbo.SupplierCreditReceipts proof ON proof.LedgerId=t.Id AND t.SourceEntityType=N''PurchaseInvoice'' AND t.IsIncoming=1
        LEFT JOIN dbo.MaintenanceOrders mo ON t.SourceEntityType=N''MaintenanceOrder'' AND mo.Id=t.SourceEntityId
        LEFT JOIN dbo.Expenses ex ON t.SourceEntityType=N''Expense'' AND ex.Id=t.SourceEntityId
        LEFT JOIN dbo.CustomerTransfers ct ON t.SourceEntityType=N''CustomerTransfer'' AND ct.Id=t.SourceEntityId
        CROSS APPLY (VALUES(COALESCE(si.InvoiceNumber,rx.OperationNumber,pi.InvoiceNumber,mo.OrderNumber,ex.ExpenseNumber))) n(DocNumber)
        WHERE t.OccurredAtUtc>=@FromUtc AND t.OccurredAtUtc<@UntilUtc;
        ;WITH scope AS (
            SELECT Id FROM dbo.CustomerTransfers WHERE OccurredAtUtc>=@FromUtc AND OccurredAtUtc<@UntilUtc
            UNION SELECT Id FROM dbo.CustomerTransfers WHERE ReversedAtUtc>=@FromUtc AND ReversedAtUtc<@UntilUtc
            UNION SELECT SourceEntityId FROM #Book WHERE SourceKind=6
        ) SELECT h.Id,h.FinancialAccountId,h.CashAccountId,h.OperationKind,h.Amount,h.Commission,h.CashDelta,
            CASE WHEN LEN(h.CustomerNumber)>4 THEN N''••••''+RIGHT(h.CustomerNumber,4) ELSE N''••••'' END AS MaskedCustomer,
            h.ActorUserId,h.OccurredAtUtc,h.OperationStatus,h.ReversedAtUtc,h.ReversedByUserId,h.CorrectsTransferId,child.Id AS ReplacementId
        INTO #Headers FROM scope JOIN dbo.CustomerTransfers h ON h.Id=scope.Id LEFT JOIN dbo.CustomerTransfers child ON child.CorrectsTransferId=h.Id;
        CREATE UNIQUE CLUSTERED INDEX HeadersId ON #Headers(Id);
        -- Check application and counter-posting separately. Old in-place edits did not preserve dated terms: do not apply today''s commission retroactively.
        ;WITH stages AS (
            SELECT h.*,p.Undo,(CASE WHEN h.OperationKind=1 THEN -h.Amount ELSE h.Amount END)*(1-2*p.Undo) AS WalletEffect,
                h.CashDelta*(1-2*p.Undo) AS CashEffect,CASE p.Undo WHEN 0 THEN h.ActorUserId ELSE h.ReversedByUserId END AS StageActor
            FROM #Headers h CROSS JOIN (VALUES(0),(1)) p(Undo)
        ), checks AS (
            SELECT s.Id,s.Undo,COUNT_BIG(t.Id) AS AllLegs,
                COUNT_BIG(CASE WHEN t.FinancialAccountId=s.FinancialAccountId AND t.ActorUserId=s.StageActor AND sg.Delta=s.WalletEffect AND (s.Undo=1 OR t.Category=CASE s.OperationKind WHEN 1 THEN N''تحويل للعميل'' ELSE N''سحب للعميل'' END) THEN 1 END) AS WalletLegs,
                COUNT_BIG(CASE WHEN t.FinancialAccountId=s.CashAccountId AND t.ActorUserId=s.StageActor AND sg.Delta=s.CashEffect AND (s.Undo=1 OR t.Category=CASE s.OperationKind WHEN 1 THEN N''تحويل من العميل'' ELSE N''تحويل للعميل'' END) THEN 1 END) AS CashLegs
            FROM stages s LEFT JOIN dbo.FinancialTransactions t ON t.SourceEntityType=N''CustomerTransfer'' AND t.SourceEntityId=s.Id AND CASE WHEN t.Category=N''عكس تحويل'' THEN 1 ELSE 0 END=s.Undo
            CROSS APPLY (VALUES(CASE WHEN t.IsIncoming=1 THEN t.Amount ELSE -t.Amount END)) sg(Delta)
            GROUP BY s.Id,s.Undo
        ) SELECT h.Id,x.Undo,CONVERT(bit,CASE WHEN wallet.AccountKind IN (1,2) AND cash.AccountKind=0 AND h.CashDelta=CASE WHEN h.OperationKind=1 THEN h.Amount+h.Commission ELSE h.Commission-h.Amount END
             AND x.AllLegs=CASE WHEN h.CashDelta=0 THEN 1 ELSE 2 END AND x.WalletLegs=1 AND x.CashLegs=CASE WHEN h.CashDelta=0 THEN 0 ELSE 1 END THEN 1 ELSE 0 END) AS Confirmed
        INTO #Evidence FROM #Headers h JOIN checks x ON x.Id=h.Id
        JOIN #Accounts wallet ON wallet.Id=h.FinancialAccountId LEFT JOIN #Accounts cash ON cash.Id=h.CashAccountId;
        ;WITH actors AS (SELECT ActorUserId AS Id FROM #Book UNION SELECT ActorUserId FROM #Headers UNION SELECT ReversedByUserId FROM #Headers WHERE ReversedByUserId IS NOT NULL)
        SELECT u.Id,CASE WHEN u.DisplayName COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9٠-٩۰-۹@+]%'' THEN N''مستخدم'' ELSE u.DisplayName END AS SafeName INTO #Actors FROM actors JOIN dbo.Users u ON u.Id=actors.Id;
        -- Current limit settings are not a history of limits. Measure today''s and this month''s usage on Egypt business boundaries (independent of the report range).
        DECLARE @AsOf datetime2(7)=SYSUTCDATETIME();
        DECLARE @Today date=CONVERT(date,@AsOf AT TIME ZONE ''UTC'' AT TIME ZONE ''Egypt Standard Time'');
        DECLARE @DayUtc datetime2(7)=CONVERT(datetime2(7),CONVERT(datetime2(7),@Today) AT TIME ZONE ''Egypt Standard Time'' AT TIME ZONE ''UTC''),
                @MonthUtc datetime2(7)=CONVERT(datetime2(7),CONVERT(datetime2(7),DATEFROMPARTS(YEAR(@Today),MONTH(@Today),1)) AT TIME ZONE ''Egypt Standard Time'' AT TIME ZONE ''UTC'');
        SELECT t.FinancialAccountId,t.IsIncoming,SUM(CASE WHEN t.OccurredAtUtc>=@DayUtc THEN t.Amount ELSE 0 END) AS UsedToday,SUM(t.Amount) AS UsedMonth
        INTO #Used FROM dbo.FinancialTransactions t JOIN #Accounts a ON a.Id=t.FinancialAccountId
        WHERE a.AccountKind<>0 AND t.CountsTowardLimits=1 AND t.OccurredAtUtc>=@MonthUtc AND t.OccurredAtUtc<=@AsOf GROUP BY t.FinancialAccountId,t.IsIncoming;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH;
    SELECT Id,AccountKind,SafeAlias,MaskedIdentifier,IsActive,IsDefault,CreatedAtUtc,UpdatedAtUtc,CurrentBalance,DailyDepositLimit,DailyWithdrawalLimit,MonthlyDepositLimit,MonthlyWithdrawalLimit,PreviousClose,NextOpen,LastBookBalance FROM #Accounts ORDER BY AccountKind,Id;
    SELECT Id,FinancialAccountId,OccurredAtUtc,IsIncoming,Amount,BalanceAfter,CountsTowardLimits,ActorUserId,SourceKind,SourceEntityId,HasDocument,DocNumber,Purpose,TransferGroupId FROM #Book ORDER BY OccurredAtUtc,Id;
    SELECT h.Id,h.FinancialAccountId,h.CashAccountId,h.OperationKind,h.Amount,h.Commission,h.CashDelta,h.MaskedCustomer,h.ActorUserId,h.OccurredAtUtc,h.OperationStatus,
        h.ReversedAtUtc,h.ReversedByUserId,h.CorrectsTransferId,h.ReplacementId,a.Confirmed,r.Confirmed FROM #Headers h JOIN #Evidence a ON a.Id=h.Id AND a.Undo=0 JOIN #Evidence r ON r.Id=h.Id AND r.Undo=1 ORDER BY h.Id;
    SELECT FinancialAccountId,IsIncoming,UsedToday,UsedMonth FROM #Used;
    SELECT Id,SafeName FROM #Actors;
    SELECT @AsOf AS ReadAtUtc;
END;';
    EXEC(N'GRANT EXECUTE ON OBJECT::dbo.ReadFinanceReport TO [ProjectZClient];');
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK; THROW;
END CATCH;
PRINT N'تم تركيب تقرير المالية للقراءة.';
GO
