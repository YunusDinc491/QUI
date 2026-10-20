# 09 — Geliştirme Fazları

> Sıra ve fazlar kullanıcı tarafından onaylandı (K-48). **Kabul kriterleri taslaktır**; her fazın başında kullanıcıyla teyit edilir. Bir faz bitmeden diğerine geçilmez (CLAUDE.md). Her adım `docs/ilerleme.md`'ye yazılır.
> Süre tahmini bilerek verilmemiştir; her faz başında işin boyutuna göre kalan süre tahmini söylenir.

| Faz | İçerik | Kabul kriteri (taslak) |
|---|---|---|
| **0 — İskelet** | Depo yapısı + `git init`, .NET çözümü (MobileApi, AdminApi, modül projeleri, test projesi), Flutter ve React projeleri, yerelde Docker ile PostgreSQL + Redis, veritabanı geçiş altyapısı, Railway (staging) ve Cloudflare Pages kurulumu, otomatik deploy, `.env.example`, **SignalR–Flutter prototipi** (echo). Eksik araçların kurulumu (Flutter SDK, Android Studio) — her biri önce haber verilerek. | Staging'de `/health` yanıt verir; boş Flutter uygulaması telefonda/emülatörde açılır ve prototip SignalR mesajı gider-gelir; admin-web boş sayfası yayında |
| **1 — Giriş ve profil** | Firebase Auth entegrasyonu (Google, e-posta), kayıt akışı (kullanıcı adı, görünen ad, ücretsiz avatar, doğum yılı, koşullar), e-posta doğrulama, profil, ayarlar, sağlayıcı bağlama/çıkarma, hesap silme, küfür filtresi, tek cihaz, min sürüm/bakım ekranı, "nasıl oynanır" turu | Google ve e-posta ile kayıt+giriş yapılır; kullanıcı adı benzersiz; profil görünür; hesap silinir; ikinci cihaz ilkini kapatır |
| **2 — Katalog ve soru bankası** | Admin girişi (Identity + TOTP), denetim kaydı, 4 mod + alt dal yönetimi, görsel yükleme (R2), tekli akıcı soru ekleme, toplu yükleme, tekrar uyarısı, taslak/yayın/arşiv, soru sayaçları | Panelden alt dal ve soru girilir; uygulama yayındaki kategorileri listeler |
| **3 — Bireysel oyun** | Soru seçimi (son 200, zorluk sırası 4/3/3), 15 sn süre ve sunucu zamanlaması, puan merdiveni, jokerler (defter), soru bildirme, kopma/pes kuralları, reklamla devam altyapısı (stub), **maç motoru ve puanlama testleri** | Bireysel maç baştan sona oynanır; puan kuralı ve joker testleri geçer |
| **4 — Rakipli maç** | SignalR eşleşme (mod+alt dal kuyruğu, 15 sn), bot (güç seviyesi, doğruluk, hız), arkadaş odası + davet bağlantısı + oda kodu + alan adı + App Links/Install Referrer, hazır kontrolü, 5 sn geri sayım, kopma/30 sn havuz, pes, rövanş | İki cihaz arasında rastgele ve arkadaş maçı oynanır; kopma senaryoları testlerle doğrulanır; bot maçı çalışır |
| **5 — Puan ve turnuva** | Dönemler, Redis sıralama, turnuva ekranı, ödül tablosu, inceleme ekranı + hatırlatma, men, dağıtım işi, cüzdan defteri (temel) | Simüle dönem kapanışı doğru dağıtır; aynı dönem iki kez dağıtılmaz (test) |
| **6 — Cüzdan ve mağaza** | Ürünler, kupon stoğu (şifreli), satın alma işlemi, Ödüllerim, avatar/çerçeve, puan geçmişi, kod sorun bildirimi akışı | Aynı kod iki kişiye satılmaz (yarış testi); bakiye defterle uyumlu |
| **7 — Reklam** | AdMob entegrasyonu (test reklamlarla), niyet kaydı, SSV doğrulama, joker/devam/2x akışları | Test reklamla ödül bir kez verilir; tekrarlanan callback ödül vermez |
| **8 — Operasyon** | Destek (talepler, soru önerileri), bildirim kutusu, FCM push, duyuru, dashboard, üye kontrolü, bot profilleri ve ayarlar panelleri | Panelden operasyonun tamamı yönetilir |
| **9 — Yayın hazırlığı** | Güvenlik gözden geçirme, yedek + **geri yükleme denemesi**, yasal metinler, Play Store sayfası, UMP onay mesajı, veri güvenliği formu, kapalı test (çevre), son düzeltmeler | Kapalı testte kritik hata yok; yedekten geri yükleme çalışır |

## Notlar

- **Admin paneli parçaları** ilgili fazlarda yapılır (soru girişi faz 2'de, turnuva incelemesi faz 5'te…). Soru girişi en erken gerektiği için panelin ilk işi odur.
- **Reklam (faz 7)** bilerek geç: AdMob test reklamlarıyla çalışılır, gerçek reklam için uygulamanın yayınlanması gerekir.
- **Alan adı** faz 4'te, davet bağlantısı yapılırken alınır.
- **Play Console hesabı** doğrulama için günler sürebildiğinden erken açılması önerilir; yeni kişisel hesaplarda yayına çıkmadan önce kapalı test koşulu olabilir (güncel kural açılırken teyit edilecek).
- **Görsel tasarım (K-53):** Kullanıcı kararı: tasarım en sona bırakılır. Faz 0–8 boyunca ekranlar **işlevsel, ham arayüzle** (varsayılan Material tema) yapılır ve **elle test edilebilir** olur; her şey çalışınca tasarıma başlanır (renk, tipografi, ikonlar, logo, avatar çizimleri). Tasarım aşaması Faz 8 ile Faz 9 (yayın hazırlığı) arasına denk gelir (Play Store görselleri tasarımdan sonra hazırlanır).
- **Test cihazı (K-54):** Kullanıcının Android telefonu yok; Flutter uygulaması Android emülatöründe test edilir.
- **Yerel önizleme (K-55):** Kullanıcı uygulamayı kendi bilgisayarında görüp kullanmak ister. Her fazın sonunda uygulama (a) emülatör penceresinde ve (b) Flutter web ile tarayıcı panelinde elle denenebilir durumda teslim edilir. Web önizlemesi yalnızca geliştirme içindir; AdMob gibi mobil eklentiler bir soyutlama arkasında web'de sahte/pasif çalışır.

## Faz durumu

| Faz | Durum | Not |
|---|---|---|
| 0 — İskelet | **Kabul edildi (2026-10-20, kullanıcı)** | Yerel iskelet tamam ve doğrulandı (API'ler, admin-web, Flutter emülatör + web, Postgres/Redis, SignalR echo). **Ertelenenler:** Railway (staging), Cloudflare Pages, GitHub deposu ve otomatik deploy — kullanıcı hesapları açılınca yapılacak (kabul kriterindeki "staging'de /health" ve "admin-web yayında" maddeleri bu yüzden henüz karşılanmadı, kullanıcı bunu bilerek kabul etti). |
| 1 — Giriş ve profil | **Devam ediyor (2026-10-20)** | **Tamam ve doğrulandı:** backend (Users modülü, PostgreSQL, kurallar, uçlar, tek cihaz, hesap silme; 65 test) ve mobil ekranların tamamı **geliştirme kimliğiyle (DevAuth)** — emülatörde ve tarayıcıda elle denendi. **Uygulama kimliği `com.senbilirsin.app` (K-56).** `FirebaseAuthGateway` yazıldı, derleniyor ve Firebase jeton doğrulaması (backend) sahte imzalı jetonlarla test edildi (76 test). **Kalan:** gerçek **Firebase projesi** (kullanıcı: docs/11 §6) → `google-services.json` + proje kimliği geldikten sonra gerçek giriş denenip Faz 1 kabulü. |
