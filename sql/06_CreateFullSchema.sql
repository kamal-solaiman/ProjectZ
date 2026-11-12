/*
    ProjectZ full business schema.
    Run AFTER 01_CreateDatabase.sql. This script does not alter the existing
    authentication connection, dbo.Users, application code, or LAN login.
    SQL Server 2016 SP1+ / compatibility level 130. Safe to re-run.
*/
USE [ProjectZ];
GO
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 50010, N'dbo.Users is required. Run 01_CreateDatabase.sql first.', 1;
GO
BEGIN TRANSACTION;

/* Security and application configuration. dbo.Users remains unchanged. */
IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
CREATE TABLE dbo.Roles
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL CONSTRAINT UQ_Roles_Name UNIQUE,
    Description NVARCHAR(500) NOT NULL CONSTRAINT DF_Roles_Description DEFAULT N'',
    IsSystem BIT NOT NULL CONSTRAINT DF_Roles_IsSystem DEFAULT 0,
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Roles_CreatedAtUtc DEFAULT SYSUTCDATETIME()
);

IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
CREATE TABLE dbo.Permissions
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY,
    Code NVARCHAR(100) NOT NULL CONSTRAINT UQ_Permissions_Code UNIQUE,
    Name NVARCHAR(150) NOT NULL,
    Module NVARCHAR(100) NOT NULL
);

IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL
CREATE TABLE dbo.UserRoles
(
    UserId INT NOT NULL CONSTRAINT FK_UserRoles_Users FOREIGN KEY REFERENCES dbo.Users(Id),
    RoleId INT NOT NULL CONSTRAINT FK_UserRoles_Roles FOREIGN KEY REFERENCES dbo.Roles(Id),
    AssignedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_UserRoles_AssignedAtUtc DEFAULT SYSUTCDATETIME(),
    AssignedByUserId INT NULL CONSTRAINT FK_UserRoles_AssignedBy FOREIGN KEY REFERENCES dbo.Users(Id),
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId)
);

IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
CREATE TABLE dbo.RolePermissions
(
    RoleId INT NOT NULL CONSTRAINT FK_RolePermissions_Roles FOREIGN KEY REFERENCES dbo.Roles(Id),
    PermissionId INT NOT NULL CONSTRAINT FK_RolePermissions_Permissions FOREIGN KEY REFERENCES dbo.Permissions(Id),
    CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, PermissionId)
);

IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NULL
CREATE TABLE dbo.SystemSettings
(
    SettingKey NVARCHAR(150) NOT NULL CONSTRAINT PK_SystemSettings PRIMARY KEY,
    SettingValue NVARCHAR(MAX) NULL,
    ValueType NVARCHAR(30) NOT NULL CONSTRAINT DF_SystemSettings_ValueType DEFAULT N'string',
    Description NVARCHAR(500) NOT NULL CONSTRAINT DF_SystemSettings_Description DEFAULT N'',
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedByUserId INT NULL CONSTRAINT FK_SystemSettings_Users FOREIGN KEY REFERENCES dbo.Users(Id),
    CONSTRAINT CK_SystemSettings_ValueType CHECK (ValueType IN (N'string', N'integer', N'decimal', N'boolean', N'json'))
);

IF OBJECT_ID(N'dbo.DocumentSequences', N'U') IS NULL
CREATE TABLE dbo.DocumentSequences
(
    DocumentType NVARCHAR(30) NOT NULL,
    SequenceYear SMALLINT NOT NULL,
    LastValue BIGINT NOT NULL CONSTRAINT DF_DocumentSequences_LastValue DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT PK_DocumentSequences PRIMARY KEY (DocumentType, SequenceYear),
    CONSTRAINT CK_DocumentSequences_LastValue CHECK (LastValue >= 0)
);

/* Parties and catalog. */
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
CREATE TABLE dbo.Customers
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    CustomerCode AS (N'CUS-' + RIGHT(N'000000' + CONVERT(NVARCHAR(20), Id), 6)) PERSISTED,
    Name NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(30) NULL,
    SecondaryPhone NVARCHAR(30) NULL,
    Address NVARCHAR(500) NOT NULL CONSTRAINT DF_Customers_Address DEFAULT N'',
    Notes NVARCHAR(1000) NOT NULL CONSTRAINT DF_Customers_Notes DEFAULT N'',
    DefaultPriceTier TINYINT NOT NULL CONSTRAINT DF_Customers_DefaultPriceTier DEFAULT 0,
    IsWalkIn BIT NOT NULL CONSTRAINT DF_Customers_IsWalkIn DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Customers_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Customers_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_Customers_DefaultPriceTier CHECK (DefaultPriceTier BETWEEN 0 AND 2),
    CONSTRAINT CK_Customers_WalkIn CHECK (IsWalkIn = 0 OR Phone IS NULL)
);

IF OBJECT_ID(N'dbo.Suppliers', N'U') IS NULL
CREATE TABLE dbo.Suppliers
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
    SupplierCode AS (N'SUP-' + RIGHT(N'000000' + CONVERT(NVARCHAR(20), Id), 6)) PERSISTED,
    Name NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(30) NULL,
    SecondaryPhone NVARCHAR(30) NULL,
    CompanyName NVARCHAR(200) NOT NULL CONSTRAINT DF_Suppliers_CompanyName DEFAULT N'',
    Specialization NVARCHAR(200) NOT NULL CONSTRAINT DF_Suppliers_Specialization DEFAULT N'',
    Address NVARCHAR(500) NOT NULL CONSTRAINT DF_Suppliers_Address DEFAULT N'',
    VodafoneCashNumber NVARCHAR(30) NOT NULL CONSTRAINT DF_Suppliers_VodafoneCashNumber DEFAULT N'',
    InstaPayAddress NVARCHAR(200) NOT NULL CONSTRAINT DF_Suppliers_InstaPayAddress DEFAULT N'',
    Notes NVARCHAR(1000) NOT NULL CONSTRAINT DF_Suppliers_Notes DEFAULT N'',
    IsActive BIT NOT NULL CONSTRAINT DF_Suppliers_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Suppliers_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Suppliers_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL
);

IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
CREATE TABLE dbo.Categories
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    ParentCategoryId INT NULL CONSTRAINT FK_Categories_Parent FOREIGN KEY REFERENCES dbo.Categories(Id),
    Description NVARCHAR(500) NOT NULL CONSTRAINT DF_Categories_Description DEFAULT N'',
    IsActive BIT NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Categories_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Categories_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Categories_Parent_Name UNIQUE (ParentCategoryId, Name),
    CONSTRAINT CK_Categories_NotOwnParent CHECK (ParentCategoryId IS NULL OR ParentCategoryId <> Id)
);

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
CREATE TABLE dbo.Products
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
    CategoryId INT NOT NULL CONSTRAINT FK_Products_Categories FOREIGN KEY REFERENCES dbo.Categories(Id),
    Barcode NVARCHAR(100) NULL,
    Sku NVARCHAR(100) NULL,
    Name NVARCHAR(250) NOT NULL,
    UnitName NVARCHAR(50) NOT NULL CONSTRAINT DF_Products_UnitName DEFAULT N'قطعة',
    CostPrice DECIMAL(19,2) NOT NULL CONSTRAINT DF_Products_CostPrice DEFAULT 0,
    RetailPrice DECIMAL(19,2) NOT NULL CONSTRAINT DF_Products_RetailPrice DEFAULT 0,
    SpecialPrice DECIMAL(19,2) NOT NULL CONSTRAINT DF_Products_SpecialPrice DEFAULT 0,
    WholesalePrice DECIMAL(19,2) NOT NULL CONSTRAINT DF_Products_WholesalePrice DEFAULT 0,
    MinimumStockAlert INT NOT NULL CONSTRAINT DF_Products_MinimumStockAlert DEFAULT 5,
    TrackSerialNumber BIT NOT NULL CONSTRAINT DF_Products_TrackSerialNumber DEFAULT 0,
    IsActiveForSale BIT NOT NULL CONSTRAINT DF_Products_IsActiveForSale DEFAULT 1,
    Notes NVARCHAR(1000) NOT NULL CONSTRAINT DF_Products_Notes DEFAULT N'',
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Products_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_Products_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_Products_Prices CHECK (CostPrice >= 0 AND RetailPrice >= 0 AND SpecialPrice >= 0 AND WholesalePrice >= 0),
    CONSTRAINT CK_Products_MinimumStockAlert CHECK (MinimumStockAlert >= 0)
);

/* Cash, wallets, ledger and customer wallet transfers. No transfer reference number. */
IF OBJECT_ID(N'dbo.FinancialAccounts', N'U') IS NULL
CREATE TABLE dbo.FinancialAccounts
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FinancialAccounts PRIMARY KEY,
    AccountKind TINYINT NOT NULL,
    Alias NVARCHAR(200) NOT NULL,
    Identifier NVARCHAR(200) NOT NULL CONSTRAINT DF_FinancialAccounts_Identifier DEFAULT N'',
    CurrentBalance DECIMAL(19,2) NOT NULL CONSTRAINT DF_FinancialAccounts_CurrentBalance DEFAULT 0,
    DailyDepositLimit DECIMAL(19,2) NOT NULL CONSTRAINT DF_FinancialAccounts_DailyDepositLimit DEFAULT 0,
    DailyWithdrawalLimit DECIMAL(19,2) NOT NULL CONSTRAINT DF_FinancialAccounts_DailyWithdrawalLimit DEFAULT 0,
    MonthlyDepositLimit DECIMAL(19,2) NOT NULL CONSTRAINT DF_FinancialAccounts_MonthlyDepositLimit DEFAULT 0,
    MonthlyWithdrawalLimit DECIMAL(19,2) NOT NULL CONSTRAINT DF_FinancialAccounts_MonthlyWithdrawalLimit DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_FinancialAccounts_IsActive DEFAULT 1,
    IsDefault BIT NOT NULL CONSTRAINT DF_FinancialAccounts_IsDefault DEFAULT 0,
    Notes NVARCHAR(1000) NOT NULL CONSTRAINT DF_FinancialAccounts_Notes DEFAULT N'',
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FinancialAccounts_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FinancialAccounts_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_FinancialAccounts_AccountKind CHECK (AccountKind BETWEEN 0 AND 2),
    CONSTRAINT CK_FinancialAccounts_Limits CHECK (DailyDepositLimit >= 0 AND DailyWithdrawalLimit >= 0 AND MonthlyDepositLimit >= 0 AND MonthlyWithdrawalLimit >= 0),
    CONSTRAINT CK_FinancialAccounts_CashIdentifier CHECK (AccountKind <> 0 OR Identifier = N''),
    CONSTRAINT UQ_FinancialAccounts_Kind_Identifier UNIQUE (AccountKind, Identifier)
);

IF OBJECT_ID(N'dbo.CustomerTransfers', N'U') IS NULL
CREATE TABLE dbo.CustomerTransfers
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerTransfers PRIMARY KEY,
    FinancialAccountId INT NOT NULL CONSTRAINT FK_CustomerTransfers_Accounts FOREIGN KEY REFERENCES dbo.FinancialAccounts(Id),
    OperationKind TINYINT NOT NULL,
    CustomerNumber NVARCHAR(30) NOT NULL,
    Amount DECIMAL(19,2) NOT NULL,
    Commission DECIMAL(19,2) NOT NULL CONSTRAINT DF_CustomerTransfers_Commission DEFAULT 0,
    CashDelta DECIMAL(19,2) NOT NULL,
    WalletBalanceAfter DECIMAL(19,2) NOT NULL,
    OperationStatus TINYINT NOT NULL CONSTRAINT DF_CustomerTransfers_OperationStatus DEFAULT 0,
    Note NVARCHAR(1000) NOT NULL CONSTRAINT DF_CustomerTransfers_Note DEFAULT N'',
    OccurredAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_CustomerTransfers_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
    ActorUserId INT NOT NULL CONSTRAINT FK_CustomerTransfers_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    ReversedAtUtc DATETIME2(7) NULL,
    ReversedByUserId INT NULL CONSTRAINT FK_CustomerTransfers_ReversedBy FOREIGN KEY REFERENCES dbo.Users(Id),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_CustomerTransfers_OperationKind CHECK (OperationKind IN (1, 2)),
    CONSTRAINT CK_CustomerTransfers_Amount CHECK (Amount > 0 AND Commission >= 0),
    CONSTRAINT CK_CustomerTransfers_Status CHECK (OperationStatus IN (0, 1)),
    CONSTRAINT CK_CustomerTransfers_Reversal CHECK ((OperationStatus = 0 AND ReversedAtUtc IS NULL AND ReversedByUserId IS NULL) OR (OperationStatus = 1 AND ReversedAtUtc IS NOT NULL AND ReversedByUserId IS NOT NULL))
);

IF OBJECT_ID(N'dbo.FinancialTransactions', N'U') IS NULL
CREATE TABLE dbo.FinancialTransactions
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FinancialTransactions PRIMARY KEY,
    FinancialAccountId INT NOT NULL CONSTRAINT FK_FinancialTransactions_Accounts FOREIGN KEY REFERENCES dbo.FinancialAccounts(Id),
    TransferGroupId UNIQUEIDENTIFIER NULL,
    OccurredAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FinancialTransactions_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
    IsIncoming BIT NOT NULL,
    Category NVARCHAR(150) NOT NULL,
    Amount DECIMAL(19,2) NOT NULL,
    BalanceAfter DECIMAL(19,2) NOT NULL,
    Counterparty NVARCHAR(250) NOT NULL CONSTRAINT DF_FinancialTransactions_Counterparty DEFAULT N'',
    Note NVARCHAR(1000) NOT NULL CONSTRAINT DF_FinancialTransactions_Note DEFAULT N'',
    ActorUserId INT NOT NULL CONSTRAINT FK_FinancialTransactions_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    CountsTowardLimits BIT NOT NULL CONSTRAINT DF_FinancialTransactions_CountsTowardLimits DEFAULT 1,
    SourceEntityType NVARCHAR(50) NULL,
    SourceEntityId BIGINT NULL,
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_FinancialTransactions_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_FinancialTransactions_Amount CHECK (Amount > 0),
    CONSTRAINT CK_FinancialTransactions_Source CHECK ((SourceEntityType IS NULL AND SourceEntityId IS NULL) OR (SourceEntityType IS NOT NULL AND SourceEntityId IS NOT NULL))
);

/* Serialized and quantity inventory. */
IF OBJECT_ID(N'dbo.InventoryUnits', N'U') IS NULL
CREATE TABLE dbo.InventoryUnits
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryUnits PRIMARY KEY,
    ProductId INT NOT NULL CONSTRAINT FK_InventoryUnits_Products FOREIGN KEY REFERENCES dbo.Products(Id),
    SupplierId INT NULL CONSTRAINT FK_InventoryUnits_Suppliers FOREIGN KEY REFERENCES dbo.Suppliers(Id),
    SerialNumber NVARCHAR(150) NULL,
    PurchaseCost DECIMAL(19,2) NOT NULL CONSTRAINT DF_InventoryUnits_PurchaseCost DEFAULT 0,
    UnitStatus TINYINT NOT NULL CONSTRAINT DF_InventoryUnits_UnitStatus DEFAULT 0,
    RegisteredAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_InventoryUnits_RegisteredAtUtc DEFAULT SYSUTCDATETIME(),
    SoldAtUtc DATETIME2(7) NULL,
    DamageReason NVARCHAR(1000) NOT NULL CONSTRAINT DF_InventoryUnits_DamageReason DEFAULT N'',
    DamagedAtUtc DATETIME2(7) NULL,
    DamageSettlementStatus NVARCHAR(50) NOT NULL CONSTRAINT DF_InventoryUnits_DamageSettlementStatus DEFAULT N'',
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_InventoryUnits_Status CHECK (UnitStatus BETWEEN 0 AND 2),
    CONSTRAINT CK_InventoryUnits_PurchaseCost CHECK (PurchaseCost >= 0),
    CONSTRAINT CK_InventoryUnits_Damage CHECK ((UnitStatus = 2 AND DamagedAtUtc IS NOT NULL) OR (UnitStatus <> 2 AND DamagedAtUtc IS NULL))
);

IF OBJECT_ID(N'dbo.StockMovements', N'U') IS NULL
CREATE TABLE dbo.StockMovements
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
    ProductId INT NOT NULL CONSTRAINT FK_StockMovements_Products FOREIGN KEY REFERENCES dbo.Products(Id),
    InventoryUnitId BIGINT NULL CONSTRAINT FK_StockMovements_Units FOREIGN KEY REFERENCES dbo.InventoryUnits(Id),
    MovementType NVARCHAR(40) NOT NULL,
    QuantityDelta INT NOT NULL,
    UnitCost DECIMAL(19,2) NOT NULL CONSTRAINT DF_StockMovements_UnitCost DEFAULT 0,
    OccurredAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_StockMovements_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
    ActorUserId INT NOT NULL CONSTRAINT FK_StockMovements_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    SourceEntityType NVARCHAR(50) NULL,
    SourceEntityId BIGINT NULL,
    Reason NVARCHAR(1000) NOT NULL CONSTRAINT DF_StockMovements_Reason DEFAULT N'',
    CONSTRAINT CK_StockMovements_QuantityDelta CHECK (QuantityDelta <> 0),
    CONSTRAINT CK_StockMovements_UnitCost CHECK (UnitCost >= 0),
    CONSTRAINT CK_StockMovements_Source CHECK ((SourceEntityType IS NULL AND SourceEntityId IS NULL) OR (SourceEntityType IS NOT NULL AND SourceEntityId IS NOT NULL))
);

/* Purchasing, supplier payments and returns. */
IF OBJECT_ID(N'dbo.PurchaseInvoices', N'U') IS NULL
CREATE TABLE dbo.PurchaseInvoices
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoices PRIMARY KEY,
    InvoiceNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_PurchaseInvoices_Number UNIQUE,
    SupplierInvoiceNumber NVARCHAR(100) NOT NULL CONSTRAINT DF_PurchaseInvoices_SupplierNumber DEFAULT N'',
    SupplierId INT NOT NULL CONSTRAINT FK_PurchaseInvoices_Suppliers FOREIGN KEY REFERENCES dbo.Suppliers(Id),
    SupplierNameSnapshot NVARCHAR(200) NOT NULL,
    SupplierPhoneSnapshot NVARCHAR(30) NOT NULL CONSTRAINT DF_PurchaseInvoices_SupplierPhone DEFAULT N'',
    SupplierCompanySnapshot NVARCHAR(200) NOT NULL CONSTRAINT DF_PurchaseInvoices_SupplierCompany DEFAULT N'',
    SupplierAddressSnapshot NVARCHAR(500) NOT NULL CONSTRAINT DF_PurchaseInvoices_SupplierAddress DEFAULT N'',
    IssuedAtUtc DATETIME2(7) NOT NULL,
    InvoiceStatus TINYINT NOT NULL CONSTRAINT DF_PurchaseInvoices_Status DEFAULT 0,
    DiscountAmount DECIMAL(19,2) NOT NULL CONSTRAINT DF_PurchaseInvoices_Discount DEFAULT 0,
    Notes NVARCHAR(1000) NOT NULL CONSTRAINT DF_PurchaseInvoices_Notes DEFAULT N'',
    BuyerUserId INT NOT NULL CONSTRAINT FK_PurchaseInvoices_Buyer FOREIGN KEY REFERENCES dbo.Users(Id),
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_PurchaseInvoices_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_PurchaseInvoices_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_PurchaseInvoices_Status CHECK (InvoiceStatus BETWEEN 0 AND 5),
    CONSTRAINT CK_PurchaseInvoices_Discount CHECK (DiscountAmount >= 0)
);

IF OBJECT_ID(N'dbo.PurchaseInvoiceLines', N'U') IS NULL
CREATE TABLE dbo.PurchaseInvoiceLines
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoiceLines PRIMARY KEY,
    PurchaseInvoiceId BIGINT NOT NULL CONSTRAINT FK_PurchaseInvoiceLines_Invoices FOREIGN KEY REFERENCES dbo.PurchaseInvoices(Id),
    LineNumber INT NOT NULL,
    ProductId INT NOT NULL CONSTRAINT FK_PurchaseInvoiceLines_Products FOREIGN KEY REFERENCES dbo.Products(Id),
    BarcodeSnapshot NVARCHAR(100) NOT NULL CONSTRAINT DF_PurchaseInvoiceLines_Barcode DEFAULT N'',
    ProductNameSnapshot NVARCHAR(250) NOT NULL,
    CategoryNameSnapshot NVARCHAR(200) NOT NULL CONSTRAINT DF_PurchaseInvoiceLines_Category DEFAULT N'',
    UnitNameSnapshot NVARCHAR(50) NOT NULL,
    TrackSerialNumber BIT NOT NULL,
    Quantity INT NOT NULL,
    ReturnedQuantity INT NOT NULL CONSTRAINT DF_PurchaseInvoiceLines_Returned DEFAULT 0,
    UnitPurchaseCost DECIMAL(19,2) NOT NULL,
    CONSTRAINT UQ_PurchaseInvoiceLines_Line UNIQUE (PurchaseInvoiceId, LineNumber),
    CONSTRAINT CK_PurchaseInvoiceLines_Quantity CHECK (Quantity > 0 AND ReturnedQuantity >= 0 AND ReturnedQuantity <= Quantity),
    CONSTRAINT CK_PurchaseInvoiceLines_Cost CHECK (UnitPurchaseCost >= 0)
);

IF OBJECT_ID(N'dbo.PurchaseInvoiceLineUnits', N'U') IS NULL
CREATE TABLE dbo.PurchaseInvoiceLineUnits
(
    PurchaseInvoiceLineId BIGINT NOT NULL CONSTRAINT FK_PurchaseLineUnits_Lines FOREIGN KEY REFERENCES dbo.PurchaseInvoiceLines(Id),
    InventoryUnitId BIGINT NOT NULL CONSTRAINT FK_PurchaseLineUnits_Units FOREIGN KEY REFERENCES dbo.InventoryUnits(Id),
    CONSTRAINT PK_PurchaseInvoiceLineUnits PRIMARY KEY (PurchaseInvoiceLineId, InventoryUnitId),
    CONSTRAINT UQ_PurchaseInvoiceLineUnits_Unit UNIQUE (InventoryUnitId)
);

IF OBJECT_ID(N'dbo.PurchaseReturns', N'U') IS NULL
CREATE TABLE dbo.PurchaseReturns
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseReturns PRIMARY KEY,
    ReturnNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_PurchaseReturns_Number UNIQUE,
    PurchaseInvoiceId BIGINT NOT NULL CONSTRAINT FK_PurchaseReturns_Invoices FOREIGN KEY REFERENCES dbo.PurchaseInvoices(Id),
    ReturnedAtUtc DATETIME2(7) NOT NULL,
    Reason NVARCHAR(1000) NOT NULL CONSTRAINT DF_PurchaseReturns_Reason DEFAULT N'',
    ActorUserId INT NOT NULL CONSTRAINT FK_PurchaseReturns_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_PurchaseReturns_CreatedAtUtc DEFAULT SYSUTCDATETIME()
);

IF OBJECT_ID(N'dbo.PurchaseReturnLines', N'U') IS NULL
CREATE TABLE dbo.PurchaseReturnLines
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseReturnLines PRIMARY KEY,
    PurchaseReturnId BIGINT NOT NULL CONSTRAINT FK_PurchaseReturnLines_Returns FOREIGN KEY REFERENCES dbo.PurchaseReturns(Id),
    PurchaseInvoiceLineId BIGINT NOT NULL CONSTRAINT FK_PurchaseReturnLines_Lines FOREIGN KEY REFERENCES dbo.PurchaseInvoiceLines(Id),
    Quantity INT NOT NULL,
    UnitPurchaseCost DECIMAL(19,2) NOT NULL,
    CONSTRAINT UQ_PurchaseReturnLines_Line UNIQUE (PurchaseReturnId, PurchaseInvoiceLineId),
    CONSTRAINT CK_PurchaseReturnLines_Values CHECK (Quantity > 0 AND UnitPurchaseCost >= 0)
);

IF OBJECT_ID(N'dbo.PurchasePayments', N'U') IS NULL
CREATE TABLE dbo.PurchasePayments
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchasePayments PRIMARY KEY,
    PurchaseInvoiceId BIGINT NOT NULL CONSTRAINT FK_PurchasePayments_Invoices FOREIGN KEY REFERENCES dbo.PurchaseInvoices(Id),
    FinancialAccountId INT NOT NULL CONSTRAINT FK_PurchasePayments_Accounts FOREIGN KEY REFERENCES dbo.FinancialAccounts(Id),
    PaymentKind TINYINT NOT NULL,
    Amount DECIMAL(19,2) NOT NULL,
    PaidAtUtc DATETIME2(7) NOT NULL,
    Note NVARCHAR(1000) NOT NULL CONSTRAINT DF_PurchasePayments_Note DEFAULT N'',
    ActorUserId INT NOT NULL CONSTRAINT FK_PurchasePayments_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_PurchasePayments_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_PurchasePayments_Kind CHECK (PaymentKind IN (1, 2)), -- 1 paid to supplier, 2 refund received
    CONSTRAINT CK_PurchasePayments_Amount CHECK (Amount > 0)
);

IF OBJECT_ID(N'dbo.PurchaseInvoiceActivities', N'U') IS NULL
CREATE TABLE dbo.PurchaseInvoiceActivities
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoiceActivities PRIMARY KEY,
    PurchaseInvoiceId BIGINT NOT NULL CONSTRAINT FK_PurchaseActivities_Invoices FOREIGN KEY REFERENCES dbo.PurchaseInvoices(Id),
    OccurredAtUtc DATETIME2(7) NOT NULL,
    ActionTitle NVARCHAR(200) NOT NULL,
    Details NVARCHAR(1500) NOT NULL CONSTRAINT DF_PurchaseActivities_Details DEFAULT N'',
    Amount DECIMAL(19,2) NULL,
    ActorUserId INT NOT NULL CONSTRAINT FK_PurchaseActivities_Actor FOREIGN KEY REFERENCES dbo.Users(Id)
);

/* Sales, payments and activity. */
IF OBJECT_ID(N'dbo.SalesInvoices', N'U') IS NULL
CREATE TABLE dbo.SalesInvoices
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoices PRIMARY KEY,
    InvoiceNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_SalesInvoices_Number UNIQUE,
    CustomerId INT NULL CONSTRAINT FK_SalesInvoices_Customers FOREIGN KEY REFERENCES dbo.Customers(Id),
    CustomerNameSnapshot NVARCHAR(200) NOT NULL,
    CustomerPhoneSnapshot NVARCHAR(30) NOT NULL CONSTRAINT DF_SalesInvoices_CustomerPhone DEFAULT N'',
    CustomerAddressSnapshot NVARCHAR(500) NOT NULL CONSTRAINT DF_SalesInvoices_CustomerAddress DEFAULT N'',
    IsWalkInCustomer BIT NOT NULL CONSTRAINT DF_SalesInvoices_IsWalkIn DEFAULT 0,
    PriceTier TINYINT NOT NULL CONSTRAINT DF_SalesInvoices_PriceTier DEFAULT 0,
    IssuedAtUtc DATETIME2(7) NOT NULL,
    InvoiceStatus TINYINT NOT NULL CONSTRAINT DF_SalesInvoices_Status DEFAULT 0,
    DiscountAmount DECIMAL(19,2) NOT NULL CONSTRAINT DF_SalesInvoices_Discount DEFAULT 0,
    Notes NVARCHAR(1000) NOT NULL CONSTRAINT DF_SalesInvoices_Notes DEFAULT N'',
    CashierUserId INT NOT NULL CONSTRAINT FK_SalesInvoices_Cashier FOREIGN KEY REFERENCES dbo.Users(Id),
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_SalesInvoices_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_SalesInvoices_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_SalesInvoices_PriceTier CHECK (PriceTier BETWEEN 0 AND 2),
    CONSTRAINT CK_SalesInvoices_Status CHECK (InvoiceStatus BETWEEN 0 AND 5),
    CONSTRAINT CK_SalesInvoices_Discount CHECK (DiscountAmount >= 0),
    CONSTRAINT CK_SalesInvoices_Customer CHECK ((IsWalkInCustomer = 1 AND CustomerId IS NULL) OR (IsWalkInCustomer = 0 AND CustomerId IS NOT NULL))
);

IF OBJECT_ID(N'dbo.SalesInvoiceLines', N'U') IS NULL
CREATE TABLE dbo.SalesInvoiceLines
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoiceLines PRIMARY KEY,
    SalesInvoiceId BIGINT NOT NULL CONSTRAINT FK_SalesInvoiceLines_Invoices FOREIGN KEY REFERENCES dbo.SalesInvoices(Id),
    LineNumber INT NOT NULL,
    ProductId INT NOT NULL CONSTRAINT FK_SalesInvoiceLines_Products FOREIGN KEY REFERENCES dbo.Products(Id),
    BarcodeSnapshot NVARCHAR(100) NOT NULL CONSTRAINT DF_SalesInvoiceLines_Barcode DEFAULT N'',
    ProductNameSnapshot NVARCHAR(250) NOT NULL,
    CategoryNameSnapshot NVARCHAR(200) NOT NULL CONSTRAINT DF_SalesInvoiceLines_Category DEFAULT N'',
    UnitNameSnapshot NVARCHAR(50) NOT NULL,
    TrackSerialNumber BIT NOT NULL,
    PriceTier TINYINT NOT NULL,
    Quantity INT NOT NULL,
    ReturnedQuantity INT NOT NULL CONSTRAINT DF_SalesInvoiceLines_Returned DEFAULT 0,
    UnitPrice DECIMAL(19,2) NOT NULL,
    UnitCost DECIMAL(19,2) NOT NULL,
    IsExchangeReplacement BIT NOT NULL CONSTRAINT DF_SalesInvoiceLines_Replacement DEFAULT 0,
    CONSTRAINT UQ_SalesInvoiceLines_Line UNIQUE (SalesInvoiceId, LineNumber),
    CONSTRAINT CK_SalesInvoiceLines_PriceTier CHECK (PriceTier BETWEEN 0 AND 2),
    CONSTRAINT CK_SalesInvoiceLines_Quantity CHECK (Quantity > 0 AND ReturnedQuantity >= 0 AND ReturnedQuantity <= Quantity),
    CONSTRAINT CK_SalesInvoiceLines_Values CHECK (UnitPrice >= 0 AND UnitCost >= 0)
);

IF OBJECT_ID(N'dbo.SalesInvoiceLineUnits', N'U') IS NULL
CREATE TABLE dbo.SalesInvoiceLineUnits
(
    SalesInvoiceLineId BIGINT NOT NULL CONSTRAINT FK_SalesLineUnits_Lines FOREIGN KEY REFERENCES dbo.SalesInvoiceLines(Id),
    InventoryUnitId BIGINT NOT NULL CONSTRAINT FK_SalesLineUnits_Units FOREIGN KEY REFERENCES dbo.InventoryUnits(Id),
    AllocationStatus TINYINT NOT NULL CONSTRAINT DF_SalesInvoiceLineUnits_Status DEFAULT 0,
    CONSTRAINT PK_SalesInvoiceLineUnits PRIMARY KEY (SalesInvoiceLineId, InventoryUnitId),
    CONSTRAINT CK_SalesInvoiceLineUnits_Status CHECK (AllocationStatus IN (0, 1)) -- 0 current sale, 1 returned to stock
);

IF OBJECT_ID(N'dbo.SalesPayments', N'U') IS NULL
CREATE TABLE dbo.SalesPayments
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesPayments PRIMARY KEY,
    SalesInvoiceId BIGINT NOT NULL CONSTRAINT FK_SalesPayments_Invoices FOREIGN KEY REFERENCES dbo.SalesInvoices(Id),
    FinancialAccountId INT NOT NULL CONSTRAINT FK_SalesPayments_Accounts FOREIGN KEY REFERENCES dbo.FinancialAccounts(Id),
    PaymentKind TINYINT NOT NULL,
    Amount DECIMAL(19,2) NOT NULL,
    PaidAtUtc DATETIME2(7) NOT NULL,
    Note NVARCHAR(1000) NOT NULL CONSTRAINT DF_SalesPayments_Note DEFAULT N'',
    ActorUserId INT NOT NULL CONSTRAINT FK_SalesPayments_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_SalesPayments_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_SalesPayments_Kind CHECK (PaymentKind IN (1, 2)), -- 1 collected, 2 refunded
    CONSTRAINT CK_SalesPayments_Amount CHECK (Amount > 0)
);

IF OBJECT_ID(N'dbo.SalesInvoiceActivities', N'U') IS NULL
CREATE TABLE dbo.SalesInvoiceActivities
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoiceActivities PRIMARY KEY,
    SalesInvoiceId BIGINT NOT NULL CONSTRAINT FK_SalesActivities_Invoices FOREIGN KEY REFERENCES dbo.SalesInvoices(Id),
    OccurredAtUtc DATETIME2(7) NOT NULL,
    ActionTitle NVARCHAR(200) NOT NULL,
    Details NVARCHAR(1500) NOT NULL CONSTRAINT DF_SalesActivities_Details DEFAULT N'',
    Amount DECIMAL(19,2) NOT NULL CONSTRAINT DF_SalesActivities_Amount DEFAULT 0,
    ActorUserId INT NOT NULL CONSTRAINT FK_SalesActivities_Actor FOREIGN KEY REFERENCES dbo.Users(Id)
);

/* Customer returns and exchanges, including serialized-unit traceability. */
IF OBJECT_ID(N'dbo.ReturnExchanges', N'U') IS NULL
CREATE TABLE dbo.ReturnExchanges
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReturnExchanges PRIMARY KEY,
    OperationNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_ReturnExchanges_Number UNIQUE,
    SalesInvoiceId BIGINT NOT NULL CONSTRAINT FK_ReturnExchanges_Invoices FOREIGN KEY REFERENCES dbo.SalesInvoices(Id),
    OperationType TINYINT NOT NULL,
    CreatedAtUtc DATETIME2(7) NOT NULL,
    ActorUserId INT NOT NULL CONSTRAINT FK_ReturnExchanges_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    Reason NVARCHAR(1000) NOT NULL CONSTRAINT DF_ReturnExchanges_Reason DEFAULT N'',
    SettlementSummary NVARCHAR(1500) NOT NULL CONSTRAINT DF_ReturnExchanges_Settlement DEFAULT N'',
    CONSTRAINT CK_ReturnExchanges_Type CHECK (OperationType IN (0, 1))
);

IF OBJECT_ID(N'dbo.ReturnExchangeLines', N'U') IS NULL
CREATE TABLE dbo.ReturnExchangeLines
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReturnExchangeLines PRIMARY KEY,
    ReturnExchangeId BIGINT NOT NULL CONSTRAINT FK_ReturnExchangeLines_Operations FOREIGN KEY REFERENCES dbo.ReturnExchanges(Id),
    LineKind TINYINT NOT NULL,
    SalesInvoiceLineId BIGINT NULL CONSTRAINT FK_ReturnExchangeLines_SalesLines FOREIGN KEY REFERENCES dbo.SalesInvoiceLines(Id),
    ProductId INT NOT NULL CONSTRAINT FK_ReturnExchangeLines_Products FOREIGN KEY REFERENCES dbo.Products(Id),
    ProductNameSnapshot NVARCHAR(250) NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(19,2) NOT NULL,
    UnitCost DECIMAL(19,2) NOT NULL CONSTRAINT DF_ReturnExchangeLines_Cost DEFAULT 0,
    UnitCondition TINYINT NOT NULL CONSTRAINT DF_ReturnExchangeLines_Condition DEFAULT 0,
    CONSTRAINT CK_ReturnExchangeLines_Kind CHECK (LineKind IN (1, 2)), -- 1 returned, 2 replacement
    CONSTRAINT CK_ReturnExchangeLines_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_ReturnExchangeLines_Values CHECK (UnitPrice >= 0 AND UnitCost >= 0),
    CONSTRAINT CK_ReturnExchangeLines_Condition CHECK (UnitCondition IN (0, 1)),
    CONSTRAINT CK_ReturnExchangeLines_Source CHECK ((LineKind = 1 AND SalesInvoiceLineId IS NOT NULL) OR (LineKind = 2 AND SalesInvoiceLineId IS NULL))
);

IF OBJECT_ID(N'dbo.ReturnExchangeUnits', N'U') IS NULL
CREATE TABLE dbo.ReturnExchangeUnits
(
    ReturnExchangeLineId BIGINT NOT NULL CONSTRAINT FK_ReturnExchangeUnits_Lines FOREIGN KEY REFERENCES dbo.ReturnExchangeLines(Id),
    InventoryUnitId BIGINT NOT NULL CONSTRAINT FK_ReturnExchangeUnits_Units FOREIGN KEY REFERENCES dbo.InventoryUnits(Id),
    CONSTRAINT PK_ReturnExchangeUnits PRIMARY KEY (ReturnExchangeLineId, InventoryUnitId)
);

/* Maintenance orders, workshop parts, payments and history. */
IF OBJECT_ID(N'dbo.MaintenanceOrders', N'U') IS NULL
CREATE TABLE dbo.MaintenanceOrders
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaintenanceOrders PRIMARY KEY,
    OrderNumber NVARCHAR(50) NOT NULL CONSTRAINT UQ_MaintenanceOrders_Number UNIQUE,
    CustomerId INT NULL CONSTRAINT FK_MaintenanceOrders_Customers FOREIGN KEY REFERENCES dbo.Customers(Id),
    CustomerNameSnapshot NVARCHAR(200) NOT NULL,
    CustomerPhoneSnapshot NVARCHAR(30) NOT NULL CONSTRAINT DF_MaintenanceOrders_CustomerPhone DEFAULT N'',
    CustomerSecondaryPhoneSnapshot NVARCHAR(30) NOT NULL CONSTRAINT DF_MaintenanceOrders_CustomerSecondary DEFAULT N'',
    CustomerAddressSnapshot NVARCHAR(500) NOT NULL CONSTRAINT DF_MaintenanceOrders_CustomerAddress DEFAULT N'',
    IsWalkInCustomer BIT NOT NULL CONSTRAINT DF_MaintenanceOrders_IsWalkIn DEFAULT 0,
    ReceivedAtUtc DATETIME2(7) NOT NULL,
    ReceivedByUserId INT NOT NULL CONSTRAINT FK_MaintenanceOrders_Receiver FOREIGN KEY REFERENCES dbo.Users(Id),
    OrderStatus TINYINT NOT NULL CONSTRAINT DF_MaintenanceOrders_Status DEFAULT 0,
    Priority TINYINT NOT NULL CONSTRAINT DF_MaintenanceOrders_Priority DEFAULT 0,
    AssignedTechnician NVARCHAR(150) NOT NULL CONSTRAINT DF_MaintenanceOrders_Technician DEFAULT N'',
    ExpectedDeliveryAtUtc DATETIME2(7) NULL,
    ExpectedDeliveryLabel NVARCHAR(200) NOT NULL CONSTRAINT DF_MaintenanceOrders_DeliveryLabel DEFAULT N'',
    DeviceType NVARCHAR(100) NOT NULL,
    DeviceBrand NVARCHAR(100) NOT NULL CONSTRAINT DF_MaintenanceOrders_Brand DEFAULT N'',
    DeviceModel NVARCHAR(150) NOT NULL CONSTRAINT DF_MaintenanceOrders_Model DEFAULT N'',
    DeviceColor NVARCHAR(80) NOT NULL CONSTRAINT DF_MaintenanceOrders_Color DEFAULT N'',
    SerialOrImei NVARCHAR(150) NOT NULL CONSTRAINT DF_MaintenanceOrders_Serial DEFAULT N'',
    PasscodeType TINYINT NOT NULL CONSTRAINT DF_MaintenanceOrders_PasscodeType DEFAULT 0,
    PasscodeValueProtected NVARCHAR(1000) NOT NULL CONSTRAINT DF_MaintenanceOrders_Passcode DEFAULT N'',
    ReportedFault NVARCHAR(2000) NOT NULL,
    PhysicalCondition NVARCHAR(1500) NOT NULL CONSTRAINT DF_MaintenanceOrders_Physical DEFAULT N'',
    AccessoriesReceived NVARCHAR(1000) NOT NULL CONSTRAINT DF_MaintenanceOrders_Accessories DEFAULT N'',
    IntakeNotes NVARCHAR(1500) NOT NULL CONSTRAINT DF_MaintenanceOrders_IntakeNotes DEFAULT N'',
    IsEstimatePendingInspection BIT NOT NULL CONSTRAINT DF_MaintenanceOrders_EstimatePending DEFAULT 0,
    EstimatedCost DECIMAL(19,2) NOT NULL CONSTRAINT DF_MaintenanceOrders_EstimatedCost DEFAULT 0,
    DiagnosisNotes NVARCHAR(2000) NOT NULL CONSTRAINT DF_MaintenanceOrders_Diagnosis DEFAULT N'',
    LaborCost DECIMAL(19,2) NOT NULL CONSTRAINT DF_MaintenanceOrders_Labor DEFAULT 0,
    DiscountAmount DECIMAL(19,2) NOT NULL CONSTRAINT DF_MaintenanceOrders_Discount DEFAULT 0,
    FinalServiceCost DECIMAL(19,2) NULL,
    HasWorkshopPricing BIT NOT NULL CONSTRAINT DF_MaintenanceOrders_HasPricing DEFAULT 0,
    DeliveredAtUtc DATETIME2(7) NULL,
    WarrantyDays INT NOT NULL CONSTRAINT DF_MaintenanceOrders_Warranty DEFAULT 0,
    DeliveryNotes NVARCHAR(1500) NOT NULL CONSTRAINT DF_MaintenanceOrders_DeliveryNotes DEFAULT N'',
    CancellationReason NVARCHAR(1500) NOT NULL CONSTRAINT DF_MaintenanceOrders_Cancellation DEFAULT N'',
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_MaintenanceOrders_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_MaintenanceOrders_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_MaintenanceOrders_Status CHECK (OrderStatus BETWEEN 0 AND 6),
    CONSTRAINT CK_MaintenanceOrders_Priority CHECK (Priority BETWEEN 0 AND 2),
    CONSTRAINT CK_MaintenanceOrders_PasscodeType CHECK (PasscodeType BETWEEN 0 AND 2),
    CONSTRAINT CK_MaintenanceOrders_Financials CHECK (EstimatedCost >= 0 AND LaborCost >= 0 AND DiscountAmount >= 0 AND (FinalServiceCost IS NULL OR FinalServiceCost >= 0)),
    CONSTRAINT CK_MaintenanceOrders_Warranty CHECK (WarrantyDays >= 0),
    CONSTRAINT CK_MaintenanceOrders_Customer CHECK ((IsWalkInCustomer = 1 AND CustomerId IS NULL) OR (IsWalkInCustomer = 0 AND CustomerId IS NOT NULL))
);

IF OBJECT_ID(N'dbo.MaintenanceOrderParts', N'U') IS NULL
CREATE TABLE dbo.MaintenanceOrderParts
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaintenanceOrderParts PRIMARY KEY,
    MaintenanceOrderId BIGINT NOT NULL CONSTRAINT FK_MaintenanceParts_Orders FOREIGN KEY REFERENCES dbo.MaintenanceOrders(Id),
    ProductId INT NULL CONSTRAINT FK_MaintenanceParts_Products FOREIGN KEY REFERENCES dbo.Products(Id),
    LineNumber INT NOT NULL,
    BarcodeSnapshot NVARCHAR(100) NOT NULL CONSTRAINT DF_MaintenanceParts_Barcode DEFAULT N'',
    ProductNameSnapshot NVARCHAR(250) NOT NULL,
    Quantity INT NOT NULL,
    UnitCost DECIMAL(19,2) NOT NULL,
    UnitSellPrice DECIMAL(19,2) NOT NULL,
    IsManualPart BIT NOT NULL CONSTRAINT DF_MaintenanceParts_IsManual DEFAULT 0,
    CONSTRAINT UQ_MaintenanceOrderParts_Line UNIQUE (MaintenanceOrderId, LineNumber),
    CONSTRAINT CK_MaintenanceOrderParts_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_MaintenanceOrderParts_Values CHECK (UnitCost >= 0 AND UnitSellPrice >= 0),
    CONSTRAINT CK_MaintenanceOrderParts_Source CHECK ((IsManualPart = 1 AND ProductId IS NULL) OR (IsManualPart = 0 AND ProductId IS NOT NULL))
);

IF OBJECT_ID(N'dbo.MaintenancePayments', N'U') IS NULL
CREATE TABLE dbo.MaintenancePayments
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaintenancePayments PRIMARY KEY,
    MaintenanceOrderId BIGINT NOT NULL CONSTRAINT FK_MaintenancePayments_Orders FOREIGN KEY REFERENCES dbo.MaintenanceOrders(Id),
    FinancialAccountId INT NOT NULL CONSTRAINT FK_MaintenancePayments_Accounts FOREIGN KEY REFERENCES dbo.FinancialAccounts(Id),
    PaymentStage TINYINT NOT NULL,
    PaymentKind TINYINT NOT NULL,
    Amount DECIMAL(19,2) NOT NULL,
    PaidAtUtc DATETIME2(7) NOT NULL,
    Note NVARCHAR(1000) NOT NULL CONSTRAINT DF_MaintenancePayments_Note DEFAULT N'',
    ActorUserId INT NOT NULL CONSTRAINT FK_MaintenancePayments_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_MaintenancePayments_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_MaintenancePayments_Stage CHECK (PaymentStage IN (1, 2)), -- 1 deposit, 2 final
    CONSTRAINT CK_MaintenancePayments_Kind CHECK (PaymentKind IN (1, 2)), -- 1 collected, 2 refunded
    CONSTRAINT CK_MaintenancePayments_Amount CHECK (Amount > 0)
);

IF OBJECT_ID(N'dbo.MaintenanceActivities', N'U') IS NULL
CREATE TABLE dbo.MaintenanceActivities
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaintenanceActivities PRIMARY KEY,
    MaintenanceOrderId BIGINT NOT NULL CONSTRAINT FK_MaintenanceActivities_Orders FOREIGN KEY REFERENCES dbo.MaintenanceOrders(Id),
    OccurredAtUtc DATETIME2(7) NOT NULL,
    ActionTitle NVARCHAR(200) NOT NULL,
    Details NVARCHAR(1500) NOT NULL CONSTRAINT DF_MaintenanceActivities_Details DEFAULT N'',
    Amount DECIMAL(19,2) NULL,
    ActorUserId INT NOT NULL CONSTRAINT FK_MaintenanceActivities_Actor FOREIGN KEY REFERENCES dbo.Users(Id)
);

/* Append-only cross-module audit. EntityId is intentionally not a polymorphic FK. */
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
CREATE TABLE dbo.AuditLog
(
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    OccurredAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AuditLog_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
    ActorUserId INT NULL CONSTRAINT FK_AuditLog_Actor FOREIGN KEY REFERENCES dbo.Users(Id),
    Module NVARCHAR(100) NOT NULL,
    ActionName NVARCHAR(150) NOT NULL,
    EntityType NVARCHAR(100) NOT NULL,
    EntityId NVARCHAR(100) NOT NULL CONSTRAINT DF_AuditLog_EntityId DEFAULT N'',
    OldValuesJson NVARCHAR(MAX) NULL,
    NewValuesJson NVARCHAR(MAX) NULL,
    ClientMachineName NVARCHAR(150) NOT NULL CONSTRAINT DF_AuditLog_Client DEFAULT N'',
    CONSTRAINT CK_AuditLog_OldValuesJson CHECK (OldValuesJson IS NULL OR ISJSON(OldValuesJson) = 1),
    CONSTRAINT CK_AuditLog_NewValuesJson CHECK (NewValuesJson IS NULL OR ISJSON(NewValuesJson) = 1)
);

COMMIT TRANSACTION;
GO

/* Idempotent indexes: operational lookups, history ordering and balance queries. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'UX_Customers_Phone')
    CREATE UNIQUE INDEX UX_Customers_Phone ON dbo.Customers(Phone) WHERE Phone IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'IX_Customers_Name')
    CREATE INDEX IX_Customers_Name ON dbo.Customers(Name) INCLUDE (Phone, IsActive);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Suppliers') AND name = N'IX_Suppliers_Name')
    CREATE INDEX IX_Suppliers_Name ON dbo.Suppliers(Name) INCLUDE (CompanyName, Phone, IsActive);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'UX_Products_Barcode')
    CREATE UNIQUE INDEX UX_Products_Barcode ON dbo.Products(Barcode) WHERE Barcode IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'UX_Products_Sku')
    CREATE UNIQUE INDEX UX_Products_Sku ON dbo.Products(Sku) WHERE Sku IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'IX_Products_Category_Name')
    CREATE INDEX IX_Products_Category_Name ON dbo.Products(CategoryId, Name) INCLUDE (IsActiveForSale, RetailPrice);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FinancialAccounts') AND name = N'UX_FinancialAccounts_SingleCash')
    CREATE UNIQUE INDEX UX_FinancialAccounts_SingleCash ON dbo.FinancialAccounts(AccountKind) WHERE AccountKind = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FinancialAccounts') AND name = N'UX_FinancialAccounts_OneDefaultPerKind')
    CREATE UNIQUE INDEX UX_FinancialAccounts_OneDefaultPerKind ON dbo.FinancialAccounts(AccountKind) WHERE IsDefault = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerTransfers') AND name = N'IX_CustomerTransfers_Account_Date')
    CREATE INDEX IX_CustomerTransfers_Account_Date ON dbo.CustomerTransfers(FinancialAccountId, OccurredAtUtc DESC) INCLUDE (OperationKind, Amount, Commission, OperationStatus);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CustomerTransfers') AND name = N'IX_CustomerTransfers_Customer')
    CREATE INDEX IX_CustomerTransfers_Customer ON dbo.CustomerTransfers(CustomerNumber, OccurredAtUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FinancialTransactions') AND name = N'IX_FinancialTransactions_Account_Date')
    CREATE INDEX IX_FinancialTransactions_Account_Date ON dbo.FinancialTransactions(FinancialAccountId, OccurredAtUtc DESC, Id DESC) INCLUDE (IsIncoming, Amount, BalanceAfter, CountsTowardLimits);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.FinancialTransactions') AND name = N'IX_FinancialTransactions_TransferGroup')
    CREATE INDEX IX_FinancialTransactions_TransferGroup ON dbo.FinancialTransactions(TransferGroupId) WHERE TransferGroupId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryUnits') AND name = N'UX_InventoryUnits_Serial')
    CREATE UNIQUE INDEX UX_InventoryUnits_Serial ON dbo.InventoryUnits(SerialNumber) WHERE SerialNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryUnits') AND name = N'IX_InventoryUnits_Product_Status')
    CREATE INDEX IX_InventoryUnits_Product_Status ON dbo.InventoryUnits(ProductId, UnitStatus) INCLUDE (SupplierId, PurchaseCost, SerialNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SalesInvoiceLineUnits') AND name = N'UX_SalesInvoiceLineUnits_ActiveUnit')
    CREATE UNIQUE INDEX UX_SalesInvoiceLineUnits_ActiveUnit ON dbo.SalesInvoiceLineUnits(InventoryUnitId) WHERE AllocationStatus = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StockMovements') AND name = N'IX_StockMovements_Product_Date')
    CREATE INDEX IX_StockMovements_Product_Date ON dbo.StockMovements(ProductId, OccurredAtUtc DESC, Id DESC) INCLUDE (QuantityDelta, UnitCost);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PurchaseInvoices') AND name = N'IX_PurchaseInvoices_Supplier_Date')
    CREATE INDEX IX_PurchaseInvoices_Supplier_Date ON dbo.PurchaseInvoices(SupplierId, IssuedAtUtc DESC) INCLUDE (InvoiceStatus, InvoiceNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SalesInvoices') AND name = N'IX_SalesInvoices_Customer_Date')
    CREATE INDEX IX_SalesInvoices_Customer_Date ON dbo.SalesInvoices(CustomerId, IssuedAtUtc DESC) INCLUDE (InvoiceStatus, InvoiceNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SalesInvoices') AND name = N'IX_SalesInvoices_Date')
    CREATE INDEX IX_SalesInvoices_Date ON dbo.SalesInvoices(IssuedAtUtc DESC) INCLUDE (InvoiceNumber, CustomerNameSnapshot, InvoiceStatus);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SalesPayments') AND name = N'IX_SalesPayments_Invoice_Date')
    CREATE INDEX IX_SalesPayments_Invoice_Date ON dbo.SalesPayments(SalesInvoiceId, PaidAtUtc DESC) INCLUDE (FinancialAccountId, PaymentKind, Amount);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PurchasePayments') AND name = N'IX_PurchasePayments_Invoice_Date')
    CREATE INDEX IX_PurchasePayments_Invoice_Date ON dbo.PurchasePayments(PurchaseInvoiceId, PaidAtUtc DESC) INCLUDE (FinancialAccountId, PaymentKind, Amount);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders') AND name = N'IX_MaintenanceOrders_Status_Received')
    CREATE INDEX IX_MaintenanceOrders_Status_Received ON dbo.MaintenanceOrders(OrderStatus, ReceivedAtUtc DESC) INCLUDE (OrderNumber, CustomerNameSnapshot, DeviceModel);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MaintenanceOrders') AND name = N'IX_MaintenanceOrders_Customer')
    CREATE INDEX IX_MaintenanceOrders_Customer ON dbo.MaintenanceOrders(CustomerId, ReceivedAtUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MaintenancePayments') AND name = N'IX_MaintenancePayments_Order_Date')
    CREATE INDEX IX_MaintenancePayments_Order_Date ON dbo.MaintenancePayments(MaintenanceOrderId, PaidAtUtc DESC) INCLUDE (PaymentStage, PaymentKind, Amount);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AuditLog') AND name = N'IX_AuditLog_Entity_Date')
    CREATE INDEX IX_AuditLog_Entity_Date ON dbo.AuditLog(EntityType, EntityId, OccurredAtUtc DESC);
GO

/* Core lookup data only; no sample business transactions are inserted. */
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK) WHERE Name = N'Administrator')
    INSERT dbo.Roles(Name, Description, IsSystem) VALUES (N'Administrator', N'Full system administration', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK) WHERE Name = N'Cashier')
    INSERT dbo.Roles(Name, Description, IsSystem) VALUES (N'Cashier', N'Sales and customer collections', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK) WHERE Name = N'Technician')
    INSERT dbo.Roles(Name, Description, IsSystem) VALUES (N'Technician', N'Maintenance operations', 1);

IF EXISTS (SELECT 1 FROM dbo.Users WHERE Username = N'admin')
   AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id = ur.RoleId JOIN dbo.Users u ON u.Id = ur.UserId WHERE u.Username = N'admin' AND r.Name = N'Administrator')
    INSERT dbo.UserRoles(UserId, RoleId)
    SELECT u.Id, r.Id FROM dbo.Users u CROSS JOIN dbo.Roles r WHERE u.Username = N'admin' AND r.Name = N'Administrator';

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'CurrencyCode')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description) VALUES (N'CurrencyCode', N'EGP', N'string', N'ISO currency code');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'CurrencyNameAr')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description) VALUES (N'CurrencyNameAr', N'ج.م', N'string', N'Arabic currency label');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'SchemaVersion')
    INSERT dbo.SystemSettings(SettingKey, SettingValue, ValueType, Description) VALUES (N'SchemaVersion', N'1', N'integer', N'ProjectZ business schema version');
GO

PRINT N'ProjectZ full business schema created/verified successfully.';
GO
