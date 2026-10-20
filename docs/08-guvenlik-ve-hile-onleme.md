# 08 — Güvenlik, Hile Önleme ve Veri

> Kararlar K-08, K-35 … K-38, K-46, K-47 ile bağlantılıdır.

## 1. Sunucu otoritesi

- İstemciye yalnızca soru metni ve 4 şık gider; **doğru cevap gitmez**.
- Cevap süresini **sunucu** kendi saatiyle ölçer (istemci "şu kadar sn'de cevapladım" diyemez). ~500 ms ağ toleransı.
- İlk dokunuş kesin cevaptır; ikinci cevap kabul edilmez.
- Soru bitiş zamanı mutlak (UTC) gönderilir; cihaz saati yanlış olsa da hile/avantaj oluşmaz.
- Ödül, puan, joker, hediye puanı değişimleri **yalnızca sunucuda** ve defter kaydıyla yapılır.

## 2. Kimlik ve oturum

- Firebase kimlik jetonu her istekte doğrulanır (SignalR'da bağlantı kurulurken); banlı kullanıcı Redis önbelleğinden anında reddedilir.
- **Tek aktif oturum:** ikinci cihaz girişi eskisini kapatır (maç sırasındaysa kopma sayılır).
- Giriş yöntemleri: Google, e-posta (Apple iOS ile gelir). Sağlayıcı bağlama/çıkarma Firebase üzerinden; **en az bir yöntem bağlı kalır**. Aynı e-posta ile oluşturulmuş hesaplar tek hesaptır. E-posta ile kayıtta doğrulama zorunlu.
- **Kullanıcı adı:** benzersiz, değiştirilemez, 3–16 karakter, küçük harf/rakam/alt çizgi. **Görünen ad** değiştirilebilir. İkisinde de **küfür/argo filtresi**: Türkçe yasaklı kelime listesi + harf değişimi yakalama (a/@, i/1, o/0 …); liste admin panelinden düzenlenir. Admin gerekçeyle kullanıcı adını zorla değiştirebilir.
- Profil resmi yüklenmediği için görsel denetim sorunu yoktur (hazır avatar).

## 3. Hız sınırı ve kötüye kullanım

- ASP.NET hız sınırlayıcı: kullanıcı/IP başına; giriş ve kayıt uçlarında daha sıkı.
- SignalR metot başına sınır (cevap, joker, kuyruk).
- Destek: günde en fazla 5 yeni talep.
- Ödüllü reklam: işlem kimliği benzersiz (`transaction_id`); aynı reklam iki kez ödül vermez. Günlük sınır yok (kullanıcı kararı).

## 4. Cihaz ve çoklu hesap

- **Cihaz kimliği loglanır** (`user_devices`). Aynı cihazdan **3'ten fazla hesap** açılırsa panelde işaretlenir; katı sınır konmaz (aile bireyleri aynı telefonu kullanabilir).
- **Play Integrity:** kayıtta ve mağaza satın almasında **bayrak** olarak kontrol edilir; ilk sürümde engellemez, panelde "doğrulanamadı" etiketi görünür.

## 5. Hile göstergeleri (turnuva incelemesi)

Admin elle inceler (05): dönemdeki maç sayısı ve galibiyet oranı · aynı rakiple tekrar eden maçlar (arkadaş maçı kasma) · aynı cihazdaki hesaplar · ortalama cevap süresi ve 1 sn altı cevap sayısı · doğru oranı ve zorluk dağılımı · son saatlerde ani puan sıçraması · reklamla 2x oranı · kopma örüntüsü · son 50 maç ayrıntısı. **Otomatik ceza yok.** Bilinen kabul edilmiş durum: arkadaş maçı ve bot maçları puanı sıralamaya yazdığı için puan kasma mümkündür; ödül dağıtımı öncesi elle inceleme bu yüzden önemlidir. Cevabı internetten aratma teknik olarak tamamen engellenemez; zaman avantajı kopma kuralıyla kapatılmıştır.

## 6. Admin güvenliği

- Ayrı admin-api, ayrı kimlik (ASP.NET Identity), **e-posta + parola + TOTP**; rol alanı baştan (tek rol "Yönetici").
- Her admin işlemi `audit_logs`'a yazılır (kim, ne, ne zaman, önce/sonra).
- Panel Cloudflare üzerinden yayınlanır; istenirse IP kısıtlaması eklenir.

## 7. Gizli bilgiler

- Anahtar, parola, servis hesabı dosyaları, kupon kodları **repoya, loga ve sohbete yazılmaz**. Railway ortam değişkenleri + yerelde `.env` (git'e girmez), örnek için `.env.example`.
- Kupon kodları veritabanında **AES-GCM** ile şifreli; anahtar ortam değişkeninde. Kod gösterimi ve kopyalama loglanır.

## 8. Veri ve KVKK

- **Toplanan veri en az tutulur:** kullanıcı adı, görünen ad, e-posta (Firebase), doğum yılı, avatar, maç/cevap kayıtları, cihaz kimliği.
- **Saklama süreleri:** ayrıntılı oyun/cevap logları **90 gün**; maç özetleri **1 yıl**; admin denetim kayıtları **2 yıl**. Temizlik arka plan işiyle.
- **Hesap silme:** uygulama içinden, yeniden doğrulama + uyarı, **anında**. Kişisel alanlar anonimleştirilir; geçmiş maçlarda rakibe "Silinmiş kullanıcı" görünür; sıralamadan çıkar; hediye puanı, jokerler ve ödül kodları silinir (uyarı ekranında yazar).
- **Yaş:** kayıtta doğum yılı; minimum yaş 13; ödül tarafında yaş sınırı yasal araştırmadan sonra (10).
- Gizlilik politikası, kullanım koşulları ve **turnuva/ödül şartları** metinleri hazırlanır (taslakları Claude hazırlar; hukuki kontrol kullanıcı tarafından bir uzmana yaptırılır).
- AdMob için Google'ın kullanıcı onay mesajı (UMP) ve Play Store **veri güvenliği formu** yayın öncesi yapılır (09, faz 9).

## 9. Ban ve uyarı

- Banlı kullanıcı giriş yapınca nedeni ve destekle iletişim yolunu görür.
- Uyarı mesajları uygulama içi bildirim kutusuna düşer ve push ile iletilir.
- Turnuvadan men, ban'dan ayrı bir işlemdir (kullanıcı o dönem için dışlanır, hesap açık kalır).

## 10. Geliştirme kimliği (DevAuth) — yalnızca yerel test

Firebase hesabı olmadan elle/otomatik test için API, `Authorization: Dev uid=<uid>;email=<e-posta>;verified=true|false;provider=password|google.com` başlığını kabul eder. **Yalnızca** ortam `Development` **ve** `Auth:DevBypass=true` iken etkindir (`appsettings.Development.json`); üretimde şema hiç kaydedilmez. Açıkken başlangıçta uyarı loglanır. Testler (`UsersApiTests`) bu yolu kullanır. Gerçek Firebase jetonları `Firebase:ProjectId` ayarlandığında doğrulanır (`https://securetoken.google.com/<projectId>`).
