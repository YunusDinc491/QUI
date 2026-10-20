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
