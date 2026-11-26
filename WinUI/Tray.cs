using System.Collections.Generic;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using DnsDegistirici;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;

namespace DnsPilot
{
    // Tepsi simgesi (durum noktalı logo), WinUI menüsü ve bildirimler.
    public sealed class Tray : IDisposable
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        readonly AppController app;
        readonly TaskbarIcon icon;
        readonly TrayMenuHost menuHost = new TrayMenuHost();
        Icon current;
        IntPtr currentHandle;
        Icon balloonLogo;

        public Tray(AppController app)
        {
            this.app = app;
            icon = new TaskbarIcon
            {
                ToolTipText = "DNS Pilot",
                NoLeftClickDelay = true,
                LeftClickCommand = new Command(() => App.Instance.ShowWindow(null)),
            };
            // Sağ tık: Windows 11 tepsi menüleri gibi WinUI menüsü (TrayMenuHost). Kütüphanenin kendi
            // "ikinci pencere" kipi menüyü göstermiyor, yerel Win32 menüsü de eski görünüyordu.
            icon.RightClickCommand = new Command(() => App.UI.TryEnqueue(() =>
            {
                try { menuHost.Show(BuildMenu()); } catch (Exception ex) { Diag.Error("tepsi menüsü", ex); }
            }));
            app.StateChanged += RefreshIcon;
            RefreshIcon();
            Create();
        }

        // Oturum açılışında Explorer henüz hazır değilse simge oluşturulamaz (kütüphane "TryCreate failed" fırlatır).
        // Önceden bu, uygulamayı açılışta çökertiyordu; şimdi bir dakika boyunca birkaç saniyede bir yeniden denenir.
        void Create()
        {
            try
            {
                icon.ForceCreate(false); // verimlilik kipi kapalı: erişim/DNS işleri yavaşlatılmasın
                created = true;
                RefreshIcon();
            }
            catch (Exception ex)
            {
                if (++createAttempts == 1) Diag.Error("tepsi simgesi oluşturulamadı, yeniden denenecek", ex);
                if (createAttempts > 20) return;
                var timer = App.UI.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(3);
                timer.IsRepeating = false;
                timer.Tick += (s, e) => Create();
                timer.Start();
            }
        }

        bool created;
        int createAttempts;

        // ---- Menü: yalnızca sık kullanılanlar, geri kalanı Ayarlar penceresinde ----

        List<TrayMenuHost.Entry> BuildMenu()
        {
            var items = new List<TrayMenuHost.Entry>();
            var s = app.State;
            var state = s.Dns;
            bool accessOn = app.PendingAccess ?? s.AccessOn;
            string pendingDns = app.PendingDns;

            if (!AppController.Elevated)
            {
                items.Add(new TrayMenuHost.Item { Text = "Yönetici olarak yeniden başlat", Glyph = "", Action = app.RestartElevated });
                items.Add(new TrayMenuHost.Separator());
            }
            string activity = app.Activity;
            if (activity != null)
                items.Add(new TrayMenuHost.Item { Text = activity, Glyph = "", Enabled = false });
            items.Add(new TrayMenuHost.Item { Text = state.Summary, Glyph = "", Enabled = false });
            items.Add(new TrayMenuHost.Item
            {
                Text = s.AccessOn ? "Erişim: etkin"
                     : "Erişim: devre dışı",
                Glyph = "",
                Enabled = false
            });
            items.Add(new TrayMenuHost.Separator());

            items.Add(new TrayMenuHost.Item
            {
                Text = accessOn ? "Erişimi durdur" : "Erişimi başlat",
                Glyph = accessOn ? "" : "",
                Action = () => { if (accessOn) app.StopAccess(); else app.StartAccess(); }
            });
            items.Add(new TrayMenuHost.Separator());

            foreach (var p in s.Profiles)
            {
                var profile = p;
                items.Add(new TrayMenuHost.Item
                {
                    Text = p.Name,
                    Checked = pendingDns != null ? pendingDns == p.Name : state.IsStatic && state.Matched == p.Name,
                    Action = () => app.ApplyProfile(profile)
                });
            }
            items.Add(new TrayMenuHost.Item
            {
                Text = "Otomatik",
                Checked = pendingDns != null ? pendingDns == "" : state.HasAdapter && !state.IsStatic,
                Action = app.ResetDns
            });

            items.Add(new TrayMenuHost.Separator());
            items.Add(new TrayMenuHost.Item { Text = "DNS hız testi", Glyph = "\uEC4A", Action = () => App.Instance.ShowWindow("Giriş") });
            items.Add(new TrayMenuHost.Item { Text = "DNS Pilot ayarları", Glyph = "\uE713", Action = () => App.Instance.ShowWindow(null) });
            items.Add(new TrayMenuHost.Item { Text = "Hata bildir", Glyph = "\uEBE8", Action = BugReport.Open });
            items.Add(new TrayMenuHost.Item { Text = "Çıkış", Glyph = "\uE7E8", Action = App.Instance.ExitApp });
            return items;
        }
        // ---- Simge: logo + sağ altta durum noktası ----

        Color? lastDot;
        string lastTip;

        // Durum anlık görüntüsünden çizilir (arayüz iş parçacığında tarama yok). Renk ve ipucu değişmediyse kabuğa
        // hiç gidilmez; Explorer meşgulken her durum değişikliğinde Shell_NotifyIcon beklemek arayüzü takıyordu.
        void RefreshIcon()
        {
            try
            {
                var s = app.State;
                var state = s.Dns;
                bool accessOn = s.AccessOn;
                Color color = !state.HasAdapter ? Color.FromArgb(220, 38, 38)   // kırmızı: bağlantı yok
                            : !state.IsStatic   ? Color.FromArgb(115, 115, 115) // gri: servis sağlayıcı DNS'i
                            : state.DohOn       ? Color.FromArgb(22, 163, 74)   // yeşil: şifreli
                            :                     Color.FromArgb(139, 92, 246); // mor: şifresiz özel
                Color dot = accessOn && state.HasAdapter ? Color.FromArgb(245, 158, 11) : color;
                if (lastDot != dot || currentHandle == IntPtr.Zero) DrawIcon(dot);
                lastDot = dot;

                string activity = app.Activity;
                string tip = AppController.Clip(activity ?? state.Summary + (accessOn ? " · Erişim" : ""), 63);
                if (tip != lastTip) icon.ToolTipText = tip;
                lastTip = tip;
            }
            catch (Exception ex)
            {
                // Kabuk o an yanıt vermiyorsa (Explorer yeniden başlıyor, oturum açılışı) bir sonraki değişiklikte yeniden denenir.
                lastDot = null;
                lastTip = null;
                Diag.Log("tepsi simgesi güncellenemedi: " + ex.Message);
            }
        }

        void DrawIcon(Color dot)
        {
            int size = Math.Max(16, GetSystemMetrics(49)); // SM_CXSMICON: tepsinin gerçek boyutu
            using (var bmp = Logo(size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float d = size * 0.40f, gap = Math.Max(1.5f, size / 12f);
                float cx = size - d / 2, cy = size - d / 2;
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.Transparent))
                    g.FillEllipse(clear, cx - d / 2 - gap, cy - d / 2 - gap, d + 2 * gap, d + 2 * gap);
                g.CompositingMode = CompositingMode.SourceOver;
                using (var b = new SolidBrush(dot)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);

                IntPtr handle = bmp.GetHicon();
                var next = Icon.FromHandle(handle);
                try { icon.Icon = next; }
                catch (Exception) { DestroyIcon(handle); throw; }
                if (currentHandle != IntPtr.Zero) DestroyIcon(currentHandle);
                current = next;
                currentHandle = handle;
            }
        }

        static string IcoPath { get { return Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"); } }

        static Bitmap Logo(int size)
        {
            var bmp = new Bitmap(size, size);
            try
            {
                using (var ico = new Icon(IcoPath, size, size))
                using (var src = ico.ToBitmap())
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                }
            }
            catch (Exception) { }
            return bmp;
        }

        // ---- Bildirim: gövdede büyük uygulama logosu ----

        public void Notify(string title, string message, bool warning)
        {
            try
            {
                if (!created) return;
                if (balloonLogo == null) balloonLogo = new Icon(IcoPath, 48, 48);
                icon.ShowNotification(title, message, NotificationIcon.None, balloonLogo.Handle, largeIcon: true);
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            icon.Dispose();
            if (currentHandle != IntPtr.Zero) DestroyIcon(currentHandle);
            if (balloonLogo != null) balloonLogo.Dispose();
        }

        sealed class Command : ICommand
        {
            readonly Action action;
            public Command(Action action) { this.action = action; }
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) { return true; }
            public void Execute(object parameter)
            {
                try { action(); } catch (Exception ex) { Diag.Error("tepsi", ex); }
            }
        }
    }
}
