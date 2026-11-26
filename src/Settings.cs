using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DnsDegistirici
{
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

        // WinUI sürümü geliştirme sırasında bin\winui\ alt klasöründen çalışır; kullanıcı dosyaları bir üstteki bin\
        // klasöründe durur. Exe'nin yanında yoksa üst klasöre bakılır.
        static string ResolveInstallDir()
        {
            string dir = Path.GetDirectoryName(typeof(Settings).Assembly.Location);
            if (File.Exists(Path.Combine(dir, "profiles.txt"))) return dir;
            var parent = Directory.GetParent(dir);
            if (parent != null && File.Exists(Path.Combine(parent.FullName, "profiles.txt"))) return parent.FullName;
            return dir;
        }

        // Uygulama yönetici izniyle mi çalışıyor? DNS değişiklikleri (netsh) ve ad çözümleme kuralı (NRPT) bunu gerektirir.
        public static bool IsElevated
        {
            get
            {
                try
                {
                    using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch (Exception) { return true; }
            }
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

        // Ayarlar bellekte tutulur; dosya yalnızca dışarıdan (Not Defteri) değiştiyse yeniden okunur.
        // Tüm erişim tek kilitle yapılır: arka plandaki bir kayıt (ör. güncelleme denetimi) ile arayüzden yapılan
        // kayıt artık birbirinin değişikliğini silemez. Yazım önce geçici dosyaya yapılıp yerine taşınır;
        // yazım sırasında kapanma/çökme dosyayı yarım bırakmaz. Okuma/yazma hatası uygulamayı çökertmez.
        static readonly object sync = new object();
        static Dictionary<string, string> cache;
        static DateTime cacheStamp;

        public static string Get(string key, string fallback)
        {
            lock (sync)
            {
                string value;
                return Current().TryGetValue(key, out value) ? value : fallback;
            }
        }

        public static void Set(string key, string value)
        {
            lock (sync)
            {
                var all = Current();
                string old;
                if (value == null) { if (!all.Remove(key)) return; }
                else if (all.TryGetValue(key, out old) && old == value) return;
                else all[key] = value;
                try
                {
                    string tmp = FilePath + ".yeni";
                    File.WriteAllLines(tmp, all.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(true));
                    File.Move(tmp, FilePath, true);
                    cacheStamp = File.GetLastWriteTimeUtc(FilePath);
                }
                catch (Exception ex)
                {
                    Diag.Error("ayar kaydedilemedi: " + key, ex);
                }
            }
        }

        static Dictionary<string, string> Current()
        {
            DateTime stamp;
            try { stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch (Exception) { stamp = cacheStamp; }
            if (cache != null && stamp == cacheStamp) return cache;

            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
            }
            catch (Exception ex)
            {
                Diag.Error("ayarlar okunamadı", ex);
                if (cache != null) return cache;
            }
            cache = d;
            cacheStamp = stamp;
            return d;
        }
    }
}
