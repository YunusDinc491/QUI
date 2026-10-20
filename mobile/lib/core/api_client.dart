import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

import '../game/game_models.dart';
import '../game/tournament_models.dart';

/// Geliştirme sunucusu adresi.
/// Android emülatöründe bilgisayarın localhost'u 10.0.2.2'dir; web ve diğerlerinde localhost.
/// Değiştirmek için: flutter run --dart-define=API_BASE_URL=http://...
String get apiBaseUrl {
  const fromEnv = String.fromEnvironment('API_BASE_URL');
  if (fromEnv.isNotEmpty) return fromEnv;
  if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android) {
    return 'http://10.0.2.2:5080';
  }
  return 'http://localhost:5080';
}

/// Sunucunun döndürdüğü hata ({code, message}) ya da ağ hatası (status 0).
class ApiException implements Exception {
  ApiException(this.status, this.code, this.message);

  final int status;
  final String code;
  final String message;

  bool get isNetwork => status == 0;

  @override
  String toString() => 'ApiException($status, $code): $message';
}

class AppConfigInfo {
  AppConfigInfo({required this.minSupportedVersion, required this.maintenanceMode, required this.maintenanceMessage});

  final String minSupportedVersion;
  final bool maintenanceMode;
  final String maintenanceMessage;

  factory AppConfigInfo.fromJson(Map<String, dynamic> json) => AppConfigInfo(
        minSupportedVersion: json['minSupportedVersion'] as String? ?? '0.0.0',
        maintenanceMode: json['maintenanceMode'] as bool? ?? false,
        maintenanceMessage: json['maintenanceMessage'] as String? ?? '',
      );
}

class Profile {
  Profile({
    required this.id,
    required this.username,
    required this.displayName,
    required this.avatarId,
    required this.totalScore,
    required this.status,
  });

  final String id;
  final String username;
  final String displayName;
  final int avatarId;
  final int totalScore;
  final String status;

  bool get isBanned => status == 'Banned';

  factory Profile.fromJson(Map<String, dynamic> json) => Profile(
        id: json['id'] as String,
        username: json['username'] as String,
        displayName: json['displayName'] as String,
        avatarId: json['avatarId'] as int,
        totalScore: (json['totalScore'] as num).toInt(),
        status: json['status'] as String,
      );
}

class AvatarInfo {
  AvatarInfo({required this.id, required this.name, required this.colorHex, required this.price, this.imageUrl});

  final int id;
  final String name;
  final String colorHex;
  final int price;
  final String? imageUrl;

  factory AvatarInfo.fromJson(Map<String, dynamic> json) => AvatarInfo(
        id: json['id'] as int,
        name: json['name'] as String,
        colorHex: json['colorHex'] as String,
        price: json['price'] as int,
        imageUrl: json['imageUrl'] as String?,
      );
}

/// Modun alt dalı (örn. Branş → Tarih). Sunucu yalnızca yayındaki kategorileri döndürür.
class CatalogCategory {
  CatalogCategory({required this.id, required this.name, this.imageUrl, this.colorHex});

  final int id;
  final String name;
  final String? imageUrl;
  final String? colorHex;

  factory CatalogCategory.fromJson(Map<String, dynamic> json) => CatalogCategory(
        id: json['id'] as int,
        name: json['name'] as String,
        imageUrl: json['imageUrl'] as String?,
        colorHex: json['colorHex'] as String?,
      );
}

/// Oyun modu: general / branch / kpss / fan.
class CatalogMode {
  CatalogMode({required this.id, required this.code, required this.name, required this.mixedAvailable, required this.categories});

  final int id;
  final String code;
  final String name;

  /// "Karışık" seçeneği gösterilsin mi (sunucu, en az 2 yayında alt dal varsa true döner).
  final bool mixedAvailable;
  final List<CatalogCategory> categories;

  /// Genel modun alt dalı yoktur; doğrudan oyuna girilir.
  bool get isGeneral => code == 'general';

  factory CatalogMode.fromJson(Map<String, dynamic> json) => CatalogMode(
        id: json['id'] as int,
        code: json['code'] as String,
        name: json['name'] as String,
        mixedAvailable: json['mixedAvailable'] as bool? ?? false,
        categories: (json['categories'] as List<dynamic>? ?? [])
            .map((e) => CatalogCategory.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class UsernameCheck {
  UsernameCheck({required this.available, this.code, this.message});

  final bool available;
  final String? code;
  final String? message;
}

/// Sunucu (MobileApi) ile konuşur. Kimlik başlığı ve cihaz kimliği her isteğe eklenir.
class ApiClient {
  ApiClient({
    required this.baseUrl,
    required this.deviceId,
    required this.authHeaderProvider,
    http.Client? httpClient,
  }) : _http = httpClient ?? http.Client();

  final String baseUrl;
  final String deviceId;

  /// `Bearer <jeton>` (Firebase) ya da `Dev uid=...` (geliştirme) döndürür; oturum yoksa null.
  final Future<String?> Function() authHeaderProvider;
  final http.Client _http;

  Future<AppConfigInfo> fetchConfig() async =>
      AppConfigInfo.fromJson(await _send('GET', '/api/config', auth: false) as Map<String, dynamic>);

  Future<UsernameCheck> checkUsername(String username) async {
    final json = await _send('GET', '/api/users/username-available?username=${Uri.encodeQueryComponent(username)}')
        as Map<String, dynamic>;
    return UsernameCheck(
      available: json['available'] as bool,
      code: json['code'] as String?,
      message: json['message'] as String?,
    );
  }

  Future<List<AvatarInfo>> fetchAvatars() async {
    final json = await _send('GET', '/api/users/avatars') as List<dynamic>;
    return json.map((e) => AvatarInfo.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<List<CatalogMode>> fetchCatalog() async {
    final json = await _send('GET', '/api/catalog/modes') as List<dynamic>;
    return json.map((e) => CatalogMode.fromJson(e as Map<String, dynamic>)).toList();
  }

  // --- Bireysel maç (Faz 3) ---

  Future<JokerBalances> fetchJokers() async => JokerBalances.fromJson(await _send('GET', '/api/jokers') as Map<String, dynamic>);

  /// [mode]: general | branch | kpss | fan. [categoryId] boşsa "Karışık" (Genel modda tek kategori).
  Future<MatchState> startSolo(String mode, int? categoryId) async => MatchState.fromJson(
      await _send('POST', '/api/matches/solo', body: {'mode': mode, 'categoryId': ?categoryId}) as Map<String, dynamic>);

  /// Devam eden maç yoksa null.
  Future<MatchState?> fetchCurrentMatch() async {
    try {
      return MatchState.fromJson(await _send('GET', '/api/matches/current') as Map<String, dynamic>);
    } on ApiException catch (e) {
      if (e.code == 'NO_ACTIVE_MATCH') return null;
      rethrow;
    }
  }

  /// [option] boşsa "süre doldu" bildirimi. [second]: çift cevap jokerinde ikinci şık.
  Future<AnswerOutcome> answer(String matchId, int position, int? option, {int? second}) async => AnswerOutcome.fromJson(
      await _send('POST', '/api/matches/$matchId/answer', body: {'position': position, 'option': option, 'second': second}) as Map<String, dynamic>);

  Future<JokerOutcome> useJoker(String matchId, int position, String type) async => JokerOutcome.fromJson(
      await _send('POST', '/api/matches/$matchId/joker', body: {'position': position, 'type': type}) as Map<String, dynamic>);

  Future<TournamentInfo> fetchTournament() async =>
      TournamentInfo.fromJson(await _send('GET', '/api/tournament/current') as Map<String, dynamic>);

  Future<PreviousTournament> fetchPreviousTournament() async =>
      PreviousTournament.fromJson(await _send('GET', '/api/tournament/previous') as Map<String, dynamic>);

  Future<WalletInfo> fetchWallet() async => WalletInfo.fromJson(await _send('GET', '/api/wallet') as Map<String, dynamic>);

  Future<List<PointsHistoryItem>> fetchPointsHistory({int page = 1}) async {
    final json = await _send('GET', '/api/wallet/history?page=$page') as List<dynamic>;
    return json.map((e) => PointsHistoryItem.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// Bota karşı oynarken seçilebilecek aktif botlar.
  Future<List<BotInfo>> fetchBots() async {
    final json = await _send('GET', '/api/bots') as List<dynamic>;
    return json.map((e) => BotInfo.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<MatchState> continueMatch(String matchId, String adIntentId) async => MatchState.fromJson(
      await _send('POST', '/api/matches/$matchId/continue', body: {'adIntentId': adIntentId}) as Map<String, dynamic>);

  Future<MatchState> declineContinue(String matchId) async =>
      MatchState.fromJson(await _send('POST', '/api/matches/$matchId/decline', body: {}) as Map<String, dynamic>);

  Future<MatchState> forfeit(String matchId) async =>
      MatchState.fromJson(await _send('POST', '/api/matches/$matchId/forfeit', body: {}) as Map<String, dynamic>);

  Future<MatchSummary> fetchSummary(String matchId) async =>
      MatchSummary.fromJson(await _send('GET', '/api/matches/$matchId/summary') as Map<String, dynamic>);

  Future<void> reportQuestion(String matchId, int position, String reason) async =>
      _send('POST', '/api/matches/$matchId/report', body: {'position': position, 'reason': reason});

  Future<Profile> register({
    required String username,
    required String displayName,
    required int avatarId,
    required int birthYear,
    required bool termsAccepted,
  }) async {
    final json = await _send('POST', '/api/users/me', body: {
      'username': username,
      'displayName': displayName,
      'avatarId': avatarId,
      'birthYear': birthYear,
      'termsAccepted': termsAccepted,
    }) as Map<String, dynamic>;
    return Profile.fromJson(json);
  }

  /// Bu cihazı hesabın aktif cihazı yapar (tek cihaz kuralı). Kayıtsızsa 404 NOT_REGISTERED fırlatır.
  Future<Profile> startSession() async => Profile.fromJson(await _send('POST', '/api/users/me/session',
      body: {'deviceId': deviceId, 'platform': defaultTargetPlatform.name}) as Map<String, dynamic>);

  Future<Profile> fetchMe() async => Profile.fromJson(await _send('GET', '/api/users/me') as Map<String, dynamic>);

  Future<Profile> updateMe({String? displayName, int? avatarId}) async => Profile.fromJson(await _send(
        'PATCH',
        '/api/users/me',
        body: {
          'displayName': ?displayName,
          'avatarId': ?avatarId,
        },
      ) as Map<String, dynamic>);

  Future<void> deleteMe() async => _send('DELETE', '/api/users/me');

  Future<dynamic> _send(String method, String path, {Object? body, bool auth = true}) async {
    final headers = <String, String>{'X-Device-Id': deviceId, 'Accept': 'application/json'};
    if (body != null) headers['Content-Type'] = 'application/json';
    if (auth) {
      final header = await authHeaderProvider();
      if (header != null) headers['Authorization'] = header;
    }

    final request = http.Request(method, Uri.parse('$baseUrl$path'))..headers.addAll(headers);
    if (body != null) request.body = jsonEncode(body);

    final http.Response response;
    try {
      response = await http.Response.fromStream(await _http.send(request).timeout(const Duration(seconds: 10)));
    } on TimeoutException {
      throw ApiException(0, 'NETWORK', 'Sunucuya ulaşılamadı. İnternet bağlantını kontrol et.');
    } catch (_) {
      throw ApiException(0, 'NETWORK', 'Sunucuya ulaşılamadı. İnternet bağlantını kontrol et.');
    }

    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (response.body.isEmpty) return null;
      return jsonDecode(utf8.decode(response.bodyBytes));
    }

    var code = 'HTTP_${response.statusCode}';
    var message = 'Bir hata oluştu (${response.statusCode}).';
    try {
      final json = jsonDecode(utf8.decode(response.bodyBytes));
      if (json is Map<String, dynamic>) {
        code = json['code'] as String? ?? code;
        message = json['message'] as String? ?? message;
      }
    } catch (_) {
      // Gövde JSON değilse varsayılan mesaj kullanılır
    }
    throw ApiException(response.statusCode, code, message);
  }
}
