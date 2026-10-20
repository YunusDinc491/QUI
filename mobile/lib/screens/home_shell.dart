import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../widgets/common.dart';
import 'play_tab.dart';
import 'profile_tab.dart';
import 'tournament_tab.dart';

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
      PlayTab(controller: widget.controller),
      TournamentTab(controller: widget.controller),
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
