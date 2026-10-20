import 'package:firebase_auth/firebase_auth.dart' as fb;
import 'package:firebase_core/firebase_core.dart';
import 'package:google_sign_in/google_sign_in.dart';

import 'auth_gateway.dart';

/// Gerçek kimlik sağlayıcı: Firebase Authentication (Google + e-posta/parola).
/// Etkinleştirmek için: `flutter run --dart-define=AUTH=firebase` (android/app/google-services.json gerekir).
/// Ekranlar aynı [AuthGateway] arayüzünü kullandığı için geliştirme uygulamasıyla aynı davranır.
class FirebaseAuthGateway implements AuthGateway {
  FirebaseAuthGateway._(this._auth);

  final fb.FirebaseAuth _auth;
  AuthUser? _current;

  /// Firebase ve Google Sign-In'i başlatır. google-services.json yoksa hata fırlatır.
  static Future<FirebaseAuthGateway> create() async {
    await Firebase.initializeApp();
    // Android'de web istemci kimliği, google-services eklentisinin ürettiği default_web_client_id'den okunur
    await GoogleSignIn.instance.initialize();
    return FirebaseAuthGateway._(fb.FirebaseAuth.instance);
  }

  @override
  AuthUser? get currentUser => _current;

  /// Firebase kullanıcısını uygulama modeline çevirir. Oturumu hangi yöntemle açtığımız
  /// (Google mi parola mı) kimlik jetonundaki `sign_in_provider` talebinden okunur.
  Future<AuthUser?> _refreshCurrent({bool forceTokenRefresh = false}) async {
    final user = _auth.currentUser;
    if (user == null) return _current = null;

    final token = await user.getIdTokenResult(forceTokenRefresh);
    final providers = user.providerData.map((p) => p.providerId).where((id) => id != 'firebase').toList();
    final signIn = token.signInProvider ?? (providers.isNotEmpty ? providers.first : AuthProviders.password);
    return _current = AuthUser(
      uid: user.uid,
      email: user.email ?? '',
      emailVerified: user.emailVerified,
      providers: providers,
      signInProvider: signIn,
    );
  }

  fb.User _requireUser() {
    final user = _auth.currentUser;
    if (user == null) throw AuthException('no-current-user', 'Oturum açık değil.');
    return user;
  }

  /// Firebase ve Google hatalarını Türkçe [AuthException]'a çevirir.
  Future<T> _wrap<T>(Future<T> Function() action) async {
    try {
      return await action();
    } on fb.FirebaseAuthException catch (e) {
      throw AuthException(e.code, _messageFor(e.code));
    } on GoogleSignInException catch (e) {
      if (e.code == GoogleSignInExceptionCode.canceled) {
        throw AuthException('canceled', 'Giriş iptal edildi.');
      }
      throw AuthException('google-error', 'Google ile giriş yapılamadı. (${e.code.name})');
    }
  }

  String _messageFor(String code) => switch (code) {
        'invalid-credential' || 'wrong-password' || 'user-not-found' || 'invalid-login-credentials' =>
          'E-posta veya parola hatalı.',
        'email-already-in-use' => 'Bu e-posta ile zaten bir hesap var.',
        'weak-password' => 'Parola çok zayıf. En az 6 karakter olmalı.',
        'invalid-email' => 'E-posta adresi geçersiz.',
        'user-disabled' => 'Bu hesap devre dışı bırakılmış.',
        'too-many-requests' => 'Çok fazla deneme yapıldı. Biraz bekleyip tekrar dene.',
        'network-request-failed' => 'İnternet bağlantısı yok ya da zayıf.',
        'requires-recent-login' => 'Bu işlem için yeniden giriş yapmalısın.',
        'credential-already-in-use' => 'Bu hesap başka bir kullanıcıya bağlı.',
        'provider-already-linked' => 'Bu giriş yöntemi zaten bağlı.',
        'no-such-provider' => 'Bu yöntem hesaba bağlı değil.',
        _ => 'Giriş işlemi başarısız oldu. ($code)',
      };

  Future<fb.AuthCredential> _googleCredential() async {
    final account = await GoogleSignIn.instance.authenticate();
    final idToken = account.authentication.idToken;
    if (idToken == null) throw AuthException('google-no-token', 'Google kimlik bilgisi alınamadı.');
    return fb.GoogleAuthProvider.credential(idToken: idToken);
  }

  @override
  Future<void> restore() async {
    final user = _auth.currentUser;
    if (user == null) {
      _current = null;
      return;
    }
    try {
      // Hesap başka yerden silinmiş/devre dışı bırakılmış olabilir
      await user.reload();
      await _refreshCurrent();
    } on fb.FirebaseAuthException catch (e) {
      if (e.code == 'user-not-found' || e.code == 'user-disabled' || e.code == 'user-token-expired') {
        await signOut();
      } else if (e.code == 'network-request-failed') {
        await _refreshCurrent(); // çevrimdışıysa yerel bilgilerle devam et; API zaten ulaşılamaz diyecek
      } else {
        rethrow;
      }
    }
  }

  @override
  Future<AuthUser> signInWithEmail(String email, String password) => _wrap(() async {
        await _auth.signInWithEmailAndPassword(email: email.trim(), password: password);
        return (await _refreshCurrent())!;
      });

  @override
  Future<AuthUser> registerWithEmail(String email, String password) => _wrap(() async {
        await _auth.createUserWithEmailAndPassword(email: email.trim(), password: password);
        return (await _refreshCurrent())!;
      });

  @override
  Future<AuthUser> signInWithGoogle({String? devEmail}) => _wrap(() async {
        await _auth.signInWithCredential(await _googleCredential());
        return (await _refreshCurrent())!;
      });

  @override
  Future<void> sendEmailVerification() => _wrap(() => _requireUser().sendEmailVerification());

  @override
  Future<AuthUser> reloadUser() => _wrap(() async {
        await _requireUser().reload();
        // email_verified talebi yeni jetonda güncellensin diye jetonu yenile
        return (await _refreshCurrent(forceTokenRefresh: true))!;
      });

  @override
  Future<void> sendPasswordReset(String email) => _wrap(() => _auth.sendPasswordResetEmail(email: email.trim()));

  @override
  Future<AuthUser> linkGoogle() => _wrap(() async {
        await _requireUser().linkWithCredential(await _googleCredential());
        return (await _refreshCurrent(forceTokenRefresh: true))!;
      });

  @override
  Future<AuthUser> linkEmail(String email, String password) => _wrap(() async {
        final credential = fb.EmailAuthProvider.credential(email: email.trim(), password: password);
        await _requireUser().linkWithCredential(credential);
        return (await _refreshCurrent(forceTokenRefresh: true))!;
      });

  @override
  Future<AuthUser> unlinkProvider(String providerId) => _wrap(() async {
        final user = _requireUser();
        final linked = user.providerData.map((p) => p.providerId).where((id) => id != 'firebase').toList();
        if (!linked.contains(providerId)) throw AuthException('no-such-provider', 'Bu yöntem hesaba bağlı değil.');
        if (linked.length <= 1) throw AuthException('last-provider', 'En az bir giriş yöntemi bağlı kalmalı.');
        await user.unlink(providerId);
        return (await _refreshCurrent(forceTokenRefresh: true))!;
      });

  @override
  Future<void> reauthenticate({String? password}) => _wrap(() async {
        final user = _requireUser();
        if (_current?.signInProvider == AuthProviders.google) {
          await user.reauthenticateWithCredential(await _googleCredential());
        } else {
          if (password == null || password.isEmpty) throw AuthException('wrong-password', 'Parola gerekli.');
          final credential = fb.EmailAuthProvider.credential(email: user.email ?? '', password: password);
          await user.reauthenticateWithCredential(credential);
        }
      });

  @override
  Future<void> deleteAuthAccount() => _wrap(() async {
        await _requireUser().delete();
        await GoogleSignIn.instance.signOut();
        _current = null;
      });

  @override
  Future<void> signOut() async {
    await _auth.signOut();
    try {
      await GoogleSignIn.instance.signOut();
    } catch (_) {
      // Google ile hiç girilmemişse sessizce geç
    }
    _current = null;
  }

  @override
  Future<String?> authorizationHeader() async {
    final user = _auth.currentUser;
    if (user == null) return null;
    // Süresi dolmak üzereyse Firebase jetonu kendisi yeniler
    final token = await user.getIdToken();
    return token == null ? null : 'Bearer $token';
  }
}
