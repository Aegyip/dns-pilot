using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DnsDegistirici
{
    public class ZapretStrategy
    {
        public string Name;
        public int Engine; // 1 = winws.exe (zapret), 2 = winws2.exe (zapret2)
        public string Args;
    }

    // Basit anahtar=değer ayar dosyası (ayarlar.txt).
    public static class Settings
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetCurrentPackageFamilyName(ref int length, StringBuilder name);

        // MSIX paketi olarak mı çalışıyoruz? (APPMODEL_ERROR_NO_PACKAGE = 15700)
        public static readonly bool Packaged = DetectPackaged();

        // Exe'nin bulunduğu klasör. Pakette salt okunurdur.
        public static readonly string InstallDir = ResolveInstallDir();

        // WinUI sürümü geliştirme sırasında bin\winui\ alt klasöründen çalışır; zapret motoru ve kullanıcı
        // dosyaları bir üstteki bin\ klasöründe durur. Exe'nin yanında yoksa üst klasöre bakılır.
        static string ResolveInstallDir()
        {
            string dir = Path.GetDirectoryName(typeof(Settings).Assembly.Location);
            if (Directory.Exists(Path.Combine(dir, "zapret")) || File.Exists(Path.Combine(dir, "profiles.txt"))) return dir;
            var parent = Directory.GetParent(dir);
            if (parent != null && (Directory.Exists(Path.Combine(parent.FullName, "zapret")) ||
                                   File.Exists(Path.Combine(parent.FullName, "profiles.txt"))))
                return parent.FullName;
            return dir;
        }

        // Kullanıcı dosyalarının (ayarlar, profiller, listeler) klasörü.
        // Klasik kurulumda exe'nin yanı; pakette %LOCALAPPDATA%\DNS Pilot.
        public static readonly string AppDir = ResolveAppDir();

        static readonly string FilePath = Path.Combine(AppDir, "ayarlar.txt");

        static bool DetectPackaged()
        {
            try
            {
                int len = 0;
                return GetCurrentPackageFullName(ref len, null) != 15700;
            }
            catch (Exception) { return false; } // Windows 7: API yok
        }

        // Pakette AppData yazımları zaten paketin özel klasörüne yönlendirilir. Gerçek yolu doğrudan
        // kullanırız ki Dosya Gezgini (paket dışı süreç) ve Not Defteri aynı dosyaları görsün.
        static string ResolveAppDir()
        {
            if (!Packaged) return InstallDir;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string family = PackageFamilyName();
            string dir = family != null
                ? Path.Combine(local, "Packages", family, "LocalCache", "Local", "DNS Pilot")
                : Path.Combine(local, "DNS Pilot");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string PackageFamilyName()
        {
            try
            {
                int len = 0;
                GetCurrentPackageFamilyName(ref len, null);
                if (len <= 0) return null;
                var sb = new StringBuilder(len);
                return GetCurrentPackageFamilyName(ref len, sb) == 0 ? sb.ToString() : null;
            }
            catch (Exception) { return null; }
        }

        public static string Get(string key, string fallback)
        {
            string value;
            return Load().TryGetValue(key, out value) ? value : fallback;
        }

        public static void Set(string key, string value)
        {
            var all = Load();
            all[key] = value;
            File.WriteAllLines(FilePath, all.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(true));
        }

        static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(FilePath)) return d;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            return d;
        }
    }

    public static class Zapret
    {
        public const string Repo = "bol-van/zapret-win-bundle";
        public const string FilterList = "liste";
        public const string FilterAuto = "otomatik";
        public const string FilterAll = "tum";

        // motor\ güncellemede tümüyle değişir; kullanıcı dosyaları bir üst klasörde kalır.
        public static readonly string Dir = Path.Combine(Settings.AppDir, "zapret");
        // Pakette motor, paketin içinde gelir (salt okunur; Store sürümüyle güncellenir).
        public static readonly string EngineDir = Settings.Packaged
            ? Path.Combine(Settings.InstallDir, "zapret", "motor")
            : Path.Combine(Dir, "motor");
        public static readonly string HostlistPath = Path.Combine(Dir, "hostlist.txt");
        public static readonly string StrategiesPath = Path.Combine(Dir, "stratejiler.txt");
        static readonly string AutoHostlistPath = Path.Combine(Dir, "autohostlist.txt");
        static readonly string ExcludePath = Path.Combine(Dir, "excludelist.txt");
        static readonly string VersionPath = Settings.Packaged
            ? Path.Combine(Settings.InstallDir, "zapret", "surum.txt")
            : Path.Combine(Dir, "surum.txt");

        static readonly StringBuilder output = new StringBuilder();

        const string DefaultStrategies =
@"# Zapret stratejileri. Her satır:  Ad | motor (2 = zapret2, 1 = eski zapret) | winws argümanları
# Kaynak: alimali54/zapret-win-turkey hazır stratejileri. Çalışmayanı bulursan buradan değiştirebilirsin.

Türk Telekom     | 2 | --payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1
Superonline      | 2 | --payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1
Vodafone         | 2 | --payload=tls_client_hello --lua-desync=multisplit:blob=fake_default_tls:ip_ttl=5:pos=2:nodrop:repeats=1
Türksat Kablonet | 2 | --payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1
Telekom Mobil    | 2 | --payload=tls_client_hello --lua-desync=fake:blob=0x00000000:ip_ttl=5:repeats=1
Turkcell Mobil   | 2 | --payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1
Vodafone Mobil   | 2 | --payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1

Türk Telekom (eski motor)   | 1 | --dpi-desync=fake --dpi-desync-ttl=4
TT alternatif (eski motor)  | 1 | --dpi-desync=fake --dpi-desync-ttl=3
Superonline (eski motor)    | 1 | --dpi-desync=fake --dpi-desync-fooling=md5sig
SOL alternatif (eski motor) | 1 | --dpi-desync=fake --dpi-desync-fooling=md5sig --dpi-desync-ttl=3
Vodafone (eski motor)       | 1 | --dpi-desync=fake,multisplit --dpi-desync-fooling=badseq --dpi-desync-split-pos=1,midsld
Turkcell Mobil (eski motor) | 1 | --dpi-desync=fake --dpi-desync-ttl=1 --dpi-desync-autottl=3
Vodafone Mobil (eski motor) | 1 | --dpi-desync=multisplit --dpi-desync-split-pos=2
Telekom Mobil (eski motor)  | 1 | --dpi-desync=fake --dpi-desync-ttl=5
";

        // Alt alan adları otomatik kapsanır. Şifreli DNS sunucuları da burada ki zapret açıkken DoH çalışsın.
        const string DefaultHostlist =
@"discord.com
discord.gg
discord.media
discord.net
discord.store
discordapp.com
discordapp.net
roblox.com
rbxcdn.com
rbxinfra.net
rbxtrk.com
pastebin.com
cloudflare-dns.com
one.one.one.one
dns.google
";

        const string DefaultExclude = "com.tr\r\ngov.tr\r\n";

        public static bool Installed
        {
            get { return File.Exists(Path.Combine(EngineDir, "winws2.exe")); }
        }

        // Motor klasörü var ama winws2.exe yok: kurulmuş, sonra silinmiş. Neredeyse her zaman antivirüs karantinasıdır.
        public static bool EngineRemoved
        {
            get { return !Installed && Directory.Exists(EngineDir); }
        }

        public const string AntivirusHint =
            "Antivirüs programınız zapret dosyalarını (winws2.exe, WinDivert) silmiş olabilir. Antivirüsün karantina / koruma geçmişinden " +
            "dosyaları geri yükleyip şu klasörü istisnalara ekleyin, ardından motoru yeniden yükleyin: ";

        public static List<ZapretStrategy> LoadStrategies()
        {
            EnsureFile(StrategiesPath, DefaultStrategies);
            var list = new List<ZapretStrategy>();
            foreach (var raw in File.ReadAllLines(StrategiesPath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { '|' }, 3);
                int engine;
                if (parts.Length < 3 || !int.TryParse(parts[1].Trim(), out engine) || (engine != 1 && engine != 2)) continue;
                var s = new ZapretStrategy();
                s.Name = parts[0].Trim();
                s.Engine = engine;
                s.Args = parts[2].Trim();
                if (s.Name.Length > 0 && s.Args.Length > 0) list.Add(s);
            }
            return list;
        }

        public static void EnsureUserFiles()
        {
            EnsureFile(HostlistPath, DefaultHostlist);
            EnsureFile(ExcludePath, DefaultExclude);
            EnsureFile(AutoHostlistPath, "");
        }

        public static bool IsRunning()
        {
            var running = Processes();
            foreach (var p in running) p.Dispose();
            return running.Count > 0;
        }

        // Hata yoksa null, varsa hata metni döner.
        public static string Start(ZapretStrategy s, string filter)
        {
            if (!Installed) return "Zapret dosyaları bulunamadı. " + AntivirusHint + EngineDir;
            Stop();
            RemoveForeignDriver();
            EnsureUserFiles();

            // OOB stratejileri hostlist ile çalışmaz (zapret-win-turkey notu).
            bool oob = s.Args.IndexOf("oob", StringComparison.OrdinalIgnoreCase) >= 0;
            var a = new StringBuilder();
            string exe;

            // Yakalama ve işleme olabildiğince dar tutulur. Tüm stratejiler yalnızca TLS ClientHello'ya dokunur, bu yüzden:
            //  - Yalnızca giden TCP 443 yakalanır. Eskiden 80 ve UDP 443 (QUIC) de yakalanıyordu; QUIC'te onay paketleri
            //    de veri taşıdığından HTTP/3 indirmelerin her paketi kullanıcı alanına çekiliyor, yoğun indirmelerde
            //    paketler gecikip düşüyor, büyük indirmeler ve bazı siteler/oyunlar kopuyordu.
            //  - Profil yalnızca TLS bağlantılarına uygulanır (--filter-tcp=443 --filter-l7=tls).
            //  - Bağlantı başına yalnızca ilk 10 veri paketi işlenir (zapret2: --out-range=-d10, zapret1: cutoff=d10);
            //    el sıkışmadan sonraki trafik betiğe uğramadan geçer.
            if (s.Engine == 2)
            {
                exe = "winws2.exe";
                a.Append("--wf-l3=ipv4 --wf-tcp-out=443");
                foreach (var lua in new[] { "zapret-lib.lua", "zapret-antidpi.lua", "zapret-auto.lua" })
                    a.Append(" --lua-init=" + Quote("@" + Path.Combine(EngineDir, "lua", lua)));
                a.Append(" --filter-tcp=443 --filter-l7=tls");
                if (s.Args.IndexOf("--out-range", StringComparison.OrdinalIgnoreCase) < 0) a.Append(" --out-range=-d10");
                a.Append(" ");
            }
            else
            {
                exe = "winws.exe";
                a.Append("--wf-tcp=443 --filter-tcp=443 ");
                if (s.Args.IndexOf("--dpi-desync-cutoff", StringComparison.OrdinalIgnoreCase) < 0) a.Append("--dpi-desync-cutoff=d10 ");
            }

            if (!oob) a.Append("--hostlist-exclude=" + Quote(ExcludePath) + " ");
            a.Append(s.Args);

            if (!oob && filter == FilterList) a.Append(" --hostlist=" + Quote(HostlistPath));
            if (!oob && filter == FilterAuto) a.Append(" --hostlist-auto=" + Quote(AutoHostlistPath));

            var psi = new ProcessStartInfo(Path.Combine(EngineDir, exe), a.ToString());
            psi.WorkingDirectory = EngineDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            lock (output) output.Length = 0;
            var p = new Process();
            p.StartInfo = psi;
            p.OutputDataReceived += Collect;
            p.ErrorDataReceived += Collect;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (p.WaitForExit(1500))
            {
                p.WaitForExit(); // kalan çıktının toplanması için
                string text;
                lock (output) text = output.ToString().Trim();
                return "Zapret hemen kapandı (kod " + p.ExitCode + ")." + FailureHint(text) + "\n\n" + text;
            }
            return null;
        }

        // Hemen kapanmanın bilinen nedenleri için anlaşılır bir açıklama.
        static string FailureHint(string text)
        {
            var other = new List<string>();
            foreach (var name in new[] { "goodbyedpi", "byedpi", "ciadpi" })
                foreach (var p in Process.GetProcessesByName(name)) { other.Add(p.ProcessName + ".exe"); p.Dispose(); }
            if (other.Count > 0)
                return " Başka bir DPI aracı çalışıyor (" + string.Join(", ", other.Distinct()) + "); kapatıp yeniden deneyin.";
            if (text.IndexOf("divert", StringComparison.OrdinalIgnoreCase) >= 0)
                return " WinDivert sürücüsü açılamadı. Antivirüs, oyun hile korumaları (Vanguard, FACEIT vb.) veya başka bir DPI aracı " +
                       "(GoodbyeDPI, başka bir zapret paketi) buna engel olabilir.";
            return "";
        }

        // GoodbyeDPI ya da başka bir zapret paketi "windivert" sürücü hizmetini kendi klasöründeki WinDivert64.sys ile
        // kaydetmiş olabilir. O klasör silinmişse veya sürücü sürümü farklıysa winws açılır açılmaz kapanır.
        // Hizmet bizim dosyamızı göstermiyorsa kaldırılır; winws açılırken kendi sürücüsüyle yeniden kaydeder.
        static void RemoveForeignDriver()
        {
            try
            {
                var m = Regex.Match(Capture("sc.exe", "qc windivert"), @"BINARY_PATH_NAME\s*:\s*(.+)");
                if (!m.Success) return; // hizmet yok
                string path = m.Groups[1].Value.Trim();
                if (path.StartsWith(@"\??\")) path = path.Substring(4);
                string ours = Path.Combine(EngineDir, "WinDivert64.sys");
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(ours), StringComparison.OrdinalIgnoreCase)) return;
                Capture("sc.exe", "stop windivert");
                Capture("sc.exe", "delete windivert");
            }
            catch (Exception) { }
        }

        static string Capture(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            using (var p = Process.Start(psi))
            {
                string text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                return text;
            }
        }

        public static void Stop()
        {
            foreach (var p in Processes())
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch (Exception) { }
                p.Dispose();
            }
        }

        public static string CurrentVersion()
        {
            return File.Exists(VersionPath) ? File.ReadAllText(VersionPath).Trim() : "";
        }

        public static string LatestVersion()
        {
            using (var wc = Client())
            {
                string json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/commits/master");
                var m = Regex.Match(json, "\"sha\"\\s*:\\s*\"([0-9a-f]{40})\"");
                if (!m.Success) throw new Exception("GitHub yanıtı okunamadı.");
                return m.Groups[1].Value;
            }
        }

        // Paketi indirir, motor klasörünü değiştirir. Hata olursa eski motor geri konur.
        public static void Install(string sha)
        {
            string zip = Path.Combine(Dir, "indirme.zip");
            string extract = Path.Combine(Dir, "indirme");
            string fresh = Path.Combine(Dir, "motor.yeni");
            string old = Path.Combine(Dir, "motor.eski");
            Directory.CreateDirectory(Dir);
            DeleteDir(extract);
            DeleteDir(fresh);
            DeleteDir(old);

            using (var wc = Client())
                wc.DownloadFile("https://codeload.github.com/" + Repo + "/zip/" + sha, zip);
            ZipFile.ExtractToDirectory(zip, extract);

            var src = Directory.GetDirectories(extract, "zapret-winws", SearchOption.AllDirectories).FirstOrDefault();
            if (src == null)
                throw new Exception("İndirilen pakette zapret-winws bulunamadı.");
            if (!File.Exists(Path.Combine(src, "winws2.exe")))
                throw new Exception("winws2.exe indirildi ama hemen silindi. " + AntivirusHint + EngineDir);
            Directory.Move(src, fresh);

            // Sürücü yüklüyken WinDivert64.sys kilitli kalır.
            Stop();
            DnsManager.Run("sc.exe", "stop windivert", null);

            if (Directory.Exists(EngineDir)) Directory.Move(EngineDir, old);
            try
            {
                Directory.Move(fresh, EngineDir);
            }
            catch (Exception)
            {
                if (Directory.Exists(old)) Directory.Move(old, EngineDir);
                throw;
            }

            File.WriteAllText(VersionPath, sha);
            DeleteDir(old);
            DeleteDir(extract);
            File.Delete(zip);
        }

        static List<Process> Processes()
        {
            return Process.GetProcessesByName("winws").Concat(Process.GetProcessesByName("winws2")).ToList();
        }

        static void Collect(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (output)
            {
                output.AppendLine(e.Data);
                if (output.Length > 2000) output.Remove(0, output.Length - 2000);
            }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "DNSPilot";
            return wc;
        }

        static void EnsureFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path)) File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        static void DeleteDir(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        static string Quote(string s)
        {
            return "\"" + s + "\"";
        }
    }
}
