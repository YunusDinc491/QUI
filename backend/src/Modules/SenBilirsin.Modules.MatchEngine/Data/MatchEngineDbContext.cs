using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SenBilirsin.Modules.MatchEngine.Domain;

namespace SenBilirsin.Modules.MatchEngine.Data;

/// <summary>Maç motorunun veritabanı bağlamı (şema "matches").</summary>
public class MatchEngineDbContext(DbContextOptions<MatchEngineDbContext> options) : DbContext(options)
{
    public const string Schema = "matches";

    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> Players => Set<MatchPlayer>();
    public DbSet<MatchQuestion> Questions => Set<MatchQuestion>();
    public DbSet<MatchAnswer> Answers => Set<MatchAnswer>();
    public DbSet<MatchJoker> Jokers => Set<MatchJoker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Match>(e =>
        {
            e.ToTable("matches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.ModeCode).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<MatchPlayer>(e =>
        {
            e.ToTable("match_players");
            e.HasKey(x => new { x.MatchId, x.UserId });
            e.Property(x => x.Result).HasMaxLength(16);
            e.HasIndex(x => x.UserId);
            // Faz 5: kesinleşmiş puanları dönemlere işlemek için
            e.HasIndex(x => x.SettledAt);
        });

        modelBuilder.Entity<MatchQuestion>(e =>
        {
            e.ToTable("match_questions");
            e.HasKey(x => new { x.MatchId, x.Position, x.Attempt });
            e.Property(x => x.Slot).HasConversion<int>();
            e.Property(x => x.OptionOrder).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<MatchAnswer>(e =>
        {
            e.ToTable("match_answers");
            // Bir oyuncu bir sıradaki soruya yalnızca bir cevap verir (ilk dokunuş kesin)
            e.HasKey(x => new { x.MatchId, x.UserId, x.Position, x.Attempt });
        });

        modelBuilder.Entity<MatchJoker>(e =>
        {
            e.ToTable("match_jokers");
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.ResultJson).HasMaxLength(200).IsRequired();
            // Bir maçta her joker türü bir kez
            e.HasKey(x => new { x.MatchId, x.UserId, x.Type });
            // Bir soruda en çok bir joker
            e.HasIndex(x => new { x.MatchId, x.UserId, x.Position, x.Attempt }).IsUnique();
        });
    }
}

/// <summary>Yalnızca tasarım zamanı (dotnet ef migrations ...); gerçek veritabanına bağlanmaz.</summary>
public class MatchEngineDbContextFactory : IDesignTimeDbContextFactory<MatchEngineDbContext>
{
    public MatchEngineDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MatchEngineDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only", MatchEngineModule.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MatchEngineDbContext(options);
    }
}
