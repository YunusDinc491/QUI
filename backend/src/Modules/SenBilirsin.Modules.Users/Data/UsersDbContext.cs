using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Modules.Users.Data;

/// <summary>Users modülünün veritabanı bağlamı. Tablolar "users" şemasındadır (modüller birbirinin tablosuna dokunmaz).</summary>
public class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    public const string Schema = "users";

    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<Avatar> Avatars => Set<Avatar>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.FirebaseUid).HasMaxLength(128).IsRequired();
            e.Property(x => x.Username).HasMaxLength(ProfileRules.UsernameMaxLength).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(ProfileRules.DisplayNameMaxLength).IsRequired();
            e.Property(x => x.TermsVersion).HasMaxLength(32).IsRequired();
            e.Property(x => x.ActiveDeviceId).HasMaxLength(128);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => x.FirebaseUid).IsUnique();
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<Avatar>(e =>
        {
            e.ToTable("avatars");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(9).IsRequired();

            // 5 ücretsiz başlangıç avatarı (K-34). Gerçek çizimler tasarım aşamasında yüklenecek; şimdilik renkli yer tutucu.
            e.HasData(
                new Avatar { Id = 1, Name = "Avatar 1", ColorHex = "#E57373", Price = 0, IsActive = true },
                new Avatar { Id = 2, Name = "Avatar 2", ColorHex = "#64B5F6", Price = 0, IsActive = true },
                new Avatar { Id = 3, Name = "Avatar 3", ColorHex = "#81C784", Price = 0, IsActive = true },
                new Avatar { Id = 4, Name = "Avatar 4", ColorHex = "#FFB74D", Price = 0, IsActive = true },
                new Avatar { Id = 5, Name = "Avatar 5", ColorHex = "#BA68C8", Price = 0, IsActive = true });
        });
    }
}
