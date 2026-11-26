using System.Collections.Generic;
using DnsDegistirici;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot.Pages
{
    // Erişim sayfası: sürücüsüz şeffaf erişim (src\Bypass.cs).
    public sealed class AccessPage : SettingsPageBase
    {
        public AccessPage() : base("Erişim") { }

        protected override void Build(StackPanel s)
        {
            var snap = App.State;
            bool on = App.PendingAccess ?? snap.AccessOn;

            string desc = snap.AccessOn
                ? "Etkin · " + App.Bypass.Connections + " bağlantı yönlendirildi. Ses ve görüntü trafiği doğrudan gider."
                : "Discord gibi yalnızca DNS değişikliğiyle açılamayan hizmetlere erişim sağlar. Sürücü kurmaz; trafik doğrudan hedefe gider, ping değişmez.";
            s.Children.Add(Card("", "Erişim", desc,
                Toggle(on, v => { if (v) App.StartAccess(); else App.StopAccess(); })));
            AccessErrorBar(s);

            Section(s, "Ayarlar");
            s.Children.Add(Card("", "Bölme biçimi",
                "Bağlantının ilk paketi, kısıtlamayı uygulayan sistemin okuyamayacağı biçimde gönderilir. Bağlantınızda erişim sağlanamazsa diğer biçimleri deneyin.",
                Combo(new[]
                {
                    new KeyValuePair<string, string>("TlsRecord", "TLS kaydını böl (önerilen)"),
                    new KeyValuePair<string, string>("TlsRecordTcp", "TLS kaydını ve TCP paketini böl"),
                    new KeyValuePair<string, string>("Tcp", "Yalnızca TCP paketini böl"),
                }, Settings.Get("erisim.bolme", "TlsRecord"), v =>
                {
                    Settings.Set("erisim.bolme", v);
                    App.AccessSettingsChanged();
                }, true, 280)));
            s.Children.Add(Card("", "Discord uyumluluğu",
                "Discord masaüstü uygulamasının güncellemeleri DNS Pilot üzerinden indirilir (Discord'un güncelleme adresi bu bilgisayardaki aktarıcıya yönlendirilir). Aktarıcıya ulaşılamazsa Discord birkaç saniye bekleyip yüklü sürümle açılır. Erişim kapatılınca ayarlar geri alınır.",
                Toggle(AppController.DiscordCompat, App.SetDiscordCompat)));

            Section(s, "Başlangıç");
            s.Children.Add(Card("", "Uygulama açıldığında başlat",
                "Erişim, uygulama her başlatıldığında otomatik olarak etkinleşir.",
                Toggle(Settings.Get("zapret.otomatik", "1") == "1", v => Settings.Set("zapret.otomatik", v ? "1" : "0"))));

            Section(s, "Gelişmiş");
            s.Children.Add(Link("", "Site listesini düzenle", "Erişimin uygulanacağı alan adlarını ekleyin veya kaldırın. Değişiklik, erişim yeniden başlatılınca geçerli olur.",
                () => { SiteList.Ensure(); OpenInNotepad(SiteList.FilePath); }));
        }
    }
}
