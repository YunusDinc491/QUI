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
| 2 — Katalog + soru bankası + admin | **Tamamlandı (2026-10-21, geliştirme ortamında; kullanıcı kabulü bekliyor)** | Katalog ve Soru modülleri, yönetici kimliği (parola + TOTP, denetim kaydı), Admin API (kategori/soru yönetimi, CSV toplu yükleme, görsel yükleme), React admin paneli (giriş, kategori, akıcı soru girişi, liste, toplu yükleme, denetim), mobil mod → alt dal ekranları, dosya depolama soyutlaması, 60 örnek soru. Otomatik testler ve tarayıcıda panel doğrulaması yapıldı. **Denenmeyenler:** Cloudflare R2 (hesap yok), gerçek telefon kimlik doğrulayıcısı, panelin görsel görünümü, mobil katalogun emülatörde uçtan uca denemesi. |
| 3 — Bireysel oyun | **Tamamlandı (2026-10-21, geliştirme ortamında; kullanıcı kabulü bekliyor)** | Joker defteri (Wallet), soru bankası seçimi (4/3/3, son 200 hariç, gevşetme) ve bildirme, saf maç kuralları, bireysel maç servisi + uçları (sunucu zamanlaması, puan merdiveni 10→100/9→10/8→5, jokerler, reklamla devam [test kapısı], pes, terk), mobil oyun/sonuç ekranları. Bireysel maç REST ile (bkz. docs/04 §8). Testler: backend 267, Flutter 43; gerçek sunucuya karşı canlı Flutter denemesi geçti. **Denenmeyenler:** emülatör/gözle görsel deneme, gerçek ödüllü reklam (Faz 7). |

## Çalışma planı ve iş paylaşımı (2026-10-20, kullanıcı talimatı)

**Kural:** Claude'un yapabildiği her şey önce, **küçük parçalar** halinde yapılır (her parça: yap → doğrula → ilerlemeye yaz → kısaca anlat). Kullanıcıya düşen işler **en sona** bırakılır ve tek listede toplanır. Dış hizmet gerektiren yerlerde (R2, AdMob, FCM, deploy) önce yerel/test karşılığı yazılır (arayüz arkasında), gerçek hesap gelince yalnızca ayar değişir.

### Claude'un yapacakları (şimdi yapılabilir, sırayla)

| Faz | Parçalar (her biri ayrı doğrulanır) | Tahmini çalışma* |
|---|---|---|
| **1 (kalan)** | Ad değişikliği (QU¿) bitirme; Faz 1 kabulü kullanıcının gerçek giriş denemesine bağlı (Faz 2'yi **engellemez**) | ~15 dk |
| **2 Katalog + soru bankası + admin** | 2.1 Katalog modülü (mod/alt dal tabloları, 4 mod tohumu, yayındaki kategoriler uçları) · 2.2 Soru modülü (kurallar, CRUD servisi, tekrar uyarısı) · 2.3 Admin kimliği (parola + TOTP, denetim kaydı, ilk admin komutu) · 2.4 Admin API (kategori/soru yönetimi, CSV/Excel toplu yükleme önizleme+hata raporu, sayaçlar) · 2.5 Admin web (giriş, kategori yönetimi, akıcı soru girişi "kaydet ve yeni", liste/filtre, toplu yükleme) · 2.6 Mobil katalog ekranları (mod → alt dal, Karışık) · 2.7 Dosya depolama soyutlaması (yerel + R2 uygulaması) · 2.8 Geliştirme için örnek test soruları (açıkça "test verisi" işaretli) | 3–4 saat |
| **3 Bireysel oyun** | Soru seçimi (son 200, 4/3/3), maç motoru + süre, puan merdiveni, jokerler (defter), reklamla devam (test reklamı arayüzü), soru bildirme, ekranlar + **otomatik testler** | 3–4 saat |
| **4 Rakipli maç** | Eşleşme kuyruğu, bot, oda/davet (bağlantı mantığı), kopma/30 sn havuz, pes, rövanş, SignalR sözleşmesi + testler, ekranlar | 5–6 saat |
| **5 Puan ve turnuva** | Dönemler, Redis sıralama, ödül tablosu, inceleme ekranı, men, dağıtım işi, cüzdan defteri + testler | 3–4 saat |
| **6 Cüzdan ve mağaza** | Ürünler, kupon stoğu (şifreli), atomik satın alma, Ödüllerim, avatar/çerçeve, itiraz akışı + yarış testi | 3–4 saat |
| **7 Reklam** | AdMob arayüzü (Google'ın herkese açık **test** reklam kimlikleriyle), niyet kaydı + SSV doğrulama mantığı + testler | 2 saat |
| **8 Operasyon** | Destek, bildirim kutusu, FCM mantığı (kimlik anahtarı gelene kadar sahte gönderici), duyuru, dashboard, üye kontrolü, bot/ayar panelleri | 4–5 saat |
| **9 Yayın hazırlığı (Claude kısmı)** | Güvenlik gözden geçirme, yedek/geri yükleme betikleri, yasal metin **taslakları**, Play Store sayfası metinleri, veri güvenliği formu taslağı | 2–3 saat |
| **Tasarım** | Faz 8 ile 9 arası (K-53): renk, ikon, logo/QU¿ yazı karakteri, avatar/çerçeve çizimleri — kullanıcıyla birlikte | ayrı konuşma |

\* Kaba tahmin: bugünkü hıza göre (Faz 0 + Faz 1 ≈ 2 saatlik çalışma + bekleme) çalışma süresi; derleme/indirme beklemeleri hariç, sapabilir.

### Kullanıcının yapacakları (en sonda, tek liste)

1. **Gerçek giriş denemesi** (Faz 1 kabulü): emülatörde e-posta ve Google ile giriş (kendi hesabınla).
2. **Hesaplar:** Google Play Console (doğrulama günler sürer, erken başla), Cloudflare (R2 + Pages + DNS), Railway (yeni proje), GitHub deposu, AdMob, **alan adı** (ad QU¿ olduğu için yeniden belirlenecek, A-12), Firebase FCM için hizmet hesabı anahtarı (indirilen JSON dosyasının yolu `.env`'e; içeriği sohbete yazılmaz).
3. **İçerik:** gerçek sorular ve alt dallar (admin panelinden), avatar/çerçeve görselleri, bot profilleri, ödül tablosu ve ürün fiyatları, kupon stoğu (elle satın alma), logo/ikon.
4. **Yasal / vergi:** SMMM ve avukat görüşmeleri (docs/10 listesi), yasal metinlerin kontrolü, Play kapalı test için test kullanıcıları (yaklaşık 12 kişi, 14 gün — kural güncel kaynaktan teyit edilecek).
5. **Kararlar:** docs/10'daki açık sorular (A-04 ödül tablosu, A-06 kupon markaları, A-11 yaş sınırı, A-12 alan adı vb.).
