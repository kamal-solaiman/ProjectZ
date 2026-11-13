using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the accounts layer; the message is safe to show in the UI.</summary>
public sealed class FinancialDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>Result of a wallet editor save (add or update).</summary>
public sealed record SavedAccount(
    int AccountId,
    FinancialAccountKind Kind,
    string Alias,
    string Identifier,
    decimal OpeningBalance,
    decimal DailyDepositLimit,
    decimal DailyWithdrawalLimit,
    decimal MonthlyDepositLimit,
    decimal MonthlyWithdrawalLimit,
    string Notes);

/// <summary>Result of a single movement save (deposit, withdrawal or adjustment).</summary>
public sealed record SavedTransaction(int TransactionId, int AccountId, decimal BalanceAfter);

/// <summary>Result of a customer wallet transfer (Vodafone Cash / InstaPay).</summary>
public sealed record SavedCustomerTransfer(
    int TransferId,
    int WalletAccountId,
    int CashAccountId,
    decimal WalletBalanceAfter,
    decimal CashBalanceAfter);

/// <summary>SQL Server persistence for the financial accounts, their ledger movements and customer wallet transfers.</summary>
public sealed class FinancialRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    // ---------------------------------------------------------------- accounts

    /// <summary>Loads the active financial accounts into the given collection.</summary>
    public void LoadAccountsInto(ObservableCollection<FinancialAccountDesignItem> target)
    {
        var loaded = new List<FinancialAccountDesignItem>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT Id, AccountKind, Alias, ISNULL(Identifier,N''), CurrentBalance, DailyDepositLimit, DailyWithdrawalLimit,
       MonthlyDepositLimit, MonthlyWithdrawalLimit, ISNULL(Notes,N''), IsDefault, IsActive
FROM dbo.FinancialAccounts
WHERE IsActive = 1
ORDER BY IsDefault DESC, Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                loaded.Add(new FinancialAccountDesignItem
                {
                    Id = reader.GetInt32(0),
                    Kind = (FinancialAccountKind)reader.GetByte(1),
                    Alias = reader.GetString(2),
                    Identifier = reader.GetString(3),
                    Balance = reader.GetDecimal(4),
                    DailyDepositLimit = reader.GetDecimal(5),
                    DailyWithdrawalLimit = reader.GetDecimal(6),
                    MonthlyDepositLimit = reader.GetDecimal(7),
                    MonthlyWithdrawalLimit = reader.GetDecimal(8),
                    Notes = reader.GetString(9),
                    IsDefault = reader.GetBoolean(10),
                    IsActive = reader.GetBoolean(11)
                });
            }
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تحميل الحسابات المالية من قاعدة البيانات.");
        }

        target.Clear();
        foreach (var item in loaded) target.Add(item);
    }

    /// <summary>
    /// Loads the ledger movements, newest first. When accountId is 0, loads the movements of every account
    /// so the aggregate KPIs (today's in/out) reflect the whole system like the local-design mode.
    /// </summary>
    public void LoadTransactionsInto(ObservableCollection<FinancialTransactionDesignItem> target, int accountId)
    {
        var loaded = new List<FinancialTransactionDesignItem>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = accountId == 0
                ? @"
SELECT Id, FinancialAccountId, OccurredAtUtc, IsIncoming, Category, Amount, BalanceAfter,
       ISNULL(Counterparty,N''), ISNULL(Note,N'')
FROM dbo.FinancialTransactions
ORDER BY OccurredAtUtc DESC, Id DESC;"
                : @"
SELECT Id, FinancialAccountId, OccurredAtUtc, IsIncoming, Category, Amount, BalanceAfter,
       ISNULL(Counterparty,N''), ISNULL(Note,N'')
FROM dbo.FinancialTransactions
WHERE FinancialAccountId = @Account
ORDER BY OccurredAtUtc DESC, Id DESC;";
            if (accountId != 0) command.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                loaded.Add(new FinancialTransactionDesignItem
                {
                    Id = reader.GetInt32(0),
                    AccountId = reader.GetInt32(1),
                    Timestamp = reader.GetDateTime(2).ToLocalTime(),
                    IsIncoming = reader.GetBoolean(3),
                    Category = reader.GetString(4),
                    Amount = reader.GetDecimal(5),
                    BalanceAfter = reader.GetDecimal(6),
                    Counterparty = reader.GetString(7),
                    Note = reader.GetString(8),
                    CountsTowardLimits = true
                });
            }
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تحميل حركات الحساب من قاعدة البيانات.");
        }

        target.Clear();
        foreach (var item in loaded) target.Add(item);
    }

    /// <summary>Adds a new account or updates an existing one; returns the persisted row.</summary>
    public SavedAccount SaveAccount(
        int? existingAccountId,
        FinancialAccountKind kind,
        string alias,
        string identifier,
        decimal openingBalance,
        decimal dailyDepositLimit,
        decimal dailyWithdrawalLimit,
        decimal monthlyDepositLimit,
        decimal monthlyWithdrawalLimit,
        string notes,
        bool makeDefault)
    {
        alias = Trim(alias, 200, "");
        identifier = kind == FinancialAccountKind.Cash ? "" : Trim(identifier, 200, "");
        notes = Trim(notes, 1000, "");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            if (existingAccountId is null)
            {
                var accountId = InsertAccount(connection, transaction, kind, alias, identifier,
                    openingBalance, dailyDepositLimit, dailyWithdrawalLimit, monthlyDepositLimit,
                    monthlyWithdrawalLimit, notes, makeDefault);
                transaction.Commit();
                return new SavedAccount(accountId, kind, alias, identifier, openingBalance,
                    dailyDepositLimit, dailyWithdrawalLimit, monthlyDepositLimit, monthlyWithdrawalLimit, notes);
            }

            UpdateAccount(connection, transaction, existingAccountId.Value, kind, alias, identifier,
                dailyDepositLimit, dailyWithdrawalLimit, monthlyDepositLimit, monthlyWithdrawalLimit, notes, makeDefault);
            transaction.Commit();
            return new SavedAccount(existingAccountId.Value, kind, alias, identifier, openingBalance,
                dailyDepositLimit, dailyWithdrawalLimit, monthlyDepositLimit, monthlyWithdrawalLimit, notes);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حفظ المحفظة المالية.");
        }
    }

    /// <summary>
    /// Deletes an account permanently, but only when it is a completed wallet with no balance and no movements.
    /// A wallet that has a ledger is deactivated (IsActive = 0) instead of being deleted.
    /// </summary>
    public (bool Deleted, string Message) DeleteAccount(int accountId, string alias)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            decimal balance;
            using (var check = CreateCommand(connection, transaction, @"
SELECT CurrentBalance FROM dbo.FinancialAccounts WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND IsActive = 1;"))
            {
                check.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                balance = (decimal)check.ExecuteScalar()!;
            }

            int movementCount;
            using (var count = CreateCommand(connection, transaction, @"
SELECT COUNT(1) FROM dbo.FinancialTransactions WHERE FinancialAccountId = @Id;"))
            {
                count.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                movementCount = (int)count.ExecuteScalar()!;
            }

            if (balance == 0m && movementCount == 0)
            {
                using var delete = CreateCommand(connection, transaction, @"
DELETE FROM dbo.FinancialAccounts WHERE Id = @Id;");
                delete.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                delete.ExecuteNonQuery();
                transaction.Commit();
                return (true, $"تم حذف محفظة {alias} نهائيًا.");
            }

            using var deactivate = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET IsActive = 0, IsDefault = CASE WHEN IsDefault = 1 THEN 0 ELSE IsDefault END,
     UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id;");
            deactivate.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
            deactivate.ExecuteNonQuery();
            transaction.Commit();
            return (false, $"يوجد سجل لـ {alias}، تم إيقافه مؤقتًا بدل الحذف النهائي.");
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حذف المحفظة المالية.");
        }
    }

    // ---------------------------------------------------------------- transactions

    /// <summary>Posts a deposit, withdrawal or adjustment movement and returns the new balance.</summary>
    public SavedTransaction SaveTransaction(
        int accountId,
        FinancialMovementKind kind,
        decimal amount,
        string counterparty,
        string note,
        bool countsTowardLimits)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
            throw new FinancialDataException("المبلغ يجب أن يكون أكبر من الصفر.");

        counterparty = Trim(counterparty, 250, "");
        note = Trim(note, 1000, "");

        var isIncoming = kind is FinancialMovementKind.Deposit or FinancialMovementKind.PositiveAdjustment;
        var category = kind switch
        {
            FinancialMovementKind.Deposit => "إيداع",
            FinancialMovementKind.Withdrawal => "سحب",
            FinancialMovementKind.PositiveAdjustment => "تعديل إيجابي",
            _ => "تعديل سلبي"
        };
        var signed = isIncoming ? amount : -amount;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            decimal balanceAfter;
            using (var balance = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Signed, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
            {
                AddMoney(balance, "@Signed", signed);
                balance.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                balanceAfter = (decimal)balance.ExecuteScalar()!;
            }

            int transactionId;
            using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
OUTPUT inserted.Id
VALUES(@Account,SYSUTCDATETIME(),@Incoming,@Category,@Amount,@Balance,@Counterparty,@Note,@Actor,@Limits,NULL,NULL);"))
            {
                insert.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
                insert.Parameters.Add("@Incoming", SqlDbType.Bit).Value = isIncoming;
                insert.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = category;
                AddMoney(insert, "@Amount", amount);
                AddMoney(insert, "@Balance", balanceAfter);
                insert.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value = counterparty;
                insert.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = note;
                insert.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                insert.Parameters.Add("@Limits", SqlDbType.Bit).Value = countsTowardLimits;
                transactionId = (int)insert.ExecuteScalar()!;
            }

            transaction.Commit();
            return new SavedTransaction(transactionId, accountId, balanceAfter);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حفظ حركة الحساب المالي.");
        }
    }

    // ---------------------------------------------------------------- customer transfers

    /// <summary>
    /// Executes a customer wallet transfer (deposit to customer or withdrawal for customer), updates the
    /// wallet and cash balances and records the transfer plus its ledger movements in one transaction.
    /// </summary>
    public SavedCustomerTransfer ExecuteCustomerTransfer(
        FinancialAccountKind walletKind,
        int walletAccountId,
        string operationKind,
        string customerNumber,
        decimal amount,
        decimal commission,
        string note)
    {
        customerNumber = Trim(customerNumber, 30, "");
        note = Trim(note, 1000, "");
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        commission = Math.Round(commission, 2, MidpointRounding.AwayFromZero);

        if (amount <= 0m)
            throw new FinancialDataException("المبلغ يجب أن يكون أكبر من الصفر.");

        var isDeposit = operationKind == "DepositToCustomer";
        // Match the local-design semantics: deposit takes money out of the wallet line and puts
        // amount + commission into cash; withdrawal puts amount into the wallet line and takes
        // amount - commission out of cash (net cash delta = commission - amount).
        var walletDelta = isDeposit ? -amount : amount;
        var cashDelta = isDeposit ? amount + commission : commission - amount;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            var accounts = EnsureAccounts(connection, transaction);
            var walletAccount = EnsureAccountById(connection, transaction, walletAccountId, walletKind);
            var cashAccount = accounts[FinancialAccountKind.Cash];

            decimal walletBalanceAfter;
            using (var wallet = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Signed, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
            {
                AddMoney(wallet, "@Signed", walletDelta);
                wallet.Parameters.Add("@Id", SqlDbType.Int).Value = walletAccount;
                walletBalanceAfter = (decimal)wallet.ExecuteScalar()!;
            }

            decimal cashBalanceAfter;
            using (var cash = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Signed, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
            {
                AddMoney(cash, "@Signed", cashDelta);
                cash.Parameters.Add("@Id", SqlDbType.Int).Value = cashAccount;
                cashBalanceAfter = (decimal)cash.ExecuteScalar()!;
            }

            int transferId;
            using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.CustomerTransfers(FinancialAccountId,OperationKind,CustomerNumber,Amount,Commission,CashDelta,
 WalletBalanceAfter,OperationStatus,Note,OccurredAtUtc,ActorUserId)
OUTPUT inserted.Id
VALUES(@Account,@OpKind,@Customer,@Amount,@Commission,@CashDelta,@WalletBalance,0,@Note,SYSUTCDATETIME(),@Actor);"))
            {
                insert.Parameters.Add("@Account", SqlDbType.Int).Value = walletAccount;
                insert.Parameters.Add("@OpKind", SqlDbType.TinyInt).Value = isDeposit ? (byte)1 : (byte)2;
                insert.Parameters.Add("@Customer", SqlDbType.NVarChar, 30).Value = customerNumber;
                AddMoney(insert, "@Amount", amount);
                AddMoney(insert, "@Commission", commission);
                AddMoney(insert, "@CashDelta", cashDelta);
                AddMoney(insert, "@WalletBalance", walletBalanceAfter);
                insert.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = note;
                insert.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                transferId = (int)insert.ExecuteScalar()!;
            }

            RecordWalletMovement(connection, transaction, walletAccount, walletBalanceAfter,
                walletDelta, isDeposit ? "تحويل للعميل" : "سحب للعميل", customerNumber,
                isDeposit ? note : $"{note} (سحب للعميل)", actorUserId);

            RecordWalletMovement(connection, transaction, cashAccount, cashBalanceAfter,
                cashDelta, isDeposit ? "تحويل من العميل" : "تحويل للعميل", customerNumber,
                isDeposit ? $"{note} (إيداع من العميل)" : note, actorUserId);

            transaction.Commit();
            return new SavedCustomerTransfer(transferId, walletAccount, cashAccount, walletBalanceAfter, cashBalanceAfter);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تنفيذ التحويل المالي.");
        }
    }

    /// <summary>
    /// Reverses a customer transfer: marks it reversed and applies the opposite financial effect on the
    /// wallet and cash balances. walletDelta/cashDelta are the original signed effects of the transfer; the
    /// method applies their negation to the current balances read from the database.
    /// </summary>
    public void ReverseCustomerTransfer(
        int transferId,
        int walletAccountId,
        FinancialAccountKind walletKind,
        decimal walletDelta,
        decimal cashDelta,
        string note)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            var accounts = EnsureAccounts(connection, transaction);
            var walletAccount = EnsureAccountById(connection, transaction, walletAccountId, walletKind);
            var cashAccount = accounts[FinancialAccountKind.Cash];

            decimal walletBalanceAfter;
            using (var wallet = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Signed, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
            {
                AddMoney(wallet, "@Signed", -walletDelta);
                wallet.Parameters.Add("@Id", SqlDbType.Int).Value = walletAccount;
                var walletResult = wallet.ExecuteScalar();
                if (walletResult is null or DBNull)
                    throw new FinancialDataException("تعذّر عكس التحويل: المحفظة غير موجودة.");
                walletBalanceAfter = (decimal)walletResult;
            }

            decimal cashBalanceAfter;
            using (var cash = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Signed, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
            {
                AddMoney(cash, "@Signed", -cashDelta);
                cash.Parameters.Add("@Id", SqlDbType.Int).Value = cashAccount;
                var cashResult = cash.ExecuteScalar();
                if (cashResult is null or DBNull)
                    throw new FinancialDataException("تعذّر عكس التحويل: حساب النقد غير موجود.");
                cashBalanceAfter = (decimal)cashResult;
            }

            RecordWalletMovement(connection, transaction, walletAccount, walletBalanceAfter,
                -walletDelta, "عكس تحويل", note, actorUserId);
            RecordWalletMovement(connection, transaction, cashAccount, cashBalanceAfter,
                -cashDelta, "عكس تحويل", note, actorUserId);

            using (var reverse = CreateCommand(connection, transaction, @"
UPDATE dbo.CustomerTransfers SET OperationStatus = 1, ReversedAtUtc = SYSUTCDATETIME(), ReversedByUserId = @Actor,
    WalletBalanceAfter = @WalletBalance
WHERE Id = @Id AND OperationStatus = 0;"))
            {
                reverse.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                AddMoney(reverse, "@WalletBalance", walletBalanceAfter);
                reverse.Parameters.Add("@Id", SqlDbType.Int).Value = transferId;
                if (reverse.ExecuteNonQuery() == 0)
                    throw new FinancialDataException("تعذّر عكس التحويل: العملية معكوسة بالفعل.");
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر عكس التحويل المالي.");
        }
    }

    /// <summary>Loads customer transfer operations of one wallet kind, newest first.</summary>
    public void LoadCustomerTransfersInto(
        ObservableCollection<VodafoneTransferOperation> vodafoneTarget,
        ObservableCollection<InstapayTransferOperation> instapayTarget)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT t.Id, t.FinancialAccountId, ISNULL(a.Alias,N''), a.AccountKind, t.OperationKind, t.CustomerNumber, t.Amount, t.Commission,
       t.CashDelta, t.WalletBalanceAfter, t.OperationStatus, ISNULL(t.Note,N''), t.OccurredAtUtc,
       ISNULL(ru.DisplayName,N''), t.ReversedAtUtc, ISNULL(au.DisplayName,N'')
FROM dbo.CustomerTransfers t
LEFT JOIN dbo.FinancialAccounts a ON a.Id = t.FinancialAccountId
LEFT JOIN dbo.Users ru ON ru.Id = t.ReversedByUserId
LEFT JOIN dbo.Users au ON au.Id = t.ActorUserId
ORDER BY t.OccurredAtUtc DESC, t.Id DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var accountId = reader.GetInt32(1);
                var alias = reader.GetString(2);
                var accountKind = reader.GetByte(3);
                var opKind = (byte)reader.GetByte(4);
                var customerNumber = reader.GetString(5);
                var amount = reader.GetDecimal(6);
                var commission = reader.GetDecimal(7);
                var cashDelta = reader.GetDecimal(8);
                var walletBalanceAfter = reader.GetDecimal(9);
                var status = reader.GetByte(10);
                var note = reader.GetString(11);
                var occurredAt = reader.GetDateTime(12).ToLocalTime();
                var reversedBy = reader.GetString(13);
                var reversedAt = reader.IsDBNull(14) ? (DateTime?)null : reader.GetDateTime(14).ToLocalTime();
                var actorName = reader.GetString(15);

                var isDeposit = opKind == 1;
                var isReversed = status == 1;

                if (accountKind == (byte)FinancialAccountKind.VodafoneCash)
                {
                    var vodafoneKind = isDeposit ? VodafoneOperationKind.DepositToCustomer : VodafoneOperationKind.WithdrawForCustomer;
                    vodafoneTarget.Add(new VodafoneTransferOperation
                    {
                        Id = id,
                        WalletId = accountId,
                        WalletAlias = alias,
                        Kind = vodafoneKind,
                        CustomerNumber = customerNumber,
                        Amount = amount,
                        Commission = commission,
                        CashDelta = cashDelta,
                        WalletBalanceAfter = walletBalanceAfter,
                        Note = note,
                        Timestamp = occurredAt,
                        ActorName = actorName,
                        IsReversed = isReversed,
                        ReversedBy = reversedBy,
                        ReversedAt = reversedAt
                    });
                }
                else if (accountKind == (byte)FinancialAccountKind.InstaPay)
                {
                    var instapayKind = isDeposit ? InstapayOperationKind.DepositToCustomer : InstapayOperationKind.WithdrawForCustomer;
                    instapayTarget.Add(new InstapayTransferOperation
                    {
                        Id = id,
                        WalletId = accountId,
                        WalletAlias = alias,
                        Kind = instapayKind,
                        CustomerNumber = customerNumber,
                        Amount = amount,
                        Commission = commission,
                        CashDelta = cashDelta,
                        WalletBalanceAfter = walletBalanceAfter,
                        Note = note,
                        Timestamp = occurredAt,
                        ActorName = actorName,
                        IsReversed = isReversed,
                        ReversedBy = reversedBy,
                        ReversedAt = reversedAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تحميل سجلات التحويلات من قاعدة البيانات.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void RecordWalletMovement(
        SqlConnection connection, SqlTransaction transaction, int accountId, decimal balanceAfter,
        decimal signedAmount, string category, string counterparty, string note, int actor)
    {
        using var ledger = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),@Incoming,@Category,@Amount,@Balance,@Counterparty,@Note,@Actor,1,N'CustomerTransfer',NULL);");
        ledger.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        ledger.Parameters.Add("@Incoming", SqlDbType.Bit).Value = signedAmount > 0m;
        ledger.Parameters.Add("@Category", SqlDbType.NVarChar, 50).Value = category;
        AddMoney(ledger, "@Amount", Math.Abs(signedAmount));
        AddMoney(ledger, "@Balance", balanceAfter);
        ledger.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value = counterparty;
        ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = note;
        ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actor;
        ledger.ExecuteNonQuery();
    }

    private static int InsertAccount(
        SqlConnection connection, SqlTransaction transaction, FinancialAccountKind kind, string alias, string identifier,
        decimal openingBalance, decimal dailyDepositLimit, decimal dailyWithdrawalLimit,
        decimal monthlyDepositLimit, decimal monthlyWithdrawalLimit, string notes, bool makeDefault)
    {
        using var insert = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,CurrentBalance,DailyDepositLimit,DailyWithdrawalLimit,
 MonthlyDepositLimit,MonthlyWithdrawalLimit,Notes,IsActive,IsDefault)
OUTPUT inserted.Id
VALUES(@Kind,@Alias,@Identifier,@Opening,@DailyDep,@DailyWdl,@MonthlyDep,@MonthlyWdl,@Notes,1,@IsDefault);");
        insert.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
        insert.Parameters.Add("@Alias", SqlDbType.NVarChar, 200).Value = alias;
        insert.Parameters.Add("@Identifier", SqlDbType.NVarChar, 200).Value = identifier;
        AddMoney(insert, "@Opening", openingBalance);
        AddMoney(insert, "@DailyDep", dailyDepositLimit);
        AddMoney(insert, "@DailyWdl", dailyWithdrawalLimit);
        AddMoney(insert, "@MonthlyDep", monthlyDepositLimit);
        AddMoney(insert, "@MonthlyWdl", monthlyWithdrawalLimit);
        insert.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = notes;
        insert.Parameters.Add("@IsDefault", SqlDbType.Bit).Value = makeDefault;
        return (int)insert.ExecuteScalar()!;
    }

    private static void UpdateAccount(
        SqlConnection connection, SqlTransaction transaction, int accountId, FinancialAccountKind kind, string alias, string identifier,
        decimal dailyDepositLimit, decimal dailyWithdrawalLimit, decimal monthlyDepositLimit, decimal monthlyWithdrawalLimit,
        string notes, bool makeDefault)
    {
        if (makeDefault)
        {
            using (var clear = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET IsDefault = 0, UpdatedAtUtc = SYSUTCDATETIME()
WHERE IsActive = 1 AND Id <> @Id;"))
            {
                clear.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                clear.ExecuteNonQuery();
            }
        }

        using var update = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET AccountKind = @Kind, Alias = @Alias, Identifier = @Identifier,
 DailyDepositLimit = @DailyDep, DailyWithdrawalLimit = @DailyWdl,
 MonthlyDepositLimit = @MonthlyDep, MonthlyWithdrawalLimit = @MonthlyWdl, Notes = @Notes,
 IsDefault = @IsDefault, UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id;");
        update.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
        update.Parameters.Add("@Alias", SqlDbType.NVarChar, 200).Value = alias;
        update.Parameters.Add("@Identifier", SqlDbType.NVarChar, 200).Value = identifier;
        AddMoney(update, "@DailyDep", dailyDepositLimit);
        AddMoney(update, "@DailyWdl", dailyWithdrawalLimit);
        AddMoney(update, "@MonthlyDep", monthlyDepositLimit);
        AddMoney(update, "@MonthlyWdl", monthlyWithdrawalLimit);
        update.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = notes;
        update.Parameters.Add("@IsDefault", SqlDbType.Bit).Value = makeDefault;
        update.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
        update.ExecuteNonQuery();
    }

    private static int EnsureAccountById(
        SqlConnection connection, SqlTransaction transaction, int accountId, FinancialAccountKind kind)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT Id FROM dbo.FinancialAccounts WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND IsActive = 1 AND AccountKind = @Kind;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
        command.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
        var value = command.ExecuteScalar();
        if (value is null or DBNull)
            throw new FinancialDataException("المحفظة المحددة غير متاحة. اختر محفظة أخرى ثم أعد المحاولة.");
        return (int)value;
    }

    private static IReadOnlyDictionary<FinancialAccountKind, int> EnsureAccounts(
        SqlConnection connection, SqlTransaction transaction)
    {
        var accounts = new Dictionary<FinancialAccountKind, int>();

        using (var command = CreateCommand(connection, transaction, @"
SELECT Id, AccountKind FROM dbo.FinancialAccounts WITH (UPDLOCK, HOLDLOCK)
WHERE IsActive = 1
ORDER BY IsDefault DESC, Id;"))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var kind = (FinancialAccountKind)reader.GetByte(1);
                accounts.TryAdd(kind, reader.GetInt32(0));
            }
        }

        foreach (var kind in new[] { FinancialAccountKind.Cash, FinancialAccountKind.VodafoneCash, FinancialAccountKind.InstaPay })
        {
            if (accounts.ContainsKey(kind)) continue;

            using var insert = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,CurrentBalance,IsActive,IsDefault,Notes)
OUTPUT inserted.Id
VALUES(@Kind,@Alias,@Identifier,0,1,1,N'أنشئت تلقائيًا للتحويلات');");
            insert.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
            insert.Parameters.Add("@Alias", SqlDbType.NVarChar, 200).Value = kind switch
            {
                FinancialAccountKind.VodafoneCash => "فودافون كاش",
                FinancialAccountKind.InstaPay => "انستاباي",
                _ => "الخزنة الرئيسية"
            };
            insert.Parameters.Add("@Identifier", SqlDbType.NVarChar, 200).Value = kind switch
            {
                FinancialAccountKind.VodafoneCash => "غير محدد",
                FinancialAccountKind.InstaPay => "غير محدد",
                _ => ""
            };
            accounts[kind] = (int)insert.ExecuteScalar()!;
        }

        return accounts;
    }

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19;
        parameter.Scale = 2;
        parameter.Value = value;
    }

    private static FinancialDataException Describe(Exception exception, string fallback)
    {
        if (exception is FinancialDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                2601 or 2627 => new FinancialDataException("البيانات مكررة: الحساب أو الحركة مسجلة بالفعل. أعد المحاولة.", sql),
                547 => new FinancialDataException("لا يمكن إتمام العملية لارتباط السجل بحساب أو حركة أخرى.", sql),
                1205 => new FinancialDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new FinancialDataException(
                    "حساب الاتصال لا يملك صلاحية العمل على الحسابات المالية. شغّل sql/09_EnablePosSales.sql على الخادم ثم أعد المحاولة.", sql),
                >= 50000 => new FinancialDataException(sql.Message, sql),
                _ => new FinancialDataException(fallback, sql)
            };
        }

        return new FinancialDataException(fallback, exception);
    }

    private static string Trim(string? value, int max, string fallback)
    {
        var trimmed = (value ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return fallback;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.GetConnectionString());
        connection.Open();
        return connection;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };
}
