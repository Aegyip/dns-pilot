using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DnsDegistirici
{
    // Sürücüsüz, şeffaf erişim (Microsoft Store sürücü içeren paket kabul etmiyor).
    //
    // 1) Windows'un ad çözümleme ilkesine (NRPT) bir kural eklenir: yalnızca listedeki alan adları (discord.com …)
    //    bu uygulamanın yerel DNS sunucusuna (127.88.0.1:53) sorulur. Diğer tüm adlar her zamanki gibi çözülür.
    // 2) Yerel DNS, listedeki adlar için 127.88.0.1 döndürür. Uygulamalar (Discord, güncelleyicisi, tarayıcı)
    //    farkında olmadan bağlantıyı bize açar; proxy ayarı gerekmez.
    // 3) 127.88.0.1:443'teki dinleyici TLS ClientHello'dan site adını okur, gerçek adresi şifreli DNS ile bulur,
    //    hedefe doğrudan bağlanır ve ClientHello'yu DPI'ın okuyamayacağı biçimde iki TLS kaydına bölerek gönderir.
    //    Şifre çözülmez, sertifikalar değişmez; sonrası iki yönlü düz kopyadır, ping değişmez.
    //    Discord sesi UDP ile sunucunun bildirdiği IP'ye gider; DNS'e ve bu dinleyiciye uğramaz.
    public sealed class Bypass
    {
        public enum SplitMode { TlsRecord, TlsRecordTcp, Tcp }

        public static readonly IPAddress ListenIp = IPAddress.Parse("127.88.0.1");
        public const string RuleComment = "DNSPilot";

        public SplitMode Mode = SplitMode.TlsRecord;
        public Func<IEnumerable<string>> Hosts = () => Array.Empty<string>();
        public Action<string> Log = _ => { };
        public bool Verbose;
        public bool ManageNrpt = true; // test için kapatılabilir

        public bool Running { get; private set; }
        public long Connections => Interlocked.Read(ref connections);
        public long Active => Interlocked.Read(ref active);
        public long DnsAnswers => Interlocked.Read(ref dnsAnswers);

        long connections, active, dnsAnswers;
        CancellationTokenSource cts;
        readonly List<IDisposable> sockets = new List<IDisposable>();
        HashSet<string> suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- Başlat / durdur ----

        public string Start()
        {
            if (Running) return null;
            suffixes = new HashSet<string>(CleanHosts(Hosts()), StringComparer.OrdinalIgnoreCase);
            if (suffixes.Count == 0) return "Site listesi boş.";
            cts = new CancellationTokenSource();
            try
            {
                var udp = new UdpClient(new IPEndPoint(ListenIp, 53));
                sockets.Add(udp);
                _ = DnsUdpLoop(udp, cts.Token);
                var dnsTcp = Listen(53); _ = AcceptLoop(dnsTcp, DnsTcp, cts.Token);
                // Discord ses sunucuları WebSocket'i 2083 gibi Cloudflare HTTPS portlarından açar
                // (wss://x.discord.media:2083); hangi porta gelindiyse hedefe de o porttan bağlanılır.
                // 443 ve 80 zorunlu, diğerleri açılamazsa atlanır.
                foreach (int p in TlsPorts) { int port = p; ListenOptional(port, port == 443, (s, c) => HandleTls(s, port, c)); }
                foreach (int p in HttpPorts) { int port = p; ListenOptional(port, port == 80, (s, c) => HandleHttp(s, port, c)); }
                // Discord güncelleyicisi için yerel aktarıcı (bkz. HandleUpdateRelay). Açılamazsa güncelleme yine fail-open ile atlanır.
                ListenOptional(UpdateRelayPort, false, HandleUpdateRelay);
            }
            catch (Exception ex)
            {
                StopSockets();
                return "Yerel dinleyici açılamadı (" + ListenIp + "): " + ex.Message;
            }

            if (ManageNrpt)
            {
                string err = Nrpt.Add(suffixes, ListenIp.ToString());
                if (err != null) { StopSockets(); return err; }
                DnsManager.FlushCache();
            }
            Running = true;
            Log("Başladı: " + ListenIp + " · " + suffixes.Count + " alan adı");
            return null;
        }

        public void Stop()
        {
            if (ManageNrpt)
            {
                try { Nrpt.RemoveAll(); } catch (Exception ex) { Log("NRPT kaldırılamadı: " + ex.Message); }
                try { DnsManager.FlushCache(); } catch { }
            }
            StopSockets();
            Running = false;
        }

        void StopSockets()
        {
            try { cts?.Cancel(); } catch { }
            foreach (var s in sockets) { try { s.Dispose(); } catch { } }
            sockets.Clear();
        }

        static readonly int[] TlsPorts = { 443, 2053, 2083, 2087, 2096, 8443 };
        static readonly int[] HttpPorts = { 80, 2052, 2082, 2086, 2095, 8080, 8880 };

        void ListenOptional(int port, bool required, Func<Socket, CancellationToken, Task> handler)
        {
            TcpListener l;
            try { l = Listen(port); }
            catch (Exception) when (!required) { Log("Port açılamadı, atlandı: " + port); return; }
            _ = AcceptLoop(l, handler, cts.Token);
        }

        TcpListener Listen(int port)
        {
            var l = new TcpListener(ListenIp, port);
            l.Start(512);
            sockets.Add(new ListenerHandle(l));
            return l;
        }

        sealed class ListenerHandle : IDisposable
        {
            readonly TcpListener l;
            public ListenerHandle(TcpListener l) { this.l = l; }
            public void Dispose() { l.Stop(); }
        }

        static async Task AcceptLoop(TcpListener l, Func<Socket, CancellationToken, Task> handler, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                Socket s;
                try { s = await l.AcceptSocketAsync(ct); }
                catch { break; }
                _ = handler(s, ct);
            }
        }

        public static List<string> CleanHosts(IEnumerable<string> lines)
        {
            return lines.Select(h => h.Trim().ToLowerInvariant().TrimStart('*').TrimStart('.'))
                        .Where(h => h.Length > 0 && !h.StartsWith("#") && h.Contains('.') && !h.Contains(' '))
                        .Distinct().ToList();
        }

        public bool Matches(string name)
        {
            name = name.TrimEnd('.').ToLowerInvariant();
            while (true)
            {
                if (suffixes.Contains(name)) return true;
                int i = name.IndexOf('.');
                if (i < 0) return false;
                name = name.Substring(i + 1);
            }
        }

        // ---- Yerel DNS ----

        async Task DnsUdpLoop(UdpClient udp, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try { r = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { continue; } // ICMP port unreachable vb.
                var remote = r.RemoteEndPoint; var q = r.Buffer;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var ans = await AnswerDns(q, ct);
                        if (ans != null) await udp.SendAsync(ans, ans.Length, remote);
                    }
                    catch { }
                });
            }
        }

        async Task DnsTcp(Socket s, CancellationToken ct)
        {
            try
            {
                using (var ns = new NetworkStream(s, true))
                {
                    var len = new byte[2];
                    while (await ReadExact(ns, len, 2, ct))
                    {
                        int n = len[0] << 8 | len[1];
                        var q = new byte[n];
                        if (!await ReadExact(ns, q, n, ct)) break;
                        var a = await AnswerDns(q, ct);
                        if (a == null) break;
                        await ns.WriteAsync(new byte[] { (byte)(a.Length >> 8), (byte)a.Length }, ct);
                        await ns.WriteAsync(a, ct);
                    }
                }
            }
            catch { }
        }

        async Task<byte[]> AnswerDns(byte[] q, CancellationToken ct)
        {
            if (q.Length < 12) return null;
            int qd = q[4] << 8 | q[5];
            if (qd != 1) return await Doh.Forward(q, ct);
            int p = 12; var labels = new List<string>();
            while (p < q.Length && q[p] != 0)
            {
                int l = q[p];
                if (l > 63 || p + 1 + l > q.Length) return null;
                labels.Add(Encoding.ASCII.GetString(q, p + 1, l));
                p += 1 + l;
            }
            p++;
            if (p + 4 > q.Length) return null;
            int qtype = q[p] << 8 | q[p + 1];
            int qEnd = p + 4;
            string name = string.Join(".", labels);

            if (!Matches(name))
            {
                // NRPT yalnızca listedekileri gönderir; yine de gelirse şifreli DNS'e iletilir.
                return await Doh.Forward(q, ct);
            }

            Interlocked.Increment(ref dnsAnswers);
            if (Verbose) Log("DNS " + name + " tür " + qtype);
            // Yanıt: soru aynen, A için 127.88.0.1; diğer türler (AAAA, HTTPS…) boş — istemci IPv4 ile bağlanır.
            var o = new MemoryStream();
            o.Write(q, 0, 2);                                   // id
            o.WriteByte((byte)(0x80 | (q[2] & 0x01)));          // QR, RD kopyası
            o.WriteByte(0x80);                                  // RA, NOERROR
            bool a = qtype == 1;
            o.Write(new byte[] { 0, 1, 0, (byte)(a ? 1 : 0), 0, 0, 0, 0 }, 0, 8);
            o.Write(q, 12, qEnd - 12);
            if (a)
            {
                o.Write(new byte[] { 0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 30, 0, 4 }, 0, 12); // ad işaretçisi, A, IN, TTL 30, 4 bayt
                o.Write(ListenIp.GetAddressBytes(), 0, 4);
            }
            return o.ToArray();
        }

        // ---- TLS (443) ----

        async Task HandleTls(Socket client, int port, CancellationToken ct)
        {
            Interlocked.Increment(ref connections);
            Interlocked.Increment(ref active);
            Socket up = null;
            string host = null;
            try
            {
                client.NoDelay = true;
                var cs = new NetworkStream(client, true);
                byte[] hello;
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(10000);
                    hello = await ReadTlsRecord(cs, t.Token);
                }
                if (hello == null) return;
                host = TlsSplit.SniHost(hello);
                if (host == null) { Log("ClientHello'da site adı yok"); return; }

                up = await Connect(host, port, ct);
                if (up == null) { Log("Bağlanamadı: " + host); return; }
                if (Verbose) Log("→ " + host + " (" + up.RemoteEndPoint + ") hello " + hello.Length + " B, sni@" + TlsSplit.SniOffset(hello));
                await TlsSplit.Send(up, hello, Mode, ct);
                long down = await Pipe(cs, new NetworkStream(up, true), ct);
                if (down == 0)
                {
                    Log("Sunucu yanıt vermeden kapattı: " + host + " (hello " + hello.Length + " B, sni@" + TlsSplit.SniOffset(hello) + ")");
                    if (Verbose) Log("hello " + Convert.ToHexString(hello));
                }
                else if (Verbose) Log("← " + host + " " + down + " B");
            }
            catch (Exception ex) { if (Verbose) Log("hata " + host + ": " + ex.GetType().Name + " " + ex.Message); }
            finally
            {
                try { client.Dispose(); } catch { }
                try { up?.Dispose(); } catch { }
                Interlocked.Decrement(ref active);
            }
        }

        // ---- Discord güncelleme aktarıcısı (127.88.0.1:33080) ----
        // Discord'un güncelleyicisi (Rust, native-tls) yalnızca TLS 1.2 konuşur; TLS 1.2'de sunucu sertifikası şifresiz
        // gittiğinden DPI, site adını göremediği bağlantıda sertifikadan tanıyıp keser (tüm portlarda; bölme, OOB, pencere
        // küçültme, sonda nokta denendi). Güncelleme adresi (NEW_UPDATE_ENDPOINT) bu yerel adrese çevrilir; istek burada
        // alınıp updates.discord.com'a uygulamanın kendi bağlantısıyla (TLS 1.3, sertifika doğrulanır, ClientHello bölünür)
        // iletilir. Yalnızca bu tek sunucuya gidilir (açık proxy değildir); şifresiz kısım bilgisayarın içinde kalır.
        // Manifestteki paketler dl2.discordapp.net'ten (engelli değil) doğrudan iner.
        public const int UpdateRelayPort = 33080;
        public static string UpdateRelayUrl => "http://" + ListenIp + ":" + UpdateRelayPort + "/";
        const string UpdateHost = "updates.discord.com";

        static readonly HttpClient relayHttp = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.None, // gzip vb. olduğu gibi aktarılır
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (ctx, ct) =>
            {
                // Sistem çözücüsü kullanılamaz (NRPT bu adı bize yönlendirir); gerçek adres DoH ile bulunur.
                var ips = await Doh.ResolveA(ctx.DnsEndPoint.Host, ct);
                foreach (var ip in ips.Take(3))
                {
                    var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        using (var c = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            c.CancelAfter(5000);
                            await s.ConnectAsync(ip, ctx.DnsEndPoint.Port, c.Token);
                        }
                        return new Doh.SplitFirstWrite(new NetworkStream(s, true), s);
                    }
                    catch { s.Dispose(); }
                }
                throw new HttpRequestException("updates.discord.com bağlanamadı");
            },
        }) { Timeout = TimeSpan.FromSeconds(30) };

        async Task HandleUpdateRelay(Socket client, CancellationToken ct)
        {
            try
            {
                client.NoDelay = true;
                using (var cs = new NetworkStream(client, true))
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // İstek başlığı
                        var head = new MemoryStream();
                        var one = new byte[1];
                        int matched = 0;
                        while (matched < 4)
                        {
                            int r = await cs.ReadAsync(one, 0, 1, ct);
                            if (r <= 0) return;
                            head.WriteByte(one[0]);
                            matched = (one[0] == (matched % 2 == 0 ? '\r' : '\n')) ? matched + 1 : (one[0] == '\r' ? 1 : 0);
                            if (head.Length > 65536) return;
                        }
                        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                        var first = lines[0].Split(' ');
                        if (first.Length < 3 || !first[1].StartsWith("/")) return;
                        var headers = new List<KeyValuePair<string, string>>();
                        long contentLength = 0;
                        bool keepAlive = !first[2].EndsWith("1.0");
                        foreach (var l in lines.Skip(1))
                        {
                            int c = l.IndexOf(':');
                            if (c <= 0) continue;
                            string k = l.Substring(0, c).Trim(), v = l.Substring(c + 1).Trim();
                            if (k.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) long.TryParse(v, out contentLength);
                            else if (k.Equals("Connection", StringComparison.OrdinalIgnoreCase)) keepAlive = !v.Equals("close", StringComparison.OrdinalIgnoreCase);
                            else if (!k.Equals("Host", StringComparison.OrdinalIgnoreCase) && !k.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)
                                     && !k.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) headers.Add(new KeyValuePair<string, string>(k, v));
                        }
                        byte[] body = null;
                        if (contentLength > 0)
                        {
                            if (contentLength > 16 * 1024 * 1024) return;
                            body = new byte[contentLength];
                            if (!await ReadExact(cs, body, (int)contentLength, ct)) return;
                        }

                        var req = new HttpRequestMessage(new HttpMethod(first[0]), "https://" + UpdateHost + first[1]);
                        if (body != null) req.Content = new ByteArrayContent(body);
                        foreach (var h in headers)
                            if (!req.Headers.TryAddWithoutValidation(h.Key, h.Value) && req.Content != null)
                                req.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);

                        byte[] resBody; string status; var resHeaders = new StringBuilder();
                        try
                        {
                            using (var res = await relayHttp.SendAsync(req, ct))
                            {
                                resBody = await res.Content.ReadAsByteArrayAsync(ct);
                                status = (int)res.StatusCode + " " + res.ReasonPhrase;
                                foreach (var h in res.Headers.Concat(res.Content.Headers))
                                {
                                    if (h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                                        || h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)) continue;
                                    foreach (var v in h.Value) resHeaders.Append(h.Key).Append(": ").Append(v).Append("\r\n");
                                }
                            }
                            if (Verbose) Log("güncelleme aktarıcısı: " + first[0] + " " + first[1].Split('?')[0] + " → " + status);
                        }
                        catch (Exception ex)
                        {
                            Log("güncelleme aktarıcısı: " + ex.GetType().Name + " " + ex.Message);
                            resBody = Encoding.UTF8.GetBytes("upstream error");
                            status = "502 Bad Gateway";
                            resHeaders.Append("Content-Type: text/plain\r\n");
                        }
                        var hdr = "HTTP/1.1 " + status + "\r\n" + resHeaders + "Content-Length: " + resBody.Length + "\r\n" +
                                  (keepAlive ? "" : "Connection: close\r\n") + "\r\n";
                        await cs.WriteAsync(Encoding.ASCII.GetBytes(hdr), ct);
                        await cs.WriteAsync(resBody, ct);
                        if (!keepAlive) return;
                    }
                }
            }
            catch { }
            finally { try { client.Dispose(); } catch { } }
        }

        // ---- Düz HTTP (80): Host başlığına göre ilet ----

        async Task HandleHttp(Socket client, int port, CancellationToken ct)
        {
            Socket up = null;
            try
            {
                client.NoDelay = true;
                var cs = new NetworkStream(client, true);
                var buf = new byte[16384]; int n = 0;
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(10000);
                    while (n < buf.Length)
                    {
                        int r = await cs.ReadAsync(buf.AsMemory(n), t.Token);
                        if (r <= 0) return;
                        n += r;
                        if (Encoding.ASCII.GetString(buf, 0, n).Contains("\r\n\r\n")) break;
                    }
                }
                string head = Encoding.ASCII.GetString(buf, 0, n);
                var m = System.Text.RegularExpressions.Regex.Match(head, @"\r\nHost:\s*([^\r:]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!m.Success) return;
                up = await Connect(m.Groups[1].Value.Trim(), port, ct);
                if (up == null) return;
                // DPI düz HTTP'de "Host:" değerini birebir karşılaştırır. Sonuna nokta eklenmiş tam alan adı
                // ("discord.com.") geçerlidir, sunucular kabul eder, DPI eşleştiremez (bölme ve harf düzeni denendi, işe yaramadı).
                var g = m.Groups[1];
                byte[] req = buf.Take(n).ToArray();
                if (!g.Value.TrimEnd().EndsWith("."))
                {
                    int end = g.Index + g.Value.TrimEnd().Length;
                    req = req.Take(end).Concat(new[] { (byte)'.' }).Concat(req.Skip(end)).ToArray();
                }
                await up.SendAsync(req, SocketFlags.None, ct);
                await Pipe(cs, new NetworkStream(up, true), ct);
            }
            catch { }
            finally
            {
                try { client.Dispose(); } catch { }
                try { up?.Dispose(); } catch { }
            }
        }

        static async Task<Socket> Connect(string host, int port, CancellationToken ct)
        {
            var ips = await Doh.ResolveA(host, ct);
            foreach (var ip in ips.Take(3))
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    using (var c = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        c.CancelAfter(5000);
                        await s.ConnectAsync(ip, port, c.Token);
                    }
                    return s;
                }
                catch { s.Dispose(); }
            }
            return null;
        }

        // a: istemci, b: sunucu. Sunucudan istemciye aktarılan bayt sayısını döndürür.
        static async Task<long> Pipe(Stream a, Stream b, CancellationToken ct)
        {
            Task<long> up, down;
            using (var p = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                up = Copy(a, b, p.Token);
                down = Copy(b, a, p.Token);
                await Task.WhenAny(up, down);
                // Bir yön kapanınca diğerinin kalan verisini teslim etmesi için kısa süre tanınır.
                await Task.WhenAny(Task.WhenAll(up, down), Task.Delay(2000));
                p.Cancel();
                try { await Task.WhenAll(up, down); } catch { }
            }
            a.Dispose(); b.Dispose();
            return down.IsCompletedSuccessfully ? down.Result : -1;
        }

        // 256 KB: 64 KB ile indirme hızı doğrudan bağlantının ~%75'i, 256 KB ile ~%90-100'ü ölçüldü.
        public static int CopyBufferSize = 256 * 1024;

        // Tampon ortak havuzdan kiralanır: 256 KB'lık dizi büyük nesne yığınına (LOH) düşer; bağlantı başına yeni dizi
        // ayırmak, çok bağlantı açıp kapatan Discord'da yığını şişirip parçalıyordu.
        static async Task<long> Copy(Stream from, Stream to, CancellationToken ct)
        {
            var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            long total = 0;
            try
            {
                int r;
                while ((r = await from.ReadAsync(buf.AsMemory(0, CopyBufferSize), ct)) > 0)
                {
                    await to.WriteAsync(buf.AsMemory(0, r), ct);
                    total += r;
                }
            }
            catch { }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(buf); }
            try { (to as NetworkStream)?.Socket.Shutdown(SocketShutdown.Send); } catch { }
            return total;
        }

        // ClientHello tek TLS kaydıdır ama birden çok TCP okumasına bölünebilir (ör. ~1.8 KB kuantum sonrası anahtarla).
        static async Task<byte[]> ReadTlsRecord(Stream s, CancellationToken ct)
        {
            var h = new byte[5];
            if (!await ReadExact(s, h, 5, ct)) return null;
            if (h[0] != 0x16) return null;
            int len = h[3] << 8 | h[4];
            if (len <= 0 || len > 16384 + 2048) return null;
            var rec = new byte[5 + len];
            Buffer.BlockCopy(h, 0, rec, 0, 5);
            if (!await ReadExact(s, rec.AsMemory(5), len, ct)) return null;
            return rec;
        }

        static Task<bool> ReadExact(Stream s, byte[] b, int n, CancellationToken ct) => ReadExact(s, b.AsMemory(), n, ct);
        static async Task<bool> ReadExact(Stream s, Memory<byte> b, int n, CancellationToken ct)
        {
            int have = 0;
            while (have < n)
            {
                int r = await s.ReadAsync(b.Slice(have, n - have), ct);
                if (r <= 0) return false;
                have += r;
            }
            return true;
        }
    }

    // ClientHello bölme işlemleri.
    public static class TlsSplit
    {
        // ClientHello içinde site adının (SNI) başladığı konum; yoksa -1.
        public static int SniOffset(byte[] ch)
        {
            try
            {
                if (ch.Length < 50 || ch[0] != 0x16 || ch[5] != 0x01) return -1;
                int p = 5 + 4 + 2 + 32;
                p += 1 + ch[p];
                p += 2 + (ch[p] << 8 | ch[p + 1]);
                p += 1 + ch[p];
                int end = p + 2 + (ch[p] << 8 | ch[p + 1]);
                p += 2;
                while (p + 4 <= end && p + 4 <= ch.Length)
                {
                    int type = ch[p] << 8 | ch[p + 1], len = ch[p + 2] << 8 | ch[p + 3];
                    if (type == 0) return p + 4 + 5;
                    p += 4 + len;
                }
            }
            catch { }
            return -1;
        }

        public static string SniHost(byte[] ch)
        {
            int o = SniOffset(ch);
            if (o < 2 || o > ch.Length) return null;
            int len = ch[o - 2] << 8 | ch[o - 1];
            if (len <= 0 || o + len > ch.Length) return null;
            return Encoding.ASCII.GetString(ch, o, len);
        }

        // Tek TLS kaydını `at` konumundan (başlık dahil) iki kayda böler; sunucu içeriği birleştirir.
        public static byte[] SplitRecord(byte[] rec, int at)
        {
            int payloadLen = rec.Length - 5;
            int a = Math.Min(Math.Max(1, at - 5), payloadLen - 1);
            int b = payloadLen - a;
            var o = new byte[rec.Length + 5];
            o[0] = rec[0]; o[1] = rec[1]; o[2] = rec[2]; o[3] = (byte)(a >> 8); o[4] = (byte)a;
            Buffer.BlockCopy(rec, 5, o, 5, a);
            o[5 + a] = rec[0]; o[6 + a] = rec[1]; o[7 + a] = rec[2]; o[8 + a] = (byte)(b >> 8); o[9 + a] = (byte)b;
            Buffer.BlockCopy(rec, 5 + a, o, 10 + a, b);
            return o;
        }

        public static async Task Send(Socket s, byte[] rec, Bypass.SplitMode mode, CancellationToken ct)
        {
            int sni = SniOffset(rec);
            // Sonunda nokta olan ad ("x.discord.com.") DPI'da eşleşmez ve olduğu gibi geçer. Bölünürse DPI adı okuyamaz
            // ve TLS 1.2'de şifresiz giden sunucu sertifikasına bakıp keser. (DPI: adı okuyup listede bulamazsa geçirir,
            // okuyamazsa sertifikaya bakar. TLS 1.3'te sertifika şifreli olduğundan bölme yeterlidir.)
            string host = SniHost(rec);
            if (sni < 0 || (host != null && host.EndsWith("."))) { await s.SendAsync(rec, SocketFlags.None, ct); return; }
            int at = sni + 1; // site adının ikinci karakteri
            switch (mode)
            {
                case Bypass.SplitMode.Tcp:
                    await s.SendAsync(rec.AsMemory(0, at), SocketFlags.None, ct);
                    await Task.Delay(1, ct);
                    await s.SendAsync(rec.AsMemory(at), SocketFlags.None, ct);
                    break;
                case Bypass.SplitMode.TlsRecordTcp:
                    var two = SplitRecord(rec, at);
                    await s.SendAsync(two.AsMemory(0, at + 5), SocketFlags.None, ct);
                    await Task.Delay(1, ct);
                    await s.SendAsync(two.AsMemory(at + 5), SocketFlags.None, ct);
                    break;
                default:
                    await s.SendAsync(SplitRecord(rec, at), SocketFlags.None, ct);
                    break;
            }
        }
    }

    // Şifreli DNS (DoH). Sistem çözücüsü kullanılamaz: listedeki adlar NRPT ile bize döner (döngü olur).
    // Sunuculara IP ile bağlanılır; ISS DoH'yi SNI'den tanıyıp keserse diye ClientHello burada da bölünür.
    public static class Doh
    {
        static readonly string[] Servers = { "https://1.1.1.1/dns-query", "https://8.8.8.8/dns-query", "https://1.0.0.1/dns-query", "https://8.8.4.4/dns-query" };
        static readonly ConcurrentDictionary<string, (IPAddress[] ips, DateTime until)> cache =
            new ConcurrentDictionary<string, (IPAddress[], DateTime)>(StringComparer.OrdinalIgnoreCase);

        static readonly HttpClient http = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(4),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectCallback = async (ctx, ct) =>
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await s.ConnectAsync(ctx.DnsEndPoint, ct);
                    return new SplitFirstWrite(new NetworkStream(s, true), s);
                }
                catch { s.Dispose(); throw; }
            },
        }) { Timeout = TimeSpan.FromSeconds(6), DefaultRequestVersion = HttpVersion.Version11 };

        static int preferred;

        // İki sunucuya (farklı sağlayıcılar) aynı anda sorulur, ilk başarılı yanıt kullanılır. Sırayla sormak, bir sunucu
        // ara sıra yanıt vermediğinde bağlantı zaman aşımı (4 sn) kadar bekletiyordu (ölçümde p95 ~4 sn).
        public static async Task<byte[]> Forward(byte[] query, CancellationToken ct)
        {
            int first = preferred;
            int second = (first + 1) % Servers.Length; // 1.1.1.1 ↔ 8.8.8.8 sırası farklı sağlayıcıları eşler
            var a = await Race(query, new[] { first, second }, ct);
            if (a != null) return a;
            return await Race(query, Enumerable.Range(0, Servers.Length).Where(i => i != first && i != second).ToArray(), ct);
        }

        static async Task<byte[]> Race(byte[] query, int[] servers, CancellationToken ct)
        {
            if (servers.Length == 0) return null;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var tasks = servers.Select(i => Ask(query, i, cts.Token)).ToList();
                while (tasks.Count > 0)
                {
                    var done = await Task.WhenAny(tasks);
                    tasks.Remove(done);
                    var (idx, bytes) = await done;
                    if (bytes != null) { preferred = idx; cts.Cancel(); return bytes; }
                }
            }
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            return null;
        }

        static async Task<(int, byte[])> Ask(byte[] query, int idx, CancellationToken ct)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, Servers[idx]) { Content = new ByteArrayContent(query) };
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");
                req.Headers.Accept.ParseAdd("application/dns-message");
                using (var res = await http.SendAsync(req, ct))
                {
                    if (!res.IsSuccessStatusCode) return (idx, null);
                    return (idx, await res.Content.ReadAsByteArrayAsync(ct));
                }
            }
            catch { return (idx, null); }
        }

        static readonly ConcurrentDictionary<string, byte> refreshing = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        // Süresi dolmuş kayıt varsa beklemeden o döndürülür ve arka planda yenilenir (Discord'un IP'leri nadiren değişir).
        public static async Task<IPAddress[]> ResolveA(string host, CancellationToken ct)
        {
            if (IPAddress.TryParse(host, out var lit)) return new[] { lit };
            if (cache.TryGetValue(host, out var c))
            {
                if (c.until <= DateTime.UtcNow && refreshing.TryAdd(host, 0))
                    _ = Task.Run(async () => { try { await Lookup(host, CancellationToken.None); } finally { refreshing.TryRemove(host, out _); } });
                return c.ips;
            }
            return await Lookup(host, ct);
        }

        static async Task<IPAddress[]> Lookup(string host, CancellationToken ct)
        {
            var a = await Forward(BuildQuery(host, 1), ct);
            var (ips, ttl) = ParseA(a);
            if (ips.Length > 0) cache[host] = (ips, DateTime.UtcNow.AddSeconds(Math.Max(30, Math.Min(ttl, 3600))));
            return ips;
        }

        public static byte[] BuildQuery(string host, int type)
        {
            var o = new MemoryStream();
            var id = (ushort)Environment.TickCount;
            o.Write(new byte[] { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 12);
            foreach (var l in host.TrimEnd('.').Split('.'))
            {
                var b = Encoding.ASCII.GetBytes(l);
                o.WriteByte((byte)b.Length); o.Write(b, 0, b.Length);
            }
            o.Write(new byte[] { 0, 0, (byte)type, 0, 1 }, 0, 5);
            return o.ToArray();
        }

        public static (IPAddress[], int) ParseA(byte[] m)
        {
            var list = new List<IPAddress>(); int ttl = 300;
            try
            {
                if (m == null || m.Length < 12) return (list.ToArray(), ttl);
                int qd = m[4] << 8 | m[5], an = m[6] << 8 | m[7];
                int p = 12;
                for (int i = 0; i < qd; i++) { p = SkipName(m, p) + 4; }
                for (int i = 0; i < an; i++)
                {
                    p = SkipName(m, p);
                    int type = m[p] << 8 | m[p + 1];
                    int t = m[p + 4] << 24 | m[p + 5] << 16 | m[p + 6] << 8 | m[p + 7];
                    int len = m[p + 8] << 8 | m[p + 9];
                    p += 10;
                    if (type == 1 && len == 4) { list.Add(new IPAddress(new[] { m[p], m[p + 1], m[p + 2], m[p + 3] })); ttl = t; }
                    p += len;
                }
            }
            catch { }
            return (list.ToArray(), ttl);
        }

        static int SkipName(byte[] m, int p)
        {
            while (true)
            {
                int l = m[p];
                if (l == 0) return p + 1;
                if ((l & 0xC0) == 0xC0) return p + 2;
                p += 1 + l;
            }
        }

        // İlk yazılan TLS kaydını (ClientHello) bölerek gönderen akış.
        internal sealed class SplitFirstWrite : Stream
        {
            readonly NetworkStream inner; readonly Socket sock; bool first = true;
            public SplitFirstWrite(NetworkStream inner, Socket sock) { this.inner = inner; this.sock = sock; }
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            {
                if (first && buffer.Length > 5 && buffer.Span[0] == 0x16)
                {
                    first = false;
                    await TlsSplit.Send(sock, buffer.ToArray(), Bypass.SplitMode.TlsRecord, ct);
                    return;
                }
                first = false;
                await inner.WriteAsync(buffer, ct);
            }
            public override Task WriteAsync(byte[] b, int o, int c, CancellationToken ct) => WriteAsync(b.AsMemory(o, c), ct).AsTask();
            public override void Write(byte[] b, int o, int c) => WriteAsync(b, o, c, default).GetAwaiter().GetResult();
            public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => inner.ReadAsync(b, ct);
            public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => inner.ReadAsync(b, o, c, ct);
            public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
            public override void Flush() => inner.Flush();
            public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            protected override void Dispose(bool d) { if (d) inner.Dispose(); base.Dispose(d); }
        }
    }

    // Windows ad çözümleme ilkesi (NRPT): yalnızca listedeki alan adları yerel DNS'e sorulur.
    //
    // Kural kayıt defterine GEÇİCİ (volatile) anahtar olarak yazılır: Windows yeniden başlayınca kendiliğinden silinir.
    // Uygulama çöker ya da Store'dan kaldırılırsa (MSIX kaldırılırken temizlik kodu çalışmaz) kural en geç ilk yeniden
    // başlatmaya kadar kalır; kalıcı kural, uygulama yokken Discord'u (VPN kullanılsa bile) çözülemez bırakırdı.
    // İkinci sunucu eklenmez: Türkiye'de 1.1.1.1'e giden düz DNS sorgusu da engelli olduğundan işe yaramıyor.
    public static class Nrpt
    {
        const string Base = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig";
        const string KeyName = "DNSPilot";

        [System.Runtime.InteropServices.DllImport("dnsapi.dll")] static extern bool DnsFlushResolverCache();

        public static string Add(IEnumerable<string> suffixes, string server)
        {
            RemoveAll();
            // ".discord.com" alt alan adlarını, "discord.com" kendisini kapsar.
            var names = suffixes.SelectMany(s => new[] { "." + s, s }).ToArray();
            try
            {
                using (var b = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(Base))
                using (var k = b.CreateSubKey(KeyName, true, Microsoft.Win32.RegistryOptions.Volatile))
                {
                    k.SetValue("Version", 2, Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("Name", names, Microsoft.Win32.RegistryValueKind.MultiString);
                    k.SetValue("GenericDNSServers", server, Microsoft.Win32.RegistryValueKind.String);
                    k.SetValue("ConfigOptions", 8, Microsoft.Win32.RegistryValueKind.DWord); // genel DNS sunucusu
                    k.SetValue("IPSECCARestriction", "", Microsoft.Win32.RegistryValueKind.String);
                    k.SetValue("Comment", Bypass.RuleComment, Microsoft.Win32.RegistryValueKind.String);
                    k.SetValue("DisplayName", "DNS Pilot", Microsoft.Win32.RegistryValueKind.String);
                }
            }
            catch (Exception ex) { return "Ad çözümleme kuralı eklenemedi: " + ex.Message; }

            // DNS istemcisi kuralı genelde hemen okur; okumadıysa bir kuralı cmdlet ile ekleyip silmek yeniden yüklemeyi tetikler.
            string probe = suffixes.First();
            if (!Redirected(probe, server, 3000))
            {
                Ps("Add-DnsClientNrptRule -Namespace '.dnspilot-reload.invalid' -NameServers '127.0.0.1' -Comment 'DNSPilotReload'; " +
                   "Get-DnsClientNrptRule | Where-Object { $_.Comment -eq 'DNSPilotReload' } | Remove-DnsClientNrptRule -Force");
                if (!Redirected(probe, server, 3000)) { RemoveAll(); return "Ad çözümleme kuralı Windows tarafından uygulanmadı."; }
            }
            return null;
        }

        static bool Redirected(string host, string server, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    DnsFlushResolverCache();
                    if (System.Net.Dns.GetHostAddresses(host).Any(a => a.ToString() == server)) return true;
                }
                catch { }
                Thread.Sleep(300);
            }
            return false;
        }

        // Bizim geçici anahtarımız ve önceki sürümlerin (cmdlet ile eklenen, kalıcı) "DNSPilot" açıklamalı kuralları silinir.
        public static void RemoveAll()
        {
            using (var b = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Base, true))
            {
                if (b == null) return;
                foreach (var name in b.GetSubKeyNames())
                {
                    bool ours = name == KeyName;
                    if (!ours)
                        using (var k = b.OpenSubKey(name))
                            ours = k != null && (k.GetValue("Comment") as string) == Bypass.RuleComment;
                    if (ours) { try { b.DeleteSubKeyTree(name, false); } catch { } }
                }
            }
            try { DnsFlushResolverCache(); } catch { }
        }

        public static bool Present()
        {
            using (var b = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Base))
            {
                if (b == null) return false;
                foreach (var name in b.GetSubKeyNames())
                {
                    if (name == KeyName) return true;
                    using (var k = b.OpenSubKey(name))
                        if (k != null && (k.GetValue("Comment") as string) == Bypass.RuleComment) return true;
                }
            }
            return false;
        }

        static void Ps(string script)
        {
            ProcessRunner.Run(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"", 30000, true);
        }
    }
}
