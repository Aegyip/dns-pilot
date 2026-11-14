using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DnsPilot
{
    public static class Program
    {
        // WinForms sürümüyle aynı adlar: ikisi aynı anda çalışıp zapret'i birbirinden kapatmasın.
        public const string MutexName = "DnsDegistirici_TekKopya";
        public const string ShowEventName = "DnsDegistirici_Goster";

        // Görev zamanlayıcıdan (oturum açılışı) başlatıldığında pencere açılmaz, yalnızca tepsi simgesi.
        public static bool StartInTray;

        [STAThread]
        static int Main(string[] args)
        {
            bool created;
            using (new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Zaten çalışıyor: çalışan kopyanın penceresini öne getir.
                    try { using (var show = EventWaitHandle.OpenExisting(ShowEventName)) show.Set(); }
                    catch (Exception) { }
                    return 0;
                }

                // netsh/schtasks çıktısı OEM kod sayfasıyla (Türkçe: 857) okunur. .NET Framework bunu hazır tanır;
                // .NET 8'de sağlayıcı kaydedilmezse Encoding.GetEncoding NotSupportedException fırlatıp uygulamayı çökertiyordu.
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

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
    }
}
