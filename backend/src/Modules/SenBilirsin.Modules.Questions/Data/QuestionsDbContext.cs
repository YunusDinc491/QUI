using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Modules.Questions.Data;

/// <summary>Soru modülünün veritabanı bağlamı (şema "questions").</summary>
public class QuestionsDbContext(DbContextOptions<QuestionsDbContext> options) : DbContext(options)
{
    public const string Schema = "questions";

    public DbSet<Question> Questions => Set<Question>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Question>(e =>
        {
            e.ToTable("questions");
            e.HasKey(x => x.Id);
            e.Ignore(x => x.Options);
            e.Property(x => x.Text).HasMaxLength(QuestionRules.TextMax).IsRequired();
            e.Property(x => x.OptionA).HasMaxLength(QuestionRules.OptionMax).IsRequired();
            e.Property(x => x.OptionB).HasMaxLength(QuestionRules.OptionMax).IsRequired();
            e.Property(x => x.OptionC).HasMaxLength(QuestionRules.OptionMax).IsRequired();
            e.Property(x => x.OptionD).HasMaxLength(QuestionRules.OptionMax).IsRequired();
            e.Property(x => x.Explanation).HasMaxLength(QuestionRules.ExplanationMax);
            e.Property(x => x.NormalizedText).HasMaxLength(QuestionRules.TextMax + 50).IsRequired();
            e.Property(x => x.Difficulty).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            // Oyun soru seçimi: kategori + yayın durumu + zorluk
            e.HasIndex(x => new { x.CategoryId, x.Status, x.Difficulty });
            e.HasIndex(x => x.NormalizedText);
        });
    }
}

/// <summary>Yalnızca tasarım zamanı (dotnet ef migrations ...); gerçek veritabanına bağlanmaz.</summary>
public class QuestionsDbContextFactory : IDesignTimeDbContextFactory<QuestionsDbContext>
{
    public QuestionsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuestionsDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only", QuestionsModule.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new QuestionsDbContext(options);
    }
}
