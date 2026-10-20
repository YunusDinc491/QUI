/// Giriş yöntemleri: Firebase'deki sağlayıcı kimlikleriyle aynı adlar.
class AuthProviders {
  static const password = 'password';
  static const google = 'google.com';

  static String label(String id) => switch (id) {
        password => 'E-posta ve parola',
        google => 'Google',
        _ => id,
      };
}

class AuthUser {
  AuthUser({
    required this.uid,
    required this.email,
    required this.emailVerified,
    required this.providers,
    required this.signInProvider,
  });

  final String uid;
  final String email;
  final bool emailVerified;

  /// Hesaba bağlı tüm yöntemler (örn. ['password', 'google.com'])
  final List<String> providers;

  /// Bu oturumu açmak için kullanılan yöntem
  final String signInProvider;

  /// E-posta/parola ile girenlerde e-posta doğrulaması zorunludur (docs/08)
  bool get needsEmailVerification => signInProvider == AuthProviders.password && !emailVerified;
}

/// Kimlik hatası; mesaj kullanıcıya gösterilmek üzere Türkçedir.
class AuthException implements Exception {
  AuthException(this.code, this.message);

  final String code;
  final String message;

  @override
  String toString() => 'AuthException($code): $message';
}

/// Kimlik sağlayıcı soyutlaması. Üretimde Firebase Auth uygulaması, geliştirmede
/// [DevAuthGateway] kullanılır; böylece ekranlar Firebase kurulmadan elle denenebilir.
abstract class AuthGateway {
  AuthUser? get currentUser;

  /// Önceki oturumu geri yükler (uygulama açılışında).
  Future<void> restore();

  Future<AuthUser> signInWithEmail(String email, String password);
  Future<AuthUser> registerWithEmail(String email, String password);
  Future<AuthUser> signInWithGoogle({String? devEmail});

  Future<void> sendEmailVerification();

  /// Doğrulama durumunu sunucudan yeniler ve güncel kullanıcıyı döndürür.
  Future<AuthUser> reloadUser();

  Future<void> sendPasswordReset(String email);

  Future<AuthUser> linkGoogle();
  Future<AuthUser> linkEmail(String email, String password);

  /// Yöntemi çıkarır. En az bir yöntem bağlı kalmalıdır.
  Future<AuthUser> unlinkProvider(String providerId);

  /// Hassas işlemlerden (hesap silme) önce kimliği yeniden doğrular.
  Future<void> reauthenticate({String? password});

  /// Kimlik hesabını (Firebase) siler. Sunucudaki profil önce silinmiş olmalıdır.
  Future<void> deleteAuthAccount();

  Future<void> signOut();

  /// API'ye gönderilecek Authorization başlığı: `Bearer <jeton>` ya da `Dev ...`. Oturum yoksa null.
  Future<String?> authorizationHeader();
}
