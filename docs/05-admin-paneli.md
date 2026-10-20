# 05 — Admin Paneli

> Kararlar K-11, K-17, K-30, K-39, K-41, K-45 ile bağlantılıdır.
> **Teknoloji:** React (Vite + TypeScript + shadcn/ui + TanStack Table/Query), Cloudflare Pages; ayrı **admin-api** (.NET 10, aynı solution). Soru girişi en çok zaman harcanacak yer olduğu için **akıcılık ve estetik önceliklidir** (kullanıcı özellikle istedi).
> **Giriş:** e-posta + parola + 2 adımlı doğrulama (TOTP); her işlem **denetim kaydına** düşer.

## 1. Dashboard

Toplam ve günlük aktif kullanıcı · yeni kayıt · **canlı oyuncu sayısı** · günlük maç sayısı · mod/kategori popülerliği · **saat bazında yoğunluk** · reklam izleme sayısı (uygulamadan sayılan) · dağıtılan ve harcanan hediye puanı · bekleyen destek talebi · bildirilen soru sayısı · kupon stok durumu. (Eklenip çıkarılabilir.)

## 2. Soru yönetimi

- **Tekli ekleme (akıcı form):** kategori (mod → alt dal), soru metni, A–D şıkları, doğru şık, zorluk (kolay/orta/zor), opsiyonel açıklama. Klavye ile hızlı geçiş, **"Kaydet ve yeni ekle"**, kategori ve zorluğu hatırlama, taslak olarak kaydetme.
- **Toplu yükleme:** CSV/Excel; yükleme önizlemesi, satır satır hata raporu, **tekrar eden soru uyarısı**. (Sık kullanılması beklenmiyor.)
- **Liste/filtre/arama:** kategoriye, zorluğa, duruma göre; düzenleme, yayınla/taslağa al/arşivle (silme yerine arşiv).
- **Soru istatistiği:** her sorunun gösterilme ve doğru sayısı → "doğru oranı" sütunu; çok kolay/zor çıkanlar işaretlenir. (Basit sayaç, sistemi zorlamaz.)
- **Kategori yönetimi:** mod altında alt dal ekle/düzenle, görsel/ikon + renk yükle (R2), **yayınla/gizle**. Soru sayıları (kolay/orta/zor) görünür; yeterlilik uyarısı veya yayın engeli **yoktur** (yayın kararı admindedir). Hedef öneri: kategori başına 40 kolay / 30 orta / 30 zor.
- **Bildirilen sorular:** aynı soruya gelen bildirimler tek satırda birleşir, sayaç artar; eşik (ör. 5) aşılınca kırmızı rozet; "en çok bildirilen" listesi. Otomatik gizleme yok.
- **Uyarılar:** soru bankası bir kategoride tükenirse (kullanıcı tüm soruları görmüşse) panele uyarı düşer.

## 3. Üye kontrolü

Kullanıcı bilgileri ve cihazlar · son maçlar ve puan geçmişi · hediye puanı / joker ekleme veya çıkarma (**defter kaydı ve gerekçe zorunlu**) · uyarı mesajı gönderme (uygulama içi + push) · **ban / ban kaldırma** · görünen ad veya avatar sıfırlama · gerekçeli **kullanıcı adı değiştirme** (kullanıcıya değiştirilemez, admin gerekirse zorlar) · turnuvadan men · Play Integrity bayrağı, aynı cihazdaki hesaplar · kupon alım/itiraz sayıları ve oranı · tüm logları görüntüleme.

## 4. Destek

- **Gelen kutusu:** kategori (hesap, ödül/kupon sorunu, hata, öneri, soru önerisi) ve duruma (Açık/Yanıtlandı/Kapalı) göre filtre; kullanıcı profiline hızlı geçiş.
- Mesajlaşma şeklinde yanıt; kapalı talebe kullanıcı yazarsa talep yeniden açılır. Yanıt yazınca kullanıcıya push + bildirim kutusu.
- **Soru önerisi:** tek tıkla **"Soru olarak ekle"** → öneri taslak soru formuna aktarılır, admin düzenleyip yayınlar. Yayınlanan öneri sahibine **1 joker hediye** edilir *(bu hediye önerim olarak onaylandı; miktar/tür yapım aşamasında kesinleşir)*.
- Kullanıcı başına günde en fazla 5 yeni talep (spam önlemi). Ekler jpg/png, en çok 3 adet, her biri en çok 5 MB (R2).

## 5. Turnuva yönetimi

- Dönem listesi ve durumu (açık / incelemede / kapandı / dağıtıldı).
- **Ödül tablosu:** sıralamaya göre miktar (ör. 1. = 100…); panelde "bu tabloyla aylık maksimum maliyet" göstergesi (1 puan = 0,05 TL).
- **İnceleme ekranı** (bitişten 1 saat önce e-posta + panel rozeti): ödül kapsamındaki herkes (eşitlik dahil) ve yedekler. Göstergeler: dönemdeki maç sayısı ve galibiyet oranı · aynı rakiple tekrar eden maç sayısı · aynı cihazdan açılan hesaplar · ortalama cevap süresi ve 1 sn altı cevap sayısı · doğru oranı ve zorluk dağılımı · puan eğrisi (son saatlerde ani sıçrama) · reklamla 2x kullanım oranı · kopma örüntüsü · son 50 maç (tek tek açılır, soru-cevap detayıyla). **Otomatik ceza yok**, ham veri + bayraklar.
- **Men:** kullanıcı o turnuvadan men edilir (veya banlanır); alttaki sıra yükselir; kullanıcıya bildirim gider, neden alanı opsiyoneldir.
- İnceleme yetişmezse ödüller otomatik dağıtılır ve **geri alınmaz** (kullanıcı kararı).
- Kullanıcıya gösterilen sıralama: puanı 0 olmayan ilk 50 + kendi sırası.

## 6. Mağaza yönetimi

- Ürün ekle/düzenle: kupon, joker paketi, avatar, çerçeve (ad, görsel, fiyat, aktif/pasif).
- **Kupon stoğu:** CSV ile kod yükleme (şifreli saklanır), stok sayısı, düşük stok uyarısı, "bozuk/kullanılmış" işaretleme.
- **Sorun bildirimleri:** kullanıcının "kodda sorun var" talepleri; yeni kod verme (ayda 1 değişim hakkı kuralı), itiraz oranı.
- Avatar ve çerçeveleri admin **tek tek yükler** (5 ücretsiz başlangıç avatarı + 10–15 ücretli).

## 7. Bot profilleri

Bot ekle/düzenle: isim, avatar, çerçeve, **güç seviyesi** (zayıf/orta/güçlü), cevap hız aralığı, hazırlanmış geçmiş. Bot profilleri gerçek kullanıcı adlarıyla çakışmaz; ekranda her zaman "Bot" etiketi vardır.

## 8. Ayarlar

- **Puan kuralları (mod bazında):** bireysel puan merdiveni, kazanma/kaybetme puanı, bonus eşiği ve miktarı, beraberlik katsayısı.
- İpucu jokeri olasılığı (başlangıç %75) · zorluk dağılımı (4/3/3) · yeni hesap joker hediyesi (5'er) · joker mağaza fiyatı (5) · 2x katlama açık/kapalı.
- **Min desteklenen sürüm** ve **bakım modu**.
- **Küfür/argo listesi** (kullanıcı adı ve görünen ad filtresi, harf değişimi yakalama).
- **Genel duyuru:** tüm kullanıcılara (veya min sürüm filtreli) push + bildirim kutusu duyurusu (basit form).

## 9. Denetim

Her admin işlemi (kim, ne, ne zaman, önce/sonra) `audit_logs` tablosuna yazılır; panelden görüntülenir. Rol alanı baştan vardır (tek rol "Yönetici"); ileride editör/destek rolleri eklenebilir.
