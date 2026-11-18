/* Sales posting, SQL Server 2016 SP1+. Upgrade: stop all tills, re-run 07, then run
   this script as administrator. No historic payments, balances or movements are rewritten. */
USE [ProjectZ];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.vwSalesLineValuation', N'V') IS NULL
    THROW 50100, N'شغّل النسخة الجديدة من sql/07_CreateReportingViews.sql أولاً.', 1;
IF COL_LENGTH('dbo.ReturnExchangeLines','NetAmount') IS NULL
    ALTER TABLE dbo.ReturnExchangeLines ADD NetAmount decimal(19,2) NULL CHECK (NetAmount >= 0);
IF COL_LENGTH('dbo.StockMovements','SalesInvoiceLineId') IS NULL
    ALTER TABLE dbo.StockMovements ADD SalesInvoiceLineId int NULL REFERENCES dbo.SalesInvoiceLines(Id);
IF TYPE_ID('dbo.SaleItemSelection') IS NULL
    EXEC(N'CREATE TYPE dbo.SaleItemSelection AS TABLE (Ordinal int PRIMARY KEY, ProductId int NOT NULL, Quantity int NOT NULL, Tier tinyint NOT NULL, ExpectedPrice decimal(19,2) NOT NULL)');
IF TYPE_ID('dbo.SaleReturnSelection') IS NULL
    EXEC(N'CREATE TYPE dbo.SaleReturnSelection AS TABLE (LineId int PRIMARY KEY, Good int NOT NULL, Damaged int NOT NULL)');
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.StockMovements') AND name=N'IX_StockMovements_SaleLine')
    CREATE INDEX IX_StockMovements_SaleLine ON dbo.StockMovements(SalesInvoiceLineId, MovementType) INCLUDE (QuantityDelta, InventoryUnitId);
GO
-- Private helpers: no EXECUTE grant to ProjectZClient. Each is called under the posting transaction.
CREATE OR ALTER PROCEDURE dbo.NextSaleNumber @Type nvarchar(30), @Prefix nvarchar(5), @Number nvarchar(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Year smallint=YEAR(SYSDATETIME()), @Value bigint;
    IF NOT EXISTS (SELECT 1 FROM dbo.DocumentSequences WITH (UPDLOCK,HOLDLOCK) WHERE DocumentType=@Type AND SequenceYear=@Year)
        INSERT dbo.DocumentSequences(DocumentType,SequenceYear,LastValue) VALUES(@Type,@Year,0);
    UPDATE dbo.DocumentSequences SET @Value=LastValue=LastValue+1 WHERE DocumentType=@Type AND SequenceYear=@Year;
    SET @Number=CONCAT(@Prefix,'-',@Year,'-',REPLICATE('0',CASE WHEN @Value<10000 THEN 4-LEN(CONVERT(varchar(20),@Value)) ELSE 0 END),@Value);
END;
GO
CREATE OR ALTER PROCEDURE dbo.PostSaleMoney
    @Invoice int, @Actor int, @Cash decimal(19,2), @Vodafone decimal(19,2), @InstaPay decimal(19,2),
    @Refund decimal(19,2), @Category nvarchar(100), @Note nvarchar(1000), @Operation int=NULL, @AccountSummary nvarchar(1000)=NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Net TABLE(AccountId int PRIMARY KEY, Kind tinyint, NetPaid decimal(19,2));
    INSERT @Net SELECT p.FinancialAccountId,a.AccountKind,SUM(CASE WHEN p.PaymentKind=1 THEN p.Amount ELSE -p.Amount END)
        FROM dbo.SalesPayments p JOIN dbo.FinancialAccounts a ON a.Id=p.FinancialAccountId
        WHERE p.SalesInvoiceId=@Invoice GROUP BY p.FinancialAccountId,a.AccountKind;
    IF EXISTS (SELECT 1 FROM @Net WHERE NetPaid<0) OR @Refund>ISNULL((SELECT SUM(NetPaid) FROM @Net),0)
        THROW 50101, N'الردود لا تطابق المقبوض على حسابات هذه الفاتورة. راجع التسوية المالية.', 1;

    DECLARE @Post TABLE(AccountId int PRIMARY KEY, Kind tinyint, Amount decimal(19,2));
    IF @Refund>0
    BEGIN
        -- Preserve channel order (cash, Vodafone, InstaPay), but never take more than this exact
        -- account retained. No fallback to cash or to a replacement/default wallet.
        ;WITH caps AS (SELECT *, CONVERT(decimal(19,2),COALESCE(SUM(NetPaid) OVER (ORDER BY Kind,AccountId ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0)) AS Prior
                       FROM @Net WHERE NetPaid>0)
        INSERT @Post SELECT AccountId,Kind,CASE WHEN NetPaid < @Refund-Prior THEN NetPaid ELSE @Refund-Prior END
        FROM caps WHERE Prior<@Refund;
    END
    ELSE
    BEGIN
        INSERT @Post SELECT a.Id,p.Kind,p.Amount FROM (VALUES (0,@Cash),(1,@Vodafone),(2,@InstaPay)) p(Kind,Amount)
        CROSS APPLY (SELECT TOP (1) Id FROM dbo.FinancialAccounts WITH (UPDLOCK,HOLDLOCK)
                     WHERE AccountKind=p.Kind AND IsActive=1 ORDER BY IsDefault DESC,Id) a WHERE p.Amount>0;
        IF COALESCE((SELECT SUM(Amount) FROM @Post),0)<>@Cash+@Vodafone+@InstaPay
            THROW 50102, N'لا يوجد حساب نشط لإحدى وسائل التحصيل المحددة.', 1;
    END;
    DECLARE @Balances TABLE(AccountId int PRIMARY KEY, BalanceAfter decimal(19,2));
    UPDATE a SET CurrentBalance=a.CurrentBalance+CASE WHEN @Refund>0 THEN -p.Amount ELSE p.Amount END, UpdatedAtUtc=SYSUTCDATETIME()
        OUTPUT inserted.Id,inserted.CurrentBalance INTO @Balances
        FROM dbo.FinancialAccounts a JOIN @Post p ON p.AccountId=a.Id
        WHERE a.IsActive=1 AND (@Refund=0 OR a.CurrentBalance>=p.Amount);
    IF (SELECT COUNT(*) FROM @Balances)<>(SELECT COUNT(*) FROM @Post)
        THROW 50103, N'حساب السداد الأصلي موقوف أو رصيده لا يكفي للرد. لم تُحفظ العملية.', 1;
    INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
        SELECT @Invoice,AccountId,CASE WHEN @Refund>0 THEN 2 ELSE 1 END,Amount,SYSUTCDATETIME(),@Note,@Actor FROM @Post;
    INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,
           Counterparty,Note,ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
        SELECT p.AccountId,SYSUTCDATETIME(),CASE WHEN @Refund>0 THEN 0 ELSE 1 END,@Category,p.Amount,b.BalanceAfter,
               i.CustomerNameSnapshot,@Note,@Actor,CASE WHEN @Refund=0 AND p.Kind<>0 THEN 1 ELSE 0 END,
               CASE WHEN @Operation IS NULL THEN N'SalesInvoice' ELSE N'ReturnExchange' END,COALESCE(@Operation,@Invoice)
        FROM @Post p JOIN @Balances b ON b.AccountId=p.AccountId JOIN dbo.SalesInvoices i ON i.Id=@Invoice;
    SELECT @AccountSummary=STUFF((SELECT CONCAT(N'، ',a.Alias,N' (#',p.AccountId,N'): ',p.Amount)
        FROM @Post p JOIN dbo.FinancialAccounts a ON a.Id=p.AccountId ORDER BY p.Kind,p.AccountId
        FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,N'');
END;
GO
-- Original and replacement sales use the same catalog price and stock issue rules.
CREATE OR ALTER PROCEDURE dbo.IssueSaleLines @Invoice int, @Actor int, @Items dbo.SaleItemSelection READONLY,
    @Replacement bit, @Value decimal(19,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM @Items) OR EXISTS (SELECT 1 FROM @Items WHERE Quantity<=0 OR Tier>2 OR Ordinal<=0)
        THROW 50104, N'تحقق من أصناف البيع والكميات وفئات الأسعار.', 1;
    DECLARE @Number nvarchar(50)=(SELECT InvoiceNumber FROM dbo.SalesInvoices WHERE Id=@Invoice),
            @LineNo int=(SELECT COALESCE(MAX(LineNumber),0) FROM dbo.SalesInvoiceLines WHERE SalesInvoiceId=@Invoice),
            @Ordinal int=0, @Product int, @Qty int, @Tier tinyint, @Price decimal(19,2), @Cost decimal(19,2), @Line int,
            @Expected decimal(19,2), @Type nvarchar(40)=CASE WHEN @Replacement=1 THEN N'ExchangeOut' ELSE N'Sale' END;
    DECLARE @Claim TABLE(Id int PRIMARY KEY, Cost decimal(19,2));
    SET @Value=0;
    WHILE EXISTS (SELECT 1 FROM @Items WHERE Ordinal>@Ordinal)
    BEGIN
        SELECT TOP (1) @Ordinal=Ordinal,@Product=ProductId,@Qty=Quantity,@Tier=Tier,@Expected=ExpectedPrice
            FROM @Items WHERE Ordinal>@Ordinal ORDER BY Ordinal;
        SET @Price=NULL;
        SELECT @Price=CASE @Tier WHEN 1 THEN SpecialPrice WHEN 2 THEN WholesalePrice ELSE RetailPrice END,@Cost=CostPrice
            FROM dbo.Products WHERE Id=@Product AND IsActiveForSale=1;
        IF @Price IS NULL OR @Price<>@Expected
            THROW 50105, N'تغيّر سعر الصنف أو لم يعد متاحًا للبيع. حدّث الكتالوج وراجع السعر قبل التأكيد.', 1;
        DELETE FROM @Claim;
        INSERT @Claim SELECT TOP (@Qty) Id,PurchaseCost FROM dbo.InventoryUnits WITH (UPDLOCK,HOLDLOCK)
            WHERE ProductId=@Product AND UnitStatus=0 ORDER BY RegisteredAtUtc,Id;
        IF EXISTS (SELECT 1 FROM dbo.InventoryUnits WITH (UPDLOCK,HOLDLOCK) WHERE ProductId=@Product)
        BEGIN
            IF (SELECT COUNT(*) FROM @Claim)<>@Qty THROW 50106, N'عدد الوحدات المتاحة أقل من الكمية المطلوبة.', 1;
            SELECT @Cost=ROUND(AVG(Cost),2) FROM @Claim;
        END
        ELSE IF COALESCE((SELECT SUM(CONVERT(bigint,QuantityDelta)) FROM dbo.StockMovements WITH (UPDLOCK,HOLDLOCK) WHERE ProductId=@Product),0)<@Qty
            THROW 50106, N'رصيد الصنف الكمي لا يكفي للعملية.', 1;
        SET @LineNo+=1;
        INSERT dbo.SalesInvoiceLines(SalesInvoiceId,LineNumber,ProductId,BarcodeSnapshot,ProductNameSnapshot,CategoryNameSnapshot,
               UnitNameSnapshot,TrackSerialNumber,PriceTier,Quantity,UnitPrice,UnitCost,IsExchangeReplacement)
            SELECT @Invoice,@LineNo,p.Id,COALESCE(p.Barcode,N''),p.Name,c.Name,p.UnitName,p.TrackSerialNumber,@Tier,@Qty,@Price,@Cost,@Replacement
            FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE p.Id=@Product;
        SET @Line=CONVERT(int,SCOPE_IDENTITY());
        UPDATE u SET UnitStatus=1,SaleInvoiceNumber=@Number,SoldAtUtc=SYSUTCDATETIME() FROM dbo.InventoryUnits u JOIN @Claim c ON c.Id=u.Id;
        INSERT dbo.SalesInvoiceLineUnits(SalesInvoiceLineId,InventoryUnitId,AllocationStatus) SELECT @Line,Id,0 FROM @Claim;
        INSERT dbo.StockMovements(SalesInvoiceLineId,ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
            SELECT @Line,@Product,Id,@Type,-1,Cost,@Actor,N'SalesInvoice',@Invoice,@Number FROM @Claim
            UNION ALL SELECT @Line,@Product,NULL,@Type,-@Qty,@Cost,@Actor,N'SalesInvoice',@Invoice,@Number WHERE NOT EXISTS (SELECT 1 FROM @Claim);
        SET @Value+=@Qty*@Price;
    END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.IssueSale
    @Actor int, @Customer int, @WalkIn bit, @Tier tinyint, @Discount decimal(19,2), @Items dbo.SaleItemSelection READONLY,
    @Cash decimal(19,2), @Vodafone decimal(19,2), @InstaPay decimal(19,2), @Notes nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF @Discount IS NULL OR @Discount<0 OR @Cash<0 OR @Vodafone<0 OR @InstaPay<0 OR @Cash+@Vodafone+@InstaPay IS NULL
            THROW 50107, N'الخصم ومبالغ السداد يجب أن تكون أرقامًا غير سالبة.', 1;
        DECLARE @Number nvarchar(50), @Name nvarchar(200)=N'عميل نقدي (عام)', @Phone nvarchar(30)=N'', @Address nvarchar(500)=N'';
        IF @WalkIn=0
        BEGIN
            SELECT @Name=Name,@Phone=COALESCE(Phone,N''),@Address=Address FROM dbo.Customers WHERE Id=@Customer AND IsActive=1 AND IsWalkIn=0;
            IF @@ROWCOUNT<>1 THROW 50108, N'اختر عميلاً مسجلاً ونشطًا لهذه الفاتورة.', 1;
        END;
        EXEC dbo.NextSaleNumber N'SalesInvoice',N'INV',@Number OUTPUT;
        INSERT dbo.SalesInvoices(InvoiceNumber,CustomerId,CustomerNameSnapshot,CustomerPhoneSnapshot,CustomerAddressSnapshot,
               IsWalkInCustomer,PriceTier,IssuedAtUtc,InvoiceStatus,DiscountAmount,Notes,CashierUserId)
            VALUES(@Number,@Customer,@Name,@Phone,@Address,@WalkIn,@Tier,SYSUTCDATETIME(),1,@Discount,@Notes,@Actor);
        DECLARE @Invoice int=CONVERT(int,SCOPE_IDENTITY()), @Gross decimal(19,2), @Net decimal(19,2), @Paid decimal(19,2);
        EXEC dbo.IssueSaleLines @Invoice,@Actor,@Items,0,@Gross OUTPUT;
        SET @Net=@Gross-@Discount;
        IF @Net<=0 OR @Cash+@Vodafone+@InstaPay<=0 OR (@WalkIn=1 AND @Cash+@Vodafone+@InstaPay<@Net)
            THROW 50109, N'تحقق من الخصم والسداد. العميل النقدي يتطلب سداد كامل الفاتورة.', 1;
        -- Preserve POS change handling: only the applied part of each tender enters the ledger.
        IF @Cash>@Net SET @Cash=@Net;
        IF @Vodafone>@Net-@Cash SET @Vodafone=@Net-@Cash;
        IF @InstaPay>@Net-@Cash-@Vodafone SET @InstaPay=@Net-@Cash-@Vodafone;
        SET @Paid=@Cash+@Vodafone+@InstaPay;
        INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,IsActive,IsDefault,Notes)
            SELECT k.Kind,k.Alias,k.Identifier,1,1,N'أنشئت تلقائيًا لتحصيل المبيعات'
            FROM (VALUES (0,N'الخزنة الرئيسية',N''),(1,N'فودافون كاش',N'غير محدد'),(2,N'انستاباي',N'غير محدد')) k(Kind,Alias,Identifier)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.FinancialAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountKind=k.Kind);
        EXEC dbo.PostSaleMoney @Invoice,@Actor,@Cash,@Vodafone,@InstaPay,0,N'تحصيل مبيعات',@Number;
        IF @Paid=@Net UPDATE dbo.SalesInvoices SET InvoiceStatus=0 WHERE Id=@Invoice;
        INSERT dbo.SalesInvoiceActivities(SalesInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
            VALUES(@Invoice,SYSUTCDATETIME(),N'إصدار فاتورة من نقطة البيع',CONCAT(N'المتبقي: ',@Net-@Paid),@Paid,@Actor);
        INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
            VALUES(@Actor,N'Sales',N'CreateSale',N'SalesInvoice',@Invoice,(SELECT @Net AS net,@Paid AS collected FOR JSON PATH,WITHOUT_ARRAY_WRAPPER));
        COMMIT;
        SELECT @Invoice,@Number,@Net,@Paid;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
-- All repeatable postings on a saved invoice start here, in the caller's transaction.
CREATE OR ALTER PROCEDURE dbo.LockSaleForSettlement
    @Invoice int, @ExpectedVersion binary(8), @Status tinyint OUTPUT, @WalkIn bit OUTPUT,
    @Number nvarchar(50) OUTPUT, @Net decimal(19,2) OUTPUT, @Paid decimal(19,2) OUTPUT, @Due decimal(19,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Discount decimal(19,2);
    SELECT @Status=InvoiceStatus,@WalkIn=IsWalkInCustomer,@Number=InvoiceNumber,@Discount=DiscountAmount
        FROM dbo.SalesInvoices WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Invoice AND RowVersion=@ExpectedVersion;
    IF @@ROWCOUNT<>1 THROW 50110, N'تغيرت الفاتورة أو لم تعد موجودة. حدّث الفواتير وأعد فتح العملية.', 1;
    SELECT @Net=NetSales,@Paid=NetCollected,@Due=RemainingAmount FROM dbo.vwSalesInvoiceSummary WHERE SalesInvoiceId=@Invoice;
    IF @Status NOT IN (3,4) AND (@Paid<0 OR @Due<0 OR (@Discount>0 AND EXISTS (
        SELECT 1 FROM dbo.SalesInvoiceLines l WHERE l.SalesInvoiceId=@Invoice AND l.IsExchangeReplacement=0
        AND l.ReturnedQuantity>(SELECT COALESCE(SUM(r.Quantity),0) FROM dbo.ReturnExchangeLines r
            WHERE r.SalesInvoiceLineId=l.Id AND r.LineKind=1 AND r.NetAmount IS NOT NULL))))
        THROW 50115, N'تحتاج الفاتورة إلى مطابقة مالية: رصيد غير متسق أو مرتجع قديم بلا توزيع خصم موثّق. لم تُحفظ العملية.', 1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CollectSalePayment
    @Invoice int, @ExpectedVersion binary(8), @Actor int, @Cash decimal(19,2), @Vodafone decimal(19,2), @InstaPay decimal(19,2), @Note nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Status tinyint,@WalkIn bit,@Number nvarchar(50),@Net decimal(19,2),@Paid decimal(19,2),@Remaining decimal(19,2);
        EXEC dbo.LockSaleForSettlement @Invoice,@ExpectedVersion,@Status OUTPUT,@WalkIn OUTPUT,@Number OUTPUT,@Net OUTPUT,@Paid OUTPUT,@Remaining OUTPUT;
        IF @Status NOT IN (1,2,5) THROW 50111, N'لا يمكن التحصيل على فاتورة مسددة أو ملغاة أو مرتجعة بالكامل.', 1;
        DECLARE @Total decimal(19,2)=@Cash+@Vodafone+@InstaPay;
        IF @Total IS NULL OR @Total<=0 OR @Cash<0 OR @Vodafone<0 OR @InstaPay<0 OR @Total>@Remaining
            THROW 50112, N'مبلغ التحصيل غير صحيح أو يتجاوز المتبقي الفعلي في قاعدة البيانات.', 1;
        EXEC dbo.PostSaleMoney @Invoice,@Actor,@Cash,@Vodafone,@InstaPay,0,N'تحصيل مبيعات',@Note;
        UPDATE dbo.SalesInvoices SET InvoiceStatus=CASE WHEN @Status=1 AND @Remaining=@Total THEN 0 ELSE @Status END,
               UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Invoice;
        INSERT dbo.SalesInvoiceActivities(SalesInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
            VALUES(@Invoice,SYSUTCDATETIME(),N'تحصيل دفعة متبقية',CONCAT(N'المتبقي: ',@Remaining-@Total,N' · ',@Note),@Total,@Actor);
        INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,OldValuesJson,NewValuesJson)
            VALUES(@Actor,N'Sales',N'CollectSalePayment',N'SalesInvoice',@Invoice,JSON_MODIFY(N'{}',N'$.remaining',@Remaining),JSON_MODIFY(N'{}',N'$.remaining',@Remaining-@Total));
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
-- Mode 0: return, 1: exchange, 2: cancel. The client specifies identities, quantities and
-- tender only. The same locked invoice supplies the stock, valuation and payment caps.
CREATE OR ALTER PROCEDURE dbo.AdjustSale
    @Invoice int, @ExpectedVersion binary(8), @Actor int, @Mode tinyint, @Reason nvarchar(1000),
    @Returns dbo.SaleReturnSelection READONLY, @Items dbo.SaleItemSelection READONLY,
    @Cash decimal(19,2), @Vodafone decimal(19,2), @InstaPay decimal(19,2)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Status tinyint,@WalkIn bit,@InvoiceNumber nvarchar(50),@Before decimal(19,2),@Paid decimal(19,2),@Debt decimal(19,2);
        EXEC dbo.LockSaleForSettlement @Invoice,@ExpectedVersion,@Status OUTPUT,@WalkIn OUTPUT,@InvoiceNumber OUTPUT,@Before OUTPUT,@Paid OUTPUT,@Debt OUTPUT;
        IF @Mode IS NULL OR @Mode>2 OR @Status IN (3,4) OR (@Mode=2 AND @Status NOT IN (0,1))
            THROW 50113, N'حالة الفاتورة لا تسمح بهذه العملية.', 1;
        IF (@Mode<>1 AND EXISTS (SELECT 1 FROM @Items)) OR (@Mode=2 AND EXISTS (SELECT 1 FROM @Returns))
           OR (@Mode<>2 AND (NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL OR NOT EXISTS (SELECT 1 FROM @Returns)))
            THROW 50114, N'تحقق من بنود العملية وسبب المرتجع أو الاستبدال.', 1;

        DECLARE @Lines TABLE(Id int PRIMARY KEY, ProductId int, Name nvarchar(250), Qty int, WasReturned int,
            Good int, Damaged int, Price decimal(19,2), Cost decimal(19,2), NetBasis decimal(19,2), HasUnits bit);
        INSERT @Lines
            SELECT l.Id,l.ProductId,l.ProductNameSnapshot,l.Quantity,l.ReturnedQuantity,
                   CASE WHEN @Mode=2 THEN l.Quantity-l.ReturnedQuantity ELSE COALESCE(r.Good,0) END,COALESCE(r.Damaged,0),
                   l.UnitPrice,l.UnitCost,v.NetLineAmount,CASE WHEN EXISTS (SELECT 1 FROM dbo.SalesInvoiceLineUnits lu WHERE lu.SalesInvoiceLineId=l.Id) THEN 1 ELSE 0 END
            FROM dbo.SalesInvoiceLines l JOIN dbo.vwSalesLineValuation v ON v.Id=l.Id LEFT JOIN @Returns r ON r.LineId=l.Id
            WHERE l.SalesInvoiceId=@Invoice;
        IF NOT EXISTS (SELECT 1 FROM @Lines) OR EXISTS (SELECT 1 FROM @Returns r LEFT JOIN @Lines l ON l.Id=r.LineId
                WHERE l.Id IS NULL OR r.Good<0 OR r.Damaged<0 OR CONVERT(bigint,r.Good)+r.Damaged NOT BETWEEN 1 AND l.Qty-l.WasReturned)
            THROW 50116, N'بند المرتجع لا يتبع الفاتورة أو الكمية تتجاوز المتاح للإرجاع.', 1;
        IF @Mode=2 AND (EXISTS (SELECT 1 FROM @Lines WHERE WasReturned>0) OR EXISTS (SELECT 1 FROM dbo.ReturnExchanges WHERE SalesInvoiceId=@Invoice))
            THROW 50113, N'لا يجوز إلغاء فاتورة لها مرتجع أو استبدال سابق.', 1;

        -- Never convert a quantity-only return to fictitious serialized stock if the
        -- product has since been moved onto unit tracking. Its identity needs reconciliation.
        IF EXISTS (SELECT 1 FROM @Lines l WHERE HasUnits=0 AND Good>0 AND EXISTS
                   (SELECT 1 FROM dbo.InventoryUnits WHERE ProductId=l.ProductId))
            THROW 50117, N'تغيّر تتبع الصنف من كمي إلى وحدات مسجلة. طابِق المخزون قبل الإرجاع.', 1;
        -- New quantity issues have a line FK. Legacy issues only had invoice + product:
        -- require the complete movement-only balance for that product to reconcile first.
        IF EXISTS (SELECT 1 FROM @Lines l WHERE l.HasUnits=0 GROUP BY l.ProductId
            HAVING SUM(CONVERT(bigint,l.Good)+l.Damaged)>0 AND SUM(CONVERT(bigint,l.Qty)-l.WasReturned)<>
                COALESCE((SELECT -SUM(CONVERT(bigint,m.QuantityDelta)) FROM dbo.StockMovements m
                    WHERE m.ProductId=l.ProductId AND m.InventoryUnitId IS NULL AND m.SourceEntityId=@Invoice
                    AND ((m.SourceEntityType=N'SalesInvoice' AND m.MovementType IN (N'Sale',N'ExchangeOut',N'Return',N'Cancel'))
                      OR (m.SourceEntityType=N'ReturnExchange' AND m.MovementType=N'ExchangeOut'))),0))
            THROW 50117, N'حركات مخزون الصنف لا تطابق كميات البيع. راجع المخزون قبل المتابعة.', 1;
        IF EXISTS (SELECT 1 FROM @Lines l WHERE l.HasUnits=0 AND l.Good+l.Damaged>0
            AND EXISTS (SELECT 1 FROM dbo.StockMovements WHERE SalesInvoiceLineId=l.Id AND MovementType IN (N'Sale',N'ExchangeOut'))
            AND l.Qty-l.WasReturned<>(SELECT -SUM(CONVERT(bigint,QuantityDelta)) FROM dbo.StockMovements
                WHERE SalesInvoiceLineId=l.Id AND InventoryUnitId IS NULL AND MovementType IN (N'Sale',N'ExchangeOut',N'Return',N'Cancel')))
            THROW 50117, N'حركات مخزون البند لا تطابق كمية البيع.', 1;

        DECLARE @Groups TABLE(LineId int, Cond tinyint, Qty int, NetAmount decimal(19,2), PRIMARY KEY(LineId,Cond));
        INSERT @Groups SELECT l.Id,g.Cond,g.Qty,dbo.SaleShare(l.NetBasis,g.StartQty+g.Qty,l.Qty)-dbo.SaleShare(l.NetBasis,g.StartQty,l.Qty)
            FROM @Lines l CROSS APPLY (VALUES (0,l.Good,l.WasReturned),(1,l.Damaged,l.WasReturned+l.Good)) g(Cond,Qty,StartQty) WHERE g.Qty>0;
        DECLARE @Returned decimal(19,2)=(SELECT SUM(NetAmount) FROM @Groups), @Replacement decimal(19,2)=0;

        DECLARE @Units TABLE(UnitId int PRIMARY KEY, LineId int, Cost decimal(19,2), Position bigint);
        INSERT @Units SELECT u.Id,l.Id,u.PurchaseCost,ROW_NUMBER() OVER (PARTITION BY l.Id ORDER BY u.Id)
            FROM @Lines l JOIN dbo.SalesInvoiceLineUnits lu ON lu.SalesInvoiceLineId=l.Id AND lu.AllocationStatus=0
            JOIN dbo.InventoryUnits u WITH (UPDLOCK,HOLDLOCK) ON u.Id=lu.InventoryUnitId AND u.ProductId=l.ProductId AND u.UnitStatus=1
            WHERE l.Good+l.Damaged>0;
        IF EXISTS (SELECT 1 FROM @Lines l WHERE l.HasUnits=1 AND l.Good+l.Damaged>0 AND
                   l.Qty-l.WasReturned<>(SELECT COUNT(*) FROM @Units u WHERE u.LineId=l.Id))
            THROW 50117, N'الوحدات المباعة المرتبطة بالبند لا تطابق الكمية المتبقية.', 1;
        DELETE u FROM @Units u JOIN @Lines l ON l.Id=u.LineId WHERE u.Position>l.Good+l.Damaged;
        UPDATE lu SET AllocationStatus=1 FROM dbo.SalesInvoiceLineUnits lu JOIN @Units x ON x.UnitId=lu.InventoryUnitId AND x.LineId=lu.SalesInvoiceLineId;
        UPDATE u SET UnitStatus=CASE WHEN x.Position<=l.Good THEN 0 ELSE 2 END,SaleInvoiceNumber=N'',SoldAtUtc=NULL,
               DamagedAtUtc=CASE WHEN x.Position>l.Good THEN SYSUTCDATETIME() END,
               DamageReason=CASE WHEN x.Position>l.Good THEN @Reason ELSE N'' END,
               DamageSettlementStatus=CASE WHEN x.Position>l.Good THEN N'قيد المراجعة' ELSE N'' END
            FROM dbo.InventoryUnits u JOIN @Units x ON x.UnitId=u.Id JOIN @Lines l ON l.Id=x.LineId;
        -- A damaged return enters then leaves sellable stock: net zero, not a second sale issue.
        DECLARE @InType nvarchar(40)=CASE WHEN @Mode=2 THEN N'Cancel' ELSE N'Return' END;
        INSERT dbo.StockMovements(SalesInvoiceLineId,ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
            SELECT l.Id,l.ProductId,u.UnitId,m.Kind,m.Qty,u.Cost,@Actor,N'SalesInvoice',@Invoice,@Reason
            FROM @Units u JOIN @Lines l ON l.Id=u.LineId CROSS APPLY (VALUES (@InType,1),(N'Damage',CASE WHEN u.Position>l.Good THEN -1 ELSE 0 END)) m(Kind,Qty) WHERE m.Qty<>0
            UNION ALL SELECT l.Id,l.ProductId,NULL,m.Kind,m.Qty,l.Cost,@Actor,N'SalesInvoice',@Invoice,@Reason
            FROM @Lines l CROSS APPLY (VALUES (@InType,l.Good+l.Damaged),(N'Damage',-l.Damaged)) m(Kind,Qty) WHERE l.HasUnits=0 AND m.Qty<>0;
        UPDATE sl SET ReturnedQuantity=l.WasReturned+l.Good+l.Damaged FROM dbo.SalesInvoiceLines sl JOIN @Lines l ON l.Id=sl.Id WHERE l.Good+l.Damaged>0;

        DECLARE @LastLine int=(SELECT MAX(LineNumber) FROM dbo.SalesInvoiceLines WHERE SalesInvoiceId=@Invoice);
        IF @Mode=1 EXEC dbo.IssueSaleLines @Invoice,@Actor,@Items,1,@Replacement OUTPUT;
        DECLARE @Diff decimal(19,2)=@Replacement-@Returned, @Receipt decimal(19,2)=@Cash+@Vodafone+@InstaPay;
        IF @Receipt IS NULL OR @Cash<0 OR @Vodafone<0 OR @InstaPay<0 OR @Receipt>CASE WHEN @Diff>0 THEN @Diff ELSE 0 END OR (@WalkIn=1 AND @Receipt<@Diff)
            THROW 50118, N'مبلغ التحصيل لا يطابق فرق الاستبدال. يجب سداد فرق العميل النقدي بالكامل.', 1;
        DECLARE @Refund decimal(19,2)=CASE WHEN -@Diff>@Debt THEN -@Diff-@Debt ELSE 0 END;
        DECLARE @After decimal(19,2)=@Before+@Diff, @NewDebt decimal(19,2)=@Debt+@Diff+@Refund-@Receipt;
        DECLARE @Summary nvarchar(1500)=CONCAT(N'تخفيض الدين: ',CASE WHEN @Diff<0 THEN -@Diff-@Refund ELSE 0 END,
            N' · رد من حسابات السداد الأصلية: ',@Refund,N' · تحصيل: ',@Receipt,N' · المتبقي: ',@NewDebt,N' ج.م');
        DECLARE @Operation int=NULL, @Number nvarchar(50)=@InvoiceNumber, @Prefix nvarchar(5)=CASE WHEN @Mode=1 THEN N'EXC' ELSE N'RET' END;
        IF @Mode<>2
        BEGIN
            EXEC dbo.NextSaleNumber N'ReturnExchange',@Prefix,@Number OUTPUT;
            INSERT dbo.ReturnExchanges(OperationNumber,SalesInvoiceId,OperationType,CreatedAtUtc,ActorUserId,Reason,SettlementSummary)
                VALUES(@Number,@Invoice,@Mode,SYSUTCDATETIME(),@Actor,@Reason,@Summary);
            SET @Operation=CONVERT(int,SCOPE_IDENTITY());
            DECLARE @Map TABLE(LineId int,Cond tinyint,Id int,PRIMARY KEY(LineId,Cond));
            INSERT dbo.ReturnExchangeLines(ReturnExchangeId,LineKind,SalesInvoiceLineId,ProductId,ProductNameSnapshot,Quantity,UnitPrice,UnitCost,UnitCondition,NetAmount)
                OUTPUT inserted.SalesInvoiceLineId,inserted.UnitCondition,inserted.Id INTO @Map
                SELECT @Operation,1,l.Id,l.ProductId,l.Name,g.Qty,l.Price,l.Cost,g.Cond,g.NetAmount FROM @Groups g JOIN @Lines l ON l.Id=g.LineId;
            INSERT dbo.ReturnExchangeUnits(ReturnExchangeLineId,InventoryUnitId)
                SELECT m.Id,u.UnitId FROM @Units u JOIN @Lines l ON l.Id=u.LineId JOIN @Map m ON m.LineId=l.Id AND m.Cond=CASE WHEN u.Position>l.Good THEN 1 ELSE 0 END;
            INSERT dbo.ReturnExchangeLines(ReturnExchangeId,LineKind,SalesInvoiceLineId,ProductId,ProductNameSnapshot,Quantity,UnitPrice,UnitCost,UnitCondition,NetAmount)
                SELECT @Operation,2,NULL,ProductId,ProductNameSnapshot,Quantity,UnitPrice,UnitCost,0,Quantity*UnitPrice FROM dbo.SalesInvoiceLines
                WHERE SalesInvoiceId=@Invoice AND LineNumber>@LastLine;
        END;
        DECLARE @Category nvarchar(100)=CASE @Mode WHEN 2 THEN N'رد فاتورة ملغاة' WHEN 0 THEN N'رد مرتجع مبيعات' ELSE N'فرق استبدال' END;
        DECLARE @AccountSummary nvarchar(1000);
        EXEC dbo.PostSaleMoney @Invoice,@Actor,@Cash,@Vodafone,@InstaPay,@Refund,@Category,@Number,@Operation,@AccountSummary OUTPUT;
        IF @AccountSummary IS NOT NULL
        BEGIN
            SET @Summary=CONCAT(@Summary,N' · ',@AccountSummary);
            UPDATE dbo.ReturnExchanges SET SettlementSummary=@Summary WHERE Id=@Operation;
        END;
        DECLARE @NewStatus tinyint=CASE WHEN @Mode=2 THEN 4 WHEN @Mode=1 THEN 5 WHEN EXISTS (SELECT 1 FROM @Lines WHERE Qty>WasReturned+Good+Damaged) THEN 2 ELSE 3 END;
        UPDATE dbo.SalesInvoices SET InvoiceStatus=@NewStatus,UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Invoice AND RowVersion=@ExpectedVersion;
        IF @@ROWCOUNT<>1 THROW 50110, N'تغيرت الفاتورة. حدّث الفواتير وأعد المحاولة.', 1;
        DECLARE @Title nvarchar(200)=CASE @NewStatus WHEN 4 THEN N'إلغاء الفاتورة بالكامل' WHEN 5 THEN N'عملية استبدال' WHEN 3 THEN N'مرتجع كلي' ELSE N'مرتجع جزئي' END;
        INSERT dbo.SalesInvoiceActivities(SalesInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
            VALUES(@Invoice,SYSUTCDATETIME(),@Title,CONCAT(@Number,N' · راجع: ',@Returned,N' · بديل: ',@Replacement,N' · ',@Summary),
                   CASE WHEN @Mode=2 THEN @Refund WHEN @Replacement>@Returned THEN @Replacement ELSE @Returned END,@Actor);
        INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,OldValuesJson,NewValuesJson)
            VALUES(@Actor,N'Sales',CASE @Mode WHEN 2 THEN N'CancelSale' WHEN 1 THEN N'CreateExchange' ELSE N'CreateReturn' END,N'SalesInvoice',@Invoice,
                   (SELECT @Status AS status,@Before AS net,@Debt AS remaining FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
                   (SELECT @Number AS document,@NewStatus AS status,@After AS net,@NewDebt AS remaining,@Refund AS refunded,@Receipt AS collected FOR JSON PATH,WITHOUT_ARRAY_WRAPPER));
        COMMIT;
        SELECT @Number AS OperationNumber,@Summary AS Settlement;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.UpdateSaleNotes @Invoice int,@ExpectedVersion binary(8),@Actor int,@Notes nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.SalesInvoices SET Notes=@Notes,UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Invoice AND RowVersion=@ExpectedVersion;
        IF @@ROWCOUNT<>1 THROW 50110, N'تغيرت الفاتورة. حدّث الفواتير وأعد المحاولة.', 1;
        INSERT dbo.SalesInvoiceActivities(SalesInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
            VALUES(@Invoice,SYSUTCDATETIME(),N'تحديث ملاحظات الفاتورة',@Notes,0,@Actor);
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
GRANT EXECUTE ON OBJECT::dbo.UpdateSaleNotes TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.vwSalesLineValuation TO [ProjectZClient];
GRANT EXECUTE, REFERENCES ON TYPE::dbo.SaleItemSelection TO [ProjectZClient];
GRANT EXECUTE, REFERENCES ON TYPE::dbo.SaleReturnSelection TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.IssueSale TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.CollectSalePayment TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.AdjustSale TO [ProjectZClient];
-- The shared client can still post other modules' stock/finance entries. For these sale
-- documents do not leave a direct payment/refund/status write path alongside the procedures.
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SalesInvoiceActivities TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SalesPayments TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SalesInvoiceLines TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SalesInvoiceLineUnits TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.ReturnExchanges TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.ReturnExchangeLines TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.ReturnExchangeUnits TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SalesInvoices TO [ProjectZClient];
GO
