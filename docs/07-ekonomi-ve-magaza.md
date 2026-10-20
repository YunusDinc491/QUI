# 07 — Turnuva, Ekonomi ve Mağaza

> Kararlar K-21, K-24, K-28 … K-34 ile bağlantılıdır.

## 1. Puan türleri

| Puan | Ne işe yarar | Sıfırlanır mı |
|---|---|---|
| **Toplam puan** | Profilde gösterge; başka işlevi yok | Hayır |
| **Dönemlik puan** | Turnuva sıralaması | Evet, her dönem |
| **Hediye puanı** | Mağazada harcanır; turnuva ödülü | Hayır (süresiz), harcanır |

Maçtan kazanılan puan (bonus ve 2x katlama dahil) hem **toplam puana** hem **dönemlik puana** yazılır. Hediye puanı yalnızca turnuva ödülü, admin düzeltmesi ve iadeyle artar.

## 2. Turnuva

- **Dönem:** 48 saatlik arka arkaya dönemler, Türkiye saati; ilk dönem yayın günü 00:00'da başlar, her 2 günde bir 00:00'da kapanır.
- **Puanın yazılması:** Maçın puanı **maç bittiği andaki** döneme yazılır. Kapanışta 30 sn'lik yazma kilidi vardır (dağıtım sırasında puan değişmesin).
- **Tek genel sıralama** (mod/kategori bazlı ayrı tablo yok). Kullanıcıya puanı 0 olmayan **ilk 50** gösterilir; kullanıcı kendi sırasını da görür (ör. 150.). 51+ aralığı listelenmez.
- **Eşitlik:** Eşit puanlı herkes aynı sırayı paylaşır ve o sıranın ödülünü alır (eşitlik elemesi yok). Uzun vadede rekabeti artıracak düzenleme sonra yapılacak.
- **Ödül:** Sıralamaya göre **hediye puanı**; miktarlar admin panelinden ayarlanır. Örnek (taslak, kesin değil): 1. 100, 2. 80, 3. 60, 4. 50, 5. 40, 6. 30, 7. 25, 8. 20, 9. 15, 10. 10. Kullanıcının tahmini: bir turda en fazla ~100 puan.
- **İnceleme:** Bitişten 1 saat önce e-posta + panel rozeti hatırlatması; admin ilk kişileri inceler (05). Hileli kullanıcı o turnuvadan men edilir ya da banlanır; alttaki sıra yükselir.
- **Dağıtım:** Zamanlanmış iş (idempotent, kilitli, "dağıtıldı" bayrağı) sıralamaya göre ödülleri **defter kaydıyla** verir, kazananlara push gönderir, sonra tabloyu sıfırlar. İnceleme yetişmezse otomatik dağıtılır, **geri alınmaz**.
- **Turnuva ekranı:** bitişe geri sayım, ödül tablosu, ilk 50, kendi sıran; ilk açılışta önceki turun ilk 10'u pop-up.
- **Etkiler:** Kullanıcı tahmini: bir kişi yüksek değerli ödül için ~2 hafta ciddi oynayıp reklam izlemeli. Hedef kitle: ciddi çabalayan 5–6 kişi, orta 10–15, hafif oynayan 20–30 kişi.

## 3. Ekonomi

- **Oran:** 1 hediye puanı = **0,05 TL** (kabul edildi). Dolayısıyla 20.000 puan = 1.000 TL, 1.000 puan = 50 TL, 300 puan = 15 TL. Kullanıcının ilk verdiği tahmini rakamlar (300 ≈ 50 TL, 1.000 ≈ 300 TL, 10.000 ≈ 200 TL) birbiriyle tutarsız olduğu için bu sabit oranla yeniden yazılacaktır; **ürün fiyat listesi ürün eklenirken** belirlenir.
- **Joker:** 5 hediye puanı (sınırsız satın alınır). Yani 1. sıra 100 puanla 20 joker alabilir.
- **Bütçe göstergesi:** Panelde "bu ödül tablosuyla aylık maksimum maliyet" gösterilir. Örnek: yukarıdaki taslak tablo bir turda 430 puan, ayda 15 turda 6.450 puan → 0,05 TL ile ayda ~320 TL.
- Ödül maliyeti ile reklam geliri arasındaki denge yayın sonrası gerçek verilerle gözden geçirilir.

## 4. Cüzdan (defter)

- `wallet_ledger` **sadece eklenir**; her hareket (turnuva ödülü, satın alma, iade, admin düzeltmesi) ayrı satırdır. Bakiye satırların toplamıdır. `idempotency_key` benzersizdir (aynı işlem iki kez uygulanmaz).
- Jokerler için `joker_ledger` aynı mantıkla çalışır (hediye, reklam, satın alma, kullanım, admin).
- Profilde **"Puan geçmişi"** ekranı defterden okur. Bakiye eksiye düşebilir yalnızca admin düzeltmesiyle (ör. hile iadesi).

## 5. Mağaza

| Tür | Fiyat | Stok | Teslim |
|---|---|---|---|
| Kupon / hediye kartı | hediye puanı | kod listesi, tükenebilir | "Ödüllerim"de kod görünür |
| Joker paketi | hediye puanı | sınırsız | joker bakiyesine eklenir |
| Avatar | hediye puanı | sınırsız | kullanıcıya açılır (**bir kez**) |
| Avatar çerçevesi | hediye puanı | sınırsız | kullanıcıya açılır (**bir kez**) |

- **Limit yok:** Kupon/joker için günlük/haftalık satın alma sınırı yoktur. 3 ay biriktirip tek seferde çok kupon alabilir (kullanıcı kararı).
- **Avatar:** 5 ücretsiz başlangıç avatarı, 10–15 ücretli; admin tek tek yükler. Kendi fotoğrafı yüklenemez. **Çerçeve** seçilince rakip maçta görür.
- **Satın alma işlemi:** tek işlem içinde (bakiye kontrolü → stoktan kod ayırma `FOR UPDATE SKIP LOCKED` → defter kaydı → kodun kullanıcıya bağlanması). Çift tıklama/ağ tekrarına karşı işlem kimliği.
- **Kupon kodları:** şifreli saklanır (AES-GCM; anahtar ortam değişkeninde), panelden CSV ile yüklenir, satın alınca stoktan **otomatik** düşer, "Ödüllerim"de görünür. Kod tek kullanımlıktır; kullanıcı istediği zaman kullanabilir.
- **Kod gösterimi:** iki aşamalı — "Kodu göster" butonu uyarı verir ("Gösterdikten sonra değişim yapılmaz, sorun yaşarsan buradan bildir"). Gösterim ve kopyalama olayı loglanır.
- **"Kod çalışmıyor" iddiası — tedbirler:** (1) sorun bildirimi gösterimden sonra **48 saat** içinde yapılır ve hata mesajının **ekran görüntüsü** istenir (R2); (2) değişim vermeden önce admin kodu markanın bakiye sorgulama sayfasından (varsa) kontrol eder, zaten harcanmışsa talep reddedilir; (3) panelde kullanıcı başına "aldığı kod / bildirdiği sorun" oranı görünür, tekrar eden itirazcılar işaretlenir; (4) kullanım koşullarına "kod gösterildikten sonra iade yoktur, kanıtlanmış teknik hatada en fazla 1 değişim" yazılır; (5) kullanıcı başına **ayda en fazla 1 değişim**.
- Kuponları şimdilik **admin kendisi satın alıp** panelden yükler (sağlayıcı API'si yok, K-33).

## 6. Yasal ve vergi

Gerçek ödül, AdMob geliri ve yaş sınırı konuları yayın öncesi araştırılacaktır; liste ve muhasebeci/avukat soruları 10. dosyadadır.
