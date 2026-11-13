using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DnsDegistirici
{
    // Bildirimlerin (tepsi balonları → Windows 11 bildirimleri) uygulama adı ve logosuyla görünmesi.
    //
    // Sorun: Paketlenmemiş bir exe'nin bildirim başlığındaki simgeyi Windows exe yolu üzerinden bulup
    // önbelleğe alıyordu; eski, kod ile çizilen "D" tepsi simgesi bazen başlıkta kalıyordu. Gövdedeki büyük
    // simge de Windows'un standart bilgi/uyarı simgesiydi.
    //
    // Çözüm:
    //  1. Sürece sabit bir AppUserModelID verilir ve bu kimlik HKCU\Software\Classes\AppUserModelId altında
    //     "DNS Pilot" adı ve logo dosyasıyla kaydedilir. Bildirim başlığı artık buradan okunur.
    //     (MSIX/Store paketinde paket kimliği kullanıldığından bu adım atlanır.)
    //  2. Balonlar NIIF_USER | NIIF_LARGE_ICON ile, uygulama logosu büyük simge olarak gösterilir.
    public static class Notifications
    {
        public const string AppId = "DnsPilot.Desktop";
        public const string DisplayName = "DNS Pilot";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        const int NIM_MODIFY = 1;
        const int NIF_INFO = 0x10;
        const int NIIF_USER = 0x4;
        const int NIIF_LARGE_ICON = 0x20;
        const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        static IntPtr balloonIcon = IntPtr.Zero;

        public static bool IsPackaged
        {
            get
            {
                try
                {
                    int len = 0;
                    return GetCurrentPackageFullName(ref len, null) != APPMODEL_ERROR_NO_PACKAGE;
                }
                catch (Exception) { return false; } // Windows 7: paket kavramı yok
            }
        }

        // Program başında, ilk pencere/tepsi simgesi oluşmadan önce çağrılmalı.
        public static void RegisterIdentity()
        {
            if (IsPackaged) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DnsPilot");
                Directory.CreateDirectory(dir);
                string logo = Path.Combine(dir, "logo.png");
                using (var bmp = Theme.Logo(256)) bmp.Save(logo, ImageFormat.Png);

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
                {
                    key.SetValue("DisplayName", DisplayName);
                    key.SetValue("IconUri", logo);
                    key.SetValue("IconBackgroundColor", "00000000");
                }
                SetCurrentProcessExplicitAppUserModelID(AppId);
                EnsureShortcut();
            }
            catch (Exception) { } // kimlik kaydedilemezse bildirimler yine çalışır, yalnızca simge eski kalabilir
        }

        // Paketlenmemiş masaüstü uygulamalarında bildirim başlığındaki ad ve simge, bu AppUserModelID'yi
        // taşıyan bir Başlat menüsü kısayolundan okunur (Microsoft'un belgelediği yöntem).
        // Kurulumun oluşturduğu kısayol (tüm kullanıcılar) varsa ona kimlik eklenir; yoksa kullanıcıya ait
        // bir kısayol oluşturulur. Uygulama yönetici olarak çalıştığı için ortak kısayol da güncellenebilir.
        static void EnsureShortcut()
        {
            string exe = Application.ExecutablePath;
            string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), DisplayName + ".lnk");
            string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), DisplayName + ".lnk");
            if (File.Exists(common))
            {
                Shortcut.Write(common, null, AppId);  // hedefi kurulumun yerinde bırak
                if (File.Exists(user)) File.Delete(user); // çift Başlat girdisi olmasın
            }
            else
            {
                Shortcut.Write(user, exe, AppId);
            }
        }

        // Balonu uygulama logosuyla gösterir. Başarısız olursa false döner (çağıran standart balona düşer).
        public static bool Show(NotifyIcon tray, string title, string text, int timeoutMs)
        {
            try
            {
                var windowField = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
                var idField = typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                if (windowField == null || idField == null) return false;
                var window = windowField.GetValue(tray) as NativeWindow;
                if (window == null || window.Handle == IntPtr.Zero) return false;

                // Büyük simge: sistemin büyük simge boyutunda (100%: 32 px, 150%: 48 px) logo.
                int size = Math.Max(32, SystemInformation.IconSize.Width);
                IntPtr icon;
                using (var bmp = Theme.Logo(size)) icon = bmp.GetHicon();

                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                    hWnd = window.Handle,
                    uID = (int)idField.GetValue(tray),
                    uFlags = NIF_INFO,
                    szInfo = Truncate(text, 255),
                    szInfoTitle = Truncate(title, 63),
                    uTimeoutOrVersion = timeoutMs,
                    dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON,
                    hBalloonIcon = icon
                };
                bool ok = Shell_NotifyIcon(NIM_MODIFY, ref data);

                // Önceki balonun simgesi artık kullanılmıyor.
                if (balloonIcon != IntPtr.Zero) DestroyIcon(balloonIcon);
                balloonIcon = icon;
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return " ";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
