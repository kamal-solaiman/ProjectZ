/* SQL Server 2016 SP1+. Back up, stop all tills, apply after the updated 07 and 13–16 and install the matching client.
   No existing ticket, payment, balance, part line or stock movement is rewritten. */
USE [ProjectZ];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.vwMaintenanceOrderSummary',N'V') AND name=N'RemainingDue') THROW 50240,N'شغّل النسخة المحدثة من sql/07_CreateReportingViews.sql أولًا.',1;
IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL THROW 50240,N'أكمل إعداد حساب الاتصال المحدود أولًا.',1;
-- sql/18 historically granted the till backup-administrator rights. Do not silently revoke
-- permissions, or certify a privileged shared connection as a cashier/supervisor boundary.
IF IS_ROLEMEMBER(N'db_owner',N'ProjectZClient')=1 OR IS_SRVROLEMEMBER(N'dbcreator',N'ProjectZClient')=1
   OR IS_SRVROLEMEMBER(N'sysadmin',N'ProjectZClient')=1
    THROW 50248,N'اعزل حساب النسخ الاحتياطي الإداري عن ProjectZClient قبل ترقية تسوية الصيانة.',1;
IF COL_LENGTH(N'dbo.MaintenanceOrderParts',N'SparePartId') IS NULL OR OBJECT_ID(N'dbo.MaintenanceSparePartMovements',N'U') IS NULL
    THROW 50240, N'أكمل مخطط أوامر الصيانة وقطع الغيار أولًا (06، 13، 16).', 1;
IF COL_LENGTH(N'dbo.MaintenancePayments',N'RefundOfPaymentId') IS NULL
    ALTER TABLE dbo.MaintenancePayments ADD RefundOfPaymentId int NULL REFERENCES dbo.MaintenancePayments(Id);
IF DATABASE_PRINCIPAL_ID(N'ProjectZMaintenanceSupervisor') IS NULL CREATE ROLE [ProjectZMaintenanceSupervisor] AUTHORIZATION dbo;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MaintenancePayments') AND name=N'IX_MaintenancePayments_Receipt')
    CREATE INDEX IX_MaintenancePayments_Receipt ON dbo.MaintenancePayments(MaintenanceOrderId, RefundOfPaymentId, Id) INCLUDE (FinancialAccountId,PaymentStage,PaymentKind,Amount);
GO
-- Private helpers: all callers lock the ticket first. No posting helper is executable by the till.
CREATE OR ALTER PROCEDURE dbo.LockMaintenanceTicket @Id int,@Version varbinary(8000),@Closed bit=0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Saved binary(8), @Status tinyint;
    SELECT @Saved=RowVersion,@Status=OrderStatus FROM dbo.MaintenanceOrders WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id;
    IF @Saved IS NULL OR @Version IS NULL OR DATALENGTH(@Version)<>8 OR @Saved<>@Version
        THROW 50241, N'تغيّر أمر الصيانة أو لم يعد موجودًا. حدّث القائمة وافتح الأمر من جديد.', 1;
    IF (@Status>=5 AND @Closed=0) OR (@Status<5 AND @Closed=1)
        THROW 50242, N'حالة الأمر المحفوظة لا تسمح بهذه العملية.', 1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.PriceMaintenanceTicket @Id int,@Labor decimal(19,2),@Discount decimal(19,2),@Total decimal(19,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Parts decimal(19,2)=(SELECT ISNULL(SUM(Quantity*UnitSellPrice),0) FROM dbo.MaintenanceOrderParts WHERE MaintenanceOrderId=@Id);
    IF @Labor IS NULL OR @Discount IS NULL OR @Labor<0 OR @Discount<0 OR @Discount>@Parts+@Labor
        THROW 50243, N'راجع المصنعية والخصم مقابل بنود الصيانة المحفوظة.', 1;
    SET @Total=@Parts+@Labor-@Discount;
END;
GO
CREATE OR ALTER PROCEDURE dbo.LogMaintenancePosting @Id int,@Actor int,@At datetime2(7),@Action nvarchar(100),@Title nvarchar(200),
    @Details nvarchar(1500),@Amount decimal(19,2)=NULL,@Before nvarchar(max)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.MaintenanceActivities(MaintenanceOrderId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
        VALUES(@Id,@At,@Title,@Details,@Amount,@Actor);
    DECLARE @After nvarchar(max)=(SELECT OrderStatus,LaborCost,DiscountAmount,FinalServiceCost,DeliveredAtUtc,WarrantyDays,DeliveryNotes,CancellationReason,
        @Details AS Details, @Amount AS PostedAmount, ORIGINAL_LOGIN() AS PostingLogin, USER_NAME() AS PostingUser FROM dbo.MaintenanceOrders WHERE Id=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    INSERT dbo.AuditLog(OccurredAtUtc,ActorUserId,Module,ActionName,EntityType,EntityId,OldValuesJson,NewValuesJson)
        VALUES(@At,@Actor,N'Maintenance',@Action,N'MaintenanceOrder',CONVERT(nvarchar(100),@Id),@Before,@After);
END;
GO
CREATE OR ALTER PROCEDURE dbo.WriteMaintenanceMoney @Id int,@Actor int,@At datetime2(7),@Stage tinyint,
    @Cash decimal(19,2),@Vodafone decimal(19,2),@InstaPay decimal(19,2),@Refund decimal(19,2),@Note nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Legs TABLE(AccountId int, Receipt int, Amount decimal(19,2), PRIMARY KEY(AccountId,Receipt));
    IF @Refund>0
    BEGIN
        -- Old cash-only refunds cannot safely be assigned to individual receipts after the fact.
        IF EXISTS (SELECT 1 FROM dbo.MaintenancePayments r LEFT JOIN dbo.MaintenancePayments p ON p.Id=r.RefundOfPaymentId
                   WHERE r.MaintenanceOrderId=@Id AND r.PaymentKind=2 AND (p.Id IS NULL OR p.PaymentKind<>1 OR p.MaintenanceOrderId<>@Id OR p.FinancialAccountId<>r.FinancialAccountId))
            THROW 50244, N'يوجد رد سابق غير مطابق لدفعة أصلية؛ راجع كشف الأمر قبل أي رد جديد.', 1;
        DECLARE @Open TABLE(Receipt int PRIMARY KEY, AccountId int, Stage tinyint, Available decimal(19,2));
        INSERT @Open SELECT p.Id,p.FinancialAccountId,p.PaymentStage,p.Amount-ISNULL(CONVERT(decimal(19,2),SUM(r.Amount)),0)
            FROM dbo.MaintenancePayments p LEFT JOIN dbo.MaintenancePayments r ON r.RefundOfPaymentId=p.Id
            WHERE p.MaintenanceOrderId=@Id AND p.PaymentKind=1 GROUP BY p.Id,p.FinancialAccountId,p.PaymentStage,p.Amount;
        IF EXISTS (SELECT 1 FROM @Open WHERE Available<0) THROW 50244, N'ردود الأمر تتجاوز إحدى دفعاته. راجع الكشف.', 1;
        ;WITH caps AS (SELECT *, ISNULL(CONVERT(decimal(19,2),SUM(Available) OVER (ORDER BY Receipt ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING)),0) AS Prior
                      FROM @Open WHERE Available>0)
        INSERT @Legs SELECT AccountId,Receipt,CASE WHEN Available<@Refund-Prior THEN Available ELSE @Refund-Prior END FROM caps WHERE Prior<@Refund;
        IF ISNULL((SELECT SUM(Amount) FROM @Legs),0)<>@Refund THROW 50245, N'الرد أكبر من صافي الدفعات المستحقة في هذا الأمر.', 1;
    END
    ELSE
    BEGIN
        INSERT @Legs SELECT a.Id,0,t.Amount FROM (VALUES(0,@Cash),(1,@Vodafone),(2,@InstaPay)) t(Kind,Amount)
            CROSS APPLY (SELECT TOP (1) Id FROM dbo.FinancialAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountKind=t.Kind AND IsActive=1 ORDER BY IsDefault DESC,Id) a
            WHERE t.Amount>0;
        IF ISNULL((SELECT SUM(Amount) FROM @Legs),0)<>@Cash+@Vodafone+@InstaPay THROW 50246, N'لا يوجد حساب نشط لإحدى قنوات التحصيل المحددة.', 1;
    END;
    DECLARE @Account int,@Receipt int,@Amount decimal(19,2),@Balance decimal(19,2),@AccountKind tinyint,
            @Customer nvarchar(200),@Number nvarchar(50);
    SELECT @Customer=CustomerNameSnapshot,@Number=OrderNumber FROM dbo.MaintenanceOrders WHERE Id=@Id;
    WHILE EXISTS (SELECT 1 FROM @Legs)
    BEGIN
        SELECT TOP (1) @Account=AccountId,@Receipt=Receipt,@Amount=Amount FROM @Legs ORDER BY AccountId,Receipt;
        UPDATE dbo.FinancialAccounts SET @Balance=CurrentBalance=CurrentBalance+CASE WHEN @Refund>0 THEN -@Amount ELSE @Amount END,
            @AccountKind=AccountKind,UpdatedAtUtc=@At WHERE Id=@Account AND IsActive=1 AND (@Refund=0 OR CurrentBalance>=@Amount);
        IF @@ROWCOUNT<>1 THROW 50246, N'حساب الدفعة الأصلي موقوف أو رصيده لا يكفي للرد. لا يُنقل الرد إلى خزنة أخرى.', 1;
        INSERT dbo.MaintenancePayments(MaintenanceOrderId,FinancialAccountId,PaymentStage,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId,RefundOfPaymentId)
            VALUES(@Id,@Account,@Stage,CASE WHEN @Refund>0 THEN 2 ELSE 1 END,@Amount,@At,@Note,@Actor,NULLIF(@Receipt,0));
        INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
            VALUES(@Account,@At,CASE WHEN @Refund>0 THEN 0 ELSE 1 END,CASE WHEN @Refund>0 THEN N'رد دفعة صيانة' ELSE N'تحصيل صيانة' END,
                   @Amount,@Balance,@Customer,LEFT(CONCAT(@Number,N' · ',@Note),1000),@Actor,CASE WHEN @AccountKind=0 THEN 0 ELSE 1 END,N'MaintenanceOrder',@Id);
        DELETE @Legs WHERE AccountId=@Account AND Receipt=@Receipt;
    END;
END;
GO
CREATE OR ALTER PROCEDURE dbo.OpenMaintenanceTicket @Actor int,@CustomerId int,@Name nvarchar(200),@Phone nvarchar(30),@Phone2 nvarchar(30),@Address nvarchar(500),
    @WalkIn bit,@Priority tinyint,@Tech nvarchar(150),@Expected datetime2(7),@ExpectedLabel nvarchar(200),@DeviceType nvarchar(100),@Brand nvarchar(100),
    @Model nvarchar(150),@Color nvarchar(80),@Serial nvarchar(150),@PasscodeType tinyint,@Passcode nvarchar(1000),@Fault nvarchar(2000),@Physical nvarchar(1500),
    @Accessories nvarchar(1000),@Intake nvarchar(1500),@EstimatePending bit,@EstimatedCost decimal(19,2),@Cash decimal(19,2),@Vodafone decimal(19,2),@InstaPay decimal(19,2)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET ARITHABORT ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @At datetime2(7)=SYSUTCDATETIME(),@Year smallint=YEAR(SYSDATETIME()),@Seq bigint,@Number nvarchar(50),@Id int,@Version binary(8),@Deposit decimal(19,2)=@Cash+@Vodafone+@InstaPay;
        IF @Deposit IS NULL OR @Cash<0 OR @Vodafone<0 OR @InstaPay<0 OR (@EstimatePending=0 AND @Deposit>@EstimatedCost)
            THROW 50245,N'راجع دفعات العربون والتقدير المبدئي.',1;
        IF @Serial<>N'' AND EXISTS (SELECT 1 FROM dbo.MaintenanceOrders WITH (UPDLOCK,HOLDLOCK) WHERE SerialOrImei=@Serial AND OrderStatus<5)
            THROW 50242,N'يوجد أمر صيانة مفتوح لهذا الجهاز.',1;
        IF NOT EXISTS (SELECT 1 FROM dbo.DocumentSequences WITH (UPDLOCK,HOLDLOCK) WHERE DocumentType=N'MaintenanceOrder' AND SequenceYear=@Year)
            INSERT dbo.DocumentSequences(DocumentType,SequenceYear,LastValue) VALUES(N'MaintenanceOrder',@Year,0);
        UPDATE dbo.DocumentSequences SET @Seq=LastValue=LastValue+1 WHERE DocumentType=N'MaintenanceOrder' AND SequenceYear=@Year;
        SET @Number=CONCAT(N'MNT-',@Year,N'-',REPLICATE(N'0',CASE WHEN @Seq<10000 THEN 4-LEN(CONVERT(varchar(20),@Seq)) ELSE 0 END),@Seq);
        INSERT dbo.MaintenanceOrders(OrderNumber,CustomerId,CustomerNameSnapshot,CustomerPhoneSnapshot,CustomerSecondaryPhoneSnapshot,CustomerAddressSnapshot,
            IsWalkInCustomer,ReceivedAtUtc,ReceivedByUserId,Priority,AssignedTechnician,ExpectedDeliveryAtUtc,ExpectedDeliveryLabel,DeviceType,DeviceBrand,
            DeviceModel,DeviceColor,SerialOrImei,PasscodeType,PasscodeValueProtected,ReportedFault,PhysicalCondition,AccessoriesReceived,IntakeNotes,IsEstimatePendingInspection,EstimatedCost)
        VALUES(@Number,@CustomerId,@Name,@Phone,@Phone2,@Address,@WalkIn,@At,@Actor,@Priority,@Tech,@Expected,@ExpectedLabel,@DeviceType,@Brand,
               @Model,@Color,@Serial,@PasscodeType,@Passcode,@Fault,@Physical,@Accessories,@Intake,@EstimatePending,@EstimatedCost);
        SET @Id=CONVERT(int,SCOPE_IDENTITY());
        EXEC dbo.WriteMaintenanceMoney @Id,@Actor,@At,1,@Cash,@Vodafone,@InstaPay,0,N'عربون الاستلام';
        EXEC dbo.LogMaintenancePosting @Id,@Actor,@At,N'Intake',N'استلام جهاز صيانة',@Number,@Deposit;
        SELECT @Version=RowVersion FROM dbo.MaintenanceOrders WHERE Id=@Id;
        COMMIT;
        SELECT @Id,@Number,@At,@Version;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.SaveMaintenanceWorkshop @Id int,@Version varbinary(8000),@Actor int,@Status tinyint,@Priority tinyint,@Tech nvarchar(150),
    @Expected datetime2(7),@ExpectedLabel nvarchar(200),@Diagnosis nvarchar(2000),@Labor decimal(19,2),@Discount decimal(19,2)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET ARITHABORT ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockMaintenanceTicket @Id,@Version;
        IF @Status IS NULL OR @Status NOT BETWEEN 0 AND 4 THROW 50242,N'استخدم إجراء التسليم أو الإلغاء المستقل لإغلاق الأمر.',1;
        DECLARE @Total decimal(19,2),@At datetime2(7)=SYSUTCDATETIME();
        EXEC dbo.PriceMaintenanceTicket @Id,@Labor,@Discount,@Total OUTPUT;
        UPDATE dbo.MaintenanceOrders SET OrderStatus=@Status,Priority=@Priority,AssignedTechnician=@Tech,ExpectedDeliveryAtUtc=@Expected,
            ExpectedDeliveryLabel=@ExpectedLabel,DiagnosisNotes=@Diagnosis,LaborCost=@Labor,DiscountAmount=@Discount,FinalServiceCost=@Total,HasWorkshopPricing=1,UpdatedAtUtc=@At WHERE Id=@Id;
        DECLARE @Details nvarchar(1500)=CONCAT(N'حالة الورشة: ',@Status,N' · الفني: ',@Tech,N' · المصنعية: ',@Labor,N' · الخصم: ',@Discount);
        EXEC dbo.LogMaintenancePosting @Id,@Actor,@At,N'Workshop',N'تحديث بيانات الورشة والفحص',@Details,@Total;
        DECLARE @Posted binary(8)=(SELECT RowVersion FROM dbo.MaintenanceOrders WHERE Id=@Id);
        COMMIT;
        SELECT @Posted;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH
END;
GO
-- Some pre-upgrade tickets contain billed lines without net issues (or an issue without a line).
-- ProductId is a legacy spare link only if its stock ledger agrees. Never invent a return.
CREATE OR ALTER PROCEDURE dbo.CheckMaintenanceStock @Id int
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM
        (SELECT COALESCE(SparePartId,ProductId) AS Spare,SUM(CONVERT(bigint,Quantity)) AS Qty FROM dbo.MaintenanceOrderParts
         WHERE MaintenanceOrderId=@Id AND IsManualPart=0 GROUP BY COALESCE(SparePartId,ProductId)) p
        FULL JOIN (SELECT SparePartId,SUM(CASE WHEN MovementKind=3 THEN CONVERT(bigint,Quantity) ELSE -CONVERT(bigint,Quantity) END) AS Qty
                   FROM dbo.MaintenanceSparePartMovements WITH (HOLDLOCK) WHERE MaintenanceOrderId=@Id AND MovementKind IN (3,4) GROUP BY SparePartId) m ON m.SparePartId=p.Spare
        WHERE ISNULL(p.Qty,0)<>ISNULL(m.Qty,0))
        THROW 50247,N'بنود الأمر لا تطابق صافي حركات الصرف. راجع المخزون قبل المتابعة.',1;
END;
GO
-- Return exactly one saved line, also when the catalog part has since been stopped. Legacy
-- discrepancies (including an ambiguous ProductId) must be reconciled, never guessed as stock.
CREATE OR ALTER PROCEDURE dbo.ReleaseMaintenanceLine @Id int,@Line int,@Actor int,@At datetime2(7)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Spare int,@Qty int,@Manual bit,@Cost decimal(19,2),@After int;
    SELECT @Spare=COALESCE(SparePartId,CASE WHEN IsManualPart=0 THEN ProductId END),@Qty=Quantity,@Manual=IsManualPart,@Cost=UnitCost FROM dbo.MaintenanceOrderParts WHERE Id=@Line AND MaintenanceOrderId=@Id;
    IF @Qty IS NULL OR (@Manual=0 AND @Spare IS NULL) THROW 50247,N'بند القطعة غير موجود أو مصدره القديم غير محدد. راجع الحركات.',1;
    IF @Spare IS NOT NULL
    BEGIN
        UPDATE dbo.MaintenanceSpareParts SET @After=Quantity=Quantity+@Qty,UpdatedAtUtc=@At WHERE Id=@Spare;
        IF @@ROWCOUNT<>1 THROW 50247,N'تعذّرت مطابقة القطعة الأصلية. راجع المخزون.',1;
        INSERT dbo.MaintenanceSparePartMovements(SparePartId,MovementKind,Quantity,QuantityBefore,QuantityAfter,UnitCost,SupplierId,MaintenanceOrderId,CustomerId,Reference,Note,ActorUserId,OccurredAtUtc)
            SELECT @Spare,4,@Qty,@After-@Qty,@After,@Cost,s.SupplierId,@Id,o.CustomerId,CONVERT(nvarchar(100),@Line),N'إرجاع بند من الأمر',@Actor,@At
            FROM dbo.MaintenanceOrders o JOIN dbo.MaintenanceSpareParts s ON s.Id=@Spare WHERE o.Id=@Id;
    END;
    DELETE dbo.MaintenanceOrderParts WHERE MaintenanceOrderId=@Id AND Id=@Line;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ChangeMaintenancePart @Id int,@Version varbinary(8000),@Actor int,@Action tinyint,@Labor decimal(19,2),@Discount decimal(19,2),
    @Spare int=NULL,@Line int=NULL,@Qty int=NULL,@Name nvarchar(250)=NULL,@Cost decimal(19,2)=NULL,@Price decimal(19,2)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET ARITHABORT ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockMaintenanceTicket @Id,@Version;
        EXEC dbo.CheckMaintenanceStock @Id;
        DECLARE @At datetime2(7)=SYSUTCDATETIME(),@Total decimal(19,2),@Barcode nvarchar(100)=N'MANUAL',@After int,@LineNo int;
        IF @Action IS NULL OR @Action NOT IN (1,2,3) THROW 50247,N'طلب تغيير بند غير صالح.',1;
        IF @Action=3 EXEC dbo.ReleaseMaintenanceLine @Id,@Line,@Actor,@At;
        ELSE
        BEGIN
            IF ISNULL(@Qty,0)<=0 THROW 50247,N'يجب أن تكون كمية البند موجبة.',1;
            IF @Action=1
            BEGIN
                UPDATE dbo.MaintenanceSpareParts SET @After=Quantity=Quantity-@Qty,@Name=Name,@Cost=CostPrice,@Price=SellPrice,
                    @Barcode=ISNULL(Barcode,N''),LastIssueAtUtc=@At,UpdatedAtUtc=@At WHERE Id=@Spare AND IsActive=1 AND Quantity>=@Qty;
                IF @@ROWCOUNT<>1 THROW 50247,N'قطعة الغيار غير متاحة أو رصيدها لا يكفي.',1;
            END
            ELSE IF @Spare IS NOT NULL OR NULLIF(LTRIM(RTRIM(@Name)),N'') IS NULL OR ISNULL(@Cost,-1)<0 OR ISNULL(@Price,-1)<0
                THROW 50247,N'راجع اسم البند اليدوي وقيمته.',1;
            SELECT @LineNo=ISNULL(MAX(LineNumber),0)+1 FROM dbo.MaintenanceOrderParts WHERE MaintenanceOrderId=@Id;
            INSERT dbo.MaintenanceOrderParts(MaintenanceOrderId,SparePartId,LineNumber,BarcodeSnapshot,ProductNameSnapshot,Quantity,UnitCost,UnitSellPrice,IsManualPart)
                VALUES(@Id,@Spare,@LineNo,@Barcode,@Name,@Qty,@Cost,@Price,CASE WHEN @Action=2 THEN 1 ELSE 0 END);
            SET @Line=CONVERT(int,SCOPE_IDENTITY());
            IF @Action=1 INSERT dbo.MaintenanceSparePartMovements(SparePartId,MovementKind,Quantity,QuantityBefore,QuantityAfter,UnitCost,SupplierId,MaintenanceOrderId,CustomerId,Reference,Note,ActorUserId,OccurredAtUtc)
                SELECT @Spare,3,@Qty,@After+@Qty,@After,@Cost,s.SupplierId,@Id,o.CustomerId,CONVERT(nvarchar(100),@Line),N'صرف بند للأمر',@Actor,@At
                FROM dbo.MaintenanceOrders o JOIN dbo.MaintenanceSpareParts s ON s.Id=@Spare WHERE o.Id=@Id;
        END;
        EXEC dbo.PriceMaintenanceTicket @Id,@Labor,@Discount,@Total OUTPUT;
        UPDATE dbo.MaintenanceOrders SET LaborCost=@Labor,DiscountAmount=@Discount,FinalServiceCost=@Total,HasWorkshopPricing=1,UpdatedAtUtc=@At WHERE Id=@Id;
        DECLARE @Title nvarchar(150)=CASE @Action WHEN 1 THEN N'صرف قطعة غيار' WHEN 2 THEN N'إضافة بند يدوي' ELSE N'إزالة بند صيانة' END,
                @Details nvarchar(1500)=CONCAT(N'بند رقم ',@Line);
        EXEC dbo.LogMaintenancePosting @Id,@Actor,@At,N'ChangePart',@Title,@Details,@Total;
        DECLARE @Posted binary(8)=(SELECT RowVersion FROM dbo.MaintenanceOrders WHERE Id=@Id);
        COMMIT;
        SELECT @Posted;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.SettleMaintenanceTicket @Id int,@Version varbinary(8000),@Actor int,@Action tinyint,
    @Cash decimal(19,2)=0,@Vodafone decimal(19,2)=0,@InstaPay decimal(19,2)=0,@Labor decimal(19,2)=NULL,@Discount decimal(19,2)=NULL,
    @ExpectedTotal decimal(19,2)=NULL,@RefundExcess bit=0,@CancelRefund decimal(19,2)=0,@ReturnParts bit=0,@Warranty int=0,@Notes nvarchar(1500)=N''
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET ARITHABORT ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockMaintenanceTicket @Id,@Version;
        DECLARE @At datetime2(7)=SYSUTCDATETIME(),@Paid decimal(19,2),@Due decimal(19,2),@Refund decimal(19,2)=0,@Total decimal(19,2),
                @Stage tinyint=1,@Collect decimal(19,2)=@Cash+@Vodafone+@InstaPay,@Code nvarchar(100),@Title nvarchar(200),@Details nvarchar(1500);
        IF @Action IS NULL OR @Action NOT IN (1,2,3) OR @Collect IS NULL OR @Cash<0 OR @Vodafone<0 OR @InstaPay<0 OR ISNULL(@CancelRefund,-1)<0
            THROW 50245,N'قيمة التسوية غير صالحة.',1;
        IF @Action IN (2,3) EXEC dbo.CheckMaintenanceStock @Id;
        -- Fail closed for unresolved balances instead of manufacturing cash from legacy rows.
        IF EXISTS (SELECT 1 FROM
            (SELECT FinancialAccountId,SUM(CASE WHEN PaymentKind=1 THEN Amount ELSE -Amount END) AS Net FROM dbo.MaintenancePayments WHERE MaintenanceOrderId=@Id GROUP BY FinancialAccountId) p
            FULL JOIN (SELECT FinancialAccountId,SUM(CASE WHEN IsIncoming=1 THEN Amount ELSE -Amount END) AS Net FROM dbo.FinancialTransactions WITH(HOLDLOCK)
                       WHERE SourceEntityType=N'MaintenanceOrder' AND SourceEntityId=@Id GROUP BY FinancialAccountId) l ON l.FinancialAccountId=p.FinancialAccountId
            WHERE ISNULL(p.Net,0)<>ISNULL(l.Net,0) OR p.Net<0)
            THROW 50244,N'دفعات هذا الأمر لا تطابق دفتر حساباته. راجع التسوية قبل المتابعة.',1;
        SELECT @Paid=ISNULL(SUM(CASE WHEN PaymentKind=1 THEN Amount ELSE -Amount END),0) FROM dbo.MaintenancePayments WHERE MaintenanceOrderId=@Id;
        IF @Action=1 -- deposit / mid-repair collection; an unpriced inspection may take a deposit
        BEGIN
            SELECT @Total=CASE WHEN o.HasWorkshopPricing=1 OR o.IsEstimatePendingInspection=0 THEN v.ServiceTotal END
                FROM dbo.MaintenanceOrders o JOIN dbo.vwMaintenanceOrderSummary v ON v.MaintenanceOrderId=o.Id WHERE o.Id=@Id;
            IF @Collect<=0 OR (@Total IS NOT NULL AND @Collect+@Paid>@Total) THROW 50245,N'الدفعة غير موجبة أو تتجاوز المتبقي على الأمر.',1;
            SELECT @Code=N'CollectDeposit',@Title=N'تحصيل دفعة تحت الحساب';
            UPDATE dbo.MaintenanceOrders SET UpdatedAtUtc=@At WHERE Id=@Id;
        END
        ELSE IF @Action=2 -- delivery: all prior collections (including an earlier delivery) count
        BEGIN
            EXEC dbo.PriceMaintenanceTicket @Id,@Labor,@Discount,@Total OUTPUT;
            IF @ExpectedTotal IS NULL OR @Total<>@ExpectedTotal THROW 50243,N'تغيّر إجمالي التسليم. حدّث بنود الأمر قبل التأكيد.',1;
            SET @Due=CASE WHEN @Total>@Paid THEN @Total-@Paid ELSE 0 END;
            IF @Collect>@Due OR ISNULL(@Warranty,-1) NOT BETWEEN 0 AND DATEDIFF(day,@At,CONVERT(date,'99991230')) THROW 50245,N'تحصيل التسليم يتجاوز المتبقي أو مدة الضمان غير صالحة.',1;
            IF @RefundExcess=1 AND @Paid>@Total SET @Refund=@Paid-@Total;
            UPDATE dbo.MaintenanceOrders SET OrderStatus=5,LaborCost=@Labor,DiscountAmount=@Discount,HasWorkshopPricing=1,FinalServiceCost=@Total,
                DeliveredAtUtc=@At,WarrantyDays=@Warranty,DeliveryNotes=@Notes,UpdatedAtUtc=@At WHERE Id=@Id;
            SELECT @Stage=2,@Code=N'Deliver',@Title=N'تسليم الجهاز وإغلاق أمر الصيانة';
        END
        ELSE -- cancellation: optional bounded return of net receipts, never an arbitrary cash withdrawal
        BEGIN
            IF @Collect<>0 OR NULLIF(LTRIM(RTRIM(@Notes)),N'') IS NULL THROW 50245,N'الإلغاء يحتاج سببًا ولا يقبل تحصيل دفعة.',1;
            IF @CancelRefund>@Paid THROW 50245,N'الرد يتجاوز صافي المحصل في الأمر.',1;
            SET @Refund=@CancelRefund;
            IF @ReturnParts=1
            BEGIN
                DECLARE @Line int;
                WHILE EXISTS (SELECT 1 FROM dbo.MaintenanceOrderParts WHERE MaintenanceOrderId=@Id)
                BEGIN
                    SELECT TOP (1) @Line=Id FROM dbo.MaintenanceOrderParts WHERE MaintenanceOrderId=@Id ORDER BY Id;
                    EXEC dbo.ReleaseMaintenanceLine @Id,@Line,@Actor,@At;
                END;
                UPDATE dbo.MaintenanceOrders SET DiscountAmount=CASE WHEN DiscountAmount>LaborCost THEN LaborCost ELSE DiscountAmount END,
                    FinalServiceCost=CASE WHEN HasWorkshopPricing=0 THEN NULL WHEN DiscountAmount>LaborCost THEN 0 ELSE LaborCost-DiscountAmount END WHERE Id=@Id;
            END;
            UPDATE dbo.MaintenanceOrders SET OrderStatus=6,CancellationReason=@Notes,UpdatedAtUtc=@At WHERE Id=@Id;
            SELECT @Code=N'Cancel',@Title=N'إلغاء أمر الصيانة';
        END;
        EXEC dbo.WriteMaintenanceMoney @Id,@Actor,@At,@Stage,@Cash,@Vodafone,@InstaPay,@Refund,@Title;
        SET @Details=LEFT(CONCAT(N'المحصل سابقًا: ',@Paid,N' · تحصيل جديد: ',@Collect,N' · رد: ',@Refund,N' · ',@Notes),1500);
        DECLARE @Amount decimal(19,2)=@Collect-@Refund;
        EXEC dbo.LogMaintenancePosting @Id,@Actor,@At,@Code,@Title,@Details,@Amount;
        DECLARE @Posted binary(8)=(SELECT RowVersion FROM dbo.MaintenanceOrders WHERE Id=@Id);
        COMMIT;
        SELECT @Posted;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH
END;
GO
-- Read-only on the header. A preview left open through a reopen cannot log a new delivery receipt.
CREATE OR ALTER PROCEDURE dbo.AppendMaintenancePrint @Id int,@Version varbinary(8000),@Actor int,@Mode tinyint
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.LockMaintenanceTicket @Id,@Version,NULL;
        IF @Mode IS NULL OR @Mode NOT IN (0,1,2) OR (@Mode=2 AND NOT EXISTS(SELECT 1 FROM dbo.MaintenanceOrders WHERE Id=@Id AND OrderStatus=5))
            THROW 50242,N'لا يمكن طباعة هذا الإيصال لحالة الأمر الحالية.',1;
        DECLARE @At datetime2(7)=SYSUTCDATETIME();
        INSERT dbo.MaintenanceActivities(MaintenanceOrderId,OccurredAtUtc,ActionTitle,Details,ActorUserId)
            VALUES(@Id,@At,N'معاينة / طباعة مستند صيانة',CASE @Mode WHEN 0 THEN N'إيصال الاستلام' WHEN 1 THEN N'ملصق الجهاز' ELSE N'إيصال التسليم والضمان' END,@Actor);
        COMMIT;
        SELECT @At;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH
END;
GO
-- No ordinary till grant. A supervisor connection must be separately authorized by the DBA.
CREATE OR ALTER PROCEDURE dbo.ReopenMaintenanceTicket @Id int,@Version varbinary(8000),@Actor int,@Reason nvarchar(1500)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET ARITHABORT ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF ISNULL(IS_ROLEMEMBER(N'ProjectZMaintenanceSupervisor'),0)<>1 OR USER_NAME()=N'ProjectZClient'
            THROW 50248,N'إعادة الفتح تتطلب هوية اتصال المشرف المستقلة.',1;
        EXEC dbo.LockMaintenanceTicket @Id,@Version,1;
        IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 50242,N'اكتب سبب إعادة الفتح للمراجعة.',1;
        IF EXISTS (SELECT 1 FROM dbo.MaintenanceOrders closed JOIN dbo.MaintenanceOrders active WITH (UPDLOCK,HOLDLOCK)
                   ON active.SerialOrImei=closed.SerialOrImei AND active.OrderStatus<5 WHERE closed.Id=@Id AND closed.SerialOrImei<>N'')
            THROW 50242,N'يوجد أمر آخر مفتوح لنفس الجهاز.',1;
        DECLARE @At datetime2(7)=SYSUTCDATETIME(), @Before nvarchar(max)=(SELECT OrderNumber,OrderStatus,AssignedTechnician,Priority,ExpectedDeliveryAtUtc,ExpectedDeliveryLabel,DiagnosisNotes,IsEstimatePendingInspection,EstimatedCost,HasWorkshopPricing,DeliveredAtUtc,WarrantyDays,DeliveryNotes,CancellationReason,LaborCost,DiscountAmount,FinalServiceCost,
            JSON_QUERY((SELECT Id,LineNumber,IsManualPart,SparePartId,ProductId,BarcodeSnapshot,ProductNameSnapshot,Quantity,UnitCost,UnitSellPrice FROM dbo.MaintenanceOrderParts WHERE MaintenanceOrderId=@Id ORDER BY Id FOR JSON PATH)) AS Parts,
            JSON_QUERY((SELECT Id,FinancialAccountId,PaymentStage,PaymentKind,PaidAtUtc,ActorUserId,Amount,RefundOfPaymentId FROM dbo.MaintenancePayments WHERE MaintenanceOrderId=@Id ORDER BY Id FOR JSON PATH)) AS Payments
            FROM dbo.MaintenanceOrders WHERE Id=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        UPDATE dbo.MaintenanceOrders SET OrderStatus=1,DeliveredAtUtc=NULL,DeliveryNotes=N'',WarrantyDays=0,CancellationReason=N'',UpdatedAtUtc=@At WHERE Id=@Id;
        EXEC dbo.LogMaintenancePosting @Id,@Actor,@At,N'Reopen',N'إعادة فتح بإذن المشرف',@Reason,NULL,@Before;
        DECLARE @Posted binary(8)=(SELECT RowVersion FROM dbo.MaintenanceOrders WHERE Id=@Id);
        COMMIT;
        SELECT @Posted;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK; THROW;
    END CATCH
END;
GO
GRANT SELECT ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.MaintenanceOrderParts TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.MaintenancePayments TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.MaintenanceOrders TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.MaintenanceOrderParts TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.MaintenancePayments TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.MaintenanceActivities TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.vwMaintenanceOrderSummary TO [ProjectZClient];
DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.MaintenanceActivities TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.AppendMaintenancePrint TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.OpenMaintenanceTicket TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.SaveMaintenanceWorkshop TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.ChangeMaintenancePart TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.SettleMaintenanceTicket TO [ProjectZClient];
DENY EXECUTE ON OBJECT::dbo.ReopenMaintenanceTicket TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.ReopenMaintenanceTicket TO [ProjectZMaintenanceSupervisor];
GO
