import 'package:flutter/material.dart';

import '../ads/rewarded_ad_service.dart';
import '../core/api_client.dart';
import '../widgets/common.dart';
import 'game_controller.dart';
import 'game_models.dart';
import 'result_screen.dart';

const _letters = ['A', 'B', 'C', 'D'];

const _jokerLabels = {'half': 'Yarı yarıya', 'double': 'Çift cevap', 'hint': 'İpucu'};

/// Bireysel maç ekranı (ham, işlevsel arayüz — tasarım en sona, K-53).
class GameScreen extends StatefulWidget {
  const GameScreen({super.key, required this.controller});

  final GameController controller;

  @override
  State<GameScreen> createState() => _GameScreenState();
}

class _GameScreenState extends State<GameScreen> {
  bool _navigated = false;

  GameController get c => widget.controller;

  @override
  void initState() {
    super.initState();
    c.addListener(_onChanged);
    c.start();
  }

  @override
  void dispose() {
    c.removeListener(_onChanged);
    c.dispose();
    super.dispose();
  }

  void _onChanged() {
    if (!mounted) return;
    setState(() {});
    if (c.phase == GamePhase.finished && !_navigated) {
      _navigated = true;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        Navigator.of(context).pushReplacement(MaterialPageRoute(
          builder: (_) => ResultScreen(api: c.api, matchId: c.state.matchId),
        ));
      });
    }
  }

  Future<void> _confirmForfeit() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Maçtan çıkılsın mı?'),
        content: const Text('Şu ana kadarki doğru sayına göre puan hesaplanır.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Devam et')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Çık')),
        ],
      ),
    );
    if (ok == true) await c.forfeit();
  }

  Future<void> _report() async {
    final controller = TextEditingController();
    final reason = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Soruyu bildir'),
        content: TextField(controller: controller, maxLength: 300, decoration: const InputDecoration(hintText: 'Neyi yanlış buldun?')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Vazgeç')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text), child: const Text('Gönder')),
        ],
      ),
    );
    if (reason == null || reason.trim().isEmpty || !mounted) return;
    final ok = await c.report(reason.trim());
    if (mounted) showSnack(context, ok ? 'Bildirimin için teşekkürler.' : (c.error ?? 'Gönderilemedi.'));
  }

  @override
  Widget build(BuildContext context) {
    final phase = c.phase;
    final q = c.question;
    final outcome = c.outcome;
    final total = c.state.total;
    final showingResult = phase == GamePhase.result && outcome != null;
    // Sonuç gösterilirken başlıktaki sıra, az önce cevaplanan sorunun sırasıdır
    final position = showingResult ? outcome.position : (q?.position ?? c.state.position);

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop && c.phase != GamePhase.finished) _confirmForfeit();
      },
      child: Scaffold(
        appBar: AppBar(
          title: Text('Soru $position / $total', key: const Key('game-title')),
          automaticallyImplyLeading: false,
          actions: [
            Center(child: Padding(padding: const EdgeInsets.only(right: 8), child: Text('Doğru: ${c.state.correctCount}', key: const Key('game-correct')))),
            IconButton(tooltip: 'Soruyu bildir', onPressed: phase == GamePhase.finished ? null : _report, icon: const Icon(Icons.flag_outlined)),
            TextButton(onPressed: phase == GamePhase.finished ? null : _confirmForfeit, child: const Text('Pes')),
          ],
        ),
        body: SafeArea(child: _body(phase, q, outcome)),
      ),
    );
  }

  Widget _body(GamePhase phase, QuestionView? q, AnswerOutcome? outcome) {
    if (phase == GamePhase.continueOffer) return _continueOffer();
    if (q == null && outcome == null) return const Center(child: CircularProgressIndicator());

    final showingResult = phase == GamePhase.result && outcome != null;
    // Sonuç sırasında gösterilen soru metni: az önce cevaplanan (state'te sıradaki soru olabilir)
    final text = showingResult ? (c.lastQuestionText ?? q?.text ?? '') : (q?.text ?? '');
    final options = showingResult ? (c.lastQuestionOptions ?? q?.options ?? const <String>[]) : (q?.options ?? const <String>[]);

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        if (phase == GamePhase.answering || phase == GamePhase.waiting) _timer(phase),
        if (showingResult) _resultBanner(outcome),
        const SizedBox(height: 12),
        Text(text, key: const Key('question-text'), style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 16),
        for (var i = 0; i < options.length; i++) _optionButton(i, options[i], phase, outcome),
        const SizedBox(height: 12),
        if (phase == GamePhase.waiting) Center(child: Text('Hazır ol… ${(c.untilStart.inMilliseconds / 1000).ceil()}', key: const Key('waiting-label'))),
        if (!showingResult) _jokerBar(),
        if (c.error != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(c.error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
      ],
    );
  }

  Widget _timer(GamePhase phase) {
    final q = c.question!;
    final total = q.endsAt.difference(q.startsAt).inMilliseconds;
    final left = phase == GamePhase.waiting ? total : c.remaining.inMilliseconds;
    return Column(
      children: [
        LinearProgressIndicator(value: total == 0 ? 0 : left / total, minHeight: 10, key: const Key('timer-bar')),
        const SizedBox(height: 4),
        Text('${(left / 1000).ceil()} sn', key: const Key('timer-label')),
      ],
    );
  }

  Widget _resultBanner(AnswerOutcome outcome) {
    final scheme = Theme.of(context).colorScheme;
    final label = outcome.timedOut ? 'Süre doldu' : (outcome.isCorrect ? 'Doğru!' : 'Yanlış');
    return Card(
      color: outcome.isCorrect ? Colors.green.shade100 : scheme.errorContainer,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(label, key: const Key('result-label'), style: Theme.of(context).textTheme.titleMedium),
            if (outcome.explanation != null && outcome.explanation!.isNotEmpty) Text(outcome.explanation!),
          ],
        ),
      ),
    );
  }

  Widget _optionButton(int index, String label, GamePhase phase, AnswerOutcome? outcome) {
    final showingResult = phase == GamePhase.result && outcome != null;
    Color? color;
    var enabled = phase == GamePhase.answering && !c.busy && !c.removedOptions.contains(index);
    if (showingResult) {
      enabled = false;
      if (index == outcome.correctOption) {
        color = Colors.green.shade300;
      } else if (index == outcome.chosenOption || index == outcome.secondOption) {
        color = Colors.red.shade300;
      }
    } else if (c.selected.contains(index)) {
      color = Colors.orange.shade300;
    }
    final removed = !showingResult && c.removedOptions.contains(index);
    final hinted = !showingResult && c.hintOption == index;

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: FilledButton.tonal(
        key: Key('option-$index'),
        style: FilledButton.styleFrom(
          backgroundColor: removed ? Colors.grey.shade300 : color,
          alignment: Alignment.centerLeft,
          padding: const EdgeInsets.all(16),
        ),
        onPressed: enabled ? () => c.select(index) : null,
        child: Row(
          children: [
            Text('${_letters[index]}  '),
            Expanded(child: Text(removed ? '—' : label)),
            if (hinted) const Icon(Icons.lightbulb, color: Colors.amber),
          ],
        ),
      ),
    );
  }

  Widget _jokerBar() {
    final b = c.balances;
    return Wrap(
      spacing: 8,
      alignment: WrapAlignment.center,
      children: [
        for (final type in ['half', 'double', 'hint'])
          OutlinedButton(
            key: Key('joker-$type'),
            onPressed: c.canUseJoker(type) ? () => c.useJoker(type) : null,
            child: Text('${_jokerLabels[type]} (${b.of(type)})'),
          ),
      ],
    );
  }

  Widget _continueOffer() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(c.outcome?.timedOut == true ? 'Süre doldu' : 'Yanlış cevap', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 8),
            Text('${c.state.correctCount} doğrun var.', key: const Key('continue-score')),
            const SizedBox(height: 16),
            if (c.canContinue) ...[
              const Text('Bir reklam izleyip yerine yeni (kolay) bir soruyla devam edebilirsin.', textAlign: TextAlign.center),
              const SizedBox(height: 12),
              FilledButton(
                key: const Key('continue-ad'),
                onPressed: c.busy ? null : () async {
                  final ok = await c.continueWithAd();
                  if (!ok && mounted && c.error != null) showSnack(context, c.error!);
                },
                child: const Text('Reklam izle ve devam et'),
              ),
              const SizedBox(height: 8),
            ],
            OutlinedButton(key: const Key('continue-decline'), onPressed: c.busy ? null : c.declineContinue, child: const Text('Maçı bitir')),
            if (c.error != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(c.error!)),
          ],
        ),
      ),
    );
  }
}

/// Mod/alt dal seçiminden maçı başlatıp oyun ekranını açar. Devam eden bir maç varsa ona döner.
Future<void> startSoloGame(BuildContext context, ApiClient api, RewardedAdService ads, String mode, int? categoryId) async {
  final navigator = Navigator.of(context);
  final messenger = ScaffoldMessenger.of(context);
  try {
    MatchState? state;
    try {
      state = await api.startSolo(mode, categoryId);
    } on ApiException catch (e) {
      if (e.code != 'MATCH_IN_PROGRESS') rethrow;
      state = await api.fetchCurrentMatch();
    }
    if (state == null) return;
    navigator.push(MaterialPageRoute(builder: (_) => GameScreen(controller: GameController(api: api, ads: ads, initial: state!))));
  } on ApiException catch (e) {
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(e.message)));
  }
}
