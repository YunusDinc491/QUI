import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../core/api_client.dart';
import '../game/game_models.dart';
import '../game/game_screen.dart';
import 'category_screen.dart';

/// Oyun için seçilen mod ve alt dal. [categoryId] null ise "Karışık" (modun tüm yayındaki alt dalları)
/// ya da Genel mod (tek kategori). Faz 3'te oyun başlatma bunu kullanır.
class GameSelection {
  const GameSelection({required this.mode, this.category});

  final CatalogMode mode;
  final CatalogCategory? category;

  int? get categoryId => category?.id;
  bool get isMixed => !mode.isGeneral && category == null;
}

/// Ana sayfa: sunucudan gelen 4 mod kartı (docs/06 §2). Branş/KPSS/Fan'da alt dal seçimi açılır.
class PlayTab extends StatefulWidget {
  const PlayTab({super.key, required this.controller, this.onSelected});

  final AppController controller;

  /// Seçim tamamlanınca çağrılır. Verilmezse (Faz 2) bilgi mesajı gösterilir.
  final void Function(GameSelection selection)? onSelected;

  @override
  State<PlayTab> createState() => _PlayTabState();
}

class _PlayTabState extends State<PlayTab> {
  late Future<List<CatalogMode>> _catalog;
  late Future<MatchState?> _current;

  static const _icons = {
    'general': Icons.public,
    'branch': Icons.menu_book_outlined,
    'kpss': Icons.school_outlined,
    'fan': Icons.favorite_outline,
  };

  @override
  void initState() {
    super.initState();
    _catalog = widget.controller.api.fetchCatalog();
    _current = _loadCurrent();
  }

  /// Devam eden maç var mı (uygulama kapanıp açıldıysa kaldığı yerden devam). Hata olursa sessizce yok sayılır.
  Future<MatchState?> _loadCurrent() async {
    try {
      return await widget.controller.api.fetchCurrentMatch();
    } catch (_) {
      return null;
    }
  }

  void _reload() => setState(() {
        _catalog = widget.controller.api.fetchCatalog();
        _current = _loadCurrent();
      });

  void _select(GameSelection selection) {
    final callback = widget.onSelected;
    if (callback != null) {
      callback(selection);
    } else {
      startSoloGame(context, widget.controller.api, widget.controller.ads, selection.mode.code, selection.categoryId)
          .then((_) => mounted ? setState(() => _current = _loadCurrent()) : null);
    }
  }

  Future<void> _open(CatalogMode mode) async {
    if (mode.isGeneral) {
      _select(GameSelection(mode: mode));
      return;
    }
    final selection = await Navigator.of(context).push<GameSelection>(
      MaterialPageRoute(builder: (_) => CategoryScreen(mode: mode)),
    );
    if (selection != null && mounted) _select(selection);
  }

  @override
  Widget build(BuildContext context) {
    final name = widget.controller.profile?.displayName ?? '';
    return SafeArea(
      child: RefreshIndicator(
        onRefresh: () async {
          _reload();
          await _catalog.catchError((_) => <CatalogMode>[]);
        },
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text('Merhaba, $name', style: Theme.of(context).textTheme.headlineSmall, key: const Key('greeting')),
            const SizedBox(height: 16),
            FutureBuilder<MatchState?>(
              future: _current,
              builder: (context, snapshot) {
                final match = snapshot.data;
                if (match == null) return const SizedBox.shrink();
                return Card(
                  key: const Key('resume-card'),
                  color: Theme.of(context).colorScheme.tertiaryContainer,
                  child: ListTile(
                    title: const Text('Devam eden bir maçın var'),
                    subtitle: Text('${match.position}. soru · ${match.correctCount} doğru'),
                    trailing: FilledButton(
                      onPressed: () => startSoloGame(context, widget.controller.api, widget.controller.ads, match.mode, match.categoryId)
                          .then((_) => mounted ? setState(() => _current = _loadCurrent()) : null),
                      child: const Text('Devam et'),
                    ),
                  ),
                );
              },
            ),
            FutureBuilder<List<CatalogMode>>(
              future: _catalog,
              builder: (context, snapshot) {
                if (snapshot.connectionState != ConnectionState.done) {
                  return const Padding(padding: EdgeInsets.all(32), child: Center(child: CircularProgressIndicator()));
                }
                if (snapshot.hasError) {
                  final message = snapshot.error is ApiException ? (snapshot.error as ApiException).message : 'Modlar yüklenemedi.';
                  return Column(
                    children: [
                      Text(message, key: const Key('catalog-error')),
                      const SizedBox(height: 8),
                      OutlinedButton(onPressed: _reload, child: const Text('Tekrar dene')),
                    ],
                  );
                }
                return Column(
                  children: [
                    for (final mode in snapshot.data!)
                      Card(
                        child: ListTile(
                          key: Key('mode-${mode.code}'),
                          leading: Icon(_icons[mode.code] ?? Icons.quiz_outlined, size: 32),
                          title: Text(mode.name),
                          subtitle: !mode.isGeneral && mode.categories.isEmpty ? const Text('Yakında') : null,
                          trailing: const Icon(Icons.chevron_right),
                          enabled: mode.isGeneral || mode.categories.isNotEmpty,
                          onTap: () => _open(mode),
                        ),
                      ),
                  ],
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}
