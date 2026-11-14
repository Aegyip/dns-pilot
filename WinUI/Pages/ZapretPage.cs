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

        // Açılır liste: (değer, görünen metin) çiftleri; seçim değişince onChange(değer).
        static ComboBox Combo(IEnumerable<KeyValuePair<string, string>> options, string value, Action<string> onChange, bool enabled = true, double width = 260)
        {
            var box = new ComboBox { MinWidth = width, IsEnabled = enabled };
            var keys = new List<string>();
            foreach (var o in options)
            {
                keys.Add(o.Key);
                box.Items.Add(new ComboBoxItem { Content = o.Value, Tag = o.Key });
            }
            box.SelectedIndex = Math.Max(0, keys.IndexOf(value));
            box.SelectionChanged += (s, e) =>
            {
                var item = box.SelectedItem as ComboBoxItem;
                if (item != null) onChange((string)item.Tag);
            };
            return box;
        }

        protected override void Build(StackPanel s)
        {
            bool on = Zapret.IsRunning();
            var strategies = Zapret.LoadStrategies();
            var strategy = AppController.CurrentStrategy(strategies);

            s.Children.Add(Card("", "Zapret",
                Zapret.Installed
                    ? "Derin paket incelemesi (DPI) yoluyla uygulanan erişim kısıtlamalarını aşar. Discord gibi yalnızca DNS değişikliğiyle açılamayan hizmetler için gereklidir."
                    : "Zapret motoru bulunamadı. Güncellemeler sayfasından yükleyebilirsiniz.",
                Toggle(on, v => { if (v) App.StartZapret(); else App.StopZapret(); }, !App.Busy && Zapret.Installed)));

            Section(s, "Yapılandırma");
            bool testing = App.StrategyTestRunning;
            List<StrategyResult> tested;
            lock (StrategyTest.Last) tested = StrategyTest.Last.Values.ToList();
            var best = StrategyTest.Best(tested);

            var findCard = Card("", "En uygun stratejiyi bul",
                App.StrategyTestStatus ?? "Tüm stratejileri bağlantınız üzerinde sırayla dener ve kısıtlanan hizmetlere en hızlı erişimi sağlayanı otomatik olarak seçer. İşlem yaklaşık bir dakika sürer.",
                testing ? (object)Btn("Durdur", App.FindStrategy) : Btn("Bul", App.FindStrategy, !App.Busy && Zapret.Installed, true));
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
                    if (Zapret.IsRunning()) App.StartZapret();
                }, !App.Busy, 280)));

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
                    if (Zapret.IsRunning()) App.StartZapret();
                }, !App.Busy)));

            Section(s, "Başlangıç ve DNS");
            s.Children.Add(Card("", "Uygulama açıldığında başlat",
                "Zapret, uygulama her başlatıldığında otomatik olarak etkinleşir.",
                Toggle(Settings.Get("zapret.otomatik", "0") == "1", v => Settings.Set("zapret.otomatik", v ? "1" : "0"))));

            bool autoDns = Settings.Get("zapret.sifreli_dns", "1") == "1";
            s.Children.Add(Card("", "Şifreli DNS'e otomatik geç",
                "Bazı servis sağlayıcılar kısıtlı sitelerin şifresiz DNS sorgularını yanıtlamaz. Bu seçenek, Zapret başlarken şifreli DNS'i etkinleştirir.",
                Toggle(autoDns, v => { Settings.Set("zapret.sifreli_dns", v ? "1" : "0"); Rebuild(); })));

            var dohProfiles = ProfileStore.Load().Where(p => p.Doh != null).ToList();
            string preferred = Settings.Get("zapret.dns", "AdGuard");
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
