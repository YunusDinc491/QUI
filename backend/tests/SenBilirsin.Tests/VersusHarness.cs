using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;
using SenBilirsin.Modules.MatchEngine;
using SenBilirsin.Modules.MatchEngine.Data;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.MatchEngine.Versus;
using SenBilirsin.Modules.Questions.Data;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Tests;

/// <summary>Elle ilerletilen saat: zamanlayıcılar yalnızca <see cref="AdvanceAsync"/> ile tetiklenir (gerçek beklemeden süre testi).</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private readonly List<ManualTimer> _timers = [];

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    /// <summary>Saati ilerletir; vadesi gelen zamanlayıcıları sırayla tetikler ve her birinden sonra <paramref name="idle"/> bekler.</summary>
    public async Task AdvanceAsync(TimeSpan by, Func<Task> idle)
    {
        var target = _now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_timers) next = _timers.Where(t => t.DueAt is { } d && d <= target).OrderBy(t => t.DueAt).FirstOrDefault();
            if (next is null) break;
            if (next.DueAt!.Value > _now) _now = next.DueAt.Value;
            next.Fire();
            await idle();
        }

        _now = target;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
            _period = period;
            return true;
        }

        public void Fire()
        {
            DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : DueAt + _period;
            callback(state);
        }

        public void Dispose() => DueAt = null;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

/// <summary>Gönderilen olayları kaydeder (JSON'a çevrilmiş biçimde, istemcinin göreceği gibi).</summary>
public sealed class RecordingNotifier : IMatchNotifier
{
    public sealed record Event(Guid User, string Name, JsonElement Payload);

    private readonly List<Event> _events = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task SendAsync(Guid userId, string eventName, object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload, Json);
        lock (_events) _events.Add(new Event(userId, eventName, element));
        return Task.CompletedTask;
    }

    public IReadOnlyList<Event> For(Guid user, string? name = null)
    {
        lock (_events) return _events.Where(e => e.User == user && (name is null || e.Name == name)).ToList();
    }
}

/// <summary>Rakipli maç koşucusu testleri için ortak kurulum: kategori+sorular, iki oyuncu (ya da bot), sahte saat, kayıt tutan bildirici.</summary>
public sealed class VersusHarness
{
    public required ManualClock Clock { get; init; }
    public required RecordingNotifier Notifier { get; init; }
    public required VersusMatchRunner Runner { get; init; }
    public required Guid A { get; init; }
    public required Guid B { get; init; }
    public required MobileApiFactory Factory { get; init; }
    public Guid MatchId => Runner.MatchId;
    public bool Ended { get; set; }

    public static async Task<int> NewCategoryAsync(MobileApiFactory factory, int easy = 14, int medium = 10, int hard = 10)
    {
        using var scope = factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var category = new Category { ModeId = catalog.Modes.Single(m => m.Code == ModeCodes.Branch).Id, Name = "V" + Guid.NewGuid().ToString("N")[..10], IsPublished = true, CreatedAt = DateTime.UtcNow };
        catalog.Categories.Add(category);
        await catalog.SaveChangesAsync();

        var questions = scope.ServiceProvider.GetRequiredService<QuestionsDbContext>();
        void Add(Difficulty d, int count)
        {
            for (var i = 0; i < count; i++)
                questions.Questions.Add(new Question
                {
                    CategoryId = category.Id, Text = $"Soru {Guid.NewGuid():N}", OptionA = "Bir", OptionB = "İki", OptionC = "Üç", OptionD = "Dört",
                    CorrectIndex = i % 4, Difficulty = d, Status = QuestionStatus.Published, NormalizedText = Guid.NewGuid().ToString("N"),
                    Explanation = "Açıklama", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
        }

        Add(Difficulty.Easy, easy);
        Add(Difficulty.Medium, medium);
        Add(Difficulty.Hard, hard);
        await questions.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>B oyuncusu <paramref name="bot"/> verilirse botlu maç kurulur.</summary>
    public static async Task<VersusHarness> CreateAsync(MobileApiFactory factory, BotProfile? bot = null, int? categoryId = null)
    {
        categoryId ??= await NewCategoryAsync(factory);
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var notifier = new RecordingNotifier();
        var a = new VersusPlayerSpec(Guid.NewGuid(), "Ali", 1, null, false, null);
        var b = bot is null
            ? new VersusPlayerSpec(Guid.NewGuid(), "Veli", 2, null, false, null)
            : new VersusPlayerSpec(bot.Id, bot.Name, bot.AvatarId, null, true, bot);

        var matchFactory = new VersusMatchFactory(factory.Services.GetRequiredService<IServiceScopeFactory>(), notifier, clock, new Random(21),
            new MatchSettings(), new VersusSettings(), NullLoggerFactory.Instance);
        VersusHarness? harness = null;
        var (runner, error) = await matchFactory.CreateAsync(bot is null ? MatchKind.Random : MatchKind.Bot, ModeCodes.Branch, categoryId, [categoryId.Value], a, b, _ => harness!.Ended = true);
        Assert.Null(error);

        harness = new VersusHarness { Runner = runner!, A = a.Id, B = b.Id, Factory = factory, Clock = clock, Notifier = notifier };
        return harness;
    }

    /// <summary>Koşul sağlanana (ya da maç bitene) kadar saati 0,25 sn'lik adımlarla ilerletir.</summary>
    public async Task UntilAsync(Func<bool> condition, double maxSeconds = 40)
    {
        for (var waited = 0.0; waited < maxSeconds && !condition() && !Ended; waited += 0.25) await AdvanceAsync(0.25);
    }

    public Task AdvanceAsync(double seconds) => Clock.AdvanceAsync(TimeSpan.FromSeconds(seconds), Runner.WhenIdleAsync);

    public async Task<int> CorrectDisplayAsync(int position)
    {
        using var scope = Factory.Services.CreateScope();
        var matches = scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>();
        var mq = await matches.Questions.AsNoTracking().SingleAsync(q => q.MatchId == MatchId && q.Position == position && q.Attempt == 0);
        var q = await scope.ServiceProvider.GetRequiredService<QuestionsDbContext>().Questions.AsNoTracking().SingleAsync(x => x.Id == mq.QuestionId);
        return OptionShuffler.ToDisplay(OptionShuffler.Parse(mq.OptionOrder), q.CorrectIndex);
    }

    public async Task AnswerAsync(Guid who, int position, bool correct)
    {
        var right = await CorrectDisplayAsync(position);
        var result = await Runner.SubmitAnswerAsync(who, position, correct ? right : (right + 1) % 4, null);
        Assert.True(result.IsSuccess, result.Error?.Code);
        await Runner.WhenIdleAsync();
    }

    /// <summary>Verilen sıradaki soruyu iki oyuncu da (belirtilen doğruluk ve gecikmeyle) cevaplar, sonuç ekranı + sıradaki soruya kadar ilerler.</summary>
    public async Task PlayRoundAsync(int position, bool aCorrect, bool bCorrect)
    {
        await AnswerAsync(A, position, aCorrect);
        await AdvanceAsync(1);
        await AnswerAsync(B, position, bCorrect);
        await AdvanceAsync(1.01); // 1 sn gösterim gecikmesi → sonuç
        await AdvanceAsync(3.01); // sonuç ekranı → sıradaki soru (ya da bitiş)
    }

    public async Task<(Guid Id, string? Result, int Correct, int Points, int Bonus, bool IsBot, int Pool)[]> PlayersInDbAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Players.AsNoTracking().Where(p => p.MatchId == MatchId).ToListAsync();
        return rows.Select(r => (r.UserId, r.Result, r.CorrectCount, r.Points, r.Bonus, r.IsBot, r.DisconnectMsUsed)).ToArray();
    }

    public async Task<MatchStatus> StatusInDbAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Matches.AsNoTracking().SingleAsync(m => m.Id == MatchId)).Status;
    }
}
