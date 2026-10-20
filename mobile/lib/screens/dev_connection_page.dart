import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:signalr_netcore/signalr_client.dart';

import '../core/api_client.dart';

/// Geliştirici aracı (yalnızca debug derlemede Ayarlar'dan açılır): sunucu sağlık kontrolü ve SignalR echo denemesi.
/// Faz 0 prototipi buraya taşındı.
class DevConnectionPage extends StatefulWidget {
  const DevConnectionPage({super.key});

  @override
  State<DevConnectionPage> createState() => _DevConnectionPageState();
}

class _DevConnectionPageState extends State<DevConnectionPage> {
  String _healthText = 'henüz kontrol edilmedi';
  String _echoText = 'henüz denenmedi';

  Future<void> _checkHealth() async {
    setState(() => _healthText = 'kontrol ediliyor...');
    try {
      final response = await http.get(Uri.parse('$apiBaseUrl/health')).timeout(const Duration(seconds: 5));
      setState(() => _healthText = 'HTTP ${response.statusCode}: ${response.body}');
    } catch (e) {
      setState(() => _healthText = 'ULAŞILAMADI ($e)');
    }
  }

  Future<void> _echo() async {
    setState(() => _echoText = 'bağlanılıyor...');
    final hub = HubConnectionBuilder()
        .withUrl(
          '$apiBaseUrl/hubs/game',
          options: HttpConnectionOptions(transport: HttpTransportType.WebSockets, skipNegotiation: true),
        )
        .build();
    try {
      await hub.start();
      final result = await hub.invoke('Echo', args: ['merhaba']);
      setState(() => _echoText = 'Sunucu yanıtı: $result');
    } catch (e) {
      setState(() => _echoText = 'HATA ($e)');
    } finally {
      await hub.stop();
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Sunucu bağlantı denemesi')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Sunucu adresi: $apiBaseUrl'),
            const SizedBox(height: 16),
            FilledButton(onPressed: _checkHealth, child: const Text('Sunucuyu kontrol et')),
            Text(_healthText, key: const Key('health-text')),
            const SizedBox(height: 16),
            FilledButton(onPressed: _echo, child: const Text('SignalR Echo gönder')),
            Text(_echoText, key: const Key('echo-text')),
          ],
        ),
      ),
    );
  }
}
