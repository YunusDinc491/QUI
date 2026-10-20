import 'dart:convert';
import 'dart:math';

import 'package:shared_preferences/shared_preferences.dart';

import 'auth_gateway.dart';

/// Yalnızca geliştirme: Firebase hesabı olmadan kayıt/giriş akışlarını elle denemek için sahte kimlik sağlayıcı.
/// Hesapları cihazda (SharedPreferences) tutar; sunucuya "Dev uid=..." başlığıyla bağlanır
/// (sunucuda yalnızca Development ortamında kabul edilir, bkz. docs/08 §10).
/// Gerçek uygulama (Firebase) hazır olunca aynı [AuthGateway] arayüzüyle değiştirilir.
class DevAuthGateway implements AuthGateway {
  DevAuthGateway(this._prefs);

  static const _accountsKey = 'dev_auth_accounts';
  static const _currentEmailKey = 'dev_auth_current_email';
  static const _currentProviderKey = 'dev_auth_current_provider';
  static const defaultGoogleEmail = 'dev.google@senbilirsin.test';

  final SharedPreferences _prefs;
  AuthUser? _current;

  @override
  AuthUser? get currentUser => _current;

  Map<String, Map<String, dynamic>> _loadAccounts() {
    final raw = _prefs.getString(_accountsKey);
    if (raw == null) return {};
    final decoded = jsonDecode(raw) as Map<String, dynamic>;
    return decoded.map((k, v) => MapEntry(k, Map<String, dynamic>.from(v as Map)));
  }

  Future<void> _saveAccounts(Map<String, Map<String, dynamic>> accounts) =>
      _prefs.setString(_accountsKey, jsonEncode(accounts));

  AuthUser _toUser(String email, Map<String, dynamic> account, String signInProvider) => AuthUser(
        uid: account['uid'] as String,
        email: email,
        emailVerified: account['verified'] as bool,
        providers: List<String>.from(account['providers'] as List),
        signInProvider: signInProvider,
      );

  Future<AuthUser> _signIn(String email, Map<String, dynamic> account, String provider) async {
    await _prefs.setString(_currentEmailKey, email);
    await _prefs.setString(_currentProviderKey, provider);
    return _current = _toUser(email, account, provider);
  }

  String _normalizeEmail(String email) => email.trim().toLowerCase();

  String _newUid() {
    final random = Random.secure();
    return 'dev-${List.generate(12, (_) => random.nextInt(16).toRadixString(16)).join()}';
  }

  @override
  Future<void> restore() async {
    final email = _prefs.getString(_currentEmailKey);
    final provider = _prefs.getString(_currentProviderKey);
    if (email == null || provider == null) return;
    final account = _loadAccounts()[email];
    _current = account == null ? null : _toUser(email, account, provider);
  }

  @override
  Future<AuthUser> registerWithEmail(String email, String password) async {
    final normalized = _normalizeEmail(email);
    if (!normalized.contains('@') || !normalized.contains('.')) {
      throw AuthException('invalid-email', 'E-posta adresi geçersiz.');
    }
    if (password.length < 6) {
      throw AuthException('weak-password', 'Parola en az 6 karakter olmalı.');
    }
    final accounts = _loadAccounts();
    if (accounts.containsKey(normalized)) {
      throw AuthException('email-already-in-use', 'Bu e-posta ile zaten bir hesap var.');
    }
    final account = <String, dynamic>{
      'uid': _newUid(),
      'password': password,
      'verified': false,
      'providers': [AuthProviders.password],
    };
    accounts[normalized] = account;
    await _saveAccounts(accounts);
    return _signIn(normalized, account, AuthProviders.password);
  }

  @override
  Future<AuthUser> signInWithEmail(String email, String password) async {
    final normalized = _normalizeEmail(email);
    final account = _loadAccounts()[normalized];
    final hasPassword = account != null && (account['providers'] as List).contains(AuthProviders.password);
    if (account == null || !hasPassword || account['password'] != password) {
      throw AuthException('invalid-credential', 'E-posta veya parola hatalı.');
    }
    return _signIn(normalized, account, AuthProviders.password);
  }

  @override
  Future<AuthUser> signInWithGoogle({String? devEmail}) async {
    final normalized = _normalizeEmail(devEmail == null || devEmail.trim().isEmpty ? defaultGoogleEmail : devEmail);
    final accounts = _loadAccounts();
    final account = accounts.putIfAbsent(normalized, () => {
          'uid': _newUid(),
          'password': null,
          'verified': true, // Google e-postayı zaten doğrulanmış getirir
          'providers': <String>[],
        });
    final providers = List<String>.from(account['providers'] as List);
    if (!providers.contains(AuthProviders.google)) providers.add(AuthProviders.google);
    account['providers'] = providers;
    account['verified'] = true;
    await _saveAccounts(accounts);
    return _signIn(normalized, account, AuthProviders.google);
  }

  Map<String, dynamic> _requireCurrentAccount(Map<String, Map<String, dynamic>> accounts) {
    final email = _current?.email;
    final account = email == null ? null : accounts[email];
    if (account == null) throw AuthException('no-current-user', 'Oturum açık değil.');
    return account;
  }

  @override
  Future<void> sendEmailVerification() async {
    // Gerçek e-posta gönderilmez; doğrulamayı ekrandaki geliştirme düğmesi simüle eder.
  }

  /// Yalnızca geliştirme: e-postadaki doğrulama bağlantısına tıklanmış gibi davranır.
  Future<void> devConfirmEmail() async {
    final accounts = _loadAccounts();
    final account = _requireCurrentAccount(accounts);
    account['verified'] = true;
    await _saveAccounts(accounts);
  }

  @override
  Future<AuthUser> reloadUser() async {
    final accounts = _loadAccounts();
    final account = _requireCurrentAccount(accounts);
    return _current = _toUser(_current!.email, account, _current!.signInProvider);
  }

  @override
  Future<void> sendPasswordReset(String email) async {
    // Geliştirmede gerçek e-posta gitmez.
  }

  @override
  Future<AuthUser> linkGoogle() async {
    final accounts = _loadAccounts();
    final account = _requireCurrentAccount(accounts);
    final providers = List<String>.from(account['providers'] as List);
    if (providers.contains(AuthProviders.google)) {
      throw AuthException('provider-already-linked', 'Google zaten bağlı.');
    }
    providers.add(AuthProviders.google);
    account['providers'] = providers;
    await _saveAccounts(accounts);
    return _current = _toUser(_current!.email, account, _current!.signInProvider);
  }

  @override
  Future<AuthUser> linkEmail(String email, String password) async {
    final accounts = _loadAccounts();
    final account = _requireCurrentAccount(accounts);
    final providers = List<String>.from(account['providers'] as List);
    if (providers.contains(AuthProviders.password)) {
      throw AuthException('provider-already-linked', 'E-posta ve parola zaten bağlı.');
    }
    if (password.length < 6) throw AuthException('weak-password', 'Parola en az 6 karakter olmalı.');
    providers.add(AuthProviders.password);
    account['providers'] = providers;
    account['password'] = password;
    await _saveAccounts(accounts);
    return _current = _toUser(_current!.email, account, _current!.signInProvider);
  }

  @override
  Future<AuthUser> unlinkProvider(String providerId) async {
    final accounts = _loadAccounts();
    final account = _requireCurrentAccount(accounts);
    final providers = List<String>.from(account['providers'] as List);
    if (!providers.contains(providerId)) {
      throw AuthException('no-such-provider', 'Bu yöntem hesaba bağlı değil.');
    }
    if (providers.length <= 1) {
      throw AuthException('last-provider', 'En az bir giriş yöntemi bağlı kalmalı.');
    }
    providers.remove(providerId);
    account['providers'] = providers;
    await _saveAccounts(accounts);
    // Oturum açılan yöntem çıkarıldıysa kalan yönteme geç
    final signIn = providers.contains(_current!.signInProvider) ? _current!.signInProvider : providers.first;
    await _prefs.setString(_currentProviderKey, signIn);
    return _current = _toUser(_current!.email, account, signIn);
  }

  @override
  Future<void> reauthenticate({String? password}) async {
    final accounts = _loadAccounts();
    final account = _requireCurrentAccount(accounts);
    if (_current!.signInProvider == AuthProviders.password && account['password'] != password) {
      throw AuthException('wrong-password', 'Parola hatalı.');
    }
  }

  @override
  Future<void> deleteAuthAccount() async {
    final accounts = _loadAccounts();
    final email = _current?.email;
    if (email != null) accounts.remove(email);
    await _saveAccounts(accounts);
    await signOut();
  }

  @override
  Future<void> signOut() async {
    _current = null;
    await _prefs.remove(_currentEmailKey);
    await _prefs.remove(_currentProviderKey);
  }

  @override
  Future<String?> authorizationHeader() async {
    final user = _current;
    if (user == null) return null;
    return 'Dev uid=${user.uid};email=${user.email};verified=${user.emailVerified};provider=${user.signInProvider}';
  }
}
