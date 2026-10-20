import 'package:signalr_netcore/signalr_client.dart';

/// Oyun hub'ına bağlantı soyutlaması (testlerde sahte gerçeklemeyle değiştirilir).
abstract class VersusTransport {
  Future<void> start();
  Future<void> stop();

  /// Hub metodunu çağırır; sunucunun `{ok, code, message, data}` yanıtını Map olarak döndürür.
  Future<Map<String, dynamic>> invoke(String method, [List<Object?> args = const []]);

  /// Sunucudan gelen olayı dinler. İlk argüman (JSON nesnesi) Map olarak iletilir.
  void on(String event, void Function(Map<String, dynamic> payload) handler);

  /// Bağlantı koptuğunda/yeniden bağlandığında haber verir.
  void onConnectionChanged(void Function(bool connected) handler);
}

/// `signalr_netcore` ile gerçek bağlantı. Kimlik jetonu ve cihaz kimliği sorgu dizesiyle gider (WebSocket başlık gönderemez).
class SignalRTransport implements VersusTransport {
  SignalRTransport({required String baseUrl, required String deviceId, required Future<String?> Function() authHeaderProvider}) {
    _hub = HubConnectionBuilder()
        .withUrl(
          '$baseUrl/hubs/game?deviceId=${Uri.encodeQueryComponent(deviceId)}',
          options: HttpConnectionOptions(
            accessTokenFactory: () async {
              final header = await authHeaderProvider() ?? '';
              return header.startsWith('Bearer ') ? header.substring(7) : header;
            },
          ),
        )
        .withAutomaticReconnect(retryDelays: [0, 1000, 2000, 4000, 8000])
        .build();
  }

  late final HubConnection _hub;

  @override
  Future<void> start() => _hub.start() ?? Future.value();

  @override
  Future<void> stop() => _hub.stop();

  @override
  Future<Map<String, dynamic>> invoke(String method, [List<Object?> args = const []]) async {
    // signalr_netcore argüman listesinde null kabul etmez; JSON'da null yazan bir yer tutucu kullanılır
    final result = await _hub.invoke(method, args: [for (final a in args) a ?? const _JsonNull()]);
    return Map<String, dynamic>.from(result as Map);
  }

  @override
  void on(String event, void Function(Map<String, dynamic> payload) handler) {
    _hub.on(event, (args) {
      final first = args == null || args.isEmpty ? null : args.first;
      if (first is Map) handler(Map<String, dynamic>.from(first));
    });
  }

  @override
  void onConnectionChanged(void Function(bool connected) handler) {
    _hub.onclose(({error}) => handler(false));
    _hub.onreconnected(({connectionId}) => handler(true));
  }
}

/// `jsonEncode` bu nesneyi `null` olarak yazar (isteğe bağlı hub argümanları için).
class _JsonNull {
  const _JsonNull();

  dynamic toJson() => null;
}
