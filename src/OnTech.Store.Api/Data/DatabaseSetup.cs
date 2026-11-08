using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Data;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "ChangeMe!123";
    public string AdminName { get; set; } = "Administrator";
}

public static class DatabaseSetup
{
    public static IServiceCollection AddStoreDatabase(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "SqlServer";
        var cs = config.GetConnectionString("Store") ?? throw new InvalidOperationException("ConnectionStrings:Store is missing.");
        services.AddDbContext<StoreDbContext>(o =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) o.UseSqlite(cs);
            else o.UseSqlServer(cs, sql => sql.CommandTimeout(30));
        });
        return services;
    }

    // EnsureCreated never changes an existing SQLite file, so columns added after it was made are added here.
    private static async Task UpgradeSqliteAsync(StoreDbContext db, ILogger log)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Customers') WHERE name = 'CustomerNumber'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Customers ADD COLUMN CustomerNumber INTEGER NULL");
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Customers_CustomerNumber ON Customers (CustomerNumber)");
        }
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Categories') WHERE name = 'Code'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Categories ADD COLUMN Code TEXT NULL");
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_Code ON Categories (Code)");
        }
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_index_list('Items') WHERE name = 'IX_Items_Code' AND \"unique\" = 1";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
        {
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Items_Code");
            await db.Database.ExecuteSqlRawAsync("UPDATE Items SET Code = NULL WHERE Id NOT IN (SELECT MIN(Id) FROM Items WHERE Code IS NOT NULL GROUP BY Code)");
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IX_Items_Code ON Items (Code) WHERE Code IS NOT NULL");
        }
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS AppliedSeeds (Name TEXT NOT NULL CONSTRAINT PK_AppliedSeeds PRIMARY KEY, AppliedAt TEXT NOT NULL)");

        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Suppliers'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
        {
            await BackupSqliteAsync(db, conn.DataSource, "before-suppliers", log);
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var sql in SuppliersAndStockInSql) await db.Database.ExecuteSqlRawAsync(sql);
            await tx.CommitAsync();
            log.LogWarning("Database upgraded: suppliers, stock-in vouchers and issue voucher numbers added.");
        }

        await UpgradeSqliteAuditFixesAsync(db, log);
    }

    private static async Task<long> ScalarAsync(StoreDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Tables that carry the <c>Version</c> concurrency token (M-1). Same column as the AuditFixes migration.</summary>
    private static readonly string[] VersionedTables = ["Users", "Items", "Customers", "Suppliers"];

    /// <summary>
    /// Unique document-number indexes that must ignore letter case (M-4), with the columns upper-cased alongside.
    /// <c>Also</c> is a column that holds the same value and is upper-cased with it (the order number is the issue voucher).
    /// </summary>
    private static readonly (string Table, string Column, string Index, string Filter, string? Also)[] DocNumberIndexes =
    [
        ("Orders", "IssueVoucherNumber", "IX_Orders_IssueVoucherNumber", "WHERE [IssueVoucherNumber] IS NOT NULL", "OrderNumber"),
        ("Orders", "ReceiptNumber", "IX_Orders_ReceiptNumber", "WHERE [ReceiptNumber] IS NOT NULL", null),
        ("StockIns", "VoucherNumber", "IX_StockIns_VoucherNumber", "", "SupplierInvoiceNumber"),
    ];

    /// <summary>
    /// The audit fixes (AuditFixes migration on SQL Server): Version columns (M-1) and case-insensitive document-number
    /// indexes (M-4). Each step checks first, so it runs once; the file is backed up before the first change and all steps
    /// run in one transaction.
    /// </summary>
    private static async Task UpgradeSqliteAuditFixesAsync(StoreDbContext db, ILogger log)
    {
        var steps = new List<string>();
        foreach (var table in VersionedTables)
            if (await ScalarAsync(db, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = 'Version'") == 0)
                steps.Add($"ALTER TABLE {table} ADD COLUMN Version INTEGER NOT NULL DEFAULT 0");

        foreach (var (table, column, index, filter, also) in DocNumberIndexes)
        {
            // New files already have NOCASE columns, so their indexes report NOCASE too.
            if (await ScalarAsync(db, $"SELECT COUNT(*) FROM pragma_index_xinfo('{index}') WHERE cid >= 0 AND coll = 'NOCASE'") > 0) continue;
            var collisions = await ScalarAsync(db,
                $"SELECT COUNT(*) FROM (SELECT UPPER({column}) FROM {table} WHERE {column} IS NOT NULL GROUP BY UPPER({column}) HAVING COUNT(*) > 1)");
            if (collisions > 0)
            {
                // Two vouchers that differ only in letter case: leave the data and the old index alone and let the owner decide.
                log.LogWarning("{Table}.{Column} has {Count} values that differ only in letter case; its index stays case-sensitive.", table, column, collisions);
                continue;
            }
            steps.Add($"UPDATE {table} SET {column} = UPPER({column}) WHERE {column} <> UPPER({column}) COLLATE BINARY");
            if (also is not null) steps.Add($"UPDATE {table} SET {also} = UPPER({also}) WHERE {also} <> UPPER({also}) COLLATE BINARY");
            steps.Add($"DROP INDEX IF EXISTS {index}");
            steps.Add($"CREATE UNIQUE INDEX {index} ON {table} ({column} COLLATE NOCASE) {filter}");
        }

        if (steps.Count == 0) return;
        await BackupSqliteAsync(db, db.Database.GetDbConnection().DataSource, "before-audit-fixes", log);
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var sql in steps) await db.Database.ExecuteSqlRawAsync(sql);
        await tx.CommitAsync();
        log.LogWarning("Database upgraded for the audit fixes ({Count} steps).", steps.Count);
    }

    // Suppliers, stock-in vouchers, the order's issue voucher number. Same tables and indexes as the 20261028124843_SuppliersAndStockIn migration.
    private static readonly string[] SuppliersAndStockInSql =
    [
        """
        CREATE TABLE Suppliers (
            Id INTEGER NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY AUTOINCREMENT,
            SupplierNumber INTEGER NULL, CompanyName TEXT NOT NULL, PhoneNumber TEXT NULL, TaxNumber TEXT NULL,
            ContactPerson TEXT NULL, Email TEXT NULL, Address TEXT NULL, Notes TEXT NULL,
            IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL, CreatedByUserId INTEGER NOT NULL)
        """,
        "CREATE INDEX IX_Suppliers_CompanyName ON Suppliers (CompanyName)",
        "CREATE INDEX IX_Suppliers_TaxNumber ON Suppliers (TaxNumber)",
        "CREATE UNIQUE INDEX IX_Suppliers_SupplierNumber ON Suppliers (SupplierNumber)",
        """
        CREATE TABLE StockIns (
            Id INTEGER NOT NULL CONSTRAINT PK_StockIns PRIMARY KEY AUTOINCREMENT,
            StockInNumber TEXT NOT NULL,
            SupplierId INTEGER NOT NULL CONSTRAINT FK_StockIns_Suppliers_SupplierId REFERENCES Suppliers (Id) ON DELETE RESTRICT,
            VoucherNumber TEXT NOT NULL, SupplierInvoiceNumber TEXT NULL, Notes TEXT NULL, Status INTEGER NOT NULL,
            CreatedByUserId INTEGER NOT NULL CONSTRAINT FK_StockIns_Users_CreatedByUserId REFERENCES Users (Id) ON DELETE RESTRICT,
            CreatedAt TEXT NOT NULL, CancelledAt TEXT NULL,
            CancelledByUserId INTEGER NULL CONSTRAINT FK_StockIns_Users_CancelledByUserId REFERENCES Users (Id) ON DELETE RESTRICT,
            CancelReason TEXT NULL)
        """,
        "CREATE INDEX IX_StockIns_StockInNumber ON StockIns (StockInNumber)",
        "CREATE UNIQUE INDEX IX_StockIns_VoucherNumber ON StockIns (VoucherNumber)",
        "CREATE INDEX IX_StockIns_CreatedAt ON StockIns (CreatedAt)",
        "CREATE INDEX IX_StockIns_SupplierId_CreatedAt ON StockIns (SupplierId, CreatedAt)",
        "CREATE INDEX IX_StockIns_CreatedByUserId ON StockIns (CreatedByUserId)",
        "CREATE INDEX IX_StockIns_CancelledByUserId ON StockIns (CancelledByUserId)",
        """
        CREATE TABLE StockInLines (
            Id INTEGER NOT NULL CONSTRAINT PK_StockInLines PRIMARY KEY AUTOINCREMENT,
            StockInId INTEGER NOT NULL CONSTRAINT FK_StockInLines_StockIns_StockInId REFERENCES StockIns (Id) ON DELETE CASCADE,
            ItemId INTEGER NOT NULL CONSTRAINT FK_StockInLines_Items_ItemId REFERENCES Items (Id) ON DELETE RESTRICT,
            Quantity INTEGER NOT NULL, ItemName TEXT NOT NULL, ItemCode TEXT NULL, CategoryId INTEGER NOT NULL, CategoryName TEXT NOT NULL,
            CONSTRAINT CK_StockInLines_Quantity_Positive CHECK ([Quantity] > 0))
        """,
        "CREATE INDEX IX_StockInLines_ItemId ON StockInLines (ItemId)",
        "CREATE INDEX IX_StockInLines_StockInId ON StockInLines (StockInId)",
        "ALTER TABLE StockMovements ADD COLUMN StockInId INTEGER NULL CONSTRAINT FK_StockMovements_StockIns_StockInId REFERENCES StockIns (Id) ON DELETE RESTRICT",
        "CREATE INDEX IX_StockMovements_StockInId ON StockMovements (StockInId)",
        "ALTER TABLE Orders ADD COLUMN IssueVoucherNumber TEXT NULL",
        "CREATE UNIQUE INDEX IX_Orders_IssueVoucherNumber ON Orders (IssueVoucherNumber) WHERE [IssueVoucherNumber] IS NOT NULL",
        GrantSupplierPermissionSql,
    ];

    /// <summary>Everyone who could manage customers can now manage suppliers too (Permission.ManageSuppliers = 256).</summary>
    internal const string GrantSupplierPermissionSql = "UPDATE Users SET Permissions = Permissions | 256 WHERE (Permissions & 2) <> 0";

    /// <summary>Copies the SQLite file next to itself before an upgrade, so the old data can always be restored.</summary>
    private static async Task BackupSqliteAsync(StoreDbContext db, string dataSource, string label, ILogger log)
    {
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:" || !File.Exists(dataSource)) return;
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataSource))!,
            $"{Path.GetFileNameWithoutExtension(dataSource)}-backup-{label}-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        // VACUUM INTO writes a consistent copy even while the file is open.
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", path);
        log.LogWarning("Backed up the database to {Path} before upgrading it.", path);
    }

    /// <summary>Creates or upgrades the database on startup and makes sure an admin exists.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

        if (db.Database.IsSqlServer()) await db.Database.MigrateAsync();
        else
        {
            await db.Database.EnsureCreatedAsync(); // SQLite is used for tests and quick local trials.
            await UpgradeSqliteAsync(db, log);
        }

        await UseVoucherAsOrderNumberAsync(db, log);
        if (!await db.Users.AnyAsync(u => u.IsAdmin)) await CreateFirstAdminAsync(scope.ServiceProvider, db, log);
        await OpeningData.ApplyAsync(db, log);
        await CatalogCodes.MakeNumericAsync(db);
        await CatalogCodes.BackfillAsync(db);
    }

    /// <summary>
    /// The issue voucher number is the order's number. Orders saved before that keep their old ORD-000123 number as their voucher
    /// (when no other order already uses it), and every order's number is set to its voucher. Runs once; afterwards nothing changes.
    /// A voucher longer than the order number column (saved when vouchers could be 50 characters) keeps its order's old number,
    /// so the update never fails on SQL Server.
    /// </summary>
    private static async Task UseVoucherAsOrderNumberAsync(StoreDbContext db, ILogger log)
    {
        const int maxOrderNumber = 30;
        var missingVoucher = db.Orders.Where(o => o.IssueVoucherNumber == null && !db.Orders.Any(x => x.IssueVoucherNumber == o.OrderNumber));
        var toRenumber = db.Orders.Where(o => o.IssueVoucherNumber != null && o.IssueVoucherNumber.Length <= maxOrderNumber
            && o.OrderNumber != o.IssueVoucherNumber);
        if (!await missingVoucher.AnyAsync() && !await toRenumber.AnyAsync()) return;

        if (db.Database.IsSqlite()) await BackupSqliteAsync(db, db.Database.GetDbConnection().DataSource, "before-voucher-numbers", log);
        await missingVoucher.ExecuteUpdateAsync(u => u.SetProperty(o => o.IssueVoucherNumber, o => o.OrderNumber));
        var changed = await toRenumber.ExecuteUpdateAsync(u => u.SetProperty(o => o.OrderNumber, o => o.IssueVoucherNumber!));
        log.LogWarning("Orders are now numbered by their issue voucher ({Count} orders renumbered).", changed);

        var tooLong = await db.Orders.CountAsync(o => o.IssueVoucherNumber != null && o.IssueVoucherNumber.Length > maxOrderNumber);
        if (tooLong > 0) log.LogWarning("{Count} orders have an issue voucher longer than {Max} characters and keep their old order number.", tooLong, maxOrderNumber);
    }

    private static async Task CreateFirstAdminAsync(IServiceProvider services, StoreDbContext db, ILogger log)
    {
        var seed = services.GetRequiredService<IConfiguration>().GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();
        var admin = new User
        {
            FullName = seed.AdminName,
            Username = seed.AdminUsername,
            NormalizedUsername = seed.AdminUsername.ToUpperInvariant(),
            IsAdmin = true,
            Permissions = Permission.All,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
        };
        admin.PasswordHash = services.GetRequiredService<PasswordService>().Hash(admin, seed.AdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        log.LogWarning("Created first admin '{Username}'. Sign in and change the password.", seed.AdminUsername);
    }
}
