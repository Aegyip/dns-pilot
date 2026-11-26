using System.Linq;
using DnsDegistirici;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot.Pages
{
    public sealed class HomePage : SettingsPageBase
    {
        public HomePage() : base("Giriş") { }

        static string StatusText(DnsState state)
        {
            if (!state.HasAdapter) return "Bağlantı yok";
            string name = !state.IsStatic ? "Otomatik" : (state.Matched ?? "Özel sunucu");
            return name + " · " + (state.DohOn ? "Şifreli (DoH)" : state.IsStatic ? "Şifresiz" : "Servis sağlayıcı DNS'i");
        }

        protected override void Build(StackPanel s)
        {
            var snap = App.State;
            var state = snap.Dns;
            bool accessOn = snap.AccessOn;
            bool accessTarget = App.PendingAccess ?? accessOn;

            string servers = state.Servers.Count == 0 ? "—" : string.Join(", ", state.Servers.Take(4));
            s.Children.Add(Card("\uE774", StatusText(state),
                state.HasAdapter ? state.AdapterName + " bağlantısı · Sunucular: " + servers : "Etkin bir ağ bağlantısı algılanmadı.",
                Btn("Değiştir", () => DnsPilot.App.Instance.ShowWindow("DNS"))));

            s.Children.Add(Card("\uEA18", "Erişim",
                accessTarget != accessOn ? (accessTarget ? "Başlatılıyor…" : "Durduruluyor…")
                : accessOn ? "Etkin · Discord gibi kısıtlanan hizmetlere sürücüsüz erişim"
                : "Devre dışı · Erişim kısıtlamalarını aşmak için etkinleştirin",
                Toggle(accessTarget, on => { if (on) App.StartAccess(); else App.StopAccess(); })));
            AccessErrorBar(s);

            Section(s, "Araçlar");
            s.Children.Add(Link("\uE916", "DNS hız testi", "Kayıtlı DNS sunucularının yanıt sürelerini ölçer ve karşılaştırır.",
                () => SpeedTestDialog.Show(XamlRoot)));
            s.Children.Add(Card("\uE894", "DNS önbelleğini temizle",
                "Bir site açılmıyorsa veya eski bir adrese yönleniyorsa, kayıtlı DNS yanıtlarını temizleyerek sorunu giderir.",
                Btn("Temizle", App.FlushCache)));
        }
    }
}
