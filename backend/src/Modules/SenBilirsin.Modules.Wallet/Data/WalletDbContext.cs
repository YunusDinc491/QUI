using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Wallet.Data;

/// <summary>Cüzdan modülünün veritabanı bağlamı (şema "wallet"). Faz 3: joker defteri; hediye puanı defteri Faz 5'te eklenir.</summary>
public class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public const string Schema = "wallet";

    public DbSet<JokerLedgerEntry> JokerLedger => Set<JokerLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<JokerLedgerEntry>(e =>
        {
            e.ToTable("joker_ledger");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(16).IsRequired();
            e.Property(x => x.RefId).HasMaxLength(64);
            e.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.UserId, x.JokerType });
        });
    }
}
