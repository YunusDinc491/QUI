import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../auth/auth_gateway.dart';
import '../widgets/common.dart';
import 'dev_connection_page.dart';

/// Ayarlar: giriş yöntemleri, bildirim türleri, ses/titreşim, yardım, gizlilik, sürüm, çıkış, hesabı sil (docs/06 §4).
class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  AppController get c => widget.controller;

  @override
  void initState() {
    super.initState();
    c.addListener(_refresh);
  }

  @override
  void dispose() {
    c.removeListener(_refresh);
    super.dispose();
  }

  void _refresh() {
    if (mounted) setState(() {});
  }

  Future<void> _guard(Future<void> Function() action) async {
    try {
      await action();
    } catch (e) {
      if (mounted) showSnack(context, errorMessage(e));
    }
  }

  Future<void> _linkEmail() async {
    final emailController = TextEditingController(text: c.user?.email ?? '');
    final passwordController = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('E-posta ve parola ekle'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(controller: emailController, decoration: const InputDecoration(labelText: 'E-posta')),
            TextField(controller: passwordController, obscureText: true, decoration: const InputDecoration(labelText: 'Parola (en az 6 karakter)')),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Vazgeç')),
          FilledButton(onPressed: () => Navigator.pop(dialogContext, true), child: const Text('Bağla')),
        ],
      ),
    );
    if (ok == true) await _guard(() => c.linkEmail(emailController.text, passwordController.text));
    emailController.dispose();
    passwordController.dispose();
  }

  Future<void> _confirmUnlink(String providerId) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('${AuthProviders.label(providerId)} yöntemini çıkar?'),
        content: const Text('Bu yöntemle artık giriş yapamazsın. En az bir giriş yöntemi bağlı kalmalıdır.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Vazgeç')),
          FilledButton(onPressed: () => Navigator.pop(dialogContext, true), child: const Text('Çıkar')),
        ],
      ),
    );
    if (ok == true) await _guard(() => c.unlinkProvider(providerId));
  }

  Future<void> _deleteAccount() async {
    final needsPassword = c.user?.signInProvider == AuthProviders.password;
    final passwordController = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Hesabı sil'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Hesabın kalıcı olarak silinir: profilin, hediye puanların, jokerlerin ve aldığın ödül kodları gider. Bu işlem geri alınamaz.',
            ),
            if (needsPassword) ...[
              const SizedBox(height: 12),
              TextField(
                key: const Key('delete-password'),
                controller: passwordController,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Parolanı gir (doğrulama)', border: OutlineInputBorder()),
              ),
            ],
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Vazgeç')),
          FilledButton(
            key: const Key('delete-confirm'),
            style: FilledButton.styleFrom(backgroundColor: Colors.red),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Hesabımı sil'),
          ),
        ],
      ),
    );
    if (ok != true) {
      passwordController.dispose();
      return;
    }
    try {
      await c.deleteAccount(password: needsPassword ? passwordController.text : null);
      if (mounted) Navigator.of(context).popUntil((route) => route.isFirst);
    } catch (e) {
      if (mounted) showSnack(context, errorMessage(e));
    } finally {
      passwordController.dispose();
    }
  }

  @override
  Widget build(BuildContext context) {
    final user = c.user;
    final linked = user?.providers ?? const <String>[];
    const known = [AuthProviders.google, AuthProviders.password];

    return Scaffold(
      appBar: AppBar(title: const Text('Ayarlar')),
      body: ListView(
        children: [
          const _Header('Giriş yöntemleri'),
          for (final id in known)
            ListTile(
              key: Key('provider-$id'),
              leading: Icon(id == AuthProviders.google ? Icons.g_mobiledata : Icons.mail_outline),
              title: Text(AuthProviders.label(id)),
              subtitle: Text(linked.contains(id) ? 'Bağlı' : 'Bağlı değil'),
              trailing: linked.contains(id)
                  ? TextButton(
                      key: Key('unlink-$id'),
                      onPressed: linked.length > 1 ? () => _confirmUnlink(id) : null,
                      child: const Text('Çıkar'),
                    )
                  : TextButton(
                      key: Key('link-$id'),
                      onPressed: () => id == AuthProviders.google ? _guard(c.linkGoogle) : _linkEmail(),
                      child: const Text('Bağla'),
                    ),
            ),
          if (linked.length <= 1)
            const Padding(
              padding: EdgeInsets.symmetric(horizontal: 16),
              child: Text('En az bir giriş yöntemi bağlı kalmalı; tek yöntem çıkarılamaz.', style: TextStyle(fontSize: 12)),
            ),
          const Divider(),
          const _Header('Bildirimler'),
          SwitchListTile(
            title: const Text('Turnuva bildirimleri'),
            value: c.getSetting('notif_tournament'),
            onChanged: (v) => c.setSetting('notif_tournament', v),
          ),
          SwitchListTile(
            title: const Text('Duyurular'),
            value: c.getSetting('notif_announcements'),
            onChanged: (v) => c.setSetting('notif_announcements', v),
          ),
          const Padding(
            padding: EdgeInsets.symmetric(horizontal: 16),
            child: Text('Hesap güvenliği ve destek yanıtları bildirimleri kapatılamaz. (Bildirimler Faz 8\'de çalışacak.)', style: TextStyle(fontSize: 12)),
          ),
          const Divider(),
          const _Header('Oyun'),
          SwitchListTile(title: const Text('Ses'), value: c.getSetting('sound'), onChanged: (v) => c.setSetting('sound', v)),
          SwitchListTile(title: const Text('Titreşim'), value: c.getSetting('vibration'), onChanged: (v) => c.setSetting('vibration', v)),
          const Divider(),
          ListTile(
            leading: const Icon(Icons.help_outline),
            title: const Text('Yardım'),
            subtitle: const Text('Destek talepleri Faz 8\'de eklenecek'),
            onTap: () => showSnack(context, 'Yardım ve destek Faz 8\'de eklenecek.'),
          ),
          ListTile(
            leading: const Icon(Icons.privacy_tip_outlined),
            title: const Text('Gizlilik politikası ve koşullar'),
            subtitle: const Text('Taslak — Faz 9\'da hazırlanacak'),
            onTap: () => showSnack(context, 'Yasal metinler Faz 9\'da eklenecek.'),
          ),
          ListTile(leading: const Icon(Icons.info_outline), title: const Text('Sürüm'), trailing: Text(c.appVersion, key: const Key('app-version'))),
          if (kDebugMode)
            ListTile(
              leading: const Icon(Icons.bug_report_outlined),
              title: const Text('Geliştirici: sunucu bağlantı denemesi'),
              onTap: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => const DevConnectionPage())),
            ),
          const Divider(),
          ListTile(
            key: const Key('sign-out'),
            leading: const Icon(Icons.logout),
            title: const Text('Çıkış yap'),
            onTap: () async {
              await c.signOut();
              if (context.mounted) Navigator.of(context).popUntil((route) => route.isFirst);
            },
          ),
          ListTile(
            key: const Key('delete-account'),
            leading: const Icon(Icons.delete_forever_outlined, color: Colors.red),
            title: const Text('Hesabı sil', style: TextStyle(color: Colors.red)),
            onTap: _deleteAccount,
          ),
          const SizedBox(height: 24),
        ],
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 4),
        child: Text(text, style: Theme.of(context).textTheme.titleSmall),
      );
}
