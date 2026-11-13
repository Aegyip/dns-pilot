using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace DnsDegistirici
{
    public class StrategyResult
    {
        public ZapretStrategy Strategy;
        public int Ok, Total;
        public int Ms = -1; // başarılı sitelerin ortalama süresi
        public string Error;

        public string Label
        {
            get
            {
                if (Error != null) return "çalışmadı";
                if (Ok == 0) return "erişemedi";
                return Ok + "/" + Total + " · " + Ms + " ms";
            }
        }
    }

    // Her stratejiyi gerçek bağlantıda dener: engelli sitelere TLS el sıkışması + HTTP isteği.
    // Sertifika doğrulanır; ISS'nin engel sayfası ya da yarıda kesilen bağlantı başarısız sayılır.
    public static class StrategyTest
    {
        // Varsayılan site listesindeki, Türkiye'de DPI ile engellenen adresler.
        public static readonly string[] Hosts = { "discord.com", "gateway.discord.gg", "cdn.discordapp.com" };

        const int ProbeTimeout = 3500;

        // Son testin sonuçları (açılır listede strateji adının yanında gösterilir).
        public static readonly Dictionary<string, StrategyResult> Last = new Dictionary<string, StrategyResult>();

        public static Dictionary<string, IPAddress> Resolve()
        {
            var map = new Dictionary<string, IPAddress>();
            foreach (var h in Hosts)
            {
                try
                {
                    var ar = Dns.BeginGetHostAddresses(h, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(5000)) continue;
                    var ip = Dns.EndGetHostAddresses(ar).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    // Engelli adlar için ISS bazen 0.0.0.0 / 127.x / 195.175.254.2 gibi sahte adres döndürür; o zaman da TLS doğrulaması yakalar.
                    if (ip != null && !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any)) map[h] = ip;
                }
                catch (Exception) { }
            }
            return map;
        }

        // Sitelerin hepsini paralel dener. Başarılı olanların süresini (ms) döndürür.
        public static List<int> ProbeAll(Dictionary<string, IPAddress> targets)
        {
            var times = new List<int>();
            var threads = targets.Select(t => new Thread(() =>
            {
                int ms = Probe(t.Key, t.Value);
                if (ms >= 0) lock (times) times.Add(ms);
            }) { IsBackground = true }).ToList();
            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join(ProbeTimeout * 2 + 1000);
            lock (times) return times.ToList();
        }

        public static int Probe(string host, IPAddress ip)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var tcp = new TcpClient(AddressFamily.InterNetwork))
                {
                    var ar = tcp.BeginConnect(ip, 443, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(ProbeTimeout)) return -1;
                    tcp.EndConnect(ar);
                    tcp.ReceiveTimeout = ProbeTimeout;
                    tcp.SendTimeout = ProbeTimeout;
                    using (var ssl = new SslStream(tcp.GetStream(), false, (s, c, ch, e) => e == SslPolicyErrors.None))
                    {
                        ssl.ReadTimeout = ProbeTimeout;
                        ssl.WriteTimeout = ProbeTimeout;
                        ssl.AuthenticateAsClient(host, null, SslProtocols.Tls12 | (SslProtocols)12288, false);
                        var req = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: " + host +
                            "\r\nUser-Agent: Mozilla/5.0\r\nConnection: close\r\n\r\n");
                        ssl.Write(req);
                        ssl.Flush();
                        var buf = new byte[16];
                        int n = 0;
                        while (n < 5)
                        {
                            int r = ssl.Read(buf, n, buf.Length - n);
                            if (r <= 0) break;
                            n += r;
                        }
                        if (n < 5 || Encoding.ASCII.GetString(buf, 0, 5) != "HTTP/") return -1;
                        if (sw.ElapsedMilliseconds > ProbeTimeout * 2) return -1;
                        return (int)sw.ElapsedMilliseconds;
                    }
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // Tüm stratejileri dener. progress(sıra, toplam, ad); cancel() true dönerse durur.
        public static List<StrategyResult> Run(List<ZapretStrategy> strategies, string filter,
                                               Dictionary<string, IPAddress> targets,
                                               Action<int, int, string> progress, Func<bool> cancel)
        {
            var results = new List<StrategyResult>();
            for (int i = 0; i < strategies.Count; i++)
            {
                if (cancel()) break;
                var s = strategies[i];
                progress(i + 1, strategies.Count, s.Name);
                var r = new StrategyResult { Strategy = s, Total = targets.Count };
                r.Error = Zapret.Start(s, filter);
                if (r.Error == null)
                {
                    Thread.Sleep(500); // WinDivert filtresinin oturması için
                    var times = ProbeAll(targets);
                    r.Ok = times.Count;
                    if (times.Count > 0) r.Ms = (int)times.Average();
                }
                results.Add(r);
                lock (Last) Last[s.Name] = r;
            }
            Zapret.Stop();
            return results;
        }

        // En çok siteye erişen; eşitse yeni motor (zapret2), sonra en hızlı.
        public static StrategyResult Best(List<StrategyResult> results)
        {
            return results.Where(r => r.Error == null && r.Ok > 0)
                          .OrderByDescending(r => r.Ok)
                          .ThenByDescending(r => r.Strategy.Engine)
                          .ThenBy(r => r.Ms)
                          .FirstOrDefault();
        }
    }
}
