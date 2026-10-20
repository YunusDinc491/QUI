import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../core/api_client.dart';
import '../widgets/common.dart';
import 'settings_screen.dart';

/// Profil sekmesi: avatar, ad, kullanıcı adı, toplam puan, düzenleme ve ayarlar (docs/06 §2).
class ProfileTab extends StatelessWidget {
  const ProfileTab({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final profile = controller.profile;
    if (profile == null) return const SizedBox.shrink();

    return SafeArea(
      child: FutureBuilder<List<AvatarInfo>>(
        future: controller.getAvatars(),
        builder: (context, snapshot) {
          final avatars = snapshot.data ?? const <AvatarInfo>[];
          final avatar = avatars.where((a) => a.id == profile.avatarId).firstOrNull;
          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Center(child: AvatarCircle(colorHex: avatar?.colorHex ?? '#9E9E9E', label: profile.displayName, radius: 44)),
              const SizedBox(height: 12),
              Text(profile.displayName,
                  key: const Key('profile-displayname'), textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineSmall),
              Text('@${profile.username}', key: const Key('profile-username'), textAlign: TextAlign.center),
              const SizedBox(height: 16),
              Card(
                child: ListTile(
                  leading: const Icon(Icons.star_outline),
                  title: const Text('Toplam puan'),
                  trailing: Text('${profile.totalScore}', key: const Key('profile-score')),
                ),
              ),
              const Card(
                child: ListTile(
                  leading: Icon(Icons.card_giftcard_outlined),
                  title: Text('Hediye puanı'),
                  subtitle: Text('Turnuva ve mağaza Faz 5–6\'da eklenecek'),
                  trailing: Text('0'),
                ),
              ),
              const Card(
                child: ListTile(
                  leading: Icon(Icons.history),
                  title: Text('Son oynanan maçlar'),
                  subtitle: Text('Maçlar Faz 3–4\'te eklenecek'),
                ),
              ),
              const SizedBox(height: 8),
              OutlinedButton.icon(
                key: const Key('edit-profile'),
                onPressed: () => _showEditDialog(context, avatars),
                icon: const Icon(Icons.edit_outlined),
                label: const Text('Profili düzenle'),
              ),
              const SizedBox(height: 8),
              FilledButton.tonalIcon(
                key: const Key('open-settings'),
                onPressed: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => SettingsScreen(controller: controller))),
                icon: const Icon(Icons.settings_outlined),
                label: const Text('Ayarlar'),
              ),
            ],
          );
        },
      ),
    );
  }

  Future<void> _showEditDialog(BuildContext context, List<AvatarInfo> avatars) async {
    final profile = controller.profile!;
    final nameController = TextEditingController(text: profile.displayName);
    var selected = profile.avatarId;
    final free = avatars.where((a) => a.price == 0).toList();

    await showDialog<void>(
      context: context,
      builder: (dialogContext) {
        var saving = false;
        return StatefulBuilder(
          builder: (context, setState) => AlertDialog(
            title: const Text('Profili düzenle'),
            content: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  TextField(
                    key: const Key('edit-displayname'),
                    controller: nameController,
                    decoration: const InputDecoration(labelText: 'Görünen ad', border: OutlineInputBorder()),
                  ),
                  const SizedBox(height: 12),
                  Wrap(
                    spacing: 6,
                    children: [
                      for (final a in free)
                        GestureDetector(
                          key: Key('edit-avatar-${a.id}'),
                          onTap: () => setState(() => selected = a.id),
                          child: AvatarCircle(colorHex: a.colorHex, label: a.name.replaceAll('Avatar ', ''), radius: 22, selected: selected == a.id),
                        ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  const Text('Kullanıcı adın değiştirilemez.', style: TextStyle(fontSize: 12)),
                ],
              ),
            ),
            actions: [
              TextButton(onPressed: saving ? null : () => Navigator.pop(dialogContext), child: const Text('Vazgeç')),
              FilledButton(
                key: const Key('edit-save'),
                onPressed: saving
                    ? null
                    : () async {
                        setState(() => saving = true);
                        try {
                          await controller.updateProfile(
                            displayName: nameController.text.trim() == profile.displayName ? null : nameController.text.trim(),
                            avatarId: selected == profile.avatarId ? null : selected,
                          );
                          if (dialogContext.mounted) Navigator.pop(dialogContext);
                        } catch (e) {
                          if (dialogContext.mounted) showSnack(dialogContext, errorMessage(e));
                          setState(() => saving = false);
                        }
                      },
                child: const Text('Kaydet'),
              ),
            ],
          ),
        );
      },
    );
    nameController.dispose();
  }
}
