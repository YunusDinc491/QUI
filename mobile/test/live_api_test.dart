import 'dart:math';

import 'package:flutter_test/flutter_test.dart';
import 'package:senbilirsin/ads/rewarded_ad_service.dart';
import 'package:senbilirsin/core/api_client.dart';
import 'package:senbilirsin/game/game_controller.dart';

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
}
