using System.Diagnostics;
using DnsDegistirici;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot.Pages
{
    public sealed class SystemPage : SettingsPageBase
    {
        public SystemPage() : base("Sistem") { }

        protected override void Build(StackPanel s)
        {
            s.Children.Add(Card("", "Windows ile başlat",
                "Oturum açtığınızda uygulama, yönetici onayı istemeden bildirim alanında başlar.",
                Toggle(App.AutostartEnabled, App.SetAutostart)));
            s.Children.Add(Link("", "Uygulama klasörünü aç",
                "DNS profillerinin, ayarların ve Zapret dosyalarının bulunduğu klasörü Dosya Gezgini'nde açar.",
                () => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Settings.AppDir + "\"") { UseShellExecute = false })));

            Section(s, "Hakkında");
            s.Children.Add(Card("", "DNS Pilot",
                "Sürüm " + AppVersion + " · Zapret motoru " + AppController.ShortSha(Zapret.CurrentVersion())));

            Section(s, "Destek");
            s.Children.Add(Card("\uEBE8", "Hata bildir",
                "GitHub'da hazır doldurulmuş bir hata bildirimi açar. Uygulama sürümü, Windows sürümü ve Zapret durumu eklenir; göndermeden önce her şeyi görüp düzenleyebilirsiniz.",
                Btn("Hata bildir", BugReport.Open, true, true)));
            s.Children.Add(Link("\uE943", "Kaynak kodu", "DNS Pilot açık kaynaklıdır. Kaynak kodu ve sürüm notları GitHub'da.", BugReport.OpenRepo));
        }
    }
}
