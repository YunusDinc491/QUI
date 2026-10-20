import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../auth/dev_auth_gateway.dart';
import '../widgets/common.dart';

/// Giriş / kayıt: Google ya da e-posta + parola (docs/06 §1). Ham arayüz.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _devGoogleEmail = TextEditingController();
  bool _registerMode = false;
  bool _busy = false;

  AppController get c => widget.controller;
  bool get _isDevGateway => c.auth is DevAuthGateway;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _devGoogleEmail.dispose();
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } catch (e) {
      if (mounted) showSnack(context, errorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text('SenBilirsin', style: Theme.of(context).textTheme.headlineLarge, textAlign: TextAlign.center),
                  const SizedBox(height: 4),
                  Text(_registerMode ? 'Hesap oluştur' : 'Giriş yap', textAlign: TextAlign.center),
                  if (c.notice != null) ...[
                    const SizedBox(height: 16),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: Theme.of(context).colorScheme.secondaryContainer,
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(c.notice!, key: const Key('login-notice')),
                    ),
                  ],
                  const SizedBox(height: 24),
                  if (_isDevGateway)
                    TextField(
                      controller: _devGoogleEmail,
                      decoration: const InputDecoration(
                        labelText: '(Geliştirme) Google hesabı e-postası — boşsa varsayılan',
                        border: OutlineInputBorder(),
                      ),
                    ),
                  if (_isDevGateway) const SizedBox(height: 8),
                  OutlinedButton.icon(
                    key: const Key('google-button'),
                    onPressed: _busy ? null : () => _run(() => c.signInWithGoogle(devEmail: _devGoogleEmail.text)),
                    icon: const Icon(Icons.g_mobiledata),
                    label: const Text('Google ile devam et'),
                  ),
                  const Padding(padding: EdgeInsets.symmetric(vertical: 16), child: Center(child: Text('veya'))),
                  TextField(
                    key: const Key('email-field'),
                    controller: _email,
                    keyboardType: TextInputType.emailAddress,
                    decoration: const InputDecoration(labelText: 'E-posta', border: OutlineInputBorder()),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    key: const Key('password-field'),
                    controller: _password,
                    obscureText: true,
                    decoration: InputDecoration(
                      labelText: 'Parola',
                      helperText: _registerMode ? 'En az 6 karakter' : null,
                      border: const OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 16),
                  FilledButton(
                    key: const Key('submit-button'),
                    onPressed: _busy
                        ? null
                        : () => _run(() => _registerMode
                            ? c.registerWithEmail(_email.text, _password.text)
                            : c.signInWithEmail(_email.text, _password.text)),
                    child: Text(_registerMode ? 'Kayıt ol' : 'Giriş yap'),
                  ),
                  TextButton(
                    key: const Key('toggle-mode'),
                    onPressed: _busy ? null : () => setState(() => _registerMode = !_registerMode),
                    child: Text(_registerMode ? 'Zaten hesabım var' : 'Hesabın yok mu? Kayıt ol'),
                  ),
                  if (!_registerMode)
                    TextButton(
                      onPressed: _busy
                          ? null
                          : () => _run(() async {
                                if (_email.text.trim().isEmpty) {
                                  showSnack(context, 'Önce e-posta adresini yaz.');
                                  return;
                                }
                                await c.sendPasswordReset(_email.text);
                                if (context.mounted) showSnack(context, 'Parola sıfırlama bağlantısı gönderildi (varsa).');
                              }),
                      child: const Text('Parolamı unuttum'),
                    ),
                  if (_busy) const Padding(padding: EdgeInsets.only(top: 12), child: LinearProgressIndicator()),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
