using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace DnsDegistirici
{
    // 3.0'dan önceki klasik kurulumlar Zapret motorunu (winws2.exe + WinDivert sürücüsü) kullanıyordu. 3.0 sürücü
    // kullanmaz; yükseltmeden sonra ilk açılışta (yönetici izniyle) bir kez:
    //  - bu uygulamanın klasöründen çalışan winws/winws2 durdurulur,
    //  - "windivert" sürücü hizmeti bizim klasörümüzdeki WinDivert64.sys'i gösteriyorsa kaldırılır
    //    (başka bir aracın, ör. GoodbyeDPI'ın sürücüsüne dokunulmaz),
    //  - motor klasörü (zapret\motor) silinir. Site listesi SiteList tarafından taşınmış olarak kalır.
    // Zapret'in açılırken değiştirdiği DNS (zapret.onceki_dns) AppController tarafından geri yüklenir.
    public static class LegacyCleanup
    {
        const string DoneKey = "eski.zapret_temizlendi";

        public static void RunOnce()
        {
            if (Settings.Packaged || !Settings.IsElevated) return;
            if (Settings.Get(DoneKey, "0") == "1") return;
            string dir = Path.Combine(Settings.AppDir, "zapret");
            string engine = Path.Combine(dir, "motor");
            try
            {
                foreach (var name in new[] { "winws", "winws2" })
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            string path = p.MainModule?.FileName ?? "";
                            if (path.StartsWith(Settings.AppDir, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); }
                        }
                        catch (Exception) { }
                        p.Dispose();
                    }

                var m = Regex.Match(ProcessRunner.Run("sc.exe", "qc windivert", 10000, true).Output, @"BINARY_PATH_NAME\s*:\s*(.+)");
                if (m.Success)
                {
                    string sys = m.Groups[1].Value.Trim();
                    if (sys.StartsWith(@"\??\")) sys = sys.Substring(4);
                    if (sys.StartsWith(Settings.AppDir, StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessRunner.Run("sc.exe", "stop windivert", 10000, true);
                        ProcessRunner.Run("sc.exe", "delete windivert", 10000, true);
                        Diag.Log("eski Zapret sürücü hizmeti (windivert) kaldırıldı");
                    }
                }

                if (Directory.Exists(engine))
                {
                    Directory.Delete(engine, true);
                    Diag.Log("eski Zapret motoru silindi: " + engine);
                }
                foreach (var f in new[] { "stratejiler.txt", "autohostlist.txt", "excludelist.txt", "surum.txt" })
                    try { File.Delete(Path.Combine(dir, f)); } catch (Exception) { }
                Settings.Set(DoneKey, "1");
            }
            catch (Exception ex) { Diag.Error("eski Zapret temizliği", ex); } // bir sonraki açılışta yeniden denenir
        }
    }
}
