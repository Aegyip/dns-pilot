using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DnsPilot
{
    public partial class App : Application
    {
        public static App Instance { get; private set; }
        public static DispatcherQueue UI { get; private set; }

        MainWindow window;
        Tray tray;
        EventWaitHandle showEvent;

        public App()
        {
            Instance = this;
            InitializeComponent();
            // Arayüzde yakalanmamış bir hata uygulamayı kapatmaz: kayda geçer (%TEMP%\DnsPilot-kayit.txt) ve uygulama sürer.
            // Önceden tepsi simgesi güncellemesi gibi küçük bir hata bile süreci sonlandırıyor, geride tıklanınca tepki
            // vermeyen bir tepsi simgesi kalıyordu ("uygulama dondu").
            UnhandledException += (s, e) =>
            {
                DnsDegistirici.Diag.Log("ARAYÜZ HATASI: " + e.Message + System.Environment.NewLine + e.Exception);
                e.Handled = true;
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                DnsDegistirici.Diag.Log("GÖREV HATASI: " + e.Exception);
                e.SetObserved();
            };
            // İş parçacıklarındaki hatalar süreci yine sonlandırır; en azından nedeni kayda geçsin.
            System.AppDomain.CurrentDomain.UnhandledException += (s, e) => DnsDegistirici.Diag.Log("ÇÖKME: " + e.ExceptionObject);
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            UI = DispatcherQueue.GetForCurrentThread();
            Identity.Register(); // bildirim başlığında "DNS Pilot" adı ve logosu

            AppController.Instance.Init(UI);
            tray = new Tray(AppController.Instance);

            // İkinci kez açılmak istenirse (exe'ye tekrar tıklama) pencereyi öne getir.
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    showEvent.WaitOne();
                    UI.TryEnqueue(() =>
                    {
                        try { ShowWindow(null); } catch (System.Exception ex) { DnsDegistirici.Diag.Error("pencere", ex); }
                    });
                }
            }) { IsBackground = true };
            listener.Start();

            if (tray != null) AppController.Instance.Notify = tray.Notify;
            AppController.Instance.StartBackgroundWork();

            if (!Program.StartInTray) ShowWindow(null);
        }

        public void ShowWindow(string page)
        {
            if (window == null)
            {
                window = new MainWindow();
                window.Closed += (s, e) => window = null;
            }
            if (page != null) window.Navigate(page);
            window.BringToFront();
        }

        public void ExitApp()
        {
            AppController.Instance.Shutdown();
            if (tray != null) tray.Dispose();
            if (window != null) window.ForceClose();
            Exit();
        }
    }
}
