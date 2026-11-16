-- Create dbo.Expenses + dbo.ExpensePayments and grant the LAN client the rights the
-- «اضافة مصروف» screen needs. Run once on an existing database after 06 / 04 / 19.
-- Idempotent: missing objects only are created, GRANT repeats safely, no row or column is modified.

USE [ProjectZ];
GO
SET NOCOUNT ON;

IF DATABASE_PRINCIPAL_ID(N'ProjectZClient') IS NULL
    THROW 50070, N'ProjectZClient database user is missing. Run sql/04_SetupLanSqlAuth.sql first.', 1;
IF OBJECT_ID(N'dbo.ExpenseCategories', N'U') IS NULL
    THROW 50071, N'dbo.ExpenseCategories is missing. Run sql/19_EnableExpenseCategories.sql first.', 1;
IF OBJECT_ID(N'dbo.FinancialAccounts', N'U') IS NULL
    THROW 50072, N'dbo.FinancialAccounts is missing. Run sql/06_CreateFullSchema.sql first.', 1;
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
    THROW 50073, N'dbo.AuditLog is missing. Run sql/06_CreateFullSchema.sql first.', 1;
GO

IF OBJECT_ID(N'dbo.Expenses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Expenses
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Expenses PRIMARY KEY,
        ExpenseNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_Expenses_Number UNIQUE,
        ExpenseCategoryId INT NOT NULL CONSTRAINT FK_Expenses_Categories FOREIGN KEY REFERENCES dbo.ExpenseCategories(Id),
        ExpenseDate DATE NOT NULL,
        Amount DECIMAL(19,2) NOT NULL,
        Description NVARCHAR(1000) NOT NULL CONSTRAINT DF_Expenses_Description DEFAULT N'',
        ActorUserId INT NOT NULL CONSTRAINT FK_Expenses_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Expenses_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT CK_Expenses_Amount CHECK (Amount > 0)
    );
    PRINT N'Created dbo.Expenses.';
END
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
               WHERE object_id = OBJECT_ID(N'dbo.Expenses') AND name = N'IX_Expenses_Date')
    CREATE INDEX IX_Expenses_Date
        ON dbo.Expenses(ExpenseDate DESC, Id DESC)
        INCLUDE (ExpenseNumber, ExpenseCategoryId, Amount);
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.Expenses') AND name = N'IX_Expenses_Category_Date')
    CREATE INDEX IX_Expenses_Category_Date
        ON dbo.Expenses(ExpenseCategoryId, ExpenseDate DESC)
        INCLUDE (Amount);
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ExpensePayments') AND name = N'IX_ExpensePayments_Account')
    CREATE INDEX IX_ExpensePayments_Account
        ON dbo.ExpensePayments(FinancialAccountId, ExpenseId)
        INCLUDE (Amount);
GO

/* The screen reads the sections and the active accounts, writes one expense with its split
   payments, deducts every used account, appends one ledger movement per account and consumes
   the yearly document sequence. */
GRANT SELECT, INSERT ON OBJECT::dbo.Expenses TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.ExpensePayments TO [ProjectZClient];
GRANT SELECT ON OBJECT::dbo.ExpenseCategories TO [ProjectZClient];
GRANT SELECT, UPDATE ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient];
GRANT SELECT, INSERT ON OBJECT::dbo.FinancialTransactions TO [ProjectZClient];
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.DocumentSequences TO [ProjectZClient];
GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient];
GO

IF OBJECT_ID(N'dbo.Expenses', N'U') IS NULL OR OBJECT_ID(N'dbo.ExpensePayments', N'U') IS NULL
    THROW 50074, N'dbo.Expenses or dbo.ExpensePayments was not created. Check the database and rerun this script.', 1;
ELSE
    PRINT N'SUCCESS: ProjectZClient can now record split-paid expenses and deduct every used account.';
GO
