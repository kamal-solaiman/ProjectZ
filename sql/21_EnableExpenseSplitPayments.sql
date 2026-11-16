-- Upgrade an existing database from one-account expenses to split payments.
-- An expense may now be paid with several financial accounts at once, so the single
-- dbo.Expenses.FinancialAccountId column becomes dbo.ExpensePayments (one row per account).
-- The script copies every existing expense into a payment row before dropping the column,
-- so no financial history is lost. Idempotent: it does nothing after the first successful run.

USE [ProjectZ];
GO
SET NOCOUNT ON;

IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50080, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
IF OBJECT_ID(N'dbo.Expenses', N'U') IS NULL
    THROW 50081, N'dbo.Expenses is missing. Run sql/20_EnableExpenses.sql first.', 1;
IF OBJECT_ID(N'dbo.FinancialAccounts', N'U') IS NULL
    THROW 50082, N'dbo.FinancialAccounts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
GO

IF OBJECT_ID(N'dbo.ExpensePayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExpensePayments
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpensePayments PRIMARY KEY,
        ExpenseId INT NOT NULL CONSTRAINT FK_ExpensePayments_Expenses FOREIGN KEY REFERENCES dbo.Expenses(Id),
        FinancialAccountId INT NOT NULL CONSTRAINT FK_ExpensePayments_Accounts FOREIGN KEY REFERENCES dbo.FinancialAccounts(Id),
        Amount DECIMAL(19,2) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ExpensePayments_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_ExpensePayments_Amount CHECK (Amount > 0),
        CONSTRAINT UQ_ExpensePayments_Expense_Account UNIQUE (ExpenseId, FinancialAccountId)
    );
    PRINT N'Created dbo.ExpensePayments.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ExpensePayments') AND name = N'IX_ExpensePayments_Account')
    CREATE INDEX IX_ExpensePayments_Account
        ON dbo.ExpensePayments(FinancialAccountId, ExpenseId)
        INCLUDE (Amount);
GO

/* Copy the old single account of every expense into a payment row, then drop the column. */
IF COL_LENGTH(N'dbo.Expenses', N'FinancialAccountId') IS NOT NULL
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT dbo.ExpensePayments(ExpenseId, FinancialAccountId, Amount)
        SELECT e.Id, e.FinancialAccountId, e.Amount
        FROM dbo.Expenses AS e
        WHERE e.FinancialAccountId IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM dbo.ExpensePayments AS p WHERE p.ExpenseId = e.Id);

        -- The covering index references the column, so it is rebuilt after the drop.
        IF EXISTS (SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.Expenses') AND name = N'IX_Expenses_Date')
            DROP INDEX IX_Expenses_Date ON dbo.Expenses;

        DECLARE @fk SYSNAME;
        SELECT TOP (1) @fk = fk.name
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Expenses')
          AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'FinancialAccountId';
        -- EXEC() accepts variables and literals, not function calls, so the dynamic statement is
        -- built in a variable first; a function call inside EXEC(...) is a syntax error (Msg 102).
        IF @fk IS NOT NULL
        BEGIN
            DECLARE @drop_fk NVARCHAR(400) = N'ALTER TABLE dbo.Expenses DROP CONSTRAINT ' + QUOTENAME(@fk);
            EXEC sp_executesql @drop_fk;
        END

        ALTER TABLE dbo.Expenses DROP COLUMN FinancialAccountId;

        COMMIT TRANSACTION;
        PRINT N'Moved existing expense accounts into dbo.ExpensePayments and dropped dbo.Expenses.FinancialAccountId.';
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.Expenses') AND name = N'IX_Expenses_Date')
    CREATE INDEX IX_Expenses_Date
        ON dbo.Expenses(ExpenseDate DESC, Id DESC)
        INCLUDE (ExpenseNumber, ExpenseCategoryId, Amount);
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.Expenses') AND name = N'IX_Expenses_Category_Date')
    CREATE INDEX IX_Expenses_Category_Date
        ON dbo.Expenses(ExpenseCategoryId, ExpenseDate DESC)
        INCLUDE (Amount);
GO

GRANT SELECT, INSERT ON OBJECT::dbo.Expenses TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.ExpensePayments TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.ExpenseCategories TO [ProjectZClient];
GRANT SELECT, UPDATE ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.DocumentSequences TO [ProjectZClient];
GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

IF COL_LENGTH(N'dbo.Expenses', N'FinancialAccountId') IS NOT NULL
    THROW 50083, N'dbo.Expenses.FinancialAccountId still exists. Check the migration output and rerun this script.', 1;
ELSE IF EXISTS (SELECT 1 FROM dbo.Expenses AS e
                WHERE NOT EXISTS (SELECT 1 FROM dbo.ExpensePayments AS p WHERE p.ExpenseId = e.Id))
    THROW 50084, N'Some expenses have no payment row. Do not use the screen until every expense has its payment.', 1;
ELSE
    PRINT N'SUCCESS: expenses now support split payments through dbo.ExpensePayments; existing accounts were migrated.';
GO
