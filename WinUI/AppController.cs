using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DnsDegistirici;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace DnsPilot
{
    // Uygulamanın işleri (WinForms sürümündeki TrayContext'in arayüzden bağımsız karşılığı).
    // Uzun süren işler arka planda çalışır; sonuçlar StateChanged ile sayfalara ve tepsiye bildirilir.
    public sealed class AppController
    {
        public static AppController Instance { get; } = new AppController();

        DispatcherQueue ui;
        volatile bool busy;

        public event Action StateChanged;
        public Action<string, string, bool> Notify = (t, m, w) => { }; // başlık, metin, uyarı mı

        public bool Busy { get { return busy; } }

        public void Init(DispatcherQueue queue)
        {
            ui = queue;
            NetworkChange.NetworkAddressChanged += (s, e) => Changed();
        }

        void Changed()
        {
            ui.TryEnqueue(() => { if (StateChanged != null) StateChanged(); });
        }

        // Açılışta: zapret otomatik başlatma, günlük güncelleme denetimi.
        public void StartBackgroundWork()
        {
            ThreadPool.QueueUserWorkItem(delegate { Autostart.RepairIfStale(); });

            if (Settings.Get("zapret.otomatik", "1") == "1" && Zapret.Installed && !Zapret.IsRunning())
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    // Açılışta ağ birkaç saniye geç gelebilir; şifreli DNS testi boşa düşmesin.
                    for (int i = 0; i < 60 && !NetworkInterface.GetIsNetworkAvailable(); i++) Thread.Sleep(1000);
                    Thread.Sleep(3000);
                    ui.TryEnqueue(() => { if (!Zapret.IsRunning()) StartZapret(); });
                });
            }

            if (!Settings.Packaged && Settings.Get("guncelleme.otomatik", "1") == "1")
            {
                var last = UpdateState.LoadLastCheck();
                if (!last.HasValue || DateTime.Now - last.Value >= TimeSpan.FromDays(1))
                {
                    var timer = ui.CreateTimer();
                    timer.Interval = TimeSpan.FromMinutes(1);
                    timer.IsRepeating = false;
                    timer.Tick += (s, e) => CheckUpdates(true);
                    timer.Start();
                }
            }
        }

        public void Shutdown()
        {
            Zapret.Stop();
        }

        // ---- Genel arka plan işi ----

        void RunBusy(Func<string> successText, Func<string> work)
        {
            if (busy) return;
            busy = true;
            Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                try { error = work(); }
                catch (Exception ex) { error = ex.Message; }
                ui.TryEnqueue(() =>
                {
                    busy = false;
                    if (StateChanged != null) StateChanged();
                    if (error == null) Notify("DNS Pilot", successText(), false);
                    else Notify("DNS Pilot", Clip(error, 250), true);
                });
            });
        }

        public static string Clip(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        // ---- DNS ----

        public void ApplyProfile(DnsProfile profile)
        {
            RunBusy(() => profile.Name + " etkinleştirildi.", () => DnsManager.Apply(profile));
        }

        public void ResetDns()
        {
            RunBusy(() => "Otomatik DNS ayarına dönüldü.", DnsManager.ResetToDhcp);
        }

        public void FlushCache()
        {
            RunBusy(() => "DNS önbelleği temizlendi.", () => { DnsManager.FlushCache(); return null; });
        }

        public Task<List<SpeedResult>> MeasureSpeed()
        {
            return Task.Run(() =>
            {
                var profiles = ProfileStore.Load();
                var state = DnsManager.GetState(profiles);
                return profiles
                    .Select(p => new SpeedResult
                    {
                        Name = p.Name,
                        Ms = DnsManager.Ping(p.V4[0]),
                        Current = state.IsStatic && state.Matched == p.Name
                    })
                    .OrderBy(r => r.Ms < 0 ? int.MaxValue : r.Ms)
                    .ToList();
            });
        }

        // ---- Zapret ----

        public static ZapretStrategy CurrentStrategy(List<ZapretStrategy> strategies)
        {
            string name = Settings.Get("zapret.strateji", "");
            return strategies.FirstOrDefault(s => s.Name == name) ?? strategies.FirstOrDefault();
        }

        public void StartZapret()
        {
            var strategy = CurrentStrategy(Zapret.LoadStrategies());
            if (strategy == null)
            {
                Notify("Zapret", "Strateji listesinde tanımlı strateji bulunamadı.", true);
                return;
            }
            string filter = Settings.Get("zapret.filtre", Zapret.FilterList);
            bool autoDns = Settings.Get("zapret.sifreli_dns", "1") == "1";
            string note = "";
            RunBusy(() => "Zapret etkin: " + strategy.Name + note, () =>
            {
                string error = Zapret.Start(strategy, filter);
                if (error != null || !autoDns) return error;

                // Servis sağlayıcılar kısıtlı sitelerin şifresiz DNS sorgularını yanıtlamıyor; zapret tek başına yetmez.
                var profiles = ProfileStore.Load();
                if (DnsManager.GetState(profiles).DohOn) return null;
                var dns = DnsManager.PickEncrypted(profiles, Settings.Get("zapret.dns", "AdGuard"));
                if (dns == null)
                    return "Zapret etkin, ancak erişilebilen şifreli DNS sunucusu bulunamadı. Kısıtlı sitelerin adresleri çözülemeyebilir.";
                note = " · DNS: " + dns.Name + " (şifreli)";
                return DnsManager.Apply(dns);
            });
        }

        public void StopZapret()
        {
            RunBusy(() => "Zapret durduruldu.", () => { Zapret.Stop(); return null; });
        }

        // ---- En uygun stratejiyi ölçerek bulma ----

        volatile bool testRunning, testCancel;
        public bool StrategyTestRunning { get { return testRunning; } }
        public string StrategyTestStatus { get; private set; }

        void TestStatus(string text)
        {
            ui.TryEnqueue(() => { StrategyTestStatus = text; if (StateChanged != null) StateChanged(); });
        }

        public void FindStrategy()
        {
            if (testRunning) { testCancel = true; TestStatus("Durduruluyor…"); return; }
            if (busy || !Zapret.Installed) return;
            busy = true;
            testRunning = true;
            testCancel = false;
            StrategyTestStatus = "Hazırlanıyor…";
            Changed();

            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                bool warn = false;
                bool wasRunning = Zapret.IsRunning();
                var strategies = Zapret.LoadStrategies();
                var previous = CurrentStrategy(strategies);
                string filter = Settings.Get("zapret.filtre", Zapret.FilterList);
                try
                {
                    var targets = StrategyTest.Resolve();
                    if (targets.Count < StrategyTest.Hosts.Length)
                    {
                        TestStatus("Şifreli DNS yapılandırılıyor…");
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
                        throw new Exception("Test sitelerinin adresleri çözülemedi. Şifreli DNS sunucularına erişilemiyor olabilir.");

                    TestStatus("Zapret olmadan bağlantı deneniyor…");
                    Zapret.Stop();
                    Thread.Sleep(300);
                    if (StrategyTest.ProbeAll(targets).Count == targets.Count)
                    {
                        result = "Kısıtlı hizmetlere Zapret olmadan da erişilebiliyor. Bağlantınızda DPI kısıtlaması algılanmadı.";
                    }
                    else
                    {
                        var results = StrategyTest.Run(strategies, filter, targets,
                            (i, n, name) => TestStatus("Deneniyor (" + i + "/" + n + "): " + name),
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
                                   : "Hiçbir strateji kısıtlı hizmetlere erişemedi. \"Uygulanacak siteler\" ayarını \"Tüm siteler\" yapıp yeniden deneyin.";
                            warn = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result = ex.Message;
                    warn = true;
                }

                string restartError = null;
                if (wasRunning && previous != null) restartError = Zapret.Start(previous, filter);
                else Zapret.Stop();
                if (restartError != null) { result += " Zapret yeniden başlatılamadı: " + restartError; warn = true; }

                ui.TryEnqueue(() =>
                {
                    busy = false;
                    testRunning = false;
                    StrategyTestStatus = result;
                    if (StateChanged != null) StateChanged();
                    Notify("Strateji testi", Clip(result, 250), warn);
                });
            });
        }

        // ---- Zapret motoru yoksa tek tıkla kurulum ----
        // Kurulum programı motoru içerir; yine de silinmiş ya da eski bir kurulumdan kalmışsa buradan yüklenir.

        volatile bool engineInstalling;
        public bool EngineInstalling { get { return engineInstalling; } }

        public void InstallEngine()
        {
            if (busy || engineInstalling || Settings.Packaged) return;
            busy = true;
            engineInstalling = true;
            Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    string latest = Zapret.LatestVersion();
                    Zapret.Install(latest);
                    UpdateState.SaveCheck(latest);
                }
                catch (Exception ex)
                {
                    error = "Zapret motoru indirilemedi. İnternet bağlantınızı denetleyip yeniden deneyin. (" + ex.Message + ")";
                }
                ui.TryEnqueue(() =>
                {
                    busy = false;
                    engineInstalling = false;
                    if (StateChanged != null) StateChanged();
                    Notify("Zapret", error ?? "Zapret motoru yüklendi. Zapret'i şimdi etkinleştirebilirsiniz.", error != null);
                });
            });
        }
        // ---- Güncellemeler ----

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

        public void CheckUpdates() { CheckUpdates(false); }

        void CheckUpdates(bool silent)
        {
            if (updateChecking || updateInstalling) return;
            updateChecking = true;
            updateError = null;
            updateMessage = null;
            Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { UpdateState.SaveCheck(Zapret.LatestVersion()); }
                catch (Exception ex)
                {
                    error = "Güncelleme sunucusuna ulaşılamadı. İnternet bağlantınızı denetleyip yeniden deneyin. (" + ex.Message + ")";
                }
                ui.TryEnqueue(() =>
                {
                    updateChecking = false;
                    updateError = silent ? null : error;
                    if (StateChanged != null) StateChanged();
                    if (silent && Updates.ZapretAvailable)
                        Notify("Güncelleme kullanılabilir", "Zapret motorunun yeni bir sürümü yayımlandı. Ayarlar > Güncellemeler'den yükleyebilirsiniz.", false);
                });
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
            Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null, warning = null;
                try
                {
                    bool wasRunning = Zapret.IsRunning();
                    Zapret.Install(latest);
                    if (wasRunning)
                        warning = Zapret.Start(CurrentStrategy(Zapret.LoadStrategies()), Settings.Get("zapret.filtre", Zapret.FilterList));
                }
                catch (Exception ex)
                {
                    error = "Güncelleme yüklenemedi. Önceki sürüm korunuyor. (" + ex.Message + ")";
                }
                ui.TryEnqueue(() =>
                {
                    busy = false;
                    updateInstalling = false;
                    updateError = error ?? (warning != null ? "Güncelleme yüklendi ancak Zapret yeniden başlatılamadı: " + warning : null);
                    updateMessage = error == null ? "Zapret motoru " + ShortSha(latest) + " sürümüne güncellendi." : null;
                    if (StateChanged != null) StateChanged();
                    Notify("Güncellemeler", Clip(updateError ?? updateMessage, 250), updateError != null);
                });
            });
        }

        public static string ShortSha(string sha)
        {
            return sha != null && sha.Length >= 7 ? sha.Substring(0, 7) : "bilinmiyor";
        }

        // ---- Windows ile başlat ----

        public bool AutostartEnabled { get { return Autostart.IsEnabled(); } }

        public void SetAutostart(bool on)
        {
            string error = on ? Autostart.Enable() : Autostart.Disable();
            if (error != null) Notify("Başlangıç ayarı", Clip(error, 250), true);
            Changed();
        }
    }

    public class SpeedResult
    {
        public string Name;
        public int Ms;
        public bool Current;
    }

    // Yönetici yetkisiyle sessizce başlamak için görev zamanlayıcı (Run anahtarı her açılışta UAC sorar).
    static class Autostart
    {
        // Görev Zamanlayıcı'da görünen ad. Store sürümü ayrı ad kullanır; klasik kurulumla çakışmaz.
        static readonly string TaskName = Settings.Packaged ? "DNSPilot" : "DNS Pilot";
        static string TaskArg { get { return "\"" + TaskName + "\""; } }
        // 1.x (WinForms) sürümünün görev adı; ilk açılışta yeni ada taşınır.
        const string LegacyTaskName = "DnsDegistirici";

        public static bool IsEnabled()
        {
            return DnsManager.Run("schtasks.exe", "/query /tn " + TaskArg, null) == 0;
        }

        // Görev başka bir exe'yi (eski WinForms sürümü, taşınmış/yeniden kurulmuş kopya) çalıştırıyorsa
        // bu exe'ye sessizce yeniden yönlendirilir. Açılışta çağrılır.
        public static void RepairIfStale()
        {
            try
            {
                if (!Settings.Packaged && DnsManager.Run("schtasks.exe", "/query /tn " + LegacyTaskName, null) == 0)
                {
                    // Eski adlı görev: aynı ayarla yeni adla oluştur, eskisini sil.
                    if (Enable() == null) DnsManager.Run("schtasks.exe", "/delete /tn " + LegacyTaskName + " /f", null);
                    return;
                }
                if (!IsEnabled()) return;
                string text;
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/query /tn " + TaskArg + " /xml")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    text = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                }
                if (text.IndexOf(SecurityElement.Escape(LaunchPath()), StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("--tray", StringComparison.OrdinalIgnoreCase) >= 0) return;
                Enable();
            }
            catch (Exception) { }
        }

        static string LaunchPath()
        {
            if (!Settings.Packaged) return Environment.ProcessPath;
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
  <Actions Context=""Author""><Exec><Command>" + exe + @"</Command><Arguments>--tray</Arguments></Exec></Actions>
</Task>";
            string file = Path.Combine(Path.GetTempPath(), "DnsPilot-task.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            var log = new StringBuilder();
            DnsManager.Run("schtasks.exe", "/create /tn " + TaskArg + " /xml \"" + file + "\" /f", log);
            File.Delete(file);
            return log.Length == 0 ? null : log.ToString();
        }

        public static string Disable()
        {
            var log = new StringBuilder();
            DnsManager.Run("schtasks.exe", "/delete /tn " + TaskArg + " /f", log);
            return log.Length == 0 ? null : log.ToString();
        }
    }

    // Bildirim kimliği: paketlenmemiş uygulamada başlıktaki ad/simge, bu AppUserModelID'yi taşıyan
    // Başlat menüsü kısayolundan okunur (WinForms sürümündeki Notifications.RegisterIdentity ile aynı).
    static class Identity
    {
        public const string AppId = "DnsPilot.Desktop";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        public static void Register()
        {
            if (Settings.Packaged) return;
            try
            {
                string logo = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
                {
                    key.SetValue("DisplayName", "DNS Pilot");
                    key.SetValue("IconUri", logo);
                }
                SetCurrentProcessExplicitAppUserModelID(AppId);

                string exe = Environment.ProcessPath;
                string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DNS Pilot.lnk");
                string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DNS Pilot.lnk");
                if (File.Exists(common))
                {
                    Shortcut.Write(common, null, AppId);
                    if (File.Exists(user)) File.Delete(user);
                }
                else Shortcut.Write(user, exe, AppId);
            }
            catch (Exception) { }
        }
    }
}
