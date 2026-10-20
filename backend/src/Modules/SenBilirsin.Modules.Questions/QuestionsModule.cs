using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Data;

namespace SenBilirsin.Modules.Questions;

/// <summary>Soru modülünün kayıt noktası. Kategori doğrulaması için Catalog modülü de kayıtlı olmalıdır.</summary>
public static class QuestionsModule
{
    public static IServiceCollection AddQuestionsModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<QuestionsDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<QuestionService>();
        services.AddScoped<QuestionImportService>();
        services.TryAddSingleton(Random.Shared);
        services.AddScoped<QuestionBank>();
        services.AddScoped<IQuestionBank>(sp => sp.GetRequiredService<QuestionBank>());
        return services;
    }

    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", QuestionsDbContext.Schema);

    public static async Task MigrateQuestionsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<QuestionsDbContext>().Database.MigrateAsync(ct);
    }
}
