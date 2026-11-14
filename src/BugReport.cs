using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DnsDegistirici
{
    // Opens a pre-filled GitHub issue form (.github/ISSUE_TEMPLATE/hata-bildirimi.yml).
    // Only technical details are sent in the URL; the user reviews everything before submitting.
    static class BugReport
    {
        public const string RepoUrl = "https://github.com/Aegyip/dns-pilot";

        public static void Open()
        {
            string url = RepoUrl + "/issues/new?template=hata-bildirimi.yml" +
                "&surum=" + Uri.EscapeDataString(AppVersion()) +
                "&windows=" + Uri.EscapeDataString(WindowsVersion()) +
                "&kurulum=" + Uri.EscapeDataString(Settings.Packaged ? "Microsoft Store" : "Klasik kurulum / kaynaktan derleme") +
                "&zapret=" + Uri.EscapeDataString(ZapretInfo());
            if (!Launch(url)) Launch(RepoUrl + "/issues/new/choose");
        }

        public static void OpenRepo()
        {
            Launch(RepoUrl);
        }

        // The app runs elevated; opening the URL through explorer.exe starts the browser in the user's normal
        // (non-admin) context. Process.Start(url) also throws on .NET 8 unless UseShellExecute is set.
        static bool Launch(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + url + "\"") { UseShellExecute = false });
                return true;
            }
            catch (Exception) { }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception) { return false; }
        }

        static string AppVersion()
        {
            return typeof(BugReport).Assembly.GetName().Version.ToString(3);
        }

        static string WindowsVersion()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string build = (k.GetValue("CurrentBuild") ?? "").ToString();
                    string display = (k.GetValue("DisplayVersion") ?? "").ToString();
                    int b; int.TryParse(build, out b);
                    string name = b >= 22000 ? "Windows 11" : "Windows 10";
                    object ubr = k.GetValue("UBR");
                    return name + (display.Length > 0 ? " " + display : "") + " (derleme " + build + (ubr != null ? "." + ubr : "") + ")";
                }
            }
            catch (Exception) { return Environment.OSVersion.VersionString; }
        }

        static string ZapretInfo()
        {
            try
            {
                string sha = Zapret.CurrentVersion() ?? "";
                if (sha.Length > 7) sha = sha.Substring(0, 7);
                return (Zapret.IsRunning() ? "Çalışıyor" : "Kapalı") + (sha.Length > 0 ? " · motor " + sha : "");
            }
            catch (Exception) { return ""; }
        }
    }
}
