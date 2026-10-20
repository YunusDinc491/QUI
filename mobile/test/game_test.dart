import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:senbilirsin/ads/rewarded_ad_service.dart';
import 'package:senbilirsin/core/api_client.dart';
import 'package:senbilirsin/game/game_controller.dart';
import 'package:senbilirsin/game/game_models.dart';
import 'package:senbilirsin/game/game_screen.dart';

/// Sunucunun bireysel maç davranışının küçük bir taklidi (süreleri paylaşılan sahte saatle ölçer).
class FakeMatchServer {
  FakeMatchServer(this.clock, {this.skew = Duration.zero});

  DateTime Function() clock;

  /// Sunucu saatinin cihaz saatinden farkı.
  final Duration skew;

  int position = 1;
  int correct = 0;
  String status = 'running';
  bool continueUsed = false;
  Map<String, int> jokers = {'half': 5, 'double': 5, 'hint': 5};
  final List<String> usedJokers = [];
  Map<String, dynamic>? activeJoker;
  DateTime startsAt = DateTime.utc(2026, 1, 1);
  final List<Map<String, dynamic>> log = [];

  DateTime get serverNow => clock().toUtc().add(skew);

  /// Ekran sırasına göre doğru şık: sıra numarasına bağlı, deterministik.
  int correctFor(int pos) => (pos + (continueUsed ? 1 : 0)) % 4;

  void begin() => startsAt = serverNow.add(const Duration(seconds: 2));

  Map<String, dynamic> state() {
    final running = status == 'running';
    return {
      'matchId': 'm1',
      'mode': 'branch',
      'categoryId': 5,
      'status': status,
      'position': position,
      'totalQuestions': 10,
      'correctCount': correct,
      'question': running
          ? {
              'position': position,
              'totalQuestions': 10,
              'text': 'Soru $position metni',
              'options': ['A$position', 'B$position', 'C$position', 'D$position'],
              'startsAt': startsAt.toIso8601String(),
              'endsAt': startsAt.add(const Duration(seconds: 15)).toIso8601String(),
            }
          : null,
      'activeJoker': activeJoker,
      'usedJokerTypes': usedJokers,
      'jokers': jokers,
      'continueOffer': status == 'awaiting_continue' ? {'deadline': serverNow.add(const Duration(seconds: 45)).toIso8601String()} : null,
      'result': ['running', 'awaiting_continue'].contains(status) ? null : {'status': status, 'reason': 'eliminated', 'correctCount': correct, 'points': 0, 'settled': true},
      'serverNow': serverNow.toIso8601String(),
    };
  }

  http.Response _json(Object body, [int status = 200]) =>
      http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json; charset=utf-8'});

  Future<http.Response> handle(http.Request r) async {
    final path = r.url.path;
    final body = r.body.isEmpty ? <String, dynamic>{} : jsonDecode(r.body) as Map<String, dynamic>;
    log.add({'path': path, ...body});

    if (path == '/api/matches/solo') {
      begin();
      return _json(state(), 201);
    }
    if (path == '/api/matches/current') return status == 'running' || status == 'awaiting_continue' ? _json(state()) : _json({'code': 'NO_ACTIVE_MATCH', 'message': 'yok'}, 404);
    if (path.endsWith('/answer')) {
      final option = body['option'] as int?;
      final second = body['second'] as int?;
      final timedOut = option == null;
      final isCorrect = !timedOut && (option == correctFor(position) || second == correctFor(position));
      final answered = position;
      if (isCorrect) {
        correct++;
        if (position == 10) {
          status = 'finished';
        } else {
          position++;
          startsAt = serverNow.add(const Duration(seconds: 3));
        }
      } else {
        status = continueUsed ? 'finished' : 'awaiting_continue';
      }
      activeJoker = null;
      return _json({
        'position': answered, 'isCorrect': isCorrect, 'timedOut': timedOut, 'correctOption': (answered + (continueUsed ? 1 : 0)) % 4,
        'chosenOption': option, 'secondOption': second, 'explanation': 'Açıklama', 'state': state(),
      });
    }
    if (path.endsWith('/joker')) {
      final type = body['type'] as String;
      jokers[type] = jokers[type]! - 1;
      usedJokers.add(type);
      final c = correctFor(position);
      activeJoker = switch (type) {
        'half' => {'type': 'half', 'removed': [for (var i = 0; i < 4; i++) if (i != c) i].take(2).toList(), 'hint': null},
        'hint' => {'type': 'hint', 'removed': null, 'hint': c},
        _ => {'type': 'double', 'removed': null, 'hint': null},
      };
      return _json({'joker': activeJoker, 'jokers': jokers});
    }
    if (path.endsWith('/continue')) {
      if (!(body['adIntentId'] as String).startsWith('test-')) return _json({'code': 'AD_NOT_VERIFIED', 'message': 'Reklam doğrulanamadı.'}, 403);
      continueUsed = true;
      status = 'running';
      begin();
      return _json(state());
    }
    if (path.endsWith('/decline')) {
      status = 'finished';
      return _json(state());
    }
    if (path.endsWith('/forfeit')) {
      status = 'forfeited';
      return _json(state());
    }
    if (path.endsWith('/summary')) {
      return _json({'matchId': 'm1', 'mode': 'branch', 'categoryId': 5, 'status': status, 'reason': 'eliminated', 'correctCount': correct, 'points': 0, 'items': []});
    }
    return _json({'code': 'NOT_FOUND', 'message': 'x'}, 404);
  }
}

class TestClock {
  DateTime now = DateTime.utc(2026, 1, 1, 12);
  void advance(Duration d) => now = now.add(d);
}

Future<(GameController, FakeMatchServer, TestClock)> newGame({RewardedAdService? ads, Duration skew = Duration.zero}) async {
  final clock = TestClock();
  final server = FakeMatchServer(() => clock.now, skew: skew);
  final api = ApiClient(baseUrl: 'http://fake', deviceId: 'd', authHeaderProvider: () async => 'Dev uid=x', httpClient: MockClient(server.handle));
  final initial = await api.startSolo('branch', 5);
  final controller = GameController(api: api, ads: ads ?? StubRewardedAdService(enabled: true), initial: initial, now: () => clock.now);
  return (controller, server, clock);
}

void main() {
  test('başlangıçta "hazırlanıyor", başlangıç anı gelince "cevaplanıyor" aşaması', () async {
    final (c, _, clock) = await newGame();
    expect(c.phase, GamePhase.waiting);
    expect(c.untilStart, const Duration(seconds: 2));

    clock.advance(const Duration(seconds: 2));
    expect(c.phase, GamePhase.answering);
    expect(c.remaining, const Duration(seconds: 15));
    clock.advance(const Duration(seconds: 6));
    expect(c.remaining, const Duration(seconds: 9));
  });

  test('geri sayım sunucu saatine göre hesaplanır (cihaz saati geride olsa bile)', () async {
    final (c, _, clock) = await newGame(skew: const Duration(seconds: 5)); // sunucu 5 sn ileride
    // Sunucuya göre soru 2 sn sonra başlıyor; cihaz farkı öğrenmiş olmalı
    expect(c.untilStart, const Duration(seconds: 2));
    clock.advance(const Duration(seconds: 2));
    expect(c.phase, GamePhase.answering);
    expect(c.remaining, const Duration(seconds: 15));
  });

  test('doğru cevap: sonuç aşaması, sonra sıradaki soru', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));

    await c.select(server.correctFor(1));
    expect(c.outcome!.isCorrect, isTrue);
    expect(c.state.correctCount, 1);
    expect(c.state.position, 2);
    expect(c.phase, GamePhase.result);
    expect(c.lastQuestionText, 'Soru 1 metni'); // sonuç ekranında az önce cevaplanan soru

    // Sıradaki soru 3 sn sonra başlar
    clock.advance(const Duration(seconds: 3));
    await c.tick();
    expect(c.outcome, isNull);
    expect(c.selected, isEmpty);
    expect(c.phase, GamePhase.answering);
    expect(c.question!.text, 'Soru 2 metni');
  });

  test('ilk dokunuş kesin: ikinci dokunuş yok sayılır', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));
    final wrong = (server.correctFor(1) + 1) % 4;

    await c.select(wrong);
    await c.select(server.correctFor(1));

    expect(server.log.where((l) => l['path'].toString().endsWith('/answer')), hasLength(1));
    expect(c.outcome!.isCorrect, isFalse);
  });

  test('süre dolunca tek bir "süre doldu" bildirimi gider ve devam teklifi çıkar', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2 + 15));
    expect(c.remaining, Duration.zero);

    await c.tick();
    await c.tick(); // ikinci tick yeni istek göndermemeli

    final answers = server.log.where((l) => l['path'].toString().endsWith('/answer')).toList();
    expect(answers, hasLength(1));
    expect(answers.first['option'], isNull);
    expect(c.outcome!.timedOut, isTrue);

    clock.advance(const Duration(seconds: 3));
    expect(c.phase, GamePhase.continueOffer);
  });

  test('yanlışta reklamla devam: aynı sıra, yeni soru; reklam hazır değilse teklif yok', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));
    await c.select((server.correctFor(1) + 1) % 4);
    clock.advance(const Duration(seconds: 3));
    expect(c.phase, GamePhase.continueOffer);
    expect(c.canContinue, isTrue);

    expect(await c.continueWithAd(), isTrue);
    expect(c.state.isRunning, isTrue);
    expect(c.outcome, isNull);
    expect(c.state.position, 1);

    final (c2, server2, clock2) = await newGame(ads: StubRewardedAdService(enabled: false));
    clock2.advance(const Duration(seconds: 2));
    await c2.select((server2.correctFor(1) + 1) % 4);
    expect(c2.canContinue, isFalse);
    expect(await c2.continueWithAd(), isFalse);
    expect(server2.log.where((l) => l['path'].toString().endsWith('/continue')), isEmpty);
  });

  test('devam teklifini reddetmek maçı bitirir', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));
    await c.select((server.correctFor(1) + 1) % 4);
    clock.advance(const Duration(seconds: 3));

    await c.declineContinue();
    expect(c.state.status, 'finished');
    expect(c.phase, GamePhase.finished);
  });

  test('Yarı yarıya: elenen şıklar seçilemez, bakiye düşer, soru başına bir joker', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));
    expect(c.canUseJoker('half'), isTrue);

    await c.useJoker('half');
    expect(c.removedOptions, hasLength(2));
    expect(c.balances.half, 4);
    expect(c.canUseJoker('hint'), isFalse); // aynı soruda ikinci joker yok
    expect(c.canUseJoker('half'), isFalse);

    final removed = c.removedOptions.first;
    await c.select(removed);
    expect(server.log.where((l) => l['path'].toString().endsWith('/answer')), isEmpty);

    await c.select(server.correctFor(1));
    clock.advance(const Duration(seconds: 3));
    await c.tick();
    // Sonraki soruda joker yeniden kullanılabilir (ama aynı tür değil)
    expect(c.canUseJoker('half'), isFalse);
    expect(c.canUseJoker('hint'), isTrue);
    expect(c.removedOptions, isEmpty);
  });

  test('İpucu seçili şıkkı işaretler', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));
    await c.useJoker('hint');
    expect(c.hintOption, server.correctFor(1));
  });

  test('Çift cevap: iki şık seçilince tek istekte gönderilir', () async {
    final (c, server, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));
    await c.useJoker('double');
    expect(c.doubleActive, isTrue);

    final wrong = (server.correctFor(1) + 1) % 4;
    await c.select(wrong);
    expect(server.log.where((l) => l['path'].toString().endsWith('/answer')), isEmpty); // henüz bekliyor

    await c.select(server.correctFor(1));
    final answer = server.log.singleWhere((l) => l['path'].toString().endsWith('/answer'));
    expect(answer['option'], wrong);
    expect(answer['second'], server.correctFor(1));
    expect(c.outcome!.isCorrect, isTrue);
  });

  test('bakiye sıfırsa joker kullanılamaz', () async {
    final (c, server, clock) = await newGame();
    server.jokers['hint'] = 0;
    clock.advance(const Duration(seconds: 2));
    c.balancesOverride = const JokerBalances(half: 5, doubleAnswer: 5, hint: 0);
    expect(c.canUseJoker('hint'), isFalse);
  });

  test('pes maçı bitirir', () async {
    final (c, _, clock) = await newGame();
    clock.advance(const Duration(seconds: 2));
    await c.forfeit();
    expect(c.state.status, 'forfeited');
    expect(c.phase, GamePhase.finished);
  });

  test('sunucu maçı bitirmişse (MATCH_NOT_RUNNING) ekran özet aşamasına geçer', () async {
    final (c, _, clock) = await newGame();
    // Sunucu bu cevaba "maç artık oynanmıyor" diyor (örn. terk nedeniyle iptal edilmiş)
    final api = ApiClient(
      baseUrl: 'http://fake',
      deviceId: 'd',
      authHeaderProvider: () async => 'x',
      httpClient: MockClient((r) async => http.Response(jsonEncode({'code': 'MATCH_NOT_RUNNING', 'message': 'x'}), 409)),
    );
    final broken = GameController(api: api, ads: StubRewardedAdService(enabled: true), initial: c.state, now: () => clock.now);
    clock.advance(const Duration(seconds: 2));

    await broken.select(0);
    expect(broken.phase, GamePhase.finished);
  });

  test('dev derlemesi dışında sahte reklam hazır sayılmaz', () async {
    final disabled = StubRewardedAdService(enabled: false);
    expect(disabled.isReady, isFalse);
    expect(await disabled.show('continue'), isNull);
    final enabled = StubRewardedAdService(enabled: true);
    final first = await enabled.show('continue');
    final second = await enabled.show('continue');
    expect(first, startsWith('test-'));
    expect(first, isNot(second)); // her niyet benzersiz
  });

  testWidgets('oyun ekranı: soru görünür, şık seçilince sonuç gösterilir', (tester) async {
    final clock = TestClock();
    final server = FakeMatchServer(() => clock.now);
    final api = ApiClient(baseUrl: 'http://fake', deviceId: 'd', authHeaderProvider: () async => 'x', httpClient: MockClient(server.handle));
    final initial = await api.startSolo('branch', 5);
    final controller = GameController(api: api, ads: StubRewardedAdService(enabled: true), initial: initial, now: () => clock.now);

    await tester.pumpWidget(MaterialApp(home: GameScreen(controller: controller)));
    expect(find.byKey(const Key('question-text')), findsOneWidget);
    expect(find.text('Soru 1 metni'), findsOneWidget);
    expect(find.byKey(const Key('waiting-label')), findsOneWidget);

    clock.advance(const Duration(seconds: 2));
    await tester.pump(const Duration(milliseconds: 200)); // zamanlayıcı bir tick atar
    expect(find.byKey(const Key('waiting-label')), findsNothing);

    await tester.tap(find.byKey(Key('option-${server.correctFor(1)}')));
    await tester.pump();
    await tester.pump();
    expect(find.byKey(const Key('result-label')), findsOneWidget);
    expect(find.text('Doğru!'), findsOneWidget);
    expect(find.text('Açıklama'), findsOneWidget);
    expect(find.byKey(const Key('game-correct')), findsOneWidget);

    // Widget ağacını kaldır (zamanlayıcıyı durdurur)
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('devam teklifi ekranı: reklam hazırsa buton görünür; hazır değilse yalnızca "Maçı bitir"', (tester) async {
    for (final adsReady in [true, false]) {
      final clock = TestClock();
      final server = FakeMatchServer(() => clock.now);
      final api = ApiClient(baseUrl: 'http://fake', deviceId: 'd', authHeaderProvider: () async => 'x', httpClient: MockClient(server.handle));
      final initial = await api.startSolo('branch', 5);
      final controller = GameController(api: api, ads: StubRewardedAdService(enabled: adsReady), initial: initial, now: () => clock.now);

      await tester.pumpWidget(MaterialApp(home: GameScreen(controller: controller)));
      clock.advance(const Duration(seconds: 2));
      await tester.pump(const Duration(milliseconds: 200));
      await controller.select((server.correctFor(1) + 1) % 4);
      clock.advance(const Duration(seconds: 3));
      await tester.pump(const Duration(milliseconds: 200));
      await tester.pump();

      expect(find.byKey(const Key('continue-decline')), findsOneWidget);
      expect(find.byKey(const Key('continue-ad')), adsReady ? findsOneWidget : findsNothing);
      await tester.pumpWidget(const SizedBox());
    }
  });
}
