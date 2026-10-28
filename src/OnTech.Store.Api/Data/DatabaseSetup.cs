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
    private static async Task UpgradeSqliteAsync(StoreDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Customers') WHERE name = 'CustomerNumber'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0) return;
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Customers ADD COLUMN CustomerNumber INTEGER NULL");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Customers_CustomerNumber ON Customers (CustomerNumber)");
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
            await UpgradeSqliteAsync(db);
        }

        if (await db.Users.AnyAsync(u => u.IsAdmin)) return;

        var seed = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();
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
        admin.PasswordHash = scope.ServiceProvider.GetRequiredService<PasswordService>().Hash(admin, seed.AdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        log.LogWarning("Created first admin '{Username}'. Sign in and change the password.", seed.AdminUsername);
    }
}
