# 06 — Mobil Ekran Akışı

> Sadece **mantıksal akış** (hangi ekran nereye gider, ne işe yarar). Görsel tasarım en sona bırakılmıştır (kullanıcı kararı). Dil: yalnızca Türkçe.

## 1. Açılış

Açılış ekranı → sürüm ve bakım kontrolü (gerekirse **"Güncelle"** veya **"Bakımdayız"** ekranı) → oturum varsa ana ekran; yoksa hoş geldin/giriş ekranı.

İlk kayıt: giriş yöntemi (Google / e-posta) → kullanıcı adı seç (anlık müsaitlik kontrolü; 3–16 karakter, küçük harf/rakam/alt çizgi, küfür filtresi) → görünen ad → avatar seç (5 ücretsiz) → doğum yılı → gizlilik politikası ve koşul onayı → **3 ekranlık "nasıl oynanır" turu** (atlanabilir, bir kez) → ana ekran. E-posta ile kayıtta e-posta doğrulaması zorunludur.

## 2. Alt menü (4 sekme)

1. **Oyna (ana sayfa):** üstte bakiye çipleri (hediye puanı, joker sayısı), zil ikonu (bildirim kutusu), **4 mod kartı**. Başka içerik yok.
2. **Turnuva:** bitişe geri sayım, ilk 50 (puanı 0 olmayan), kendi sıran, ödül tablosu, önceki turun kazananları. Kullanıcı bu sayfayı **ilk açtığında** önceki turun kazananları pop-up olarak gösterilir.
3. **Mağaza:** sekmeler **Kupon / Joker / Avatar / Çerçeve**, bakiye, ürün detayı, satın alma onay diyaloğu. **Ödüllerim** buradan açılır (kuponlar, kod gösterme).
4. **Profil:** avatar ve çerçeve (değiştirme), görünen ad, kullanıcı adı, istatistikler (oynanan maç, galibiyet, toplam puan), son 20 maç, puan geçmişi, **Jokerlerim**, **Ayarlar**.

Jokerlerim ekranına ana sayfadaki joker çipinden ve profilden gidilir; orada joker sayıları ve "reklam izle, joker kazan" butonu vardır.

## 3. Oyun akışı

Mod seç → alt dal seç (Genel'de yok; diğer modlarda başta **Karışık**) → rakip seçimi:

- **Bireysel:** 3 sn hazırlık → oyun.
- **Arkadaşla:** oda kur → davet bağlantısı + oda kodu paylaş (Android paylaşım menüsü) → arkadaş gelince 5'ten geri sayım → oyun. Ayrıca **"Koda katıl"** girişi.
- **Rastgele:** 15 sn arama ekranı (iptal edilebilir) → bulunursa "Rakip bulundu" + 5'ten geri sayım. Bulunamazsa: **Bota karşı oyna / Bireysel oyna / Tekrar ara**.

**Oyun ekranı:** üstte soru numarası ve geri sayım çubuğu, rakibin "cevapladı" ışığı, ortada soru, 4 şık (seçilen şık turuncu), altta joker çubuğu. Cevap sonrası sonuç görünümü (doğru cevap, senin ve rakibin cevabı) **3 sn** kalır, sıradaki soru gelir. Kopma durumunda üstte "Rakibin bağlantısı koptu, X sn bekleniyor" şeridi görünür. Geri tuşunda **"Çıkarsan maçı kaybedersin"** onayı.

**Maç sonu:**
- Bireysel: elenme/bitiş özeti, kazanılan puan, (hak varsa ve reklam hazırsa) **reklamla devam** teklifi, tekrar oyna.
- Rakipli: Kazandın/Kaybettin/Berabere, doğru sayıları, puan ve bonus, **"Puanı 2'ye katla"** (reklam; sadece puan > 0), soru inceleme listesi (açıklama + **soruyu bildir**), **Rövanş** (arkadaşta) / **Yeni rakip bul** (rastgelede), Ana sayfa.

## 4. Diğer ekranlar

- **Bildirim kutusu:** zilden açılır; okundu/okunmadı; destek yanıtı, uyarı, ödül, men, joker hediyesi, turnuva olayları.
- **Ayarlar:** giriş yöntemleri (bağla/çıkar; en az biri kalır), bildirim türleri (hesap güvenliği ve destek yanıtı kapatılamaz), ses ve titreşim, **Yardım**, gizlilik/koşullar, sürüm, çıkış, **hesabı sil**.
- **Yardım:** taleplerim listesi, yeni talep (kategori, konu, mesaj, en çok 3 ekran görüntüsü), talep detayı (mesajlaşma), soru önerisi formu.
- **Hesap silme:** yeniden doğrulama + uyarı (hediye puanı, jokerler, ödül kodları silinir) → anında silme.
- **Banlı kullanıcı:** giriş yapınca nedeni ve destekle iletişim yolunu görür.
- **Bağlantı yok:** "İnternet bağlantısı gerekli" ekranı (çevrimdışı mod yok).
- **Bildirim izni (Android 13+):** ilk maçtan sonra istenir.

## 5. Tasarım (en sona)

Renk, tipografi, animasyon, ikonlar, kart tasarımları, mod/alt dal görselleri, avatar ve çerçeve çizimleri, logo — **tüm bunlar akış ve mantık oturduktan sonra** konuşulacak. Marka/telif: Fan modunda takım/dizi logosu kullanımı telif riski taşır (10).
