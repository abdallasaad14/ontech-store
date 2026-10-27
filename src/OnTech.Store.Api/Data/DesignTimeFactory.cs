using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OnTech.Store.Api.Data;

/// <summary>Used by "dotnet ef migrations add" so migrations are always generated for SQL Server.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    public StoreDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlServer("Server=.\\SQLEXPRESS;Database=OnTechStore;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
}
