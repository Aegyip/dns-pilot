using DnsDegistirici;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot.Pages
{
    public sealed class DnsPage : SettingsPageBase
    {
        public DnsPage() : base("DNS") { }

        protected override void Build(StackPanel s)
        {
            var profiles = ProfileStore.Load();
            var state = DnsManager.GetState(profiles);

            s.Children.Add(new TextBlock
            {
                Text = "Bağlantınızın alan adlarını çözmek için kullanacağı DNS sunucusunu seçin. Şifreli (DoH) sunucular, " +
                       "sorgularınızı internet servis sağlayıcınızın görmesini ve yönlendirmesini engeller.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(1, 0, 0, 4),
            });

            Section(s, "Sunucular");
            foreach (var p in profiles)
            {
                var profile = p;
                bool active = state.IsStatic && state.Matched == p.Name;
                string desc = string.Join(", ", p.V4) + " · " + (p.Doh != null ? "Şifreli (DNS over HTTPS)" : "Şifresiz");
                object action = active ? (object)Badge("✓ Kullanılıyor", true) : Btn("Kullan", () => App.ApplyProfile(profile), !App.Busy);
                s.Children.Add(Card(p.Doh != null ? "" : "", p.Name, desc, action));
            }
            bool auto = state.HasAdapter && !state.IsStatic;
            s.Children.Add(Card("", "Otomatik",
                "DNS sunucusunu modeminiz veya servis sağlayıcınız belirler. Bağlantı sorunlarında varsayılan ayara dönmek için kullanın.",
                auto ? (object)Badge("✓ Kullanılıyor", true) : Btn("Kullan", App.ResetDns, !App.Busy)));

            Section(s, "Gelişmiş");
            s.Children.Add(Link("", "DNS profillerini düzenle",
                "Listeye özel sunucu ekleyin veya mevcut sunucuları kaldırın. Değişiklikler sayfa yeniden açıldığında uygulanır.",
                () => OpenInNotepad(ProfileStore.FilePath)));
        }
    }
}
