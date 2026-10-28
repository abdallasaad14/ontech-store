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

        if (!await db.Users.AnyAsync(u => u.IsAdmin)) await CreateFirstAdminAsync(scope.ServiceProvider, db, log);
        await OpeningData.ApplyAsync(db, log);
        await CatalogCodes.MakeNumericAsync(db);
        await CatalogCodes.BackfillAsync(db);
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
