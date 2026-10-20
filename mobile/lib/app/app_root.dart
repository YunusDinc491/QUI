import 'package:flutter/material.dart';

import '../screens/home_shell.dart';
import '../screens/login_screen.dart';
import '../screens/registration_screen.dart';
import '../screens/status_screens.dart';
import '../screens/tour_screen.dart';
import '../screens/verify_email_screen.dart';
import 'app_controller.dart';

/// MaterialApp + durum makinesine göre gösterilen ekran.
class SenBilirsinApp extends StatelessWidget {
  SenBilirsinApp({super.key, required this.controller});

  final AppController controller;
  final GlobalKey<NavigatorState> _navigatorKey = GlobalKey<NavigatorState>();

  @override
  Widget build(BuildContext context) {
    // Faz 0-8: varsayılan Material tema; tasarım en sona bırakıldı (K-53).
    return MaterialApp(
      title: 'QU¿',
      navigatorKey: _navigatorKey,
      theme: ThemeData(colorSchemeSeed: Colors.indigo, useMaterial3: true),
      home: AppRoot(controller: controller, navigatorKey: _navigatorKey),
    );
  }
}

class AppRoot extends StatefulWidget {
  const AppRoot({super.key, required this.controller, required this.navigatorKey});

  final AppController controller;
  final GlobalKey<NavigatorState> navigatorKey;

  @override
  State<AppRoot> createState() => _AppRootState();
}

class _AppRootState extends State<AppRoot> with WidgetsBindingObserver {
  AppStage? _lastStage;

  AppController get c => widget.controller;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    c.addListener(_onControllerChanged);
    c.start();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    c.removeListener(_onControllerChanged);
    super.dispose();
  }

  /// Oturum düşünce (başka cihaz, çıkış, hesap silme) açık alt sayfaları (Ayarlar vb.) kapat.
  void _onControllerChanged() {
    if (_lastStage == AppStage.ready && c.stage != AppStage.ready) {
      widget.navigatorKey.currentState?.popUntil((route) => route.isFirst);
    }
    _lastStage = c.stage;
  }

  /// Uygulama ön plana dönünce başka cihazdan girilip girilmediğini kontrol et (tek cihaz kuralı).
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) c.refreshProfile();
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: c,
      builder: (context, _) {
        switch (c.stage) {
          case AppStage.starting:
          case AppStage.offline:
          case AppStage.updateRequired:
          case AppStage.maintenance:
          case AppStage.banned:
            return StatusScreen(controller: c);
          case AppStage.signedOut:
            return LoginScreen(controller: c);
          case AppStage.verifyEmail:
            return VerifyEmailScreen(controller: c);
          case AppStage.needsRegistration:
            return RegistrationScreen(controller: c);
          case AppStage.ready:
            return c.tourPending ? TourScreen(controller: c) : HomeShell(controller: c);
        }
      },
    );
  }
}
