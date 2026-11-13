using System;
using System.Globalization;

namespace DnsDegistirici
{
    // Güncellemeler sayfasının gösterdiği durum. Denetim sonucu ayarlar.txt'de saklanır.
    public class UpdateState
    {
        public bool Checking, Installing;
        public string Error;          // son denetim/yükleme hatası
        public string Message;        // son işlemin sonucu (ör. "Zapret motoru güncellendi")
        public string ZapretCurrent;  // yüklü motor sürümü (commit)
        public string ZapretLatest;   // son denetimde bulunan sürüm; null = hiç denetlenmedi
        public DateTime? LastCheck;

        public bool ZapretAvailable
        {
            get
            {
                return !string.IsNullOrEmpty(ZapretLatest) && ZapretLatest.Length >= 7 &&
                       !string.Equals(ZapretLatest, ZapretCurrent, StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool AutoCheck
        {
            get { return Settings.Get("guncelleme.otomatik", "1") == "1"; }
        }

        public static DateTime? LoadLastCheck()
        {
            DateTime d;
            string v = Settings.Get("guncelleme.son_denetim", "");
            return DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d) ? d : (DateTime?)null;
        }

        public static void SaveCheck(string latest)
        {
            Settings.Set("guncelleme.son_denetim", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            Settings.Set("guncelleme.zapret_son", latest);
        }

        // Windows Update'teki gibi: "Bugün 14:32", "Dün 09:10", "12 Kasım 2026 18:05"
        public static string FormatWhen(DateTime when)
        {
            var tr = new CultureInfo("tr-TR");
            if (when.Date == DateTime.Today) return "Bugün " + when.ToString("HH:mm", tr);
            if (when.Date == DateTime.Today.AddDays(-1)) return "Dün " + when.ToString("HH:mm", tr);
            return when.ToString("d MMMM yyyy HH:mm", tr);
        }
    }
}
