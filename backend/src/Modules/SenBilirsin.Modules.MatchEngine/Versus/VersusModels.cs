using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Application;

namespace SenBilirsin.Modules.MatchEngine.Versus;

/// <summary>
/// Maç olaylarını oyunculara iletir (SignalR gerçeklemesi MobileApi'dedir; testlerde kayıt tutan sahte).
/// Olay adları docs/04 §3'tedir: MatchFound, CountdownStarted, QuestionStarted, OpponentAnswered, QuestionResult,
/// JokerResult, OpponentDisconnected, OpponentReconnected, MatchFinished, MatchCancelled…
/// </summary>
public interface IMatchNotifier
{
    Task SendAsync(Guid userId, string eventName, object payload);
}

public static class MatchEvents
{
    public const string MatchFound = "MatchFound";
    public const string QueueTimeout = "QueueTimeout";
    public const string OpponentJoinedRoom = "OpponentJoinedRoom";
    public const string RoomClosed = "RoomClosed";
    public const string CountdownStarted = "CountdownStarted";
    public const string QuestionStarted = "QuestionStarted";
    public const string OpponentAnswered = "OpponentAnswered";
    public const string QuestionResult = "QuestionResult";
    public const string JokerResult = "JokerResult";
    public const string OpponentDisconnected = "OpponentDisconnected";
    public const string OpponentReconnected = "OpponentReconnected";
    public const string MatchFinished = "MatchFinished";
    public const string MatchCancelled = "MatchCancelled";
    public const string RematchRequested = "RematchRequested";
    public const string RematchResult = "RematchResult";
}

/// <summary>Maçtaki bir taraf. Bot ise <see cref="Id"/> botun profil kimliğidir.</summary>
public sealed record VersusPlayerSpec(Guid Id, string Name, int AvatarId, int? FrameId, bool IsBot, BotProfile? Bot);

public sealed record VersusMatchSpec(
    Guid MatchId,
    MatchKind Kind,
    string ModeCode,
    int? CategoryId,
    VersusPlayerSpec A,
    VersusPlayerSpec B,
    IReadOnlyList<BankQuestion> Questions,
    bool SeenFilterRelaxed);

public sealed record JokerGrant(string Type, int? Hint, IReadOnlyList<int>? Removed);

public static class MatchEndReasonsVersus
{
    public const string Normal = "normal";
    public const string Forfeit = "forfeit";
    public const string Disconnect = "disconnect";
    public const string BothDisconnected = "both_disconnected";
    public const string ServerRestart = "server_restart";
    public const string NotReady = "not_ready";
}
