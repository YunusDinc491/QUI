import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:senbilirsin/app/app_controller.dart';
import 'package:senbilirsin/auth/dev_auth_gateway.dart';
import 'package:senbilirsin/core/api_client.dart';
import 'package:senbilirsin/screens/category_screen.dart';
import 'package:senbilirsin/screens/play_tab.dart';

const _catalogJson = [
  {'id': 1, 'code': 'general', 'name': 'Genel sorular', 'mixedAvailable': false, 'categories': []},
  {
    'id': 2,
    'code': 'branch',
    'name': 'Branş seçmeli',
    'mixedAvailable': true,
    'categories': [
      {'id': 100, 'name': 'Tarih', 'imageUrl': null, 'colorHex': '#1E88E5'},
      {'id': 101, 'name': 'Coğrafya', 'imageUrl': null, 'colorHex': null},
    ],
  },
  {'id': 3, 'code': 'kpss', 'name': 'KPSS tarzı', 'mixedAvailable': false, 'categories': []},
  {
    'id': 4,
    'code': 'fan',
    'name': 'Fanlara özel',
    'mixedAvailable': false,
    'categories': [
      {'id': 102, 'name': 'Dizi', 'imageUrl': null, 'colorHex': null},
    ],
  },
];

Future<AppController> _controller({bool fail = false}) async {
  SharedPreferences.setMockInitialValues({});
  final prefs = await SharedPreferences.getInstance();
  final auth = DevAuthGateway(prefs);
  final api = ApiClient(
    baseUrl: 'http://fake',
    deviceId: 'device-A',
    authHeaderProvider: auth.authorizationHeader,
    httpClient: MockClient((r) async {
      if (fail) return http.Response(jsonEncode({'code': 'X', 'message': 'Sunucu hatası'}), 500);
      return http.Response(jsonEncode(_catalogJson), 200, headers: {'content-type': 'application/json; charset=utf-8'});
    }),
  );
  return AppController(api: api, auth: auth, prefs: prefs, appVersion: '1.0.0');
}

void main() {
  test('katalog JSON\'u modellere çevrilir', () async {
    final c = await _controller();
    final modes = await c.api.fetchCatalog();
    expect(modes.map((m) => m.code), ['general', 'branch', 'kpss', 'fan']);
    expect(modes[0].isGeneral, isTrue);
    expect(modes[1].mixedAvailable, isTrue);
    expect(modes[1].categories.map((e) => e.name), ['Tarih', 'Coğrafya']);
    expect(modes[1].categories.first.colorHex, '#1E88E5');
  });

  testWidgets('alt dal ekranı: Karışık başta, sonra alt dallar; seçim geri döner', (tester) async {
    final c = await _controller();
    final branch = (await c.api.fetchCatalog())[1];
    GameSelection? picked;

    await tester.pumpWidget(MaterialApp(
      home: Builder(
        builder: (context) => ElevatedButton(
          onPressed: () async => picked = await Navigator.of(context).push<GameSelection>(MaterialPageRoute(builder: (_) => CategoryScreen(mode: branch))),
          child: const Text('aç'),
        ),
      ),
    ));
    await tester.tap(find.text('aç'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('category-mixed')), findsOneWidget);
    expect(find.text('Tarih'), findsOneWidget);
    expect(find.text('Coğrafya'), findsOneWidget);

    await tester.tap(find.byKey(const Key('category-100')));
    await tester.pumpAndSettle();
    expect(picked?.categoryId, 100);
    expect(picked?.isMixed, isFalse);
  });

  testWidgets('Karışık seçilirse kategori boş ve isMixed doğru', (tester) async {
    final c = await _controller();
    final branch = (await c.api.fetchCatalog())[1];
    GameSelection? picked;

    await tester.pumpWidget(MaterialApp(
      home: Builder(
        builder: (context) => ElevatedButton(
          onPressed: () async => picked = await Navigator.of(context).push<GameSelection>(MaterialPageRoute(builder: (_) => CategoryScreen(mode: branch))),
          child: const Text('aç'),
        ),
      ),
    ));
    await tester.tap(find.text('aç'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('category-mixed')));
    await tester.pumpAndSettle();

    expect(picked?.categoryId, isNull);
    expect(picked?.isMixed, isTrue);
  });

  testWidgets('tek alt dallı modda Karışık gösterilmez', (tester) async {
    final c = await _controller();
    final fan = (await c.api.fetchCatalog())[3];
    await tester.pumpWidget(MaterialApp(home: CategoryScreen(mode: fan)));
    expect(find.byKey(const Key('category-mixed')), findsNothing);
    expect(find.text('Dizi'), findsOneWidget);
  });

  testWidgets('Oyna sekmesi: 4 mod kartı, alt dalı olmayan mod "Yakında", Genel doğrudan seçilir', (tester) async {
    final c = await _controller();
    GameSelection? picked;
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: PlayTab(controller: c, onSelected: (s) => picked = s))));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('mode-general')), findsOneWidget);
    expect(find.byKey(const Key('mode-branch')), findsOneWidget);
    expect(find.byKey(const Key('mode-kpss')), findsOneWidget);
    expect(find.byKey(const Key('mode-fan')), findsOneWidget);
    expect(find.text('Yakında'), findsOneWidget); // yalnızca KPSS

    await tester.tap(find.byKey(const Key('mode-general')));
    await tester.pumpAndSettle();
    expect(picked?.mode.code, 'general');
    expect(picked?.isMixed, isFalse);
  });

  testWidgets('Oyna sekmesi: Branş → Tarih seçimi onSelected\'a ulaşır', (tester) async {
    final c = await _controller();
    GameSelection? picked;
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: PlayTab(controller: c, onSelected: (s) => picked = s))));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('mode-branch')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('category-100')));
    await tester.pumpAndSettle();

    expect(picked?.mode.code, 'branch');
    expect(picked?.category?.name, 'Tarih');
  });

  testWidgets('sunucu hatasında mesaj ve "Tekrar dene" görünür', (tester) async {
    final c = await _controller(fail: true);
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: PlayTab(controller: c))));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('catalog-error')), findsOneWidget);
    expect(find.text('Tekrar dene'), findsOneWidget);
  });
}
