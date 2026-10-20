import 'dart:async';

import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../core/api_client.dart';
import '../widgets/common.dart';

/// Kayıt tamamlama: kullanıcı adı (değiştirilemez), görünen ad, avatar, doğum yılı, koşul onayı (docs/06 §1).
class RegistrationScreen extends StatefulWidget {
  const RegistrationScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<RegistrationScreen> createState() => _RegistrationScreenState();
}

class _RegistrationScreenState extends State<RegistrationScreen> {
  final _username = TextEditingController();
  final _displayName = TextEditingController();
  Timer? _debounce;

  bool _checking = false;
  bool? _usernameOk;
  String? _usernameMessage;

  List<AvatarInfo> _avatars = [];
  String? _avatarsError;
  int? _avatarId;
  int? _birthYear;
  bool _terms = false;
  bool _busy = false;

  AppController get c => widget.controller;

  @override
  void initState() {
    super.initState();
    _loadAvatars();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _username.dispose();
    _displayName.dispose();
    super.dispose();
  }

  Future<void> _loadAvatars() async {
    try {
      final avatars = await c.getAvatars();
      if (!mounted) return;
      setState(() {
        _avatars = avatars.where((a) => a.price == 0).toList();
        _avatarsError = null;
        _avatarId ??= _avatars.isEmpty ? null : _avatars.first.id;
      });
    } catch (e) {
      if (mounted) setState(() => _avatarsError = errorMessage(e));
    }
  }

  void _onUsernameChanged(String value) {
    _debounce?.cancel();
    setState(() {
      _usernameOk = null;
      _usernameMessage = null;
      _checking = value.trim().isNotEmpty;
    });
    if (value.trim().isEmpty) return;

    // Her tuşta değil, yazmayı bıraktıktan kısa süre sonra sunucuya sor
    _debounce = Timer(const Duration(milliseconds: 400), () async {
      try {
        final result = await c.api.checkUsername(value);
        if (!mounted || _username.text != value) return;
        setState(() {
          _checking = false;
          _usernameOk = result.available;
          _usernameMessage = result.available ? 'Kullanılabilir ✓' : result.message;
        });
      } catch (e) {
        if (!mounted) return;
        setState(() {
          _checking = false;
          _usernameOk = false;
          _usernameMessage = errorMessage(e);
        });
      }
    });
  }

  bool get _canSubmit =>
      !_busy &&
      _usernameOk == true &&
      _displayName.text.trim().length >= 2 &&
      _avatarId != null &&
      _birthYear != null &&
      _terms;

  Future<void> _submit() async {
    setState(() => _busy = true);
    try {
      await c.completeRegistration(
        username: _username.text.trim(),
        displayName: _displayName.text.trim(),
        avatarId: _avatarId!,
        birthYear: _birthYear!,
        termsAccepted: _terms,
      );
    } catch (e) {
      if (mounted) showSnack(context, errorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _showTerms() {
    showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Gizlilik politikası ve koşullar'),
        content: const Text(
          'Bu bir TASLAK metindir. Gizlilik politikası, kullanım koşulları ve turnuva/ödül şartları yayın öncesinde (Faz 9) hazırlanıp buraya eklenecek.',
        ),
        actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Kapat'))],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final currentYear = DateTime.now().year;
    final years = [for (var y = currentYear - 13; y >= currentYear - 100; y--) y];

    return Scaffold(
      appBar: AppBar(
        title: const Text('Profilini oluştur'),
        actions: [TextButton(onPressed: c.signOut, child: const Text('Çıkış'))],
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            TextField(
              key: const Key('username-field'),
              controller: _username,
              onChanged: _onUsernameChanged,
              autocorrect: false,
              decoration: InputDecoration(
                labelText: 'Kullanıcı adı',
                helperText: 'Sonradan değiştirilemez. 3–16 karakter: küçük harf, rakam, alt çizgi.',
                helperMaxLines: 2,
                border: const OutlineInputBorder(),
                suffixIcon: _checking
                    ? const Padding(padding: EdgeInsets.all(12), child: SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)))
                    : _usernameOk == null
                        ? null
                        : Icon(_usernameOk! ? Icons.check_circle : Icons.error, color: _usernameOk! ? Colors.green : Colors.red),
              ),
            ),
            if (_usernameMessage != null)
              Padding(
                padding: const EdgeInsets.only(top: 4, left: 12),
                child: Text(
                  _usernameMessage!,
                  key: const Key('username-message'),
                  style: TextStyle(color: _usernameOk == true ? Colors.green : Colors.red),
                ),
              ),
            const SizedBox(height: 16),
            TextField(
              key: const Key('displayname-field'),
              controller: _displayName,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: 'Görünen ad',
                helperText: 'Sonradan değiştirebilirsin. 2–24 karakter.',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 16),
            Text('Avatar seç', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            if (_avatarsError != null)
              Text(_avatarsError!, style: const TextStyle(color: Colors.red))
            else
              Wrap(
                spacing: 8,
                children: [
                  for (final a in _avatars)
                    GestureDetector(
                      key: Key('avatar-${a.id}'),
                      onTap: () => setState(() => _avatarId = a.id),
                      child: AvatarCircle(colorHex: a.colorHex, label: a.name.replaceAll('Avatar ', ''), selected: _avatarId == a.id),
                    ),
                ],
              ),
            const SizedBox(height: 16),
            DropdownButtonFormField<int>(
              key: const Key('birthyear-field'),
              initialValue: _birthYear,
              decoration: const InputDecoration(labelText: 'Doğum yılı', border: OutlineInputBorder()),
              items: [for (final y in years) DropdownMenuItem(value: y, child: Text('$y'))],
              onChanged: (v) => setState(() => _birthYear = v),
            ),
            const SizedBox(height: 8),
            CheckboxListTile(
              key: const Key('terms-checkbox'),
              value: _terms,
              onChanged: (v) => setState(() => _terms = v ?? false),
              controlAffinity: ListTileControlAffinity.leading,
              contentPadding: EdgeInsets.zero,
              title: const Text('Gizlilik politikasını ve kullanım koşullarını kabul ediyorum'),
              subtitle: GestureDetector(
                onTap: _showTerms,
                child: Text('Metni oku', style: TextStyle(color: Theme.of(context).colorScheme.primary, decoration: TextDecoration.underline)),
              ),
            ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('register-button'),
              onPressed: _canSubmit ? _submit : null,
              child: _busy ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Kaydı tamamla'),
            ),
          ],
        ),
      ),
    );
  }
}
