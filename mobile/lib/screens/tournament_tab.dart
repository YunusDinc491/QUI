import 'dart:async';

import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../core/api_client.dart';
import '../game/tournament_models.dart';
import '../widgets/common.dart';
import 'wallet_screen.dart';

/// Turnuva sekmesi (docs/07 §2): bitişe geri sayım, ödül tablosu, ilk 50, kendi sıran; ilk açılışta önceki turun ilk 10'u.
class TournamentTab extends StatefulWidget {
  const TournamentTab({super.key, required this.controller});

  final AppController controller;

  @override
  State<TournamentTab> createState() => _TournamentTabState();
}

class _TournamentTabState extends State<TournamentTab> {
  late Future<TournamentInfo> _future;
  Timer? _ticker;
  Duration _offset = Duration.zero;

  ApiClient get api => widget.controller.api;

  @override
  void initState() {
    super.initState();
    _future = _load();
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) => mounted ? setState(() {}) : null);
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  Future<TournamentInfo> _load() async {
    final info = await api.fetchTournament();
    _offset = info.serverNow.difference(DateTime.now().toUtc());
    unawaited(_maybeShowPrevious());
    return info;
  }

  void _reload() => setState(() => _future = _load());

  /// Önceki turun ilk 10'u: her dönem için bir kez gösterilir.
  Future<void> _maybeShowPrevious() async {
    try {
      final previous = await api.fetchPreviousTournament();
      final id = previous.periodId;
      final userId = widget.controller.profile?.id;
      if (id == null || userId == null || previous.top.isEmpty) return;
      final key = 'previous_seen_$userId';
      if ((widget.controller.prefs.getInt(key) ?? 0) >= id) return;
      await widget.controller.prefs.setInt(key, id);
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          key: const Key('previous-dialog'),
          title: const Text('Geçen turun ilk 10\'u'),
          content: SizedBox(
            width: double.maxFinite,
            child: ListView(shrinkWrap: true, children: [
              for (final r in previous.top)
                ListTile(dense: true, leading: Text('${r.rank}.'), title: Text(r.name), subtitle: r.reward > 0 ? Text('+${r.reward} hediye puanı') : null, trailing: Text('${r.points}')),
              if (previous.me != null) ...[
                const Divider(),
                Text('Senin sıran: ${previous.me!.rank}. (${previous.me!.points} puan${previous.me!.reward > 0 ? ', +${previous.me!.reward} hediye puanı' : ''})', key: const Key('previous-me')),
              ],
            ]),
          ),
          actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Tamam'))],
        ),
      );
    } catch (_) {
      // Pop-up isteğe bağlı: hata olursa sessizce geçilir
    }
  }

  String _countdown(DateTime endsAt) {
    final left = endsAt.difference(DateTime.now().toUtc().add(_offset));
    if (left.isNegative) return 'Tur kapanıyor…';
    final h = left.inHours;
    final m = left.inMinutes % 60;
    final s = left.inSeconds % 60;
    return '${h}s ${m.toString().padLeft(2, '0')}dk ${s.toString().padLeft(2, '0')}sn';
  }

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: RefreshIndicator(
        onRefresh: () async => _reload(),
        child: FutureBuilder<TournamentInfo>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return ListView(children: const [Padding(padding: EdgeInsets.all(48), child: Center(child: CircularProgressIndicator()))]);
            }
            if (snapshot.hasError) {
              return ListView(padding: const EdgeInsets.all(24), children: [
                Text(errorMessage(snapshot.error!), key: const Key('tournament-error')),
                const SizedBox(height: 8),
                OutlinedButton(onPressed: _reload, child: const Text('Tekrar dene')),
              ]);
            }
            final info = snapshot.data!;
            return ListView(padding: const EdgeInsets.all(16), children: [
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    const Text('Tur bitişine'),
                    Text(_countdown(info.endsAt), key: const Key('tournament-countdown'), style: Theme.of(context).textTheme.headlineMedium),
                    const SizedBox(height: 8),
                    info.me == null
                        ? const Text('Bu turda henüz puanın yok. Maç oyna, sıralamaya gir!', key: Key('tournament-me'))
                        : Text('Sıran: ${info.me!.rank}. · ${info.me!.points} puan'
                            '${info.me!.reward > 0 ? ' · şu an ${info.me!.reward} hediye puanı kazanıyorsun' : ''}', key: const Key('tournament-me')),
                  ]),
                ),
              ),
              ExpansionTile(
                key: const Key('rewards-tile'),
                title: const Text('Ödül tablosu'),
                children: [for (final t in info.rewards) ListTile(dense: true, leading: Text(t.rankLabel), title: Text('${t.amount} hediye puanı'))],
              ),
              const SizedBox(height: 8),
              Text('İlk 50', style: Theme.of(context).textTheme.titleMedium),
              if (info.top.isEmpty) const Padding(padding: EdgeInsets.all(16), child: Text('Henüz kimse puan almadı.', key: Key('tournament-empty'))),
              for (final r in info.top)
                Card(
                  color: r.isMe ? Theme.of(context).colorScheme.primaryContainer : null,
                  child: ListTile(
                    key: Key('row-${r.rank}-${r.name}'),
                    leading: Text('${r.rank}.', style: const TextStyle(fontWeight: FontWeight.bold)),
                    title: Text(r.isMe ? '${r.name} (sen)' : r.name),
                    trailing: Text('${r.points}'),
                  ),
                ),
              const SizedBox(height: 8),
              OutlinedButton.icon(
                key: const Key('open-wallet'),
                icon: const Icon(Icons.account_balance_wallet_outlined),
                label: const Text('Hediye puanım ve geçmiş'),
                onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => WalletScreen(api: api))),
              ),
            ]);
          },
        ),
      ),
    );
  }
}
