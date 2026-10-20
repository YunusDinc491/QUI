# 04 — SignalR Olayları ve REST Uçları

> Bu dosya sözleşme taslağıdır; isimler İngilizce olacak şekilde yapım sırasında (faz 3–4) kesinleştirilir. Aşağıdaki Türkçe adlar anlam içindir.

## 1. Bağlantı

- Tek hub; bağlantı kurulurken Firebase kimlik jetonu gönderilir ve doğrulanır.
- **Bir kullanıcı = bir aktif bağlantı.** İkinci cihaz girişinde eski oturuma "oturum kapatıldı" olayı gönderilir.
- Yeniden bağlanan istemciye sunucu otomatik **tam durum özeti** yollar (aktif maç, soru, kalan süre, skor); istemci kaldığı yerden devam eder.
- Keepalive/zaman aşımı kısa tutulur (kopma tespiti için). Arka plana geçişte istemci yaşam döngüsü sinyalini ayrıca gönderir.
- Hız sınırı: metot başına (ör. saniyede en fazla birkaç cevap).

## 2. İstemciden sunucuya

| Metot | Anlam |
|---|---|
| `JoinQueue(mode, category)` | Rastgele rakip kuyruğuna gir |
| `LeaveQueue()` | Aramayı iptal et |
| `ChooseFallback(bot \| solo \| retry)` | 15 sn sonunda bulunamayınca seçim |
| `CreateRoom(mode, category)` | Arkadaş odası kur |
| `JoinRoom(code)` | Odaya katıl |
| `LeaveRoom()` | Odadan çık |
| `StartSolo(mode, category)` | Bireysel maç başlat |
| `Ready(matchId)` | Maça hazır |
| `SubmitAnswer(matchId, position, option)` | Cevap ver (ilk dokunuş kesin) |
| `UseJoker(matchId, position, type)` | Joker kullan |
| `Forfeit(matchId)` | Pes |
| `ResumeMatch(matchId)` | Yeniden bağlanınca maça dön |
| `ContinueWithAd(matchId, adIntentId)` | Bireyselde reklamla devam |
| `RequestRematch(matchId)` / `RespondRematch(matchId, accept)` | Rövanş |

## 3. Sunucudan istemciye

| Olay | İçerik |
|---|---|
| `MatchFound` | Rakip bilgisi (ad, avatar, çerçeve, bot mu), maç kimliği |
| `QueueTimeout` | 15 sn doldu, seçenekler (bot / bireysel / tekrar ara) |
| `OpponentJoinedRoom` / `RoomClosed` | Oda olayları |
| `CountdownStarted` | 5 sn geri sayım, başlangıç zamanı |
| `QuestionStarted` | Sıra, metin, 4 şık (karışık sırada), **mutlak bitiş zamanı**, joker durumu (doğru cevap YOK) |
| `OpponentAnswered` | Rakip cevap verdi bilgisi (cevabın kendisi değil) |
| `QuestionResult` | Doğru şık, senin ve rakibin seçimi, skorlar |
| `JokerResult` | Eleme sonucu / ipucu / çift cevap onayı |
| `OpponentDisconnected` | Kalan havuz süresi (ms) |
| `OpponentReconnected` | — |
| `MatchFinished` | Özet: sonuç, doğru sayıları, puan, bonus, 2x teklifi, soru listesi (açıklamalarla) |
| `MatchCancelled` | Neden (hazır olmadı, sunucu yeniden başladı, çift kopma…) |
| `RematchRequested` / `RematchResult` | Rövanş |
| `RewardGranted` | Reklam ödülü işlendi (niyet kimliği) |
| `SessionClosed` | Başka cihazdan girildi |
| `MaintenanceNotice` | Bakım uyarısı |

## 4. REST uçları (mobil-api, özet)

- **Hesap/profil:** kayıt tamamlama, kullanıcı adı müsaitlik kontrolü, profil okuma/güncelleme (görünen ad, avatar, çerçeve), giriş yöntemi bilgisi, hesap silme.
- **Katalog:** modlar, alt dallar (yayında olanlar).
- **Cüzdan:** hediye puanı bakiyesi, puan geçmişi, joker sayıları.
- **Mağaza:** ürün listesi, satın alma (işlem kimliği ile), Ödüllerim, kupon kodunu gösterme, koda sorun bildirme.
- **Turnuva:** aktif dönem, ilk 50, kendi sıram, ödül tablosu, önceki kazananlar.
- **Reklam:** niyet oluşturma (`ad_intents`); AdMob SSV geri çağrısı ayrı bir imzalı uç.
- **Destek:** talep aç, listele, mesaj gönder, ekran görüntüsü yükle; soru önerisi gönder.
- **Soru bildirme:** maç özeti üzerinden.
- **Bildirim:** liste, okundu, ayarlar (tür açma/kapama), push token kaydı.
- **Yapılandırma:** min sürüm, bakım modu, genel ayarlar.
- **Son maçlar:** profil için son 20 maç.

## 5. Güvenlik notları

- Her uçta kimlik doğrulama; admin uçları ayrı hostta ve ayrı kimlikle.
- Hız sınırı: API'de kullanıcı/IP başına; giriş ve kayıt uçlarında daha sıkı.
- Kupon kodu gösterme, satın alma, ödül verme uçları **idempotent** (işlem kimliği benzersiz).
- Doğru cevap alanı hiçbir yanıtta yoktur (yalnızca `QuestionResult`'ta, süre bittikten/herkes cevapladıktan sonra).

## 6. Gerçekleşen uçlar — Faz 1 (2026-10-20)

Hepsi `Authorization: Bearer <Firebase kimlik jetonu>` ister (geliştirmede `Dev uid=...;email=...;verified=true;provider=password` da kabul edilir, bkz. 08). `X-Device-Id` başlığı "korumalı" uçlarda zorunludur.

| Uç | Açıklama |
|---|---|
| `GET /api/config` (anonim) | `minSupportedVersion`, `maintenanceMode`, `maintenanceMessage` (şimdilik ayar dosyasından; admin panelinden yönetim Faz 8) |
| `GET /api/users/username-available?username=` | `{available, code, message}`; kural ve benzersizlik kontrolü |
| `GET /api/users/avatars` | Aktif avatarlar (id, ad, renk, fiyat) |
| `POST /api/users/me` | Kayıt tamamlama `{username, displayName, avatarId, birthYear, termsAccepted}` → 201. Hatalar: `ALREADY_REGISTERED`(409), `USERNAME_TAKEN`(409), `TERMS_NOT_ACCEPTED`, `USERNAME_*`, `DISPLAYNAME_*`, `AGE_TOO_YOUNG`, `AVATAR_*`, `EMAIL_NOT_VERIFIED`(403; e-posta/parola girişinde doğrulanmamış e-posta) |
| `POST /api/users/me/session` | `{deviceId, platform}` → bu cihazı aktif cihaz yapar (tek cihaz). Giriş yapar yapmaz çağrılır. Kayıtsızsa 404 `NOT_REGISTERED` |
| `GET /api/users/me` 🔒 | Profil (`id, username, displayName, avatarId, frameId, totalScore, status`) |
| `PATCH /api/users/me` 🔒 | `{displayName?, avatarId?}` |
| `DELETE /api/users/me` 🔒 | Hesabı anonimleştirir (204). Firebase hesabını istemci siler |

🔒 = aktif cihaz kontrolü: başka cihaz aktifse `401 {"code":"SESSION_REPLACED"}`.
Hata gövdesi: `{ "code": "...", "message": "Türkçe açıklama" }`.

## 7. Gerçekleşen uçlar — Faz 2 (2026-10-21)

### Mobil API

| Uç | Açıklama |
|---|---|
| `GET /api/catalog/modes` 🔐 | 4 mod ve her birinin **yayındaki** alt dalları: `[{id, code, name, mixedAvailable, categories:[{id, name, imageUrl, colorHex}]}]`. `mixedAvailable`: Genel dışındaki modlarda ≥2 yayında alt dal varsa true ("Karışık"). Genel modun kategorisi yayınlanmadıkça listede görünmez; istemci Genel'e doğrudan girer. |

### Admin API (ayrı host; hepsi `Authorization: Bearer <admin JWT>`, rol `admin`; giriş hariç)

JSON'da sıralamalar metin: zorluk `Easy|Medium|Hard`, durum `Draft|Published|Archived`. Hata gövdesi `{code, message}`; tekrar şüphesinde ayrıca `duplicates`.

| Uç | Açıklama |
|---|---|
| `POST /api/auth/login` (anonim) | `{email, password, code}` → `{token, expiresAt}`. Hatada hangi alanın yanlış olduğu söylenmez (401); 5 ardışık hatada 15 dk kilit (429). Aynı TOTP kodu ikinci kez kabul edilmez. |
| `GET /api/auth/me`, `POST /api/auth/change-password` | Oturumdaki yönetici; parola değiştirme (≥12 karakter) |
| `GET /api/categories` | Tüm kategoriler + soru sayıları `{easy, medium, hard (yayındakiler), draft, archived}` |
| `POST /api/categories`, `PUT /api/categories/{id}` | `{modeId, name, imageUrl?, colorHex?, order?}`. Aynı modda aynı ad 409; Genel modda yeni kategori eklenemez |
| `POST /api/categories/{id}/publish`, `…/unpublish` | Yayınla / gizle (soru sayısına bağlı engel yok) |
| `GET /api/questions` | `categoryId, difficulty, status, search, page, pageSize(≤100)` |
| `GET/POST /api/questions`, `PUT /api/questions/{id}` | `{categoryId, text, options[4], correctIndex(0–3), difficulty, explanation?, status?, ignoreDuplicates?}`. Benzer soru varsa `409 DUPLICATE_SUSPECTED` |
| `POST /api/questions/{id}/status` | `{status}` — yayınla / taslağa al / arşivle (silme yok) |
| `POST /api/questions/check-duplicate` | `{categoryId, text, excludeId?}` → `{duplicates}` (form içinde anlık uyarı) |
| `POST /api/questions/import/preview`, `…/import/commit` | CSV toplu yükleme: `{csv}` / `{csv, includeDuplicates, publish}`. Sütunlar: `mod;kategori;zorluk;soru;A;B;C;D;dogru;aciklama` (ayraç `;` `,` ya da sekme otomatik; en çok 500 satır). Önizleme satır satır `ok/error/duplicate` döndürür. **Şimdilik yalnızca CSV** (Excel'den "CSV UTF-8" olarak kaydedilir). |
| `POST /api/uploads/image?folder=categories\|avatars\|frames` | Çok parçalı form, alan `file`. PNG/JPEG/WebP (ilk baytlardan doğrulanır, SVG yok), ≤1 MB → `{key, url}`. Yerel depoda `/uploads/…` altında sunulur; üretimde Cloudflare R2. |
| `GET /api/audit-logs` | `action, entity, page, pageSize(≤200)`; her yazma işleminin önce/sonra kaydı |

Yerel komutlar (AdminApi, sunucu başlatmaz): `-- create-admin <e-posta>` (rastgele parola + TOTP anahtarı → yalnızca `.local/` altındaki git dışı dosya), `-- seed-samples` (yalnızca Development; 60 örnek soru).

## 8. Gerçekleşen uçlar — Faz 3 (bireysel maç, 2026-10-21)

> **Sapma notu:** Bu dosyanın §2–3 taslağında `StartSolo`, `SubmitAnswer`, `UseJoker`… SignalR metotları olarak geçiyordu. Bireysel maçta sunucudan istemciye anlık itme gerekmediği (süreyi sunucu zaman damgasıyla ölçer, istemci geri sayımı sunucu saat farkıyla gösterir) ve REST yeniden bağlanmayı/test edilebilirliği kolaylaştırdığı için **bireysel maç REST ile** yapıldı. SignalR rakipli maç (Faz 4) içindir. (Taslak olduğu için karar değişikliği sayılmadı.)

Hepsi `Authorization` + `X-Device-Id` ister; aktif cihaz değilse `401 SESSION_REPLACED`. Hata gövdesi `{code, message}` (+ `matchId`). Zamanlar UTC (ISO 8601). **Doğru şık hiçbir görünümde yoktur**; yalnızca cevap yanıtında (`AnswerOutcome`) ve maç bittikten sonra özette gelir. Şıklar sunucunun karıştırdığı ekran sırasındadır; tüm indeksler ekran sırasındadır.

| Uç | Açıklama |
|---|---|
| `GET /api/jokers` | `{half, double, hint}` bakiyeleri (ilk çağrıda her birinden 5 hediye, defterle) |
| `POST /api/matches/solo` | `{mode: general\|branch\|kpss\|fan, categoryId?}` → 201 `MatchState`. Kategori yoksa "Karışık" (Genel'de tek kategori). Hatalar: `MODE_INVALID`, `MODE_UNAVAILABLE`, `CATEGORY_UNAVAILABLE`(404), `NOT_ENOUGH_QUESTIONS`(409), `MATCH_IN_PROGRESS`(409 + `matchId`) |
| `GET /api/matches/current` | Devam eden maçın tam durumu (yeniden bağlanma); yoksa 404 `NO_ACTIVE_MATCH` |
| `POST /api/matches/{id}/answer` | `{position, option?, second?}` → `AnswerOutcome {position, isCorrect, timedOut, correctOption, chosenOption, secondOption, explanation, state}`. `option` boş = "süre doldu" bildirimi (yalnız süre bittiyse). İlk dokunuş kesin: aynı soruya tekrar istek kayıtlı sonucu döner. Süre sonu + 1,5 sn toleranstan sonraki cevap süre aşımı sayılır. `TOO_EARLY`, `WRONG_POSITION`, `OPTION_INVALID`, `DOUBLE_NOT_ACTIVE` |
| `POST /api/matches/{id}/joker` | `{position, type: half\|double\|hint}` → `{joker: {type, removed?, hint?}, jokers}`. Soru başına en çok bir, tür başına maçta bir. `JOKER_TYPE_USED`, `JOKER_ALREADY_USED_THIS_QUESTION`, `NO_JOKER`, `QUESTION_TIMED_OUT` |
| `POST /api/matches/{id}/continue` | `{adIntentId}` — yanlıştan sonra (45 sn içinde, maçta bir kez) reklam doğrulanınca aynı sıraya yeni **kolay** soru. Faz 3'te yalnız `Ads:TestMode` + `test-…` kimlik; gerçek doğrulama Faz 7 |
| `POST /api/matches/{id}/decline` | Devam teklifini reddet → maç elenmeyle biter |
| `POST /api/matches/{id}/forfeit` | Pes: o ana kadarki doğru sayısına göre puan |
| `GET /api/matches/{id}/summary` | Maç bittikten sonra: her soru, ekran şıkları, doğru şık, cevaplar, açıklama, kullanılan joker, değiştirilen soru işareti |
| `POST /api/matches/{id}/report` | `{position, reason(≤300)}` → 204. Yalnızca gösterilmiş soru; soru×kullanıcı başına bir bildirim |

`MatchState`: `matchId, mode, categoryId, status(running\|awaiting_continue\|finished\|cancelled\|forfeited), position, totalQuestions, correctCount, question{position, text, options[4], startsAt, endsAt}, activeJoker, usedJokerTypes, jokers, continueOffer{deadline}, result{status, reason, correctCount, points, settled}, serverNow`. `question.startsAt` gelecekte olabilir (önceki sonucun gösterilmesi: sonraki soru 3 sn sonra; ilk soru 2 sn sonra). Süre: soru başına 15 sn. Terk: son soru bitişinden 120 sn sonra hâlâ hareket yoksa maç iptal (puan yok).

## 9. Gerçekleşen sözleşme — Faz 4 (rakipli maç, SignalR, 2026-10-21)

Hub: `/hubs/game`. Bağlanırken kimlik (Firebase jetonu `access_token` sorgusunda; geliştirmede `Dev …`) ve **aktif cihaz** (`X-Device-Id` başlığı ya da `?deviceId=`) doğrulanır; uyuşmazsa bağlantı kesilir. Bir kullanıcı = bir aktif bağlantı (ikincisi gelince eskisine `SessionClosed`). Keepalive 7 sn, istemci zaman aşımı 20 sn. Her hub metodu `{ok, code, message, data}` döndürür. Doğru cevap, bir sorunun sonucu açıklanana kadar hiçbir olayda yoktur; şık sırası iki oyuncuda aynıdır.

**İstemci → sunucu:** `JoinQueue(mode, categoryId?)`, `LeaveQueue()`, `ChooseFallback(choice: bot|solo|retry, botId?)`, `CreateRoom(mode, categoryId?)` → `{code, expiresAt}`, `JoinRoom(code)`, `LeaveRoom()`, `Ready(matchId)`, `SubmitAnswer(matchId, position, option?, second?)`, `UseJoker(matchId, position, type)`, `Forfeit(matchId)`, `ResumeMatch(matchId)`, `RequestRematch(matchId)`, `RespondRematch(matchId, accept)`, `AppBackgrounded()` / `AppForegrounded()` (yaşam döngüsü: arka plan = anında kopma sayılır). REST: `GET /api/bots`.

**Sunucu → istemci:** `MatchFound {matchId, kind, mode, categoryId, needsReady, readyTimeoutSeconds, opponent{id?, name, avatarId, frameId, isBot}}`, `QueueTimeout {options}`, `OpponentJoinedRoom`, `RoomClosed`, `CountdownStarted {startsAt, seconds}`, `QuestionStarted {position, total, text, options[4], startsAt, endsAt, jokers, usedJokerTypes, yourScore, opponentScore, serverNow}`, `OpponentAnswered {position}` (cevabın kendisi yok), `QuestionResult {position, correctOption, explanation, yourOption, yourSecond, opponentOption, yourCorrect, opponentCorrect, yourScore, opponentScore}`, `JokerResult` (yalnızca kullanana), `OpponentDisconnected {remainingMs}` / `OpponentReconnected`, `MatchFinished {result: win|lose|draw, points, bonus, reason: normal|forfeit|disconnect, yourCorrect, opponentCorrect, opponent}`, `MatchCancelled {reason: not_ready|both_disconnected|server_restart}`, `RematchRequested` / `RematchResult {accepted, reason?}`, `MatchState` (yeniden bağlanınca tam durum özeti), `SessionClosed`.

**Kurallar (uygulandı):** kuyruk anahtarı mod+alt dal, uygun bekleyen varsa anında eşleşir, yoksa 15 sn sonra `QueueTimeout` (otomatik bot yok); rastgele eşleşmede 10 sn içinde iki taraf `Ready` demezse iptal; 5 sn geri sayım; soru 15 sn; iki cevap sonrası 1 sn, aksi halde süre + 1,5 sn tolerans sonunda sonuç; sonuç ekranı 3 sn; kazanan en çok doğru yapan: kazanan 10, kaybeden 0, **8+ doğru yapan herkese +5**, beraberlikte eşik ve üstüyse doğru × katsayı (başlangıç 1); arkadaş odası 6 karakter kod, 5 dk, tek rakip; kopma: soru açıkken süre durmaz, sorular arasında maç duraklar ve kopanın 30 sn havuzu azalır, havuz biterse kopan kaybeder, iki gerçek oyuncu birden koparsa iptal; pes anında kayıp; sunucu yeniden başlarsa süren rakipli maçlar iptal (puan yok); rövanş 2 dk geçerli, bota karşı hemen, insana karşı 30 sn içinde karşı taraf onaylarsa.
