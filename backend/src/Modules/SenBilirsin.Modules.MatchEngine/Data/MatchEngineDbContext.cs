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
    public DbSet<BotProfile> Bots => Set<BotProfile>();

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

        modelBuilder.Entity<BotProfile>(e =>
        {
            e.ToTable("bot_profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(32).IsRequired();
            // Başlangıç botları (açıkça "Bot" etiketiyle gösterilir; panelden yenileri eklenir)
            e.HasData(
                new BotProfile { Id = new Guid("b0000000-0000-0000-0000-000000000001"), Name = "Çaylak Bot", AvatarId = 1, Power = 1, SpeedMinMs = 5000, SpeedMaxMs = 12000 },
                new BotProfile { Id = new Guid("b0000000-0000-0000-0000-000000000002"), Name = "Dengeli Bot", AvatarId = 2, Power = 2, SpeedMinMs = 4000, SpeedMaxMs = 10000 },
                new BotProfile { Id = new Guid("b0000000-0000-0000-0000-000000000003"), Name = "Usta Bot", AvatarId = 3, Power = 3, SpeedMinMs = 3000, SpeedMaxMs = 8000 });
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
