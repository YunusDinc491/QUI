# SenBilirsin — İlerleme Günlüğü

> **Kural (CLAUDE.md, 0. madde):** Yapılan her şey, sebebi ve ayrıntılarıyla, yapıldığı an buraya yazılır. Hiçbir şey eksik bırakılmaz, atlanmaz.
> Dosya **yalnızca sonuna eklenir**; eski kayıt silinmez/düzenlenmez. Hata olursa yeni bir "Düzeltme" kaydı eklenir.
> Saatler Türkiye saatidir (UTC+3). Yeni kayıt, en alttaki `<!-- KAYIT-SONU -->` işaretinin hemen üstüne eklenir.

## Kayıt şablonu

```
### YYYY-MM-DD HH:MM — Kısa başlık
- **Faz:** (örn. Hazırlık / Faz 0 …)
- **Ne yapıldı:** …
- **Neden:** …
- **Değişen dosyalar:** …
- **Doğrulama:** (komut/test ve gerçek sonucu; yapılmadıysa "yapılmadı" yazılır)
- **Sorun / çözüm:** …
- **Sıradaki adım:** …
```

---

## Kayıtlar

### 2026-10-20 13:22 — Planlama oturumu: kararlar kullanıcıyla netleştirildi (kod yazılmadı)
- **Faz:** Hazırlık (planlama)
- **Ne yapıldı:** Kullanıcıyla uzun bir konuşmada ürün, oyun kuralları, mimari, altyapı, admin paneli, ekonomi/mağaza, güvenlik, ekran akışı, veri modeli, SignalR olayları, fazlar ve çalışma kuralları netleştirildi. Bilgisayardaki araçlar salt-okunur kontrol edildi (kurulum yapılmadı). AdMob/AdSense gelirinin Türkiye'deki vergisel durumu için web araştırması yapıldı; kaynaklar çelişkili çıktı, kesin bilgi olarak alınmadı.
- **Neden:** Kod yazmadan önce her detayı konuşup md dosyalarına dökmek ve yapım aşamasında takılmamak (kullanıcının talebi).
- **Değişen dosyalar:** Henüz yok (bu kayıttan sonra oluşturulan dosyalar aşağıda ayrı kayıtlarla işlendi).
- **Doğrulama:** Araç kontrolü sonucu — git 2.54, .NET SDK 10.0.400/10.0.401, Node 24.20, npm 11.19, Docker 29.8, gh 2.101, Java 26 mevcut; Flutter, adb/Android SDK, Android Studio, psql yok. Telefon/emülatör durumu bilinmiyor.
- **Sorun / çözüm:** Yok.
- **Sıradaki adım:** CLAUDE.md ve `docs/` md dosyalarının oluşturulması, Stop hook'unun kurulması.

### 2026-10-20 13:40 — CLAUDE.md oluşturuldu
- **Faz:** Hazırlık
- **Ne yapıldı:** Proje kökünde `CLAUDE.md` yazıldı. İçerik: 0) ilerleme kaydı kuralı (kullanıcının verdiği, atlanamaz), 1) kararlar dokunulmaz kuralı, proje özeti, doküman haritası, değişmez mimari ilkeler, çalışma şekli, kod kuralları (kod İngilizce, yorum/UI Türkçe, monorepo, test zorunluluğu), git kuralları, kullanıcı notları.
- **Neden:** Kullanıcı, kurallar belirlendikten sonra proje özel CLAUDE.md istedi; ilk kural olarak ilerleme kaydı yazılacaktı.
- **Değişen dosyalar:** `CLAUDE.md` (yeni)
- **Doğrulama:** Dosya Write ile yazıldı; içerik kullanıcının onayladığı 8 soru + hook kararıyla karşılaştırıldı.
- **Sorun / çözüm:** Yok.
- **Sıradaki adım:** `docs/ilerleme.md` (bu dosya) ile `docs/` altındaki md'lerin oluşturulması.

### 2026-10-20 13:41 — docs/ilerleme.md oluşturuldu
- **Faz:** Hazırlık
- **Ne yapıldı:** Bu dosya oluşturuldu: kural başlığı, kayıt şablonu, ilk iki kayıt, "Yarım kalan / sıradaki adım" bölümü.
- **Neden:** CLAUDE.md 0. kuralı gereği ilerleme günlüğü.
- **Değişen dosyalar:** `docs/ilerleme.md` (yeni)
- **Doğrulama:** Dosya Write ile yazıldı.
- **Sorun / çözüm:** Yok.
- **Sıradaki adım:** `docs/00` … `docs/11` md dosyalarının yazılması.

### 2026-10-20 13:25 — Düzeltme: önceki iki kaydın saatleri yanlış yazılmıştı
- **Faz:** Hazırlık
- **Ne yapıldı:** "13:40 — CLAUDE.md oluşturuldu" ve "13:41 — docs/ilerleme.md oluşturuldu" kayıtlarındaki saatler ölçülmeden yazılmıştı. Dosya yazma zamanları sistemden okundu: `CLAUDE.md` ve `docs/ilerleme.md` gerçekte **13:24**'te oluşturuldu.
- **Neden:** Kayıtların doğru olması (kural: sonuç/saat uydurulmaz). Eski kayıt kurala göre düzenlenmedi, bu düzeltme eklendi.
- **Değişen dosyalar:** `docs/ilerleme.md` (yalnızca ekleme)
- **Doğrulama:** `Get-Item ... LastWriteTime` çıktısı: CLAUDE.md 13:24:53, ilerleme.md 13:24:53; şimdiki saat 13:24 (komut anında).
- **Sorun / çözüm:** Bundan sonra her kayıttan önce saat `Get-Date` ile okunacak. Ek not: kayıt işareti `<!-- KAYIT-SONU -->` başlıkta da geçtiği için ekleme yaparken altındaki `## Yarım kalan / sıradaki adım

- **Çalışma modu:** Gece özerk çalışma (kullanıcı uyuyor). Faz 2 → 9 sırayla, küçük parçalar, her parçada test; her faz sonunda commit + günlük. **Push YOK** (repo public, onay bekliyor). Koruma kuralları: 2026-10-21 00:39 kaydı.
- **Şu an:** Faz 2, Parça 2.1 (Katalog modülü) başlıyor.
- **Faz 0:** Kabul edildi. Ertelenen: Railway, Cloudflare Pages, GitHub otomatik deploy.
- **Faz 1:** Backend + mobil ekranlar + Firebase kodu **tamam**; kalan: kullanıcının gerçek giriş denemesi (e-posta + Google) ve Faz 1 kabulü. Uygulama adı QU¿; paket `com.senbilirsin.app`; Firebase proje `senbilirsin-832b8`.
- **Yerel ortam:** Docker postgres (5433) / redis (6380); emülatör `senbilirsin_pixel`; `mobile-api` önizleme sunucusu (5080) çalışıyor olabilir. JDK 21: `C:\Users\Lider\dev\jdk-21`; Flutter: `C:\Users\Lider\dev\flutter` (PATH'te değil, tam yolla).
- **Kullanıcıya kalanlar (sabah):** repo private/push kararı, gerçek giriş denemesi, hesaplar (Play Console, Cloudflare, Railway, AdMob, alan adı, FCM anahtarı), içerik, yasal/vergi, açık sorular (docs/10), tasarım.
- **Disk:** C: ~14 GB boş. Git: yerel commit'ler, `origin` tanımlı, push yok.
