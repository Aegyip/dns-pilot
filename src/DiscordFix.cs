using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DnsDegistirici
{
    // Discord masaüstü uyumluluğu (yalnızca sürücüsüz erişim açıkken).
    //
    // Discord'un güncelleyicisi (Rust, native-tls/schannel) yalnızca TLS 1.2 konuşur. TLS 1.2'de sunucu sertifikası
    // şifresiz gider; ISS'nin DPI'ı site adını okuyamadığı (bölünmüş) bağlantılarda sertifikaya bakıp keser, okuyabildiğinde
    // adı engeller. Giden veriyi biçimlendirerek geçirilemez (bölme noktaları, TCP parçalama, OOB, pencere küçültme, diğer
    // Cloudflare portları denendi; sonunda nokta olan adı schannel reddeder).
    //
    // 1) Güncelleme adresi (NEW_UPDATE_ENDPOINT) uygulamanın yerel aktarıcısına çevrilir (Bypass.UpdateRelayUrl,
    //    127.88.0.1:33080). Aktarıcı isteği updates.discord.com'a TLS 1.3 ile iletir; güncellemeler normal şekilde iner.
    // 2) Güvenlik ağı: Discord'un kendi "fail open" ayarı (DESKTOP_TTI_UPDATE_CHECK_DEADLINE_MS). Aktarıcıya ulaşılamazsa
    //    Discord bu sürenin sonunda yüklü sürümle açılır, sonsuza dek "Discord Updater" ekranında beklemez.
    //
    // Erişim kapatılınca ya da uygulama kapanırken (değerler hâlâ bizimkiyse) ikisi de silinir. Uygulama çöker ya da
    // Store'dan kaldırılırsa adres kalabilir; o durumda güncelleme denetimi başarısız olur ama 2. madde sayesinde Discord
    // açılmaya devam eder; DNS Pilot bir sonraki açılışında ayarı düzeltir.
    public static class DiscordFix
    {
        public const string Key = "DESKTOP_TTI_UPDATE_CHECK_DEADLINE_MS";
        // Güncelleme denetimi başarısız olursa bu yolda ~50 ms içinde olur; 3 sn Discord'un açılışını gereksiz geciktirmez.
        public const int Value = 3000;
        const int OldValue = 8000; // 2.2.0 ilk sürümü
        public const string EndpointKey = "NEW_UPDATE_ENDPOINT";
        const string OldEndpointValue = "https://updates.discord.com./"; // 2.2.0 test sürümünden

        static IEnumerable<string> SettingsFiles()
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (var dir in new[] { "discord", "discordptb", "discordcanary" })
            {
                string f = Path.Combine(roaming, dir, "settings.json");
                if (File.Exists(f)) yield return f;
            }
        }

        static bool OurEndpoint(string v) => v == Bypass.UpdateRelayUrl || v == OldEndpointValue;
        static bool DefaultEndpoint(string v) => v == null || v.TrimEnd('/') == "https://updates.discord.com";

        static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;

        // Ayarlar yoksa (ya da varsayılan / eski değerimizse) yazar; kullanıcının kendi değerine dokunmaz.
        public static int Ensure(Action<string> log)
        {
            int changed = 0;
            foreach (var f in SettingsFiles())
            {
                try
                {
                    var obj = Read(f);
                    if (obj == null) continue;
                    bool dirty = false;
                    string ep = Str(obj[EndpointKey]);
                    if ((DefaultEndpoint(ep) || ep == OldEndpointValue) && ep != Bypass.UpdateRelayUrl)
                    {
                        obj[EndpointKey] = Bypass.UpdateRelayUrl;
                        dirty = true;
                    }
                    int? cur = Number(obj[Key]);
                    if (cur == null || cur.Value <= 0 || cur.Value == OldValue) { obj[Key] = Value; dirty = true; }
                    if (!dirty) continue;
                    Write(f, obj);
                    changed++;
                    log?.Invoke("Discord güncelleme ayarı yazıldı: " + f);
                }
                catch (Exception ex) { log?.Invoke("Discord ayarı yazılamadı (" + f + "): " + ex.Message); }
            }
            return changed;
        }

        // keepDeadline: uygulama kapanırken yalnızca aktarıcı adresi geri alınır (aktarıcı artık yok), fail-open kalır.
        public static void Remove(Action<string> log, bool keepDeadline = false)
        {
            foreach (var f in SettingsFiles())
            {
                try
                {
                    var obj = Read(f);
                    if (obj == null) continue;
                    bool dirty = false;
                    if (OurEndpoint(Str(obj[EndpointKey]))) { obj.Remove(EndpointKey); dirty = true; }
                    int? v = Number(obj[Key]);
                    if (!keepDeadline && (v == Value || v == OldValue)) { obj.Remove(Key); dirty = true; }
                    if (!dirty) continue;
                    Write(f, obj);
                    log?.Invoke("Discord güncelleme ayarı geri alındı: " + f);
                }
                catch (Exception ex) { log?.Invoke("Discord ayarı geri alınamadı (" + f + "): " + ex.Message); }
            }
        }

        // Discord ayar dosyasını kendi de yazar (pencere konumu, sunucudan gelen deney ayarları); bu sırada anahtarımız
        // düşebilir. Erişim açıkken dosya izlenir ve anahtar eksikse geri yazılır, böylece Discord'un bir sonraki
        // açılışında hazırdır. Kendi yazmamız da olay üretir; ikinci Ensure değişiklik bulmaz, döngü olmaz.
        static readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        static System.Threading.Timer debounce;

        public static void StartWatching(Action<string> log)
        {
            lock (watchers)
            {
                if (watchers.Count > 0) return;
                debounce = new System.Threading.Timer(_ => { try { Ensure(log); } catch (Exception) { } });
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                foreach (var dir in new[] { "discord", "discordptb", "discordcanary" })
                {
                    string path = Path.Combine(roaming, dir);
                    if (!Directory.Exists(path)) continue;
                    try
                    {
                        var w = new FileSystemWatcher(path, "settings.json")
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        };
                        FileSystemEventHandler h = (s, e) => debounce?.Change(1500, System.Threading.Timeout.Infinite);
                        w.Changed += h;
                        w.Created += h;
                        w.Renamed += (s, e) => debounce?.Change(1500, System.Threading.Timeout.Infinite);
                        w.EnableRaisingEvents = true;
                        watchers.Add(w);
                    }
                    catch (Exception ex) { log?.Invoke("Discord ayar dosyası izlenemiyor: " + ex.Message); }
                }
            }
        }

        public static void StopWatching()
        {
            lock (watchers)
            {
                foreach (var w in watchers) { try { w.Dispose(); } catch (Exception) { } }
                watchers.Clear();
                try { debounce?.Dispose(); } catch (Exception) { }
                debounce = null;
            }
        }

        static int? Number(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out int i)) return i;
                if (v.TryGetValue(out double d)) return (int)d;
            }
            return null;
        }

        static JsonObject Read(string f)
        {
            string text = File.ReadAllText(f, Encoding.UTF8);
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
        }

        // Yerinde yazılır (geçici dosya + taşıma değil): MSIX paketindeki uygulamada AppData'da yeni oluşturulan
        // dosyalar paketin özel klasörüne yönlendirilir, var olan dosyadaki değişiklik ise gerçek dosyaya yazılır.
        static void Write(string f, JsonObject obj)
        {
            string text = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.SetLength(0);
                fs.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
