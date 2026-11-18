/* SQL Server 2016 SP1+. Stop tills; back up; run updated 07, 12 then this file as DBA (stop on error).
   No opening entitlement is inferred from a historic return, payment or activity string. */
USE [ProjectZ];
GO
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
GO
IF OBJECT_ID(N'dbo.vwPurchaseDocumentValue',N'V') IS NULL OR DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50250,N'شغّل مخطط المشتريات والتقارير والصلاحيات المحدثة (06، 07، 12) أولًا.',1;
IF IS_ROLEMEMBER(N'db_owner',N'ProjectZClient')=1 OR IS_SRVROLEMEMBER(N'dbcreator',N'ProjectZClient')=1 OR IS_SRVROLEMEMBER(N'sysadmin',N'ProjectZClient')=1
    THROW 50250,N'افصل حساب النسخ الاحتياطي الإداري عن اتصال الكاشير أولًا.',1;
IF TYPE_ID(N'dbo.PurchaseItemSelection') IS NULL EXEC(N'CREATE TYPE dbo.PurchaseItemSelection AS TABLE(Ordinal int PRIMARY KEY, LineId int NULL, ProductId int NOT NULL, Quantity int NOT NULL, Cost decimal(19,2) NOT NULL)');
IF TYPE_ID(N'dbo.PurchaseReturnSelection') IS NULL EXEC(N'CREATE TYPE dbo.PurchaseReturnSelection AS TABLE(LineId int PRIMARY KEY, Quantity int NOT NULL)');
IF COL_LENGTH(N'dbo.PurchaseReturns',N'NetReduction') IS NULL
    ALTER TABLE dbo.PurchaseReturns ADD NetReduction decimal(19,2) NULL, DebtReduction decimal(19,2) NULL;
IF COL_LENGTH(N'dbo.PurchaseInvoiceActivities',N'DocumentDelta') IS NULL
    ALTER TABLE dbo.PurchaseInvoiceActivities ADD DocumentDelta decimal(19,2) NULL;
-- A withdrawn unit remains identifiable in its original movements and any historical sales.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.InventoryUnits') AND name=N'CK_InventoryUnits_StatusWithWithdrawal')
BEGIN
    ALTER TABLE dbo.InventoryUnits DROP CONSTRAINT CK_InventoryUnits_Status;
    ALTER TABLE dbo.InventoryUnits ADD CONSTRAINT CK_InventoryUnits_StatusWithWithdrawal CHECK (UnitStatus BETWEEN 0 AND 3);
END;
IF OBJECT_ID(N'dbo.PurchaseReturnUnits',N'U') IS NULL CREATE TABLE dbo.PurchaseReturnUnits(
    PurchaseReturnLineId int NOT NULL REFERENCES dbo.PurchaseReturnLines(Id), InventoryUnitId int PRIMARY KEY REFERENCES dbo.InventoryUnits(Id), WasDamaged bit NOT NULL);
IF OBJECT_ID(N'dbo.SupplierCreditNotes',N'U') IS NULL CREATE TABLE dbo.SupplierCreditNotes(
    Id int IDENTITY PRIMARY KEY, SupplierId int NOT NULL REFERENCES dbo.Suppliers(Id), SourceReturnId int NOT NULL UNIQUE REFERENCES dbo.PurchaseReturns(Id),
    Amount decimal(19,2) NOT NULL CHECK(Amount>0), UpdatedAtUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion rowversion NOT NULL);
IF OBJECT_ID(N'dbo.SupplierCreditApplications',N'U') IS NULL CREATE TABLE dbo.SupplierCreditApplications(
    Id int IDENTITY PRIMARY KEY, CreditId int NOT NULL REFERENCES dbo.SupplierCreditNotes(Id), InvoiceId int NOT NULL REFERENCES dbo.PurchaseInvoices(Id),
    Amount decimal(19,2) NOT NULL CHECK(Amount>0), ActorUserId int NOT NULL REFERENCES dbo.Users(Id), AppliedAtUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID(N'dbo.SupplierCreditReceipts',N'U') IS NULL CREATE TABLE dbo.SupplierCreditReceipts(
    Id int IDENTITY PRIMARY KEY, CreditId int NOT NULL REFERENCES dbo.SupplierCreditNotes(Id), SupplierId int NOT NULL REFERENCES dbo.Suppliers(Id),
    Evidence nvarchar(120) NOT NULL CHECK(LEN(LTRIM(RTRIM(Evidence)))>0), PaymentId int NOT NULL UNIQUE REFERENCES dbo.PurchasePayments(Id),
    LedgerId int NOT NULL UNIQUE REFERENCES dbo.FinancialTransactions(Id), UNIQUE(SupplierId,Evidence));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.SupplierCreditApplications') AND name=N'IX_SupplierCreditApplications_Invoice')
    CREATE INDEX IX_SupplierCreditApplications_Invoice ON dbo.SupplierCreditApplications(InvoiceId,CreditId) INCLUDE(Amount);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.SupplierCreditApplications') AND name=N'IX_SupplierCreditApplications_Note')
    CREATE INDEX IX_SupplierCreditApplications_Note ON dbo.SupplierCreditApplications(CreditId) INCLUDE(Amount);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.SupplierCreditReceipts') AND name=N'IX_SupplierCreditReceipts_Note')
    CREATE INDEX IX_SupplierCreditReceipts_Note ON dbo.SupplierCreditReceipts(CreditId,PaymentId);
GO
CREATE OR ALTER VIEW dbo.vwSupplierCreditBalances AS
SELECT c.Id, c.SupplierId, c.SourceReturnId, r.PurchaseInvoiceId AS SourceInvoiceId, r.ReturnNumber, c.Amount, c.RowVersion,
       a.AppliedAmount, p.ReceivedAmount, CONVERT(decimal(19,2),c.Amount-a.AppliedAmount-p.ReceivedAmount) AS AvailableAmount
FROM dbo.SupplierCreditNotes c JOIN dbo.PurchaseReturns r ON r.Id=c.SourceReturnId
CROSS APPLY (SELECT CONVERT(decimal(19,2),COALESCE(SUM(Amount),0)) AS AppliedAmount FROM dbo.SupplierCreditApplications WHERE CreditId=c.Id) a
CROSS APPLY (SELECT CONVERT(decimal(19,2),COALESCE(SUM(m.Amount),0)) AS ReceivedAmount FROM dbo.SupplierCreditReceipts x JOIN dbo.PurchasePayments m ON m.Id=x.PaymentId WHERE x.CreditId=c.Id) p;
GO
CREATE OR ALTER VIEW dbo.vwPurchaseInvoiceSummary AS
SELECT d.*, n.CreditIssued, n.AvailableCredit, a.CreditApplied, n.CreditReceived, s.SettledAmount,
       CONVERT(decimal(19,2),CASE WHEN d.InvoiceStatus NOT IN (3,5) AND d.NetPurchases>s.SettledAmount THEN d.NetPurchases-s.SettledAmount ELSE 0 END) AS RemainingPayable,
       CONVERT(bit,CASE WHEN d.DiscountAmount>d.Subtotal OR s.SettledAmount<0 OR s.SettledAmount>d.NetPurchases OR (d.InvoiceStatus IN (3,5) AND d.NetPurchases<>0)
         OR d.SupplierRefundReceived<>n.CreditReceived
         OR EXISTS(SELECT q.AccountId,q.Kind FROM (
             SELECT p.FinancialAccountId AS AccountId,p.PaymentKind AS Kind,p.Amount AS Difference FROM dbo.PurchasePayments p WHERE p.PurchaseInvoiceId=d.PurchaseInvoiceId
             UNION ALL SELECT t.FinancialAccountId,CASE WHEN t.IsIncoming=1 THEN 2 ELSE 1 END,-t.Amount FROM dbo.FinancialTransactions t WHERE t.SourceEntityType=N'PurchaseInvoice' AND t.SourceEntityId=d.PurchaseInvoiceId
         ) q GROUP BY q.AccountId,q.Kind HAVING SUM(q.Difference)<>0)
         OR EXISTS(SELECT 1 FROM dbo.PurchaseReturns r WHERE r.PurchaseInvoiceId=d.PurchaseInvoiceId AND r.NetReduction IS NULL)
         OR EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines l WHERE l.PurchaseInvoiceId=d.PurchaseInvoiceId AND l.ReturnedQuantity<>
             (SELECT COALESCE(SUM(x.Quantity),0) FROM dbo.PurchaseReturnLines x WHERE x.PurchaseInvoiceLineId=l.Id)) THEN 1 ELSE 0 END) AS NeedsReview
FROM dbo.vwPurchaseDocumentValue d
CROSS APPLY (SELECT CONVERT(decimal(19,2),COALESCE(SUM(Amount),0)) AS CreditIssued, CONVERT(decimal(19,2),COALESCE(SUM(AvailableAmount),0)) AS AvailableCredit,
                    CONVERT(decimal(19,2),COALESCE(SUM(ReceivedAmount),0)) AS CreditReceived FROM dbo.vwSupplierCreditBalances WHERE SourceInvoiceId=d.PurchaseInvoiceId) n
CROSS APPLY (SELECT CONVERT(decimal(19,2),COALESCE(SUM(Amount),0)) AS CreditApplied FROM dbo.SupplierCreditApplications WHERE InvoiceId=d.PurchaseInvoiceId) a
-- A receipt on an issued note reduces the note, not the source invoice's coverage a second time.
CROSS APPLY (SELECT CONVERT(decimal(19,2),d.NetPaid+n.CreditReceived+a.CreditApplied-n.CreditIssued) AS SettledAmount) s;
GO
CREATE OR ALTER PROCEDURE dbo.LockPurchaseDocument @Id int,@Version varbinary(8000),@AllowClosed bit=0 AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Saved binary(8),@Status tinyint;
    SELECT @Saved=RowVersion,@Status=InvoiceStatus FROM dbo.PurchaseInvoices WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;
    IF @Saved IS NULL OR @Version IS NULL OR DATALENGTH(@Version)<>8 OR @Saved<>@Version THROW 50251,N'تغيّرت الفاتورة. حدّث السجل وافتح المستند من جديد.',1;
    IF @Status IN (3,5) AND @AllowClosed=0 THROW 50252,N'الفاتورة ملغاة أو مرتجعة بالكامل.',1;
    IF EXISTS(SELECT 1 FROM dbo.vwPurchaseInvoiceSummary WHERE PurchaseInvoiceId=@Id AND NeedsReview=1)
        THROW 50253,N'الفاتورة تحتاج مطابقة تاريخية للمبالغ أو مستندات المرتجع. لم تُسجّل حركة جديدة.',1;
    IF @Status NOT IN (3,5) AND EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines l WHERE l.PurchaseInvoiceId=@Id AND l.Quantity-l.ReturnedQuantity<>
        (SELECT COUNT(*) FROM dbo.PurchaseInvoiceLineUnits lu JOIN dbo.InventoryUnits u ON u.Id=lu.InventoryUnitId WHERE lu.PurchaseInvoiceLineId=l.Id AND u.ProductId=l.ProductId AND u.UnitStatus<>3))
        THROW 50254,N'روابط وحدات المخزون لا تطابق الكميات المحفوظة.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.NextPurchaseNumber @Type nvarchar(30),@Prefix nvarchar(4),@Number nvarchar(50) OUTPUT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Year smallint=YEAR(SYSDATETIME()),@Value bigint;
    IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSequences WITH(UPDLOCK,HOLDLOCK) WHERE DocumentType=@Type AND SequenceYear=@Year)
        INSERT dbo.DocumentSequences(DocumentType,SequenceYear,LastValue) VALUES(@Type,@Year,0);
    UPDATE dbo.DocumentSequences SET @Value=LastValue=LastValue+1 WHERE DocumentType=@Type AND SequenceYear=@Year;
    SET @Number=CONCAT(@Prefix,'-',@Year,'-',REPLICATE('0',CASE WHEN @Value<10000 THEN 4-LEN(CONVERT(varchar(20),@Value)) ELSE 0 END),@Value);
END;
GO
CREATE OR ALTER PROCEDURE dbo.FinishPurchasePosting @Id int,@Actor int,@Action nvarchar(100),@Title nvarchar(200),@Details nvarchar(1500),
    @Amount decimal(19,2),@Delta decimal(19,2)=NULL AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET InvoiceStatus=CASE WHEN i.InvoiceStatus=3 THEN 3
        WHEN NOT EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id AND Quantity>ReturnedQuantity) THEN 5
        WHEN EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id AND ReturnedQuantity>0) THEN 4
        WHEN s.RemainingPayable=0 THEN 0 WHEN s.SettledAmount>0 THEN 1 ELSE 2 END,UpdatedAtUtc=SYSUTCDATETIME()
        FROM dbo.PurchaseInvoices i JOIN dbo.vwPurchaseInvoiceSummary s ON s.PurchaseInvoiceId=i.Id WHERE i.Id=@Id;
    INSERT dbo.PurchaseInvoiceActivities(PurchaseInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId,DocumentDelta)
        SELECT @Id,CASE WHEN @Action=N'CreatePurchaseInvoice' THEN CreatedAtUtc ELSE SYSUTCDATETIME() END,@Title,@Details,@Amount,@Actor,@Delta FROM dbo.PurchaseInvoices WHERE Id=@Id;
    INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
        SELECT @Actor,N'Purchasing',@Action,N'PurchaseInvoice',CONVERT(nvarchar(100),@Id),
        (SELECT @Details AS Details,@Amount AS Amount,@Delta AS DocumentDelta, s.NetPurchases,s.RemainingPayable,s.AvailableCredit FROM dbo.vwPurchaseInvoiceSummary s WHERE PurchaseInvoiceId=@Id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
END;
GO
-- Private: @ReturnLine is NULL for edit/delete. Damaged units already left sellable stock when damaged.
CREATE OR ALTER PROCEDURE dbo.WithdrawPurchaseUnits @Id int,@Actor int,@Line int,@Count int,@ReturnLine int=NULL,@DamagedFirst bit=0 AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Take TABLE(Id int PRIMARY KEY,ProductId int,Cost decimal(19,2),OldStatus tinyint);
    INSERT @Take SELECT TOP (@Count) u.Id,u.ProductId,u.PurchaseCost,u.UnitStatus FROM dbo.PurchaseInvoiceLineUnits lu
        JOIN dbo.InventoryUnits u WITH(UPDLOCK,HOLDLOCK) ON u.Id=lu.InventoryUnitId JOIN dbo.PurchaseInvoiceLines l ON l.Id=lu.PurchaseInvoiceLineId
        WHERE l.PurchaseInvoiceId=@Id AND l.Id=@Line AND u.ProductId=l.ProductId AND (u.UnitStatus=0 OR (@ReturnLine IS NOT NULL AND u.UnitStatus=2 AND u.DamageSettlementStatus<>N'مرتجع للمورد'))
        ORDER BY CASE WHEN u.UnitStatus=2 AND @DamagedFirst=1 THEN -1 ELSE u.UnitStatus END,u.Id DESC;
    IF @@ROWCOUNT<>@Count THROW 50254,N'القطع المرتبطة والمتاحة لا تكفي؛ لا يمكن سحب قطع مباعة أو غير موثقة.',1;
    IF @ReturnLine IS NOT NULL INSERT dbo.PurchaseReturnUnits(PurchaseReturnLineId,InventoryUnitId,WasDamaged) SELECT @ReturnLine,Id,CASE WHEN OldStatus=2 THEN 1 ELSE 0 END FROM @Take;
    UPDATE u SET UnitStatus=3,DamagedAtUtc=NULL FROM dbo.InventoryUnits u JOIN @Take t ON t.Id=u.Id;
    INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
        SELECT ProductId,Id,CASE WHEN @ReturnLine IS NULL THEN N'PurchaseAdjustment' ELSE N'ReturnToSupplier' END,-1,Cost,@Actor,
            CASE WHEN @ReturnLine IS NULL THEN N'PurchaseInvoice' ELSE N'PurchaseReturn' END,
            COALESCE((SELECT PurchaseReturnId FROM dbo.PurchaseReturnLines WHERE Id=@ReturnLine),@Id),N'سحب وحدة من مخزون المشتريات' FROM @Take WHERE OldStatus=0;
    IF @ReturnLine IS NULL DELETE lu FROM dbo.PurchaseInvoiceLineUnits lu JOIN @Take t ON t.Id=lu.InventoryUnitId;
END;
GO
-- Use stable line IDs/numbers, not grid position. New supply may set catalog cost; document edits never do.
CREATE OR ALTER PROCEDURE dbo.WritePurchaseLines @Id int,@Actor int,@Items dbo.PurchaseItemSelection READONLY,@Creating bit AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM @Items) OR EXISTS(SELECT 1 FROM @Items WHERE Ordinal<=0 OR Quantity<=0 OR Cost<0)
       OR EXISTS(SELECT LineId FROM @Items WHERE LineId IS NOT NULL GROUP BY LineId HAVING COUNT(*)>1)
       OR EXISTS(SELECT 1 FROM @Items x WHERE x.LineId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines l WHERE l.Id=x.LineId AND l.ProductId=x.ProductId AND l.PurchaseInvoiceId=@Id))
        THROW 50252,N'راجع هوية بنود الفاتورة وكمياتها وأسعارها.',1;
    DECLARE @Line int,@Qty int,@Old int,@Returned int,@OldCost decimal(19,2),@Count int,@Product int,@Cost decimal(19,2),@Unit int,@Track bit,
            @Number nvarchar(50),@Supplier int,@Ordinal int=0,@LineNo int=(SELECT COALESCE(MAX(LineNumber),0) FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id);
    SELECT @Number=InvoiceNumber,@Supplier=SupplierId FROM dbo.PurchaseInvoices WHERE Id=@Id;
    WHILE EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines l WHERE l.PurchaseInvoiceId=@Id AND NOT EXISTS(SELECT 1 FROM @Items x WHERE x.LineId=l.Id))
    BEGIN
        SELECT TOP(1) @Line=l.Id,@Count=l.Quantity,@Returned=l.ReturnedQuantity FROM dbo.PurchaseInvoiceLines l WHERE l.PurchaseInvoiceId=@Id AND NOT EXISTS(SELECT 1 FROM @Items x WHERE x.LineId=l.Id) ORDER BY l.Id;
        IF @Returned<>0 THROW 50254,N'لا يمكن حذف بند له مرتجع.',1;
        EXEC dbo.WithdrawPurchaseUnits @Id,@Actor,@Line,@Count;
        DELETE dbo.PurchaseInvoiceLines WHERE Id=@Line;
    END;
    WHILE EXISTS(SELECT 1 FROM @Items WHERE Ordinal>@Ordinal)
    BEGIN
        SELECT TOP(1) @Ordinal=Ordinal,@Line=LineId,@Product=ProductId,@Qty=Quantity,@Cost=Cost FROM @Items WHERE Ordinal>@Ordinal ORDER BY Ordinal;
        SET @Old=0;
        IF @Line IS NULL
        BEGIN
            SET @LineNo+=1;
            INSERT dbo.PurchaseInvoiceLines(PurchaseInvoiceId,LineNumber,ProductId,BarcodeSnapshot,ProductNameSnapshot,CategoryNameSnapshot,UnitNameSnapshot,TrackSerialNumber,Quantity,UnitPurchaseCost)
                SELECT @Id,@LineNo,p.Id,COALESCE(p.Barcode,N''),p.Name,c.Name,p.UnitName,p.TrackSerialNumber,@Qty,@Cost FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE p.Id=@Product AND p.IsActiveForSale=1;
            IF @@ROWCOUNT<>1 THROW 50252,N'الصنف غير موجود أو موقوف.',1;
            SET @Line=CONVERT(int,SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            SELECT @Old=Quantity,@Returned=ReturnedQuantity,@OldCost=UnitPurchaseCost FROM dbo.PurchaseInvoiceLines WHERE Id=@Line;
            IF @Qty<@Returned OR (@OldCost<>@Cost AND (@Returned>0 OR EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLineUnits lu JOIN dbo.InventoryUnits u ON u.Id=lu.InventoryUnitId WHERE lu.PurchaseInvoiceLineId=@Line AND u.UnitStatus<>0)))
                THROW 50254,N'لا يمكن إعادة تسعير بند له قطع مباعة أو تالفة أو مرتجعة، أو مسح كميته المرتجعة.',1;
            IF @Old>@Qty
            BEGIN
                SET @Count=@Old-@Qty;
                EXEC dbo.WithdrawPurchaseUnits @Id,@Actor,@Line,@Count;
            END;
            UPDATE dbo.PurchaseInvoiceLines SET Quantity=@Qty,UnitPurchaseCost=@Cost WHERE Id=@Line;
            UPDATE u SET PurchaseCost=@Cost,SupplierId=@Supplier FROM dbo.InventoryUnits u JOIN dbo.PurchaseInvoiceLineUnits lu ON lu.InventoryUnitId=u.Id
                WHERE lu.PurchaseInvoiceLineId=@Line AND u.UnitStatus=0 AND (u.PurchaseCost<>@Cost OR u.SupplierId<>@Supplier);
        END;
        SELECT @Track=TrackSerialNumber FROM dbo.PurchaseInvoiceLines WHERE Id=@Line;
        SET @Count=@Qty-@Old;
        WHILE @Count>0
        BEGIN
            INSERT dbo.InventoryUnits(ProductId,SupplierId,PurchaseInvoiceNumber,PurchaseCost) VALUES(@Product,@Supplier,@Number,@Cost);
            SET @Unit=CONVERT(int,SCOPE_IDENTITY());
            IF @Track=1 UPDATE dbo.InventoryUnits SET SerialNumber=CONCAT(N'SN-',@Number,'-',@Unit) WHERE Id=@Unit;
            INSERT dbo.PurchaseInvoiceLineUnits(PurchaseInvoiceLineId,InventoryUnitId) VALUES(@Line,@Unit);
            INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
                VALUES(@Product,@Unit,N'Supply',1,@Cost,@Actor,N'PurchaseInvoice',@Id,@Number);
            SET @Count-=1;
        END;
        IF @Creating=1 UPDATE dbo.Products SET CostPrice=@Cost,UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Product;
    END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.WritePurchasePayment @Id int,@Actor int,@Cash decimal(19,2),@Vodafone decimal(19,2),@InstaPay decimal(19,2),@Note nvarchar(1000) AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Total decimal(19,2)=@Cash+@Vodafone+@InstaPay;
    IF @Total IS NULL OR @Total<=0 OR @Cash<0 OR @Vodafone<0 OR @InstaPay<0 OR @Total>(SELECT RemainingPayable FROM dbo.vwPurchaseInvoiceSummary WHERE PurchaseInvoiceId=@Id)
        THROW 50255,N'السداد يجب أن يكون موجبًا ولا يتجاوز الدين المحفوظ بعد الخصم والمرتجعات والمقاصة.',1;
    DECLARE @Post TABLE(Kind int PRIMARY KEY,Amount decimal(19,2),AccountId int);
    INSERT @Post SELECT k.Kind,k.Amount,(SELECT TOP(1) Id FROM dbo.FinancialAccounts WITH(UPDLOCK,HOLDLOCK) WHERE AccountKind=k.Kind AND IsActive=1 ORDER BY IsDefault DESC,Id)
        FROM (VALUES(0,@Cash),(1,@Vodafone),(2,@InstaPay)) k(Kind,Amount) WHERE k.Amount>0;
    DECLARE @Balance TABLE(Id int PRIMARY KEY,AfterAmount decimal(19,2));
    UPDATE a SET CurrentBalance=CurrentBalance-p.Amount,UpdatedAtUtc=SYSUTCDATETIME() OUTPUT inserted.Id,inserted.CurrentBalance INTO @Balance
        FROM dbo.FinancialAccounts a JOIN @Post p ON a.Id=p.AccountId WHERE a.IsActive=1 AND a.CurrentBalance>=p.Amount;
    IF (SELECT COUNT(*) FROM @Balance)<>(SELECT COUNT(*) FROM @Post) THROW 50256,N'لا يوجد حساب دفع نشط برصيد كافٍ للقناة المختارة.',1;
    INSERT dbo.PurchasePayments(PurchaseInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
        SELECT @Id,AccountId,1,Amount,SYSUTCDATETIME(),@Note,@Actor FROM @Post;
    INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
        SELECT p.AccountId,SYSUTCDATETIME(),0,N'سداد مشتريات',p.Amount,b.AfterAmount,i.SupplierNameSnapshot,@Note,@Actor,CASE WHEN p.Kind=0 THEN 0 ELSE 1 END,N'PurchaseInvoice',i.Id
        FROM @Post p JOIN @Balance b ON b.Id=p.AccountId CROSS JOIN dbo.PurchaseInvoices i WHERE i.Id=@Id;
END;
GO
CREATE OR ALTER PROCEDURE dbo.CreatePurchaseDocument @Actor int,@Supplier int,@Reference nvarchar(100),@Date datetime2(7),@Discount decimal(19,2),@Notes nvarchar(1000),
    @Items dbo.PurchaseItemSelection READONLY,@Cash decimal(19,2),@Vodafone decimal(19,2),@InstaPay decimal(19,2) AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Number nvarchar(50),@Id int,@Net decimal(19,2),@Paid decimal(19,2)=@Cash+@Vodafone+@InstaPay,@Details nvarchar(1500),@Status tinyint;
        IF @Discount IS NULL OR @Discount<0 OR @Paid IS NULL OR @Cash<0 OR @Vodafone<0 OR @InstaPay<0 OR EXISTS(SELECT 1 FROM @Items WHERE LineId IS NOT NULL)
            THROW 50252,N'راجع الخصم ومبالغ السداد وبنود المستند الجديد.',1;
        EXEC dbo.NextPurchaseNumber N'PurchaseInvoice',N'PUR',@Number OUTPUT;
        INSERT dbo.PurchaseInvoices(InvoiceNumber,SupplierInvoiceNumber,SupplierId,SupplierNameSnapshot,SupplierPhoneSnapshot,SupplierCompanySnapshot,SupplierAddressSnapshot,
            IssuedAtUtc,InvoiceStatus,DiscountAmount,Notes,BuyerUserId)
            SELECT @Number,@Reference,Id,Name,COALESCE(Phone,N''),COALESCE(CompanyName,N''),COALESCE(Address,N''),@Date,2,@Discount,@Notes,@Actor FROM dbo.Suppliers WHERE Id=@Supplier AND IsActive=1;
        IF @@ROWCOUNT<>1 THROW 50252,N'اختر موردًا نشطًا.',1;
        SET @Id=CONVERT(int,SCOPE_IDENTITY());
        EXEC dbo.WritePurchaseLines @Id,@Actor,@Items,1;
        SELECT @Net=NetPurchases FROM dbo.vwPurchaseDocumentValue WHERE PurchaseInvoiceId=@Id;
        IF @Net<=0 THROW 50255,N'صافي الفاتورة بعد الخصم يجب أن يكون موجبًا.',1;
        IF @Paid>0 EXEC dbo.WritePurchasePayment @Id,@Actor,@Cash,@Vodafone,@InstaPay,N'سداد عند إنشاء الفاتورة';
        SET @Details=CONCAT(@Number,N' · الصافي ',@Net,N' · السداد ',@Paid);
        EXEC dbo.FinishPurchasePosting @Id,@Actor,N'CreatePurchaseInvoice',N'اعتماد فاتورة شراء',@Details,@Paid,@Net;
        SELECT @Status=InvoiceStatus FROM dbo.PurchaseInvoices WHERE Id=@Id;
        COMMIT;
        SELECT @Id,@Number,@Net,@Paid,@Net-@Paid,@Status;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
-- No channel, desired paid total, account or cash adjustment is accepted by this endpoint.
CREATE OR ALTER PROCEDURE dbo.EditPurchaseDocument @Id int,@Version varbinary(8000),@Actor int,@Supplier int,@Reference nvarchar(100),@Date datetime2(7),@Discount decimal(19,2),@Notes nvarchar(1000),@Items dbo.PurchaseItemSelection READONLY AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockPurchaseDocument @Id,@Version;
        DECLARE @Before decimal(19,2),@Covered decimal(19,2),@Net decimal(19,2),@Gross decimal(19,2),@Delta decimal(19,2),@Details nvarchar(1500);
        SELECT @Before=NetPurchases,@Covered=SettledAmount FROM dbo.vwPurchaseInvoiceSummary WHERE PurchaseInvoiceId=@Id;
        IF @Supplier<>(SELECT SupplierId FROM dbo.PurchaseInvoices WHERE Id=@Id) OR @Supplier IS NULL THROW 50252,N'لا يمكن نقل مستند معتمد إلى مورد آخر.',1;
        IF @Discount IS NULL OR @Discount<0 THROW 50255,N'الخصم غير صحيح.',1;
        EXEC dbo.WritePurchaseLines @Id,@Actor,@Items,0;
        UPDATE dbo.PurchaseInvoices SET SupplierInvoiceNumber=@Reference,IssuedAtUtc=@Date,Notes=@Notes,DiscountAmount=@Discount WHERE Id=@Id;
        SELECT @Net=NetPurchases,@Gross=ActiveGross FROM dbo.vwPurchaseDocumentValue WHERE PurchaseInvoiceId=@Id;
        IF @Net<=0 OR @Discount>@Gross OR @Covered>@Net THROW 50255,N'الصافي المعدّل يجب أن يكون موجبًا ويغطي السداد والمقاصة بعد المرتجعات. لم يتحرك أي مبلغ.',1;
        SET @Delta=@Net-@Before; SET @Details=CONCAT(N'صافي المستند ',@Before,N' ← ',@Net,N'؛ دون حركة مالية.');
        EXEC dbo.FinishPurchasePosting @Id,@Actor,N'EditPurchaseInvoice',N'تعديل مستند شراء',@Details,@Net,@Delta;
        COMMIT;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.PayPurchaseDocument @Id int,@Version varbinary(8000),@Actor int,@Cash decimal(19,2),@Vodafone decimal(19,2),@InstaPay decimal(19,2),@Notes nvarchar(1000) AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockPurchaseDocument @Id,@Version;
        EXEC dbo.WritePurchasePayment @Id,@Actor,@Cash,@Vodafone,@InstaPay,@Notes;
        DECLARE @Amount decimal(19,2)=@Cash+@Vodafone+@InstaPay,@Details nvarchar(1500)=CONCAT(N'كاش ',@Cash,N' · فودافون ',@Vodafone,N' · إنستاباي ',@InstaPay,N' · ',@Notes);
        EXEC dbo.FinishPurchasePosting @Id,@Actor,N'PayPurchaseInvoice',N'سداد للمورد',@Details,@Amount;
        COMMIT;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ReturnPurchaseDocument @Id int,@Version varbinary(8000),@Actor int,@Lines dbo.PurchaseReturnSelection READONLY,@Cancel bit,@DamagedFirst bit,@Reason nvarchar(1000) AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockPurchaseDocument @Id,@Version;
        IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL OR @Cancel IS NULL OR (@Cancel=1 AND EXISTS(SELECT 1 FROM @Lines)) THROW 50252,N'راجع نوع المستند وسبب السحب.',1;
        DECLARE @Work dbo.PurchaseReturnSelection;
        IF @Cancel=1 INSERT @Work SELECT Id,Quantity-ReturnedQuantity FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id AND Quantity>ReturnedQuantity;
        ELSE INSERT @Work SELECT * FROM @Lines;
        IF NOT EXISTS(SELECT 1 FROM @Work) OR EXISTS(SELECT 1 FROM @Work w LEFT JOIN dbo.PurchaseInvoiceLines l ON l.Id=w.LineId AND l.PurchaseInvoiceId=@Id WHERE l.Id IS NULL OR w.Quantity<=0 OR w.Quantity>l.Quantity-l.ReturnedQuantity)
            THROW 50254,N'راجع بنود المرتجع والكميات المتبقية.',1;
        DECLARE @OldNet decimal(19,2),@Debt decimal(19,2),@Net decimal(19,2),@Reduction decimal(19,2),@AgainstDebt decimal(19,2),@Credit decimal(19,2),@Delta decimal(19,2),
                @Number nvarchar(50),@Doc int,@Line int=0,@Qty int,@ReturnLine int,@Units int=(SELECT SUM(Quantity) FROM @Work),@Status tinyint,
                @Action nvarchar(100),@Title nvarchar(200),@Details nvarchar(1500);
        SELECT @OldNet=NetPurchases,@Debt=RemainingPayable FROM dbo.vwPurchaseInvoiceSummary WHERE PurchaseInvoiceId=@Id;
        EXEC dbo.NextPurchaseNumber N'PurchaseReturn',N'PRT',@Number OUTPUT;
        INSERT dbo.PurchaseReturns(ReturnNumber,PurchaseInvoiceId,ReturnedAtUtc,Reason,ActorUserId) VALUES(@Number,@Id,SYSUTCDATETIME(),@Reason,@Actor);
        SET @Doc=CONVERT(int,SCOPE_IDENTITY());
        WHILE EXISTS(SELECT 1 FROM @Work WHERE LineId>@Line)
        BEGIN
            SELECT TOP(1) @Line=LineId,@Qty=Quantity FROM @Work WHERE LineId>@Line ORDER BY LineId;
            INSERT dbo.PurchaseReturnLines(PurchaseReturnId,PurchaseInvoiceLineId,Quantity,UnitPurchaseCost) SELECT @Doc,@Line,@Qty,UnitPurchaseCost FROM dbo.PurchaseInvoiceLines WHERE Id=@Line;
            SET @ReturnLine=CONVERT(int,SCOPE_IDENTITY());
            EXEC dbo.WithdrawPurchaseUnits @Id,@Actor,@Line,@Qty,@ReturnLine,@DamagedFirst;
            UPDATE dbo.PurchaseInvoiceLines SET ReturnedQuantity=ReturnedQuantity+@Qty WHERE Id=@Line;
        END;
        IF @Cancel=1 UPDATE dbo.PurchaseInvoices SET InvoiceStatus=3 WHERE Id=@Id;
        SELECT @Net=NetPurchases FROM dbo.vwPurchaseDocumentValue WHERE PurchaseInvoiceId=@Id;
        SET @Reduction=@OldNet-@Net; SET @AgainstDebt=CASE WHEN @Debt<@Reduction THEN @Debt ELSE @Reduction END; SET @Credit=@Reduction-@AgainstDebt; SET @Delta=-@Reduction;
        UPDATE dbo.PurchaseReturns SET NetReduction=@Reduction,DebtReduction=@AgainstDebt WHERE Id=@Doc;
        IF @Credit>0 INSERT dbo.SupplierCreditNotes(SupplierId,SourceReturnId,Amount) SELECT SupplierId,@Doc,@Credit FROM dbo.PurchaseInvoices WHERE Id=@Id;
        SET @Action=CASE WHEN @Cancel=1 THEN N'CancelPurchaseInvoice' ELSE N'ReturnPurchaseInvoice' END;
        SET @Title=CASE WHEN @Cancel=1 THEN N'إلغاء فاتورة شراء' ELSE N'مرتجع للمورد' END;
        SET @Details=CONCAT(@Number,N' · خفض الدين ',@AgainstDebt,N' · رصيد لنا عند المورد ',@Credit,N' · دون قبض · ',@Reason);
        EXEC dbo.FinishPurchasePosting @Id,@Actor,@Action,@Title,@Details,@Reduction,@Delta;
        SELECT @Status=InvoiceStatus FROM dbo.PurchaseInvoices WHERE Id=@Id;
        COMMIT;
        SELECT @Number,@Units,@Reduction,@AgainstDebt,@Credit,@Status;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
-- Lock the note itself, not just its target debt: two different invoices can compete for one note.
CREATE OR ALTER PROCEDURE dbo.ClaimSupplierCredit @Credit int,@CreditVersion varbinary(8000),@Amount decimal(19,2),@Supplier int,@Source int OUTPUT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Saved binary(8),@Owner int;
    SELECT @Saved=RowVersion,@Owner=SupplierId FROM dbo.SupplierCreditNotes WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Credit;
    IF @Saved IS NULL OR @CreditVersion IS NULL OR DATALENGTH(@CreditVersion)<>8 OR @Saved<>@CreditVersion THROW 50251,N'تغيّر إشعار المورد. حدّث الرصيد ثم راجع المبلغ.',1;
    IF @Owner<>@Supplier OR @Amount IS NULL OR @Amount<=0 OR @Amount>(SELECT AvailableAmount FROM dbo.vwSupplierCreditBalances WHERE Id=@Credit)
        THROW 50257,N'لا يكفي الرصيد المتاح لهذا المورد أو أن الإشعار لمورد آخر.',1;
    SELECT @Source=SourceInvoiceId FROM dbo.vwSupplierCreditBalances WHERE Id=@Credit;
    IF EXISTS(SELECT 1 FROM dbo.vwPurchaseInvoiceSummary WHERE PurchaseInvoiceId=@Source AND NeedsReview=1) THROW 50253,N'فاتورة مصدر الإشعار تحتاج مطابقة قبل استخدام الرصيد.',1;
    UPDATE dbo.SupplierCreditNotes SET UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Credit;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ApplySupplierCredit @Id int,@Version varbinary(8000),@Actor int,@Credit int,@CreditVersion varbinary(8000),@Amount decimal(19,2) AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockPurchaseDocument @Id,@Version;
        DECLARE @Supplier int=(SELECT SupplierId FROM dbo.PurchaseInvoices WHERE Id=@Id),@Source int;
        EXEC dbo.ClaimSupplierCredit @Credit,@CreditVersion,@Amount,@Supplier,@Source OUTPUT;
        IF @Source=@Id OR @Amount>(SELECT RemainingPayable FROM dbo.vwPurchaseInvoiceSummary WHERE PurchaseInvoiceId=@Id)
            THROW 50257,N'اختر دين فاتورة أخرى لنفس المورد، دون تجاوز المتبقي عليها.',1;
        INSERT dbo.SupplierCreditApplications(CreditId,InvoiceId,Amount,ActorUserId) VALUES(@Credit,@Id,@Amount,@Actor);
        DECLARE @Details nvarchar(1500)=CONCAT(N'إشعار #',@Credit,N' · مقاصة ',@Amount,N' دون حركة خزينة');
        EXEC dbo.FinishPurchasePosting @Id,@Actor,N'ApplySupplierCredit',N'استخدام رصيد المورد',@Details,@Amount;
        COMMIT;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ReceiveSupplierCredit @Id int,@Version varbinary(8000),@Actor int,@Credit int,@CreditVersion varbinary(8000),
    @Amount decimal(19,2),@Account int,@Evidence nvarchar(120) AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockPurchaseDocument @Id,@Version,1;
        DECLARE @Supplier int,@Name nvarchar(200),@Source int,@Payment int,@Ledger int,@Balance decimal(19,2),@Details nvarchar(1500),@At datetime2(7)=SYSUTCDATETIME();
        SELECT @Supplier=SupplierId,@Name=SupplierNameSnapshot FROM dbo.PurchaseInvoices WHERE Id=@Id;
        EXEC dbo.ClaimSupplierCredit @Credit,@CreditVersion,@Amount,@Supplier,@Source OUTPUT;
        SET @Evidence=LTRIM(RTRIM(@Evidence));
        IF @Source<>@Id OR NULLIF(@Evidence,N'') IS NULL OR EXISTS(SELECT 1 FROM dbo.SupplierCreditReceipts WHERE SupplierId=@Supplier AND Evidence=@Evidence)
            THROW 50258,N'أدخل مرجع إثبات قبض فعلي غير مستخدم للمورد، على فاتورة الإشعار الأصلية.',1;
        UPDATE dbo.FinancialAccounts SET @Balance=CurrentBalance=CurrentBalance+@Amount,UpdatedAtUtc=@At WHERE Id=@Account AND IsActive=1;
        IF @@ROWCOUNT<>1 THROW 50256,N'حساب القبض المحدد غير موجود أو موقوف. لا يتم التحويل إلى حساب بديل.',1;
        INSERT dbo.PurchasePayments(PurchaseInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId) VALUES(@Id,@Account,2,@Amount,@At,@Evidence,@Actor);
        SET @Payment=CONVERT(int,SCOPE_IDENTITY());
        INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
            VALUES(@Account,@At,1,N'قبض من مورد',@Amount,@Balance,@Name,@Evidence,@Actor,0,N'PurchaseInvoice',@Id);
        SET @Ledger=CONVERT(int,SCOPE_IDENTITY());
        INSERT dbo.SupplierCreditReceipts(CreditId,SupplierId,Evidence,PaymentId,LedgerId) VALUES(@Credit,@Supplier,@Evidence,@Payment,@Ledger);
        SET @Details=CONCAT(N'إشعار #',@Credit,N' · سند قبض ',@Evidence,N' · حساب #',@Account,N' · ',@Amount);
        EXEC dbo.FinishPurchasePosting @Id,@Actor,N'ReceiveSupplierCredit',N'قبض فعلي من المورد',@Details,@Amount;
        COMMIT;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.DeleteUnsettledPurchase @Id int,@Version varbinary(8000),@Actor int AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockPurchaseDocument @Id,@Version;
        IF EXISTS(SELECT 1 FROM dbo.PurchasePayments WHERE PurchaseInvoiceId=@Id) OR EXISTS(SELECT 1 FROM dbo.PurchaseReturns WHERE PurchaseInvoiceId=@Id)
           OR EXISTS(SELECT 1 FROM dbo.SupplierCreditApplications WHERE InvoiceId=@Id) THROW 50252,N'لا يمكن حذف مستند له تسوية أو مرتجع.',1;
        DECLARE @Line int=0,@Count int;
        WHILE EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id AND Id>@Line)
        BEGIN
            SELECT TOP(1) @Line=Id,@Count=Quantity FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id AND Id>@Line ORDER BY Id;
            EXEC dbo.WithdrawPurchaseUnits @Id,@Actor,@Line,@Count;
        END;
        INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,OldValuesJson)
            SELECT @Actor,N'Purchasing',N'DeletePurchaseInvoice',N'PurchaseInvoice',CONVERT(nvarchar(100),@Id),
                (SELECT * FROM dbo.vwPurchaseInvoiceSummary WHERE PurchaseInvoiceId=@Id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
        DELETE dbo.PurchaseInvoiceActivities WHERE PurchaseInvoiceId=@Id;
        DELETE dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id;
        DELETE dbo.PurchaseInvoices WHERE Id=@Id;
        COMMIT;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO
-- Shared accounts/stock are still used by other modules; purchase documents and claims are procedure-only.
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchaseInvoices TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoices TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchaseInvoiceLines TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceLines TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchaseInvoiceLineUnits TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceLineUnits TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchaseReturns TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseReturns TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchaseReturnLines TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseReturnLines TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchaseReturnUnits TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseReturnUnits TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchasePayments TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchasePayments TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.PurchaseInvoiceActivities TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.PurchaseInvoiceActivities TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.SupplierCreditNotes TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.SupplierCreditNotes TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.SupplierCreditApplications TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.SupplierCreditApplications TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.SupplierCreditReceipts TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.SupplierCreditReceipts TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.vwSupplierCreditBalances TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.vwPurchaseInvoiceSummary TO [ProjectZClient];
GRANT EXECUTE,REFERENCES ON TYPE::dbo.PurchaseItemSelection TO [ProjectZClient];
GRANT EXECUTE,REFERENCES ON TYPE::dbo.PurchaseReturnSelection TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.CreatePurchaseDocument TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.EditPurchaseDocument TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.PayPurchaseDocument TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.ReturnPurchaseDocument TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.ApplySupplierCredit TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.ReceiveSupplierCredit TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.DeleteUnsettledPurchase TO [ProjectZClient];
GO
