using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DnsDegistirici
{
    // Tanılama kaydı: hatalar, işlem süreleri ve dış komutlar.
    // %TEMP%\DnsPilot-kayit.txt dosyasına yazılır (1 MB'ı geçince .eski olarak saklanır) ve son satırlar bellekte tutulur;
    // Sistem sayfasındaki "Tanılama bilgilerini kopyala" düğmesi bunları hata bildirimine eklenmek üzere panoya koyar.
    public static class Diag
    {
        public static readonly string FilePath = Path.Combine(Path.GetTempPath(), "DnsPilot-kayit.txt");
        const long MaxSize = 1024 * 1024;
        const int MemoryLines = 400;

        static readonly object sync = new object();
        static readonly Queue<string> recent = new Queue<string>();

        public static void Log(string text)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text;
            lock (sync)
            {
                recent.Enqueue(line);
                while (recent.Count > MemoryLines) recent.Dequeue();
                try
                {
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxSize)
                    {
                        string old = FilePath + ".eski";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception) { }
            }
        }

        public static void Error(string where, Exception ex)
        {
            Log("HATA (" + where + "): " + ex);
        }

        public static string Recent(int max)
        {
            lock (sync)
            {
                var lines = recent.ToArray();
                int start = Math.Max(0, lines.Length - max);
                var sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                return sb.ToString();
            }
        }
    }
}
