# Microsoft Store yeniden gönderim (2.2.0)

Sertifikasyon raporu (16.11.2026) üç madde içeriyordu. 2.2.0 hepsini ele alır.

| Madde | Sorun | 2.2.0'daki çözüm |
|---|---|---|
| 10.2.4.2 Security – Software Dependencies | Pakette Microsoft'un sağlamadığı sürücü vardı (zapret motoruyla gelen `WinDivert64.sys`). | Store paketi motorsuz üretilir (`msix\build-msix.ps1` artık `-Engine` olmadan çalışır). Varsayılan erişim yöntemi sürücü kullanmaz (`src\Bypass.cs`). Paket içeriği doğrulandı: `WinDivert`, `winws`, `.sys` dosyası yok. |
| 10.1.2.10 Functionality (3000x2000, %250) | Ekran görüntüsünde içerik yarım çizilmişti. | Pencere boyutu çalışma alanıyla sınırlandı (önceden yüksek ölçekte ekrandan taşıyordu). İlk sayfa açılış animasyonu olmadan açılır (görüntü animasyonun ortasında alınmış gibiydi). 1920x1080 %175'te (en yüksek ölçek) hem canlı ölçek değişiminde hem sıfırdan açılışta denetlendi. |
| 10.6.3 Capabilities | `allowElevation` talebi diğer sorunlar yüzünden incelenmemişti. | Aşağıdaki gerekçe metni Submission options → "Restricted capabilities" alanına yazılmalı. |

## Gönderim adımları

1. Partner Center → DNS Pilot → mevcut taslak (Submission 1) → **Packages**: eski `DNSPilot.msix`'i kaldırın, `dist\DNSPilot-2.2.0.msix`'i yükleyin.
2. **Submission options** → restricted capability gerekçesi (aşağıda) ve "Notes for certification".
3. **Store listings**: açıklamada Zapret/WinDivert geçiyorsa "sürücüsüz erişim" olarak güncelleyin (öneri aşağıda).
4. **Resubmit for certification**.

## allowElevation gerekçesi (İngilizce, Submission options'a yapıştırın)

> DNS Pilot is a network utility that changes the system DNS servers of the active network adapters and enables DNS over HTTPS (netsh / Windows DNS client settings). It also adds a temporary Name Resolution Policy Table rule (HKLM, volatile, removed on reboot) so that only user-listed domains are resolved by the app's local resolver on 127.88.0.1. Both operations modify machine-wide network configuration and require administrator rights. The app requests elevation once at launch; it does not install drivers or services, and all changes are reverted when the feature is turned off or the app exits.

## Notes for certification (İngilizce)

> The app runs in the notification area. Open it from the tray icon or Start menu and accept the UAC prompt. "DNS" page: pick a DNS profile (e.g. Cloudflare) to change the adapter's DNS; "Otomatik" restores DHCP. "Erişim" (Access) page: the toggle enables driverless access to services that are blocked by deep packet inspection in some countries (e.g. Discord in Turkey). It does not use a VPN or remote server and installs no driver: connections to the listed domains go to a local listener which forwards them directly to the original server, only splitting the first TLS record. No account or sign-in is required.

## Mağaza açıklaması için öneri (Türkçe)

> **Erişim:** Discord gibi yalnızca DNS değişikliğiyle açılamayan hizmetlere sürücü kurmadan erişim. Trafik uzak bir sunucudan geçmez; bağlantınız doğrudan hedefe gider, ping değişmez.

## Yerel test notları

- Test paketi: `msix\build-msix.ps1 -TestName DNSPilot.Test -TestPublisher "CN=DNSPilotTest" -CertThumbprint <parmak izi> -TestRevision <n>` (aynı sürümü yeniden kurabilmek için `-TestRevision` son haneyi artırır).
- 26.11.2026'da Türk Telekom bağlantısında, Zapret kapalıyken doğrulandı: discord.com, gateway (WebSocket), cdn, media, updates; Discord masaüstü açıldı ve oturum kurdu (`[GatewaySocket] [READY]`); 119 MB indirmede doğrudan bağlantıyla aynı hız.
- Sesli kanal test edilmedi (kullanıcının hesabıyla kanala girilmedi). Ses UDP ile sunucunun bildirdiği IP'ye gider ve bu yola uğramaz; Zapret de yalnızca TCP 443'ü işliyordu.
- Ayrıntılı bağlantı kaydı için uygulamanın `ayarlar.txt` dosyasına `erisim.kayit=1` ekleyin; kayıt `%TEMP%\DnsPilot-kayit.txt`.
