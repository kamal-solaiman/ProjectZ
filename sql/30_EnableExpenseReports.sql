/* SQL Server 2016 SP1+, compatibility 130. Install after the expense register and split payments
   (19/20/21). Independent of the other report installers. No backfill, balance updates or new writes. */
USE [ProjectZ];
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
BEGIN
    RAISERROR(N'افتح نافذة إدارية جديدة خارج أي معاملة.',16,1); RETURN;
END;
SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL OR OBJECT_ID(N'dbo.ExpenseCategories',N'U') IS NULL
    OR COL_LENGTH(N'dbo.Expenses',N'ExpenseDate') IS NULL OR COL_LENGTH(N'dbo.Expenses',N'FinancialAccountId') IS NOT NULL
    OR COL_LENGTH(N'dbo.ExpensePayments',N'FinancialAccountId') IS NULL OR COL_LENGTH(N'dbo.FinancialTransactions',N'SourceEntityId') IS NULL
    THROW 50268,N'أكمل تجهيز الحساب المحدود 04 وجداول المصروفات 19 و20 وترقية الدفع المقسّم 21 أولًا.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FinancialTransactions') AND name=N'IX_FinancialTransactions_SourceDate')
        EXEC(N'CREATE INDEX IX_FinancialTransactions_SourceDate ON dbo.FinancialTransactions(SourceEntityType,OccurredAtUtc) INCLUDE(SourceEntityId);');
    -- Same lookup index as installer 23 when wallet corrections are installed, never a duplicate.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FinancialTransactions') AND name=N'IX_FinancialTransactions_SourceAccount')
        EXEC(N'CREATE INDEX IX_FinancialTransactions_SourceAccount ON dbo.FinancialTransactions(SourceEntityType,SourceEntityId,FinancialAccountId,Id) INCLUDE(IsIncoming,Amount,Category);');
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.ReadExpenseReport
    @FromDate date, @ThroughDate date, @PreviousStartUtc datetime2(7), @UntilUtc datetime2(7)
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT <> 0 OR (@@OPTIONS & 2) <> 0
    BEGIN
        RAISERROR(N''حمّل التقرير خارج معاملة تسجيل مصروف.'',16,1); RETURN;
    END;
    SET XACT_ABORT ON;
    DECLARE @Days int = DATEDIFF(day,@FromDate,@ThroughDate)+1;
    IF @Days IS NULL OR @Days NOT BETWEEN 1 AND 366 OR @FromDate < ''00020102'' OR @ThroughDate = ''99991231''
        THROW 50269,N''اختر فترة صحيحة لها فترة سابقة مماثلة، بحد أقصى 366 يومًا.'',1;
    DECLARE @PreviousDate date = DATEADD(day,-@Days,@FromDate);
    -- The application uses Egypt business midnights (including DST) for both periods. Bound the
    -- UTC envelope to those same dates even when someone calls the endpoint outside the app.
    IF @PreviousStartUtc IS NULL OR @UntilUtc IS NULL OR @PreviousStartUtc >= @UntilUtc
        OR ABS(DATEDIFF_BIG(minute,@PreviousDate,@PreviousStartUtc))>840
        OR ABS(DATEDIFF_BIG(minute,DATEADD(day,1,@ThroughDate),@UntilUtc))>840
        THROW 50269,N''حدود توقيت مصر لا تتفق مع تاريخي التقرير.'',1;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- Either the written day or the posting instant can select a source. Do not inner join
        -- postings to documents here: a missing source is real cash to review, not zero cash.
        SELECT e.Id INTO #Sources FROM dbo.Expenses e WHERE e.ExpenseDate BETWEEN @PreviousDate AND @ThroughDate
        UNION SELECT t.SourceEntityId FROM dbo.FinancialTransactions t WHERE t.SourceEntityType=N''Expense''
            AND t.OccurredAtUtc>=@PreviousStartUtc AND t.OccurredAtUtc<@UntilUtc;
        CREATE UNIQUE CLUSTERED INDEX SourceId ON #Sources(Id);

        SELECT e.Id, CASE WHEN e.ExpenseNumber COLLATE Latin1_General_100_BIN2 LIKE N''EXP-[0-9][0-9][0-9][0-9]-[0-9]%''
                 AND LEN(e.ExpenseNumber) BETWEEN 10 AND 19 AND SUBSTRING(e.ExpenseNumber,10,50) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9]%''
                 THEN e.ExpenseNumber ELSE CONCAT(N''مصروف #'',e.Id) END AS Reference,
            e.ExpenseDate,e.Amount,e.ExpenseCategoryId,
            CASE WHEN c.Name COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9٠-٩۰-۹@+]%'' THEN N''تصنيف'' ELSE ISNULL(c.Name,N''تصنيف'') END AS SafeCategory,
            e.ActorUserId,e.CreatedAtUtc
        INTO #Documents FROM #Sources s JOIN dbo.Expenses e ON e.Id=s.Id LEFT JOIN dbo.ExpenseCategories c ON c.Id=e.ExpenseCategoryId;
        SELECT p.Id,p.ExpenseId,p.FinancialAccountId,p.Amount INTO #Shares
            FROM #Documents d JOIN dbo.ExpensePayments p ON p.ExpenseId=d.Id;
        -- All lifetime legs for selected documents, not just those inside the dates: evidence for
        -- matching a backdated expense. Only in-window legs feed the dated cash analysis.
        SELECT t.Id,t.SourceEntityId,t.FinancialAccountId,t.OccurredAtUtc,t.IsIncoming,t.Amount,t.BalanceAfter,t.ActorUserId
        INTO #Postings FROM #Sources s JOIN dbo.FinancialTransactions t ON t.SourceEntityType=N''Expense'' AND t.SourceEntityId=s.Id;

        ;WITH ids AS (SELECT FinancialAccountId FROM #Shares UNION SELECT FinancialAccountId FROM #Postings)
        SELECT a.Id,a.AccountKind,CASE WHEN a.Alias COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9٠-٩۰-۹@+]%''
              OR NULLIF(ident.Value,N'''') IS NOT NULL AND CHARINDEX(ident.Value,a.Alias)>0 THEN N''حساب'' ELSE a.Alias END AS SafeAlias,a.IsActive
        INTO #Accounts FROM ids JOIN dbo.FinancialAccounts a ON a.Id=ids.FinancialAccountId
            CROSS APPLY (VALUES(LTRIM(RTRIM(a.Identifier)))) ident(Value);
        ;WITH ids AS (SELECT ActorUserId FROM #Documents UNION SELECT ActorUserId FROM #Postings)
        SELECT u.Id,CASE WHEN u.DisplayName COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9٠-٩۰-۹@+]%'' THEN N''مستخدم'' ELSE u.DisplayName END AS SafeName
        INTO #Actors FROM ids JOIN dbo.Users u ON u.Id=ids.ActorUserId;
        DECLARE @ReadAtUtc datetime2(7)=SYSUTCDATETIME();
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH;
    -- No free-text expense description, posting note, counterparty or payment identifier is returned.
    SELECT Id,Reference,ExpenseDate,Amount,ExpenseCategoryId,SafeCategory,ActorUserId,CreatedAtUtc FROM #Documents ORDER BY ExpenseDate,Id;
    SELECT Id,ExpenseId,FinancialAccountId,Amount FROM #Shares ORDER BY ExpenseId,Id;
    SELECT Id,SourceEntityId,FinancialAccountId,OccurredAtUtc,IsIncoming,Amount,BalanceAfter,ActorUserId FROM #Postings ORDER BY OccurredAtUtc,Id;
    SELECT Id,AccountKind,SafeAlias,IsActive FROM #Accounts;
    SELECT Id,SafeName FROM #Actors;
    SELECT @ReadAtUtc AS ReadAtUtc;
END;';
    EXEC(N'GRANT EXECUTE ON OBJECT::dbo.ReadExpenseReport TO [ProjectZClient];');
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK; THROW;
END CATCH;
PRINT N'تم تركيب تقرير المصروفات للقراءة دون تغيير المستندات أو القيود.';
GO
