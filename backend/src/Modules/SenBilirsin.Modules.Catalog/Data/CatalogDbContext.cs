using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Catalog.Domain;

namespace SenBilirsin.Modules.Catalog.Data;

/// <summary>Katalog modülünün veritabanı bağlamı (şema "catalog").</summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    /// <summary>Tohumlanan "Genel" kategorisinin kimliği. Yeni kategoriler 100'den başlar (çakışma olmasın).</summary>
    public const int GeneralCategoryId = 1;

    public DbSet<Mode> Modes => Set<Mode>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Mode>(e =>
        {
            e.ToTable("modes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(16).IsRequired();
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasData(
                new Mode { Id = 1, Code = ModeCodes.General, Name = "Genel sorular", Order = 1 },
                new Mode { Id = 2, Code = ModeCodes.Branch, Name = "Branş seçmeli", Order = 2 },
                new Mode { Id = 3, Code = ModeCodes.Kpss, Name = "KPSS tarzı", Order = 3 },
                new Mode { Id = 4, Code = ModeCodes.Fan, Name = "Fanlara özel", Order = 4 });
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn().HasIdentityOptions(startValue: 100);
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.ImageUrl).HasMaxLength(512);
            e.Property(x => x.ColorHex).HasMaxLength(9);
            // Aynı modda aynı ad iki kez olmasın
            e.HasIndex(x => new { x.ModeId, x.Name }).IsUnique();
            e.HasOne<Mode>().WithMany().HasForeignKey(x => x.ModeId);
            // Genel modunun tek örtük kategorisi; admin soru ekleyip yayınlayana kadar gizli
            e.HasData(new Category
            {
                Id = GeneralCategoryId, ModeId = 1, Name = "Genel", Order = 1, IsPublished = false,
                CreatedAt = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
            });
        });
    }
}
