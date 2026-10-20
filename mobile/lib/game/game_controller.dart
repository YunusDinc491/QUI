import 'dart:async';

import 'package:flutter/foundation.dart';

import '../ads/rewarded_ad_service.dart';
import '../core/api_client.dart';
import 'game_models.dart';

/// Ekranda gösterilen aşama.
enum GamePhase {
  /// Soru henüz başlamadı (kısa bekleme).
  waiting,

  /// Soru açık, cevap bekleniyor.
  answering,

  /// Cevap verildi/süre doldu: doğru şık gösteriliyor, sıradaki soru bekleniyor.
  result,

  /// Yanlıştan sonra "reklamla devam" kararı.
  continueOffer,

  /// Maç bitti (özet ekranına geçilir).
  finished,
}

/// Bir bireysel maçı yürüten istemci tarafı mantığı. Süreyi sunucu ölçer; burada yalnızca sunucu saatine göre
/// geri sayım gösterilir ve süre bitince tek bir "süre doldu" bildirimi gönderilir. Doğru cevap yalnızca
/// cevap sonrası gelen [AnswerOutcome]'dan bilinir.
class GameController extends ChangeNotifier {
  GameController({
    required this.api,
    required this.ads,
    required MatchState initial,
    DateTime Function()? now,
    this.resultHold = const Duration(milliseconds: 2500),
  })  : _now = now ?? DateTime.now,
        _state = initial {
    _syncClock(initial);
  }

  final ApiClient api;
  final RewardedAdService ads;
  final DateTime Function() _now;

  /// Sıradaki soru yoksa (maç bitti / devam teklifi) sonucun ekranda kalma süresi.
  final Duration resultHold;

  MatchState _state;
  Duration _serverOffset = Duration.zero;
  Timer? _ticker;

  AnswerOutcome? outcome;
  QuestionView? _answered;
  DateTime? _resultUntil;
  bool _timeoutSent = false;
  DateTime? _retryAfter;
  bool _forceOver = false;
  bool busy = false;
  String? error;

  /// Seçili şıklar (çift cevapta 2'ye kadar).
  final List<int> selected = [];
  ActiveJoker? activeJoker;
  JokerBalances? balancesOverride;

  MatchState get state => _state;

  /// Sonuç gösterilirken, az önce cevaplanan sorunun metni/şıkları (state artık sıradaki soruyu taşır).
  String? get lastQuestionText => _answered?.text;
  List<String>? get lastQuestionOptions => _answered?.options;
  QuestionView? get question => _state.question;
  JokerBalances get balances => balancesOverride ?? _state.jokers;

  /// Cihaz saati + sunucu farkı = tahmini sunucu saati.
  DateTime get serverNow => _now().toUtc().add(_serverOffset);

  Duration get remaining {
    final q = question;
    if (q == null) return Duration.zero;
    final left = q.endsAt.difference(serverNow);
    return left.isNegative ? Duration.zero : left;
  }

  /// Başlamasına kalan süre (başlamışsa sıfır).
  Duration get untilStart {
    final q = question;
    if (q == null) return Duration.zero;
    final left = q.startsAt.difference(serverNow);
    return left.isNegative ? Duration.zero : left;
  }

  GamePhase get phase {
    if (_forceOver) return GamePhase.finished;
    if (_state.isOver && outcome == null) return GamePhase.finished;
    if (outcome != null) {
      final holding = _resultUntil != null && serverNow.isBefore(_resultUntil!);
      if (holding) return GamePhase.result;
      if (_state.isOver) return GamePhase.finished;
      if (_state.isAwaitingContinue) return GamePhase.continueOffer;
    }
    if (_state.isAwaitingContinue) return GamePhase.continueOffer;
    final q = question;
    if (q == null) return GamePhase.finished;
    return serverNow.isBefore(q.startsAt) ? GamePhase.waiting : GamePhase.answering;
  }

  bool get doubleActive => activeJoker?.type == 'double';
  List<int> get removedOptions => activeJoker?.removed ?? const [];
  int? get hintOption => activeJoker?.hint;
  bool get jokerUsedThisQuestion => activeJoker != null;
  bool canUseJoker(String type) =>
      phase == GamePhase.answering && !busy && !jokerUsedThisQuestion && !_state.usedJokerTypes.contains(type) && balances.of(type) > 0 && outcome == null;

  bool get canContinue => ads.isReady;

  // ---------------------------------------------------------------- Saat

  void _syncClock(MatchState s) {
    _serverOffset = s.serverNow.difference(_now().toUtc());
    activeJoker = s.activeJoker;
    balancesOverride = null;
  }

  /// Ekran yenilemesi için düzenli çağrılır (testlerde elle çağrılır).
  void start() {
    _ticker?.cancel();
    _ticker = Timer.periodic(const Duration(milliseconds: 100), (_) => tick());
  }

  /// Süre bittiyse tek seferlik "süre doldu" bildirimi gönderir; sonuç gösterim süresi dolduysa ekranı ilerletir.
  Future<void> tick() async {
    acknowledgeResult();
    final retryOk = _retryAfter == null || !_now().isBefore(_retryAfter!);
    if (phase == GamePhase.answering && remaining == Duration.zero && !_timeoutSent && !busy && retryOk) {
      await _submit(null, null);
    } else {
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- Cevap

  /// Şık seçimi. Çift cevap açıkken iki şık seçilir, ikincisinde cevap gönderilir; değilse ilk dokunuş kesindir.
  Future<void> select(int index) async {
    if (phase != GamePhase.answering || busy || selected.contains(index) || removedOptions.contains(index)) return;
    selected.add(index);
    notifyListeners();
    if (doubleActive && selected.length < 2) return;
    await _submit(selected.first, selected.length > 1 ? selected[1] : null);
  }

  Future<void> _submit(int? option, int? second) async {
    final q = question;
    if (q == null || busy) return;
    busy = true;
    _answered = q;
    if (option == null) _timeoutSent = true;
    error = null;
    notifyListeners();
    try {
      final result = await api.answer(_state.matchId, q.position, option, second: second);
      outcome = result;
      selected
        ..clear()
        ..addAll([?result.chosenOption, ?result.secondOption]);
      _state = result.state;
      _syncClock(_state);
      activeJoker = null;
      // Sıradaki soru varsa onun başlangıcına kadar, yoksa sabit süre boyunca sonuç gösterilir
      _resultUntil = _state.question?.startsAt ?? serverNow.add(resultHold);
      if (_state.isAwaitingContinue || _state.isOver) _resultUntil = serverNow.add(resultHold);
    } on ApiException catch (e) {
      error = e.message;
      if (option != null) selected.clear();
      if (e.code == 'MATCH_NOT_RUNNING' || e.code == 'WRONG_POSITION') {
        // Sunucu maçı bizden önce bitirmiş (terk/iptal vb.): özet ekranına geç
        _forceOver = true;
      } else if (option == null) {
        // Süre bildirimi ulaşmadı (ağ/erken): kısa beklemeyle yeniden dene
        _timeoutSent = false;
        _retryAfter = _now().add(const Duration(seconds: 2));
      }
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  /// Sonuç ekranı bittikten sonra bir sonraki soruya geçildiğini işaretler (seçimleri temizler).
  void acknowledgeResult() {
    if (phase == GamePhase.result) return;
    if (outcome != null && !_state.isOver && !_state.isAwaitingContinue) {
      outcome = null;
      selected.clear();
      _timeoutSent = false;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- Joker

  Future<void> useJoker(String type) async {
    final q = question;
    if (q == null || !canUseJoker(type)) return;
    busy = true;
    error = null;
    notifyListeners();
    try {
      final result = await api.useJoker(_state.matchId, q.position, type);
      activeJoker = result.joker;
      balancesOverride = result.balances;
    } on ApiException catch (e) {
      error = e.message;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- Devam / pes

  /// Reklam izleyip aynı sıraya yeni (kolay) soruyla devam eder. Reklam hazır değilse hiçbir şey yapmaz.
  Future<bool> continueWithAd() async {
    if (!ads.isReady || busy) return false;
    busy = true;
    error = null;
    notifyListeners();
    try {
      final intent = await ads.show('continue');
      if (intent == null) return false;
      _state = await api.continueMatch(_state.matchId, intent);
      _syncClock(_state);
      outcome = null;
      selected.clear();
      _timeoutSent = false;
      return true;
    } on ApiException catch (e) {
      error = e.message;
      return false;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> declineContinue() async {
    busy = true;
    error = null;
    notifyListeners();
    try {
      _state = await api.declineContinue(_state.matchId);
      _syncClock(_state);
      _resultUntil = serverNow;
    } on ApiException catch (e) {
      error = e.message;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> forfeit() async {
    busy = true;
    error = null;
    notifyListeners();
    try {
      _state = await api.forfeit(_state.matchId);
      _syncClock(_state);
      outcome = null;
    } on ApiException catch (e) {
      error = e.message;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<bool> report(String reason) async {
    // Sonuç ekranındayken bildirilen, az önce cevaplanan sorudur; aksi halde açık olan soru
    final position = phase == GamePhase.result ? outcome?.position : question?.position;
    if (position == null) return false;
    try {
      await api.reportQuestion(_state.matchId, position, reason);
      return true;
    } on ApiException catch (e) {
      error = e.message;
      notifyListeners();
      return false;
    }
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }
}
