namespace DnsPilot
{
    // Beklenmeyen hataları %TEMP%\DnsPilot-hata.txt dosyasına yazar (destek için).
    static class Trace
    {
        public static void Log(string step)
        {
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DnsPilot-hata.txt"),
                    System.DateTime.Now.ToString("HH:mm:ss.fff") + "  " + step + System.Environment.NewLine);
            }
            catch (System.Exception) { }
        }
    }
}
