using System.Diagnostics;
using DnsDegistirici;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot.Pages
{
    public sealed class SystemPage : SettingsPageBase
    {
        public SystemPage() : base("Sistem") { }

        string copied;

        async void CopyDiagnostics()
        {
            try
            {
                copied = "Hazırlanıyor…";
                Rebuild();
                string text = await App.DiagnosticsText();
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                copied = "Panoya kopyalandı. Hata bildiriminin \"Ek bilgi\" alanına yapıştırabilirsiniz (Ctrl+V).";
            }
            catch (System.Exception ex)
            {
                DnsDegistirici.Diag.Error("tanılama kopyalama", ex);
                copied = "Kopyalanamadı: " + ex.Message + " · Kayıt dosyası: " + DnsDegistirici.Diag.FilePath;
            }
            Rebuild();
        }

        protected override void Build(StackPanel s)
        {
            s.Children.Add(Card("", "Windows ile başlat",
                "Oturum açtığınızda uygulama, yönetici onayı istemeden bildirim alanında başlar.",
                Toggle(App.AutostartEnabled == true, App.SetAutostart, App.AutostartEnabled.HasValue)));
            s.Children.Add(Link("", "Uygulama klasörünü aç",
                "DNS profillerinin, ayarların ve Zapret dosyalarının bulunduğu klasörü Dosya Gezgini'nde açar.",
                () => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Settings.AppDir + "\"") { UseShellExecute = false })));

            Section(s, "Hakkında");
            s.Children.Add(Card("", "DNS Pilot",
                "Sürüm " + AppVersion + " · Zapret motoru " + AppController.ShortSha(App.State.ZapretVersion)));

            Section(s, "Destek");
            s.Children.Add(Card("\uEBE8", "Hata bildir",
                "GitHub'da hazır doldurulmuş bir hata bildirimi açar. Uygulama sürümü, Windows sürümü ve Zapret durumu eklenir; göndermeden önce her şeyi görüp düzenleyebilirsiniz.",
                Btn("Hata bildir", BugReport.Open, true, true)));
            s.Children.Add(Card("", "Tanılama bilgilerini kopyala",
                copied ?? "DNS ve Zapret durumu, ayarlar ve son işlem kayıtları panoya kopyalanır. Hata bildiriminin \"Ek bilgi\" alanına yapıştırın; " +
                "kullanıcı adınız ve klasör yollarınız çıkarılır.",
                Btn("Kopyala", CopyDiagnostics)));
            s.Children.Add(Link("\uE943", "Kaynak kodu", "DNS Pilot açık kaynaklıdır. Kaynak kodu ve sürüm notları GitHub'da.", BugReport.OpenRepo));
        }
    }
}
