# DNS Pilot + Zapret

> Uygulamanın görünen adı **DNS Pilot**. Dosya ve klasör adları (`DnsDegistirici`) eski adıyla kalıyor.

Sistem tepsisinde bekleyen küçük araç: tek tıkla DNS değiştirir ve zapret'i (DPI engeli aşma) başlatıp durdurur.

## Kullanım
- `bin\DnsDegistirici.exe`'yi çalıştır (yönetici izni ister).
- Tepsideki **D** ikonuna sağ tıkla: DNS seçimi, zapret aç/kapa, Ayarlar, Çıkış.
- Sol tık (ya da exe'yi yeniden açmak): **Ayarlar** penceresi. Giriş, DNS, Zapret ve Sistem sayfalarında tüm ayarlar var.

Tepsi simgesi uygulama logosudur; sağ alttaki nokta durumu gösterir: **yeşil** = şifreli DNS (DoH), **mor** = özel ama şifresiz, **gri** = ISS/modem DNS'i, **kırmızı** = bağlantı yok.
**Turuncu** nokta = zapret çalışıyor (zapret açılırken DNS zaten şifreliye geçer).

Logo: `src\icon\logo-*.png` (Figma'dan) → `src\icon\make-ico.ps1` → `src\app.ico`. Exe simgesi, pencere simgeleri ve tepsi bu dosyayı kullanır.

## DNS ile zapret farkı
- **DNS:** ISS'nin DNS yönlendirmesini aşar, DNS trafiğini şifreler.
- **Zapret:** Discord gibi DPI ile yapılan engelleri aşar. Sadece DNS değiştirmek bunlara yetmez.

Vodafone'da ölçülen (29.10.2026):
- discord.com için şifresiz DNS sorguları hangi sunucuya gidersen git düşürülüyor. Discord için **şifreli DNS şart**.
- Cloudflare ve Google'ın şifreli DNS'i engelli (zapret açıkken bile her seferinde geçmiyor). **AdGuard ve Quad9 geçiyor.**

Araç şifreli DNS'i açmadan önce bağlantıyı test eder, geçemezse şifresiz ayarlayıp uyarır.
**Zapret başlarken DNS şifresizse otomatik olarak AdGuard'a geçer** (Ayarlar → Zapret → "Başlarken şifreli DNS'e geç").
Başka bir sağlayıcı istersen Ayarlar → Zapret → "Tercih edilen şifreli DNS"ten seç.

## Zapret
- **Ayarlar → Zapret → En uygun stratejiyi bul:** her stratejiyi senin bağlantında dener (discord.com, gateway.discord.gg, cdn.discordapp.com'a sertifika doğrulamalı HTTPS isteği), en çok siteye erişen en hızlısını seçer. ~1 dk sürer, "Durdur" ile kesilebilir. Sonuçlar strateji listesinde görünür (★ = seçilen).
- **Ayarlar → Zapret → Strateji:** ISS'ne göre seç. Biri çalışmazsa diğerlerini dene.
- **Hangi siteler:** önerilen ayar "Sadece listedekiler". Liste: `bin\zapret\hostlist.txt`.
- **Ayarlar → Güncellemeler:** Windows Update düzeninde. "Güncellemeleri denetle" bol-van/zapret-win-bundle'daki son motoru kontrol eder, "Şimdi yükle" indirip kurar (site listeleri ve stratejiler korunur). Uygulamanın kendi güncellemeleri Microsoft Store üzerinden gelir. Otomatik denetim açıksa uygulama açılışından 1 dk sonra, günde en fazla bir kez denetlenir ve güncelleme varsa bildirim gösterilir.

Klasörler:
- `bin\zapret\motor\`: zapret motoru (güncellemede tümüyle değişir)
- `bin\zapret\hostlist.txt`, `excludelist.txt`, `stratejiler.txt`: senin ayarların
- `bin\profiles.txt`: DNS profilleri
- `bin\ayarlar.txt`: seçili strateji vb.

## Sorun olursa
- İnternet kesilirse menüden **Otomatik (modem / ISS)** seç.
- Zapret siteleri bozarsa **Zapret'i durdur** de ya da başka bir strateji dene.
- Uygulamadan çıkınca zapret de kapanır.

## Derleme
```
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Microsoft Store yayını
- Partner Center ürünü: **DNS Pilot** (EXE/MSI uygulaması). Erişilebilirlik, Özellikler, Yaş derecelendirmesi ve Türkçe Store sayfası tamamlandı.
- Kurucu: `powershell -ExecutionPolicy Bypass -File installer\build-setup.ps1` → `dist\DNSPilot-Setup-<sürüm>.exe`. Sessiz kurulum `/S`, sessiz kaldırma `/uninstall /S`.
- Store görselleri: `store\` (Figma "My Project" → Branding sayfasından).
- İndirme adresi (Cloudflare R2, bucket `dns-pilot-releases`): `https://pub-8381b8018eaa4b93a7d06321c7f3bf64.r2.dev/<sürüm>/DNSPilot-Setup-<sürüm>.exe`

Göndermeden önce kalanlar:
1. Kurucuyu kod imzalama sertifikasıyla imzala: `signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a dist\DNSPilot-Setup-1.0.0.exe`
2. İmzalı dosyayı R2'ye `1.0.0/DNSPilot-Setup-1.0.0.exe` olarak yükle. Her sürüm yeni bir klasöre; yüklenen dosya sonradan değiştirilmez.
3. Partner Center → Paketler: adres, mimari x64 ve x86, dil Türkçe, sessiz parametre `/S`; ardından paket doğrulamasını çalıştır ve gönder.
