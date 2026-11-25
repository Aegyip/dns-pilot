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
            var snap = App.State;
            var state = snap.Dns;
            string pending = App.PendingDns; // sıradaki/çalışan DNS değişikliği

            s.Children.Add(new TextBlock
            {
                Text = "Bağlantınızın alan adlarını çözmek için kullanacağı DNS sunucusunu seçin. Şifreli (DoH) sunucular, " +
                       "sorgularınızı internet servis sağlayıcınızın görmesini ve yönlendirmesini engeller.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(1, 0, 0, 4),
            });

            Section(s, "Sunucular");
            foreach (var p in snap.Profiles)
            {
                var profile = p;
                bool active = state.IsStatic && state.Matched == p.Name;
                string desc = string.Join(", ", p.V4) + " · " + (p.Doh != null ? "Şifreli (DNS over HTTPS)" : "Şifresiz");
                object action = pending == p.Name ? (object)Badge("Uygulanıyor…", false)
                              : active && pending == null ? Badge("✓ Kullanılıyor", true)
                              : Btn("Kullan", () => App.ApplyProfile(profile));
                s.Children.Add(Card(p.Doh != null ? "\uE72E" : "\uE785", p.Name, desc, action));
            }
            bool auto = state.HasAdapter && !state.IsStatic;
            s.Children.Add(Card("\uE839", "Otomatik",
                "DNS sunucusunu modeminiz veya servis sağlayıcınız belirler. Bağlantı sorunlarında varsayılan ayara dönmek için kullanın.",
                pending == "" ? (object)Badge("Uygulanıyor…", false)
                : auto && pending == null ? Badge("✓ Kullanılıyor", true)
                : Btn("Kullan", App.ResetDns)));

            Section(s, "Şifreli DNS");
            s.Children.Add(Card("\uE72E", "Şifreli DNS kesilirse şifresiz DNS'e dön",
                "Önerilir. Kapalıyken şifreli DNS sunucusuna ulaşılamadığı anda (ör. servis sağlayıcı engellediğinde) siteler açılmaz " +
                "veya çok yavaş açılır. Açıkken Windows bu durumda geçici olarak şifresiz DNS kullanır.",
                Toggle(DnsManager.UdpFallback, App.SetUdpFallback)));
            s.Children.Add(Card("\uE7A7", "Zapret durunca önceki DNS ayarına dön",
                "Zapret açılırken şifreli DNS'e geçildiyse, Zapret durdurulduğunda veya uygulamadan çıkıldığında önceki DNS ayarınız geri yüklenir.",
                Toggle(AppController.RestoreDnsOnStop, v => Settings.Set("dns.geri_al", v ? "1" : "0"))));

            Section(s, "Gelişmiş");
            s.Children.Add(Link("\uE70F", "DNS profillerini düzenle",
                "Listeye özel sunucu ekleyin veya mevcut sunucuları kaldırın. Değişiklikler sayfa yeniden açıldığında uygulanır.",
                () => OpenInNotepad(ProfileStore.FilePath)));
        }
    }
}
