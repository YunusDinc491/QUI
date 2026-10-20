import 'package:flutter/foundation.dart';

/// Ödüllü reklam soyutlaması (mimari ilke 6). Gerçek AdMob uygulaması Faz 7'de gelir; o zamana kadar
/// yalnızca geliştirme derlemelerinde çalışan [StubRewardedAdService] kullanılır, yayın derlemesinde reklam "hazır değil" sayılır
/// (yani sahte ödül/devam teklifi gösterilmez).
abstract class RewardedAdService {
  /// Reklam şu an gösterilebilir mi? Hazır değilse "reklamla devam" teklifi hiç gösterilmez (docs/01 §5).
  bool get isReady;

  /// Reklamı gösterir; ödül kazanıldıysa sunucuda doğrulanacak niyet kimliğini döndürür, aksi halde null.
  Future<String?> show(String purpose);
}

class StubRewardedAdService implements RewardedAdService {
  StubRewardedAdService({bool? enabled}) : _enabled = enabled ?? kDebugMode;

  final bool _enabled;
  int _counter = 0;

  @override
  bool get isReady => _enabled;

  /// Sunucudaki `Ads:TestMode` kapısı yalnızca "test-" ile başlayan, tek kullanımlık kimlikleri kabul eder.
  @override
  Future<String?> show(String purpose) async {
    if (!_enabled) return null;
    _counter++;
    return 'test-${DateTime.now().microsecondsSinceEpoch}-$_counter';
  }
}
