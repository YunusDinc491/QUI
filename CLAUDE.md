# QU¿ — Claude Çalışma Kuralları

Bu dosya projeye özeldir ve her oturumun başında okunur. Kullanıcının genel tercihleri `~/.claude/CLAUDE.md` içindedir (Türkçe konuş, adım adım ne ve neden anlat, uzun işlerde kalan süreyi söyle, uydurma sonuç verme). Bu dosya onların üstüne proje kurallarını ekler.

## 0. BİRİNCİ KURAL — İlerleme kaydı (atlanamaz)

**Yapılan her şey, sebebi ve ayrıntılarıyla, yapıldığı an `docs/ilerleme.md` dosyasına yazılır. Hiçbir şey eksik bırakılmaz, atlanmaz.**

- Dosya yalnızca **sonuna eklenir**. Eski kayıt silinmez ve düzenlenmez; yanlış bir kayıt çıkarsa yeni bir "Düzeltme" kaydı eklenir.
- Kayıt işin **hemen ardından** yazılır. Bir adım kayda geçmeden sonraki adıma geçilmez.
- "Yapılan her şey" şunları kapsar: dosya oluşturma/değiştirme/silme, komut çalıştırma, kurulum, karar, hata ve çözümü, test ve gerçek sonucu, kullanıcıyla varılan anlaşma, vazgeçilen yol.
- Her kayıtta bulunacaklar: tarih-saat (Türkiye saati, `2026-10-20 14:35`), faz, ne yapıldı, neden, değişen dosyalar, nasıl doğrulandı (çalıştırılan komut/test ve **gerçek** sonucu), varsa sorun ve çözümü, sıradaki adım.
- Her mesajımın sonunda "ilerleme dosyasına şu kayıt eklendi" diye teyit edilir.
- Oturum yarıda kalırsa dosyanın sonundaki **"Yarım kalan / sıradaki adım"** bölümü güncellenir; yeni oturum oradan devam eder.
- Bu kuralı bir **Stop hook** denetler (`.claude/hooks/check-progress.mjs`): `docs/ilerleme.md` en son değişen dosyadan eskiyse oturum bitmeden uyarı gelir. Hook'u devre dışı bırakma, atlatma.

## 1. İkinci kural — Kararlar dokunulmaz

- `docs/` altındaki md dosyalarında netleşmiş kararlar kullanıcıya **sormadan değiştirilmez ya da düzenlenmez**.
- Bir kararı değiştirmek gerekirse (kullanıcı isterse ya da bir çelişki bulunursa) önce sor. Onaydan sonra md güncellenir ve `docs/00-vizyon-ve-kararlar.md` içindeki **"Karar değişiklik günlüğü"**ne eski hali, yeni hali, neden ve tarih eklenir; ilerleme dosyasına da yazılır.
- md'de karşılığı olmayan bir durumda tahmin etme, sor. Çok küçük işlerde makul varsayılan seç ve ilerleme dosyasına "varsayım" diye yaz.
- Kullanıcının o anki talimatı md'lerdeki her şeyin üstündedir; ama o durumda da md güncellemesi için onay al.

## 2. Proje özeti

Türkçe mobil bilgi yarışması; uygulama adı **QU¿** (K-01). Kod/kimlik adları (`SenBilirsin.*`, `com.senbilirsin.app`, Firebase `senbilirsin-832b8`) bilerek eski haliyle kalır; kullanıcıya görünen her yerde "QU¿" yazılır. İlk sürüm yalnızca Android. 4 mod: Genel, Branş seçmeli, KPSS tarzı, Fanlara özel. Bireysel ve rakipli (rastgele / arkadaş daveti / isteğe bağlı bot) maçlar, 10 soru, 15 sn. 2 günlük turnuva sıralaması; ilk 10'a hediye puanı; hediye puanıyla mağazadan kupon, joker, avatar, çerçeve. Ödüllü reklam (AdMob).

Teknoloji: Flutter · C# .NET 10 modüler monolit · PostgreSQL · Redis · SignalR · Firebase Auth/FCM/Crashlytics · AdMob (SSV) · Cloudflare R2/Pages/DNS · Railway · React admin paneli + ayrı admin API.

## 3. Doküman haritası (`docs/`)

| Dosya | İçerik |
|---|---|
| `00-vizyon-ve-kararlar.md` | Ürün, kararlar (K-numaralı), kapsam dışı, **karar değişiklik günlüğü** |
| `01-oyun-kurallari.md` | Modlar, maç akışı, puanlar, joker, reklam, kopma, bot, arkadaş odası |
| `02-mimari-ve-altyapi.md` | Modüller, servisler, Redis/bellek, deploy, yedek, ortamlar |
| `03-veri-modeli.md` | Tablolar ve alanlar |
| `04-signalr-ve-api.md` | SignalR olayları, REST uçları |
| `05-admin-paneli.md` | Panel bölümleri ve davranışları |
| `06-mobil-ekran-akisi.md` | Ekran haritası ve geçişler (tasarım en sona) |
| `07-ekonomi-ve-magaza.md` | Turnuva, hediye puanı, cüzdan, mağaza, kupon |
| `08-guvenlik-ve-hile-onleme.md` | Güvenlik, hile, KVKK, saklama |
| `09-gelistirme-fazlari.md` | Fazlar ve kabul kriterleri |
| `10-riskler-yasal-ve-acik-sorular.md` | Riskler, yasal araştırma, açık sorular |
| `11-hesaplar-ve-kurulum-kontrol-listesi.md` | Hesaplar, araçlar, kurulum |
| `ilerleme.md` | Zaman çizelgeli ilerleme günlüğü (append-only) |

## 4. Değişmez mimari ilkeler

1. **Sunucu her şeyi belirler.** Doğru cevap istemciye hiçbir zaman gönderilmez; cevap süresini sunucu kendi saatiyle ölçer.
2. **Hediye puanı ve joker değişimi yalnızca defter (ledger) kaydıyla yapılır.** Bakiye defterin toplamıdır; `idempotency_key` benzersizdir.
3. Modüller birbirinin tablosuna doğrudan dokunmaz; tanımlı arayüzden erişir.
4. Admin API, mobil API'den ayrı bir host'tur (aynı solution, aynı veritabanı).
5. Gizli bilgiler (anahtar, parola, kupon kodu, servis hesabı) **hiçbir dosyaya, loga ya da sohbete yazılmaz.** `.env`/ortam değişkeni kullanılır; örnek için `.env.example`.
6. Ödüllü reklam sadece sunucu doğrulamasıyla (SSV + niyet kaydı) ödül verir. Oyun sırasında ani/aracı reklam (interstitial) yoktur.
7. Basit tut: tek instance, aşırı mühendislik yok. Kapsam dışı bir şey eklemek gerekirse önce sor.

## 5. Çalışma şekli

- Faz sırasına uy (`docs/09-gelistirme-fazlari.md`); bir faz bitmeden diğerine geçme.
- **Tasarım en sona (K-53):** Tasarım aşamasına kadar ekranlar işlevsel/ham yapılır (varsayılan Material tema) ve elle test edilebilir olmalıdır; süslü arayüz yazma.
- Kod değişikliğinden önce **ne ve neden** yaptığını anlat.
- Test etmediğin şeyi "test ettim" deme. Başarısız sonucu olduğu gibi göster.
- Kalıcı kurulum (Flutter, Android Studio vb.), **yeni paket/bağımlılık** (NuGet, pub, npm; neden gerekli, alternatifi ne) ve dış servis işlemi öncesi kullanıcıya söyle. Hesap açma, ödeme, deploy, dışarıya mesaj gibi dışarıya etki eden işleri onay alarak yap.
- Oturum başında bu dosyayı ve `docs/ilerleme.md`'nin son kayıtlarını oku.
- Her adım sonunda kısa özet ver: ne yapıldı, ne doğrulandı, ne kaldı.
- **Küçük parçalar (kullanıcı kuralı, 2026-10-20):** İşi küçük, sınırları belli parçalara böl (her parça: yap → doğrula (test/derleme/ekran) → ilerlemeye yaz → kullanıcıya ne yaptığını kısaca anlat). Büyük işi tek seferde yazma; varsayıma/tahmine dayanma, emin değilsen önce dosyaya/koda bak. **Kullanıcıya düşen işler (hesap açma, mağaza/ödeme, gerçek hesapla giriş denemesi, içerik girişi, onaylar) en sona bırakılır**; Claude'un yapabildiği her şey önce yapılır, kullanıcı işleri tek bir listede toplanıp sonda verilir.

## 6. Kod kuralları

- **Kod İngilizce** (sınıf, tablo, uç, Dart), **ekrandaki metinler Türkçe**, **kod yorumları Türkçe**.
- Depo yapısı (monorepo): `backend/`, `admin-web/`, `mobile/`, `docs/`. Md'ler ve ilerleme dosyası `docs/` altında; dosya adlarında Türkçe karakter yok.
- Kararlı sürümler kullan (.NET 10, Flutter stable). Biçimlendirici/analyzer uyarılarını temiz tut.
- **Test:** maç motoru, puanlama, defter (ledger) ve turnuva dağıtımı için otomatik test **zorunlu**. Arayüz için zorunlu değil; elle doğrulanır ve ilerleme dosyasına yazılır.

## 7. Git

- Faz 0'da `git init` yapılır (monorepo). Commit'i kullanıcı "commit at" deyince at; küçük ve anlamlı commit'ler.
- `.gitignore`: `.env`, anahtarlar, `google-services.json` dışındaki servis hesabı dosyaları, `bin/obj`, `node_modules`, `build/`, `.dart_tool/`.
- Commit mesajı sonuna: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

## 8. Kullanıcı hakkında

Türkçe konuşur, kod bilgisi orta seviye. İşler adım adım, ne ve neden anlatılarak yapılır. Uzun işlerde geçen süreyi değil bitmesine kalan tahmini süreyi söyle. Hesap açma (Play Console, Firebase, AdMob, Cloudflare, Railway) adımlarını kullanıcı yapar, sen yol göster.
