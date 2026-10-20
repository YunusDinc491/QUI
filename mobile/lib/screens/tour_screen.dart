import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../widgets/common.dart';

/// İlk kayıttan sonra bir kez gösterilen 3 ekranlık "nasıl oynanır" turu; atlanabilir (docs/06 §1).
class TourScreen extends StatefulWidget {
  const TourScreen({super.key, required this.controller});

  final AppController controller;

  @override
  State<TourScreen> createState() => _TourScreenState();
}

class _TourScreenState extends State<TourScreen> {
  final _pageController = PageController();
  int _page = 0;

  static const _pages = [
    (Icons.quiz_outlined, 'Bilgini göster', '10 soru, her soru için 15 saniye. Doğru cevaplar sana puan kazandırır.'),
    (Icons.people_outline, 'Rakibe karşı oyna', 'Rastgele bir rakiple, arkadaşınla ya da tek başına oyna. Jokerlerini akıllıca kullan.'),
    (Icons.emoji_events_outlined, 'Turnuvaya katıl', 'Her 2 günde bir sıralamanın ilk 10\'una giren hediye puanı kazanır; mağazadan ödüle çevir.'),
  ];

  @override
  void dispose() {
    _pageController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isLast = _page == _pages.length - 1;
    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(key: const Key('tour-skip'), onPressed: widget.controller.finishTour, child: const Text('Atla')),
            ),
            Expanded(
              child: PageView(
                controller: _pageController,
                onPageChanged: (i) => setState(() => _page = i),
                children: [for (final p in _pages) MessageBody(icon: p.$1, title: p.$2, message: p.$3)],
              ),
            ),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                for (var i = 0; i < _pages.length; i++)
                  Container(
                    margin: const EdgeInsets.all(4),
                    width: 8,
                    height: 8,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: i == _page ? Theme.of(context).colorScheme.primary : Colors.grey.shade400,
                    ),
                  ),
              ],
            ),
            Padding(
              padding: const EdgeInsets.all(16),
              child: FilledButton(
                key: const Key('tour-next'),
                onPressed: () {
                  if (isLast) {
                    widget.controller.finishTour();
                  } else {
                    _pageController.nextPage(duration: const Duration(milliseconds: 250), curve: Curves.easeOut);
                  }
                },
                child: Text(isLast ? 'Başla' : 'İleri'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
