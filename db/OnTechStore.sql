IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] int NOT NULL IDENTITY,
        [CompanyName] nvarchar(200) NOT NULL,
        [PhoneNumber] nvarchar(50) NULL,
        [TaxNumber] nvarchar(50) NULL,
        [ContactPerson] nvarchar(150) NULL,
        [Email] nvarchar(256) NULL,
        [Address] nvarchar(500) NULL,
        [Notes] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByUserId] int NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [NormalizedEmail] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(512) NOT NULL,
        [IsAdmin] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [MustChangePassword] bit NOT NULL,
        [Permissions] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastLoginAt] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE TABLE [Items] (
        [Id] int NOT NULL IDENTITY,
        [CategoryId] int NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Code] nvarchar(64) NULL,
        [Description] nvarchar(500) NULL,
        [Quantity] int NOT NULL,
        [LowStockThreshold] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Items] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Items_LowStockThreshold_NonNegative] CHECK ([LowStockThreshold] >= 0),
        CONSTRAINT [CK_Items_Quantity_NonNegative] CHECK ([Quantity] >= 0),
        CONSTRAINT [FK_Items_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] int NOT NULL IDENTITY,
        [OrderNumber] nvarchar(30) NOT NULL,
        [CustomerId] int NOT NULL,
        [WithReceipt] bit NOT NULL,
        [ReceiptNumber] nvarchar(50) NULL,
        [Notes] nvarchar(1000) NULL,
        [Status] int NOT NULL,
        [CreatedByUserId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CancelledAt] datetime2 NULL,
        [CancelledByUserId] int NULL,
        [CancelReason] nvarchar(500) NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Orders_Users_CancelledByUserId] FOREIGN KEY ([CancelledByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Orders_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderLines] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [ItemId] int NOT NULL,
        [Quantity] int NOT NULL,
        [ItemName] nvarchar(150) NOT NULL,
        [ItemCode] nvarchar(64) NULL,
        [CategoryId] int NOT NULL,
        [CategoryName] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_OrderLines] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderLines_Quantity_Positive] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_OrderLines_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE TABLE [StockMovements] (
        [Id] bigint NOT NULL IDENTITY,
        [ItemId] int NOT NULL,
        [Type] int NOT NULL,
        [Change] int NOT NULL,
        [QuantityAfter] int NOT NULL,
        [OrderId] int NULL,
        [UserId] int NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StockMovements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockMovements_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockMovements_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockMovements_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Name] ON [Categories] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Customers_CompanyName] ON [Customers] ([CompanyName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Customers_TaxNumber] ON [Customers] ([TaxNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Items_CategoryId_Name] ON [Items] ([CategoryId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Items_Code] ON [Items] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderLines_CategoryId] ON [OrderLines] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderLines_ItemId] ON [OrderLines] ([ItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderLines_OrderId] ON [OrderLines] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CancelledByUserId] ON [Orders] ([CancelledByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CreatedAt] ON [Orders] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CreatedByUserId_CreatedAt] ON [Orders] ([CreatedByUserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerId_CreatedAt] ON [Orders] ([CustomerId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_OrderNumber] ON [Orders] ([OrderNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_ReceiptNumber] ON [Orders] ([ReceiptNumber]) WHERE [ReceiptNumber] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockMovements_CreatedAt] ON [StockMovements] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockMovements_ItemId_CreatedAt] ON [StockMovements] ([ItemId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockMovements_OrderId] ON [StockMovements] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockMovements_UserId] ON [StockMovements] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027184848_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027184848_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

