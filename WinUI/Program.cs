using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DnsPilot
{
    public static class Program
    {
        // WinForms sürümüyle aynı adlar: ikisi aynı anda çalışıp ayarları birbirinden değiştirmesin.
        public const string MutexName = "DnsDegistirici_TekKopya";
        public const string ShowEventName = "DnsDegistirici_Goster";

        // Görev zamanlayıcıdan (oturum açılışı) başlatıldığında pencere açılmaz, yalnızca tepsi simgesi.
        public static bool StartInTray;

        [STAThread]
        static int Main(string[] args)
        {
            // netsh/schtasks çıktısı OEM kod sayfasıyla (Türkçe: 857) okunur. .NET Framework bunu hazır tanır;
            // .NET 8'de sağlayıcı kaydedilmezse Encoding.GetEncoding NotSupportedException fırlatıp uygulamayı çökertiyordu.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Kaldırıcı çağırır: DNS Pilot'un yaptığı DNS ayarını geri al, arayüz açmadan çık.
            if (args.Any(a => string.Equals(a, "--dns-sifirla", StringComparison.OrdinalIgnoreCase)))
                return ResetDnsForUninstall();

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                // "Yönetici olarak yeniden başlat": eski kopya kapanırken yenisi birkaç saniye bekler.
                if (!created && args.Any(a => string.Equals(a, "--yeniden", StringComparison.OrdinalIgnoreCase)))
                {
                    try { created = mutex.WaitOne(15000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    // Zaten çalışıyor: çalışan kopyanın penceresini öne getir.
                    try { using (var show = EventWaitHandle.OpenExisting(ShowEventName)) show.Set(); }
                    catch (Exception) { }
                    return 0;
                }

                StartInTray = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Application.Start(p =>
                {
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    new App();
                });
            }
            return 0;
        }

        // 2.x'te Zapret'in kaydettiği önceki DNS varsa ona, yoksa DNS bu uygulamanın bir profilindeyse otomatik (DHCP) ayara döner.
        // Kullanıcının kendi girdiği, profillerde olmayan bir DNS'e dokunulmaz.
        static int ResetDnsForUninstall()
        {
            try
            {
                string saved = DnsDegistirici.Settings.Get("zapret.onceki_dns", null);
                string error;
                if (saved != null) error = DnsDegistirici.DnsManager.Restore(saved);
                else
                {
                    var state = DnsDegistirici.DnsManager.GetState(DnsDegistirici.ProfileStore.Load());
                    error = state.IsStatic && state.Matched != null ? DnsDegistirici.DnsManager.ResetToDhcp() : null;
                }
                DnsDegistirici.Diag.Log("kaldırma: DNS geri alındı" + (error != null ? " · " + error : ""));
                return error == null ? 0 : 1;
            }
            catch (Exception ex)
            {
                DnsDegistirici.Diag.Error("kaldırma: DNS geri alma", ex);
                return 1;
            }
        }
    }
}
