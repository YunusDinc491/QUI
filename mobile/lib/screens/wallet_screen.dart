import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../game/tournament_models.dart';
import '../widgets/common.dart';

/// Hediye puanı bakiyesi, joker sayıları ve "Puan geçmişi" (docs/07 §4). Veriler defterden gelir.
class WalletScreen extends StatefulWidget {
  const WalletScreen({super.key, required this.api});

  final ApiClient api;

  @override
  State<WalletScreen> createState() => _WalletScreenState();
}

class _WalletScreenState extends State<WalletScreen> {
  late final Future<(WalletInfo, List<PointsHistoryItem>)> _future = _load();

  Future<(WalletInfo, List<PointsHistoryItem>)> _load() async => (await widget.api.fetchWallet(), await widget.api.fetchPointsHistory());

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Hediye puanım')),
      body: SafeArea(
        child: FutureBuilder<(WalletInfo, List<PointsHistoryItem>)>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
            if (snapshot.hasError) return Center(child: Text(errorMessage(snapshot.error!), key: const Key('wallet-error')));
            final (wallet, history) = snapshot.data!;
            return ListView(padding: const EdgeInsets.all(16), children: [
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text('${wallet.balance}', key: const Key('wallet-balance'), style: Theme.of(context).textTheme.displaySmall),
                    const Text('hediye puanı'),
                    const SizedBox(height: 8),
                    Text('Jokerlerim: Yarı yarıya ${wallet.half} · Çift cevap ${wallet.doubleAnswer} · İpucu ${wallet.hint}', key: const Key('wallet-jokers')),
                  ]),
                ),
              ),
              const SizedBox(height: 8),
              Text('Puan geçmişi', style: Theme.of(context).textTheme.titleMedium),
              if (history.isEmpty) const Padding(padding: EdgeInsets.all(16), child: Text('Henüz hareket yok.', key: Key('wallet-empty'))),
              for (final h in history)
                ListTile(
                  dense: true,
                  title: Text(h.label),
                  subtitle: Text(h.note ?? '${h.createdAt.toLocal()}'.split('.').first),
                  trailing: Text('${h.amount > 0 ? '+' : ''}${h.amount}', style: TextStyle(color: h.amount >= 0 ? Colors.green.shade700 : Colors.red.shade700, fontWeight: FontWeight.bold)),
                ),
            ]);
          },
        ),
      ),
    );
  }
}
