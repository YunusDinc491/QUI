using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Application;

namespace SenBilirsin.Modules.MatchEngine.Versus;

/// <summary>Rakipli maç kurar: iki oyuncunun görmediği sorulardan 4/3/3 seçer, kaydı yazar, koşucuyu oluşturur.</summary>
public sealed class VersusMatchFactory(
    IServiceScopeFactory scopes,
    IMatchNotifier notifier,
    TimeProvider clock,
    Random random,
    MatchSettings match,
    VersusSettings versus,
    ILoggerFactory loggers)
{
    public async Task<(VersusMatchRunner? Runner, string? ErrorCode)> CreateAsync(
        MatchKind kind, string modeCode, int? categoryId, IReadOnlyCollection<int> categoryIds,
        VersusPlayerSpec a, VersusPlayerSpec b, Action<VersusMatchRunner> onEnded)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bank = scope.ServiceProvider.GetRequiredService<IQuestionBank>();
        var humans = new[] { a, b }.Where(p => !p.IsBot).Select(p => p.Id).ToList();

        var picked = await bank.PickForUsersAsync(humans, categoryIds, MatchPlan.PickPlan(match));
        if (picked.PoolTooSmall) return (null, "NOT_ENOUGH_QUESTIONS");

        var orders = picked.Questions.Select(_ => OptionShuffler.Shuffle(random)).ToList();
        var spec = new VersusMatchSpec(Guid.NewGuid(), kind, modeCode, categoryId, a, b, picked.Questions, picked.SeenFilterRelaxed);
        await scope.ServiceProvider.GetRequiredService<VersusStore>().CreateMatchAsync(spec, orders);

        var runner = new VersusMatchRunner(spec, orders, scopes, notifier, clock, random, match, versus, loggers.CreateLogger<VersusMatchRunner>(), onEnded);
        return (runner, null);
    }
}
