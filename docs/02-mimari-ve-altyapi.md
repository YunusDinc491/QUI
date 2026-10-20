# 02 — Mimari ve Altyapı

> Kararlar K-02 … K-12, K-43 … K-45 ile bağlantılıdır.

## 1. Genel yapı

```
Flutter (Android)  ──REST/SignalR──►  mobil-api (.NET 10, tek instance)  ──►  PostgreSQL
                                          │                                  ►  Redis
                                          ├─ Firebase Auth (jeton doğrulama)
                                          ├─ FCM (push)
                                          └─ AdMob SSV callback

React admin (Cloudflare Pages) ──REST──►  admin-api (.NET 10, ayrı host)  ──►  aynı PostgreSQL / Redis
Cloudflare R2: kategori görselleri, avatarlar, destek ekran görüntüleri, yedekler
```

- **Modüler monolit:** tek solution, modüller kendi tablolarına sahip; modüller arası erişim tanımlı arayüzlerle olur (tablolara doğrudan SQL ile girilmez).
- **İki host:** `mobil-api` (kullanıcı REST + SignalR + arka plan işleri) ve `admin-api` (yönetim). İkisi aynı modül kütüphanelerini ve veritabanını paylaşır.
- Tek instance; SignalR ölçekleme (backplane) şimdilik **yok** (kapsam dışı).

## 2. Modüller

| Modül | Sorumluluk |
|---|---|
| Users | profil, avatar/çerçeve, cihazlar, ban/uyarı |
| Catalog | modlar, kategoriler (alt dallar), görseller |
| Questions | soru bankası, bildirimler, istatistikler, görülen sorular |
| Matchmaking | kuyruk, oda/davet, bot teklifi |
| MatchEngine | maç durumu, süreler, kopma, puanlama |
| Tournament | dönemler, sıralama, ödül tablosu, men, dağıtım |
| Wallet | hediye puanı defteri, joker defteri |
| Store | ürünler, kupon stoğu, satın alma, ödüllerim |
| Ads | reklam niyeti, SSV doğrulama |
| Support | talepler, mesajlar, ekler, soru önerileri |
| Notifications | push, bildirim kutusu, duyuru |
| Administration | admin kullanıcıları, denetim kaydı, uygulama ayarları |

## 3. Redis ve bellek

- **Süren maçların durumu bellekte** tutulur (her maç için bir "actor" ve zamanlayıcılar). Tek instance olduğu için yeterli; ileride birden fazla instance'a geçilirse Redis'e taşınabilir.
- **Redis şunlar için:** sıralama tablosu (`sorted set`, dönem başına), eşleşme kuyruğu ve çevrimiçi/aktif maç bilgisi, banlı kullanıcı ve ayar önbelleği, hız sınırı sayaçları.
- Maç bitince sonuçlar (maç, oyuncular, cevaplar) PostgreSQL'e yazılır.

## 4. Arka plan işleri (mobil-api içinde BackgroundService)

Dönem kapatma ve ödül dağıtımı (idempotent, kilitli) · inceleme hatırlatması (bitişe 1 saat kala e-posta + rozet) · oda temizliği · istatistik toplama · log/kayıt temizliği (saklama süreleri) · günlük Postgres yedeği.

## 5. Zamanlama

- Sunucu soru gönderirken **mutlak bitiş zamanı** (UTC) yollar; istemci sunucuyla saat farkını hesaplar, cihaz saati yanlış olsa da geri sayım doğru olur.
- Kesin kararı sunucu verir; ~500 ms ağ toleransı.

## 6. Dağıtım (Railway + Cloudflare)

| Parça | Yer |
|---|---|
| mobil-api | Railway servisi |
| admin-api | Railway servisi (ayrı) |
| admin-web (React) | Cloudflare Pages |
| PostgreSQL, Redis | Railway servisleri |
| Görsel/dosya/yedek | Cloudflare R2 |
| DNS / alan adı | Cloudflare (alan adı faz 4'te alınır) |

- Kullanıcının Railway Hobby planı zaten var (başka bir projede kullanılıyor); mevcut projeye karışmadan **yeni proje** açılır. Gerekirse plan yükseltilir.
- **Ortamlar:** `staging` (küçük kaynak) ve `production`. Firebase ve AdMob da test/üretim olarak ayrı kurulur.
- **Deploy:** süren maçlar iptal olur (puan değişmez). Öncesinde admin panelinden **bakım modu** açılır; az oyuncu olan saatte yapılır.
- **Yedek:** Postgres için günlük `pg_dump` zamanlanmış iş → R2; son 14–30 yedek tutulur. Railway'in yerleşik yedek özelliği plana bağlı olabilir; kurulumda kontrol edilir ama kendi yedeğimiz her halükârda kurulur. Postgres/Redis Railway'de yönetilen servis değildir; yedek bu yüzden önemlidir.
- **CI/CD:** GitHub → Railway ve Cloudflare Pages otomatik deploy. Flutter derlemesi yerelde; Play Console iç/kapalı test kanalına yüklenir.

## 7. Depo yapısı (monorepo)

```
SenBilirsin/
  CLAUDE.md
  docs/            # md dosyaları ve ilerleme.md
  backend/         # .NET çözümü: MobileApi, AdminApi, Modules/*, tests
  admin-web/       # React (Vite + TypeScript + shadcn/ui + TanStack Table/Query)
  mobile/          # Flutter
  .claude/         # hook ve ayarlar
```

## 8. İzleme ve günlük

- Sunucu: **Serilog** yapılandırılmış log (kritik olaylar veritabanına da yazılır), sağlık uçları (`/health`), dışarıdan **uptime** kontrolü (ör. UptimeRobot ücretsiz), panelde **canlı oyuncu sayısı**.
- Mobil: **Firebase Crashlytics** (ücretsiz çökme raporu). Sentry kullanılmaz.

## 9. Sürüm ve bakım yönetimi

- Sunucu bir `min_supported_version` ve `maintenance_mode` bilgisi döner. Eski sürümde "güncelle" ekranı, bakımda "bakımdayız" ekranı çıkar.
- İki ayar da admin panelinden değiştirilir (`app_config`).

## 10. Kimlik

- Kullanıcı: Firebase kimlik jetonu her istekte doğrulanır (SignalR'da bağlantı kurulurken). Kendi jetonumuz üretilmez; banlı kullanıcı kontrolü Redis'te önbellekte.
- Admin: ayrı kimlik sistemi (ASP.NET Identity), e-posta + parola + TOTP 2 adımlı doğrulama; rol alanı baştan vardır ("Yönetici" tek rol). Ayrıntı 08.

## 11. Teknik riskler (bilinenler)

- Flutter için resmî SignalR istemcisi yok; topluluk paketleri (ör. `signalr_netcore`) bakım riski taşır. **Faz 0/4'te erken prototip** ile test edilir; gerekirse düz WebSocket protokolü sarmalayıcısı yazılır.
- Ağ koparken sunucunun fark etmesi SignalR keepalive süresine bağlıdır; kısa tutulur.
- Ayrıntılar için bkz. 10.
