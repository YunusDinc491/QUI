import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:senbilirsin/app/app_controller.dart';
import 'package:senbilirsin/auth/dev_auth_gateway.dart';
import 'package:senbilirsin/core/api_client.dart';
import 'package:senbilirsin/game/tournament_models.dart';
import 'package:senbilirsin/screens/tournament_tab.dart';
import 'package:senbilirsin/screens/wallet_screen.dart';

Map<String, dynamic> row(int rank, String name, int points, {bool me = false, int reward = 0}) =>
    {'rank': rank, 'userId': 'u$rank', 'name': name, 'avatarId': 1, 'frameId': null, 'points': points, 'isMe': me, 'reward': reward};

Map<String, dynamic> currentJson({bool withMe = true, List<Map<String, dynamic>>? top}) => {
      'periodId': 7,
      'startsAt': '2026-10-20T21:00:00Z',
      'endsAt': DateTime.now().toUtc().add(const Duration(hours: 5, minutes: 30)).toIso8601String(),
      'serverNow': DateTime.now().toUtc().toIso8601String(),
      'rewards': [
        {'rankFrom': 1, 'rankTo': 1, 'amount': 100},
        {'rankFrom': 2, 'rankTo': 3, 'amount': 60},
      ],
      'top': top ?? [row(1, 'Zeynep', 120, reward: 100), row(2, 'Ali', 80, me: true, reward: 60)],
      'me': withMe ? {'rank': 2, 'points': 80, 'reward': 60} : null,
    };

Future<AppController> controller(Future<http.Response> Function(http.Request) handler, {Map<String, Object>? prefsInit}) async {
  SharedPreferences.setMockInitialValues(prefsInit ?? {});
  final prefs = await SharedPreferences.getInstance();
  final auth = DevAuthGateway(prefs);
  final api = ApiClient(baseUrl: 'http://fake', deviceId: 'd', authHeaderProvider: auth.authorizationHeader, httpClient: MockClient(handler));
  final c = AppController(api: api, auth: auth, prefs: prefs, appVersion: '1.0.0');
  c.profile = Profile(id: 'user-1', username: 'ali', displayName: 'Ali', avatarId: 1, totalScore: 0, status: 'Active');
  return c;
}

http.Response json(Object body, [int status = 200]) => http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json; charset=utf-8'});

void main() {
  test('turnuva ve cüzdan JSON\'u modellere çevrilir', () {
    final info = TournamentInfo.fromJson(currentJson());
    expect(info.periodId, 7);
    expect(info.rewards.map((t) => t.rankLabel), ['1.', '2.–3.']);
    expect(info.top[1].isMe, isTrue);
    expect(info.me!.rank, 2);

    final wallet = WalletInfo.fromJson({'balance': 150, 'jokers': {'half': 5, 'double': 4, 'hint': 3}});
    expect((wallet.balance, wallet.doubleAnswer), (150, 4));

    final item = PointsHistoryItem.fromJson({'id': 1, 'amount': -25, 'type': 'purchase', 'note': null, 'createdAt': '2026-10-21T10:00:00'});
    expect(item.label, 'Mağaza alışverişi');
    expect(item.createdAt.isUtc, isTrue);

    final none = PreviousTournament.fromJson({'periodId': null});
    expect(none.periodId, isNull);
    expect(none.top, isEmpty);
  });

  testWidgets('turnuva sekmesi: geri sayım, kendi sıran, ödül tablosu ve ilk 50', (tester) async {
    final c = await controller((r) async {
      if (r.url.path == '/api/tournament/current') return json(currentJson());
      return json({'periodId': null}); // önceki tur yok
    });

    await tester.pumpWidget(MaterialApp(home: Scaffold(body: TournamentTab(controller: c))));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));

    expect(find.byKey(const Key('tournament-countdown')), findsOneWidget);
    expect(find.textContaining('5s'), findsWidgets); // ~5 saat 30 dk kaldı
    expect(find.textContaining('Sıran: 2. · 80 puan'), findsOneWidget);
    expect(find.text('Zeynep'), findsOneWidget);
    expect(find.text('Ali (sen)'), findsOneWidget);

    await tester.tap(find.byKey(const Key('rewards-tile')));
    await tester.pumpAndSettle();
    expect(find.text('100 hediye puanı'), findsOneWidget);
    expect(find.text('2.–3.'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('puanı olmayan oyuncuya ve boş tabloya anlamlı mesaj', (tester) async {
    final c = await controller((r) async {
      if (r.url.path == '/api/tournament/current') return json(currentJson(withMe: false, top: []));
      return json({'periodId': null});
    });

    await tester.pumpWidget(MaterialApp(home: Scaffold(body: TournamentTab(controller: c))));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));

    expect(find.textContaining('henüz puanın yok'), findsOneWidget);
    expect(find.byKey(const Key('tournament-empty')), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('sunucu hatasında mesaj ve tekrar dene', (tester) async {
    final c = await controller((r) async => json({'code': 'X', 'message': 'Sunucu hatası'}, 500));

    await tester.pumpWidget(MaterialApp(home: Scaffold(body: TournamentTab(controller: c))));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));

    expect(find.byKey(const Key('tournament-error')), findsOneWidget);
    expect(find.text('Tekrar dene'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('önceki turun ilk 10 penceresi her dönem için yalnızca bir kez açılır', (tester) async {
    Future<http.Response> handler(http.Request r) async {
      if (r.url.path == '/api/tournament/current') return json(currentJson());
      return json({
        'periodId': 6,
        'distributed': true,
        'top': [row(1, 'Zeynep', 300, reward: 100), row(2, 'Ali', 200, me: true, reward: 60)],
        'me': {'rank': 2, 'points': 200, 'reward': 60},
      });
    }

    final c = await controller(handler);
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: TournamentTab(controller: c))));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 200));
    await tester.pump(const Duration(milliseconds: 200));

    expect(find.byKey(const Key('previous-dialog')), findsOneWidget);
    expect(find.textContaining('Senin sıran: 2.'), findsOneWidget);
    await tester.tap(find.text('Tamam'));
    await tester.pumpAndSettle();
    expect(c.prefs.getInt('previous_seen_user-1'), 6);
    await tester.pumpWidget(const SizedBox());

    // Aynı dönem için ikinci açılışta pencere çıkmaz
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: TournamentTab(controller: c))));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 200));
    await tester.pump(const Duration(milliseconds: 200));
    expect(find.byKey(const Key('previous-dialog')), findsNothing);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('cüzdan ekranı: bakiye, jokerler ve puan geçmişi', (tester) async {
    final c = await controller((r) async {
      if (r.url.path == '/api/wallet') return json({'balance': 135, 'jokers': {'half': 5, 'double': 4, 'hint': 3}});
      return json([
        {'id': 2, 'amount': -25, 'type': 'purchase', 'refType': 'order', 'refId': '1', 'note': null, 'createdAt': '2026-10-21T10:00:00Z'},
        {'id': 1, 'amount': 160, 'type': 'tournament', 'refType': 'period', 'refId': '6', 'note': 'Turnuva ödülü (1. sıra)', 'createdAt': '2026-10-20T21:00:30Z'},
      ]);
    });

    await tester.pumpWidget(MaterialApp(home: WalletScreen(api: c.api)));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));

    expect(find.byKey(const Key('wallet-balance')), findsOneWidget);
    expect(find.text('135'), findsOneWidget);
    expect(find.textContaining('Çift cevap 4'), findsOneWidget);
    expect(find.text('Mağaza alışverişi'), findsOneWidget);
    expect(find.text('-25'), findsOneWidget);
    expect(find.text('+160'), findsOneWidget);
    expect(find.text('Turnuva ödülü (1. sıra)'), findsOneWidget);
  });

  testWidgets('boş cüzdan geçmişi', (tester) async {
    final c = await controller((r) async {
      if (r.url.path == '/api/wallet') return json({'balance': 0, 'jokers': {'half': 5, 'double': 5, 'hint': 5}});
      return json([]);
    });
    await tester.pumpWidget(MaterialApp(home: WalletScreen(api: c.api)));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
    expect(find.byKey(const Key('wallet-empty')), findsOneWidget);
  });
}
