namespace SenBilirsin.Modules.MatchEngine.Application;

// İstemciye giden görünümler. Doğru şık, soru cevaplanana (ya da süresi bitene) kadar hiçbirinde bulunmaz (mimari ilke 1).

public sealed record JokerBalances(int Half, int Double, int Hint);

/// <param name="Options">Ekrandaki (sunucunun karıştırdığı) sırayla 4 şık.</param>
/// <param name="StartsAt">Sorunun başlangıç anı (sunucu saati, UTC). Önceki sonucun gösterilmesi için gelecekte olabilir.</param>
/// <param name="EndsAt">Mutlak bitiş anı (sunucu saati, UTC); geri sayım <c>EndsAt − ServerNow</c> farkıyla yapılır.</param>
public sealed record QuestionView(int Position, int TotalQuestions, string Text, IReadOnlyList<string> Options, DateTime StartsAt, DateTime EndsAt);

/// <param name="Removed">Yarı yarıya: elenen ekran şıkları.</param>
/// <param name="Hint">İpucu: gösterilen ekran şıkkı (doğru olmayabilir).</param>
public sealed record ActiveJokerView(string Type, IReadOnlyList<int>? Removed, int? Hint);

public sealed record ContinueOfferView(DateTime Deadline);

public sealed record MatchResultView(string Status, string Reason, int CorrectCount, int Points, bool Settled);

/// <summary>Maçın o anki tam durumu; yeniden bağlanan istemci buradan kaldığı yerden devam eder.</summary>
public sealed record MatchState(
    Guid MatchId,
    string Mode,
    int? CategoryId,
    string Status,
    int Position,
    int TotalQuestions,
    int CorrectCount,
    QuestionView? Question,
    ActiveJokerView? ActiveJoker,
    IReadOnlyList<string> UsedJokerTypes,
    JokerBalances Jokers,
    ContinueOfferView? ContinueOffer,
    MatchResultView? Result,
    DateTime ServerNow);

public sealed record AnswerOutcome(
    int Position,
    bool IsCorrect,
    bool TimedOut,
    int CorrectOption,
    int? ChosenOption,
    int? SecondOption,
    string? Explanation,
    MatchState State);

public sealed record JokerOutcome(ActiveJokerView Joker, JokerBalances Jokers);

public sealed record SummaryItem(
    int Position,
    string Text,
    IReadOnlyList<string> Options,
    int CorrectOption,
    int? ChosenOption,
    int? SecondOption,
    bool IsCorrect,
    bool TimedOut,
    string? Explanation,
    string? JokerUsed,
    bool Replaced);

public sealed record MatchSummary(Guid MatchId, string Mode, int? CategoryId, string Status, string Reason, int CorrectCount, int Points, IReadOnlyList<SummaryItem> Items);

public sealed record MatchError(string Code, string Message, int StatusCode = 400, Guid? MatchId = null);

public sealed class MatchResult<T>
{
    public T? Value { get; private init; }
    public MatchError? Error { get; private init; }
    public bool IsSuccess => Error is null;

    public static MatchResult<T> Ok(T value) => new() { Value = value };
    public static MatchResult<T> Fail(string code, string message, int status = 400, Guid? matchId = null) =>
        new() { Error = new MatchError(code, message, status, matchId) };
}
