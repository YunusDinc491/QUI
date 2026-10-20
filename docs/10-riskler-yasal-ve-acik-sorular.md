# 10 — Riskler, Yasal Araştırma ve Açık Sorular

## 1. Açık sorular (kullanıcıdan netleşmesi gerekenler)

| No | Soru | Durum / geçici varsayım |
|---|---|---|
| A-01 | Rakipli modda bonus eşiği: ">8" mi, "≥8" mi? | **ÇÖZÜLDÜ (2026-10-20):** 8 ve üzeri (K-51) |
| A-02 | Maç başına toplam joker limiti var mı (öneri: 2)? | **ÇÖZÜLDÜ (2026-10-20):** her joker türü maçta bir kez kullanılabilir (K-52) |
| A-03 | Bireysel puan tablosu "doğru sayısı" esasıyla yazıldı (8 doğru=5, 9=10, 10=100). Özgün anlatım "X. soruda elenirse" şeklindeydi. Kullanıcı yorumu "doğru anlamışsın" diye onayladı. | Onaylı; sadece not |
| A-04 | Turnuva ödül tablosu rakamları ve mağaza ürün fiyatları | 1 puan = 0,05 TL sabit; tablo ve fiyatlar faz 5–6'da belirlenecek (taslak 07'de) |
| A-05 | Eşitlikte "uzun vadede rekabet yaratacak düzenleme" | Şimdilik eşit puanlı herkes ödül alır; düzenleme sonraya |
| A-06 | Hangi markalardan kupon alınacak; ürün listesi | Elle alınıp yüklenecek; liste faz 6'da |
| A-07 | İnceleme hatırlatması hangi e-posta adresine gidecek | Faz 5'te admin hesabı oluşturulurken |
| A-08 | Bot ayrıntıları (güç seviyeleri, doğruluk eğrileri, hız aralıkları) | Faz 4'te ayarlanır |
| A-09 | Joker başlangıç sayısı, ipucu olasılığı, yeni hesap hediyesi | Yayın öncesi yeniden düzenlenecek (kullanıcı notu) |
| A-10 | Fan modunda takım/dizi logosu kullanımı (telif) | Görsel kararı tasarım aşamasında |
| A-11 | Ödüllerde yaş sınırı (18+?) | Yasal araştırmadan sonra |
| A-12 | Alan adı seçimi | Büyük olasılıkla **senbilirsin.com** (kullanıcı, 2026-10-20; henüz alınmadı, kesinleşince alınır). Faz 4'te |
| A-13 | Görsel tasarım konuşmasının zamanı | **ÇÖZÜLDÜ (2026-10-20):** tasarım en sona; önce çalışan ham iskelet (K-53) |
| A-14 | Bot maçında bağlantı kopmasının insan maçıyla aynı sonuçlanması (kayıp) kararı, bot **artık gizli olmadığı** için gözden geçirilmeli mi? | Kayıtlı karar geçerli (K-25 uyarınca insan maçıyla aynı); yalnızca bilgi notu |
| A-16 | Android uygulama kimliği (paket adı) | **ÇÖZÜLDÜ (2026-10-20):** `com.senbilirsin.app` (K-56). Yayından sonra değiştirilemez |
| A-15 | Telefon/emülatör: test için gerçek Android telefon var mı | **ÇÖZÜLDÜ (2026-10-20):** telefon yok, emülatör kullanılacak (K-54) |

## 2. Riskler

| Risk | Etki | Önlem |
|---|---|---|
| **Gerçek ödül** (hediye kartı) — yarışma/ödül mevzuatı, vergi, tüketici hukuku, **Google Play "gerçek para/ödüllü oyun" politikası** | Yayın reddi veya yasal sorun | Yayın öncesi araştırma (aşağıda); uzman danışmanlığı |
| **AdMob geliri vergisi** | Yanlış mükellefiyet | Kaynaklar çelişkili; SMMM ile bir kerelik danışmanlık (aşağıda). İlk gelirden önce karar |
| **Puan kasma** (arkadaş + bot maçları sıralamada) | Ödül maliyeti artar / haksızlık | Kabul edilmiş risk (K-22); ödül dağıtımı öncesi elle inceleme, cihaz bayrakları, men |
| **Çoklu hesap** | Ödül sömürüsü | Cihaz kimliği, aynı cihazda 3+ hesap bayrağı, Play Integrity bayrağı |
| **Cevap aratma** | Adaletsizlik | Teknik olarak tamamen engellenemez; zaman avantajı kopma kuralıyla kapatıldı |
| **Kupon "çalışmıyor" iddiaları** | Maddi kayıp | 07'deki tedbirler (48 sa, ekran görüntüsü, ayda 1 değişim, itiraz oranı) |
| **Deploy sırasında maç iptali** | Kullanıcı memnuniyetsizliği | Bakım modu + az oyuncu saati; maç iptalinde puan değişmez |
| **Tek instance** | Kesinti = tüm oyun durur | Uptime izleme, yedek, ileride ölçekleme |
| **Railway'de yönetilmeyen DB** | Veri kaybı | Günlük `pg_dump` → R2, geri yükleme denemesi (faz 9) |
| **Flutter SignalR paketi bakımı** | Gerçek zamanlı özelliğin bozulması | Faz 0 prototipi; gerekirse düz WebSocket sarmalayıcı |
| **Ağ kopmasının geç fark edilmesi** | Haksız kopma kararı | Kısa keepalive, arka plan sinyali |
| **18 yaş altı / KVKK** | Yasal sorumluluk | Doğum yılı, aydınlatma metni, minimum yaş 13, silme, saklama süreleri |
| **Reklam doluluğu (no-fill)** | "Reklamla devam" vaadi yerine getirilemez | Reklam hazırsa teklif göster, değilse gösterme |
| **Telif** (Fan modu logoları, kategori adları) | Mağaza/marka sorunu | Tasarım aşamasında karar |
| **Google Play yeni hesap kapalı test şartı** | Yayın gecikmesi | Hesabı erken aç, şartı güncel kaynaktan teyit et |
| **Hobby plan sınırları** | Kaynak yetersizliği | İzle, gerekirse yükselt |
| **Flutter "Built-in Kotlin" geçişi:** `firebase_auth` ve `firebase_core` Kotlin Gradle Eklentisi (KGP) uyguluyor; Flutter ileride bunu reddedecek (derleme uyarısı, 2026-10-20) | İleride Flutter/AGP güncellemesinde Android derlemesi kırılabilir | Paket güncellemeleri izlenir; Flutter sürümü bilerek yükseltilir, güncellemeden önce derleme denenir |

## 3. Yasal / vergi araştırma listesi (yayın öncesi)

1. Google Play "gerçek para ödüllü oyun ve yarışma" politikası: ödüllü bilgi yarışmasına izin, gerekli beyanlar, ülke kısıtları.
2. Türkiye'de ödüllü yarışma/hediye dağıtımı: gerekli izin/bildirim var mı, vergilendirme (kullanıcıya verilen kupon gelir mi), bilgi yarışması "şans oyunu" sayılır mı.
3. 18 yaş altına ödül verme, ebeveyn rızası.
4. KVKK: aydınlatma metni, açık rıza, VERBİS yükümlülüğü, yurt dışı aktarım (Firebase/Cloudflare/Railway).
5. Tüketici hukuku: ödülün şartları, hile durumunda men/ödül kaybı hükümleri, kullanım koşullarına yazılması.
6. AdMob/AdSense geliri için vergi statüsü (aşağıda).

## 4. Muhasebeci (SMMM) / avukat soru listesi

- Şahıs işletmesi mi, 20/B stopaj rejimi mi, sermaye şirketi mi uygun? Hangi gelir seviyesinde geçiş?
- AdMob geliri yazılım/mobil uygulama istisnasına giriyor mu (kaynaklar çelişkili)? Hizmet ihracatı KDV istisnası uygulanır mı?
- Ödül olarak verilen hediye kartlarının faturası, KDV ve stopaj durumu.
- Ödüllü yarışma için ek izin/bildirim gerekir mi?
- 18 yaş altı kullanıcıya ödül verme.
- AdMob ödeme profili ve vergi bilgisi formlarının doğru doldurulması.

## 5. Kaynaklar (vergi araştırması, 2026-10-20; hepsi vergi danışmanlığı blogları, resmî metin değil)

- [Google AdSense Gelirlerin Vergilendirilmesi (2026), Kadim Hukuk](https://kadimhukuk.com.tr/makale/google-adsense-gelirlerin-vergilendirilmesi/)
- [Google Adsense Reklam Gelirlerinin Vergilendirilmesi 2026, Tahancı Hukuk](https://www.tahanci.av.tr/google-adsense-reklam-gelirlerinin-vergilendirilmesi/)
- [Admob, Adsense, Youtube vb. Geliri Olan Şirketlerin Muhasebe İşlemleri, SMMM Armağan Yörük](https://armaganyoruk.com/admob-adsense-youtube-vb-geliri-olan-sirketlerin-muhasebe-islemleri/)
- [App Store ve Google Play Geliri: Türkiye'de Vergi Rehberi (2026), Remote Vergi](https://remotevergi.com/app-store-play-store-vergi/)
- [Mobile App Developer Vergi Rehberi 2026, Vergi Merkezi](https://vergimerkezi.com.tr/mobile-app-developer-vergi-rehberi/)
- [Google gelirlerinin vergilendirilmesi, Paraşüt](https://www.parasut.com/blog/google-gelirlerinin-vergilendirilmesi)

Araştırma özeti: AdMob geliri düzenli olduğu için vergi mükellefiyeti gerekiyor görünüyor; şirket kurmak zorunlu görünmüyor (şahıs işletmesi/20B stopaj rejimi gündemde). İstisna kapsamı, rakamlar ve 2026 değişiklikleri kaynaklar arasında çelişkili; kesin bilgi olarak kullanılmaz, gelir oluşmadan önce uzman görüşü alınır.
