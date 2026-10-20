# 11 — Hesaplar ve Kurulum Kontrol Listesi

> Hesap açma, ödeme ve onay adımlarını **kullanıcı yapar**, Claude yol gösterir. Kalıcı kurulumlar öncesi kullanıcıya haber verilir.

## 1. Hesaplar

| Hesap | Durum (2026-10-20) | Maliyet | Ne zaman |
|---|---|---|---|
| Google Play Console (geliştirici) | **Yok, açılacak** | Tek seferlik ~25 $ | **Erken** (kimlik doğrulaması günler sürebilir); yeni kişisel hesapta kapalı test şartı olabilir, açılırken teyit |
| Firebase projesi (Auth, FCM, Crashlytics) | **Yok** | Ücretsiz | Faz 1 başı; staging ve prod ayrı |
| AdMob | **Yok** | Ücretsiz | Faz 7; gerçek reklam uygulama yayınlanınca. Vergi/ödeme bilgileri için SMMM |
| Railway | Hobby plan **var** (başka projede) | Mevcut | Faz 0; mevcut projeye karışmadan yeni proje |
| Cloudflare (R2, Pages, DNS) | Bilinmiyor / açılacak | Ücretsiz kota (R2 için kart gerekebilir) | R2 faz 2, Pages faz 0 |
| Alan adı | Alınmadı; büyük olasılıkla **senbilirsin.com** | ~10 $/yıl | **Faz 4** (davet bağlantısı); o adıma kadar alınmaz, kod ayardan okur |
| GitHub | `gh` aracı kurulu; hesap durumu teyit edilecek | Ücretsiz | Faz 0 |
| Apple Developer | İlk sürümde yok | Yıllık ücretli | iOS'a geçince |

## 2. Yerel araçlar (2026-10-20 salt-okunur kontrol)

| Araç | Durum |
|---|---|
| Git | 2.54.0 — var |
| .NET SDK | 10.0.400 / 10.0.401 — var |
| Node / npm | 24.20.0 / 11.19.0 — var |
| Docker | 29.8.1 — var (Postgres + Redis yerelde bununla) |
| GitHub CLI | 2.101.0 — var |
| Java | 26.0.1 — var, çok yeni; Android derlemesi Android Studio'nun kendi Java'sını kullanır |
| **Flutter SDK** | **Kuruldu (2026-10-20):** 3.47.7 stable, `C:\Users\Lider\dev\flutter`; PATH'e eklenmedi, tam yolla çağrılır |
| **Android SDK (adb, emülatör)** | **Kuruluyor (2026-10-20):** `C:\Users\Lider\AppData\Local\Android\Sdk` (komut satırı araçlarıyla; Android Studio IDE'si kurulmadı, gerekirse sonra eklenir) |
| **JDK 21 (Temurin 21.0.12.1)** | **Kuruldu (2026-10-20):** `C:\Users\Lider\dev\jdk-21`; Flutter'a `flutter config --jdk-dir` ile tanıtıldı. Sistemdeki Java 26, Android derlemesinde (`jlink`) hata verdiği için kullanılmaz; Java 26'ya dokunulmadı |
| Android NDK r28c | Gradle ilk derlemede otomatik kurdu (SDK içinde) |
| AVD `senbilirsin_pixel` | Pixel 7, Android 16 (API 36), Google Play x86_64; WHPX hızlandırma çalışıyor |
| psql | Yok (gerekmez; Docker içinden) |
| Visual Studio C++ bileşenleri | Yok, gerekmez (Windows masaüstü hedefimiz yok) |
| Test telefonu / emülatör | **Android telefon yok → Android emülatörü kullanılacak** (K-54); emülatör için sanallaştırma desteği (Hyper-V/WHPX) ve RAM kontrol edilecek |

## 3. Kurulum sırası (Faz 0)

1. Kullanıcıdan onay: Flutter SDK ve Android Studio (SDK, emülatör) kurulumu.
2. `flutter doctor` ile doğrulama; Android lisansları.
3. Telefon (USB hata ayıklama) veya emülatör hazırlama.
4. Docker ile yerel PostgreSQL + Redis (compose dosyası).
5. Railway (staging) projesi, Cloudflare Pages projesi, GitHub deposu.
6. Her adım `docs/ilerleme.md`'ye yazılır.

## 4. Yayın öncesi yapılacaklar (Faz 9)

Gizlilik politikası ve koşullar (taslağı Claude hazırlar) · turnuva/ödül şartları metni · Play Store sayfası (açıklama, ekran görüntüleri, ikon, derecelendirme anketi) · veri güvenliği formu · UMP onay mesajı · kapalı test (çevre) · yedek geri yükleme denemesi · AdMob hesabı ve vergi bilgileri.

## 5. Yerel çalıştırma (bilgisayarda önizleme, K-55)

| Ne | Nasıl |
|---|---|
| Postgres + Redis | Proje kökünde `docker compose up -d` (portlar 5433 / 6380; `.env` gerekir) |
| MobileApi (5080), AdminApi (5090), admin-web (5173), Flutter web (5174) | Uygulamanın tarayıcı panelinde `.claude/launch.json` yapılandırmaları ile başlatılır (`mobile-api`, `admin-api`, `admin-web`, `flutter-web`) |
| Android emülatörü | `C:\Users\Lider\AppData\Local\Android\Sdk\emulator\emulator.exe -avd senbilirsin_pixel` (ilk açılış ~1–3 dk) |
| Uygulamayı emülatöre kurma | `flutter build apk --debug` (klasör: `mobile/`), sonra `adb install -r mobile\build\app\outputs\flutter-apk\app-debug.apk` ve uygulamayı aç (`com.senbilirsin.app`) |

- Emülatörden bilgisayardaki sunucuya adres **`http://10.0.2.2:5080`**tir (uygulama bunu kendisi seçer); web önizlemesinde `http://localhost:5080`.
- Flutter, Android derlemesi için **JDK 21** (`C:\Users\Lider\dev\jdk-21`) kullanır; ilk derleme NDK indirdiği için uzun sürer (~10 dk), sonrakiler ~3 dk.
- `flutter` ve `adb` PATH'e eklenmemiştir; tam yolla çağrılır: `C:\Users\Lider\dev\flutter\bin\flutter.bat`, `C:\Users\Lider\AppData\Local\Android\Sdk\platform-tools\adb.exe`.

## 6. Firebase kurulum adımları (kullanıcı yapar — Faz 1)

1. <https://console.firebase.google.com> → **Proje ekle** → ad: `SenBilirsin` → Google Analytics: **kapalı** (gerekmiyor).
2. **Build → Authentication → Başlayın → Oturum açma yöntemi**: **E-posta/Parola** etkinleştir (parolasız e-posta bağlantısı kapalı kalsın) ve **Google** etkinleştir (destek e-postası olarak kendi adresini seç).
3. **Proje ayarları (dişli) → Uygulamanızı ekleyin → Android**:
   - Android paket adı: **`com.senbilirsin.app`**
   - Uygulama takma adı: `SenBilirsin`
   - **Hata ayıklama imza sertifikası SHA-1**: `6B:4A:D9:69:BD:1A:57:86:25:77:B7:B1:EF:33:47:7C:C1:05:0A:96` (bu bilgisayarın debug anahtarı, 2026-10-20'de `keytool` ile okundu; herkese açık parmak izidir, gizli değil)
   - **`google-services.json`** dosyasını indir (**Google girişini etkinleştirdikten ve SHA-1'i ekledikten sonra** indir; yoksa içinde Google için gereken istemci kimliği olmaz — sonradan eklersen dosyayı yeniden indir).
4. Dosyayı `mobile\android\app\google-services.json` olarak kaydet (ya da nerede olduğunu söyle, ben taşırım).
5. **Proje kimliğini** (Proje ayarları → Genel → *Proje kimliği*, örn. `senbilirsin-ab123`) söyle; backend'in `.env` dosyasında `Firebase__ProjectId` olarak ayarlanır.
6. Yayın sürümü için (Faz 9) ayrıca **yayın anahtarının SHA-1'i** eklenecek; Play App Signing kullanılırsa Play Console'daki SHA-1 de eklenir.

Çalıştırma: `flutter run --dart-define=AUTH=firebase` ya da APK derlerken `--dart-define=AUTH=firebase`. Bu bayrak yoksa uygulama geliştirme kimliğiyle (DevAuth) çalışır.
