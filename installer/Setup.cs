using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DNS Pilot Kurulum")]
[assembly: AssemblyProduct("DNS Pilot")]
[assembly: AssemblyVersion("2.1.1.0")]
[assembly: AssemblyFileVersion("2.1.1.0")]

// DNS Pilot kurucusu. Uygulama (WinUI 3, .NET ve Windows App SDK dahil) bu dosyaya zip olarak gömülüdür.
//   setup.exe                 → kurar (onay sorar, sonunda uygulamayı açar)
//   setup.exe /S              → sessiz kurulum (Microsoft Store bunu kullanır)
//   setup.exe /uninstall [/S] → kaldırır
// Zapret motoru da kurulumla gelir (zapret\motor); ilk açılışta Zapret hemen kullanılabilir.
static class Setup
{
    const string AppName = "DNS Pilot";
    const string Version = "2.1.1";
    const string Publisher = "Ozan İnce";
    const string ExeName = "DnsPilot.exe";
    const string OldExeName = "DnsDegistirici.exe"; // 1.x (WinForms) sürümü
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DNSPilot";
    const string TaskName = "\"DNS Pilot\"";
    const string LegacyTaskName = "DnsDegistirici"; // 1.x sürümünün görev adı

    static string InstallDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName); }
    }

    static string ShortcutPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk"); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = false, uninstall = false;
        foreach (var a in args)
        {
            var s = a.ToLowerInvariant().TrimStart('/', '-');
            if (s == "s" || s == "silent" || s == "quiet" || s == "q") silent = true;
            if (s == "uninstall" || s == "u") uninstall = true;
        }
        Application.EnableVisualStyles();
        try
        {
            if (uninstall) return Uninstall(silent);
            return Install(silent);
        }
        catch (Exception ex)
        {
            if (!silent) MessageBox.Show(ex.Message, AppName + " Kurulum", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1603; // ERROR_INSTALL_FAILURE
        }
    }

    static int Install(bool silent)
    {
        if (!silent && MessageBox.Show(AppName + " " + Version + " bilgisayarınıza kurulacak.\n\nKonum: " + InstallDir,
                AppName + " Kurulum", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return 1602; // ERROR_INSTALL_USEREXIT

        StopApp();
        // Zapret çalıştıysa WinDivert sürücüsü yüklü kalır ve WinDivert64.sys kilitlidir; üzerine yazılabilmesi için durdurulur.
        Run("sc.exe", "stop windivert");
        Directory.CreateDirectory(InstallDir);
        string exe = Path.Combine(InstallDir, ExeName);
        Extract(InstallDir);
        // 1.x sürümünden yükseltme: eski tek dosyalık exe kaldırılır (ayarlar, profiller ve zapret dosyaları korunur).
        try { File.Delete(Path.Combine(InstallDir, OldExeName)); } catch (Exception) { }

        // Kaldırma için kurucunun kendisini kurulum klasörüne kopyala.
        string uninstaller = Path.Combine(InstallDir, "uninstall.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, uninstaller, true);

        CreateShortcut(ShortcutPath, exe, AppName);

        using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", Publisher);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(DirSize(InstallDir) / 1024), RegistryValueKind.DWord);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }

        if (!silent)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            MessageBox.Show(AppName + " kuruldu. Simgesi bildirim alanında (saatin yanında) görünür.",
                AppName + " Kurulum", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return 0;
    }

    static int Uninstall(bool silent)
    {
        if (!silent && MessageBox.Show(AppName + " kaldırılsın mı? Ayarlarınız ve DNS profilleriniz de silinecek.\n\n" +
                "DNS Pilot'un yaptığı DNS ayarı geri alınır (Zapret öncesi ayarınıza ya da otomatik ayara dönülür).",
                AppName + " Kaldır", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 1602;

        // Uygulamanın kendisi, kaydettiği önceki DNS ayarını geri yükler (WinUI\Program.cs: --dns-sifirla).
        // Önceden DNS "yalnızca şifreli" modda kalıyor, kaldırmadan sonra internet yavaş ya da kesik kalabiliyordu.
        string appExe = Path.Combine(InstallDir, ExeName);
        if (File.Exists(appExe)) Run(appExe, "--dns-sifirla", 60000);

        StopApp();
        Run("schtasks.exe", "/delete /tn " + TaskName + " /f");
        Run("schtasks.exe", "/delete /tn " + LegacyTaskName + " /f");
        Run("sc.exe", "stop windivert");
        try { File.Delete(ShortcutPath); } catch (Exception) { }
        // Uygulamanın bildirim kimliği için oluşturduğu kullanıcı kısayolu ve kaydı (src\Notifications.cs).
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk")); } catch (Exception) { }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\DnsPilot.Desktop", false); } catch (Exception) { }
        try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch (Exception) { }

        // Çalışan uninstall.exe kendi klasörünü silemez: silmeyi kısa bir gecikmeyle cmd'ye bırak.
        string dir = InstallDir;
        if (Directory.Exists(dir))
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        if (!silent) MessageBox.Show(AppName + " kaldırıldı.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    // Gömülü app.zip'i kurulum klasörüne açar. Var olan uygulama dosyalarının üzerine yazılır;
    // kullanıcı dosyaları (ayarlar.txt, profiles.txt, zapret\) zip'te olmadığı için dokunulmaz.
    static void Extract(string dir)
    {
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.zip"))
        {
            if (res == null) throw new Exception("Kurulum paketi bozuk: uygulama dosyaları bulunamadı.");
            using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
            {
                string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                foreach (var entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip dışına yazma
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var src = entry.Open())
                    using (var dst = File.Create(target))
                        src.CopyTo(dst);
                }
            }
        }
    }

    static long DirSize(string dir)
    {
        long total = 0;
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            try { total += new FileInfo(f).Length; } catch (Exception) { }
        return total;
    }

    static void StopApp()
    {
        foreach (var name in new[] { "DnsPilot", "DnsDegistirici", "winws", "winws2" })
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { }
                p.Dispose();
            }
    }

    static void Run(string file, string args, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false };
            using (var p = Process.Start(psi)) p.WaitForExit(timeoutMs);
        }
        catch (Exception) { }
    }

    // Başlat menüsü kısayolu (WScript.Shell COM, ek başvuru gerektirmez).
    static void CreateShortcut(string lnk, string target, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnk));
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        var t = sc.GetType();
        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
        t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
        t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }
}
