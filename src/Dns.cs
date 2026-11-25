using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.Win32;

namespace DnsDegistirici
{
    public class DnsProfile
    {
        public string Name;
        public List<string> V4 = new List<string>();
        public List<string> V6 = new List<string>();
        public string Doh; // DoH şablonu; null ise şifresiz DNS
    }

    public class DnsState
    {
        public bool HasAdapter;
        public string AdapterName;
        public List<string> Servers = new List<string>();
        public bool IsStatic;
        public bool DohOn;
        public string Matched; // eşleşen profil adı
        public string Summary;
    }

    public static class ProfileStore
    {
        public static readonly string FilePath = Path.Combine(Settings.AppDir, "profiles.txt");

        const string Default =
@"# DNS profilleri. Her satır:  Ad | IPv4 (virgülle) | IPv6 (virgülle, boş olabilir) | DoH şablonu (boş = şifresiz)
# Kaydettikten sonra menüyü yeniden açman yeterli.

Cloudflare | 1.1.1.1, 1.0.0.1 | 2606:4700:4700::1111, 2606:4700:4700::1001 | https://cloudflare-dns.com/dns-query
Google     | 8.8.8.8, 8.8.4.4 | 2001:4860:4860::8888, 2001:4860:4860::8844 | https://dns.google/dns-query
Quad9      | 9.9.9.9, 149.112.112.112 | 2620:fe::fe, 2620:fe::9 | https://dns.quad9.net/dns-query
AdGuard (reklam engelli) | 94.140.14.14, 94.140.15.15 | 2a10:50c0::ad1:ff, 2a10:50c0::ad2:ff | https://dns.adguard-dns.com/dns-query

# Zapret'in dnscrypt-proxy servisini kullanıyorsan başındaki # işaretini kaldır:
# Yerel dnscrypt | 127.0.0.1 | ::1 |
";

        public static List<DnsProfile> Load()
        {
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, Default, new UTF8Encoding(true));

            var list = new List<DnsProfile>();
            foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                if (parts.Length < 2) continue;

                var p = new DnsProfile();
                p.Name = parts[0].Trim();
                p.V4 = SplitIps(parts[1], AddressFamily.InterNetwork);
                if (parts.Length > 2) p.V6 = SplitIps(parts[2], AddressFamily.InterNetworkV6);
                if (parts.Length > 3 && parts[3].Trim().StartsWith("https://")) p.Doh = parts[3].Trim();
                if (p.Name.Length > 0 && p.V4.Count > 0) list.Add(p);
            }
            return list;
        }

        static List<string> SplitIps(string s, AddressFamily family)
        {
            var result = new List<string>();
            foreach (var x in s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                IPAddress ip;
                if (IPAddress.TryParse(x.Trim(), out ip) && ip.AddressFamily == family)
                    result.Add(ip.ToString());
            }
            return result;
        }
    }

    public static class DnsManager
    {
        const string TcpipKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
        const string DohKey = @"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\";

        // İnternete çıkan (ağ geçidi olan) fiziksel/sanal bağdaştırıcılar.
        public static List<NetworkInterface> ActiveAdapters()
        {
            var list = new List<NetworkInterface>();
            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (Exception ex) { Diag.Error("ağ bağdaştırıcıları okunamadı", ex); return list; }
            foreach (var n in all)
            {
                try
                {
                    // Bağdaştırıcı tam o sırada kaybolursa (Wi-Fi geçişi, VPN) özellik okumaları hata fırlatabilir.
                    if (n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                        n.Supports(NetworkInterfaceComponent.IPv4) &&
                        n.GetIPProperties().GatewayAddresses.Count > 0)
                        list.Add(n);
                }
                catch (Exception) { }
            }
            return list;
        }

        public static DnsState GetState(List<DnsProfile> profiles)
        {
            var s = new DnsState();
            var adapters = ActiveAdapters();
            if (adapters.Count == 0)
            {
                s.Summary = "Bağlı ağ bulunamadı";
                return s;
            }

            var n = adapters[0];
            s.HasAdapter = true;
            s.AdapterName = n.Name;
            s.Servers = n.GetIPProperties().DnsAddresses.Select(a => a.ToString()).ToList();
            s.IsStatic = IsStatic(n.Id);
            s.DohOn = s.IsStatic && HasDoh(n.Id);

            var firstV4 = s.Servers.FirstOrDefault(x => x.IndexOf(':') < 0);
            if (s.IsStatic && firstV4 != null)
            {
                var match = profiles.FirstOrDefault(p => p.V4.Contains(firstV4));
                if (match != null) s.Matched = match.Name;
            }

            string label = !s.IsStatic ? "Otomatik (ISS)" : (s.Matched ?? "Özel: " + firstV4);
            s.Summary = n.Name + ": " + label + (s.DohOn ? " · DoH" : "");
            return s;
        }

        // Hata yoksa null, varsa hata metni döner.
        // Şifreli DNS kesilirse Windows şifresiz DNS'e dönebilsin mi? Varsayılan: evet. Kapalıyken ("yalnızca şifreli")
        // servis sağlayıcı DoH'yi kestiği anda (ör. Zapret durunca) her ad çözümlemesi zaman aşımını bekler; internet
        // çok yavaşlamış ya da kesilmiş gibi görünür.
        public static bool UdpFallback
        {
            get { return Settings.Get("dns.udp_yedek", "1") == "1"; }
        }

        public static string Apply(DnsProfile p) { return Apply(p, null); }

        // reachable: DoH erişimi az önce denendiyse sonucu (yeniden denenmesin); null ise burada denenir.
        public static string Apply(DnsProfile p, bool? reachable)
        {
            var adapters = ActiveAdapters();
            if (adapters.Count == 0) return "Bağlı ağ bulunamadı.";
            var log = new StringBuilder();
            bool fallback = UdpFallback;

            // "Yalnızca şifreli" modda DoH erişilemezse hiçbir ad çözülmez; bu yüzden önce deneriz.
            bool useDoh = p.Doh != null && (reachable ?? DohReachable(p));
            if (p.Doh != null)
            {
                foreach (var ip in p.V4.Concat(p.V6))
                {
                    string args = " encryption server=" + ip + " dohtemplate=" + p.Doh +
                                  (useDoh ? " autoupgrade=yes udpfallback=" + (fallback ? "yes" : "no") : " autoupgrade=no udpfallback=yes");
                    if (Netsh("dns add" + args, null) != 0)
                        Netsh("dns set" + args, log);
                }
            }

            foreach (var n in adapters)
            {
                var props = n.GetIPProperties();

                // DoH bayrakları sunucular değişmeden önce yazılır ki değişiklikle birlikte okunsun.
                ClearDoh(n.Id);
                if (useDoh)
                {
                    SetDoh(n.Id, "Doh", p.V4, fallback);
                    SetDoh(n.Id, "Doh6", p.V6, fallback);
                }

                SetServers("ipv4", props.GetIPv4Properties().Index, p.V4, log);

                if (n.Supports(NetworkInterfaceComponent.IPv6))
                {
                    int i6 = props.GetIPv6Properties().Index;
                    if (p.V6.Count > 0) SetServers("ipv6", i6, p.V6, log);
                    else Netsh("interface ipv6 set dnsservers name=" + i6 + " source=dhcp", log);
                }
            }

            FlushCache();
            if (p.Doh != null && !useDoh)
                log.Insert(0, "ISS şifreli DNS'i (" + new Uri(p.Doh).Host + ") engelliyor. " +
                              "DNS şifresiz olarak ayarlandı. Zapret açıkken tekrar seçersen şifreli olur.\n");
            return log.Length == 0 ? null : log.ToString();
        }

        // Tercih edilen addaki profil önce denenir; ilk erişilebilen şifreli profil döner, yoksa null.
        public static DnsProfile PickEncrypted(List<DnsProfile> profiles, string preferred)
        {
            // Sunucular aynı anda denenir: sırayla denemek, DoH'nin engellendiği bir ağda 4 × 6 sn bekletiyordu.
            var candidates = profiles.Where(p => p.Doh != null)
                .OrderBy(p => p.Name.StartsWith(preferred, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
            var tasks = candidates.Select(p => System.Threading.Tasks.Task.Run(() => DohReachable(p))).ToArray();
            try { System.Threading.Tasks.Task.WaitAll(tasks, 10000); } catch (Exception) { }
            for (int i = 0; i < candidates.Count; i++)
                if (tasks[i].IsCompletedSuccessfully && tasks[i].Result) return candidates[i];
            return null;
        }

        // Geçerli DNS ayarını geri yüklenebilir biçimde anlatır: "dhcp" ya da "v4 adresleri;v6 adresleri".
        public static string Describe()
        {
            var adapters = ActiveAdapters();
            if (adapters.Count == 0) return null;
            var n = adapters[0];
            if (!IsStatic(n.Id)) return "dhcp";
            var servers = n.GetIPProperties().DnsAddresses.Select(a => a.ToString()).ToList();
            return string.Join(",", servers.Where(x => x.IndexOf(':') < 0)) + ";" +
                   string.Join(",", servers.Where(x => x.IndexOf(':') >= 0 && !x.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)));
        }

        // Describe() ile kaydedilen ayara döner.
        public static string Restore(string saved)
        {
            if (string.IsNullOrEmpty(saved) || saved == "dhcp") return ResetToDhcp();
            var parts = saved.Split(';');
            var p = new DnsProfile { Name = "Önceki DNS" };
            foreach (var ip in parts[0].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) p.V4.Add(ip.Trim());
            if (parts.Length > 1)
                foreach (var ip in parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) p.V6.Add(ip.Trim());
            if (p.V4.Count == 0) return ResetToDhcp();
            return Apply(p, false);
        }

        // ISS'ler DoH'yi genelde TLS'teki sunucu adından (SNI) tanıyıp keser.
        // Şablondaki adla gerçek bir TLS el sıkışması deneyerek bunu yakalarız.
        public static bool DohReachable(DnsProfile p)
        {
            try
            {
                using (var tcp = new TcpClient(AddressFamily.InterNetwork))
                {
                    var ar = tcp.BeginConnect(IPAddress.Parse(p.V4[0]), 443, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(3000)) return false;
                    tcp.EndConnect(ar);
                    tcp.ReceiveTimeout = 3000;
                    tcp.SendTimeout = 3000;
                    using (var ssl = new SslStream(tcp.GetStream(), false))
                    {
                        // 12288 = TLS 1.3; .NET 4.8 derleyicisinde adı yok ama Windows 11 destekliyor.
                        ssl.AuthenticateAsClient(new Uri(p.Doh).Host, null, SslProtocols.Tls12 | (SslProtocols)12288, false);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string ResetToDhcp()
        {
            var adapters = ActiveAdapters();
            if (adapters.Count == 0) return "Bağlı ağ bulunamadı.";
            var log = new StringBuilder();

            foreach (var n in adapters)
            {
                var props = n.GetIPProperties();
                ClearDoh(n.Id);
                Netsh("interface ipv4 set dnsservers name=" + props.GetIPv4Properties().Index + " source=dhcp", log);
                if (n.Supports(NetworkInterfaceComponent.IPv6))
                    Netsh("interface ipv6 set dnsservers name=" + props.GetIPv6Properties().Index + " source=dhcp", log);
            }

            FlushCache();
            return log.Length == 0 ? null : log.ToString();
        }

        public static void FlushCache()
        {
            Run(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns", null);
        }

        // UDP 53 üzerinden en iyi yanıt süresi (ms); yanıt yoksa -1.
        public static int Ping(string server)
        {
            var query = BuildQuery("google.com");
            var ep = new IPEndPoint(IPAddress.Parse(server), 53);
            int best = -1;
            using (var udp = new UdpClient(ep.AddressFamily))
            {
                udp.Client.ReceiveTimeout = 1500;
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        udp.Send(query, query.Length, ep);
                        IPEndPoint from = null;
                        udp.Receive(ref from);
                        int ms = (int)sw.ElapsedMilliseconds;
                        if (best < 0 || ms < best) best = ms;
                    }
                    catch (SocketException) { }
                }
            }
            return best;
        }

        static byte[] BuildQuery(string host)
        {
            var b = new List<byte> { 0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
            foreach (var label in host.Split('.'))
            {
                b.Add((byte)label.Length);
                b.AddRange(Encoding.ASCII.GetBytes(label));
            }
            b.AddRange(new byte[] { 0, 0, 1, 0, 1 }); // A kaydı, IN sınıfı
            return b.ToArray();
        }

        static void SetServers(string family, int index, List<string> ips, StringBuilder log)
        {
            Netsh("interface " + family + " set dnsservers name=" + index + " source=static address=" + ips[0] +
                  " register=primary validate=no", log);
            for (int i = 1; i < ips.Count; i++)
                Netsh("interface " + family + " add dnsservers name=" + index + " address=" + ips[i] +
                      " index=" + (i + 1) + " validate=no", log);
        }

        static bool IsStatic(string guid)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(TcpipKey + guid))
            {
                var v = k == null ? null : k.GetValue("NameServer") as string;
                return !string.IsNullOrEmpty(v);
            }
        }

        // Windows 11'in "Şifreli DNS" ayarı. DohFlags bitleri (DNS_DOH_SERVER_SETTINGS): 0x1 = kayıtlı şablonu kullan,
        // 0x4 = DoH başarısız olursa şifresiz UDP/TCP'ye dön. 1 = yalnızca şifreli, 5 = şifreli tercih, şifresize izin.
        static void SetDoh(string guid, string sub, List<string> ips, bool fallback)
        {
            foreach (var ip in ips)
                using (var k = Registry.LocalMachine.CreateSubKey(DohKey + guid + @"\DohInterfaceSettings\" + sub + @"\" + ip))
                    k.SetValue("DohFlags", fallback ? 5L : 1L, RegistryValueKind.QWord);
        }

        static bool HasDoh(string guid)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(DohKey + guid + @"\DohInterfaceSettings\Doh"))
                return k != null && k.SubKeyCount > 0;
        }

        static void ClearDoh(string guid)
        {
            try { Registry.LocalMachine.DeleteSubKeyTree(DohKey + guid + @"\DohInterfaceSettings", false); }
            catch (UnauthorizedAccessException) { }
        }

        static int Netsh(string args, StringBuilder log)
        {
            return Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args, log);
        }

        // Zaman sınırlı (ProcessRunner); takılan komut öldürülür ve -1 döner.
        public static int Run(string file, string args, StringBuilder log, int timeoutMs = ProcessRunner.DefaultTimeout)
        {
            var r = ProcessRunner.Run(file, args, timeoutMs, log == null);
            if (log != null && (r.TimedOut || r.ExitCode != 0))
                log.AppendLine(Path.GetFileName(file) + " " + args + "\n  → " +
                               (r.TimedOut ? "yanıt vermedi (" + timeoutMs / 1000 + " sn), durduruldu" : r.Output));
            return r.TimedOut ? -1 : r.ExitCode;
        }
    }
}
