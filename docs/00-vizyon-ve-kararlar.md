# 00 — Vizyon ve Kararlar

> **Kural:** Bu dosyadaki (ve diğer md'lerdeki) netleşmiş kararlar kullanıcıya sormadan değiştirilmez. Değişiklik gerekirse önce sorulur, onaydan sonra en alttaki **Karar değişiklik günlüğü**ne yazılır (CLAUDE.md, 1. madde).
> Karar tarihleri: hepsi 2026-10-20 planlama oturumunda kullanıcıyla netleştirildi.

## 1. Ürün

**SenBilirsin**: Türkçe, mobil bir bilgi yarışması oyunu. Bireysel ya da rakipli oynanır. Rakip rastgele bulunur, arkadaş daveti ile gelir ya da kullanıcı isterse bot olur. Kazanılan puan 2 günlük turnuva sıralamasını belirler; sıralamada ilk 10'a girenler "hediye puanı" kazanır. Hediye puanı ile mağazadan gerçek ödül (hediye kartı/kupon kodu gibi), joker, avatar ve avatar çerçevesi alınır. Gelir kaynağı ödüllü reklamdır.

İlk yayın: **yalnızca Android**, **yalnızca Türkçe**. Çevredeki insanlardan gelen geri bildirimle güncellenerek ilerlenir; maliyetli kısımlar (iOS vb.) sonra eklenir.

**Hedef ölçek:** ilk hedef en fazla ~100 eşzamanlı oyuncu; tek instance; mimari mümkün olduğunca basit.

## 2. Kararlar

| No | Karar | Ayrıntı |
|---|---|---|
| K-01 | Uygulama adı **SenBilirsin**; logo henüz yok | — |
| K-02 | Mobil: **Flutter**, ilk sürüm Android | 02 |
| K-03 | Backend: **C# .NET 10 modüler monolit** | 02 |
| K-04 | Veritabanı: **PostgreSQL** | 03 |
| K-05 | **Redis**: sıralama, kuyruk, önbellek, hız sınırı, çevrimiçi bilgisi | 02 |
| K-06 | Gerçek zamanlı: **SignalR** | 04 |
| K-07 | Barındırma: **Railway** (Hobby plan; duruma göre yükseltilir), admin arayüzü Cloudflare Pages | 02 |
| K-08 | Kimlik: **Firebase Auth** (Google, e-posta; Apple iOS'ta); sağlayıcı bağlama/çıkarma; en az bir yöntem bağlı kalır | 08 |
| K-09 | Reklam: **AdMob ödüllü reklam + SSV**; oyun sırasında ani reklam yok | 01, 07 |
| K-10 | Görsel/dosya depolama: **Cloudflare R2** | 02 |
| K-11 | Admin: **React + ayrı admin API** (aynı solution, ayrı host); Sentry yok, Crashlytics var | 05 |
| K-12 | Alan adı gerektiğinde (faz 4, davet bağlantısı) Cloudflare üzerinden alınır; kod baştan ayarlardan okunur | 02 |
| K-13 | **4 mod**: Genel, Branş seçmeli, KPSS tarzı, Fanlara özel. Alt dalları admin belirler | 01 |
| K-14 | Maç: **10 soru, soru başı 15 sn, 4 şık (A–D)**; zorluk sırası 4 kolay + 3 orta + 3 zor (ayarlanabilir) | 01 |
| K-15 | Rakip seçimi: **Bireysel / Arkadaşla / Rastgele** | 01 |
| K-16 | Rastgele aramada **15 sn** rakip aranır; bulunmazsa kullanıcıya **Bota karşı / Bireysel / Tekrar ara** seçenekleri sunulur. **Otomatik bot eşleşmesi yok**, bot açıkça "Bot" etiketiyle görünür | 01 |
| K-17 | Bot profilleri admin panelinden tek tek yönetilir (isim, avatar, güç seviyesi, geçmiş) | 01, 05 |
| K-18 | **Bireysel mod**: yanlışta elenir; 10 doğru = 100, 9 doğru = 10, 8 doğru = 5, 7 ve altı = 0 puan; bir kez reklamla devam | 01 |
| K-19 | **Rakipli mod**: 10 soru hepsi oynanır, en çok doğru yapan kazanır; kazanan 10, kaybeden 0 puan; bonus +5; beraberlikte eşiğin üstünde doğru varsa ikisi de doğru sayısı × katsayı alır | 01 |
| K-20 | Puan kuralları **mod bazında** (Genel/Branş/KPSS/Fan) admin panelinden ayarlanır | 01, 05 |
| K-21 | Maç sonu reklamla **puanı 2'ye katlama**: sadece puan kazananlara, bonus dahil, sıralamaya yansır | 01, 07 |
| K-22 | Arkadaş ve bot maçlarının puanı sıralamaya girer; puan kasma riski kabul edilmiştir | 01, 10 |
| K-23 | **Jokerler**: yarı yarıya (2 yanlış şık elenir), çift cevap, ipucu (admin ayarlı olasılıkla doğru şıkkı gösterir; başlangıç %75). Soru başına en fazla bir joker. Rakip joker kullanımını görmez. Zamanla ilgili joker yok | 01 |
| K-24 | Yeni kayıt olana **her jokerden 5 adet** hediye; jokerler ödüllü reklamla (günlük sınırsız) veya mağazadan **5 hediye puanı** karşılığı sınırsız alınır | 01, 07 |
| K-25 | Bağlantı kopması: **maç başına 30 sn ortak süre havuzu**; soru açıkken kopanın soru süresi durmaz; havuz biterse kaybeder | 01 |
| K-26 | Bilerek çıkış (pes): rakipli modda anında kayıp, bireyselde o ana kadarki doğru sayısına göre puan | 01 |
| K-27 | Arkadaş odası: davet **bağlantısı + oda kodu**; oda 5 dk geçerli, tek rakip; kurucu çıkarsa kapanır; bitince **rövanş** istenebilir | 01 |
| K-28 | **Turnuva**: 48 saatlik arka arkaya dönemler (Türkiye saati); tek genel sıralama; puanı 0 olmayan ilk 50 listelenir, kullanıcı kendi sırasını görür; **eşit puanlı herkes aynı sıranın ödülünü alır** | 07 |
| K-29 | Ödül tablosu sıralamaya göre admin panelinden ayarlanır; tur bitince ilk 10'a **hediye puanı** verilir | 07 |
| K-30 | Bitişten **1 saat önce** admin elle inceleme yapar (e-posta + panel rozeti ile hatırlatma); hileli kullanıcı turnuvadan men edilir ya da banlanır; yetişmezse ödül otomatik dağıtılır ve geri alınmaz | 07, 08 |
| K-31 | **Hediye puanı** süresiz; 1 puan = 0,05 TL kabul edildi; tüm hareketler defter (ledger) ile | 07 |
| K-32 | **Mağaza**: kupon/hediye kartı, joker, avatar, avatar çerçevesi. Avatar ve çerçeve bir kez alınır; joker/kupon için limit yok | 07 |
| K-33 | Kupon kodları **tek tek elle** alınıp panelden yüklenir; satın alınca stoktan otomatik düşer, "Ödüllerim"de görünür | 07 |
| K-34 | **Profil resmi yüklenmez**; hazır avatarlar (5 ücretsiz, 10–15 ücretli, admin tek tek yükler). Çerçeve rakibe görünür | 06, 07 |
| K-35 | Kullanıcı adı **benzersiz ve değiştirilemez** (3–16 karakter, küçük harf/rakam/alt çizgi); görünen ad değiştirilebilir; küfür filtresi var; admin gerekçeyle zorla değiştirebilir | 08 |
| K-36 | Kayıtta **doğum yılı** sorulur; minimum yaş 13; ödüllerde yaş sınırı yasal araştırmadan sonra | 08, 10 |
| K-37 | Hesap silme zorunlu (uygulama içi), anında; geçmiş rakipte "Silinmiş kullanıcı" | 08 |
| K-38 | **Tek cihaz kuralı**; cihaz kimliği loglanır; aynı cihazdan 3'ten fazla hesap işaretlenir | 08 |
| K-39 | Soru bankası: 4 şık, 3 zorluk, opsiyonel açıklama; tekli (akıcı form) ve toplu yükleme; tekrar uyarısı; oyunda "soruyu bildir" | 05 |
| K-40 | Soru tekrarı: kullanıcının son **200** gördüğü soru tutulur; hepsi bitince rastgele tekrar ve panele uyarı | 01 |
| K-41 | Destek: kategorili talepler (mesajlaşma şeklinde), soru önerisi, soru bildirimi; yanıt uygulama içi + push (e-posta yok) | 05 |
| K-42 | Bildirimler: push + uygulama içi kutu; Android 13 izni ilk maçtan sonra istenir; pazarlama amaçlı günlük hatırlatma yok | 06 |
| K-43 | Maç durumu bellekte (tek instance); deploy/yeniden başlatmada süren maçlar iptal edilir (puan değişmez), önce bakım modu | 02 |
| K-44 | Postgres günlük yedeği R2'ye; **staging + prod** ortamı; monorepo; Serilog + uptime kontrolü + Crashlytics | 02 |
| K-45 | Zorunlu güncelleme (min sürüm) ve bakım modu baştan var | 02, 05 |
| K-46 | Veri saklama: ayrıntılı oyun/cevap logları 90 gün, maç özetleri 1 yıl, admin denetim kayıtları 2 yıl | 08 |
| K-47 | Play Integrity ilk sürümde **bayrak** olarak kullanılır, engellemez | 08 |
| K-48 | Geliştirme **10 faz** (0–9) | 09 |
| K-49 | Çalışma kuralları: kod İngilizce, yorum Türkçe, UI Türkçe; maç/puan/defter/turnuva testleri zorunlu; commit'i kullanıcı söyleyince | CLAUDE.md |
| K-50 | Stop hook ile ilerleme kaydı denetlenir | CLAUDE.md |
| K-51 | Rakipli modda **bonus eşiği 8 ve üzeri** doğru (≥8); beraberlik puanı da bu eşik ve üzerinde verilir | 01 |
| K-52 | **Her joker türü bir maçta bir kez** kullanılabilir (3 tür → en çok 3 joker/maç); soru başına en fazla bir joker kuralı da geçerli | 01 |
| K-53 | **Tasarım en sona**: önce tüm işlevler (faz 0–8) çalışan, elle test edilebilir iskelet/ham arayüzle yapılır (varsayılan Material tema); tasarım her şey çalışınca başlar | 06, 09 |
| K-54 | Kullanıcının Android telefonu yok → test **Android emülatörüyle** yapılır | 11 |
| K-56 | Android **uygulama kimliği (paket adı): `com.senbilirsin.app`**. Alan adı büyük olasılıkla `senbilirsin.com` olacak (henüz alınmadı; kesinleşince/alınınca işlenir, davet bağlantısı faz 4) | 10, 11 |
| K-55 | **Yerel önizleme**: kullanıcı uygulamayı bilgisayarda görüp kullanabilmeli. Ana test ortamı **Android emülatörü** (masaüstünde pencere); hızlı önizleme için **Flutter web** (uygulamanın tarayıcı panelinde). Web yalnızca geliştirme önizlemesidir, yayın hedefi değildir; yalnızca mobilde çalışan eklentiler (AdMob vb.) soyutlama arkasında web'de sahte/pasif olur | 09, 11 |

## 3. Kapsam dışı (ilk sürümde YOK)

iOS ve Apple ile giriş · kendi fotoğrafını profil resmi yapma · ani/ara reklam (interstitial) · pazarlama amaçlı günlük hatırlatma bildirimi · günlük görev / günün sorusu / arkadaş listesi / ana sayfada kart dışı içerik · seviye ve rütbe sistemi (toplam puan sadece göstergedir) · çevrimdışı mod · e-posta ile destek yanıtı · Sentry · kod/kupon sağlayıcı API entegrasyonu · çok instance / SignalR backplane · Telegram bildirimi · çok seviyeli (3+) kategori.

## 4. Karar değişiklik günlüğü

> Yeni kayıt: tarih, eski karar, yeni karar, neden, kullanıcı onayı. Aşağıdakiler planlama sırasında (md'ler yazılmadan önce) değişen kararlardır; bilgi amaçlı işlenmiştir.

| Tarih | Eski | Yeni | Neden |
|---|---|---|---|
| 2026-10-20 | Rastgele aramada 10 sn sonra otomatik, gizli bot eşleşmesi | 15 sn arama, sonra kullanıcıya Bot/Bireysel/Tekrar ara seçeneği; bot etiketli | Kullanıcı kararı (risk ve basitlik) |
| 2026-10-20 | 5 şıklı sorular | 4 şıklı sorular | "Yarı yarıya" jokerinin anlamlı olması |
| 2026-10-20 | Reklam sadece joker kazanma + maç sonu katlama | Bireysel modda yanlışta bir kez reklamla devam da var | Kullanıcı netleştirdi (b) |
| 2026-10-20 | Maç sonu katlama kaybedene de teklif | Sadece puan kazananlara | Kaybedenin puanı 0 |
| 2026-10-20 | "Günlük ve 2 günlük" iki tablo (belirsiz) | Tek tablo, 2 günlük dönem | Öneri onaylandı |
| 2026-10-20 | Kopmada "maç başına 2 hak" önerisi | 30 sn ortak süre havuzu | Kullanıcı önerisi |
| 2026-10-20 | Profil resmi yükleme | Hazır avatar seçimi | Kullanıcı kararı (denetim/maliyet) |
| 2026-10-20 | Bireyselde "reklam yok" / mağaza ilk sürümden çıkarılabilir önerisi | Mağaza (kupon, joker, avatar, çerçeve) kapsamda | Kullanıcı kararı (e) |
| 2026-10-20 | Bonus eşiği belirsizdi (">8" mi "≥8" mi) | **≥8** (K-51) | Kullanıcı kararı (A-01) |
| 2026-10-20 | Maç başına joker limiti belirsizdi (öneri: toplam 2) | **Her joker türü maçta bir kez** (K-52) | Kullanıcı kararı (A-02) |
| 2026-10-20 | Tasarım konuşmasının zamanı belirsizdi (öneri: faz 0 sonu / faz 1 başı) | **Tasarım en sona**, önce çalışan ham iskelet (K-53) | Kullanıcı kararı (A-13) |
| 2026-10-20 | Test cihazı bilinmiyordu | Android telefon yok, **emülatör** (K-54) | Kullanıcı yanıtı (A-15) |
