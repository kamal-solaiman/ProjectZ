/* Customer wallet transfers, SQL Server 2016 SP1+. Stop old clients before upgrading.
   No historical header, balance or ledger entry is rewritten. No external transfer number. */
USE [ProjectZ];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
IF OBJECT_ID(N'dbo.CustomerTransfers',N'U') IS NULL OR DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50230, N'شغّل إعداد قاعدة البيانات وحساب ProjectZClient أولًا.', 1;
IF COL_LENGTH(N'dbo.CustomerTransfers',N'CorrectsTransferId') IS NULL
    ALTER TABLE dbo.CustomerTransfers ADD CorrectsTransferId int NULL REFERENCES dbo.CustomerTransfers(Id);
IF COL_LENGTH(N'dbo.CustomerTransfers',N'CashAccountId') IS NULL
    ALTER TABLE dbo.CustomerTransfers ADD CashAccountId int NULL REFERENCES dbo.FinancialAccounts(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerTransfers') AND name=N'UX_CustomerTransfers_Corrects')
    CREATE UNIQUE INDEX UX_CustomerTransfers_Corrects ON dbo.CustomerTransfers(CorrectsTransferId) WHERE CorrectsTransferId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FinancialTransactions') AND name=N'IX_FinancialTransactions_SourceAccount')
    CREATE INDEX IX_FinancialTransactions_SourceAccount ON dbo.FinancialTransactions(SourceEntityType, SourceEntityId, FinancialAccountId, Id) INCLUDE (IsIncoming, Amount, Category);
GO
-- Private to PostCustomerTransfer. The caller holds the transfer and account locks. For an
-- application, Id returns the new header; for a counter-entry, Id is the original header.
CREATE OR ALTER PROCEDURE dbo.WriteCustomerTransferEffect
    @Undo bit, @Id int OUTPUT, @Predecessor int, @Wallet int, @Cash int, @Kind tinyint,
    @Customer nvarchar(30), @Amount decimal(19,2), @Commission decimal(19,2), @CashDelta decimal(19,2),
    @Note nvarchar(1000), @Actor int, @WalletAfter decimal(19,2) OUTPUT, @CashAfter decimal(19,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @WalletDelta decimal(19,2) = CASE WHEN @Kind=1 THEN -@Amount ELSE @Amount END;
    IF @Undo=1 SELECT @WalletDelta=-@WalletDelta, @CashDelta=-@CashDelta;

    UPDATE dbo.FinancialAccounts SET @WalletAfter=CurrentBalance=CurrentBalance+@WalletDelta, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Wallet;
    IF @CashDelta<>0
        UPDATE dbo.FinancialAccounts SET @CashAfter=CurrentBalance=CurrentBalance+@CashDelta, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Cash;
    ELSE
        SELECT @CashAfter=CurrentBalance FROM dbo.FinancialAccounts WHERE Id=@Cash;

    IF @Undo=1
        UPDATE dbo.CustomerTransfers SET OperationStatus=1, ReversedAtUtc=SYSUTCDATETIME(), ReversedByUserId=@Actor WHERE Id=@Id;
    ELSE
    BEGIN
        INSERT dbo.CustomerTransfers(FinancialAccountId,CashAccountId,OperationKind,CustomerNumber,Amount,Commission,CashDelta,
            WalletBalanceAfter,Note,ActorUserId,CorrectsTransferId)
        VALUES(@Wallet,@Cash,@Kind,@Customer,@Amount,@Commission,@CashDelta,@WalletAfter,@Note,@Actor,@Predecessor);
        SET @Id=CONVERT(int,SCOPE_IDENTITY());
    END;

    INSERT dbo.FinancialTransactions(FinancialAccountId,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
                                     ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
        SELECT AccountId,CASE WHEN Delta>0 THEN 1 ELSE 0 END,Category,ABS(Delta),BalanceAfter,@Customer,@Note,@Actor,1,N'CustomerTransfer',@Id
        FROM (VALUES
            (@Wallet,@WalletDelta,@WalletAfter,CASE WHEN @Undo=1 THEN N'عكس تحويل' WHEN @Kind=1 THEN N'تحويل للعميل' ELSE N'سحب للعميل' END),
            (@Cash,@CashDelta,@CashAfter,CASE WHEN @Undo=1 THEN N'عكس تحويل' WHEN @Kind=1 THEN N'تحويل من العميل' ELSE N'تحويل للعميل' END)
        ) legs(AccountId,Delta,BalanceAfter,Category) WHERE Delta<>0;
END;
GO
-- Action 1: issue, 2: counter and replace, 3: reverse (including the records screen's "delete").
-- No public parameter describes the old money, counterparty, cash box or wallet.
CREATE OR ALTER PROCEDURE dbo.PostCustomerTransfer
    @Action tinyint, @Provider tinyint, @Actor int, @TransferId int=NULL, @ExpectedVersion varbinary(8000)=NULL,
    @NewWallet int=NULL, @NewKind tinyint=NULL, @NewCustomer nvarchar(30)=NULL,
    @NewAmount decimal(19,2)=NULL, @NewCommission decimal(19,2)=NULL, @Note nvarchar(1000)=N''
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET ARITHABORT ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF @Action IS NULL OR @Action NOT IN (1,2,3) OR @Provider IS NULL OR @Provider NOT IN (1,2)
           OR (@Action=1 AND (@TransferId IS NOT NULL OR @ExpectedVersion IS NOT NULL))
            THROW 50231, N'طلب تحويل المحفظة غير صالح.', 1;
        IF @Action IN (1,2) AND (@NewWallet IS NULL OR ISNULL(@NewKind,0) NOT IN (1,2) OR ISNULL(@NewAmount,0)<=0
                                  OR ISNULL(@NewCommission,-1)<0 OR NULLIF(LTRIM(RTRIM(@NewCustomer)),N'') IS NULL)
            THROW 50231, N'راجع نوع التحويل وحساب المحفظة ورقم العميل والمبلغ والعمولة.', 1;

        DECLARE @OldWallet int, @SavedCash int, @OldKind tinyint, @OldCustomer nvarchar(30), @OldAmount decimal(19,2),
                @OldCommission decimal(19,2), @OldCashDelta decimal(19,2), @OldStatus tinyint, @Version binary(8);
        IF @Action IN (2,3)
        BEGIN
            SELECT @OldWallet=FinancialAccountId, @SavedCash=CashAccountId, @OldKind=OperationKind, @OldCustomer=CustomerNumber,
                   @OldAmount=Amount, @OldCommission=Commission, @OldCashDelta=CashDelta, @OldStatus=OperationStatus, @Version=RowVersion
            FROM dbo.CustomerTransfers WITH (UPDLOCK,HOLDLOCK) WHERE Id=@TransferId;
            IF @Version IS NULL OR @OldStatus<>0 OR @ExpectedVersion IS NULL OR DATALENGTH(@ExpectedVersion)<>8 OR @Version<>@ExpectedVersion
                THROW 50232, N'تغيّرت العملية أو عُكست بالفعل. حدّث السجلات وافتح العملية الحالية من جديد.', 1;
        END;

        -- SingleCash is unique. Lock it before the wallets in every workflow. Never substitute
        -- a provider default, create a missing account, or require the unrelated provider to be live.
        DECLARE @Cash int;
        SELECT @Cash=Id FROM dbo.FinancialAccounts WITH (UPDLOCK,HOLDLOCK) WHERE AccountKind=0 AND IsActive=1;
        IF @Cash IS NULL OR (@SavedCash IS NOT NULL AND @SavedCash<>@Cash)
            THROW 50233, N'خزنة العملية غير متاحة. راجع حساب النقدية في «حساباتي».', 1;
        IF @Action IN (2,3) AND NOT EXISTS (SELECT 1 FROM dbo.FinancialAccounts WITH (UPDLOCK,HOLDLOCK)
                                           WHERE Id=@OldWallet AND AccountKind=@Provider AND IsActive=1)
            THROW 50234, N'المحفظة الأصلية موقوفة أو لا تخص هذا السجل. راجع الحساب ثم حدّث السجلات.', 1;
        IF @Action IN (1,2) AND NOT EXISTS (SELECT 1 FROM dbo.FinancialAccounts WITH (UPDLOCK,HOLDLOCK)
                                           WHERE Id=@NewWallet AND AccountKind=@Provider AND IsActive=1)
            THROW 50234, N'المحفظة المحددة موقوفة أو لا تخص هذا النوع من التحويلات.', 1;

        DECLARE @Id int=@TransferId, @WalletAfter decimal(19,2), @CashAfter decimal(19,2), @Before nvarchar(max), @After nvarchar(max);
        IF @Action IN (2,3)
        BEGIN
            -- Legacy clients edited in place. A coherent history can still be reversed; if its net
            -- per account differs from the header, do not guess which historic posting was right.
            DECLARE @Net TABLE(AccountId int PRIMARY KEY, Delta decimal(38,2));
            INSERT @Net SELECT FinancialAccountId,SUM(CASE WHEN IsIncoming=1 THEN Amount ELSE -Amount END)
                FROM dbo.FinancialTransactions WITH (HOLDLOCK) WHERE SourceEntityType=N'CustomerTransfer' AND SourceEntityId=@Id
                GROUP BY FinancialAccountId;
            IF ISNULL((SELECT Delta FROM @Net WHERE AccountId=@OldWallet),0)<>CASE WHEN @OldKind=1 THEN -@OldAmount ELSE @OldAmount END
               OR ISNULL((SELECT Delta FROM @Net WHERE AccountId=@Cash),0)<>@OldCashDelta
               OR @OldCashDelta<>CASE WHEN @OldKind=1 THEN @OldAmount+@OldCommission ELSE @OldCommission-@OldAmount END
               OR EXISTS (SELECT 1 FROM @Net WHERE AccountId NOT IN (@OldWallet,@Cash) AND Delta<>0)
                THROW 50235, N'أثر التحويل في الدفتر لا يطابق السجل. يلزم مطابقة حسابية قبل العكس أو التعديل.', 1;

            SET @Before=(SELECT Id,FinancialAccountId,CashAccountId,OperationKind,CustomerNumber,Amount,Commission,CashDelta,OperationStatus,Note,
                                CorrectsTransferId,CONVERT(varchar(18),RowVersion,1) AS RowVersion
                         FROM dbo.CustomerTransfers WHERE Id=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
            DECLARE @UndoNote nvarchar(1000)=CASE WHEN @Action=2 THEN CONCAT(N'عكس قبل تصحيح التحويل رقم ',@Id) ELSE @Note END;
            EXEC dbo.WriteCustomerTransferEffect 1,@Id OUTPUT,NULL,@OldWallet,@Cash,@OldKind,@OldCustomer,@OldAmount,@OldCommission,@OldCashDelta,
                                                 @UndoNote,@Actor,@WalletAfter OUTPUT,@CashAfter OUTPUT;
        END;
        IF @Action IN (1,2)
        BEGIN
            DECLARE @NewCashDelta decimal(19,2)=CASE WHEN @NewKind=1 THEN @NewAmount+@NewCommission ELSE @NewCommission-@NewAmount END;
            EXEC dbo.WriteCustomerTransferEffect 0,@Id OUTPUT,@TransferId,@NewWallet,@Cash,@NewKind,@NewCustomer,@NewAmount,@NewCommission,@NewCashDelta,
                                                 @Note,@Actor,@WalletAfter OUTPUT,@CashAfter OUTPUT;
        END;
        SELECT @Version=RowVersion FROM dbo.CustomerTransfers WHERE Id=@Id;
        SET @After=(SELECT Id,FinancialAccountId,CashAccountId,OperationKind,CustomerNumber,Amount,Commission,CashDelta,OperationStatus,Note,
                           CorrectsTransferId,ReversedAtUtc,ReversedByUserId,CONVERT(varchar(18),RowVersion,1) AS RowVersion
                    FROM dbo.CustomerTransfers WHERE Id=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,OldValuesJson,NewValuesJson)
        VALUES(@Actor,N'Finance',CASE @Action WHEN 1 THEN N'CreateCustomerTransfer' WHEN 2 THEN N'CorrectCustomerTransfer' ELSE N'ReverseCustomerTransfer' END,
               N'CustomerTransfer',CONVERT(nvarchar(100),ISNULL(@TransferId,@Id)),@Before,@After);
        COMMIT;
        SELECT @Id AS TransferId,CASE WHEN @Action=3 THEN @OldWallet ELSE @NewWallet END AS WalletAccountId,
               @Cash AS CashAccountId,@WalletAfter AS WalletBalanceAfter,@CashAfter AS CashBalanceAfter,@Version AS RowVersion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
GRANT SELECT ON OBJECT::dbo.CustomerTransfers TO [ProjectZClient];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.CustomerTransfers TO [ProjectZClient];
DENY EXECUTE ON OBJECT::dbo.WriteCustomerTransferEffect TO [ProjectZClient];
GRANT EXECUTE ON OBJECT::dbo.PostCustomerTransfer TO [ProjectZClient];
GO
