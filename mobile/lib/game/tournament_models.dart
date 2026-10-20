// Turnuva ve cüzdan görünümleri (docs/07). Zamanlar UTC.

DateTime _utc(Object? value) {
  final text = value as String;
  return DateTime.parse(text.endsWith('Z') || text.contains('+') ? text : '${text}Z').toUtc();
}

class RewardTier {
  const RewardTier({required this.rankFrom, required this.rankTo, required this.amount});

  final int rankFrom;
  final int rankTo;
  final int amount;

  String get rankLabel => rankFrom == rankTo ? '$rankFrom.' : '$rankFrom.–$rankTo.';

  factory RewardTier.fromJson(Map<String, dynamic> json) =>
      RewardTier(rankFrom: json['rankFrom'] as int, rankTo: json['rankTo'] as int, amount: json['amount'] as int);
}

class LeaderboardRow {
  const LeaderboardRow({required this.rank, required this.name, required this.avatarId, required this.points, required this.isMe, required this.reward});

  final int rank;
  final String name;
  final int avatarId;
  final int points;
  final bool isMe;
  final int reward;

  factory LeaderboardRow.fromJson(Map<String, dynamic> json) => LeaderboardRow(
        rank: json['rank'] as int,
        name: json['name'] as String,
        avatarId: json['avatarId'] as int,
        points: json['points'] as int,
        isMe: json['isMe'] as bool,
        reward: json['reward'] as int? ?? 0,
      );
}

class MyStanding {
  const MyStanding({required this.rank, required this.points, required this.reward});

  final int rank;
  final int points;
  final int reward;

  factory MyStanding.fromJson(Map<String, dynamic> json) =>
      MyStanding(rank: json['rank'] as int, points: json['points'] as int, reward: json['reward'] as int? ?? 0);
}

class TournamentInfo {
  const TournamentInfo({
    required this.periodId,
    required this.endsAt,
    required this.serverNow,
    required this.rewards,
    required this.top,
    this.me,
  });

  final int periodId;
  final DateTime endsAt;
  final DateTime serverNow;
  final List<RewardTier> rewards;
  final List<LeaderboardRow> top;
  final MyStanding? me;

  factory TournamentInfo.fromJson(Map<String, dynamic> json) => TournamentInfo(
        periodId: json['periodId'] as int,
        endsAt: _utc(json['endsAt']),
        serverNow: _utc(json['serverNow']),
        rewards: (json['rewards'] as List<dynamic>).map((e) => RewardTier.fromJson(e as Map<String, dynamic>)).toList(),
        top: (json['top'] as List<dynamic>).map((e) => LeaderboardRow.fromJson(e as Map<String, dynamic>)).toList(),
        me: json['me'] == null ? null : MyStanding.fromJson(json['me'] as Map<String, dynamic>),
      );
}

/// Önceki turun ilk 10'u ("ilk açılışta pop-up"). Önceki tur yoksa [periodId] boştur.
class PreviousTournament {
  const PreviousTournament({required this.periodId, required this.top, this.me});

  final int? periodId;
  final List<LeaderboardRow> top;
  final MyStanding? me;

  factory PreviousTournament.fromJson(Map<String, dynamic> json) => PreviousTournament(
        periodId: json['periodId'] as int?,
        top: (json['top'] as List<dynamic>? ?? []).map((e) => LeaderboardRow.fromJson(e as Map<String, dynamic>)).toList(),
        me: json['me'] == null ? null : MyStanding.fromJson(json['me'] as Map<String, dynamic>),
      );
}

class WalletInfo {
  const WalletInfo({required this.balance, required this.half, required this.doubleAnswer, required this.hint});

  final int balance;
  final int half;
  final int doubleAnswer;
  final int hint;

  factory WalletInfo.fromJson(Map<String, dynamic> json) {
    final jokers = json['jokers'] as Map<String, dynamic>;
    return WalletInfo(balance: (json['balance'] as num).toInt(), half: jokers['half'] as int, doubleAnswer: jokers['double'] as int, hint: jokers['hint'] as int);
  }
}

class PointsHistoryItem {
  const PointsHistoryItem({required this.amount, required this.type, required this.createdAt, this.note});

  final int amount;
  final String type;
  final String? note;
  final DateTime createdAt;

  String get label => switch (type) {
        'tournament' => 'Turnuva ödülü',
        'purchase' => 'Mağaza alışverişi',
        'refund' => 'İade',
        'admin' => 'Düzeltme',
        _ => type,
      };

  factory PointsHistoryItem.fromJson(Map<String, dynamic> json) => PointsHistoryItem(
        amount: (json['amount'] as num).toInt(),
        type: json['type'] as String,
        note: json['note'] as String?,
        createdAt: _utc(json['createdAt']),
      );
}
