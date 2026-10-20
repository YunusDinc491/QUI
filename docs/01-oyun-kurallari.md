# 01 — Oyun Kuralları

> Kararlar K-13 … K-27, K-40 ile bağlantılıdır. Burada değişiklik için kullanıcıya sorulur.

## 1. Modlar ve kategoriler

| Mod | Alt dal | Notlar |
|---|---|---|
| Genel sorular | Yok (tek örtük kategori) | Doğrudan rakip seçimine gidilir |
| Branş seçmeli | Tarih, Türkçe, Matematik vb. | Alt dalları admin belirler |
| KPSS tarzı | Alt dallar admin belirler | |
| Fanlara özel | Alt dallar admin belirler (takım, dizi vb.) | |

- Hiyerarşi **iki seviye**: Mod → alt dal (ileride gerekirse artırılır, K-13).
- Branş, KPSS ve Fan modlarında alt dal listesinin başında **"Karışık"** vardır; o moddaki yayındaki tüm alt dallardan karışık soru gelir.
- Her mod ve alt dalın ikonu/görseli ve rengi olabilir (admin yükler).
- Bir kategori admin "yayınla" diyene kadar kullanıcıya görünmez. Yeterli soru olduğunu admin kendisi kontrol eder; panelde soru sayıları görünür ama engel/uyarı yoktur.

## 2. Maç türleri

| Tür | Rakip | Kural seti |
|---|---|---|
| Bireysel | Yok | Bireysel kural (eleme) |
| Rastgele | Gerçek oyuncu | Rakipli kural |
| Bot | Bot (kullanıcı seçer) | Rakipli kural |
| Arkadaş | Davetle gelen kişi | Rakipli kural |

## 3. Soru kuralları

- Her maç **10 soru**, her soru **15 saniye**, **4 şık (A–D)**.
- Zorluk sırası (ayarlanabilir): **1–4 kolay, 5–7 orta, 8–10 zor**.
- Şık sırasını **sunucu karıştırır**; iki rakip aynı soruları aynı sırada ve aynı şık dizilimiyle görür.
- Soru seçimi: kullanıcının **son 200 gördüğü** sorular hariç tutulur (rakipli maçta iki oyuncununki de). Yeterli soru yoksa kısıt gevşer ve panele uyarı düşer. Hepsi bitince rastgele tekrar eder ve admin paneline uyarı gelir.
- Sorunun opsiyonel **açıklama** alanı olabilir; maç sonu incelemede doluysa gösterilir.
- Doğru cevap hiçbir zaman istemciye gitmez; süreyi sunucu ölçer (küçük ağ toleransıyla).

## 4. Soru akışı

1. Sunucu soruyu ve **mutlak bitiş zamanını** yollar; istemci geri sayımı sunucu saat farkıyla gösterir.
2. Oyuncu bir şıkka dokunur: **ilk dokunuş kesin cevaptır**, seçili şık turuncu görünür. Değiştirilemez.
3. Rakipli maçta rakip cevap verince küçük bir "rakip cevapladı" ışığı yanar.
4. **İkisi de cevaplayınca 1 sn sonra** (veya süre bitince) sonuç gösterilir: doğru cevap, kendi cevabı, rakibin cevabı, skor.
5. Sonuç ekranı **3 sn** kalır, sıradaki soru gelir. Akıcılık için başka bekleme yok.
6. Süre dolunca cevap vermeyen **yanlış** sayılır.

## 5. Bireysel mod kuralları

- Yanlış cevap (veya süre dolması) **elenmeyi** getirir ve oyun biter.
- **Puan (doğru sayısına göre):** 10 doğru = **100**, 9 doğru = **10**, 8 doğru = **5**, 7 ve altı = **0**. (Admin panelinden mod bazında ayarlanır.)
- **Reklamla devam:** Yanlışta bir kez, ödüllü reklam hazırsa teklif edilir. Reklam yoksa teklif gösterilmez.
  - Devam edilince yanlış bilinen sıranın yerine **kolay** havuzdan **yeni bir soru** gelir (aynı soru tekrarlanmaz); sonraki sorular normal zorluk sırasıyla sürer. Örnek: 7. soruda yanlış → yeni 7. soru kolay, 8–10 zor.
- Bağlantı kopması: maç **iptal**, puan değişmez (bireysel).
- Bilerek çıkış (pes): o ana kadarki doğru sayısına göre puan hesaplanır.

## 6. Rakipli mod kuralları

- **10 sorunun hepsi** oynanır; yanlış cevapta elenme yoktur.
- **Kazanan:** en çok doğru cevabı olan.
- **Puanlar** (mod bazında admin ayarlı; başlangıç değerleri): kazanan **10**, kaybeden **0**, **bonus +5** (aşağıdaki eşik).
- **Bonus eşiği:** **8 ve üzeri** doğru yapana ekstra puan (K-51, kullanıcı kararı). Eşik değeri yine de admin panelinden ayarlanabilir parametredir.
- **Beraberlik:** Eşik (8) ve üzerinde doğru varsa iki oyuncu da **doğru sayısı × katsayı** (katsayı panelden ayarlanır) kadar puan alır; eşik altında beraberlikte puan yoktur.
- **Maç sonu 2x:** Puan kazanan oyuncuya (bonus dahil toplam > 0) ödüllü reklamla **puanını 2'ye katlama** teklif edilir. Katlanan puan sıralamaya yansır. Kaybedene/0 puanlıya teklif yoktur.
- Arkadaş ve bot maçlarının puanı da sıralamaya girer (K-22).

## 7. Rastgele eşleşme

1. Oyuncu mod ve alt dalı seçer, "Rastgele rakip" der.
2. Kuyruk anahtarı **mod + alt dal**. Aynı seçimi yapan başka aktif oyuncu varsa **anında** eşleşir (15 sn'yi beklemez).
3. Arama ekranı **15 sn** sayar; kullanıcı aramayı iptal edebilir.
4. Bulunamazsa: **"Bota karşı oyna"**, **"Bireysel oyna"**, **"Tekrar ara"** seçenekleri çıkar. Otomatik bot eşleşmesi **yoktur**.
5. Eşleşme bulununca **iki oyuncunun da hazır olması** beklenir; 10 sn içinde gelmeyen olursa maç iptal (puan değişmez). Sonra **5'ten geri sayım** başlar.

## 8. Bot

- Bot açıkça **"Bot" etiketiyle** görünür (isim + avatar var).
- Bot profilleri admin panelinden tek tek tanımlanır (isim, avatar, güç seviyesi, geçmiş).
- Davranış: her botun **güç seviyesi** vardır (zayıf/orta/güçlü). Doğruluk oranı **zorluğa göre** değişir (kolayda yüksek, zorda düşük) ve her maçta küçük rastgele sapma eklenir. Cevap süresi 3–12 sn aralığında rastgele. Bot "hep doğru" ya da "hep yanlış" bilmez. Ayrıntılar yapım aşamasında (faz 4) ayarlanır.
- Bot maçı rakipli kurallarla oynanır; bağlantı kopması insan maçıyla aynı sonuçlanır (kopan kaybeder). Bot bağlantı koparmaz.

## 9. Arkadaşla oyun

- Oyuncu mod ve alt dalı seçer, "Arkadaşla" der; **oda** kurulur. **Davet bağlantısı + kısa oda kodu** paylaşılır. "Koda katıl" girişi de vardır.
- Oda **5 dk** geçerlidir, **tek rakip** girebilir, doluysa ikinci kişi "Oda dolu" görür. Kurucu çıkarsa oda kapanır. Kendi bağlantısına tıklamak bir şey yapmaz.
- Davet eden mod/alt dalı seçer, katılan sadece onaylar.
- Bağlantı: uygulama yüklüyse doğrudan odaya (Android App Links). Yüklü değilse Play Store'a yönlendirir; kurulum ve kayıttan sonra aynı bağlantı tekrar açılınca odaya gider. Kayıt yapmamışsa önce kayıt ekranına yönlendirir (Play Install Referrer ile davet kodu taşınır). Bunun için bir **alan adı** gerekir (faz 4).
- Arkadaş iki taraf da geldiğinde **5'ten geri sayım** başlar.
- Maç bitince **rövanş** istenebilir (aynı kategori, aynı rakip, karşı taraf onaylarsa yeni maç). Rastgele rakipte "yeni rakip bul" butonu vardır.

## 10. Jokerler

| Joker | Etkisi |
|---|---|
| Yarı yarıya | 4 şıktan **2 yanlış şık** elenir, 2 şık kalır |
| Çift cevap | İki şık işaretleme hakkı; biri doğruysa doğru sayılır |
| İpucu | Admin ayarlı olasılıkla (başlangıç **%75**) doğru şıkkı gösterir, kalan olasılıkla yanlış bir şıkkı gösterir |

- **Soru başına en fazla bir joker.** Ayrıca **her joker türü bir maçta bir kez** kullanılabilir (K-52); yani bir maçta en çok 3 joker (her türden 1) kullanılır.
- Rakip joker kullandığını **görmez**. Zamanla ilgili joker yoktur.
- Yeni hesaba **her jokerden 5 adet** hediye. Reklamla (günlük sınır yok, işlem kimliğiyle doğrulanır) veya mağazadan **5 hediye puanı** karşılığı sınırsız alınır.
- Jokerlerim ekranı: sayılar + "reklam izle, joker kazan" butonu (ana sayfa joker çipinden ve profilden açılır).
- Başlangıç stok sayısı ve ipucu olasılığı yayın öncesi yeniden düzenlenebilir.

## 11. Bağlantı kopması ve çıkış

- **Tespit:** Uygulama arka plana atılınca Flutter yaşam döngüsü sinyali sunucuya anında gider. Ağ kopmasında SignalR keepalive süresi kadar gecikme olabilir (kısa tutulur).
- **Soru açıkken** kopan oyuncunun soru süresi **durmaz**; döndüğünde kalan süresi neyse o kadar vardır. Rakip en fazla o sorunun kalan süresi kadar bekler.
- **Sorular arasında / sonuç ekranında / süre bittikten sonra** kopma olursa maç **duraklar** ve kopan oyuncunun **30 sn'lik maç başı ortak süre havuzu** azalır. Her dönüşte havuzdan kalanla devam edilir; havuz 0 olursa kopan **kaybeder** (rakipli modda rakip kazanır).
- İki oyuncu birden koparsa maç iptal, puan yok.
- Bireysel modda kopma: maç iptal, puan yok.
- **Pes:** Geri tuşunda onay diyaloğu ("Çıkarsan maçı kaybedersin"). Rakipli: anında kayıp. Bireysel: o ana kadarki doğru sayısına göre puan.
- İkinci cihazdan giriş eski oturumu kapatır; maç sırasındaysa kopma sayılır.
- Sunucu yeniden başlarsa süren maçlar iptal edilir, puan değişmez (K-43).

## 12. Maç sonu

- **Bireysel:** elenme/bitiş özeti, kazanılan puan, (varsa) reklamla devam teklifi, "tekrar oyna".
- **Rakipli:** Kazandın/Kaybettin/Berabere, doğru sayıları, puan ve bonus, "Puanı 2'ye katla", soru inceleme listesi (açıklama + **soruyu bildir** butonu), "Tekrar oyna/Rövanş", "Ana sayfa".
- Soru bildirme nedenleri: yanlış cevap, belirsiz soru, yazım hatası, uygunsuz içerik. Bildirim maç sonunda yapılır (oyun akarken buton yok).
