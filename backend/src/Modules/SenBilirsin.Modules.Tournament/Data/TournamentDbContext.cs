using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SenBilirsin.Modules.Tournament.Domain;

namespace SenBilirsin.Modules.Tournament.Data;

/// <summary>Turnuva modülünün veritabanı bağlamı (şema "tournament").</summary>
public class TournamentDbContext(DbContextOptions<TournamentDbContext> options) : DbContext(options)
{
    public const string Schema = "tournament";

    public DbSet<Period> Periods => Set<Period>();
    public DbSet<PeriodScoreEntry> Entries => Set<PeriodScoreEntry>();
    public DbSet<PeriodScore> Scores => Set<PeriodScore>();
    public DbSet<RewardTier> Rewards => Set<RewardTier>();
    public DbSet<Disqualification> Disqualifications => Set<Disqualification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Period>(e =>
        {
            e.ToTable("tournament_periods");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => x.StartsAt).IsUnique();
        });

        modelBuilder.Entity<PeriodScoreEntry>(e =>
        {
            e.ToTable("period_score_entries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchId, x.UserId }).IsUnique();
            e.HasIndex(x => x.TotalApplied);
        });

        modelBuilder.Entity<PeriodScore>(e =>
        {
            e.ToTable("period_scores");
            e.HasKey(x => new { x.PeriodId, x.UserId });
            e.HasIndex(x => new { x.PeriodId, x.Points });
        });

        modelBuilder.Entity<RewardTier>(e =>
        {
            e.ToTable("period_reward_table");
            e.HasKey(x => x.Id);
            // Taslak ödül tablosu (docs/07 §2, kesin değil): panelden değiştirilir
            e.HasData(
                new RewardTier { Id = 1, RankFrom = 1, RankTo = 1, Amount = 100 },
                new RewardTier { Id = 2, RankFrom = 2, RankTo = 2, Amount = 80 },
                new RewardTier { Id = 3, RankFrom = 3, RankTo = 3, Amount = 60 },
                new RewardTier { Id = 4, RankFrom = 4, RankTo = 4, Amount = 50 },
                new RewardTier { Id = 5, RankFrom = 5, RankTo = 5, Amount = 40 },
                new RewardTier { Id = 6, RankFrom = 6, RankTo = 6, Amount = 30 },
                new RewardTier { Id = 7, RankFrom = 7, RankTo = 7, Amount = 25 },
                new RewardTier { Id = 8, RankFrom = 8, RankTo = 8, Amount = 20 },
                new RewardTier { Id = 9, RankFrom = 9, RankTo = 9, Amount = 15 },
                new RewardTier { Id = 10, RankFrom = 10, RankTo = 10, Amount = 10 });
        });

        modelBuilder.Entity<Disqualification>(e =>
        {
            e.ToTable("disqualifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.PeriodId, x.UserId }).IsUnique();
        });
    }
}

/// <summary>Yalnızca tasarım zamanı (dotnet ef migrations ...); gerçek veritabanına bağlanmaz.</summary>
public class TournamentDbContextFactory : IDesignTimeDbContextFactory<TournamentDbContext>
{
    public TournamentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TournamentDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only", TournamentModule.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new TournamentDbContext(options);
    }
}
