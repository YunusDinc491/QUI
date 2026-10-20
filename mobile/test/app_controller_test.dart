import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:senbilirsin/app/app_controller.dart';
import 'package:senbilirsin/auth/dev_auth_gateway.dart';
import 'package:senbilirsin/core/api_client.dart';

/// Küçük bir sahte sunucu: sunucu davranışını (kayıt, aktif cihaz, silme) bellekte taklit eder.
class FakeServer {
  FakeServer({this.maintenance = false, this.minVersion = '0.0.0', this.offline = false});

  bool maintenance;
  String minVersion;
  bool offline;
  final Map<String, Map<String, dynamic>> users = {}; // uid -> profil
  final Map<String, String> activeDevice = {}; // uid -> deviceId

  String _uid(http.Request r) {
    final header = r.headers['Authorization'] ?? '';
    return RegExp(r'uid=([^;]+)').firstMatch(header)?.group(1) ?? '';
  }

  http.Response _json(Object body, [int status = 200]) =>
      http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json; charset=utf-8'});

  Future<http.Response> handle(http.Request r) async {
    if (offline) throw http.ClientException('offline');
    final path = r.url.path;
    final uid = _uid(r);
    final device = r.headers['X-Device-Id'];

    if (path == '/api/config') {
      return _json({'minSupportedVersion': minVersion, 'maintenanceMode': maintenance, 'maintenanceMessage': 'Bakımdayız'});
    }
    if (path == '/api/users/avatars') {
      return _json([
        {'id': 1, 'name': 'Avatar 1', 'colorHex': '#E57373', 'price': 0, 'imageUrl': null},
        {'id': 2, 'name': 'Avatar 2', 'colorHex': '#64B5F6', 'price': 0, 'imageUrl': null},
      ]);
    }
    if (path == '/api/users/username-available') {
      final name = r.url.queryParameters['username'] ?? '';
      final taken = users.values.any((u) => u['username'] == name);
      return _json({'available': !taken, 'code': taken ? 'USERNAME_TAKEN' : null, 'message': taken ? 'Bu kullanıcı adı alınmış.' : null});
    }
    if (path == '/api/users/me' && r.method == 'POST') {
      final body = jsonDecode(r.body) as Map<String, dynamic>;
      users[uid] = {
        'id': 'id-$uid',
        'username': body['username'],
        'displayName': body['displayName'],
        'avatarId': body['avatarId'],
        'totalScore': 0,
        'status': 'Active',
      };
      return _json(users[uid]!, 201);
    }
    if (path == '/api/users/me/session') {
      if (!users.containsKey(uid)) return _json({'code': 'NOT_REGISTERED', 'message': 'Önce kayıt tamamlanmalı.'}, 404);
      activeDevice[uid] = device ?? '';
      return _json(users[uid]!);
    }
    if (path == '/api/users/me') {
      if (!users.containsKey(uid)) return _json({'code': 'NOT_REGISTERED', 'message': 'x'}, 404);
      if (activeDevice[uid] != device) return _json({'code': 'SESSION_REPLACED', 'message': 'x'}, 401);
      if (r.method == 'DELETE') {
        users.remove(uid);
        return http.Response('', 204);
      }
      if (r.method == 'PATCH') {
        final body = jsonDecode(r.body) as Map<String, dynamic>;
        users[uid]!.addAll(body);
        return _json(users[uid]!);
      }
      return _json(users[uid]!);
    }
    return _json({'code': 'NOT_FOUND', 'message': 'x'}, 404);
  }
}

Future<(AppController, FakeServer, DevAuthGateway)> setUpApp({FakeServer? server, String version = '1.0.0'}) async {
  SharedPreferences.setMockInitialValues({});
  final prefs = await SharedPreferences.getInstance();
  final fake = server ?? FakeServer();
  final auth = DevAuthGateway(prefs);
  final api = ApiClient(
    baseUrl: 'http://fake',
    deviceId: 'device-A',
    authHeaderProvider: auth.authorizationHeader,
    httpClient: MockClient(fake.handle),
  );
  return (AppController(api: api, auth: auth, prefs: prefs, appVersion: version), fake, auth);
}

void main() {
  test('bakım modunda bakım ekranına gider', () async {
    final (c, _, _) = await setUpApp(server: FakeServer(maintenance: true));
    await c.start();
    expect(c.stage, AppStage.maintenance);
  });

  test('min sürümün altındaysa güncelleme ekranına gider', () async {
    final (c, _, _) = await setUpApp(server: FakeServer(minVersion: '2.0.0'), version: '1.9.9');
    await c.start();
    expect(c.stage, AppStage.updateRequired);
  });

  test('sunucuya ulaşılamazsa çevrimdışı ekranına gider', () async {
    final (c, _, _) = await setUpApp(server: FakeServer(offline: true));
    await c.start();
    expect(c.stage, AppStage.offline);
  });

  test('oturum yoksa giriş ekranı gelir', () async {
    final (c, _, _) = await setUpApp();
    await c.start();
    expect(c.stage, AppStage.signedOut);
  });

  test('e-posta ile tam akış: kayıt → doğrulama → profil kaydı → ana ekran → tur', () async {
    final (c, server, auth) = await setUpApp();
    await c.start();

    await c.registerWithEmail('ali@test.com', '123456');
    expect(c.stage, AppStage.verifyEmail);

    await auth.devConfirmEmail();
    await c.checkEmailVerified();
    expect(c.stage, AppStage.needsRegistration);

    await c.completeRegistration(username: 'ali123', displayName: 'Ali', avatarId: 1, birthYear: 2000, termsAccepted: true);
    expect(c.stage, AppStage.ready);
    expect(c.profile?.username, 'ali123');
    expect(c.tourPending, isTrue);
    expect(server.users.length, 1);

    await c.finishTour();
    expect(c.tourPending, isFalse);

    // Uygulama yeniden açılırsa tur tekrar gösterilmez
    final c2 = AppController(api: c.api, auth: auth, prefs: c.prefs, appVersion: '1.0.0');
    await c2.start();
    expect(c2.stage, AppStage.ready);
    expect(c2.tourPending, isFalse);
  });

  test('Google ile giriş: e-posta doğrulaması istenmez, doğrudan kayıt ekranına gider', () async {
    final (c, _, _) = await setUpApp();
    await c.start();
    await c.signInWithGoogle(devEmail: 'g@test.com');
    expect(c.stage, AppStage.needsRegistration);
  });

  test('kayıtlı kullanıcı yeniden girince doğrudan ana ekrana gider', () async {
    final (c, _, _) = await setUpApp();
    await c.start();
    await c.signInWithGoogle(devEmail: 'g@test.com');
    await c.completeRegistration(username: 'gizem', displayName: 'Gizem', avatarId: 2, birthYear: 1999, termsAccepted: true);
    await c.signOut();
    expect(c.stage, AppStage.signedOut);

    await c.signInWithGoogle(devEmail: 'g@test.com');
    expect(c.stage, AppStage.ready);
    expect(c.profile?.displayName, 'Gizem');
  });

  test('başka cihaz girince bu cihazın oturumu düşer ve bilgi mesajı çıkar', () async {
    final (c, server, auth) = await setUpApp();
    await c.start();
    await c.signInWithGoogle(devEmail: 'g@test.com');
    await c.completeRegistration(username: 'gizem', displayName: 'Gizem', avatarId: 1, birthYear: 1999, termsAccepted: true);

    // Başka bir cihaz aynı hesabın aktif cihazı olur
    server.activeDevice[auth.currentUser!.uid] = 'device-B';

    await c.refreshProfile();
    expect(c.stage, AppStage.signedOut);
    expect(c.notice, contains('başka bir cihazdan'));
    expect(auth.currentUser, isNull);
  });

  test('profil güncellenir', () async {
    final (c, _, _) = await setUpApp();
    await c.start();
    await c.signInWithGoogle(devEmail: 'g@test.com');
    await c.completeRegistration(username: 'gizem', displayName: 'Gizem', avatarId: 1, birthYear: 1999, termsAccepted: true);

    await c.updateProfile(displayName: 'Yeni Ad', avatarId: 2);
    expect(c.profile?.displayName, 'Yeni Ad');
    expect(c.profile?.avatarId, 2);
  });

  test('hesap silme: profil anonimleşir, kimlik silinir, giriş ekranına dönülür', () async {
    final (c, server, auth) = await setUpApp();
    await c.start();
    await c.registerWithEmail('a@b.com', '123456');
    await auth.devConfirmEmail();
    await c.checkEmailVerified();
    await c.completeRegistration(username: 'silinecek', displayName: 'Silinecek', avatarId: 1, birthYear: 1990, termsAccepted: true);

    await c.deleteAccount(password: '123456');

    expect(c.stage, AppStage.signedOut);
    expect(c.notice, 'Hesabın silindi.');
    expect(server.users, isEmpty);
    expect(auth.currentUser, isNull);
    expect(() => auth.signInWithEmail('a@b.com', '123456'), throwsA(anything));
  });

  test('hesap silmede yanlış parola işlemi durdurur, hesap yerinde kalır', () async {
    final (c, server, auth) = await setUpApp();
    await c.start();
    await c.registerWithEmail('a@b.com', '123456');
    await auth.devConfirmEmail();
    await c.checkEmailVerified();
    await c.completeRegistration(username: 'kalacak', displayName: 'Kalacak', avatarId: 1, birthYear: 1990, termsAccepted: true);

    await expectLater(c.deleteAccount(password: 'yanlis'), throwsA(anything));
    expect(c.stage, AppStage.ready);
    expect(server.users.length, 1);
  });
}
