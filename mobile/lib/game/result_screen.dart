import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../widgets/common.dart';
import 'game_models.dart';

const _letters = ['A', 'B', 'C', 'D'];

const _reasonText = {
  'completed': 'Tebrikler, 10 sorunun hepsini bildin!',
  'eliminated': 'Yanlış cevapla elendin.',
  'forfeit': 'Maçtan çıktın.',
  'abandoned': 'Maç bağlantı kopması nedeniyle iptal edildi.',
};

/// Maç sonu: puan, doğru sayısı ve soru incelemesi (açıklamalarla). Soru bildirme buradan da yapılabilir.
class ResultScreen extends StatefulWidget {
  const ResultScreen({super.key, required this.api, required this.matchId, this.onPlayAgain});

  final ApiClient api;
  final String matchId;
  final VoidCallback? onPlayAgain;

  @override
  State<ResultScreen> createState() => _ResultScreenState();
}

class _ResultScreenState extends State<ResultScreen> {
  late Future<MatchSummary> _summary;

  @override
  void initState() {
    super.initState();
    _summary = widget.api.fetchSummary(widget.matchId);
  }

  Future<void> _report(SummaryItem item) async {
    final controller = TextEditingController();
    final reason = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Soru ${item.position} bildir'),
        content: TextField(controller: controller, maxLength: 300, decoration: const InputDecoration(hintText: 'Neyi yanlış buldun?')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Vazgeç')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text), child: const Text('Gönder')),
        ],
      ),
    );
    if (reason == null || reason.trim().isEmpty || !mounted) return;
    try {
      await widget.api.reportQuestion(widget.matchId, item.position, reason.trim());
      if (mounted) showSnack(context, 'Bildirimin için teşekkürler.');
    } catch (e) {
      if (mounted) showSnack(context, errorMessage(e));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Maç sonucu'), automaticallyImplyLeading: false),
      body: SafeArea(
        child: FutureBuilder<MatchSummary>(
          future: _summary,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
            if (snapshot.hasError) {
              return MessageBody(
                icon: Icons.error_outline,
                title: 'Sonuç alınamadı',
                message: errorMessage(snapshot.error!),
                actions: [FilledButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Ana sayfa'))],
              );
            }
            final summary = snapshot.data!;
            return ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Text('${summary.points} puan', key: const Key('result-points'), style: Theme.of(context).textTheme.displaySmall, textAlign: TextAlign.center),
                const SizedBox(height: 4),
                Text('${summary.correctCount} / 10 doğru', key: const Key('result-correct'), textAlign: TextAlign.center),
                const SizedBox(height: 4),
                Text(_reasonText[summary.reason] ?? '', textAlign: TextAlign.center),
                const SizedBox(height: 16),
                Row(
                  children: [
                    Expanded(child: FilledButton(key: const Key('result-home'), onPressed: () => Navigator.of(context).pop(), child: const Text('Ana sayfa'))),
                    if (widget.onPlayAgain != null) ...[
                      const SizedBox(width: 8),
                      Expanded(child: OutlinedButton(key: const Key('result-again'), onPressed: widget.onPlayAgain, child: const Text('Tekrar oyna'))),
                    ],
                  ],
                ),
                const Divider(height: 32),
                for (final item in summary.items) _review(item),
              ],
            );
          },
        ),
      ),
    );
  }

  Widget _review(SummaryItem item) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(item.isCorrect ? Icons.check_circle : Icons.cancel, color: item.isCorrect ? Colors.green : Colors.red),
                const SizedBox(width: 8),
                Expanded(child: Text('${item.position}. ${item.text}', style: const TextStyle(fontWeight: FontWeight.bold))),
                IconButton(tooltip: 'Bildir', onPressed: () => _report(item), icon: const Icon(Icons.flag_outlined)),
              ],
            ),
            for (var i = 0; i < item.options.length; i++)
              Text(
                '${_letters[i]}) ${item.options[i]}${i == item.correctOption ? '  ✓' : ''}${i == item.chosenOption || i == item.secondOption ? '  (senin cevabın)' : ''}',
                style: TextStyle(color: i == item.correctOption ? Colors.green.shade800 : null, fontWeight: i == item.correctOption ? FontWeight.bold : null),
              ),
            if (item.timedOut) const Text('Süre doldu', style: TextStyle(fontStyle: FontStyle.italic)),
            if (item.replaced) const Text('Reklamla devam: bu soru yenisiyle değiştirildi', style: TextStyle(fontStyle: FontStyle.italic)),
            if (item.explanation != null && item.explanation!.isNotEmpty) Padding(padding: const EdgeInsets.only(top: 4), child: Text(item.explanation!)),
          ],
        ),
      ),
    );
  }
}
