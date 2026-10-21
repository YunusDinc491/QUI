using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SenBilirsin.Modules.Store.Domain;

namespace SenBilirsin.Modules.Store.Data;

/// <summary>Mağaza modülünün veritabanı bağlamı (şema "store").</summary>
public class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public const string Schema = "store";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<CouponCode> Coupons => Set<CouponCode>();
    public DbSet<UserReward> Rewards => Set<UserReward>();
    public DbSet<UserItem> Items => Set<UserItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("store_products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.JokerType).HasConversion<int?>();
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.ImageUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<CouponCode>(e =>
        {
            e.ToTable("coupon_codes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.CodeEncrypted).HasMaxLength(600).IsRequired();
            e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ReservedForKey).HasMaxLength(160);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.ProductId, x.CodeHash }).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.Status });
        });

        modelBuilder.Entity<UserReward>(e =>
        {
            e.ToTable("user_rewards");
            e.HasKey(x => x.Id);
            e.Property(x => x.PurchaseKey).HasMaxLength(160).IsRequired();
            e.Property(x => x.IssueStatus).HasConversion<int>();
            e.Property(x => x.IssueText).HasMaxLength(500);
            e.Property(x => x.IssueScreenshotUrl).HasMaxLength(512);
            e.HasIndex(x => x.PurchaseKey).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.IssueStatus);
        });

        modelBuilder.Entity<UserItem>(e =>
        {
            e.ToTable("user_items");
            e.HasKey(x => new { x.UserId, x.ProductId });
        });
    }
}

/// <summary>Yalnızca tasarım zamanı (dotnet ef migrations ...); gerçek veritabanına bağlanmaz.</summary>
public class StoreDbContextFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    public StoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only", StoreModule.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new StoreDbContext(options);
    }
}
