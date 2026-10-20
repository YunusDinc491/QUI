import 'dart:math';

import 'package:flutter/material.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app/app_controller.dart';
import 'app/app_root.dart';
import 'auth/auth_gateway.dart';
import 'auth/dev_auth_gateway.dart';
import 'auth/firebase_auth_gateway.dart';
import 'core/api_client.dart';

/// Kimlik sağlayıcı seçimi: varsayılan geliştirme kimliği; gerçek Firebase için `--dart-define=AUTH=firebase`.
const _authMode = String.fromEnvironment('AUTH', defaultValue: 'dev');

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final prefs = await SharedPreferences.getInstance();
  final packageInfo = await PackageInfo.fromPlatform();

  final AuthGateway auth;
  if (_authMode == 'firebase') {
    try {
      auth = await FirebaseAuthGateway.create();
    } catch (e) {
      // Çoğu zaman android/app/google-services.json eksiktir
      runApp(_StartupError('Firebase başlatılamadı. google-services.json eklendi mi?\n\n$e'));
      return;
    }
  } else {
    auth = DevAuthGateway(prefs); // Docs/08 §10: yalnızca geliştirme
  }

  final api = ApiClient(
    baseUrl: apiBaseUrl,
    deviceId: _deviceId(prefs),
    authHeaderProvider: auth.authorizationHeader,
  );

  runApp(SenBilirsinApp(controller: AppController(api: api, auth: auth, prefs: prefs, appVersion: packageInfo.version)));
}

/// Bu kuruluma özgü rastgele cihaz kimliği (tek cihaz kuralı için); ilk açılışta üretilip saklanır.
String _deviceId(SharedPreferences prefs) {
  const key = 'device_id';
  final existing = prefs.getString(key);
  if (existing != null) return existing;
  final random = Random.secure();
  final id = List.generate(24, (_) => random.nextInt(16).toRadixString(16)).join();
  prefs.setString(key, id);
  return id;
}

class _StartupError extends StatelessWidget {
  const _StartupError(this.message);

  final String message;

  @override
  Widget build(BuildContext context) => MaterialApp(
        home: Scaffold(body: SafeArea(child: Padding(padding: const EdgeInsets.all(24), child: Center(child: Text(message))))),
      );
}
