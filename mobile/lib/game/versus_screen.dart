import 'dart:async';

import 'package:flutter/material.dart';

import '../ads/rewarded_ad_service.dart';
import '../core/api_client.dart';
import '../widgets/common.dart';
import 'game_models.dart';
import 'game_screen.dart';
import 'versus_controller.dart';
import 'versus_transport.dart';

const _letters = ['A', 'B', 'C', 'D'];
const _jokerLabels = {'half': 'Yarı yarıya', 'double': 'Çift cevap', 'hint': 'İpucu'};

/// Rakipli maç ekranı: arama → hazır → geri sayım → sorular → sonuç (ham, işlevsel arayüz; tasarım en sona, K-53).
class VersusScreen extends StatefulWidget {
  const VersusScreen({super.key, required this.controller, required this.api, required this.ads, required this.start});

  final VersusController controller;
  final ApiClient api;
  final RewardedAdService ads;

  /// Ekran açılınca bağlandıktan sonra yapılacak ilk eylem (rastgele ara / oda kur / koda katıl).
  final Future<void> Function(VersusController c) start;

  @override
  State<VersusScreen> createState() => _VersusScreenState();
}

class _VersusScreenState extends State<VersusScreen> with WidgetsBindingObserver {
  Timer? _ticker;
  bool _connecting = true;

  VersusController get c => widget.controller;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    c.addListener(_refresh);
    _ticker = Timer.periodic(const Duration(milliseconds: 250), (_) => _refresh());
    _begin();
  }

  Future<void> _begin() async {
    try {
      await c.connect();
      await widget.start(c);
    } catch (_) {
      c.error = 'Sunucuya bağlanılamadı.';
    }

    if (mounted) setState(() => _connecting = false);
  }

  void _refresh() {
    if (mounted) setState(() {});
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) c.appBackgrounded();
    if (state == AppLifecycleState.resumed) c.appForegrounded();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _ticker?.cancel();
    c.removeListener(_refresh);
    c.disconnect();
    c.dispose();
    super.dispose();
  }

  bool get _inMatch => const {VersusPhase.found, VersusPhase.countdown, VersusPhase.question, VersusPhase.result}.contains(c.phase);

  Future<void> _confirmLeave() async {
    if (!_inMatch) {
      if (c.phase == VersusPhase.searching) await c.cancelSearch();
      if (c.phase == VersusPhase.roomWaiting) await c.leaveRoom();
      if (mounted) Navigator.of(context).pop();
      return;
    }
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Maçtan çıkılsın mı?'),
        content: const Text('Çıkarsan maçı kaybedersin.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Devam et')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Çık')),
        ],
      ),
    );
    if (ok == true) {
      await c.forfeit();
      if (mounted) Navigator.of(context).pop();
    }
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: c.phase == VersusPhase.finished || c.phase == VersusPhase.cancelled,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _confirmLeave();
      },
      child: Scaffold(
        appBar: AppBar(
          title: Text(_title(), key: const Key('versus-title')),
          automaticallyImplyLeading: false,
          actions: [
            if (c.phase == VersusPhase.question || c.phase == VersusPhase.result)
              Center(child: Padding(padding: const EdgeInsets.only(right: 12), child: Text('${c.yourScore} – ${c.opponentScore}', key: const Key('versus-score')))),
            TextButton(onPressed: _confirmLeave, child: Text(_inMatch ? 'Pes' : 'Kapat')),
          ],
        ),
        body: SafeArea(child: _connecting ? const Center(child: CircularProgressIndicator()) : _body()),
      ),
    );
  }

  String _title() => switch (c.phase) {
        VersusPhase.searching => 'Rakip aranıyor',
        VersusPhase.searchTimedOut => 'Rakip bulunamadı',
        VersusPhase.roomWaiting => 'Arkadaş odası',
        VersusPhase.found => 'Rakip bulundu',
        VersusPhase.countdown => 'Hazır ol',
        VersusPhase.question || VersusPhase.result => 'Soru ${c.question?.position ?? ''} / ${c.question?.total ?? 10}',
        VersusPhase.finished => 'Maç bitti',
        VersusPhase.cancelled => 'Maç iptal',
        VersusPhase.idle => 'Rakipli maç',
      };

  Widget _body() {
    if (c.error != null && c.phase == VersusPhase.idle) {
      return MessageBody(icon: Icons.error_outline, title: 'Bağlanılamadı', message: c.error!, actions: [
        FilledButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Geri dön')),
      ]);
    }
    return switch (c.phase) {
      VersusPhase.idle => const Center(child: CircularProgressIndicator()),
      VersusPhase.searching => _searching(),
      VersusPhase.searchTimedOut => _timedOut(),
      VersusPhase.roomWaiting => _room(),
      VersusPhase.found => _found(),
      VersusPhase.countdown => _countdown(),
      VersusPhase.question || VersusPhase.result => _question(),
      VersusPhase.finished => _finished(),
      VersusPhase.cancelled => _cancelled(),
    };
  }

  // ---------------------------------------------------------------- Aşamalar

  Widget _searching() {
    final started = c.searchStartedAt ?? DateTime.now();
    final elapsed = DateTime.now().difference(started).inSeconds.clamp(0, 15);
    return Center(
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        const CircularProgressIndicator(),
        const SizedBox(height: 16),
        Text('Rakip aranıyor… $elapsed / 15 sn', key: const Key('search-label')),
        const SizedBox(height: 16),
        OutlinedButton(key: const Key('search-cancel'), onPressed: () async {
          await c.cancelSearch();
          if (mounted) Navigator.of(context).pop();
        }, child: const Text('Aramayı iptal et')),
      ]),
    );
  }

  Widget _timedOut() {
    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
        const Text('Şu an uygun bir rakip bulunamadı.', textAlign: TextAlign.center),
        const SizedBox(height: 16),
        FilledButton(key: const Key('fallback-bot'), onPressed: _pickBot, child: const Text('Bota karşı oyna')),
        const SizedBox(height: 8),
        OutlinedButton(key: const Key('fallback-solo'), onPressed: () async {
          final mode = c.pendingMode;
          if (await c.chooseFallback('solo') && mounted && mode != null) {
            final navigator = Navigator.of(context);
            await startSoloGame(context, widget.api, widget.ads, mode.$1, mode.$2);
            if (navigator.mounted) navigator.maybePop();
          }
        }, child: const Text('Bireysel oyna')),
        const SizedBox(height: 8),
        OutlinedButton(key: const Key('fallback-retry'), onPressed: () => c.chooseFallback('retry'), child: const Text('Tekrar ara')),
        if (c.error != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(c.error!)),
      ]),
    );
  }

  Future<void> _pickBot() async {
    List<BotInfo> bots;
    try {
      bots = await widget.api.fetchBots();
    } catch (_) {
      bots = [];
    }
    if (!mounted) return;
    final chosen = await showModalBottomSheet<String?>(
      context: context,
      builder: (context) => SafeArea(
        child: ListView(shrinkWrap: true, children: [
          const ListTile(title: Text('Hangi bota karşı oynamak istersin?')),
          ListTile(leading: const Icon(Icons.shuffle), title: const Text('Rastgele bot'), onTap: () => Navigator.pop(context, '')),
          for (final b in bots)
            ListTile(
              leading: const Icon(Icons.smart_toy_outlined),
              title: Text(b.name),
              subtitle: Text('Güç: ${b.powerLabel}'),
              onTap: () => Navigator.pop(context, b.id),
            ),
        ]),
      ),
    );
    if (chosen == null) return;
    await c.chooseFallback('bot', botId: chosen.isEmpty ? null : chosen);
  }

  Widget _room() {
    return Center(
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        const Text('Arkadaşına bu kodu gönder:'),
        const SizedBox(height: 8),
        SelectableText(c.roomCode ?? '', key: const Key('room-code'), style: Theme.of(context).textTheme.displaySmall),
        const SizedBox(height: 8),
        const Text('Oda 5 dakika geçerlidir.'),
        const SizedBox(height: 16),
        OutlinedButton(onPressed: () async {
          await c.leaveRoom();
          if (mounted) Navigator.of(context).pop();
        }, child: const Text('Odayı kapat')),
      ]),
    );
  }

  Widget _found() {
    final o = c.opponent;
    return Center(
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        Text('Rakip: ${o?.name ?? ''}${o?.isBot == true ? ' (Bot)' : ''}', key: const Key('found-opponent'), style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 16),
        FilledButton(key: const Key('ready-button'), onPressed: c.readySent ? null : c.ready, child: Text(c.readySent ? 'Rakip bekleniyor…' : 'Hazırım')),
        if (c.error != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(c.error!)),
      ]),
    );
  }

  Widget _countdown() {
    final end = c.countdownEndsAt;
    final left = end == null ? 0 : (end.difference(c.serverNow).inMilliseconds / 1000).ceil().clamp(0, 99);
    return Center(
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        Text('${c.opponent?.name ?? 'Rakip'} ile maç başlıyor', textAlign: TextAlign.center),
        const SizedBox(height: 12),
        Text('$left', key: const Key('countdown-number'), style: Theme.of(context).textTheme.displayLarge),
      ]),
    );
  }

  Widget _question() {
    final q = c.question;
    if (q == null) return const Center(child: CircularProgressIndicator());
    final showingResult = c.phase == VersusPhase.result && c.lastResult != null;
    final result = c.lastResult;

    return ListView(padding: const EdgeInsets.all(16), children: [
      if (c.opponentDisconnectedMs != null)
        Card(
          color: Theme.of(context).colorScheme.errorContainer,
          child: Padding(
            padding: const EdgeInsets.all(8),
            child: Text('Rakibin bağlantısı koptu (${(c.opponentDisconnectedMs! / 1000).ceil()} sn bekleniyor)', key: const Key('opponent-disconnected')),
          ),
        ),
      if (!showingResult) ...[
        LinearProgressIndicator(value: c.remaining.inMilliseconds / 15000, minHeight: 10, key: const Key('versus-timer')),
        Align(alignment: Alignment.centerRight, child: Text('${(c.remaining.inMilliseconds / 1000).ceil()} sn')),
      ],
      if (showingResult)
        Card(
          color: result!.yourCorrect ? Colors.green.shade100 : Theme.of(context).colorScheme.errorContainer,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(result.timedOut ? 'Süre doldu' : (result.yourCorrect ? 'Doğru!' : 'Yanlış'), key: const Key('versus-result-label'), style: Theme.of(context).textTheme.titleMedium),
              Text(result.opponentCorrect ? 'Rakip de doğru bildi.' : 'Rakip bilemedi.'),
              if (result.explanation != null && result.explanation!.isNotEmpty) Text(result.explanation!),
            ]),
          ),
        ),
      const SizedBox(height: 8),
      if (!showingResult && c.opponentAnswered) const Text('Rakip cevapladı', key: Key('opponent-answered')),
      Text(q.text, key: const Key('versus-question'), style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 12),
      for (var i = 0; i < q.options.length; i++) _option(i, q.options[i], showingResult ? result : null),
      const SizedBox(height: 8),
      if (!showingResult) _jokerBar(),
      if (c.error != null) Text(c.error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
    ]);
  }

  Widget _option(int index, String label, VersusRoundResult? result) {
    Color? color;
    var enabled = c.phase == VersusPhase.question && !c.answered && !c.removedOptions.contains(index);
    if (result != null) {
      enabled = false;
      if (index == result.correctOption) {
        color = Colors.green.shade300;
      } else if (index == result.yourOption || index == result.yourSecond) {
        color = Colors.red.shade300;
      }
    } else if (c.selected.contains(index)) {
      color = Colors.orange.shade300;
    }
    final removed = result == null && c.removedOptions.contains(index);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: FilledButton.tonal(
        key: Key('versus-option-$index'),
        style: FilledButton.styleFrom(backgroundColor: removed ? Colors.grey.shade300 : color, alignment: Alignment.centerLeft, padding: const EdgeInsets.all(16)),
        onPressed: enabled ? () => c.select(index) : null,
        child: Row(children: [
          Text('${_letters[index]}  '),
          Expanded(child: Text(removed ? '—' : label)),
          if (result == null && c.hintOption == index) const Icon(Icons.lightbulb, color: Colors.amber),
          if (result != null && index == result.opponentOption) const Icon(Icons.person_outline),
        ]),
      ),
    );
  }

  Widget _jokerBar() {
    final b = c.jokers;
    return Wrap(spacing: 8, alignment: WrapAlignment.center, children: [
      for (final type in ['half', 'double', 'hint'])
        OutlinedButton(
          key: Key('versus-joker-$type'),
          onPressed: c.canUseJoker(type) ? () => c.useJoker(type) : null,
          child: Text('${_jokerLabels[type]} (${b?.of(type) ?? 0})'),
        ),
    ]);
  }

  Widget _finished() {
    final f = c.finish!;
    final label = switch (f.result) { 'win' => 'Kazandın!', 'lose' => 'Kaybettin', _ => 'Berabere' };
    final why = switch (f.reason) {
      'forfeit' => f.result == 'win' ? 'Rakip maçtan çıktı.' : 'Maçtan çıktın.',
      'disconnect' => f.result == 'win' ? 'Rakibin bağlantısı koptu.' : 'Bağlantı süren doldu.',
      _ => '',
    };
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Text(label, key: const Key('versus-final'), style: Theme.of(context).textTheme.displaySmall),
          const SizedBox(height: 8),
          Text('${f.yourCorrect} – ${f.opponentCorrect}', key: const Key('versus-final-score'), style: Theme.of(context).textTheme.headlineSmall),
          if (why.isNotEmpty) Text(why),
          const SizedBox(height: 8),
          Text('${f.points} puan${f.bonus > 0 ? ' (bonus +${f.bonus})' : ''}', key: const Key('versus-final-points')),
          const SizedBox(height: 16),
          if (c.rematchIncoming) ...[
            const Text('Rakip rövanş istiyor!', key: Key('rematch-incoming')),
            Row(mainAxisSize: MainAxisSize.min, children: [
              FilledButton(key: const Key('rematch-accept'), onPressed: () => c.respondRematch(true), child: const Text('Kabul et')),
              const SizedBox(width: 8),
              OutlinedButton(key: const Key('rematch-decline'), onPressed: () => c.respondRematch(false), child: const Text('Reddet')),
            ]),
          ] else
            OutlinedButton(
              key: const Key('rematch-button'),
              onPressed: c.rematchRequested ? null : c.requestRematch,
              child: Text(c.rematchRequested ? 'Rakip bekleniyor…' : 'Rövanş'),
            ),
          if (c.rematchMessage != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(c.rematchMessage!, key: const Key('rematch-message'))),
          if (c.error != null) Text(c.error!),
          const SizedBox(height: 16),
          FilledButton(key: const Key('versus-home'), onPressed: () => Navigator.of(context).pop(), child: const Text('Ana sayfa')),
        ]),
      ),
    );
  }

  Widget _cancelled() {
    final text = switch (c.cancelReason) {
      'not_ready' => 'Rakip hazır olmadığı için maç iptal edildi.',
      'both_disconnected' => 'İki oyuncunun da bağlantısı koptuğu için maç iptal edildi. Puan değişmedi.',
      'server_restart' => 'Sunucu yeniden başladığı için maç iptal edildi. Puan değişmedi.',
      'expired' => 'Odanın süresi doldu.',
      'session_closed' => 'Hesabına başka bir cihazdan giriş yapıldı.',
      _ => 'Maç iptal edildi. Puan değişmedi.',
    };
    return MessageBody(icon: Icons.cancel_outlined, title: 'Maç iptal', message: text, actions: [
      FilledButton(key: const Key('versus-cancel-home'), onPressed: () => Navigator.of(context).pop(), child: const Text('Ana sayfa')),
    ]);
  }
}

/// Rakipli maç ekranını açar (SignalR bağlantısı ekran ömrü boyunca açık kalır).
Future<void> openVersusScreen(BuildContext context, ApiClient api, RewardedAdService ads, Future<void> Function(VersusController c) start,
    {VersusTransport? transport}) {
  final controller = VersusController(
    transport: transport ?? SignalRTransport(baseUrl: api.baseUrl, deviceId: api.deviceId, authHeaderProvider: api.authHeaderProvider),
  );
  return Navigator.of(context).push(MaterialPageRoute(builder: (_) => VersusScreen(controller: controller, api: api, ads: ads, start: start)));
}
