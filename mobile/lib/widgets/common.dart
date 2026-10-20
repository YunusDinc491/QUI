import 'package:flutter/material.dart';

import '../auth/auth_gateway.dart';
import '../core/api_client.dart';

/// Hata nesnesinden kullanıcıya gösterilecek Türkçe mesajı çıkarır.
String errorMessage(Object error) {
  if (error is AuthException) return error.message;
  if (error is ApiException) return error.message;
  return 'Beklenmeyen bir hata oluştu.';
}

void showSnack(BuildContext context, String message) {
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));
}

/// "#RRGGBB" → Color
Color colorFromHex(String hex) {
  final cleaned = hex.replaceAll('#', '');
  final value = int.tryParse(cleaned.length == 6 ? 'FF$cleaned' : cleaned, radix: 16) ?? 0xFF9E9E9E;
  return Color(value);
}

/// Ham avatar: renkli daire + adın baş harfi (gerçek çizimler tasarım aşamasında gelecek).
class AvatarCircle extends StatelessWidget {
  const AvatarCircle({super.key, required this.colorHex, required this.label, this.radius = 28, this.selected = false});

  final String colorHex;
  final String label;
  final double radius;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    final initial = label.isEmpty ? '?' : label.characters.first.toUpperCase();
    return Container(
      padding: const EdgeInsets.all(3),
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(color: selected ? Theme.of(context).colorScheme.primary : Colors.transparent, width: 3),
      ),
      child: CircleAvatar(
        radius: radius,
        backgroundColor: colorFromHex(colorHex),
        child: Text(initial, style: TextStyle(fontSize: radius * 0.8, color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }
}

/// Ortalanmış, tek mesajlı basit ekran gövdesi (bakım, çevrimdışı vb.).
class MessageBody extends StatelessWidget {
  const MessageBody({super.key, required this.icon, required this.title, required this.message, this.actions = const []});

  final IconData icon;
  final String title;
  final String message;
  final List<Widget> actions;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 64),
            const SizedBox(height: 16),
            Text(title, style: Theme.of(context).textTheme.headlineSmall, textAlign: TextAlign.center),
            const SizedBox(height: 8),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 24),
            ...actions,
          ],
        ),
      ),
    );
  }
}
