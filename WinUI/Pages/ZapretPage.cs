using System;
using System.Collections.Generic;
using System.Linq;
using DnsDegistirici;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot.Pages
{
    // Erişim sayfası: varsayılan yöntem sürücüsüz şeffaf moddur; Zapret yalnızca motoru kuruluysa seçilebilir.
    public sealed class ZapretPage : SettingsPageBase
    {
        public ZapretPage() : base("Erişim") { }

        protected override void Build(StackPanel s)
        {
            var snap = App.State;
            bool on = App.PendingZapret ?? snap.AccessOn;
            bool installed = snap.ZapretInstalled;
            string method = AppController.Method;
            bool zapret = method == AppController.MethodZapret;

            string desc = zapret
                ? "Zapret ile derin paket incelemesi (DPI) yoluyla uygulanan erişim kısıtlamalarını aşar."
                : "Discord gibi yalnızca DNS değişikliğiyle açılamayan hizmetlere erişim sağlar. Sürücü kurmaz; trafik doğrudan hedefe gider, ping değişmez.";
            if (snap.BypassRunning)
                desc = "Etkin · " + App.Bypass.Connections + " bağlantı yönlendirildi. Ses ve görüntü trafiği doğrudan gider.";
            s.Children.Add(Card("", "Erişim", desc,
                Toggle(on, v => { if (v) App.StartAccess(); else App.StopAccess(); })));
            ZapretErrorBar(s);

            Section(s, "Yöntem");
            var methods = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(AppController.MethodBypass, "Sürücüsüz (önerilen)"),
            };
            if (installed) methods.Add(new KeyValuePair<string, string>(AppController.MethodZapret, "Zapret"));
            s.Children.Add(Card("\uE945", "Erişim yöntemi",
                installed
                    ? "Sürücüsüz yöntem çoğu bağlantıda yeterlidir. Zapret, ağ sürücüsü kullanarak tüm uygulamalara uygulanır."
                    : "Sürücüsüz yöntem, bağlantının ilk paketini kısıtlamayı uygulayan sistemin okuyamayacağı biçimde gönderir.",
                Combo(methods, method, v =>
                {
                    Settings.Set("erisim.yontem", v);
                    App.AccessMethodChanged();
                    Rebuild();
                }, installed, 280)));

            if (!zapret)
            {
                s.Children.Add(Card("\uE9D9", "Bölme biçimi",
                    "Bağlantınızda erişim sağlanamazsa diğer biçimleri deneyin.",
                    Combo(new[]
                    {
                        new KeyValuePair<string, string>("TlsRecord", "TLS kaydını böl (önerilen)"),
                        new KeyValuePair<string, string>("TlsRecordTcp", "TLS kaydını ve TCP paketini böl"),
                        new KeyValuePair<string, string>("Tcp", "Yalnızca TCP paketini böl"),
                    }, Settings.Get("erisim.bolme", "TlsRecord"), v =>
                    {
                        Settings.Set("erisim.bolme", v);
                        App.AccessMethodChanged();
                    }, true, 280)));
                s.Children.Add(Card("\uE8BD", "Discord uyumluluğu",
                    "Discord masaüstü uygulamasının güncelleme denetimi bu yöntemle tamamlanamaz. Bu seçenek, Discord'un kendi \"güncelleme denetimi başarısızsa yine de aç\" ayarını etkinleştirir; Discord birkaç saniye sonra açılır. Erişim kapatılınca ayar geri alınır.",
                    Toggle(AppController.DiscordCompat, App.SetDiscordCompat)));
            }
            else
                BuildZapret(s, snap, installed);

            Section(s, "Başlangıç");
            s.Children.Add(Card("\uE7E8", "Uygulama açıldığında başlat",
                "Erişim, uygulama her başlatıldığında otomatik olarak etkinleşir.",
                Toggle(Settings.Get("zapret.otomatik", "1") == "1", v => Settings.Set("zapret.otomatik", v ? "1" : "0"))));

            Section(s, "Gelişmiş");
            s.Children.Add(Link("\uE8FD", "Site listesini düzenle", "Erişimin uygulanacağı alan adlarını ekleyin veya kaldırın. Değişiklik, erişim yeniden başlatılınca geçerli olur.",
                () => { Zapret.EnsureUserFiles(); OpenInNotepad(Zapret.HostlistPath); }));
            if (zapret)
                s.Children.Add(Link("\uE70F", "Stratejileri düzenle", "Yeni strateji tanımlayın veya mevcut stratejilerin parametrelerini değiştirin.",
                    () => OpenInNotepad(Zapret.StrategiesPath)));
        }

        void BuildZapret(StackPanel s, Snapshot snap, bool installed)
        {
            var strategies = snap.Strategies;
            var strategy = AppController.CurrentStrategy(strategies);
            bool testing = App.StrategyTestRunning;
            List<StrategyResult> tested;
            lock (StrategyTest.Last) tested = StrategyTest.Last.Values.ToList();
            var best = StrategyTest.Best(tested);

            var findCard = Card("\uE9D9", "En uygun stratejiyi bul",
                App.StrategyTestStatus ?? "Tüm stratejileri bağlantınız üzerinde sırayla dener ve kısıtlanan hizmetlere en hızlı erişimi sağlayanı otomatik olarak seçer. İşlem yaklaşık bir dakika sürer.",
                testing ? (object)Btn("Durdur", App.FindStrategy) : Btn("Bul", App.FindStrategy, installed, true));
            if (testing)
            {
                findCard.Description = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = App.StrategyTestStatus ?? "", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                        new ProgressBar { IsIndeterminate = true, MaxWidth = 360, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left }
                    }
                };
            }
            s.Children.Add(findCard);

            var strategyOptions = strategies.Select(st =>
            {
                StrategyResult res;
                lock (StrategyTest.Last) StrategyTest.Last.TryGetValue(st.Name, out res);
                string label = st.Name;
                if (res != null) label += (res == best ? "  ★ " : " · ") + res.Label;
                return new KeyValuePair<string, string>(st.Name, label);
            });
            s.Children.Add(Card("\uE945", "Strateji",
                "Kısıtlamaları aşmak için kullanılan yöntem. En iyi sonuç için internet servis sağlayıcınıza uygun olanı seçin.",
                Combo(strategyOptions, strategy != null ? strategy.Name : null, v =>
                {
                    Settings.Set("zapret.strateji", v);
                    App.ZapretSettingsChanged();
                }, true, 280)));

            s.Children.Add(Card("\uE8FD", "Uygulanacak siteler",
                "Önerilen ayar, yalnızca listedeki sitelerdir. Otomatik algılama, erişilemeyen siteleri kendiliğinden listeye ekler.",
                Combo(new[]
                {
                    new KeyValuePair<string, string>(Zapret.FilterList, "Yalnızca listedeki siteler"),
                    new KeyValuePair<string, string>(Zapret.FilterAuto, "Otomatik algılama"),
                    new KeyValuePair<string, string>(Zapret.FilterAll, "Tüm siteler"),
                }, Settings.Get("zapret.filtre", Zapret.FilterList), v =>
                {
                    Settings.Set("zapret.filtre", v);
                    App.ZapretSettingsChanged();
                })));

            bool autoDns = Settings.Get("zapret.sifreli_dns", "1") == "1";
            s.Children.Add(Card("\uE72E", "Şifreli DNS'e otomatik geç",
                "Bazı servis sağlayıcılar kısıtlı sitelerin şifresiz DNS sorgularını yanıtlamaz. Bu seçenek, Zapret başlarken şifreli DNS'i etkinleştirir.",
                Toggle(autoDns, v => { Settings.Set("zapret.sifreli_dns", v ? "1" : "0"); Rebuild(); })));

            var dohProfiles = snap.Profiles.Where(p => p.Doh != null).ToList();
            string preferred = Settings.Get("zapret.dns", "Cloudflare");
            var match = dohProfiles.FirstOrDefault(p => p.Name.StartsWith(preferred, StringComparison.OrdinalIgnoreCase));
            s.Children.Add(Card("\uE774", "Tercih edilen şifreli DNS",
                "Seçilen sunucuya ulaşılamazsa sıradaki şifreli sunucu kullanılır.",
                Combo(dohProfiles.Select(p => new KeyValuePair<string, string>(p.Name, p.Name)),
                      match != null ? match.Name : preferred, v => Settings.Set("zapret.dns", v), autoDns)));
        }
    }
}
