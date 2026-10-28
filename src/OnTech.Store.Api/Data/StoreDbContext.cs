using Microsoft.EntityFrameworkCore;

namespace OnTech.Store.Api.Data;

public class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<AppliedSeed> AppliedSeeds => Set<AppliedSeed>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppliedSeed>(e =>
        {
            e.HasKey(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        b.Entity<User>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.NormalizedUsername).HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.HasIndex(x => x.NormalizedUsername).IsUnique();
            e.Ignore(x => x.EffectivePermissions);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(10);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Item>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.CategoryId, x.Name }).IsUnique();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Category).WithMany(c => c.Items).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Items_Quantity_NonNegative", "[Quantity] >= 0");
                t.HasCheckConstraint("CK_Items_LowStockThreshold_NonNegative", "[LowStockThreshold] >= 0");
            });
        });

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(50);
            e.Property(x => x.TaxNumber).HasMaxLength(50);
            e.Property(x => x.ContactPerson).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.CompanyName);
            e.HasIndex(x => x.TaxNumber);
            e.HasIndex(x => x.CustomerNumber).IsUnique();
        });

        b.Entity<Order>(e =>
        {
            e.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.ReceiptNumber).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.HasIndex(x => x.OrderNumber);
            e.HasIndex(x => x.ReceiptNumber).IsUnique().HasFilter("[ReceiptNumber] IS NOT NULL");
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            e.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt });
            e.HasOne(x => x.Customer).WithMany(c => c.Orders).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CancelledByUser).WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderLine>(e =>
        {
            e.Property(x => x.ItemName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ItemCode).HasMaxLength(64);
            e.Property(x => x.CategoryName).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.ItemId);
            e.HasIndex(x => x.CategoryId);
            e.HasOne(x => x.Order).WithMany(o => o.Lines).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_OrderLines_Quantity_Positive", "[Quantity] > 0"));
        });

        b.Entity<StockMovement>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.ItemId, x.CreatedAt });
            e.HasIndex(x => x.CreatedAt);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
