using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DNS Pilot Kurulum")]
[assembly: AssemblyProduct("DNS Pilot")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// DNS Pilot kurucusu. Uygulama exe'si bu dosyaya gömülüdür.
//   setup.exe                 → kurar (onay sorar, sonunda uygulamayı açar)
//   setup.exe /S              → sessiz kurulum (Microsoft Store bunu kullanır)
//   setup.exe /uninstall [/S] → kaldırır
// Zapret motoru kurulumda yoktur; uygulama ilk kullanımda Güncellemeler sayfasından indirir.
static class Setup
{
    const string AppName = "DNS Pilot";
    const string Version = "1.0.0";
    const string Publisher = "Ozan İnce";
    const string ExeName = "DnsDegistirici.exe";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DNSPilot";
    const string TaskName = "DnsDegistirici";

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
        Directory.CreateDirectory(InstallDir);
        string exe = Path.Combine(InstallDir, ExeName);
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.exe"))
        {
            if (res == null) throw new Exception("Kurulum paketi bozuk: uygulama dosyası bulunamadı.");
            using (var f = File.Create(exe)) res.CopyTo(f);
        }

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
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024 + new FileInfo(uninstaller).Length / 1024), RegistryValueKind.DWord);
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
                "DNS ayarınız şu an özel bir sunucudaysa, kaldırmadan önce uygulamadan \"Otomatik (modem / ISS)\" seçmeniz önerilir.",
                AppName + " Kaldır", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 1602;

        StopApp();
        Run("schtasks.exe", "/delete /tn " + TaskName + " /f");
        Run("sc.exe", "stop windivert");
        try { File.Delete(ShortcutPath); } catch (Exception) { }
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

    static void StopApp()
    {
        foreach (var name in new[] { "DnsDegistirici", "winws", "winws2" })
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { }
                p.Dispose();
            }
    }

    static void Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false };
            using (var p = Process.Start(psi)) p.WaitForExit(15000);
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
