using System;
using System.Collections.Generic;
using System.Linq;
using DnsDegistirici;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot.Pages
{
    public sealed class ZapretPage : SettingsPageBase
    {
        public ZapretPage() : base("Zapret") { }

        protected override void Build(StackPanel s)
        {
            var snap = App.State;
            bool on = App.PendingZapret ?? snap.ZapretRunning;
            bool installed = snap.ZapretInstalled;
            var strategies = snap.Strategies;
            var strategy = AppController.CurrentStrategy(strategies);

            if (!installed && !Settings.Packaged)
                s.Children.Add(EngineMissingCard());
            else
                s.Children.Add(Card("\uEA18", "Zapret",
                    installed
                        ? "Derin paket incelemesi (DPI) yoluyla uygulanan erişim kısıtlamalarını aşar. Discord gibi yalnızca DNS değişikliğiyle açılamayan hizmetler için gereklidir."
                        : "Zapret motoru bulunamadı.",
                    Toggle(on, v => { if (v) App.StartZapret(); else App.StopZapret(); }, installed)));
            ZapretErrorBar(s);

            Section(s, "Yapılandırma");
            bool testing = App.StrategyTestRunning;
            List<StrategyResult> tested;
            lock (StrategyTest.Last) tested = StrategyTest.Last.Values.ToList();
            var best = StrategyTest.Best(tested);

            var findCard = Card("", "En uygun stratejiyi bul",
                App.StrategyTestStatus ?? "Tüm stratejileri bağlantınız üzerinde sırayla dener ve kısıtlanan hizmetlere en hızlı erişimi sağlayanı otomatik olarak seçer. İşlem yaklaşık bir dakika sürer.",
                testing ? (object)Btn("Durdur", App.FindStrategy) : Btn("Bul", App.FindStrategy, installed, true));
            if (testing)
            {
                // Windows Ayarlar'daki gibi kartın altında belirsiz ilerleme çubuğu.
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
            s.Children.Add(Card("", "Strateji",
                "Kısıtlamaları aşmak için kullanılan yöntem. En iyi sonuç için internet servis sağlayıcınıza uygun olanı seçin.",
                Combo(strategyOptions, strategy != null ? strategy.Name : null, v =>
                {
                    Settings.Set("zapret.strateji", v);
                    App.ZapretSettingsChanged();
                }, true, 280)));

            s.Children.Add(Card("", "Uygulanacak siteler",
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

            Section(s, "Başlangıç ve DNS");
            s.Children.Add(Card("", "Uygulama açıldığında başlat",
                "Zapret, uygulama her başlatıldığında otomatik olarak etkinleşir.",
                Toggle(Settings.Get("zapret.otomatik", "1") == "1", v => Settings.Set("zapret.otomatik", v ? "1" : "0"))));

            bool autoDns = Settings.Get("zapret.sifreli_dns", "1") == "1";
            s.Children.Add(Card("", "Şifreli DNS'e otomatik geç",
                "Bazı servis sağlayıcılar kısıtlı sitelerin şifresiz DNS sorgularını yanıtlamaz. Bu seçenek, Zapret başlarken şifreli DNS'i etkinleştirir.",
                Toggle(autoDns, v => { Settings.Set("zapret.sifreli_dns", v ? "1" : "0"); Rebuild(); })));

            var dohProfiles = snap.Profiles.Where(p => p.Doh != null).ToList();
            string preferred = Settings.Get("zapret.dns", "Cloudflare");
            var match = dohProfiles.FirstOrDefault(p => p.Name.StartsWith(preferred, StringComparison.OrdinalIgnoreCase));
            s.Children.Add(Card("", "Tercih edilen şifreli DNS",
                "Seçilen sunucuya ulaşılamazsa sıradaki şifreli sunucu kullanılır.",
                Combo(dohProfiles.Select(p => new KeyValuePair<string, string>(p.Name, p.Name)),
                      match != null ? match.Name : preferred, v => Settings.Set("zapret.dns", v), autoDns)));

            Section(s, "Gelişmiş");
            s.Children.Add(Link("", "Site listesini düzenle", "Zapret'in uygulanacağı alan adlarını ekleyin veya kaldırın.",
                () => { Zapret.EnsureUserFiles(); OpenInNotepad(Zapret.HostlistPath); }));
            s.Children.Add(Link("", "Stratejileri düzenle", "Yeni strateji tanımlayın veya mevcut stratejilerin parametrelerini değiştirin.",
                () => OpenInNotepad(Zapret.StrategiesPath)));
        }
    }
}
