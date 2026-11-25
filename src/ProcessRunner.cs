using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DnsDegistirici
{
    // Dış komutlar (netsh, schtasks, sc, ipconfig) için tek yardımcı.
    //  - Her çağrının zaman sınırı vardır; süre dolarsa süreç ağacıyla öldürülür. Takılan bir netsh artık uygulamayı kilitlemez.
    //  - stdout ve stderr aynı anda okunur (sırayla okumak, biri dolduğunda iki tarafı da bekletebiliyordu).
    //  - Süre ve sonuç tanılama kaydına yazılır.
    public static class ProcessRunner
    {
        public sealed class Result
        {
            public int ExitCode = -1;
            public string Output = "";
            public bool TimedOut;
            public long Ms;
        }

        public const int DefaultTimeout = 15000;

        static Encoding Oem
        {
            get
            {
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch (Exception) { return Encoding.UTF8; }
            }
        }

        // quiet: başarısız çıkış kodu beklenen bir sonuçsa (ör. "görev yok") kayda yazılmaz; zaman aşımı yine yazılır.
        public static Result Run(string file, string args, int timeoutMs = DefaultTimeout, bool quiet = false)
        {
            var r = new Result();
            var sw = Stopwatch.StartNew();
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Oem,
                    StandardErrorEncoding = Oem,
                };
                using (var p = Process.Start(psi))
                {
                    Task<string> stdout = p.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        r.TimedOut = true;
                        try { p.Kill(true); } catch (Exception) { }
                        p.WaitForExit(2000);
                    }
                    else
                    {
                        r.ExitCode = p.ExitCode;
                    }
                    // Komutun başlattığı bir alt süreç boruyu açık tutarsa okuma bitmeyebilir; sonsuza dek beklenmez.
                    Task.WaitAll(new Task[] { stdout, stderr }, 2000);
                    r.Output = ((stdout.IsCompleted ? stdout.Result : "") + (stderr.IsCompleted ? stderr.Result : "")).Trim();
                }
            }
            catch (Exception ex)
            {
                r.Output = ex.Message;
            }
            r.Ms = sw.ElapsedMilliseconds;
            if (r.TimedOut)
                Diag.Log("KOMUT ZAMAN AŞIMI (" + timeoutMs + " ms): " + Path.GetFileName(file) + " " + args);
            else if ((r.ExitCode != 0 && !quiet) || r.Ms > 3000)
                Diag.Log("komut " + Path.GetFileName(file) + " " + args + " → kod " + r.ExitCode + ", " + r.Ms + " ms" +
                         (r.ExitCode != 0 && r.Output.Length > 0 ? " · " + Clip(r.Output.Replace("\r", "").Replace("\n", " "), 300) : ""));
            return r;
        }

        static string Clip(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
