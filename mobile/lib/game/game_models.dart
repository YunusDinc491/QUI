// Sunucudan gelen bireysel maç görünümleri (docs/04 Faz 3). Doğru cevap yalnızca [AnswerOutcome]'da,
// soru cevaplandıktan sonra gelir.

DateTime _time(Object? value) => DateTime.parse(value as String).toUtc();

class JokerBalances {
  const JokerBalances({required this.half, required this.doubleAnswer, required this.hint});

  final int half;
  final int doubleAnswer;
  final int hint;

  int of(String type) => switch (type) {
        'half' => half,
        'double' => doubleAnswer,
        _ => hint,
      };

  factory JokerBalances.fromJson(Map<String, dynamic> json) =>
      JokerBalances(half: json['half'] as int, doubleAnswer: json['double'] as int, hint: json['hint'] as int);
}

class QuestionView {
  const QuestionView({
    required this.position,
    required this.total,
    required this.text,
    required this.options,
    required this.startsAt,
    required this.endsAt,
  });

  final int position;
  final int total;
  final String text;
  final List<String> options;
  final DateTime startsAt;
  final DateTime endsAt;

  factory QuestionView.fromJson(Map<String, dynamic> json) => QuestionView(
        position: json['position'] as int,
        total: json['totalQuestions'] as int,
        text: json['text'] as String,
        options: (json['options'] as List<dynamic>).cast<String>(),
        startsAt: _time(json['startsAt']),
        endsAt: _time(json['endsAt']),
      );
}

class ActiveJoker {
  const ActiveJoker({required this.type, this.removed, this.hint});

  final String type;

  /// Yarı yarıya: elenen ekran şıkları.
  final List<int>? removed;

  /// İpucu: gösterilen şık (doğru olmayabilir).
  final int? hint;

  factory ActiveJoker.fromJson(Map<String, dynamic> json) => ActiveJoker(
        type: json['type'] as String,
        removed: (json['removed'] as List<dynamic>?)?.cast<int>(),
        hint: json['hint'] as int?,
      );
}

class MatchResultInfo {
  const MatchResultInfo({required this.status, required this.reason, required this.correctCount, required this.points});

  final String status;
  final String reason;
  final int correctCount;
  final int points;

  factory MatchResultInfo.fromJson(Map<String, dynamic> json) => MatchResultInfo(
        status: json['status'] as String,
        reason: json['reason'] as String,
        correctCount: json['correctCount'] as int,
        points: json['points'] as int,
      );
}

class MatchState {
  const MatchState({
    required this.matchId,
    required this.mode,
    required this.categoryId,
    required this.status,
    required this.position,
    required this.total,
    required this.correctCount,
    required this.usedJokerTypes,
    required this.jokers,
    required this.serverNow,
    this.question,
    this.activeJoker,
    this.continueDeadline,
    this.result,
  });

  final String matchId;
  final String mode;
  final int? categoryId;

  /// running | awaiting_continue | finished | cancelled | forfeited
  final String status;
  final int position;
  final int total;
  final int correctCount;
  final QuestionView? question;
  final ActiveJoker? activeJoker;
  final List<String> usedJokerTypes;
  final JokerBalances jokers;
  final DateTime? continueDeadline;
  final MatchResultInfo? result;
  final DateTime serverNow;

  bool get isRunning => status == 'running';
  bool get isAwaitingContinue => status == 'awaiting_continue';
  bool get isOver => status == 'finished' || status == 'cancelled' || status == 'forfeited';

  factory MatchState.fromJson(Map<String, dynamic> json) => MatchState(
        matchId: json['matchId'] as String,
        mode: json['mode'] as String,
        categoryId: json['categoryId'] as int?,
        status: json['status'] as String,
        position: json['position'] as int,
        total: json['totalQuestions'] as int,
        correctCount: json['correctCount'] as int,
        question: json['question'] == null ? null : QuestionView.fromJson(json['question'] as Map<String, dynamic>),
        activeJoker: json['activeJoker'] == null ? null : ActiveJoker.fromJson(json['activeJoker'] as Map<String, dynamic>),
        usedJokerTypes: (json['usedJokerTypes'] as List<dynamic>).cast<String>(),
        jokers: JokerBalances.fromJson(json['jokers'] as Map<String, dynamic>),
        continueDeadline: json['continueOffer'] == null ? null : _time((json['continueOffer'] as Map<String, dynamic>)['deadline']),
        result: json['result'] == null ? null : MatchResultInfo.fromJson(json['result'] as Map<String, dynamic>),
        serverNow: _time(json['serverNow']),
      );
}

class AnswerOutcome {
  const AnswerOutcome({
    required this.position,
    required this.isCorrect,
    required this.timedOut,
    required this.correctOption,
    required this.state,
    this.chosenOption,
    this.secondOption,
    this.explanation,
  });

  final int position;
  final bool isCorrect;
  final bool timedOut;
  final int correctOption;
  final int? chosenOption;
  final int? secondOption;
  final String? explanation;
  final MatchState state;

  factory AnswerOutcome.fromJson(Map<String, dynamic> json) => AnswerOutcome(
        position: json['position'] as int,
        isCorrect: json['isCorrect'] as bool,
        timedOut: json['timedOut'] as bool,
        correctOption: json['correctOption'] as int,
        chosenOption: json['chosenOption'] as int?,
        secondOption: json['secondOption'] as int?,
        explanation: json['explanation'] as String?,
        state: MatchState.fromJson(json['state'] as Map<String, dynamic>),
      );
}

class JokerOutcome {
  const JokerOutcome({required this.joker, required this.balances});

  final ActiveJoker joker;
  final JokerBalances balances;

  factory JokerOutcome.fromJson(Map<String, dynamic> json) => JokerOutcome(
        joker: ActiveJoker.fromJson(json['joker'] as Map<String, dynamic>),
        balances: JokerBalances.fromJson(json['jokers'] as Map<String, dynamic>),
      );
}

class SummaryItem {
  const SummaryItem({
    required this.position,
    required this.text,
    required this.options,
    required this.correctOption,
    required this.isCorrect,
    required this.timedOut,
    required this.replaced,
    this.chosenOption,
    this.secondOption,
    this.explanation,
    this.jokerUsed,
  });

  final int position;
  final String text;
  final List<String> options;
  final int correctOption;
  final int? chosenOption;
  final int? secondOption;
  final bool isCorrect;
  final bool timedOut;
  final String? explanation;
  final String? jokerUsed;
  final bool replaced;

  factory SummaryItem.fromJson(Map<String, dynamic> json) => SummaryItem(
        position: json['position'] as int,
        text: json['text'] as String,
        options: (json['options'] as List<dynamic>).cast<String>(),
        correctOption: json['correctOption'] as int,
        chosenOption: json['chosenOption'] as int?,
        secondOption: json['secondOption'] as int?,
        isCorrect: json['isCorrect'] as bool,
        timedOut: json['timedOut'] as bool,
        explanation: json['explanation'] as String?,
        jokerUsed: json['jokerUsed'] as String?,
        replaced: json['replaced'] as bool,
      );
}

class MatchSummary {
  const MatchSummary({required this.matchId, required this.status, required this.reason, required this.correctCount, required this.points, required this.items});

  final String matchId;
  final String status;
  final String reason;
  final int correctCount;
  final int points;
  final List<SummaryItem> items;

  factory MatchSummary.fromJson(Map<String, dynamic> json) => MatchSummary(
        matchId: json['matchId'] as String,
        status: json['status'] as String,
        reason: json['reason'] as String,
        correctCount: json['correctCount'] as int,
        points: json['points'] as int,
        items: (json['items'] as List<dynamic>).map((e) => SummaryItem.fromJson(e as Map<String, dynamic>)).toList(),
      );
}

/// Bota karşı oyunda seçilebilen bot (güç: 1 zayıf, 2 orta, 3 güçlü).
class BotInfo {
  const BotInfo({required this.id, required this.name, required this.avatarId, required this.power});

  final String id;
  final String name;
  final int avatarId;
  final int power;

  String get powerLabel => switch (power) { 1 => 'Zayıf', 2 => 'Orta', _ => 'Güçlü' };

  factory BotInfo.fromJson(Map<String, dynamic> json) =>
      BotInfo(id: json['id'] as String, name: json['name'] as String, avatarId: json['avatarId'] as int, power: json['power'] as int);
}
