import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/testing.dart';
import 'package:senbilirsin/ads/rewarded_ad_service.dart';
import 'package:senbilirsin/core/api_client.dart';
import 'package:senbilirsin/game/versus_controller.dart';
import 'package:senbilirsin/game/versus_screen.dart';
import 'package:senbilirsin/game/versus_transport.dart';

/// Sahte hub bağlantısı: sunucu olaylarını elle yayınlar, çağrıları kaydeder.
class FakeTransport implements VersusTransport {
  final Map<String, void Function(Map<String, dynamic>)> _handlers = {};
  void Function(bool)? _connection;
  final List<(String, List<Object?>)> calls = [];
  final Map<String, Map<String, dynamic>> responses = {};
  bool throwOnInvoke = false;

  /// Çağrı yapılırken (yanıt dönmeden önce) çalıştırılır: sunucunun yanıttan önce olay göndermesini taklit eder.
  void Function(String method)? onInvoke;
  bool started = false;

  @override
  Future<void> start() async => started = true;

  @override
  Future<void> stop() async => started = false;

  @override
  Future<Map<String, dynamic>> invoke(String method, [List<Object?> args = const []]) async {
    calls.add((method, args));
    onInvoke?.call(method);
    if (throwOnInvoke) throw Exception('offline');
    return responses[method] ?? {'ok': true};
  }

  @override
  void on(String event, void Function(Map<String, dynamic> payload) handler) => _handlers[event] = handler;

  @override
  void onConnectionChanged(void Function(bool connected) handler) => _connection = handler;

  void emit(String event, Map<String, dynamic> payload) => _handlers[event]!(payload);

  void setConnected(bool value) => _connection!(value);

  List<(String, List<Object?>)> callsOf(String method) => calls.where((c) => c.$1 == method).toList();
}

class TestClock {
  DateTime now = DateTime.utc(2026, 1, 1, 12);
  void advance(Duration d) => now = now.add(d);
}

Map<String, dynamic> found({bool needsReady = true, bool bot = false}) => {
      'matchId': 'm1',
      'kind': 'random',
      'needsReady': needsReady,
      'opponent': {'id': null, 'name': bot ? 'Dengeli Bot' : 'Berk', 'avatarId': 2, 'isBot': bot},
    };

Map<String, dynamic> question(TestClock clock, {int position = 1, int seconds = 15, Map<String, dynamic>? extra}) => {
      'matchId': 'm1',
      'position': position,
      'total': 10,
      'text': 'Soru $position metni',
      'options': ['Bir', 'İki', 'Üç', 'Dört'],
      'startsAt': clock.now.toIso8601String(),
      'endsAt': clock.now.add(Duration(seconds: seconds)).toIso8601String(),
      'serverNow': clock.now.toIso8601String(),
      'usedJokerTypes': <String>[],
      'jokers': {'half': 5, 'double': 5, 'hint': 5},
      'activeJoker': null,
      'yourScore': 0,
      'opponentScore': 0,
      ...?extra,
    };

Map<String, dynamic> result({int position = 1, bool you = true, bool opp = false, int yourScore = 1, int oppScore = 0}) => {
      'position': position,
      'correctOption': 2,
      'explanation': 'Açıklama',
      'yourOption': you ? 2 : 0,
      'yourSecond': null,
      'yourCorrect': you,
      'yourTimedOut': false,
      'opponentOption': opp ? 2 : 1,
      'opponentCorrect': opp,
      'yourScore': yourScore,
      'opponentScore': oppScore,
      'serverNow': DateTime.utc(2026, 1, 1, 12).toIso8601String(),
    };

(VersusController, FakeTransport, TestClock) newController() {
  final clock = TestClock();
  final transport = FakeTransport();
  final c = VersusController(transport: transport, now: () => clock.now);
  return (c, transport, clock);
}

void main() {
  main2();
  test('arama: JoinQueue çağrılır; 15 sn dolunca seçenekler; tekrar ara ve bot seçimi', () async {
    final (c, t, _) = newController();
    await c.connect();

    await c.search('branch', 100);
    expect(t.callsOf('JoinQueue').single.$2, ['branch', 100]);
    expect(c.phase, VersusPhase.searching);

    t.emit('QueueTimeout', {'options': ['bot', 'solo', 'retry']});
    expect(c.phase, VersusPhase.searchTimedOut);

    await c.chooseFallback('retry');
    expect(c.phase, VersusPhase.searching);

    t.emit('QueueTimeout', {});
    await c.chooseFallback('bot', botId: 'b1');
    expect(t.callsOf('ChooseFallback').last.$2, ['bot', 'b1']);
    expect(c.pendingMode, ('branch', 100));
  });

  test('rakip bulunur: hazır ol bir kez gönderilir; geri sayım ve soru akışı sunucu saatiyle', () async {
    final (c, t, clock) = newController();
    await c.connect();

    t.emit('MatchFound', found());
    expect(c.phase, VersusPhase.found);
    expect(c.opponent!.name, 'Berk');
    expect(c.opponent!.isBot, isFalse);

    await c.ready();
    await c.ready();
    expect(t.callsOf('Ready'), hasLength(1));
    expect(t.callsOf('Ready').single.$2, ['m1']);

    t.emit('CountdownStarted', {'startsAt': clock.now.add(const Duration(seconds: 5)).toIso8601String(), 'serverNow': clock.now.toIso8601String()});
    expect(c.phase, VersusPhase.countdown);

    clock.advance(const Duration(seconds: 5));
    t.emit('QuestionStarted', question(clock));
    expect(c.phase, VersusPhase.question);
    expect(c.question!.options, ['Bir', 'İki', 'Üç', 'Dört']);
    expect(c.remaining, const Duration(seconds: 15));
    clock.advance(const Duration(seconds: 4));
    expect(c.remaining, const Duration(seconds: 11));
  });

  test('geri sayım, cihaz saati sunucudan farklı olsa da sunucu saatine göre hesaplanır', () async {
    final (c, t, clock) = newController();
    await c.connect();
    // Sunucu 7 sn ileride
    final server = clock.now.add(const Duration(seconds: 7));
    t.emit('QuestionStarted', {
      ...question(clock),
      'serverNow': server.toIso8601String(),
      'endsAt': server.add(const Duration(seconds: 15)).toIso8601String(),
    });
    expect(c.remaining, const Duration(seconds: 15));
  });

  test('cevap: ilk dokunuş kesin; rakip cevapladı işareti; sonuç ve skor; sıradaki soru seçimleri sıfırlar', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('MatchFound', found(needsReady: false));
    t.emit('QuestionStarted', question(clock));

    await c.select(2);
    await c.select(0); // ikinci dokunuş yok sayılır
    expect(t.callsOf('SubmitAnswer'), hasLength(1));
    expect(t.callsOf('SubmitAnswer').single.$2, ['m1', 1, 2, null]);
    expect(c.answered, isTrue);

    t.emit('OpponentAnswered', {'position': 1});
    expect(c.opponentAnswered, isTrue);

    t.emit('QuestionResult', result());
    expect(c.phase, VersusPhase.result);
    expect(c.lastResult!.correctOption, 2);
    expect(c.lastResult!.yourCorrect, isTrue);
    expect(c.yourScore, 1);

    clock.advance(const Duration(seconds: 3));
    t.emit('QuestionStarted', question(clock, position: 2, extra: {'yourScore': 1}));
    expect(c.phase, VersusPhase.question);
    expect(c.selected, isEmpty);
    expect(c.opponentAnswered, isFalse);
    expect(c.lastResult, isNull);
  });

  test('cevap reddedilirse hata gösterilir ve seçim temizlenir', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('QuestionStarted', question(clock));
    t.responses['SubmitAnswer'] = {'ok': false, 'code': 'WRONG_POSITION', 'message': 'Sıradaki soru bu değil.'};

    await c.select(1);
    expect(c.error, 'Sıradaki soru bu değil.');
    expect(c.selected, isEmpty);
  });

  test('sunucuya ulaşılamazsa anlaşılır hata', () async {
    final (c, t, _) = newController();
    await c.connect();
    t.throwOnInvoke = true;
    await c.search('general', 1);
    expect(c.error, 'Sunucuya ulaşılamadı.');
    expect(c.phase, VersusPhase.idle);
  });

  test('Yarı yarıya: elenen şık seçilemez, bakiye düşer, sonra başka joker yok', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('QuestionStarted', question(clock));
    expect(c.canUseJoker('half'), isTrue);

    await c.useJoker('half');
    expect(t.callsOf('UseJoker').single.$2, ['m1', 1, 'half']);
    t.emit('JokerResult', {'position': 1, 'joker': {'type': 'half', 'removed': [0, 3], 'hint': null}, 'jokers': {'half': 4, 'double': 5, 'hint': 5}});
    expect(c.removedOptions, [0, 3]);
    expect(c.jokers!.half, 4);
    expect(c.canUseJoker('hint'), isFalse);

    await c.select(0);
    expect(t.callsOf('SubmitAnswer'), isEmpty);
    await c.select(2);
    expect(t.callsOf('SubmitAnswer'), hasLength(1));
  });

  test('Çift cevap: iki şık seçilince tek istekte gider', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('QuestionStarted', question(clock));
    t.emit('JokerResult', {'position': 1, 'joker': {'type': 'double', 'removed': null, 'hint': null}, 'jokers': {'half': 5, 'double': 4, 'hint': 5}});

    await c.select(0);
    expect(t.callsOf('SubmitAnswer'), isEmpty);
    await c.select(2);
    expect(t.callsOf('SubmitAnswer').single.$2, ['m1', 1, 0, 2]);
  });

  test('bitiş ve iptal olayları', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('QuestionStarted', question(clock));
    t.emit('MatchFinished', {'result': 'win', 'points': 15, 'bonus': 5, 'reason': 'normal', 'yourCorrect': 9, 'opponentCorrect': 7});
    expect(c.phase, VersusPhase.finished);
    expect(c.finish!.points, 15);
    expect(c.finish!.bonus, 5);

    final (c2, t2, _) = newController();
    await c2.connect();
    t2.emit('MatchCancelled', {'reason': 'both_disconnected'});
    expect(c2.phase, VersusPhase.cancelled);
    expect(c2.cancelReason, 'both_disconnected');
  });

  test('rakibin kopması ve dönüşü bildirilir', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('QuestionStarted', question(clock));
    t.emit('OpponentDisconnected', {'remainingMs': 30000});
    expect(c.opponentDisconnectedMs, 30000);
    t.emit('OpponentReconnected', {});
    expect(c.opponentDisconnectedMs, isNull);
  });

  test('yeniden bağlanınca tam durum özeti açık soruyu geri getirir', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('MatchState', {
      'matchId': 'm1', 'phase': 'question', 'paused': false, 'position': 3, 'total': 10, 'yourScore': 2, 'opponentScore': 1,
      'serverNow': clock.now.toIso8601String(),
      'opponent': {'name': 'Berk', 'avatarId': 2, 'frameId': null, 'isBot': false},
      'question': {
        'position': 3, 'text': 'Soru 3 metni', 'options': ['a', 'b', 'c', 'd'],
        'startsAt': clock.now.subtract(const Duration(seconds: 6)).toIso8601String(),
        'endsAt': clock.now.add(const Duration(seconds: 9)).toIso8601String(), 'answered': false, 'activeJoker': null,
      },
      'usedJokerTypes': ['half'],
    });

    expect(c.phase, VersusPhase.question);
    expect(c.question!.position, 3);
    expect(c.remaining, const Duration(seconds: 9));
    expect(c.yourScore, 2);
    expect(c.usedJokerTypes, ['half']);
    expect(c.answered, isFalse);
  });

  test('arka plan/ön plan sinyalleri yalnızca aktif maçta gider; dönüşte durum özeti uygulanır', () async {
    final (c, t, clock) = newController();
    await c.connect();
    await c.appBackgrounded();
    expect(t.callsOf('AppBackgrounded'), isEmpty); // boştayken gönderilmez

    t.emit('QuestionStarted', question(clock));
    await c.appBackgrounded();
    expect(t.callsOf('AppBackgrounded'), hasLength(1));

    t.responses['AppForegrounded'] = {
      'ok': true,
      'data': {
        'matchId': 'm1', 'phase': 'between', 'paused': false, 'position': 1, 'total': 10, 'yourScore': 1, 'opponentScore': 0,
        'serverNow': clock.now.toIso8601String(), 'opponent': {'name': 'Berk', 'avatarId': 2, 'isBot': false}, 'question': null, 'usedJokerTypes': [],
      },
    };
    await c.appForegrounded();
    expect(c.phase, VersusPhase.result);
    expect(c.yourScore, 1);
  });

  test('arkadaş odası: kur, kod görünür, rakip gelince maç; oda kapanırsa iptal', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.responses['CreateRoom'] = {'ok': true, 'data': {'code': 'ABC234', 'expiresAt': clock.now.add(const Duration(minutes: 5)).toIso8601String()}};

    await c.createRoom('branch', 100);
    expect(c.phase, VersusPhase.roomWaiting);
    expect(c.roomCode, 'ABC234');

    t.emit('OpponentJoinedRoom', {'opponent': {'name': 'Berk', 'avatarId': 2}});
    expect(c.opponent!.name, 'Berk');

    t.emit('RoomClosed', {'reason': 'expired'});
    expect(c.phase, VersusPhase.cancelled);

    expect(await c.joinRoom('XYZ789'), isTrue);
    expect(t.callsOf('JoinRoom').single.$2, ['XYZ789']);
  });

  test('başka cihazdan giriş (SessionClosed) maçı iptal eder', () async {
    final (c, t, _) = newController();
    await c.connect();
    t.emit('SessionClosed', {'reason': 'new_connection'});
    expect(c.phase, VersusPhase.cancelled);
    expect(c.cancelReason, 'session_closed');
  });

  testWidgets('rakipli maç ekranı: arama → rakip bulundu → hazırım → soru → cevap → sonuç → bitiş', (tester) async {
    final clock = TestClock();
    final transport = FakeTransport();
    final controller = VersusController(transport: transport, now: () => clock.now);
    final api = ApiClient(baseUrl: 'http://fake', deviceId: 'd', authHeaderProvider: () async => 'x', httpClient: MockClient((r) async => throw Exception('kullanılmaz')));

    await tester.pumpWidget(MaterialApp(
      home: VersusScreen(controller: controller, api: api, ads: StubRewardedAdService(enabled: true), start: (c) => c.search('branch', 100)),
    ));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.byKey(const Key('search-label')), findsOneWidget);
    expect(transport.callsOf('JoinQueue'), hasLength(1));

    transport.emit('MatchFound', found());
    await tester.pump();
    expect(find.byKey(const Key('found-opponent')), findsOneWidget);
    await tester.tap(find.byKey(const Key('ready-button')));
    await tester.pump();
    expect(transport.callsOf('Ready'), hasLength(1));

    transport.emit('CountdownStarted', {'startsAt': clock.now.add(const Duration(seconds: 5)).toIso8601String(), 'serverNow': clock.now.toIso8601String()});
    await tester.pump();
    expect(find.byKey(const Key('countdown-number')), findsOneWidget);

    clock.advance(const Duration(seconds: 5));
    transport.emit('QuestionStarted', question(clock));
    await tester.pump();
    expect(find.text('Soru 1 metni'), findsOneWidget);
    expect(find.byKey(const Key('versus-timer')), findsOneWidget);

    await tester.tap(find.byKey(const Key('versus-option-2')));
    await tester.pump();
    expect(transport.callsOf('SubmitAnswer').single.$2, ['m1', 1, 2, null]);

    transport.emit('OpponentDisconnected', {'remainingMs': 30000});
    await tester.pump();
    expect(find.byKey(const Key('opponent-disconnected')), findsOneWidget);

    transport.emit('QuestionResult', result());
    await tester.pump();
    expect(find.text('Doğru!'), findsOneWidget);
    expect(find.byKey(const Key('versus-score')), findsOneWidget);

    transport.emit('MatchFinished', {'result': 'win', 'points': 15, 'bonus': 5, 'reason': 'normal', 'yourCorrect': 9, 'opponentCorrect': 7});
    await tester.pump();
    expect(find.text('Kazandın!'), findsOneWidget);
    expect(find.text('15 puan (bonus +5)'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('kuyruk zaman aşımı ekranı üç seçenek sunar', (tester) async {
    final clock = TestClock();
    final transport = FakeTransport();
    final controller = VersusController(transport: transport, now: () => clock.now);
    final api = ApiClient(baseUrl: 'http://fake', deviceId: 'd', authHeaderProvider: () async => 'x', httpClient: MockClient((r) async => throw Exception('x')));

    await tester.pumpWidget(MaterialApp(
      home: VersusScreen(controller: controller, api: api, ads: StubRewardedAdService(enabled: true), start: (c) => c.search('branch', 100)),
    ));
    await tester.pump(const Duration(milliseconds: 300));
    transport.emit('QueueTimeout', {});
    await tester.pump();

    expect(find.byKey(const Key('fallback-bot')), findsOneWidget);
    expect(find.byKey(const Key('fallback-solo')), findsOneWidget);
    expect(find.byKey(const Key('fallback-retry')), findsOneWidget);
    await tester.tap(find.byKey(const Key('fallback-retry')));
    await tester.pump();
    expect(transport.callsOf('ChooseFallback').single.$2, ['retry', null]);

    await tester.pumpWidget(const SizedBox());
  });
}

void main2() {
  test('rakip zaten bekliyorsa MatchFound, JoinQueue yanıtından önce gelse de "bulundu" aşaması ezilmez', () async {
    final (c, t, _) = newController();
    await c.connect();
    t.onInvoke = (method) {
      if (method == 'JoinQueue') t.emit('MatchFound', found());
    };

    await c.search('branch', 100);

    expect(c.phase, VersusPhase.found);
  });

  test('rövanş: istek gider, rakip kabul ederse yeni maç; reddederse mesaj; gelen istek kabul/ret edilir', () async {
    final (c, t, clock) = newController();
    await c.connect();
    t.emit('MatchFound', found(needsReady: false));
    t.emit('QuestionStarted', question(clock));
    t.emit('MatchFinished', {'result': 'lose', 'points': 0, 'bonus': 0, 'reason': 'normal', 'yourCorrect': 3, 'opponentCorrect': 7});

    await c.requestRematch();
    await c.requestRematch(); // ikinci istek yok sayılır
    expect(t.callsOf('RequestRematch'), hasLength(1));
    expect(c.rematchRequested, isTrue);

    t.emit('RematchResult', {'matchId': 'm1', 'accepted': false, 'reason': 'declined'});
    expect(c.rematchRequested, isFalse);
    expect(c.rematchMessage, 'Rakip rövanşı kabul etmedi.');

    t.emit('RematchRequested', {'matchId': 'm1'});
    expect(c.rematchIncoming, isTrue);
    await c.respondRematch(true);
    expect(t.callsOf('RespondRematch').single.$2, ['m1', true]);
    expect(c.rematchIncoming, isFalse);

    // Yeni maç başlayınca rövanş durumu sıfırlanır
    t.emit('MatchFound', found(needsReady: false));
    expect(c.phase, VersusPhase.countdown);
    expect(c.finish, isNull);
    expect(c.rematchMessage, isNull);
  });

  testWidgets('bitiş ekranında Rövanş butonu ve gelen istek', (tester) async {
    final clock = TestClock();
    final transport = FakeTransport();
    final controller = VersusController(transport: transport, now: () => clock.now);
    final api = ApiClient(baseUrl: 'http://fake', deviceId: 'd', authHeaderProvider: () async => 'x', httpClient: MockClient((r) async => throw Exception('x')));

    await tester.pumpWidget(MaterialApp(
      home: VersusScreen(controller: controller, api: api, ads: StubRewardedAdService(enabled: true), start: (c) async {}),
    ));
    await tester.pump(const Duration(milliseconds: 300));
    transport.emit('MatchFound', found(needsReady: false));
    transport.emit('MatchFinished', {'result': 'draw', 'points': 0, 'bonus': 0, 'reason': 'normal', 'yourCorrect': 5, 'opponentCorrect': 5});
    await tester.pump();

    expect(find.text('Berabere'), findsOneWidget);
    await tester.tap(find.byKey(const Key('rematch-button')));
    await tester.pump();
    expect(transport.callsOf('RequestRematch'), hasLength(1));

    transport.emit('RematchRequested', {'matchId': 'm1'});
    await tester.pump();
    expect(find.byKey(const Key('rematch-incoming')), findsOneWidget);
    await tester.tap(find.byKey(const Key('rematch-decline')));
    await tester.pump();
    expect(transport.callsOf('RespondRematch').single.$2, ['m1', false]);

    await tester.pumpWidget(const SizedBox());
  });
}
