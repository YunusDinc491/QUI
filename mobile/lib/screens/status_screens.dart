import 'package:flutter/material.dart';

import '../app/app_controller.dart';
import '../widgets/common.dart';

/// Açılış / sürüm-bakım kontrolü / çevrimdışı / banlı: tek mesajlı ham ekranlar (docs/06 §1, §4).
class StatusScreen extends StatelessWidget {
  const StatusScreen({super.key, required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    return Scaffold(body: SafeArea(child: _body(context)));
  }

  Widget _body(BuildContext context) {
    switch (controller.stage) {
      case AppStage.offline:
        return MessageBody(
          icon: Icons.wifi_off,
          title: 'İnternet bağlantısı gerekli',
          message: 'Oynamak için internete bağlı olmalısın. Bağlantını kontrol edip tekrar dene.',
          actions: [FilledButton(onPressed: controller.start, child: const Text('Tekrar dene'))],
        );
      case AppStage.updateRequired:
        return MessageBody(
          icon: Icons.system_update,
          title: 'Güncelleme gerekli',
          message:
              'Devam etmek için uygulamanın yeni sürümünü yüklemelisin. (Mevcut: ${controller.appVersion}, gereken en az: ${controller.config?.minSupportedVersion})',
          actions: [FilledButton(onPressed: controller.start, child: const Text('Tekrar kontrol et'))],
        );
      case AppStage.maintenance:
        final message = controller.config?.maintenanceMessage ?? '';
        return MessageBody(
          icon: Icons.build_circle_outlined,
          title: 'Bakımdayız',
          message: message.isEmpty ? 'Kısa süre sonra tekrar dene.' : message,
          actions: [FilledButton(onPressed: controller.start, child: const Text('Tekrar dene'))],
        );
      case AppStage.banned:
        return MessageBody(
          icon: Icons.block,
          title: 'Hesabın askıya alındı',
          message:
              'Hesabına erişim kısıtlandı. İtiraz etmek ya da bilgi almak için destek ile iletişime geçebilirsin. (Destek ekranı Faz 8\'de eklenecek.)',
          actions: [OutlinedButton(onPressed: controller.signOut, child: const Text('Çıkış yap'))],
        );
      default:
        return const Center(child: CircularProgressIndicator());
    }
  }
}
