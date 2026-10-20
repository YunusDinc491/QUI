import 'dart:async';

import 'package:flutter/foundation.dart';

import 'game_models.dart';
import 'versus_transport.dart';

enum VersusPhase {
  idle,

  /// Rakip aranıyor (15 sn).
  searching,

  /// 15 sn doldu: bot / bireysel / tekrar ara seçimi bekleniyor.
  searchTimedOut,

  /// Arkadaş odası kuruldu, rakip bekleniyor.
  roomWaiting,

  /// Rakip bulundu: hazır olunması bekleniyor.
  found,

  /// 5'ten geri sayım.
  countdown,

  /// Soru açık.
  question,

  /// Sonuç gösteriliyor (sıradaki soru gelene kadar).
  result,
  finished,
  cancelled,
}

class Opponent {
  const Opponent({required this.name, required this.avatarId, required this.isBot});

  final String name;
  final int avatarId;
  final bool isBot;

  factory Opponent.fromJson(Map<String, dynamic> json) =>
      Opponent(name: json['name'] as String? ?? 'Rakip', avatarId: json['avatarId'] as int? ?? 1, isBot: json['isBot'] as bool? ?? false);
}

class VersusQuestion {
  const VersusQuestion({required this.position, required this.total, required this.text, required this.options, required this.endsAt});

  final int position;
  final int total;
  final String text;
  final List<String> options;
  final DateTime endsAt;
}

class VersusRoundResult {
  const VersusRoundResult({
    required this.position,
    required this.correctOption,
    required this.yourCorrect,
    required this.opponentCorrect,
    required this.timedOut,
    this.yourOption,
    this.yourSecond,
    this.opponentOption,
    this.explanation,
  });

  final int position;
  final int correctOption;
  final int? yourOption;
  final int? yourSecond;
  final int? opponentOption;
  final bool yourCorrect;
  final bool opponentCorrect;
  final bool timedOut;
  final String? explanation;
}

class VersusFinish {
  const VersusFinish({
    required this.result,
    required this.points,
    required this.bonus,
    required this.reason,
    required this.yourCorrect,
    required this.opponentCorrect,
  });

  /// win | lose | draw
  final String result;
  final int points;
  final int bonus;

  /// normal | forfeit | disconnect
  final String reason;
  final int yourCorrect;
  final int opponentCorrect;
}

/// Rakipli maçın istemci mantığı (SignalR üzerinden): arama, hazır olma, geri sayım, soru akışı, joker, kopma bildirimleri.
/// Sunucu süreyi ölçer; burada yalnızca sunucu saat farkıyla geri sayım gösterilir.
class VersusController extends ChangeNotifier {
  VersusController({required this.transport, DateTime Function()? now}) : _now = now ?? DateTime.now;

  final VersusTransport transport;
  final DateTime Function() _now;

  VersusPhase phase = VersusPhase.idle;
  String? error;

  String? matchId;
  Opponent? opponent;
  bool needsReady = false;
  bool readySent = false;
  String? roomCode;
  DateTime? roomExpiresAt;
  DateTime? countdownEndsAt;
  VersusQuestion? question;
  int yourScore = 0;
  int opponentScore = 0;
  bool opponentAnswered = false;
  final List<int> selected = [];
  VersusRoundResult? lastResult;
  VersusFinish? finish;
  String? cancelReason;

  /// Rövanş: biz istedik / rakip istedi; sonuç mesajı (reddedildi, süre doldu).
  bool rematchRequested = false;
  bool rematchIncoming = false;
  String? rematchMessage;

  /// Rakip koptuysa havuzda kalan süre (ms).
  int? opponentDisconnectedMs;

  // Joker durumu
  JokerBalances? jokers;
  List<String> usedJokerTypes = [];
  ActiveJoker? activeJoker;

  /// Arama başlangıcı (15 sn geri sayım için).
  DateTime? searchStartedAt;

  /// Son aranan mod ve alt dal (kuyruk zaman aşımında "bireysel oyna" için).
  (String, int?)? pendingMode;
  bool connected = false;
  bool _wired = false;
  Duration _serverOffset = Duration.zero;

  DateTime get serverNow => _now().toUtc().add(_serverOffset);

  Duration get remaining {
    final q = question;
    if (q == null) return Duration.zero;
    final left = q.endsAt.difference(serverNow);
    return left.isNegative ? Duration.zero : left;
  }

  bool get doubleActive => activeJoker?.type == 'double';
  List<int> get removedOptions => activeJoker?.removed ?? const [];
  int? get hintOption => activeJoker?.hint;
  bool get answered => selected.isNotEmpty && (!doubleActive || selected.length >= 2) || _answeredNoOption;
  bool _answeredNoOption = false;

  bool canUseJoker(String type) =>
      phase == VersusPhase.question && !answered && activeJoker == null && !usedJokerTypes.contains(type) && (jokers?.of(type) ?? 0) > 0;

  // ---------------------------------------------------------------- Bağlantı

  Future<void> connect() async {
    _wire();
    await transport.start();
    connected = true;
    notifyListeners();
  }

  Future<void> disconnect() async {
    await transport.stop();
    connected = false;
    notifyListeners();
  }

  void _wire() {
    if (_wired) return;
    _wired = true;
    transport.onConnectionChanged((c) {
      connected = c;
      notifyListeners();
    });
    transport.on('MatchFound', _onMatchFound);
    transport.on('QueueTimeout', (_) => _set(VersusPhase.searchTimedOut));
    transport.on('OpponentJoinedRoom', (p) {
      opponent = Opponent.fromJson(Map<String, dynamic>.from(p['opponent'] as Map));
      notifyListeners();
    });
    transport.on('RoomClosed', (p) {
      roomCode = null;
      cancelReason = p['reason'] as String?;
      _set(VersusPhase.cancelled);
    });
    transport.on('CountdownStarted', (p) {
      _sync(p);
      countdownEndsAt = DateTime.parse(p['startsAt'] as String).toUtc();
      _set(VersusPhase.countdown);
    });
    transport.on('QuestionStarted', _onQuestionStarted);
    transport.on('OpponentAnswered', (_) {
      opponentAnswered = true;
      notifyListeners();
    });
    transport.on('QuestionResult', _onQuestionResult);
    transport.on('JokerResult', (p) {
      final grant = Map<String, dynamic>.from(p['joker'] as Map);
      activeJoker = ActiveJoker.fromJson(grant);
      jokers = JokerBalances.fromJson(Map<String, dynamic>.from(p['jokers'] as Map));
      notifyListeners();
    });
    transport.on('OpponentDisconnected', (p) {
      opponentDisconnectedMs = p['remainingMs'] as int?;
      notifyListeners();
    });
    transport.on('OpponentReconnected', (_) {
      opponentDisconnectedMs = null;
      notifyListeners();
    });
    transport.on('MatchFinished', (p) {
      finish = VersusFinish(
        result: p['result'] as String,
        points: p['points'] as int,
        bonus: p['bonus'] as int,
        reason: p['reason'] as String,
        yourCorrect: p['yourCorrect'] as int,
        opponentCorrect: p['opponentCorrect'] as int,
      );
      _set(VersusPhase.finished);
    });
    transport.on('MatchCancelled', (p) {
      cancelReason = p['reason'] as String?;
      _set(VersusPhase.cancelled);
    });
    transport.on('MatchState', _onMatchState);
    transport.on('RematchRequested', (_) {
      rematchIncoming = true;
      notifyListeners();
    });
    transport.on('RematchResult', (p) {
      rematchRequested = false;
      rematchIncoming = false;
      if (p['accepted'] != true) rematchMessage = p['reason'] == 'timeout' ? 'Rakip rövanşa yanıt vermedi.' : 'Rakip rövanşı kabul etmedi.';
      notifyListeners();
    });
    transport.on('SessionClosed', (_) {
      cancelReason = 'session_closed';
      _set(VersusPhase.cancelled);
    });
  }

  // ---------------------------------------------------------------- Olaylar

  void _sync(Map<String, dynamic> p) {
    final serverNowText = p['serverNow'] as String?;
    if (serverNowText != null) _serverOffset = DateTime.parse(serverNowText).toUtc().difference(_now().toUtc());
  }

  void _set(VersusPhase next) {
    phase = next;
    notifyListeners();
  }

  void _onMatchFound(Map<String, dynamic> p) {
    matchId = p['matchId'] as String;
    opponent = Opponent.fromJson(Map<String, dynamic>.from(p['opponent'] as Map));
    needsReady = p['needsReady'] as bool? ?? false;
    readySent = false;
    yourScore = 0;
    opponentScore = 0;
    question = null;
    lastResult = null;
    finish = null;
    usedJokerTypes = [];
    activeJoker = null;
    roomCode = null;
    rematchRequested = false;
    rematchIncoming = false;
    rematchMessage = null;
    _set(needsReady ? VersusPhase.found : VersusPhase.countdown);
  }

  void _onQuestionStarted(Map<String, dynamic> p) {
    _sync(p);
    matchId = p['matchId'] as String? ?? matchId;
    question = VersusQuestion(
      position: p['position'] as int,
      total: p['total'] as int,
      text: p['text'] as String,
      options: (p['options'] as List<dynamic>).cast<String>(),
      endsAt: DateTime.parse(p['endsAt'] as String).toUtc(),
    );
    yourScore = p['yourScore'] as int? ?? yourScore;
    opponentScore = p['opponentScore'] as int? ?? opponentScore;
    jokers = p['jokers'] == null ? jokers : JokerBalances.fromJson(Map<String, dynamic>.from(p['jokers'] as Map));
    usedJokerTypes = (p['usedJokerTypes'] as List<dynamic>? ?? []).cast<String>();
    activeJoker = p['activeJoker'] == null ? null : ActiveJoker.fromJson(Map<String, dynamic>.from(p['activeJoker'] as Map));
    selected.clear();
    _answeredNoOption = false;
    opponentAnswered = false;
    lastResult = null;
    _set(VersusPhase.question);
  }

  void _onQuestionResult(Map<String, dynamic> p) {
    _sync(p);
    yourScore = p['yourScore'] as int;
    opponentScore = p['opponentScore'] as int;
    lastResult = VersusRoundResult(
      position: p['position'] as int,
      correctOption: p['correctOption'] as int,
      yourOption: p['yourOption'] as int?,
      yourSecond: p['yourSecond'] as int?,
      opponentOption: p['opponentOption'] as int?,
      yourCorrect: p['yourCorrect'] as bool,
      opponentCorrect: p['opponentCorrect'] as bool,
      timedOut: p['yourTimedOut'] as bool? ?? false,
      explanation: p['explanation'] as String?,
    );
    _set(VersusPhase.result);
  }

  /// Yeniden bağlanınca sunucunun gönderdiği tam durum özeti.
  void _onMatchState(Map<String, dynamic> p) {
    _sync(p);
    matchId = p['matchId'] as String;
    opponent = Opponent.fromJson(Map<String, dynamic>.from(p['opponent'] as Map));
    yourScore = p['yourScore'] as int;
    opponentScore = p['opponentScore'] as int;
    usedJokerTypes = (p['usedJokerTypes'] as List<dynamic>? ?? []).cast<String>();
    final q = p['question'];
    if (q is Map) {
      final map = Map<String, dynamic>.from(q);
      question = VersusQuestion(
        position: map['position'] as int,
        total: p['total'] as int,
        text: map['text'] as String,
        options: (map['options'] as List<dynamic>).cast<String>(),
        endsAt: DateTime.parse(map['endsAt'] as String).toUtc(),
      );
      activeJoker = map['activeJoker'] == null ? null : ActiveJoker.fromJson(Map<String, dynamic>.from(map['activeJoker'] as Map));
      _answeredNoOption = map['answered'] as bool? ?? false;
      _set(VersusPhase.question);
    } else {
      _set(p['phase'] == 'countdown' ? VersusPhase.countdown : VersusPhase.result);
    }
  }

  // ---------------------------------------------------------------- Eylemler

  bool get _canEnterSearching => phase == VersusPhase.idle || phase == VersusPhase.searchTimedOut || phase == VersusPhase.cancelled;

  Future<bool> _call(String method, [List<Object?> args = const []]) async {
    error = null;
    try {
      final result = await transport.invoke(method, args);
      if (result['ok'] != true) {
        error = result['message'] as String? ?? 'İşlem başarısız.';
        notifyListeners();
        return false;
      }

      return true;
    } catch (_) {
      error = 'Sunucuya ulaşılamadı.';
      notifyListeners();
      return false;
    }
  }

  Future<void> search(String mode, int? categoryId) async {
    searchStartedAt = _now();
    pendingMode = (mode, categoryId);
    // Rakip zaten bekliyorsa MatchFound, bu çağrının yanıtından ÖNCE gelebilir; o durumda aşama ezilmez
    if (await _call('JoinQueue', [mode, categoryId]) && _canEnterSearching) _set(VersusPhase.searching);
  }

  Future<void> cancelSearch() async {
    await _call('LeaveQueue');
    _set(VersusPhase.idle);
  }

  /// 15 sn sonunda: `bot`, `solo` ya da `retry`.
  Future<bool> chooseFallback(String choice, {String? botId}) async {
    final ok = await _call('ChooseFallback', [choice, botId]);
    if (ok && choice == 'retry' && _canEnterSearching) _set(VersusPhase.searching);
    if (ok && choice == 'solo') _set(VersusPhase.idle);
    return ok;
  }

  Future<void> createRoom(String mode, int? categoryId) async {
    error = null;
    try {
      final result = await transport.invoke('CreateRoom', [mode, categoryId]);
      if (result['ok'] == true) {
        final data = Map<String, dynamic>.from(result['data'] as Map);
        roomCode = data['code'] as String;
        roomExpiresAt = DateTime.parse(data['expiresAt'] as String).toUtc();
        _set(VersusPhase.roomWaiting);
      } else {
        error = result['message'] as String?;
        notifyListeners();
      }
    } catch (_) {
      error = 'Sunucuya ulaşılamadı.';
      notifyListeners();
    }
  }

  Future<bool> joinRoom(String code) => _call('JoinRoom', [code]);

  Future<void> leaveRoom() async {
    await _call('LeaveRoom');
    roomCode = null;
    _set(VersusPhase.idle);
  }

  Future<void> ready() async {
    final id = matchId;
    if (id == null || readySent) return;
    readySent = true;
    notifyListeners();
    if (!await _call('Ready', [id])) readySent = false;
  }

  /// İlk dokunuş kesin; çift cevap açıksa iki şık seçilince gönderilir.
  Future<void> select(int index) async {
    final id = matchId;
    final q = question;
    if (phase != VersusPhase.question || id == null || q == null || answered || selected.contains(index) || removedOptions.contains(index)) return;
    selected.add(index);
    notifyListeners();
    if (doubleActive && selected.length < 2) return;
    if (!await _call('SubmitAnswer', [id, q.position, selected.first, selected.length > 1 ? selected[1] : null])) selected.clear();
    notifyListeners();
  }

  /// Joker kullanır (soru başına bir, tür başına maçta bir; rakip görmez).
  Future<void> useJoker(String type) async {
    final id = matchId;
    final q = question;
    if (id == null || q == null || !canUseJoker(type)) return;
    await _call('UseJoker', [id, q.position, type]);
  }

  /// Maç bittikten sonra rövanş iste (aynı kategori, aynı rakip). Bota karşıysa hemen yeni maç başlar.
  Future<void> requestRematch() async {
    final id = matchId;
    if (id == null || rematchRequested) return;
    rematchMessage = null;
    if (await _call('RequestRematch', [id])) {
      rematchRequested = true;
      notifyListeners();
    }
  }

  Future<void> respondRematch(bool accept) async {
    final id = matchId;
    if (id == null) return;
    rematchIncoming = false;
    await _call('RespondRematch', [id, accept]);
    notifyListeners();
  }

  Future<void> forfeit() async {
    final id = matchId;
    if (id != null) await _call('Forfeit', [id]);
  }

  /// Uygulama arka plana atıldı/öne döndü (docs/01 §11).
  Future<void> appBackgrounded() async {
    if (phase == VersusPhase.idle || phase == VersusPhase.finished || phase == VersusPhase.cancelled) return;
    await _call('AppBackgrounded');
  }

  Future<void> appForegrounded() async {
    if (phase == VersusPhase.idle || phase == VersusPhase.finished || phase == VersusPhase.cancelled) return;
    try {
      final result = await transport.invoke('AppForegrounded');
      final data = result['data'];
      if (data is Map) _onMatchState(Map<String, dynamic>.from(data));
    } catch (_) {
      error = 'Sunucuya ulaşılamadı.';
      notifyListeners();
    }
  }

  bool _disposed = false;

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }

  /// Ekran kapandıktan sonra gelen geç yanıtlar/olaylar hata vermesin.
  @override
  void notifyListeners() {
    if (!_disposed) super.notifyListeners();
  }

  void reset() {
    phase = VersusPhase.idle;
    matchId = null;
    opponent = null;
    question = null;
    lastResult = null;
    finish = null;
    cancelReason = null;
    roomCode = null;
    selected.clear();
    notifyListeners();
  }
}
