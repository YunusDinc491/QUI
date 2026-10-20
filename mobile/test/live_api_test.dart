import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:senbilirsin/ads/rewarded_ad_service.dart';
import 'package:senbilirsin/core/api_client.dart';
import 'package:senbilirsin/game/game_controller.dart';
import 'package:senbilirsin/game/versus_controller.dart';
import 'package:senbilirsin/game/versus_transport.dart';

/// Gerçek yerel sunucuya karşı uçtan uca deneme (varsayılan olarak atlanır).
/// Çalıştırmak için: sunucuyu başlat (geliştirme veritabanında örnek sorular olmalı) ve
/// `flutter test test/live_api_test.dart --dart-define=LIVE_API=http://localhost:5080`.
const _live = String.fromEnvironment('LIVE_API');

void main() {
  final skip = _live.isEmpty ? 'LIVE_API tanımlı değil (yerel sunucu gerekir)' : null;

  test('gerçek sunucuda: kayıt → katalog → bireysel maç → joker → cevap → özet', skip: skip, () async {
    final uid = 'live${Random().nextInt(1 << 30)}';
    final api = ApiClient(
      baseUrl: _live,
      deviceId: 'live-device',
      authHeaderProvider: () async => 'Dev uid=$uid;email=$uid@test.local;verified=true;provider=google.com',
    );

    await api.register(username: 'l${uid.substring(4)}', displayName: 'Canlı', avatarId: 1, birthYear: 2000, termsAccepted: true);
    await api.startSession();

    final catalog = await api.fetchCatalog();
    expect(catalog.map((m) => m.code), ['general', 'branch', 'kpss', 'fan']);
    final branch = catalog.firstWhere((m) => m.code == 'branch');
    final history = branch.categories.firstWhere((c) => c.name == 'Tarih');

    final jokers = await api.fetchJokers();
    expect(jokers.half, 5);

    // Turnuva ve cüzdan uçları gerçek JSON'la
    final tournament = await api.fetchTournament();
    expect(tournament.rewards, hasLength(10));
    expect(tournament.endsAt.isAfter(tournament.serverNow), isTrue);
    expect(tournament.endsAt.difference(tournament.serverNow), lessThanOrEqualTo(const Duration(hours: 48)));
    final previous = await api.fetchPreviousTournament();
    expect(previous.top.length, lessThanOrEqualTo(10));
    final wallet = await api.fetchWallet();
    expect((wallet.balance, wallet.half), (0, 5));
    expect(await api.fetchPointsHistory(), isEmpty);

    final initial = await api.startSolo('branch', history.id);
    expect(initial.total, 10);
    expect(initial.question!.options, hasLength(4));

    final controller = GameController(api: api, ads: StubRewardedAdService(enabled: true), initial: initial);
    // Gerçek saatle çalışır: ilk soruya kadar bekle
    await Future<void>.delayed(controller.untilStart + const Duration(milliseconds: 300));
    expect(controller.phase, GamePhase.answering);

    await controller.useJoker('half');
    expect(controller.removedOptions, hasLength(2));
    expect(controller.balances.half, 4);

    // Elenmemiş bir şık seç (doğru olmayabilir): sunucu sonucu ve doğru şıkkı döner
    final pick = [0, 1, 2, 3].firstWhere((i) => !controller.removedOptions.contains(i));
    await controller.select(pick);
    expect(controller.outcome, isNotNull);
    expect(controller.outcome!.correctOption, inInclusiveRange(0, 3));
    expect(controller.outcome!.explanation == null || controller.outcome!.explanation!.isNotEmpty, isTrue);

    await controller.forfeit();
    final summary = await api.fetchSummary(initial.matchId);
    expect(summary.items, isNotEmpty);
    expect(summary.status, anyOf('forfeited', 'finished'));
    controller.dispose();
  });

  test('gerçek sunucuda: iki oyuncu SignalR (WebSocket) ile rakipli maç — arama, hazır, soru, cevap, pes', skip: skip, () async {
    Future<(ApiClient, String)> newPlayer() async {
      final uid = 'live${Random().nextInt(1 << 30)}';
      final api = ApiClient(
        baseUrl: _live,
        deviceId: 'dev-$uid',
        authHeaderProvider: () async => 'Dev uid=$uid;email=$uid@test.local;verified=true;provider=google.com',
      );
      await api.register(username: 'l${uid.substring(4)}', displayName: 'Canlı ${uid.substring(4, 8)}', avatarId: 1, birthYear: 2000, termsAccepted: true);
      await api.startSession();
      return (api, uid);
    }

    Future<void> until(bool Function() condition, String what, {int ms = 12000}) async {
      final end = DateTime.now().add(Duration(milliseconds: ms));
      while (!condition()) {
        if (DateTime.now().isAfter(end)) fail('Zaman aşımı: $what');
        await Future<void>.delayed(const Duration(milliseconds: 50));
      }
    }

    final (apiA, _) = await newPlayer();
    final (apiB, _) = await newPlayer();
    final catalog = await apiA.fetchCatalog();
    final history = catalog.firstWhere((m) => m.code == 'branch').categories.firstWhere((c) => c.name == 'Tarih');

    VersusController make(ApiClient api) => VersusController(
          transport: SignalRTransport(baseUrl: _live, deviceId: api.deviceId, authHeaderProvider: api.authHeaderProvider),
        );
    final a = make(apiA);
    final b = make(apiB);
    await a.connect();
    await b.connect();

    await a.search('branch', history.id);
    await b.search('branch', history.id);
    await until(() => a.phase == VersusPhase.found && b.phase == VersusPhase.found, 'rakip bulunamadı');
    expect(a.matchId, b.matchId);
    expect(a.opponent!.name, startsWith('Canlı'));

    await a.ready();
    await b.ready();
    await until(() => a.phase == VersusPhase.question && b.phase == VersusPhase.question, 'ilk soru gelmedi');
    expect(a.question!.options, b.question!.options);
    expect(a.question!.options, hasLength(4));

    await a.select(0);
    await b.select(1);
    await until(() => a.lastResult != null && b.lastResult != null, 'sonuç gelmedi');
    expect(a.lastResult!.correctOption, inInclusiveRange(0, 3));
    expect(a.lastResult!.correctOption, b.lastResult!.correctOption);
    expect(a.opponentScore + a.yourScore, lessThanOrEqualTo(1));

    await a.forfeit();
    await until(() => a.phase == VersusPhase.finished && b.phase == VersusPhase.finished, 'bitiş gelmedi');
    expect(b.finish!.result, 'win');
    expect(a.finish!.result, 'lose');
    expect(b.finish!.reason, 'forfeit');

    await a.disconnect();
    await b.disconnect();
  });
}
