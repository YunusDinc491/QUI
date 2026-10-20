import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../ads/rewarded_ad_service.dart';
import '../auth/auth_gateway.dart';
import '../core/api_client.dart';
import '../core/version.dart';

/// Uygulamanın hangi ekranda olduğunu belirleyen ana durum (docs/06 §1).
enum AppStage {
  starting,
  offline, // İnternet/sunucu yok (çevrimdışı mod yok)
  updateRequired,
  maintenance,
  signedOut,
  verifyEmail,
  needsRegistration,
  banned,
  ready,
}

/// Oturum, kayıt ve profil akışını yönetir. Ekranlar bu denetleyiciyi dinler ve yöntemlerini çağırır;
/// yöntemler hata durumunda [AuthException] ya da [ApiException] fırlatır (ekran mesajı gösterir).
class AppController extends ChangeNotifier {
  AppController({required this.api, required this.auth, required this.prefs, required this.appVersion, RewardedAdService? ads})
      : ads = ads ?? StubRewardedAdService();

  final ApiClient api;
  final AuthGateway auth;
  final SharedPreferences prefs;
  final String appVersion;

  /// Ödüllü reklam hizmeti (Faz 7'ye kadar yalnızca geliştirme derlemesinde çalışan taslak).
  final RewardedAdService ads;

  AppStage stage = AppStage.starting;
  AppConfigInfo? config;
  Profile? profile;

  /// Giriş ekranında bir kez gösterilecek bilgi (örn. "Hesabına başka bir cihazdan giriş yapıldı").
  String? notice;

  /// Kayıttan hemen sonra "nasıl oynanır" turu gösterilecek mi?
  bool tourPending = false;

  AuthUser? get user => auth.currentUser;

  void _setStage(AppStage next) {
    stage = next;
    notifyListeners();
  }

  /// Açılış: sürüm/bakım kontrolü, sonra oturumu geri yükle.
  Future<void> start() async {
    _setStage(AppStage.starting);
    try {
      config = await api.fetchConfig();
    } on ApiException {
      _setStage(AppStage.offline);
      return;
    }

    if (config!.maintenanceMode) {
      _setStage(AppStage.maintenance);
    } else if (isVersionLessThan(appVersion, config!.minSupportedVersion)) {
      _setStage(AppStage.updateRequired);
    } else {
      await auth.restore();
      await _resolveAccount();
    }
  }

  /// Giriş sonrası: e-posta doğrulandı mı, sunucuda kayıtlı mı, banlı mı?
  Future<void> _resolveAccount() async {
    final current = auth.currentUser;
    if (current == null) {
      _setStage(AppStage.signedOut);
      return;
    }
    if (current.needsEmailVerification) {
      _setStage(AppStage.verifyEmail);
      return;
    }

    try {
      final fetched = await api.startSession();
      profile = fetched;
      if (fetched.isBanned) {
        _setStage(AppStage.banned);
      } else {
        tourPending = !(prefs.getBool(_tourKey(fetched.id)) ?? false);
        _setStage(AppStage.ready);
      }
    } on ApiException catch (e) {
      if (e.code == 'NOT_REGISTERED') {
        profile = null;
        _setStage(AppStage.needsRegistration);
      } else if (e.isNetwork) {
        _setStage(AppStage.offline);
      } else {
        notice = e.message;
        await auth.signOut();
        _setStage(AppStage.signedOut);
      }
    }
  }

  // --- Giriş / kayıt (kimlik) ---

  Future<void> signInWithEmail(String email, String password) async {
    await auth.signInWithEmail(email, password);
    notice = null;
    await _resolveAccount();
  }

  Future<void> registerWithEmail(String email, String password) async {
    await auth.registerWithEmail(email, password);
    await auth.sendEmailVerification();
    notice = null;
    await _resolveAccount();
  }

  Future<void> signInWithGoogle({String? devEmail}) async {
    await auth.signInWithGoogle(devEmail: devEmail);
    notice = null;
    await _resolveAccount();
  }

  Future<void> resendVerification() => auth.sendEmailVerification();

  /// "Doğruladım, devam et": doğrulama durumunu yenileyip akışa devam eder.
  Future<void> checkEmailVerified() async {
    await auth.reloadUser();
    await _resolveAccount();
  }

  Future<void> sendPasswordReset(String email) => auth.sendPasswordReset(email);

  Future<void> signOut() async {
    await auth.signOut();
    profile = null;
    tourPending = false;
    _setStage(AppStage.signedOut);
  }

  // --- Profil ---

  List<AvatarInfo>? _avatarCache;

  /// Avatar listesi (bir kez çekilir, sonra önbellekten gelir).
  Future<List<AvatarInfo>> getAvatars() async => _avatarCache ??= await api.fetchAvatars();

  Future<void> completeRegistration({
    required String username,
    required String displayName,
    required int avatarId,
    required int birthYear,
    required bool termsAccepted,
  }) async {
    await api.register(
      username: username,
      displayName: displayName,
      avatarId: avatarId,
      birthYear: birthYear,
      termsAccepted: termsAccepted,
    );
    // Kayıttan sonra bu cihaz aktif cihaz olur
    profile = await api.startSession();
    tourPending = true;
    _setStage(AppStage.ready);
  }

  Future<void> finishTour() async {
    final id = profile?.id;
    if (id != null) await prefs.setBool(_tourKey(id), true);
    tourPending = false;
    notifyListeners();
  }

  Future<void> updateProfile({String? displayName, int? avatarId}) async {
    try {
      profile = await api.updateMe(displayName: displayName, avatarId: avatarId);
      notifyListeners();
    } on ApiException catch (e) {
      if (await _handleSessionReplaced(e)) return;
      rethrow;
    }
  }

  /// Uygulama ön plana dönünce profili yeniler; başka cihaz girdiyse oturumu düşürür.
  Future<void> refreshProfile() async {
    if (stage != AppStage.ready) return;
    try {
      profile = await api.fetchMe();
      notifyListeners();
    } on ApiException catch (e) {
      await _handleSessionReplaced(e);
    }
  }

  Future<bool> _handleSessionReplaced(ApiException e) async {
    if (e.code != 'SESSION_REPLACED') return false;
    notice = 'Hesabına başka bir cihazdan giriş yapıldı. Bu cihazdaki oturum kapatıldı.';
    await auth.signOut();
    profile = null;
    _setStage(AppStage.signedOut);
    return true;
  }

  /// Hesabı siler (K-37): kimlik yeniden doğrulanır, sunucudaki profil anonimleştirilir, kimlik hesabı silinir.
  Future<void> deleteAccount({String? password}) async {
    await auth.reauthenticate(password: password);
    await api.deleteMe();
    await auth.deleteAuthAccount();
    profile = null;
    tourPending = false;
    notice = 'Hesabın silindi.';
    _setStage(AppStage.signedOut);
  }

  // --- Giriş yöntemleri (Ayarlar) ---

  Future<void> linkGoogle() async {
    await auth.linkGoogle();
    notifyListeners();
  }

  Future<void> linkEmail(String email, String password) async {
    await auth.linkEmail(email, password);
    notifyListeners();
  }

  Future<void> unlinkProvider(String providerId) async {
    await auth.unlinkProvider(providerId);
    notifyListeners();
  }

  // --- Yerel ayarlar (ses, titreşim, bildirim türleri) ---

  bool getSetting(String key, {bool defaultValue = true}) => prefs.getBool('setting_$key') ?? defaultValue;

  Future<void> setSetting(String key, bool value) async {
    await prefs.setBool('setting_$key', value);
    notifyListeners();
  }

  String _tourKey(String userId) => 'tour_done_$userId';
}
