import 'package:flutter/material.dart';

import '../core/api_client.dart';
import 'play_tab.dart';

/// Branş / KPSS / Fan modlarında alt dal seçimi (docs/06): listenin başında "Karışık", sonra yayındaki alt dallar.
class CategoryScreen extends StatelessWidget {
  const CategoryScreen({super.key, required this.mode});

  final CatalogMode mode;

  Color? _color(String? hex) {
    if (hex == null || !RegExp(r'^#[0-9a-fA-F]{6}$').hasMatch(hex)) return null;
    return Color(int.parse('FF${hex.substring(1)}', radix: 16));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(mode.name)),
      body: mode.categories.isEmpty
          ? const Center(child: Text('Bu modda henüz alt dal yok.', key: Key('no-categories')))
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                if (mode.mixedAvailable)
                  Card(
                    child: ListTile(
                      key: const Key('category-mixed'),
                      leading: const Icon(Icons.shuffle, size: 32),
                      title: const Text('Karışık'),
                      subtitle: const Text('Tüm alt dallardan karışık sorular'),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => Navigator.of(context).pop(GameSelection(mode: mode)),
                    ),
                  ),
                for (final category in mode.categories)
                  Card(
                    child: ListTile(
                      key: Key('category-${category.id}'),
                      leading: CircleAvatar(
                        backgroundColor: _color(category.colorHex) ?? Theme.of(context).colorScheme.primaryContainer,
                        child: Text(category.name.characters.first.toUpperCase()),
                      ),
                      title: Text(category.name),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => Navigator.of(context).pop(GameSelection(mode: mode, category: category)),
                    ),
                  ),
              ],
            ),
    );
  }
}
