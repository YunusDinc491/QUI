import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../auth/dev_auth_gateway.dart';
import '../widgets/common.dart';

/// E-posta/parola ile kayıtta e-posta doğrulaması zorunludur (docs/08).
class VerifyEmailScreen extends StatefulWidget {
  const VerifyEmailScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<VerifyEmailScreen> createState() => _VerifyEmailScreenState();
}

class _VerifyEmailScreenState extends State<VerifyEmailScreen> {
  bool _busy = false;

  AppController get c => widget.controller;

  Future<void> _run(Future<void> Function() action, {String? success}) async {
    setState(() => _busy = true);
    try {
      await action();
      if (success != null && mounted) showSnack(context, success);
    } catch (e) {
      if (mounted) showSnack(context, errorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final email = c.user?.email ?? '';
    final gateway = c.auth;
    return Scaffold(
      body: SafeArea(
        child: MessageBody(
          icon: Icons.mark_email_unread_outlined,
          title: 'E-postanı doğrula',
          message: '$email adresine bir doğrulama bağlantısı gönderdik. Bağlantıya tıkladıktan sonra "Doğruladım" düğmesine bas.',
          actions: [
            FilledButton(
              key: const Key('verified-button'),
              onPressed: _busy ? null : () => _run(c.checkEmailVerified),
              child: const Text('Doğruladım, devam et'),
            ),
            const SizedBox(height: 8),
            OutlinedButton(
              onPressed: _busy ? null : () => _run(c.resendVerification, success: 'Doğrulama e-postası tekrar gönderildi.'),
              child: const Text('E-postayı tekrar gönder'),
            ),
            if (gateway is DevAuthGateway) ...[
              const SizedBox(height: 8),
              TextButton(
                key: const Key('dev-confirm-email'),
                onPressed: _busy ? null : () => _run(() async {
                  await gateway.devConfirmEmail();
                  await c.checkEmailVerified();
                }),
                child: const Text('(Geliştirme) E-postadaki bağlantıya tıkla'),
              ),
            ],
            TextButton(onPressed: _busy ? null : c.signOut, child: const Text('Çıkış yap')),
          ],
        ),
      ),
    );
  }
}
