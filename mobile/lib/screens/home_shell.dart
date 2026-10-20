import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../widgets/common.dart';
import 'profile_tab.dart';

/// Alt menülü ana kabuk: Oyna · Turnuva · Mağaza · Profil (docs/06 §2). Oyun/turnuva/mağaza içerikleri sonraki fazlarda.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key, required this.controller});

  final AppController controller;

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final pages = <Widget>[
      _PlayTab(controller: widget.controller),
      const SafeArea(
        child: MessageBody(
          icon: Icons.emoji_events_outlined,
          title: 'Turnuva',
          message: 'Sıralama ve ödüller Faz 5\'te eklenecek.',
        ),
      ),
      const SafeArea(
        child: MessageBody(
          icon: Icons.storefront_outlined,
          title: 'Mağaza',
          message: 'Kupon, joker, avatar ve çerçeveler Faz 6\'da eklenecek.',
        ),
      ),
      ProfileTab(controller: widget.controller),
    ];

    return Scaffold(
      body: IndexedStack(index: _index, children: pages),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (i) => setState(() => _index = i),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.sports_esports_outlined), selectedIcon: Icon(Icons.sports_esports), label: 'Oyna'),
          NavigationDestination(icon: Icon(Icons.emoji_events_outlined), selectedIcon: Icon(Icons.emoji_events), label: 'Turnuva'),
          NavigationDestination(icon: Icon(Icons.storefront_outlined), selectedIcon: Icon(Icons.storefront), label: 'Mağaza'),
          NavigationDestination(icon: Icon(Icons.person_outline), selectedIcon: Icon(Icons.person), label: 'Profil'),
        ],
      ),
    );
  }
}

/// Ana sayfa: yalnızca 4 mod kartı (docs/06 §2). Oyun akışı Faz 3'te.
class _PlayTab extends StatelessWidget {
  const _PlayTab({required this.controller});

  final AppController controller;

  static const _modes = [
    (Icons.public, 'Genel sorular'),
    (Icons.menu_book_outlined, 'Branş seçmeli'),
    (Icons.school_outlined, 'KPSS tarzı'),
    (Icons.favorite_outline, 'Fanlara özel'),
  ];

  @override
  Widget build(BuildContext context) {
    final name = controller.profile?.displayName ?? '';
    return SafeArea(
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text('Merhaba, $name', style: Theme.of(context).textTheme.headlineSmall, key: const Key('greeting')),
          const SizedBox(height: 16),
          for (final mode in _modes)
            Card(
              child: ListTile(
                leading: Icon(mode.$1, size: 32),
                title: Text(mode.$2),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => showSnack(context, 'Oyun ekranları Faz 3\'te eklenecek.'),
              ),
            ),
        ],
      ),
    );
  }
}
