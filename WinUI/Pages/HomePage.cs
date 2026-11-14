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
            var profiles = ProfileStore.Load();
            var state = DnsManager.GetState(profiles);
            bool zapretOn = Zapret.IsRunning();
            var strategy = AppController.CurrentStrategy(Zapret.LoadStrategies());

            string servers = state.Servers.Count == 0 ? "—" : string.Join(", ", state.Servers.Take(4));
            s.Children.Add(Card("", StatusText(state),
                state.HasAdapter ? state.AdapterName + " bağlantısı · Sunucular: " + servers : "Etkin bir ağ bağlantısı algılanmadı.",
                Btn("Değiştir", () => DnsPilot.App.Instance.ShowWindow("DNS"))));

            s.Children.Add(Card("", "Zapret",
                !Zapret.Installed ? "Zapret motoru bulunamadı."
                : zapretOn ? "Etkin · " + (strategy != null ? strategy.Name : "Varsayılan") + " stratejisi kullanılıyor"
                : "Devre dışı · Erişim kısıtlamalarını aşmak için etkinleştirin",
                Toggle(zapretOn, on => { if (on) App.StartZapret(); else App.StopZapret(); }, !App.Busy && Zapret.Installed)));

            var u = App.Updates;
            if (!Settings.Packaged && u.ZapretAvailable && !u.Installing)
            {
                var info = new InfoBar
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Informational,
                    Title = "Güncelleme kullanılabilir",
                    Message = "Zapret motorunun yeni bir sürümü yüklenmeye hazır.",
                    ActionButton = Btn("Görüntüle", () => DnsPilot.App.Instance.ShowWindow("Güncellemeler")),
                    Margin = new Thickness(0, 4, 0, 0),
                };
                s.Children.Add(info);
            }

            Section(s, "Araçlar");
            s.Children.Add(Link("", "DNS hız testi", "Kayıtlı DNS sunucularının yanıt sürelerini ölçer ve karşılaştırır.",
                () => SpeedTestDialog.Show(XamlRoot)));
            s.Children.Add(Card("", "DNS önbelleğini temizle",
                "Bir site açılmıyorsa veya eski bir adrese yönleniyorsa, kayıtlı DNS yanıtlarını temizleyerek sorunu giderir.",
                Btn("Temizle", App.FlushCache, !App.Busy)));
        }
    }
}
