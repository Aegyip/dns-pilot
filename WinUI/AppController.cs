using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // Arayüzün gösterdiği durum. Arka planda tek seferde hesaplanır; sayfalar ve tepsi yalnızca bunu okur,
    // arayüz iş parçacığında dosya, kayıt defteri, süreç ya da ağ taraması yapılmaz.
    public sealed class Snapshot
    {
        public List<DnsProfile> Profiles = new List<DnsProfile>();
        public DnsState Dns = new DnsState { Summary = "Durum okunuyor…" };
        public bool ZapretRunning, ZapretInstalled, EngineRemoved, BypassRunning;
        public bool AccessOn { get { return ZapretRunning || BypassRunning; } }
        public List<ZapretStrategy> Strategies = new List<ZapretStrategy>();
        public ZapretStrategy Strategy;
        public string ZapretVersion = "";
        public bool? Autostart; // null: henüz okunmadı
    }

    // Uygulamanın işleri. Sistemi değiştiren her iş (DNS, Zapret, strateji testi, motor kurulumu, başlangıç görevi)
    // tek bir iş parçacığında sırayla çalışır:
    //  - Meşgulken yapılan tıklamalar artık yok sayılmaz, sıraya girer. Aynı türden bekleyen iş varsa en son istenen
    //    geçerli olur (ör. Zapret'i art arda aç/kapat → yalnızca son istek).
    //  - Sayfalar ve tepsi, çalışan işi ve adımını ("Şifreli DNS sunucusu aranıyor…") gösterir.
    //  - Dış komutlar ve ağ denemeleri zaman sınırlıdır; bir iş takılıp tüm uygulamayı kilitleyemez.
    public sealed class AppController
    {
        public static AppController Instance { get; } = new AppController();

        DispatcherQueue ui;

        public event Action StateChanged;
        public Action<string, string, bool> Notify = (t, m, w) => { }; // başlık, metin, uyarı mı

        // Son Zapret hatası. Tepsi bildirimi kolayca kaçırılır (bildirimler kapalı, odak modu); sayfalarda da gösterilir.
        public string ZapretError { get; private set; }

        public void ClearZapretError()
        {
            ZapretError = null;
            RaiseUi();
        }

        void SetZapretError(string error)
        {
            ZapretError = error;
            if (error != null) Diag.Log("Zapret: " + error.Replace("\n", " | "));
        }

        public void Init(DispatcherQueue queue)
        {
            ui = queue;
            refreshTimer = new Timer(RefreshCallback, null, Timeout.Infinite, Timeout.Infinite);
            // İlk görüntü hemen (başlangıç görevi sorgusu hariç; o arka planda gelir).
            snapshot = Compute(snapshot, false);
            // Ağ değişiklikleri seri gelir (VPN, Hyper-V, Wi-Fi geçişi, DNS değişikliğinin kendisi): tek yenilemede toplanır.
            NetworkChange.NetworkAddressChanged += (s, e) => RequestRefresh(600);
            NetworkChange.NetworkAvailabilityChanged += (s, e) => RequestRefresh(600);
            RequestRefresh(0);
        }

        // ---- Durum ----

        volatile Snapshot snapshot = new Snapshot();
        public Snapshot State { get { return snapshot; } }

        Timer refreshTimer;
        int refreshRunning;
        volatile bool refreshDirty;

        public void RequestRefresh(int delayMs = 200)
        {
            if (refreshTimer != null) refreshTimer.Change(Math.Max(0, delayMs), Timeout.Infinite);
        }

        void RefreshCallback(object _)
        {
            if (Interlocked.Exchange(ref refreshRunning, 1) == 1) { refreshDirty = true; return; }
            try
            {
                do
                {
                    refreshDirty = false;
                    var sw = Stopwatch.StartNew();
                    snapshot = Compute(snapshot, true);
                    if (sw.ElapsedMilliseconds > 2000) Diag.Log("durum okuması yavaş: " + sw.ElapsedMilliseconds + " ms");
                    RaiseUi();
                } while (refreshDirty);
            }
            catch (Exception ex) { Diag.Error("durum yenileme", ex); }
            finally { Interlocked.Exchange(ref refreshRunning, 0); }
        }

        // Her parça ayrı korunur: biri okunamazsa önceki değeri kalır, görüntünün geri kalanı güncellenir.
        static Snapshot Compute(Snapshot prev, bool withAutostart)
        {
            var s = new Snapshot();
            try { s.Profiles = ProfileStore.Load(); } catch (Exception ex) { s.Profiles = prev.Profiles; Diag.Error("profiller", ex); }
            try { s.Dns = DnsManager.GetState(s.Profiles); } catch (Exception ex) { s.Dns = prev.Dns; Diag.Error("DNS durumu", ex); }
            try { s.ZapretRunning = Zapret.IsRunning(); } catch (Exception) { s.ZapretRunning = prev.ZapretRunning; }
            s.BypassRunning = Instance.Bypass.Running;
            // Discord kapanırken ayarlarını yeniden yazar; erişim açıkken Discord uyumluluk ayarı korunur.
            if (s.BypassRunning && DiscordCompat) try { DiscordFix.Ensure(m => Diag.Log("erişim: " + m)); } catch (Exception) { }
            try { s.ZapretInstalled = Zapret.Installed; s.EngineRemoved = Zapret.EngineRemoved; }
            catch (Exception) { s.ZapretInstalled = prev.ZapretInstalled; s.EngineRemoved = prev.EngineRemoved; }
            try { s.Strategies = Zapret.LoadStrategies(); } catch (Exception ex) { s.Strategies = prev.Strategies; Diag.Error("stratejiler", ex); }
            s.Strategy = CurrentStrategy(s.Strategies);
            s.ZapretVersion = Zapret.CurrentVersion();
            s.Autostart = prev.Autostart;
            if (withAutostart)
                try { s.Autostart = Autostart.IsEnabled(); } catch (Exception) { }
            return s;
        }

        // Abonelerin her biri ayrı korunur: tepsideki bir hata (ör. Explorer meşgul) sayfaların güncellenmesini engellemez,
        // uygulamayı da çökertmez.
        void RaiseUi()
        {
            if (ui == null) return;
            ui.TryEnqueue(() =>
            {
                var handlers = StateChanged;
                if (handlers == null) return;
                foreach (Action h in handlers.GetInvocationList())
                {
                    try { h(); }
                    catch (Exception ex) { Diag.Error("arayüz güncelleme (" + h.Method.DeclaringType?.Name + ")", ex); }
                }
            });
        }

        void OnUi(Action action)
        {
            if (ui == null) return;
            ui.TryEnqueue(() =>
            {
                try { action(); } catch (Exception ex) { Diag.Error("arayüz", ex); }
            });
        }

        // ---- İş kuyruğu ----

        sealed class Job
        {
            public string Kind;             // aynı türden bekleyen iş yenisiyle değiştirilir (null: değiştirilmez)
            public string Title;            // "Zapret başlatılıyor"
            public Func<string> Work;       // hata yoksa null, varsa hata metni
            public Func<string> Success;    // başarı bildirimi (null: bildirim yok)
            public string NotifyTitle = "DNS Pilot";
            public bool? ZapretTarget;      // iş bitince Zapret açık mı olacak
            public string DnsTarget;        // iş bitince seçili olacak DNS profili ("" = Otomatik)
            public Action<string> Done;     // arayüz iş parçacığında, hata metniyle
        }

        readonly object qlock = new object();
        readonly List<Job> queue = new List<Job>();
        Job current;
        string step;
        Thread worker;

        void Enqueue(Job job)
        {
            lock (qlock)
            {
                if (job.Kind != null)
                {
                    int removed = queue.RemoveAll(j => j.Kind == job.Kind);
                    if (removed > 0) Diag.Log("sıradaki iş yenisiyle değişti: " + job.Title);
                }
                queue.Add(job);
                if (worker == null)
                {
                    worker = new Thread(WorkLoop) { IsBackground = true, Name = "DNS Pilot işleri" };
                    worker.Start();
                }
            }
            RaiseUi();
        }

        void WorkLoop()
        {
            while (true)
            {
                Job job;
                lock (qlock)
                {
                    if (queue.Count == 0) { worker = null; current = null; step = null; break; }
                    job = queue[0];
                    queue.RemoveAt(0);
                    current = job;
                    step = null;
                }
                RaiseUi();

                var sw = Stopwatch.StartNew();
                string error;
                try { error = job.Work(); }
                catch (Exception ex)
                {
                    Diag.Error(job.Title, ex);
                    error = ex.Message;
                }
                Diag.Log("iş: " + job.Title + " · " + sw.ElapsedMilliseconds + " ms" +
                         (error != null ? " · hata: " + error.Replace("\n", " | ") : ""));

                string success = null;
                if (error == null && job.Success != null)
                    try { success = job.Success(); } catch (Exception) { }
                lock (qlock) current = null;
                snapshot = Compute(snapshot, false); // sonuç hemen görünsün
                OnUi(() =>
                {
                    if (job.Done != null) job.Done(error);
                    if (error != null) Notify(job.NotifyTitle, Clip(error, 250), true);
                    else if (success != null) Notify(job.NotifyTitle, success, false);
                });
            }
            RaiseUi();
            RequestRefresh(300);
        }

        void Step(string text)
        {
            lock (qlock) step = text;
            RaiseUi();
        }

        public bool Busy
        {
            get { lock (qlock) return current != null || queue.Count > 0; }
        }

        // "Zapret başlatılıyor: şifreli DNS sunucusu aranıyor… · sırada 1 iş"; boşta null.
        public string Activity
        {
            get
            {
                lock (qlock)
                {
                    if (current == null && queue.Count == 0) return null;
                    string text = current != null ? current.Title + (step != null ? ": " + step : "…") : "Başlıyor…";
                    if (queue.Count > 0) text += " · sırada " + queue.Count + " iş";
                    return text;
                }
            }
        }

        // Sırada ya da çalışmakta olan Zapret aç/kapat isteğinin hedefi (anahtar bunu gösterir, geri sıçramaz).
        public bool? PendingZapret
        {
            get
            {
                lock (qlock)
                {
                    for (int i = queue.Count - 1; i >= 0; i--) if (queue[i].ZapretTarget.HasValue) return queue[i].ZapretTarget;
                    return current != null ? current.ZapretTarget : null;
                }
            }
        }

        public string PendingDns
        {
            get
            {
                lock (qlock)
                {
                    for (int i = queue.Count - 1; i >= 0; i--) if (queue[i].DnsTarget != null) return queue[i].DnsTarget;
                    return current != null ? current.DnsTarget : null;
                }
            }
        }

        public static string Clip(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        // ---- Yönetici izni ----
        // Uygulama normalde her açılışta yönetici izni ister. Yine de izinsiz çalışıyorsa (ör. kısayol/uyumluluk ayarı,
        // standart kullanıcı hesabı) DNS ve Zapret işleri anlaşılmaz Windows hatalarıyla düşmek yerine baştan durdurulur
        // ve sayfalarda "Yönetici olarak yeniden başlat" gösterilir.

        public static readonly bool Elevated = Zapret.IsElevated;

        const string DnsElevationMessage =
            "DNS ayarlarını değiştirmek yönetici izni gerektirir, ancak DNS Pilot yönetici izniyle çalışmıyor. " +
            "\"Yönetici olarak yeniden başlat\" düğmesini kullanın.";

        // Yönetici izniyle yeni bir kopya açar ve bu kopyayı kapatır. İzin isteği reddedilirse hiçbir şey değişmez.
        public void RestartElevated()
        {
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath, "--yeniden") { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // kullanıcı "Hayır" dedi
            {
                return;
            }
            Diag.Log("yönetici olarak yeniden başlatılıyor");
            App.Instance.ExitApp();
        }

        // ---- Açılış ve kapanış ----

        public void StartBackgroundWork()
        {
            if (!Elevated)
            {
                // İzinsiz kopya başlangıç görevini kendi yoluna yönlendirmeye, Zapret'i başlatmaya ya da DNS'i geri
                // almaya çalışmaz; bunların hepsi zaten başarısız olur.
                Diag.Log("yönetici izni yok: arka plan işleri atlandı");
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Autostart.RepairIfStale(); } catch (Exception ex) { Diag.Error("başlangıç görevi onarımı", ex); }
                RequestRefresh(0);
            });

            bool zapretMethod = Method == MethodZapret;
            bool autoAccess = Settings.Get("zapret.otomatik", "1") == "1";
            if (autoAccess && !(zapretMethod && Zapret.IsRunning()))
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    // Açılışta ağ birkaç saniye geç gelebilir; şifreli DNS testi boşa düşmesin.
                    for (int i = 0; i < 60 && !NetworkInterface.GetIsNetworkAvailable(); i++) Thread.Sleep(1000);
                    if (zapretMethod) Thread.Sleep(3000);
                    if (!Zapret.IsRunning() && !Bypass.Running) StartAccess();
                });
            }
            else if (!autoAccess)
            {
                // Uygulama erişim açıkken kapanmış/çökmüş olabilir: kalan ad çözümleme kuralı ve Zapret'in değiştirdiği DNS
                // geri alınır. Kural kalırsa listedeki sitelere (Discord) uygulama açılana kadar ulaşılamaz.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { if (Nrpt.Present()) { Nrpt.RemoveAll(); DnsManager.FlushCache(); Diag.Log("kalan ad çözümleme kuralı silindi"); } }
                    catch (Exception ex) { Diag.Error("NRPT temizliği", ex); }
                });
                if (SavedDns != null && !Zapret.IsRunning())
                    Enqueue(new Job { Title = "Önceki DNS ayarına dönülüyor", Work = RestoreSavedDns });
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

        // Çıkışta Zapret durur, değiştirdiği DNS geri alınır. Sınırlı süre beklenir; takılırsa çıkış yine olur.
        public void Shutdown()
        {
            var t = Task.Run(() =>
            {
                try
                {
                    if (Bypass.Running) Bypass.Stop();
                    Zapret.Stop();
                    RestoreSavedDns();
                }
                catch (Exception ex) { Diag.Error("kapanış", ex); }
            });
            if (!t.Wait(15000)) Diag.Log("kapanış işleri 15 sn içinde bitmedi");
        }

        // ---- DNS ----

        public void ApplyProfile(DnsProfile profile)
        {
            Enqueue(new Job
            {
                Kind = "dns",
                Title = profile.Name + " uygulanıyor",
                DnsTarget = profile.Name,
                Work = () =>
                {
                    Step("sunucu deneniyor");
                    if (!Elevated) return DnsElevationMessage;
                    ForgetSavedDns(); // kullanıcı DNS'i kendisi seçti; Zapret dururken geri alınmasın
                    return DnsManager.Apply(profile);
                },
                Success = () => profile.Name + " etkinleştirildi.",
            });
        }

        public void ResetDns()
        {
            Enqueue(new Job
            {
                Kind = "dns",
                Title = "Otomatik DNS ayarına dönülüyor",
                DnsTarget = "",
                Work = () => { if (!Elevated) return DnsElevationMessage; ForgetSavedDns(); return DnsManager.ResetToDhcp(); },
                Success = () => "Otomatik DNS ayarına dönüldü.",
            });
        }

        public void FlushCache()
        {
            Enqueue(new Job
            {
                Kind = "flush",
                Title = "DNS önbelleği temizleniyor",
                Work = () => { DnsManager.FlushCache(); return null; },
                Success = () => "DNS önbelleği temizlendi.",
            });
        }

        public void SetUdpFallback(bool on)
        {
            Settings.Set("dns.udp_yedek", on ? "1" : "0");
            // Şifreli bir profil kullanılıyorsa yeni ayar hemen uygulansın.
            var s = snapshot;
            var active = s.Dns.DohOn ? s.Profiles.FirstOrDefault(p => p.Name == s.Dns.Matched && p.Doh != null) : null;
            if (active != null)
                Enqueue(new Job
                {
                    Kind = "dns",
                    Title = active.Name + " yeniden uygulanıyor",
                    DnsTarget = active.Name,
                    Work = () => Elevated ? DnsManager.Apply(active) : DnsElevationMessage,
                });
            else RaiseUi();
        }

        public Task<List<SpeedResult>> MeasureSpeed()
        {
            return Task.Run(() =>
            {
                var s = snapshot;
                // Sunucular paralel ölçülür (sırayla ölçüm, yanıt vermeyen sunucularda 20 sn'yi buluyordu).
                var tasks = s.Profiles.Select(p => Task.Run(() =>
                {
                    int ms;
                    try { ms = DnsManager.Ping(p.V4[0]); } catch (Exception) { ms = -1; }
                    return new SpeedResult { Name = p.Name, Ms = ms, Current = s.Dns.IsStatic && s.Dns.Matched == p.Name };
                })).ToArray();
                Task.WaitAll(tasks, 15000);
                return tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result)
                            .OrderBy(r => r.Ms < 0 ? int.MaxValue : r.Ms).ToList();
            });
        }

        // ---- Zapret'in değiştirdiği DNS'i geri alma ----
        // Zapret açılırken şifreli DNS'e geçildiyse önceki ayar saklanır; Zapret durunca ya da uygulamadan çıkılınca
        // geri yüklenir. Önceden DNS "yalnızca şifreli" modda kalıyordu: Zapret kapalıyken servis sağlayıcı DoH'yi
        // kesince internet yavaşlıyor ya da kesiliyordu.

        const string SavedDnsKey = "zapret.onceki_dns";

        static string SavedDns { get { return Settings.Get(SavedDnsKey, null); } }

        public static bool RestoreDnsOnStop
        {
            get { return Settings.Get("dns.geri_al", "1") == "1"; }
        }

        static void ForgetSavedDns() { Settings.Set(SavedDnsKey, null); }

        static string RestoreSavedDns()
        {
            string saved = SavedDns;
            if (saved == null) return null;
            if (!Elevated) return DnsElevationMessage; // kayıt silinmez; yönetici izniyle açılınca geri alınır
            ForgetSavedDns();
            if (!RestoreDnsOnStop) return null;
            Diag.Log("önceki DNS geri yükleniyor: " + saved);
            return DnsManager.Restore(saved);
        }

        // Zapret açıkken kısıtlı sitelerin adları çözülebilsin diye şifreli DNS'e geçer. Hata/uyarı metni döner.
        string EnsureEncryptedDns(Action<string> onSwitched)
        {
            var profiles = ProfileStore.Load();
            if (DnsManager.GetState(profiles).DohOn) return null;
            string before = DnsManager.Describe();
            Step("şifreli DNS sunucusu aranıyor");
            var dns = DnsManager.PickEncrypted(profiles, Settings.Get("zapret.dns", "Cloudflare"));
            if (dns == null)
                return "Zapret etkin, ancak erişilebilen şifreli DNS sunucusu bulunamadı. Kısıtlı sitelerin adresleri çözülemeyebilir.";
            Step(dns.Name + " uygulanıyor");
            string error = DnsManager.Apply(dns, true);
            if (SavedDns == null && before != null) Settings.Set(SavedDnsKey, before);
            if (onSwitched != null) onSwitched(dns.Name);
            return error;
        }

        // ---- Erişim: sürücüsüz şeffaf mod (varsayılan) ya da Zapret ----
        // Store paketi sürücü içeremediği için varsayılan yöntem sürücüsüz moddur (src\Bypass.cs). Zapret yalnızca
        // motoru kuruluysa (GitHub kurulumu) seçilebilir. İkisi aynı anda çalışmaz.

        public const string MethodBypass = "surucusuz", MethodZapret = "zapret";
        public readonly Bypass Bypass = new Bypass();

        public static string Method
        {
            get
            {
                string m = Settings.Get("erisim.yontem", MethodBypass);
                return m == MethodZapret && Zapret.Installed ? MethodZapret : MethodBypass;
            }
        }

        // Discord masaüstü uygulamasının güncelleme kontrolü sürücüsüz yolla geçemez (TLS 1.2, bkz. DiscordFix);
        // bu seçenek Discord'un kendi "güncelleme başarısızsa yine de aç" ayarını yazar.
        public static bool DiscordCompat { get { return Settings.Get("erisim.discord", "1") == "1"; } }

        public void SetDiscordCompat(bool on)
        {
            Settings.Set("erisim.discord", on ? "1" : "0");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (on && Bypass.Running) { DiscordFix.Ensure(m => Diag.Log("erişim: " + m)); DiscordFix.StartWatching(m => Diag.Log("erişim: " + m)); }
                    else if (!on) { DiscordFix.StopWatching(); DiscordFix.Remove(m => Diag.Log("erişim: " + m)); }
                }
                catch (Exception ex) { Diag.Error("Discord ayarı", ex); }
            });
        }

        public void StartAccess()
        {
            if (Method == MethodZapret) StartZapret(); else StartBypass();
        }

        // Tek iş: hem sürücüsüz mod hem Zapret durur (aynı türdeki işler kuyrukta birbirinin yerine geçer).
        public void StopAccess() { StopZapret(); }

        // Yöntem değişti: erişim açıksa yeni yöntemle yeniden başlatılır (başlatan iş diğer yöntemi kendisi durdurur).
        public void AccessMethodChanged()
        {
            bool on = PendingZapret ?? snapshot.AccessOn;
            if (on) StartAccess(); else RequestRefresh(0);
        }

        public void StartBypass()
        {
            Enqueue(new Job
            {
                Kind = "zapret",
                Title = "Erişim başlatılıyor",
                NotifyTitle = "Erişim",
                ZapretTarget = true,
                Work = () =>
                {
                    if (!Elevated) return "Sürücüsüz erişim yönetici izni gerektirir.";
                    if (Zapret.IsRunning()) { Step("Zapret durduruluyor"); Zapret.Stop(); }
                    Bypass.Log = m => Diag.Log("erişim: " + m);
                    Bypass.Verbose = Settings.Get("erisim.kayit", "0") == "1";
                    Bypass.Hosts = () => { Zapret.EnsureUserFiles(); return File.ReadAllLines(Zapret.HostlistPath); };
                    Bypass.SplitMode mode;
                    Bypass.Mode = Enum.TryParse(Settings.Get("erisim.bolme", "TlsRecord"), out mode) ? mode : Bypass.SplitMode.TlsRecord;
                    if (Bypass.Running) Bypass.Stop();
                    Step("ad çözümleme kuralı ekleniyor");
                    string error = Bypass.Start();
                    if (error == null && DiscordCompat) { DiscordFix.Ensure(m => Diag.Log("erişim: " + m)); DiscordFix.StartWatching(m => Diag.Log("erişim: " + m)); }
                    SetZapretError(error);
                    if (error != null) Diag.Log("erişim: " + error);
                    return error;
                },
                Success = () => "Erişim etkin. Discord'u yeniden başlatmanız gerekebilir.",
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
            string note = "";
            string name = "";
            Enqueue(new Job
            {
                Kind = "zapret",
                Title = "Zapret başlatılıyor",
                NotifyTitle = "Zapret",
                ZapretTarget = true,
                Work = () =>
                {
                    var strategy = CurrentStrategy(Zapret.LoadStrategies());
                    if (strategy == null) return "Strateji listesinde tanımlı strateji bulunamadı.";
                    name = strategy.Name;
                    if (Bypass.Running) { Step("sürücüsüz erişim durduruluyor"); Bypass.Stop(); DiscordFix.StopWatching(); }
                    else if (Nrpt.Present()) Nrpt.RemoveAll(); // önceki oturumdan (çökme) kalan kural
                    string filter = Settings.Get("zapret.filtre", Zapret.FilterList);
                    Step(strategy.Name);
                    string error;
                    try { error = Zapret.Start(strategy, filter); }
                    catch (Exception ex) { error = "Zapret başlatılamadı: " + ex.Message; }
                    SetZapretError(error);
                    if (error != null || Settings.Get("zapret.sifreli_dns", "1") != "1") return error;
                    // Servis sağlayıcılar kısıtlı sitelerin şifresiz DNS sorgularını yanıtlamıyor; zapret tek başına yetmez.
                    return EnsureEncryptedDns(dns => note = " · DNS: " + dns + " (şifreli)");
                },
                Success = () => "Zapret etkin: " + name + note,
            });
        }

        public void StopZapret()
        {
            ZapretError = null;
            bool bypass = Bypass.Running;
            Enqueue(new Job
            {
                Kind = "zapret",
                Title = bypass ? "Erişim durduruluyor" : "Zapret durduruluyor",
                NotifyTitle = bypass ? "Erişim" : "Zapret",
                ZapretTarget = false,
                Work = () =>
                {
                    if (Bypass.Running) { Bypass.Stop(); DiscordFix.StopWatching(); DiscordFix.Remove(m => Diag.Log("erişim: " + m)); }
                    Zapret.Stop();
                    if (SavedDns != null) Step("önceki DNS ayarına dönülüyor");
                    return RestoreSavedDns();
                },
                Success = () => bypass ? "Erişim durduruldu." : "Zapret durduruldu.",
            });
        }

        // Strateji ya da filtre değişti: Zapret açıksa (veya açılmak üzereyse) yeni ayarla yeniden başlatılır.
        public void ZapretSettingsChanged()
        {
            bool on = PendingZapret ?? snapshot.ZapretRunning;
            if (on) StartZapret();
            else RequestRefresh(0);
        }

        // ---- En uygun stratejiyi ölçerek bulma ----

        volatile bool testRunning, testCancel;
        public bool StrategyTestRunning { get { return testRunning; } }
        public string StrategyTestStatus { get; private set; }

        void TestStatus(string text)
        {
            OnUi(() => { StrategyTestStatus = text; RaiseUi(); });
            Step(text);
        }

        public void FindStrategy()
        {
            if (testRunning) { testCancel = true; TestStatus("Durduruluyor…"); return; }
            if (!snapshot.ZapretInstalled) return;
            if (!Elevated) { StrategyTestStatus = Zapret.ElevationMessage; RaiseUi(); return; }
            testRunning = true;
            testCancel = false;
            StrategyTestStatus = "Sırada…";
            string result = null;
            bool warn = false;

            Enqueue(new Job
            {
                Kind = "test",
                Title = "Strateji testi",
                NotifyTitle = "Strateji testi",
                Work = () =>
                {
                    result = RunStrategyTest(out warn);
                    return null;
                },
                Done = error =>
                {
                    testRunning = false;
                    StrategyTestStatus = error ?? result;
                    if (error == null) Notify("Strateji testi", Clip(result, 250), warn);
                    RaiseUi();
                },
            });
            RaiseUi();
        }

        string RunStrategyTest(out bool warn)
        {
            warn = false;
            string result;
            bool wasRunning = Zapret.IsRunning();
            var strategies = Zapret.LoadStrategies();
            var previous = CurrentStrategy(strategies);
            string filter = Settings.Get("zapret.filtre", Zapret.FilterList);
            try
            {
                TestStatus("Hazırlanıyor…");
                var targets = StrategyTest.Resolve();
                if (targets.Count < StrategyTest.Hosts.Length)
                {
                    TestStatus("Şifreli DNS yapılandırılıyor…");
                    if (previous != null) Zapret.Start(previous, filter);
                    EnsureEncryptedDns(null);
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
                Diag.Error("strateji testi", ex);
                result = ex.Message;
                warn = true;
            }

            try
            {
                string restartError = null;
                if (wasRunning && previous != null) restartError = Zapret.Start(previous, filter);
                else { Zapret.Stop(); RestoreSavedDns(); }
                if (restartError != null) { result += " Zapret yeniden başlatılamadı: " + restartError; warn = true; }
            }
            catch (Exception ex)
            {
                result += " Zapret yeniden başlatılamadı: " + ex.Message;
                warn = true;
            }
            return result;
        }

        // ---- Zapret motoru yoksa tek tıkla kurulum ----
        // Kurulum programı motoru içerir; yine de silinmiş ya da eski bir kurulumdan kalmışsa buradan yüklenir.

        volatile bool engineInstalling;
        public bool EngineInstalling { get { return engineInstalling; } }

        public void InstallEngine()
        {
            if (engineInstalling || Settings.Packaged) return;
            engineInstalling = true;
            Enqueue(new Job
            {
                Kind = "engine",
                Title = "Zapret motoru yükleniyor",
                NotifyTitle = "Zapret",
                Work = () =>
                {
                    string error = null;
                    try
                    {
                        Step("indiriliyor");
                        string latest = Zapret.LatestVersion();
                        Zapret.Install(latest);
                        UpdateState.SaveCheck(latest);
                    }
                    catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException)
                    {
                        error = "Zapret motoru indirilemedi. İnternet bağlantınızı denetleyip yeniden deneyin. (" + ex.Message + ")";
                    }
                    catch (Exception ex)
                    {
                        // Antivirüs çıkarılan dosyayı engellediğinde de buraya düşülür (ör. "dosya virüs içeriyor").
                        error = "Zapret motoru yüklenemedi: " + ex.Message;
                        if (ex.Message.IndexOf(Zapret.AntivirusHint, StringComparison.Ordinal) < 0)
                            error += "\n" + Zapret.AntivirusHint + Zapret.EngineDir;
                    }
                    SetZapretError(error);
                    return error;
                },
                Success = () => "Zapret motoru yüklendi. Zapret'i şimdi etkinleştirebilirsiniz.",
                Done = e => engineInstalling = false,
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
                    ZapretCurrent = snapshot.ZapretVersion,
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
            RaiseUi();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { UpdateState.SaveCheck(Zapret.LatestVersion()); }
                catch (Exception ex)
                {
                    error = "Güncelleme sunucusuna ulaşılamadı. İnternet bağlantınızı denetleyip yeniden deneyin. (" + ex.Message + ")";
                }
                OnUi(() =>
                {
                    updateChecking = false;
                    updateError = silent ? null : error;
                    RaiseUi();
                    if (silent && Updates.ZapretAvailable)
                        Notify("Güncelleme kullanılabilir", "Zapret motorunun yeni bir sürümü yayımlandı. Ayarlar > Güncellemeler'den yükleyebilirsiniz.", false);
                });
            });
        }

        public void InstallUpdates()
        {
            if (updateInstalling || !Updates.ZapretAvailable) return;
            string latest = Updates.ZapretLatest;
            updateInstalling = true;
            updateError = null;
            updateMessage = null;
            string warning = null;
            Enqueue(new Job
            {
                Kind = "engine",
                Title = "Zapret motoru güncelleniyor",
                NotifyTitle = "Güncellemeler",
                Work = () =>
                {
                    try
                    {
                        bool wasRunning = Zapret.IsRunning();
                        Step("indiriliyor");
                        Zapret.Install(latest);
                        var strategy = CurrentStrategy(Zapret.LoadStrategies());
                        if (wasRunning && strategy != null)
                            warning = Zapret.Start(strategy, Settings.Get("zapret.filtre", Zapret.FilterList));
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return "Güncelleme yüklenemedi. Önceki sürüm korunuyor. (" + ex.Message + ")";
                    }
                },
                Done = error =>
                {
                    updateInstalling = false;
                    updateError = error ?? (warning != null ? "Güncelleme yüklendi ancak Zapret yeniden başlatılamadı: " + warning : null);
                    updateMessage = error == null ? "Zapret motoru " + ShortSha(latest) + " sürümüne güncellendi." : null;
                    if (error == null) Notify("Güncellemeler", Clip(updateError ?? updateMessage, 250), updateError != null);
                    RaiseUi();
                },
            });
        }

        public static string ShortSha(string sha)
        {
            return sha != null && sha.Length >= 7 ? sha.Substring(0, 7) : "bilinmiyor";
        }

        // ---- Windows ile başlat ----

        public bool? AutostartEnabled { get { return snapshot.Autostart; } }

        public void SetAutostart(bool on)
        {
            Enqueue(new Job
            {
                Kind = "autostart",
                Title = on ? "Başlangıç görevi oluşturuluyor" : "Başlangıç görevi kaldırılıyor",
                NotifyTitle = "Başlangıç ayarı",
                Work = () =>
                {
                    string error = on ? Autostart.Enable() : Autostart.Disable();
                    snapshot.Autostart = Autostart.IsEnabled();
                    return error;
                },
            });
        }

        // ---- Tanılama ----

        // Hata bildirimine eklenmek üzere: sürüm, ağ ve DNS durumu, ayarlar, son kayıt satırları. Kişisel veri içermez
        // (kullanıcı adı ve klasör yolları temizlenir); kullanıcı yapıştırmadan önce görebilir.
        public Task<string> DiagnosticsText()
        {
            return Task.Run(() =>
            {
                var sb = new StringBuilder();
                var s = snapshot;
                sb.AppendLine("### DNS Pilot tanılama");
                sb.AppendLine("Sürüm: " + typeof(AppController).Assembly.GetName().Version + (Settings.Packaged ? " (Store)" : " (klasik kurulum)"));
                sb.AppendLine("Windows: " + BugReport.WindowsVersion());
                sb.AppendLine("Zaman: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("İş: " + (Activity ?? "boşta"));
                sb.AppendLine();
                sb.AppendLine("DNS: " + s.Dns.Summary);
                sb.AppendLine("Sunucular: " + string.Join(", ", s.Dns.Servers));
                sb.AppendLine("Şifresiz DNS'e geri dönüş: " + (DnsManager.UdpFallback ? "açık" : "kapalı") +
                              " · Zapret durunca önceki DNS: " + (RestoreDnsOnStop ? "açık" : "kapalı") +
                              " · saklanan: " + (SavedDns ?? "yok"));
                sb.AppendLine("Zapret: " + (s.ZapretRunning ? "çalışıyor" : "kapalı") + " · motor " + ShortSha(s.ZapretVersion) +
                              " · strateji " + (s.Strategy != null ? s.Strategy.Name : "-") + " · filtre " + Settings.Get("zapret.filtre", Zapret.FilterList));
                try
                {
                    foreach (var n in DnsManager.ActiveAdapters())
                        sb.AppendLine("Bağdaştırıcı: " + n.Name + " · " + n.Description + " · " + n.NetworkInterfaceType);
                }
                catch (Exception) { }
                sb.AppendLine();
                sb.AppendLine("--- ayarlar.txt");
                try
                {
                    string path = Path.Combine(Settings.AppDir, "ayarlar.txt");
                    if (File.Exists(path)) sb.AppendLine(File.ReadAllText(path).Trim());
                }
                catch (Exception) { }
                sb.AppendLine();
                sb.AppendLine("--- netsh dns show encryption (özet)");
                var r = ProcessRunner.Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "dns show encryption", 10000);
                foreach (var line in r.Output.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.Length > 0 && t.IndexOf(':') > 0) sb.AppendLine(t);
                }
                sb.AppendLine();
                sb.AppendLine("--- son kayıtlar");
                sb.Append(Diag.Recent(150));
                return Scrub(sb.ToString());
            });
        }

        static string Scrub(string text)
        {
            string user = Environment.UserName;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "%USERPROFILE%");
            if (!string.IsNullOrEmpty(user) && user.Length > 2) text = text.Replace(user, "<kullanıcı>");
            return text;
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
                string text = ProcessRunner.Run("schtasks.exe", "/query /tn " + TaskArg + " /xml").Output;
                if (text.IndexOf(SecurityElement.Escape(LaunchPath()), StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("--tray", StringComparison.OrdinalIgnoreCase) >= 0) return;

                // Görev başka bir exe'yi gösteriyor. Yalnızca o exe artık yoksa (taşınmış, silinmiş, eski sürüm) bu kopyaya
                // yönlendirilir. Var olan başka bir kurulumun görevi ele geçirilmez: önceden ikinci bir kopyayı (geliştirme,
                // taşınabilir, test) bir kez açmak, Windows açılışında hep o kopyanın başlamasına yol açıyordu.
                var m = System.Text.RegularExpressions.Regex.Match(text, "<Command>(.*?)</Command>");
                string target = m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim().Trim('"')) : null;
                if (!string.IsNullOrEmpty(target) && File.Exists(target) &&
                    text.IndexOf("--tray", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Diag.Log("başlangıç görevi başka bir kopyayı gösteriyor, dokunulmadı: " + target);
                    return;
                }
                Diag.Log("başlangıç görevi bu kopyaya yönlendirildi (önceki hedef: " + (target ?? "?") + ")");
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
