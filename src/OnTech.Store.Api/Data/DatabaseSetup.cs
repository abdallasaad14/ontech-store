using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Data;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public string AdminEmail { get; set; } = "admin@ontech.local";
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

    /// <summary>Creates or upgrades the database on startup and makes sure an admin exists.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

        if (db.Database.IsSqlServer()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync(); // SQLite is used for tests and quick local trials.

        if (await db.Users.AnyAsync(u => u.IsAdmin)) return;

        var seed = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();
        var admin = new User
        {
            FullName = seed.AdminName,
            Email = seed.AdminEmail,
            NormalizedEmail = seed.AdminEmail.ToUpperInvariant(),
            IsAdmin = true,
            Permissions = Permission.All,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
        };
        admin.PasswordHash = scope.ServiceProvider.GetRequiredService<PasswordService>().Hash(admin, seed.AdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        log.LogWarning("Created first admin {Email}. Sign in and change the password.", seed.AdminEmail);
    }
}
