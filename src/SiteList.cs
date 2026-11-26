using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DnsDegistirici
{
    // Erişimin uygulanacağı alan adları (siteler.txt). Alt alan adları otomatik kapsanır.
    // 2.x sürümlerinde liste zapret\hostlist.txt'teydi; ilk açılışta oradan taşınır.
    public static class SiteList
    {
        public static readonly string FilePath = Path.Combine(Settings.AppDir, "siteler.txt");
        static readonly string LegacyPath = Path.Combine(Settings.AppDir, "zapret", "hostlist.txt");

        // Şifreli DNS sunucularının adları da listededir: servis sağlayıcı DoH bağlantısını adından (SNI) tanıyıp keserse
        // bu adlar da yerel dinleyiciden, ilk paketi bölünerek geçer.
        const string Default =
@"# Erişimin uygulanacağı alan adları. Her satıra bir ad; alt alan adları otomatik kapsanır.
# Değişiklik, erişim yeniden başlatılınca geçerli olur.
discord.com
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
dns.adguard-dns.com
dns.quad9.net
";

        public static string[] Load()
        {
            Ensure();
            return File.ReadAllLines(FilePath, Encoding.UTF8);
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            if (!File.Exists(FilePath))
            {
                if (File.Exists(LegacyPath))
                {
                    File.Copy(LegacyPath, FilePath);
                    Diag.Log("site listesi zapret\\hostlist.txt'ten taşındı");
                }
                else File.WriteAllText(FilePath, Default, new UTF8Encoding(false));
            }
            EnsureDohHosts();
        }

        // Profillerdeki tüm DoH adları (kullanıcının eklediği sunucular dahil) eksikse listeye eklenir.
        static void EnsureDohHosts()
        {
            try
            {
                var lines = File.ReadAllLines(FilePath, Encoding.UTF8).Select(l => l.Trim().ToLowerInvariant()).ToList();
                var missing = new List<string>();
                foreach (var p in ProfileStore.Load())
                {
                    if (p.Doh == null) continue;
                    string host = new Uri(p.Doh).Host.ToLowerInvariant();
                    if (!lines.Contains(host) && !missing.Contains(host)) missing.Add(host);
                }
                if (missing.Count == 0) return;
                string text = File.ReadAllText(FilePath, Encoding.UTF8);
                if (text.Length > 0 && !text.EndsWith("\n")) text += "\r\n";
                File.WriteAllText(FilePath, text + string.Join("\r\n", missing) + "\r\n", new UTF8Encoding(false));
                Diag.Log("site listesine DoH adları eklendi: " + string.Join(", ", missing));
            }
            catch (Exception ex) { Diag.Error("site listesi güncellenemedi", ex); }
        }
    }
}
