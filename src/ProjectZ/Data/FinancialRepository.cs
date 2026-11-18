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

/// <summary>Proposed terms only. Original accounts and effects are never supplied by a records screen.</summary>
public sealed record CustomerTransferTerms(int WalletId, bool IsDeposit, string CustomerNumber, decimal Amount, decimal Commission, string Note);

/// <summary>Result of a customer wallet transfer (Vodafone Cash / InstaPay).</summary>
public sealed record SavedCustomerTransfer(
    int TransferId,
    int WalletAccountId,
    int CashAccountId,
    decimal WalletBalanceAfter,
    decimal CashBalanceAfter,
    byte[] RowVersion);

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

        // Merge in place: the UI holds references to these rows (the cash card is bound through
        // MyAccountsViewModel.CashAccount), so replacing them froze every balance on screen.
        FinancialAccountSnapshot.Apply(target, loaded);
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
       ISNULL(Counterparty,N''), ISNULL(Note,N''), CountsTowardLimits
FROM dbo.FinancialTransactions
ORDER BY OccurredAtUtc DESC, Id DESC;"
                : @"
SELECT Id, FinancialAccountId, OccurredAtUtc, IsIncoming, Category, Amount, BalanceAfter,
       ISNULL(Counterparty,N''), ISNULL(Note,N''), CountsTowardLimits
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
                    CountsTowardLimits = reader.GetBoolean(9)
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
    /// Makes one active wallet the default of its own kind and persists it. The filtered unique index
    /// UX_FinancialAccounts_OneDefaultPerKind allows a single default per AccountKind, so every other
    /// row of that kind is cleared inside the same transaction — never the other kinds' defaults.
    /// </summary>
    public void SetDefaultAccount(int accountId, FinancialAccountKind kind)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            // Rejects a wallet that was stopped or deleted in another session instead of silently
            // writing a default that the screen cannot show.
            EnsureAccountById(connection, transaction, accountId, kind);

            using (var clear = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET IsDefault = 0, UpdatedAtUtc = SYSUTCDATETIME()
WHERE AccountKind = @Kind AND IsDefault = 1 AND Id <> @Id;"))
            {
                clear.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
                clear.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                clear.ExecuteNonQuery();
            }

            using (var mark = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET IsDefault = 1, UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id AND AccountKind = @Kind;"))
            {
                mark.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                mark.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
                if (mark.ExecuteNonQuery() == 0)
                    throw new FinancialDataException("تعذّر تعيين المحفظة الافتراضية: لم يُعثر على الحساب. اضغط «تحديث» ثم حاول مرة أخرى.");
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تعيين المحفظة الافتراضية.");
        }
    }

    /// <summary>
    /// Activates or stops one wallet. Stopping is a soft delete: the row and its ledger stay, so the
    /// default flag must be cleared — otherwise the filtered unique index keeps a hidden default and the
    /// screen shows no default wallet at all.
    /// </summary>
    public void SetAccountActive(int accountId, bool isActive)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            FinancialAccountKind kind;
            using (var check = CreateCommand(connection, transaction, @"
SELECT AccountKind FROM dbo.FinancialAccounts WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id;"))
            {
                check.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                if (check.ExecuteScalar() is not { } value || value is DBNull)
                    throw new FinancialDataException("المحفظة غير موجودة في قاعدة البيانات. اضغط «تحديث» ثم حاول مرة أخرى.");
                kind = (FinancialAccountKind)Convert.ToByte(value);
            }

            // The cash row is the system's single cash account; the screen hides its toggle, and the
            // database must refuse it too.
            if (!isActive && kind == FinancialAccountKind.Cash)
                throw new FinancialDataException("لا يمكن إيقاف الخزنة النقدية لأنها حساب أساسي مرتبط بالنظام.");

            using (var update = CreateCommand(connection, transaction, isActive ? @"
UPDATE dbo.FinancialAccounts SET IsActive = 1, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id;" : @"
UPDATE dbo.FinancialAccounts SET IsActive = 0, IsDefault = 0, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id;"))
            {
                update.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
                if (update.ExecuteNonQuery() == 0)
                    throw new FinancialDataException("المحفظة غير موجودة في قاعدة البيانات. اضغط «تحديث» ثم حاول مرة أخرى.");
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Describe(ex, isActive ? "تعذّر تفعيل المحفظة." : "تعذّر إيقاف المحفظة.");
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

    private enum TransferAction : byte { Issue = 1, Correct = 2, Reverse = 3 }

    public SavedCustomerTransfer ExecuteCustomerTransfer(FinancialAccountKind provider, CustomerTransferTerms terms) =>
        PostCustomerTransfer(TransferAction.Issue, provider, null, null, terms, terms.Note);

    /// <summary>Reverse the saved effect and issue a linked replacement; returns the replacement, not the old ID.</summary>
    public SavedCustomerTransfer UpdateCustomerTransfer(int transferId, byte[] expectedVersion, FinancialAccountKind provider, CustomerTransferTerms replacement) =>
        PostCustomerTransfer(TransferAction.Correct, provider, transferId, expectedVersion, replacement, replacement.Note);

    public void ReverseCustomerTransfer(int transferId, byte[] expectedVersion, FinancialAccountKind provider, string note) =>
        PostCustomerTransfer(TransferAction.Reverse, provider, transferId, expectedVersion, null, note);

    private SavedCustomerTransfer PostCustomerTransfer(TransferAction action, FinancialAccountKind provider, int? id,
        byte[]? expectedVersion, CustomerTransferTerms? terms, string note)
    {
        if (action != TransferAction.Issue && expectedVersion is not { Length: 8 })
            throw new FinancialDataException("نسخة التحويل غير متاحة. حدّث السجلات وافتح العملية من جديد.");
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.PostCustomerTransfer";
            command.Parameters.Add("@Action", SqlDbType.TinyInt).Value = (byte)action;
            command.Parameters.Add("@Provider", SqlDbType.TinyInt).Value = (byte)provider;
            command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            if (id.HasValue)
            {
                command.Parameters.Add("@TransferId", SqlDbType.Int).Value = id.Value;
                command.Parameters.Add("@ExpectedVersion", SqlDbType.VarBinary, 8).Value = expectedVersion!;
            }
            if (terms is not null)
            {
                command.Parameters.Add("@NewWallet", SqlDbType.Int).Value = terms.WalletId;
                command.Parameters.Add("@NewKind", SqlDbType.TinyInt).Value = terms.IsDeposit ? (byte)1 : (byte)2;
                command.Parameters.Add("@NewCustomer", SqlDbType.NVarChar, 30).Value = Trim(terms.CustomerNumber, 30, "");
                AddMoney(command, "@NewAmount", Math.Round(terms.Amount, 2, MidpointRounding.AwayFromZero));
                AddMoney(command, "@NewCommission", Math.Round(terms.Commission, 2, MidpointRounding.AwayFromZero));
            }
            command.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = Trim(note, 1000, "");
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new FinancialDataException("لم يصل إيصال الحفظ. حدّث السجلات وتحقق قبل إعادة المحاولة.");
            return new SavedCustomerTransfer(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetDecimal(3), reader.GetDecimal(4), (byte[])reader[5]);
        }
        catch (SqlException ex) when (ex.Number is 2812 or 229 or 230)
        {
            throw new FinancialDataException("يلزم تهيئة إجراءات التحويل وصلاحياتها على الخادم: sql/23_EnableCustomerTransferCorrections.sql.", ex);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حفظ عملية المحفظة.");
        }
    }

    /// <summary>Loads every movement of the فودافون كاش wallets, newest first.</summary>
    public void LoadVodafoneWalletMovementsInto(ObservableCollection<VodafoneTransferOperation> target) =>
        LoadWalletMovementsInto(FinancialAccountKind.VodafoneCash, target, new ObservableCollection<InstapayTransferOperation>());

    /// <summary>Loads every movement of the إنستاباي wallets, newest first.</summary>
    public void LoadInstapayWalletMovementsInto(ObservableCollection<InstapayTransferOperation> target) =>
        LoadWalletMovementsInto(FinancialAccountKind.InstaPay, new ObservableCollection<VodafoneTransferOperation>(), target);

    /// <summary>
    /// Loads every movement of the wallets of one kind — customer transfers, sales collections, supplier
    /// payments, returns, maintenance payments, expenses, wallet-to-wallet transfers and manual
    /// adjustments — newest first. The «سجلات» screens must show the whole wallet history, not only
    /// customer transfers, so the rows come from dbo.FinancialTransactions and are enriched from
    /// dbo.CustomerTransfers when the movement is one of the editable customer transfers.
    /// </summary>
    public void LoadWalletMovementsInto(
        FinancialAccountKind kind,
        ObservableCollection<VodafoneTransferOperation> vodafoneTarget,
        ObservableCollection<InstapayTransferOperation> instapayTarget)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT t.Id, t.FinancialAccountId, ISNULL(a.Alias,N''), ISNULL(a.Identifier,N''), a.AccountKind,
       t.IsIncoming, ISNULL(t.Category,N''), COALESCE(ct.Amount,t.Amount), t.BalanceAfter, COALESCE(ct.CustomerNumber,t.Counterparty),
       COALESCE(ct.Note,t.Note), t.OccurredAtUtc, ISNULL(au.DisplayName,N''), ISNULL(t.SourceEntityType,N''),
       ct.Id, ISNULL(ct.OperationKind,0), ISNULL(ct.Commission,0), ISNULL(ct.CashDelta,0),
       ISNULL(ct.OperationStatus,0), ISNULL(ru.DisplayName,N''), ct.ReversedAtUtc, ct.RowVersion, ct.CorrectsTransferId,
       CASE WHEN t.SourceEntityType=N'CustomerTransfer' THEN t.SourceEntityId END
FROM dbo.FinancialTransactions t
JOIN dbo.FinancialAccounts a ON a.Id = t.FinancialAccountId
LEFT JOIN dbo.Users au ON au.Id = t.ActorUserId
-- One application per header. For a legacy in-place edit the latest application on the
-- saved wallet, not the original ledger leg, represents the header. All other legs stay raw.
LEFT JOIN dbo.CustomerTransfers ct
       ON t.SourceEntityType=N'CustomerTransfer' AND ct.Id=t.SourceEntityId AND ct.FinancialAccountId=t.FinancialAccountId
      AND t.Id=(SELECT MAX(ap.Id) FROM dbo.FinancialTransactions ap
                WHERE ap.SourceEntityType=N'CustomerTransfer' AND ap.SourceEntityId=ct.Id AND ap.FinancialAccountId=ct.FinancialAccountId
                  AND ap.Category IN (N'تحويل للعميل',N'سحب للعميل',N'تعديل سجل'))
      AND t.Amount=ct.Amount AND t.IsIncoming=CASE WHEN ct.OperationKind=1 THEN 0 ELSE 1 END
LEFT JOIN dbo.Users ru ON ru.Id = ct.ReversedByUserId
WHERE a.AccountKind = @Kind
ORDER BY t.OccurredAtUtc DESC, t.Id DESC;";
            command.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var movementId = reader.GetInt32(0);
                var accountId = reader.GetInt32(1);
                var alias = reader.GetString(2);
                var identifier = reader.GetString(3);
                var accountKind = reader.GetByte(4);
                var isIncoming = reader.GetBoolean(5);
                var category = reader.GetString(6);
                var amount = reader.GetDecimal(7);
                var balanceAfter = reader.GetDecimal(8);
                var counterparty = reader.GetString(9);
                var note = reader.GetString(10);
                var occurredAt = reader.GetDateTime(11).ToLocalTime();
                var actorName = reader.GetString(12);
                var sourceType = reader.GetString(13);
                // The LEFT JOIN means every ct column is NULL for a sale, purchase, expense, maintenance
                // or adjustment row, so each one must be read through IsDBNull instead of GetXxx.
                var hasTransfer = !reader.IsDBNull(14);
                var transferId = reader.IsDBNull(23) ? 0 : reader.GetInt32(23);
                var transferKind = hasTransfer ? reader.GetByte(15) : (byte)0;
                var commission = hasTransfer ? reader.GetDecimal(16) : 0m;
                var cashDelta = hasTransfer ? reader.GetDecimal(17) : 0m;
                var transferStatus = hasTransfer ? reader.GetByte(18) : (byte)0;
                var reversedBy = hasTransfer ? reader.GetString(19) : string.Empty;
                var reversedAt = hasTransfer && !reader.IsDBNull(20) ? reader.GetDateTime(20).ToLocalTime() : (DateTime?)null;

                var version = hasTransfer ? (byte[])reader[21] : Array.Empty<byte>();
                int? correctsId = reader.IsDBNull(22) ? null : reader.GetInt32(22);
                var isCustomerTransfer = hasTransfer;
                var sourceTitle = isCustomerTransfer ? "تحويل عميل" : DescribeMovementSource(sourceType, category);

                if (accountKind == (byte)FinancialAccountKind.VodafoneCash)
                {
                    var depositSide = isCustomerTransfer ? transferKind == 1 : isIncoming;
                    vodafoneTarget.Add(new VodafoneTransferOperation
                    {
                        Id = transferId,
                        RowVersion = version,
                        CorrectsTransferId = correctsId,
                        MovementId = movementId,
                        WalletId = accountId,
                        WalletAlias = alias,
                        WalletNumber = identifier,
                        Kind = depositSide ? VodafoneOperationKind.DepositToCustomer : VodafoneOperationKind.WithdrawForCustomer,
                        CustomerNumber = counterparty,
                        Amount = amount,
                        Commission = isCustomerTransfer ? commission : 0m,
                        CashDelta = isCustomerTransfer ? cashDelta : 0m,
                        WalletBalanceAfter = balanceAfter,
                        Note = note,
                        Timestamp = occurredAt,
                        ActorName = actorName,
                        IsReversed = isCustomerTransfer && transferStatus == 1,
                        ReversedBy = reversedBy,
                        ReversedAt = reversedAt,
                        IsCustomerTransfer = isCustomerTransfer,
                        IsIncoming = isIncoming,
                        SourceTitle = sourceTitle,
                        CategoryTitle = category
                    });
                }
                else if (accountKind == (byte)FinancialAccountKind.InstaPay)
                {
                    var depositSide = isCustomerTransfer ? transferKind == 1 : isIncoming;
                    instapayTarget.Add(new InstapayTransferOperation
                    {
                        Id = transferId,
                        RowVersion = version,
                        CorrectsTransferId = correctsId,
                        MovementId = movementId,
                        WalletId = accountId,
                        WalletAlias = alias,
                        WalletNumber = identifier,
                        Kind = depositSide ? InstapayOperationKind.DepositToCustomer : InstapayOperationKind.WithdrawForCustomer,
                        CustomerNumber = counterparty,
                        Amount = amount,
                        Commission = isCustomerTransfer ? commission : 0m,
                        CashDelta = isCustomerTransfer ? cashDelta : 0m,
                        WalletBalanceAfter = balanceAfter,
                        Note = note,
                        Timestamp = occurredAt,
                        ActorName = actorName,
                        IsReversed = isCustomerTransfer && transferStatus == 1,
                        ReversedBy = reversedBy,
                        ReversedAt = reversedAt,
                        IsCustomerTransfer = isCustomerTransfer,
                        IsIncoming = isIncoming,
                        SourceTitle = sourceTitle,
                        CategoryTitle = category
                    });
                }
            }
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تحميل حركات المحافظ من قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Turns a ledger row's source/category pair into the Arabic label the records screens show. Rows
    /// written by another screen carry its SourceEntityType; manual rows carry no source, so their own
    /// category («إيداع»، «سحب»، «تحويل صادر»…) is the honest label.
    /// </summary>
    private static string DescribeMovementSource(string sourceType, string category) => sourceType switch
    {
        "SalesInvoice" => category.Contains("فرق استبدال", StringComparison.Ordinal) ? "استبدال / فرق" : "بيع / تحصيل",
        "ReturnExchange" => "مرتجع / استبدال",
        "PurchaseInvoice" => "شراء / سداد",
        "MaintenanceOrder" => category.Contains("رد ", StringComparison.Ordinal) ? "رد صيانة" : "صيانة",
        "Expense" => "مصروف",
        "CustomerTransfer" => "عكس / تسوية تحويل",
        _ => string.IsNullOrWhiteSpace(category) ? "حركة يدوية" : category
    };

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
       ISNULL(ru.DisplayName,N''), t.ReversedAtUtc, ISNULL(au.DisplayName,N''), t.RowVersion, t.CorrectsTransferId, ISNULL(a.Identifier,N'')
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
                        RowVersion = (byte[])reader[16],
                        CorrectsTransferId = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                        WalletNumber = reader.GetString(18),
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
                        RowVersion = (byte[])reader[16],
                        CorrectsTransferId = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                        WalletNumber = reader.GetString(18),
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
            // Cleared per kind only: editing a Vodafone line must not demote the cash or InstaPay
            // default (UX_FinancialAccounts_OneDefaultPerKind is scoped to AccountKind).
            using (var clear = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET IsDefault = 0, UpdatedAtUtc = SYSUTCDATETIME()
WHERE AccountKind = @Kind AND IsDefault = 1 AND Id <> @Id;"))
            {
                clear.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
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
                547 => new FinancialDataException(
                    sql.Message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase)
                        ? "قيمة أحد الحقول غير مسموح بها في قاعدة البيانات (قيد تحقق). راجع المبلغ والعمولة ثم أعد المحاولة."
                        : "لا يمكن إتمام العملية لارتباط السجل بحساب أو حركة أخرى.", sql),
                207 when sql.Message.Contains("CorrectsTransferId", StringComparison.OrdinalIgnoreCase) => new FinancialDataException(
                    "يلزم ترقية سجلات التحويل على الخادم: sql/23_EnableCustomerTransferCorrections.sql.", sql),
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
