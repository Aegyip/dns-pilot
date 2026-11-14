using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("DNS Pilot")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace DnsDegistirici
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (new Mutex(true, "DnsDegistirici_TekKopya", out created))
            {
                if (!created)
                {
                    // Zaten çalışıyor: sessizce çıkmak yerine ilk kopyaya simgesini yeniden göstermesini söyle.
                    try
                    {
                        using (var show = EventWaitHandle.OpenExisting(TrayContext.ShowEventName)) show.Set();
                    }
                    catch (Exception) { }
                    return;
                }
                Notifications.RegisterIdentity(); // bildirim başlığında "DNS Pilot" adı ve logosu
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    class TrayContext : ApplicationContext, IAppActions
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        static extern bool ChangeWindowMessageFilter(uint msg, uint flag);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string windowName);

        public const string ShowEventName = "DnsDegistirici_Goster";

        readonly NotifyIcon tray = new NotifyIcon();
        readonly EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Control invoker = new Control();
        IntPtr iconHandle = IntPtr.Zero;
        bool busy;
        bool? autostart;

        public TrayContext()
        {
            invoker.CreateControl();

            // Uygulama yönetici olarak çalıştığı için Explorer'ın "TaskbarCreated" mesajı UIPI'ye takılıyor;
            // izin verilmezse Explorer geç açıldığında / yeniden başladığında simge bir daha görünmüyor.
            const uint MSGFLT_ADD = 1;
            ChangeWindowMessageFilter(RegisterWindowMessage("TaskbarCreated"), MSGFLT_ADD);

            menu.Opening += (s, e) => { BuildMenu(); Theme.StyleMenu(menu); RefreshIcon(); e.Cancel = false; };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(null); };
            tray.BalloonTipClicked += (s, e) => { if (balloonPage != null) OpenSettings(balloonPage); };
            tray.BalloonTipClosed += (s, e) => balloonPage = null;
            NetworkChange.NetworkAddressChanged += (s, e) => invoker.BeginInvoke(new Action(() => { RefreshIcon(); NotifyChanged(); }));
            BuildMenu();
            RefreshIcon();
            tray.Visible = true;

            // Oturum açılışında görev çubuğu henüz hazır olmayabilir; hazır olunca simgeyi yeniden ekle.
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool waited = false;
                for (int i = 0; i < 120 && FindWindow("Shell_TrayWnd", null) == IntPtr.Zero; i++)
                {
                    waited = true;
                    Thread.Sleep(1000);
                }
                if (waited)
                {
                    Thread.Sleep(2000);
                    invoker.BeginInvoke(new Action(ReAddIcon));
                }
            });

            // İkinci kez açılmaya çalışılırsa simgeyi yeniden göster.
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    showEvent.WaitOne();
                    invoker.BeginInvoke(new Action(() => { ReAddIcon(); OpenSettings(null); }));
                }
            });

            ScheduleAutoUpdateCheck();

            if (Settings.Get("zapret.otomatik", "0") == "1" && !Zapret.IsRunning())
            {
                // Açılışta ağ birkaç saniye geç gelebilir; şifreli DNS testi boşa düşmesin diye bekle.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    for (int i = 0; i < 60 && !NetworkInterface.GetIsNetworkAvailable(); i++) Thread.Sleep(1000);
                    Thread.Sleep(3000);
                    invoker.BeginInvoke(new Action(() => { if (!Zapret.IsRunning()) StartZapret(); }));
                });
            }
        }

        // Açılıştan bir dakika sonra, son denetimin üzerinden bir gün geçtiyse güncellemeleri sessizce denetle.
        void ScheduleAutoUpdateCheck()
        {
            if (Settings.Packaged) return; // Store sürümünde motor güncellemeleri Store ile gelir
            if (Settings.Get("guncelleme.otomatik", "1") != "1") return;
            var last = UpdateState.LoadLastCheck();
            if (last.HasValue && DateTime.Now - last.Value < TimeSpan.FromDays(1)) return;
            var timer = new System.Windows.Forms.Timer { Interval = 60000 };
            timer.Tick += (s, e) => { timer.Stop(); timer.Dispose(); CheckUpdates(true); };
            timer.Start();
        }

        void ReAddIcon()
        {
            tray.Visible = false;
            RefreshIcon();
            tray.Visible = true;
        }

        // Tepsi menüsünde sadece sık kullanılanlar; gerisi Ayarlar penceresinde.
        void BuildMenu()
        {
            menu.Items.Clear();
            var profiles = ProfileStore.Load();
            var state = DnsManager.GetState(profiles);
            bool zapretOn = Zapret.IsRunning();
            var strategy = CurrentStrategy(Zapret.LoadStrategies());

            var header = new ToolStripMenuItem(state.Summary);
            header.Enabled = false;
            menu.Items.Add(header);
            var zapretHeader = new ToolStripMenuItem(zapretOn
                ? "Zapret: açık · " + (strategy != null ? strategy.Name : "?")
                : "Zapret: kapalı");
            zapretHeader.Enabled = false;
            menu.Items.Add(zapretHeader);
            menu.Items.Add(new ToolStripSeparator());

            var toggle = new ToolStripMenuItem(zapretOn ? "Zapret'i durdur" : "Zapret'i başlat");
            toggle.Font = new Font(toggle.Font, FontStyle.Bold);
            toggle.Enabled = !busy && Zapret.Installed;
            toggle.Click += (s, e) => { if (zapretOn) StopZapret(); else StartZapret(); };
            menu.Items.Add(toggle);
            menu.Items.Add(new ToolStripSeparator());

            foreach (var p in profiles)
            {
                var profile = p;
                var item = new ToolStripMenuItem(p.Name + (p.Doh != null ? "   🔒 DoH" : ""));
                item.Checked = state.IsStatic && state.Matched == p.Name;
                item.Enabled = !busy;
                item.Click += (s, e) => ApplyProfile(profile);
                menu.Items.Add(item);
            }

            var auto = new ToolStripMenuItem("Otomatik (modem / ISS)");
            auto.Checked = state.HasAdapter && !state.IsStatic;
            auto.Enabled = !busy;
            auto.Click += (s, e) => ResetDns();
            menu.Items.Add(auto);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Ayarlar", null, (s, e) => OpenSettings(null));
            menu.Items.Add("Hata bildir", null, (s, e) => BugReport.Open());
            menu.Items.Add("Çıkış", null, (s, e) => Quit());
        }

        // ---- IAppActions: Ayarlar penceresi bunları çağırır ----

        public event Action StateChanged;
        public bool Busy { get { return busy; } }
        public bool AutostartEnabled
        {
            get { if (autostart == null) autostart = Autostart.IsEnabled(); return autostart.Value; }
        }

        void NotifyChanged()
        {
            if (StateChanged != null) StateChanged();
        }

        public void ApplyProfile(DnsProfile profile)
        {
            RunBusy(profile.Name + " etkin", () => DnsManager.Apply(profile));
        }

        public void ResetDns()
        {
            RunBusy("Otomatik DNS'e dönüldü", DnsManager.ResetToDhcp);
        }

        public void FlushCache()
        {
            RunBusy("Önbellek temizlendi", () => { DnsManager.FlushCache(); return null; });
        }

        public void StopZapret()
        {
            RunBusy("Zapret durduruldu", () => { Zapret.Stop(); return null; });
        }

        public void SetAutostart(bool on)
        {
            if (on != AutostartEnabled) ToggleAutostart();
        }

        public void SpeedTest()
        {
            SpeedTest(ProfileStore.Load());
        }

        // ---- En uygun stratejiyi ölçerek bulma ----

        volatile bool testRunning, testCancel;
        public bool StrategyTestRunning { get { return testRunning; } }
        public string StrategyTestStatus { get; private set; }

        void TestStatus(string text)
        {
            invoker.BeginInvoke(new Action(() => { StrategyTestStatus = text; NotifyChanged(); }));
        }

        public void FindStrategy()
        {
            if (testRunning) { testCancel = true; TestStatus("Durduruluyor…"); return; }
            if (busy || !Zapret.Installed) return;
            busy = true;
            testRunning = true;
            testCancel = false;
            StrategyTestStatus = "Hazırlanıyor…";
            NotifyChanged();

            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                var icon = ToolTipIcon.Info;
                bool wasRunning = Zapret.IsRunning();
                var strategies = Zapret.LoadStrategies();
                var previous = CurrentStrategy(strategies);
                string filter = Settings.Get("zapret.filtre", Zapret.FilterList);
                try
                {
                    // Engelli adlar şifresiz DNS ile çözülmüyor; gerekirse önce şifreli DNS'e geç.
                    var targets = StrategyTest.Resolve();
                    if (targets.Count < StrategyTest.Hosts.Length)
                    {
                        TestStatus("Şifreli DNS ayarlanıyor…");
                        if (previous != null) Zapret.Start(previous, filter);
                        var profiles = ProfileStore.Load();
                        if (!DnsManager.GetState(profiles).DohOn)
                        {
                            var dns = DnsManager.PickEncrypted(profiles, Settings.Get("zapret.dns", "AdGuard"));
                            if (dns != null) DnsManager.Apply(dns);
                        }
                        DnsManager.FlushCache();
                        targets = StrategyTest.Resolve();
                    }
                    if (targets.Count == 0)
                        throw new Exception("Test sitelerinin adresi çözülemedi. Şifreli DNS'e erişilemiyor olabilir.");

                    // Zapret kapalıyken de erişiliyorsa bağlantında DPI engeli yok demektir.
                    TestStatus("Zapret'siz bağlantı deneniyor…");
                    Zapret.Stop();
                    Thread.Sleep(300);
                    if (StrategyTest.ProbeAll(targets).Count == targets.Count)
                    {
                        result = "Zapret olmadan da engelli sitelere erişiliyor. Bağlantında DPI engeli yok; strateji fark etmez.";
                    }
                    else
                    {
                        var results = StrategyTest.Run(strategies, filter, targets,
                            (i, n, name) => TestStatus("Deneniyor " + i + "/" + n + ": " + name),
                            () => testCancel);
                        var best = StrategyTest.Best(results);
                        if (best != null)
                        {
                            Settings.Set("zapret.strateji", best.Strategy.Name);
                            previous = best.Strategy;
                            result = "En uygun strateji: " + best.Strategy.Name + " (" + best.Label + ")" +
                                     (testCancel ? " · test yarıda kesildi" : "");
                        }
                        else
                        {
                            result = testCancel ? "Test durduruldu."
                                   : "Hiçbir strateji engelli sitelere erişemedi. Hangi siteler ayarını \"Tüm siteler\" yapıp yeniden dene.";
                            icon = ToolTipIcon.Warning;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result = ex.Message;
                    icon = ToolTipIcon.Warning;
                }

                // Zapret test öncesi açıksa (yeni) stratejiyle yeniden aç, kapalıysa kapalı bırak.
                string restartError = null;
                if (wasRunning && previous != null) restartError = Zapret.Start(previous, filter);
                else Zapret.Stop();
                if (restartError != null) { result += "\nZapret yeniden başlatılamadı: " + restartError; icon = ToolTipIcon.Warning; }

                invoker.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    testRunning = false;
                    StrategyTestStatus = result;
                    RefreshIcon();
                    NotifyChanged();
                    Notify(8000, "Strateji testi", Clip(result, 250), icon);
                }));
            });
        }

        SettingsForm settings;

        void OpenSettings(string page)
        {
            if (settings != null && !settings.IsDisposed)
            {
                if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
                if (page != null) settings.GoTo(page);
                settings.Activate();
                return;
            }
            settings = new SettingsForm(this, page);
            settings.FormClosed += (s, e) => { settings.Dispose(); settings = null; };
            settings.Show();
            settings.Activate();
        }
        static ZapretStrategy CurrentStrategy(List<ZapretStrategy> strategies)
        {
            string name = Settings.Get("zapret.strateji", "");
            return strategies.FirstOrDefault(s => s.Name == name) ?? strategies.FirstOrDefault();
        }

        public void StartZapret()
        {
            var strategy = CurrentStrategy(Zapret.LoadStrategies());
            if (strategy == null)
            {
                Notify(5000, "Zapret", "stratejiler.txt içinde strateji yok.", ToolTipIcon.Warning);
                return;
            }
            string filter = Settings.Get("zapret.filtre", Zapret.FilterList);
            bool autoDns = Settings.Get("zapret.sifreli_dns", "1") == "1";
            string note = "";
            RunBusy(() => "Zapret açık: " + strategy.Name + note, () =>
            {
                string error = Zapret.Start(strategy, filter);
                if (error != null || !autoDns) return error;

                // ISS engelli sitelerin şifresiz DNS sorgularını düşürüyor; zapret tek başına yetmez.
                var profiles = ProfileStore.Load();
                if (DnsManager.GetState(profiles).DohOn) return null;
                var dns = DnsManager.PickEncrypted(profiles, Settings.Get("zapret.dns", "AdGuard"));
                if (dns == null)
                    return "Zapret açık, ama erişilebilen şifreli DNS bulunamadı. Engelli sitelerin adı çözülmeyebilir.";
                note = "\nDNS: " + dns.Name + " (şifreli)";
                return DnsManager.Apply(dns);
            });
        }

        // ---- Güncellemeler (Windows Update düzeni) ----

        volatile bool updateChecking, updateInstalling;
        string updateError, updateMessage;

        public UpdateState Updates
        {
            get
            {
                return new UpdateState
                {
                    Checking = updateChecking,
                    Installing = updateInstalling,
                    Error = updateError,
                    Message = updateMessage,
                    ZapretCurrent = Zapret.CurrentVersion(),
                    ZapretLatest = Settings.Get("guncelleme.zapret_son", null),
                    LastCheck = UpdateState.LoadLastCheck()
                };
            }
        }

        public void CheckUpdates()
        {
            CheckUpdates(false);
        }

        // silent: açılıştaki otomatik denetim; yalnızca güncelleme varsa bildirim gösterir.
        void CheckUpdates(bool silent)
        {
            if (updateChecking || updateInstalling) return;
            updateChecking = true;
            updateError = null;
            updateMessage = null;
            NotifyChanged();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string latest = null, error = null;
                try
                {
                    latest = Zapret.LatestVersion();
                    UpdateState.SaveCheck(latest);
                }
                catch (Exception ex)
                {
                    error = "Güncelleme sunucusuna ulaşılamadı. İnternet bağlantınızı denetleyip yeniden deneyin. (" + ex.Message + ")";
                }
                invoker.BeginInvoke(new Action(() =>
                {
                    updateChecking = false;
                    updateError = silent ? null : error;
                    NotifyChanged();
                    if (silent && Updates.ZapretAvailable)
                        Balloon("Güncelleme kullanılabilir",
                                "Zapret motorunun yeni bir sürümü yayımlandı. Yüklemek için tıklayın.",
                                ToolTipIcon.Info, "Güncellemeler");
                }));
            });
        }

        public void InstallUpdates()
        {
            if (busy || updateInstalling || !Updates.ZapretAvailable) return;
            string latest = Updates.ZapretLatest;
            busy = true;
            updateInstalling = true;
            updateError = null;
            updateMessage = null;
            NotifyChanged();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null, warning = null;
                try
                {
                    bool wasRunning = Zapret.IsRunning();
                    Zapret.Install(latest);
                    if (wasRunning)
                        warning = Zapret.Start(CurrentStrategy(Zapret.LoadStrategies()),
                                               Settings.Get("zapret.filtre", Zapret.FilterList));
                }
                catch (Exception ex)
                {
                    error = "Güncelleme yüklenemedi. Önceki sürüm korunuyor. (" + ex.Message + ")";
                }
                invoker.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    updateInstalling = false;
                    updateError = error ?? (warning != null ? "Güncelleme yüklendi ancak Zapret yeniden başlatılamadı: " + warning : null);
                    updateMessage = error == null ? "Zapret motoru " + ShortSha(latest) + " sürümüne güncellendi." : null;
                    RefreshIcon();
                    NotifyChanged();
                    Balloon("Güncellemeler", Clip(updateError ?? updateMessage, 250),
                            updateError != null ? ToolTipIcon.Warning : ToolTipIcon.Info, "Güncellemeler");
                }));
            });
        }

        // Tüm bildirimler buradan geçer: uygulama logosuyla gösterilir; olmazsa standart balona düşer.
        void Notify(int timeout, string title, string text, ToolTipIcon icon)
        {
            if (!Notifications.Show(tray, title, text, timeout))
                tray.ShowBalloonTip(timeout, title, text, icon);
        }
        // Bildirime tıklanınca ilgili Ayarlar sayfası açılır.
        string balloonPage;

        void Balloon(string title, string text, ToolTipIcon icon, string page)
        {
            balloonPage = page;
            Notify(6000, title, text, icon);
        }
        static string ShortSha(string sha)
        {
            return sha.Length >= 7 ? sha.Substring(0, 7) : "bilinmiyor";
        }

        void RunBusy(string successText, Func<string> work)
        {
            RunBusy(() => successText, work);
        }

        void RunBusy(Func<string> successText, Func<string> work)
        {
            if (busy) return;
            busy = true;
            NotifyChanged();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                try { error = work(); }
                catch (Exception ex) { error = ex.Message; }
                invoker.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    RefreshIcon();
                    NotifyChanged();
                    if (error == null) Notify(3000, "DNS", successText(), ToolTipIcon.Info);
                    else Notify(8000, "DNS uyarısı", Clip(error, 250), ToolTipIcon.Warning);
                }));
            });
        }

        void SpeedTest(List<DnsProfile> profiles)
        {
            Notify(2000, "Hız testi", "Ölçülüyor…", ToolTipIcon.None);
            ThreadPool.QueueUserWorkItem(delegate
            {
                var state = DnsManager.GetState(profiles);
                var rows = profiles
                    .Select(p => new SpeedTestForm.Row
                    {
                        Name = p.Name,
                        Ms = DnsManager.Ping(p.V4[0]),
                        Current = state.IsStatic && state.Matched == p.Name
                    })
                    .OrderBy(r => r.Ms < 0 ? int.MaxValue : r.Ms)
                    .ToList();
                const string note = "Ölçüm şifresiz UDP 53 ile yapılır. ISS bu trafiğe müdahale ediyorsa sonuçlar yanıltıcı olabilir. Kalın yazılan, şu an kullandığın DNS.";
                invoker.BeginInvoke(new Action(() => new SpeedTestForm(rows, note).Show()));
            });
        }

        void ToggleAutostart()
        {
            string error = autostart == true ? Autostart.Disable() : Autostart.Enable();
            autostart = Autostart.IsEnabled();
            if (error != null) Notify(6000, "Başlangıç ayarı", Clip(error, 250), ToolTipIcon.Error);
        }

        void RefreshIcon()
        {
            var state = DnsManager.GetState(ProfileStore.Load());
            bool zapretOn = Zapret.IsRunning();
            Color color = !state.HasAdapter ? Color.FromArgb(220, 38, 38)   // kırmızı: bağlantı yok
                        : !state.IsStatic   ? Color.FromArgb(115, 115, 115) // gri: ISS DNS'i
                        : state.DohOn       ? Color.FromArgb(22, 163, 74)   // yeşil: şifreli
                        :                     Color.FromArgb(139, 92, 246); // mor: şifresiz özel (mavi logoda mavi seçilmiyordu)

            // Tepsinin o anki DPI'daki gerçek boyutunda çizilir (100%: 16, 125%: 20, 150%: 24 px); ölçeklenip bulanıklaşmaz.
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = Theme.Logo(size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Durum noktaları logodan oyulmuş bir boşlukla ayrılır: önce şeffaf halka, sonra nokta.
                Action<Color, float, float, float> badge = (c, cx, cy, d) =>
                {
                    float gap = Math.Max(1.5f, size / 12f);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (var clear = new SolidBrush(Color.Transparent))
                        g.FillEllipse(clear, cx - d / 2 - gap, cy - d / 2 - gap, d + 2 * gap, d + 2 * gap);
                    g.CompositingMode = CompositingMode.SourceOver;
                    using (var b = new SolidBrush(c))
                        g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
                };
                // Sağ altta tek durum noktası. Zapret çalışırken turuncu (açılırken DNS zaten şifreliye geçer);
                // bağlantı yoksa her durumda kırmızı. İki nokta 16 px'te D harfini kapatıyordu.
                float dot = size * 0.40f;
                Color dotColor = zapretOn && state.HasAdapter ? Color.FromArgb(245, 158, 11) : color;
                badge(dotColor, size - dot / 2, size - dot / 2, dot);

                IntPtr handle = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(handle);
                if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
                iconHandle = handle;
            }
            tray.Text = Clip(state.Summary + (zapretOn ? " · Zapret" : ""), 63);
        }

        void Quit()
        {
            Zapret.Stop();
            tray.Visible = false;
            tray.Dispose();
            if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
            ExitThread();
        }

        static string Clip(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }

    // Yönetici yetkisiyle sessizce başlamak için görev zamanlayıcı kullanılır (Run anahtarı UAC sorar).
    static class Autostart
    {
        // Store sürümü ayrı bir görev adı kullanır; klasik kurulumla çakışmaz.
        static readonly string TaskName = Settings.Packaged ? "DNSPilot" : "\"DNS Pilot\""; // klasik: Görev Zamanlayıcı'da "DNS Pilot"

        public static bool IsEnabled()
        {
            return DnsManager.Run("schtasks.exe", "/query /tn " + TaskName, null) == 0;
        }

        // Pakette exe yolu her güncellemede değişir; sabit uygulama takma adı (dnspilot.exe) kullanılır.
        static string LaunchPath()
        {
            if (!Settings.Packaged) return Application.ExecutablePath;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Microsoft", "WindowsApps", "dnspilot.exe");
        }

        public static string Enable()
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string exe = SecurityElement.Escape(LaunchPath());
            string xml =
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + @"</UserId></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>" + user + @"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>" + exe + @"</Command></Exec></Actions>
</Task>";
            string file = Path.Combine(Path.GetTempPath(), "DnsDegistirici-task.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            var log = new StringBuilder();
            DnsManager.Run("schtasks.exe", "/create /tn " + TaskName + " /xml \"" + file + "\" /f", log);
            File.Delete(file);
            return log.Length == 0 ? null : log.ToString();
        }

        public static string Disable()
        {
            var log = new StringBuilder();
            DnsManager.Run("schtasks.exe", "/delete /tn " + TaskName + " /f", log);
            return log.Length == 0 ? null : log.ToString();
        }
    }
}
