/* Executive P&L / cash summary. SQL Server 2016 SP1, compatibility 130.
   Install after 26-30. No backfill or changes to prior sales, postings, stock, audit or permissions of operational screens. */
USE [ProjectZ];
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
BEGIN
    RAISERROR(N'افتح نافذة إدارية جديدة خارج أي معاملة.',16,1); RETURN;
END;
SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL OR OBJECT_ID(N'dbo.ReadSalesReport',N'P') IS NULL
    OR OBJECT_ID(N'dbo.ReadPurchaseReport',N'P') IS NULL OR OBJECT_ID(N'dbo.ReadMaintenanceReport',N'P') IS NULL
    OR OBJECT_ID(N'dbo.ReadFinanceReport',N'P') IS NULL OR OBJECT_ID(N'dbo.ReadExpenseReport',N'P') IS NULL
    THROW 50270,N'جهّز الحساب المحدود والتقارير الخمسة (26 إلى 30) قبل تركيب إجمالي التقارير.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    -- DeleteDamaged legitimately removes standalone stock history. Preserve just its dated valuations
    -- transactionally for all client versions, with no FK to the deleted unit. No second stock issue.
    IF OBJECT_ID(N'dbo.OverallRemovedDamage',N'U') IS NULL EXEC(N'CREATE TABLE dbo.OverallRemovedDamage(
        MovementId int NOT NULL PRIMARY KEY, UnitId int NULL, MovementType nvarchar(40) NOT NULL, OccurredAtUtc datetime2(7) NOT NULL,
        QuantityDelta int NOT NULL, UnitCost decimal(19,2) NOT NULL); CREATE INDEX IX_OverallRemovedDamage_Unit ON dbo.OverallRemovedDamage(UnitId,OccurredAtUtc);');
    EXEC sys.sp_executesql N'CREATE OR ALTER TRIGGER dbo.PreserveRemovedDamage ON dbo.StockMovements AFTER DELETE AS
    BEGIN
        SET NOCOUNT ON;
        INSERT dbo.OverallRemovedDamage(MovementId,UnitId,MovementType,OccurredAtUtc,QuantityDelta,UnitCost)
        SELECT Id,InventoryUnitId,MovementType,OccurredAtUtc,QuantityDelta,UnitCost FROM deleted WHERE MovementType IN(N''Damage'',N''DamageRestore'');
    END;';
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.StockMovements') AND name=N'IX_StockMovements_OverallDamage')
        EXEC(N'CREATE INDEX IX_StockMovements_OverallDamage ON dbo.StockMovements(MovementType,OccurredAtUtc) INCLUDE(InventoryUnitId,QuantityDelta,UnitCost);');
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.ReadOverallReport @FromDate date, @ThroughDate date
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT<>0 OR (@@OPTIONS & 2)<>0
    BEGIN
        RAISERROR(N''افتح الملخص خارج معاملة الحفظ.'',16,1); RETURN;
    END;
    SET XACT_ABORT ON;
    DECLARE @Days int=DATEDIFF(day,@FromDate,@ThroughDate)+1;
    IF @Days IS NULL OR @Days NOT BETWEEN 1 AND 366 OR @FromDate<''00030101'' OR @ThroughDate=''99991231''
        THROW 50271,N''اختر فترة صحيحة حتى 366 يومًا ولها فترة سابقة.'',1;
    DECLARE @Previous date=DATEADD(day,-@Days,@FromDate);
    -- Compute each midnight separately. Two equal numbers of Egypt calendar days need not contain equal UTC hours.
    ;WITH days AS (SELECT @Previous AS Day UNION ALL SELECT DATEADD(day,1,Day) FROM days WHERE Day<@ThroughDate)
    SELECT Day,CONVERT(datetime2(7),CONVERT(datetime2(7),Day) AT TIME ZONE ''Egypt Standard Time'' AT TIME ZONE ''UTC'') AS StartUtc,
        CONVERT(datetime2(7),CONVERT(datetime2(7),DATEADD(day,1,Day)) AT TIME ZONE ''Egypt Standard Time'' AT TIME ZONE ''UTC'') AS UntilUtc
    INTO #Days FROM days OPTION(MAXRECURSION 732);
    DECLARE @Start datetime2(7)=(SELECT MIN(StartUtc) FROM #Days),@End datetime2(7)=(SELECT MAX(UntilUtc) FROM #Days);
    CREATE TABLE #Fact(AtUtc datetime2(7) NOT NULL, Measure smallint NOT NULL, Amount decimal(28,2) NULL);
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- MOVEMENT basis of 26: original line allocation (not the live remaining value), dated
        -- returns/replacements and cancellation snapshots. Payments are never a sales value.
        INSERT #Fact SELECT i.IssuedAtUtc,x.Measure,x.Amount FROM dbo.SalesInvoices i
        LEFT JOIN dbo.SalesInvoiceLines l ON l.SalesInvoiceId=i.Id AND l.IsExchangeReplacement=0
        LEFT JOIN dbo.vwSalesLineValuation v ON v.Id=l.Id
        CROSS APPLY(VALUES(1,v.NetLineAmount),(5,l.Quantity*l.UnitCost)) x(Measure,Amount)
        WHERE i.IssuedAtUtc>=@Start AND i.IssuedAtUtc<@End;
        INSERT #Fact SELECT r.CreatedAtUtc,x.Measure,x.Amount FROM dbo.ReturnExchanges r LEFT JOIN dbo.ReturnExchangeLines l ON l.ReturnExchangeId=r.Id
        CROSS APPLY(VALUES(CASE WHEN l.LineKind=2 THEN 3 ELSE 2 END,l.NetAmount),(5,CASE WHEN l.LineKind=2 THEN 1 ELSE -1 END*l.UnitCost*l.Quantity)) x(Measure,Amount)
        WHERE r.CreatedAtUtc>=@Start AND r.CreatedAtUtc<@End;
        INSERT #Fact SELECT a.OccurredAtUtc,x.Measure,x.Amount FROM dbo.AuditLog a
        CROSS APPLY(VALUES(TRY_CONVERT(int,a.EntityId),TRY_CONVERT(decimal(19,2),JSON_VALUE(a.OldValuesJson,''$.net'')))) k(Id,Net)
        OUTER APPLY(SELECT SUM(l.Quantity*l.UnitCost) Cost FROM dbo.SalesInvoiceLines l WHERE l.SalesInvoiceId=k.Id AND NOT EXISTS(SELECT 1 FROM dbo.ReturnExchanges r WHERE r.SalesInvoiceId=k.Id)) c
        CROSS APPLY(VALUES(4,k.Net),(5,CASE WHEN k.Net IS NOT NULL THEN -c.Cost END)) x(Measure,Amount)
        WHERE a.Module=N''Sales'' AND a.ActionName=N''CancelSale'' AND a.EntityType=N''SalesInvoice'' AND a.OccurredAtUtc>=@Start AND a.OccurredAtUtc<@End;
        INSERT #Fact SELECT PaidAtUtc,CASE PaymentKind WHEN 1 THEN 6 ELSE 7 END,Amount FROM dbo.SalesPayments WHERE PaidAtUtc>=@Start AND PaidAtUtc<@End;
        INSERT #Fact SELECT IssuedAtUtc,8,1 FROM dbo.SalesInvoices WHERE IssuedAtUtc>=@Start AND IssuedAtUtc<@End;

        -- PURCHASES: signed movement of the supplier document (27), not COGS. Cash, debt
        -- reduction, issuing/using supplier credit are independent measures, never earnings.
        INSERT #Fact SELECT a.OccurredAtUtc,CASE a.ActionName WHEN N''CreatePurchaseInvoice'' THEN 10 ELSE 11 END,
            CASE WHEN a.ActionName=N''DeletePurchaseInvoice'' THEN -TRY_CONVERT(decimal(19,2),JSON_VALUE(a.OldValuesJson,''$.NetPurchases'')) ELSE v.Delta END
        FROM dbo.AuditLog a CROSS APPLY(VALUES(TRY_CONVERT(decimal(19,2),JSON_VALUE(a.NewValuesJson,''$.DocumentDelta'')))) v(Delta)
        WHERE a.Module=N''Purchasing'' AND a.EntityType=N''PurchaseInvoice'' AND a.ActionName IN(N''CreatePurchaseInvoice'',N''UpdatePurchaseInvoice'',N''EditPurchaseInvoice'',N''DeletePurchaseInvoice'',N''CancelPurchaseInvoice'')
            AND (a.ActionName<>N''CancelPurchaseInvoice'' OR v.Delta IS NULL) AND a.OccurredAtUtc>=@Start AND a.OccurredAtUtc<@End;
        INSERT #Fact SELECT i.CreatedAtUtc,10,NULL FROM dbo.PurchaseInvoices i WHERE i.CreatedAtUtc>=@Start AND i.CreatedAtUtc<@End
            AND NOT EXISTS(SELECT 1 FROM dbo.AuditLog a WHERE a.Module=N''Purchasing'' AND a.EntityType=N''PurchaseInvoice'' AND a.ActionName=N''CreatePurchaseInvoice'' AND a.EntityId=CONVERT(nvarchar(100),i.Id));
        SELECT r.Id,r.PurchaseInvoiceId,r.ReturnedAtUtc,r.NetReduction,r.DebtReduction,c.Amount AS CreditAmount,c.SupplierId AS CreditSupplier,i.SupplierId
        INTO #Return FROM dbo.PurchaseReturns r JOIN dbo.PurchaseInvoices i ON i.Id=r.PurchaseInvoiceId LEFT JOIN dbo.SupplierCreditNotes c ON c.SourceReturnId=r.Id
        WHERE r.ReturnedAtUtc>=@Start AND r.ReturnedAtUtc<@End;
        INSERT #Fact SELECT r.ReturnedAtUtc,x.Measure,x.Amount FROM #Return r CROSS APPLY(VALUES(12,r.NetReduction),(15,r.DebtReduction),(16,ISNULL(r.CreditAmount,0))) x(Measure,Amount);
        INSERT #Fact SELECT AppliedAtUtc,17,Amount FROM dbo.SupplierCreditApplications WHERE AppliedAtUtc>=@Start AND AppliedAtUtc<@End;
        INSERT #Fact SELECT PaidAtUtc,CASE PaymentKind WHEN 1 THEN 13 ELSE 14 END,Amount FROM dbo.PurchasePayments WHERE PaidAtUtc>=@Start AND PaidAtUtc<@End;

        -- SERVICE: anchors are a delivery locked in the current header, a 24 delivery event, or
        -- the PRE-reopen closing snapshot. Never back-project today''s part list onto an earlier close.
        SELECT TRY_CONVERT(int,a.EntityId) Id,TRY_CONVERT(datetime2(7),JSON_VALUE(a.OldValuesJson,''$.DeliveredAtUtc'')) ClosedAt,
            JSON_QUERY(a.OldValuesJson,''$.Parts'') Parts INTO #OldClose FROM dbo.AuditLog a
        WHERE a.Module=N''Maintenance'' AND a.EntityType=N''MaintenanceOrder'' AND a.ActionName=N''Reopen'' AND TRY_CONVERT(int,JSON_VALUE(a.OldValuesJson,''$.OrderStatus''))=5;
        SELECT p.MaintenanceOrderId Id INTO #Tickets FROM dbo.MaintenancePayments p WHERE p.PaidAtUtc>=@Start AND p.PaidAtUtc<@End
        UNION SELECT Id FROM dbo.MaintenanceOrders WHERE OrderStatus=5 AND DeliveredAtUtc>=@Start AND DeliveredAtUtc<@End
        UNION SELECT Id FROM #OldClose WHERE ClosedAt>=@Start AND ClosedAt<@End AND Id IS NOT NULL
        UNION SELECT MaintenanceOrderId FROM dbo.MaintenanceActivities WHERE ActionTitle=N''تسليم الجهاز وإغلاق أمر الصيانة'' AND OccurredAtUtc>=@Start AND OccurredAtUtc<@End
        UNION SELECT TRY_CONVERT(int,EntityId) FROM dbo.AuditLog WHERE Module=N''Maintenance'' AND EntityType=N''MaintenanceOrder'' AND ActionName=N''Deliver'' AND OccurredAtUtc>=@Start AND OccurredAtUtc<@End;
        CREATE UNIQUE CLUSTERED INDEX TicketsId ON #Tickets(Id);
        ;WITH anchors AS (
            SELECT o.Id,o.DeliveredAtUtc AS AtUtc FROM #Tickets t JOIN dbo.MaintenanceOrders o ON o.Id=t.Id WHERE o.OrderStatus=5 AND o.DeliveredAtUtc IS NOT NULL
            UNION SELECT c.Id,c.ClosedAt FROM #OldClose c JOIN #Tickets t ON t.Id=c.Id WHERE c.ClosedAt IS NOT NULL
        ), events AS (
            SELECT t.Id,COALESCE(TRY_CONVERT(datetime2(7),JSON_VALUE(a.NewValuesJson,''$.DeliveredAtUtc'')),a.OccurredAtUtc) AtUtc FROM #Tickets t JOIN dbo.AuditLog a
                ON a.Module=N''Maintenance'' AND a.EntityType=N''MaintenanceOrder'' AND a.EntityId=CONVERT(nvarchar(100),t.Id) AND a.ActionName=N''Deliver''
        ), markers AS (
            SELECT Id,AtUtc FROM anchors UNION SELECT Id,AtUtc FROM events
            UNION SELECT t.Id,a.OccurredAtUtc FROM #Tickets t JOIN dbo.MaintenanceActivities a ON a.MaintenanceOrderId=t.Id AND a.ActionTitle=N''تسليم الجهاز وإغلاق أمر الصيانة''
                WHERE NOT EXISTS(SELECT 1 FROM anchors z WHERE z.Id=t.Id AND z.AtUtc=a.OccurredAtUtc) AND NOT EXISTS(SELECT 1 FROM events z WHERE z.Id=t.Id AND z.AtUtc=a.OccurredAtUtc)
        ) SELECT m.Id,m.AtUtc,CONVERT(decimal(28,2),CASE WHEN saved.Copies=1 AND LEFT(LTRIM(saved.Parts),1)=N''['' THEN historical.Cost
            WHEN saved.Copies=0 AND o.OrderStatus=5 AND o.DeliveredAtUtc=m.AtUtc THEN currentParts.Cost END) Cost
        INTO #Closes FROM markers m JOIN dbo.MaintenanceOrders o ON o.Id=m.Id
        OUTER APPLY(SELECT COUNT_BIG(*) Copies,MAX(c.Parts) Parts FROM #OldClose c WHERE c.Id=m.Id AND c.ClosedAt=m.AtUtc) saved
        OUTER APPLY(SELECT CASE WHEN COUNT_BIG(*)=COUNT_BIG(CASE WHEN Quantity>0 AND UnitCost>=0 THEN 1 END) THEN COALESCE(SUM(Quantity*UnitCost),0) END Cost
            FROM OPENJSON(saved.Parts) WITH(Quantity int ''$.Quantity'',UnitCost decimal(19,2) ''$.UnitCost'')) historical
        OUTER APPLY(SELECT COALESCE(SUM(l.Quantity*l.UnitCost),0) Cost FROM dbo.MaintenanceOrderParts l WHERE l.MaintenanceOrderId=o.Id) currentParts;
        CREATE UNIQUE CLUSTERED INDEX CloseTime ON #Closes(Id,AtUtc);
        ;WITH changes AS (SELECT *,LAG(Cost,1,CONVERT(decimal(28,2),0)) OVER(PARTITION BY Id ORDER BY AtUtc) PrevCost FROM #Closes)
        INSERT #Fact SELECT AtUtc,26,Cost-PrevCost FROM changes WHERE AtUtc>=@Start AND AtUtc<@End;
        INSERT #Fact SELECT AtUtc,21,1 FROM #Closes WHERE AtUtc>=@Start AND AtUtc<@End;
        INSERT #Fact SELECT ReceivedAtUtc,20,1 FROM dbo.MaintenanceOrders WHERE ReceivedAtUtc>=@Start AND ReceivedAtUtc<@End;
        INSERT #Fact SELECT PaidAtUtc,CASE WHEN PaymentKind=2 THEN 24 WHEN PaymentStage=2 THEN 23 ELSE 22 END,Amount FROM dbo.MaintenancePayments WHERE PaidAtUtc>=@Start AND PaidAtUtc<@End;
        -- Advance receipts before first delivery move into earnings ONCE; all subsequent money is
        -- dated at its own event. Reopening earns nothing. No selling price or estimate is revenue.
        ;WITH firstClose AS (SELECT Id,MIN(AtUtc) FirstAt FROM #Closes GROUP BY Id)
        INSERT #Fact SELECT x.AtUtc,25,CASE WHEN p.PaymentKind=2 THEN -p.Amount ELSE p.Amount END
        FROM firstClose c JOIN dbo.MaintenancePayments p ON p.MaintenanceOrderId=c.Id
        CROSS APPLY(VALUES(CASE WHEN p.PaidAtUtc<c.FirstAt THEN c.FirstAt ELSE p.PaidAtUtc END)) x(AtUtc)
        WHERE p.PaidAtUtc<@End AND x.AtUtc>=@Start AND x.AtUtc<@End;
        -- A delivery-stage receipt without any dated close is not proof of profit on an invented day.
        INSERT #Fact SELECT p.PaidAtUtc,25,NULL FROM dbo.MaintenancePayments p WHERE p.PaymentStage=2 AND p.PaidAtUtc>=@Start AND p.PaidAtUtc<@End
            AND NOT EXISTS(SELECT 1 FROM #Closes c WHERE c.Id=p.MaintenanceOrderId);

        -- FINANCE: actual account legs by channel, never added to revenue; EXPENSES by written
        -- date for operating result, with ledger payments/credits independently dated for liquidity.
        SELECT t.Id,t.SourceEntityId,t.SourceEntityType,t.IsIncoming,t.Amount,t.FinancialAccountId,t.OccurredAtUtc,a.AccountKind,
            CASE WHEN t.Category IN(N''عكس تحويل'',N''عكس تعديل'') THEN 1 ELSE 0 END UndoStage
        INTO #Book FROM dbo.FinancialTransactions t JOIN dbo.FinancialAccounts a ON a.Id=t.FinancialAccountId WHERE t.OccurredAtUtc>=@Start AND t.OccurredAtUtc<@End;
        INSERT #Fact SELECT OccurredAtUtc,30+2*AccountKind+CASE WHEN IsIncoming=1 THEN 0 ELSE 1 END,Amount FROM #Book;
        INSERT #Fact SELECT OccurredAtUtc,CASE WHEN IsIncoming=1 THEN 42 ELSE 41 END,Amount FROM #Book WHERE SourceEntityType=N''Expense'';
        INSERT #Fact SELECT d.StartUtc,40,e.Amount FROM #Days d JOIN dbo.Expenses e ON e.ExpenseDate=d.Day;
        -- Same signed stage proof as 29 (cash may have zero effect). Corrections have a new header.
        ;WITH ids AS (SELECT Id FROM dbo.CustomerTransfers WHERE OccurredAtUtc>=@Start AND OccurredAtUtc<@End
            UNION SELECT Id FROM dbo.CustomerTransfers WHERE ReversedAtUtc>=@Start AND ReversedAtUtc<@End
            UNION SELECT SourceEntityId FROM #Book WHERE SourceEntityType=N''CustomerTransfer'')
        , stages AS (
            SELECT h.Id,h.OccurredAtUtc,h.ReversedAtUtc,h.OperationStatus,h.Commission,h.FinancialAccountId,h.CashAccountId,h.OperationKind,h.Amount,h.CashDelta,x.Undo,
                (CASE h.OperationKind WHEN 1 THEN -h.Amount ELSE h.Amount END)*(1-2*x.Undo) WalletEffect,h.CashDelta*(1-2*x.Undo) CashEffect,
                CASE x.Undo WHEN 0 THEN h.ActorUserId ELSE h.ReversedByUserId END Actor
            FROM ids JOIN dbo.CustomerTransfers h ON h.Id=ids.Id CROSS JOIN(VALUES(0),(1)) x(Undo)
        ), proof AS (
            SELECT s.Id,s.Undo,COUNT_BIG(t.Id) AllLegs,
                COUNT_BIG(CASE WHEN t.FinancialAccountId=s.FinancialAccountId AND t.ActorUserId=s.Actor AND delta.Amount=s.WalletEffect AND (s.Undo=1 OR t.Category=CASE s.OperationKind WHEN 1 THEN N''تحويل للعميل'' ELSE N''سحب للعميل'' END) THEN 1 END) WalletLegs,
                COUNT_BIG(CASE WHEN t.FinancialAccountId=s.CashAccountId AND t.ActorUserId=s.Actor AND delta.Amount=s.CashEffect AND (s.Undo=1 OR t.Category=CASE s.OperationKind WHEN 1 THEN N''تحويل من العميل'' ELSE N''تحويل للعميل'' END) THEN 1 END) CashLegs
            FROM stages s LEFT JOIN dbo.FinancialTransactions t ON t.SourceEntityType=N''CustomerTransfer'' AND t.SourceEntityId=s.Id AND CASE WHEN t.Category=N''عكس تحويل'' THEN 1 ELSE 0 END=s.Undo
            CROSS APPLY(VALUES(CASE WHEN t.IsIncoming=1 THEN t.Amount ELSE -t.Amount END)) delta(Amount) GROUP BY s.Id,s.Undo
        ) SELECT s.Id,s.OccurredAtUtc,s.ReversedAtUtc,s.OperationStatus,s.Commission,s.Undo,
            CONVERT(bit,CASE WHEN w.AccountKind IN(1,2) AND c.AccountKind=0 AND s.CashDelta=CASE s.OperationKind WHEN 1 THEN s.Amount+s.Commission ELSE s.Commission-s.Amount END
                AND p.AllLegs=CASE WHEN s.CashDelta=0 THEN 1 ELSE 2 END AND p.WalletLegs=1 AND p.CashLegs=CASE WHEN s.CashDelta=0 THEN 0 ELSE 1 END THEN 1 ELSE 0 END) Confirmed
        INTO #Fee FROM stages s JOIN proof p ON p.Id=s.Id AND p.Undo=s.Undo JOIN dbo.FinancialAccounts w ON w.Id=s.FinancialAccountId LEFT JOIN dbo.FinancialAccounts c ON c.Id=s.CashAccountId;
        INSERT #Fact SELECT s.AtUtc,36+f.Undo,CASE WHEN f.Confirmed=1 THEN f.Commission END FROM #Fee f
        CROSS APPLY(VALUES(CASE WHEN f.Undo=1 THEN f.ReversedAtUtc ELSE f.OccurredAtUtc END)) s(AtUtc)
        WHERE (f.Undo=0 OR f.OperationStatus=1) AND s.AtUtc>=@Start AND s.AtUtc<@End;
        INSERT #Fact SELECT b.OccurredAtUtc,36+b.UndoStage,NULL FROM #Book b WHERE b.SourceEntityType=N''CustomerTransfer''
            AND NOT EXISTS(SELECT 1 FROM #Fee f WHERE f.Id=b.SourceEntityId AND f.Undo=b.UndoStage AND f.Confirmed=1 AND (f.Undo=0 OR f.OperationStatus=1));
        SELECT AccountKind,COUNT_BIG(*) Accounts,SUM(CurrentBalance) Amount INTO #Balances FROM dbo.FinancialAccounts GROUP BY AccountKind;

        -- DAMAGE: only the stock movement is a write-off. A damaged sales return already reversed
        -- COGS: charge the Damage row once, not the return line as a second loss. Deleted standalone
        -- movements preserved by the narrow archive are read at their ORIGINAL day and unit cost.
        ;WITH stock AS (
            SELECT Id,InventoryUnitId,MovementType,OccurredAtUtc,QuantityDelta,UnitCost FROM dbo.StockMovements WHERE MovementType IN(N''Damage'',N''DamageRestore'') AND OccurredAtUtc<@End
            UNION ALL SELECT MovementId,UnitId,MovementType,OccurredAtUtc,QuantityDelta,UnitCost FROM dbo.OverallRemovedDamage WHERE OccurredAtUtc<@End
        ) SELECT *,LAG(MovementType) OVER(PARTITION BY InventoryUnitId ORDER BY OccurredAtUtc,Id) PriorKind,
             LAG(UnitCost) OVER(PARTITION BY InventoryUnitId ORDER BY OccurredAtUtc,Id) PriorCost INTO #Damage FROM stock;
        CREATE INDEX DmgUnit ON #Damage(InventoryUnitId,OccurredAtUtc,Id);
        INSERT #Fact SELECT OccurredAtUtc,CASE WHEN MovementType=N''Damage'' THEN 50 ELSE 51 END,
            CASE WHEN MovementType=N''Damage'' AND QuantityDelta<0 AND (InventoryUnitId IS NULL OR (QuantityDelta=-1 AND ISNULL(PriorKind,N'''')<>N''Damage'')) THEN -CONVERT(bigint,QuantityDelta)*UnitCost
                 WHEN MovementType=N''DamageRestore'' AND InventoryUnitId IS NOT NULL AND QuantityDelta=1 AND PriorKind=N''Damage'' AND PriorCost=UnitCost THEN UnitCost END
        FROM #Damage WHERE OccurredAtUtc>=@Start AND OccurredAtUtc<@End;
        INSERT #Fact SELECT u.DamagedAtUtc,50,NULL FROM dbo.InventoryUnits u WHERE u.UnitStatus=2 AND u.DamagedAtUtc>=@Start AND u.DamagedAtUtc<@End
            AND NOT EXISTS(SELECT 1 FROM #Damage m WHERE m.InventoryUnitId=u.Id AND m.MovementType=N''Damage'');
        -- A pre-archive hard deletion erased the DATE as well as the cost. Do not move the loss to
        -- deletion day. Historical days that could contain the erased movement cannot be certified.
        DECLARE @Unlocated datetime2(7)=(SELECT MAX(a.OccurredAtUtc) FROM dbo.AuditLog a WHERE a.Module=N''Inventory'' AND a.ActionName=N''DeleteDamagedUnit'' AND a.EntityType=N''InventoryUnit''
            AND a.OccurredAtUtc>=@Start AND NOT EXISTS(SELECT 1 FROM dbo.OverallRemovedDamage h WHERE h.UnitId=TRY_CONVERT(int,a.EntityId) AND h.MovementType=N''Damage''));
        INSERT #Fact SELECT StartUtc,50,NULL FROM #Days WHERE StartUtc<=@Unlocated;
        -- Actual supplier settlement, not a ''returned to supplier'' status string. All-damaged:
        -- the full signed reduction. Mixed: only a separately provable share (including 0).
        -- Later collections/applications of the same credit must never enter this measure again.
        ;WITH lines AS (
            SELECT r.Id,l.Quantity,l.UnitPurchaseCost,n.Linked,n.Damaged,n.Proven
            FROM #Return r JOIN dbo.PurchaseReturnLines l ON l.PurchaseReturnId=r.Id
            CROSS APPLY(SELECT COUNT_BIG(*) Linked,COUNT_BIG(CASE WHEN u.WasDamaged=1 THEN 1 END) Damaged,
                  COUNT_BIG(CASE WHEN u.WasDamaged=1 AND lastMove.MovementType=N''Damage'' AND lastMove.QuantityDelta=-1 THEN 1 END) Proven
                FROM dbo.PurchaseReturnUnits u OUTER APPLY(SELECT TOP(1) m.MovementType,m.QuantityDelta FROM #Damage m WHERE m.InventoryUnitId=u.InventoryUnitId AND m.OccurredAtUtc<=r.ReturnedAtUtc ORDER BY m.OccurredAtUtc DESC,m.Id DESC) lastMove
                WHERE u.PurchaseReturnLineId=l.Id) n
        ), totals AS (SELECT Id,SUM(CONVERT(bigint,Quantity)) Qty,SUM(Linked) Linked,SUM(Damaged) Damaged,SUM(Proven) Proven,SUM(Quantity*UnitPurchaseCost) Gross,SUM(Damaged*UnitPurchaseCost) DamagedGross FROM lines GROUP BY Id)
        INSERT #Fact SELECT r.ReturnedAtUtc,52,CASE WHEN t.Linked=t.Qty AND t.Proven=t.Damaged AND r.NetReduction=r.DebtReduction+ISNULL(r.CreditAmount,0) AND (r.CreditAmount IS NULL OR r.CreditSupplier=r.SupplierId)
                THEN CASE WHEN t.Damaged=t.Qty OR r.NetReduction=0 THEN r.NetReduction WHEN r.NetReduction=t.Gross THEN t.DamagedGross END END
        FROM #Return r LEFT JOIN totals t ON t.Id=r.Id WHERE t.Qty IS NULL OR t.Linked<>t.Qty OR t.Damaged>0;

        CREATE CLUSTERED INDEX ByTime ON #Fact(AtUtc,Measure);
        SELECT d.Day,f.Measure,CONVERT(decimal(28,2),SUM(ISNULL(f.Amount,0))) KnownAmount,COUNT_BIG(*)-COUNT_BIG(f.Amount) Missing,COUNT_BIG(*) FactCount
        INTO #Daily FROM #Days d JOIN #Fact f ON f.AtUtc>=d.StartUtc AND f.AtUtc<d.UntilUtc GROUP BY d.Day,f.Measure;
        DECLARE @ReadAt datetime2(7)=SYSUTCDATETIME();
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH;
    -- Numeric aggregates only. No invoice/operation/order/account IDs or free-text columns.
    SELECT Day,Measure,KnownAmount,Missing,FactCount FROM #Daily ORDER BY Day,Measure;
    SELECT AccountKind,Accounts,Amount FROM #Balances ORDER BY AccountKind;
    SELECT @ReadAt AS ReadAtUtc;
END;';
    EXEC(N'GRANT EXECUTE ON OBJECT::dbo.ReadOverallReport TO [ProjectZClient];');
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK; THROW;
END CATCH;
PRINT N'تم تركيب إجمالي التقارير؛ مخرجات مجمّعة للقراءة فقط.';
GO
