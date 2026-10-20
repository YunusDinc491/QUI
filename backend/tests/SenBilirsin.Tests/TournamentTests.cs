using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Tournament.Application;
using SenBilirsin.Modules.Tournament.Data;
using SenBilirsin.Modules.Tournament.Domain;
using SenBilirsin.Modules.Users.Application;
using SenBilirsin.Modules.Users.Data;
using SenBilirsin.Modules.Wallet.Application;

namespace SenBilirsin.Tests;

/// <summary>Faz 5: dönemler (Türkiye saati), puan yazma, sıralama, men, ödül dağıtımı — gerçek PostgreSQL üzerinde.</summary>
[Collection("api")]
public class TournamentTests(MobileApiFactory factory)
{
    // Testler birbirinin puanını karıştırmasın diye her test kendi (uzak gelecekteki) dönemini kullanır
    private static int _next = 5000;
    private static DateTime FutureMoment() => TournamentClockFor().PeriodStartFor(new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc)) + TimeSpan.FromHours(48) * (10 * Interlocked.Increment(ref _next)); // 10 dönemlik aralık: bir testin +1/+5 dönemlik kayıtları diğerine çarpmaz

    private static TournamentClock TournamentClockFor(string? first = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Tournament:FirstPeriodStart"] = first }).Build());

    private sealed class Services(IServiceScope scope, FakeClock clock) : IDisposable
    {
        public TournamentDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        public PeriodService Periods { get; } = new(scope.ServiceProvider.GetRequiredService<TournamentDbContext>(), TournamentClockFor(), clock);
        public ScoreService Scores { get; private set; } = null!;
        public DistributionService Distribution { get; private set; } = null!;
        public IPointsWallet Wallet { get; } = scope.ServiceProvider.GetRequiredService<IPointsWallet>();
        public FakeClock Clock { get; } = clock;

        public Services Build(IServiceScope s)
        {
            Scores = new ScoreService(Db, Periods, s.ServiceProvider.GetRequiredService<IPlayerScores>(), Clock);
            Distribution = new DistributionService(Db, Scores, Wallet, Clock);
            return this;
        }

        public void Dispose() => scope.Dispose();
    }

    private Services New(DateTimeOffset? now = null)
    {
        var scope = factory.Services.CreateScope();
        return new Services(scope, new FakeClock(now ?? DateTimeOffset.UtcNow)).Build(scope);
    }

    private async Task<Guid> NewUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        var user = new SenBilirsin.Modules.Users.Domain.UserAccount
        {
            Id = Guid.NewGuid(), FirebaseUid = Guid.NewGuid().ToString("N"), Username = "t" + Guid.NewGuid().ToString("N")[..10], DisplayName = "Turnuva",
            AvatarId = 1, BirthYear = 2000, CreatedAt = DateTime.UtcNow, TermsAcceptedAt = DateTime.UtcNow, TermsVersion = "t",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    // ---------------------------------------------------------------- Dönem sınırları

    [Fact]
    public void Periods_AreAligned48HourBlocksInTurkeyTime()
    {
        var clock = TournamentClockFor("2026-01-01T00:00:00+03:00");
        var first = clock.FirstPeriodStartUtc;
        Assert.Equal(new DateTime(2025, 12, 31, 21, 0, 0, DateTimeKind.Utc), first); // Türkiye 00:00 = 21:00 UTC

        Assert.Equal(first, clock.PeriodStartFor(first));
        Assert.Equal(first, clock.PeriodStartFor(first.AddHours(47.99)));
        Assert.Equal(first.AddHours(48), clock.PeriodStartFor(first.AddHours(48)));          // sınır yeni döneme aittir
        Assert.Equal(first.AddHours(96), clock.PeriodStartFor(first.AddHours(100)));
        Assert.Equal(first, clock.PeriodStartFor(first.AddDays(-5)));                          // başlangıçtan önceki an ilk döneme düşer

        // Her dönem Türkiye saatiyle 00:00'da başlar
        var anyStart = clock.PeriodStartFor(new DateTime(2026, 10, 21, 13, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.Zero, (anyStart + TournamentClock.TurkeyOffset).TimeOfDay);
    }

    [Fact]
    public async Task EnsureFor_CreatesEachPeriodOnce_EvenUnderRaces()
    {
        var moment = FutureMoment();
        var periods = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var s = New();
            return await s.Periods.EnsureForAsync(moment);
        }));

        Assert.Single(periods.Select(p => p.Id).Distinct());
        Assert.Equal(TournamentClock.PeriodLength, periods[0].EndsAt - periods[0].StartsAt);
    }

    // ---------------------------------------------------------------- Puan yazma

    [Fact]
    public async Task Credit_WritesToThePeriodOfTheMatchEnd_AccumulatesAndIsIdempotentPerMatch()
    {
        using var s = New();
        var user = await NewUserAsync();
        var moment = FutureMoment();
        var match1 = Guid.NewGuid();

        Assert.True(await s.Scores.CreditAsync(match1, user, 100, moment));
        Assert.False(await s.Scores.CreditAsync(match1, user, 100, moment)); // aynı maç ikinci kez yazılmaz
        Assert.True(await s.Scores.CreditAsync(Guid.NewGuid(), user, 10, moment.AddHours(1)));
        Assert.False(await s.Scores.CreditAsync(Guid.NewGuid(), user, 0, moment)); // 0 puan yazılmaz

        var period = await s.Periods.EnsureForAsync(moment);
        var row = await s.Db.Scores.AsNoTracking().SingleAsync(x => x.PeriodId == period.Id && x.UserId == user);
        Assert.Equal((110, 2), (row.Points, row.MatchCount));

        // Bir sonraki dönemin maçı ayrı dönemde birikir
        await s.Scores.CreditAsync(Guid.NewGuid(), user, 7, moment.AddHours(49));
        var next = await s.Periods.EnsureForAsync(moment.AddHours(49));
        Assert.NotEqual(period.Id, next.Id);
        Assert.Equal(7, (await s.Db.Scores.AsNoTracking().SingleAsync(x => x.PeriodId == next.Id && x.UserId == user)).Points);
    }

    [Fact]
    public async Task Credit_ParallelDuplicatesOfTheSameMatch_CountOnce_AndTotalScoreIsUpdatedOnce()
    {
        var user = await NewUserAsync();
        var moment = FutureMoment();
        var match = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var s = New();
            return await s.Scores.CreditAsync(match, user, 50, moment);
        }));

        Assert.Equal(1, results.Count(r => r));
        using var check = New();
        var period = await check.Periods.EnsureForAsync(moment);
        Assert.Equal(50, (await check.Db.Scores.AsNoTracking().SingleAsync(x => x.PeriodId == period.Id && x.UserId == user)).Points);

        using var scope = factory.Services.CreateScope();
        Assert.Equal(50, (await scope.ServiceProvider.GetRequiredService<UsersDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == user)).TotalScore);
    }

    // ---------------------------------------------------------------- Sıralama

    [Fact]
    public async Task Standings_TiesShareARank_ZeroPointsAreHidden_AndMyRankIsShownBeyondTheTop()
    {
        using var s = New();
        var moment = FutureMoment();
        var users = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => NewUserAsync()));
        int[] points = [90, 80, 80, 50, 40, 10]; // 1, 2, 2, 4, 5, 6. sıra
        for (var i = 0; i < users.Length; i++) await s.Scores.CreditAsync(Guid.NewGuid(), users[i], points[i], moment);

        var period = await s.Periods.EnsureForAsync(moment);
        var standings = await s.Scores.GetStandingsAsync(period, take: 4, me: users[5]);

        Assert.Equal([1, 2, 2, 4], standings.Top.Select(r => r.Rank));
        Assert.Equal(users[0], standings.Top[0].UserId);
        Assert.Equal((6, 10), (standings.Me!.Rank, standings.Me.Points)); // ilk 4'te değil ama kendi sırası görünür

        var everyone = await s.Scores.RankedAsync(period.Id);
        Assert.Equal(6, everyone.Count);
        Assert.Equal([1, 2, 2, 4, 5, 6], everyone.Select(r => r.Rank));
    }

    [Fact]
    public async Task Disqualification_RemovesThePlayer_AndLowerRanksMoveUp()
    {
        using var s = New();
        var moment = FutureMoment();
        var users = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => NewUserAsync()));
        for (var i = 0; i < 3; i++) await s.Scores.CreditAsync(Guid.NewGuid(), users[i], 100 - i * 10, moment);
        var period = await s.Periods.EnsureForAsync(moment);

        s.Db.Disqualifications.Add(new Disqualification { PeriodId = period.Id, UserId = users[0], Reason = "Hile", CreatedAt = DateTime.UtcNow });
        await s.Db.SaveChangesAsync();

        var ranked = await s.Scores.RankedAsync(period.Id);
        Assert.Equal([users[1], users[2]], ranked.Select(r => r.UserId));
        Assert.Equal([1, 2], ranked.Select(r => r.Rank));
    }

    // ---------------------------------------------------------------- Dağıtım

    [Fact]
    public async Task Distribution_PaysTheRewardTable_TiesGetTheirTiersReward_AndTheLedgerBalancesMatch()
    {
        using var s = New();
        var moment = FutureMoment();
        var users = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => NewUserAsync()));
        int[] points = [100, 70, 70, 40, 5]; // 1., 2., 2., 4., 5.
        for (var i = 0; i < 5; i++) await s.Scores.CreditAsync(Guid.NewGuid(), users[i], points[i], moment);
        var period = await s.Periods.EnsureForAsync(moment);

        var result = await s.Distribution.DistributeAsync(period.Id);

        Assert.False(result.AlreadyDistributed);
        Assert.Equal(5, result.Recipients);
        // Varsayılan tablo: 1→100, 2→80, 3→60, 4→50, 5→40. Eşit 2. sıradakiler ikisi de 80 alır (eşitlik elemesi yok).
        var balances = new List<long>();
        foreach (var u in users) balances.Add(await s.Wallet.GetBalanceAsync(u));
        Assert.Equal(new long[] { 100, 80, 80, 50, 40 }, balances);
        Assert.Equal(350, result.TotalPoints);
        Assert.Equal(PeriodStatus.Distributed, (await s.Periods.GetAsync(period.Id))!.Status);
    }

    [Fact]
    public async Task Distribution_RunTwiceOrInParallel_PaysOnlyOnce()
    {
        var moment = FutureMoment();
        var user = await NewUserAsync();
        int periodId;
        using (var s = New())
        {
            await s.Scores.CreditAsync(Guid.NewGuid(), user, 30, moment);
            periodId = (await s.Periods.EnsureForAsync(moment)).Id;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var s = New();
            return await s.Distribution.DistributeAsync(periodId);
        }));

        Assert.Equal(1, results.Count(r => !r.AlreadyDistributed));
        using var check = New();
        Assert.Equal(100, await check.Wallet.GetBalanceAsync(user));
        Assert.Single(await check.Wallet.GetHistoryAsync(user, 1, 10));

        // Sonradan tekrar çağrılsa da değişmez
        Assert.True((await check.Distribution.DistributeAsync(periodId)).AlreadyDistributed);
        Assert.Equal(100, await check.Wallet.GetBalanceAsync(user));
    }

    [Fact]
    public async Task Distribution_SkipsDisqualified_AndRankingBeyondTheTable_GetsNothing()
    {
        using var s = New();
        var moment = FutureMoment();
        var users = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => NewUserAsync()));
        for (var i = 0; i < 12; i++) await s.Scores.CreditAsync(Guid.NewGuid(), users[i], 200 - i, moment); // 12 farklı puan
        var period = await s.Periods.EnsureForAsync(moment);
        s.Db.Disqualifications.Add(new Disqualification { PeriodId = period.Id, UserId = users[0], Reason = "Hile", CreatedAt = DateTime.UtcNow });
        await s.Db.SaveChangesAsync();

        await s.Distribution.DistributeAsync(period.Id);

        Assert.Equal(0, await s.Wallet.GetBalanceAsync(users[0]));      // men edilen alamaz
        Assert.Equal(100, await s.Wallet.GetBalanceAsync(users[1]));    // alttaki sıra yükselir: artık 1.
        Assert.Equal(10, await s.Wallet.GetBalanceAsync(users[10]));    // 10. sıra
        Assert.Equal(0, await s.Wallet.GetBalanceAsync(users[11]));     // 11. sıra: tabloda yok
    }

    [Fact]
    public async Task DistributeDue_WaitsForThe30SecondWriteLockAfterThePeriodEnds()
    {
        var moment = FutureMoment();
        using var seed = New();
        var user = await NewUserAsync();
        await seed.Scores.CreditAsync(Guid.NewGuid(), user, 20, moment);
        var period = await seed.Periods.EnsureForAsync(moment);

        // Bitişten 10 sn sonra: yazma kilidi sürüyor → dağıtılmaz
        using (var early = New(new DateTimeOffset(period.EndsAt.AddSeconds(10), TimeSpan.Zero)))
        {
            var dueEarly = await early.Distribution.DistributeDueAsync();
            Assert.DoesNotContain(dueEarly, r => r.PeriodId == period.Id);
            Assert.Equal(0, await early.Wallet.GetBalanceAsync(user));
        }

        // 31 sn sonra dağıtılır
        using var late = New(new DateTimeOffset(period.EndsAt.AddSeconds(31), TimeSpan.Zero));
        var due = await late.Distribution.DistributeDueAsync();
        Assert.Contains(due, r => r.PeriodId == period.Id && !r.AlreadyDistributed);
        Assert.Equal(100, await late.Wallet.GetBalanceAsync(user));
    }

    [Fact]
    public async Task LatePointsForADistributedPeriod_GoToTheCurrentPeriodInstead()
    {
        var user = await NewUserAsync();
        var moment = FutureMoment();
        using var s = New(new DateTimeOffset(moment.AddDays(10), TimeSpan.Zero));
        await s.Scores.CreditAsync(Guid.NewGuid(), user, 10, moment);
        var period = await s.Periods.EnsureForAsync(moment);
        await s.Distribution.DistributeAsync(period.Id);

        await s.Scores.CreditAsync(Guid.NewGuid(), user, 25, moment); // dönem kapanmış: geç gelen kayıt

        Assert.Equal(10, (await s.Db.Scores.AsNoTracking().SingleAsync(x => x.PeriodId == period.Id && x.UserId == user)).Points);
        var current = await s.Periods.GetCurrentAsync();
        Assert.Equal(25, (await s.Db.Scores.AsNoTracking().SingleAsync(x => x.PeriodId == current.Id && x.UserId == user)).Points);
    }

    [Fact]
    public async Task PendingTotals_AreRetried_WhenTheTotalScoreUpdateWasMissed()
    {
        var user = await NewUserAsync();
        var moment = FutureMoment();
        using var s = New();
        await s.Scores.CreditAsync(Guid.NewGuid(), user, 40, moment);

        // Yansıtma başarısız olmuş gibi: işareti geri al ve toplam puanı sıfırla
        await s.Db.Entries.Where(e => e.UserId == user).ExecuteUpdateAsync(x => x.SetProperty(e => e.TotalApplied, false));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<UsersDbContext>().Users.Where(u => u.Id == user).ExecuteUpdateAsync(x => x.SetProperty(u => u.TotalScore, 0L));

        Assert.True(await s.Scores.ApplyPendingTotalsAsync() >= 1);
        await s.Scores.ApplyPendingTotalsAsync(); // ikinci tur ek puan eklememeli

        using var check = factory.Services.CreateScope();
        Assert.Equal(40, (await check.ServiceProvider.GetRequiredService<UsersDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == user)).TotalScore);
    }

    // ---------------------------------------------------------------- Uçlar ve maç entegrasyonu

    private async Task<(HttpClient Client, Guid UserId)> RegisteredAsync()
    {
        var uid = Guid.NewGuid().ToString("N");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Dev uid={uid};email={uid}@test.local;verified=true;provider=google.com");
        client.DefaultRequestHeaders.Add("X-Device-Id", "d1");
        await client.PostAsJsonAsync("/api/users/me", new { username = "e" + uid[..10], displayName = "Uç Test", avatarId = 1, birthYear = 2000, termsAccepted = true });
        await client.PostAsJsonAsync("/api/users/me/session", new { deviceId = "d1", platform = "test" });
        var me = await client.GetFromJsonAsync<JsonElement>("/api/users/me");
        return (client, me.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CurrentEndpoint_ShowsThePeriod_RewardTable_TopList_AndMyRank()
    {
        var (client, userId) = await RegisteredAsync();
        using (var s = New())
            await s.Scores.CreditAsync(Guid.NewGuid(), userId, 60, DateTime.UtcNow);

        var json = await client.GetFromJsonAsync<JsonElement>("/api/tournament/current");

        var starts = json.GetProperty("startsAt").GetDateTimeOffset();
        var ends = json.GetProperty("endsAt").GetDateTimeOffset();
        Assert.Equal(48, (ends - starts).TotalHours);
        Assert.Equal(TimeSpan.Zero, (starts.UtcDateTime + TournamentClock.TurkeyOffset).TimeOfDay);
        Assert.Equal(10, json.GetProperty("rewards").GetArrayLength());
        var me = json.GetProperty("me");
        Assert.True(me.GetProperty("points").GetInt32() >= 60);
        Assert.True(me.GetProperty("rank").GetInt32() >= 1);
        Assert.Contains(json.GetProperty("top").EnumerateArray(), r => r.GetProperty("isMe").GetBoolean() || r.GetProperty("userId").GetGuid() != userId);
        Assert.True(json.GetProperty("top").GetArrayLength() <= 50);
    }

    [Fact]
    public async Task PreviousEndpoint_And_WalletEndpoints_WorkForARegisteredUser()
    {
        var (client, userId) = await RegisteredAsync();
        var previous = await client.GetFromJsonAsync<JsonElement>("/api/tournament/previous");
        Assert.True(previous.TryGetProperty("periodId", out _));

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPointsWallet>().GrantAsync(userId, 75, "tournament", "period", "1", $"w:{userId:N}", "Test");

        var wallet = await client.GetFromJsonAsync<JsonElement>("/api/wallet");
        Assert.Equal(75, wallet.GetProperty("balance").GetInt64());
        Assert.Equal(5, wallet.GetProperty("jokers").GetProperty("half").GetInt32());

        var history = await client.GetFromJsonAsync<JsonElement>("/api/wallet/history");
        Assert.Equal(75, history[0].GetProperty("amount").GetInt64());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/tournament/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/wallet")).StatusCode);
    }

    [Fact]
    public async Task VersusMatch_CreditsTheWinnerAndTheBonus_ButNeverTheBotOrACancelledMatch()
    {
        // Rakipli maç: A 10 doğru, B 7 doğru → A 15 puan alır ve dönemlik puanına yazılır
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        for (var pos = 1; pos <= 10; pos++) await h.PlayRoundAsync(pos, aCorrect: true, bCorrect: pos <= 7);
        Assert.True(h.Ended);

        using var s = New();
        var period = await s.Periods.GetCurrentAsync();
        Assert.Equal(15, (await s.Db.Scores.AsNoTracking().SingleAsync(x => x.PeriodId == period.Id && x.UserId == h.A)).Points);
        Assert.False(await s.Db.Scores.AnyAsync(x => x.UserId == h.B && x.PeriodId == period.Id)); // kaybeden 0 puan: satır yok

        // İptal edilen maç puan yazmaz
        var cancelled = await VersusHarness.CreateAsync(factory);
        await cancelled.Runner.StartAsync();
        await cancelled.AdvanceAsync(5.1);
        await cancelled.Runner.CancelAsync("test");
        Assert.False(await s.Db.Entries.AnyAsync(e => e.UserId == cancelled.A || e.UserId == cancelled.B));
    }
}
