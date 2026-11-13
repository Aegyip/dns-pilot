using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DnsDegistirici
{
    // Windows 11 görünümü: yuvarlak köşeler, sistemin açık/koyu temasına uyan renkler, Fluent simgeleri.
    public class Palette
    {
        public bool Dark;
        public Color Back, Surface, Text, SubText, Hover, Pressed, Separator, Border, Accent, AccentText, Track;
        // Ayarlar penceresi: kartlar, düğmeler, kenar çubuğu.
        public Color Card, CardBorder, CardHover, ControlBack, ControlHover, ControlBorder, NavSelected, NavHover, Success;

        public static bool? ForceDark; // önizleme için

        public static Palette Current()
        {
            var p = new Palette();
            p.Dark = ForceDark ?? IsDarkMode();
            p.Accent = AccentColor(p.Dark);
            p.AccentText = Luma(p.Accent) > 150 ? Color.Black : Color.White;
            if (p.Dark)
            {
                p.Back = Color.FromArgb(44, 44, 44);
                p.Surface = Color.FromArgb(32, 32, 32);
                p.Text = Color.FromArgb(255, 255, 255);
                p.SubText = Color.FromArgb(160, 160, 160);
                p.Hover = Color.FromArgb(61, 61, 61);
                p.Pressed = Color.FromArgb(56, 56, 56);
                p.Separator = Color.FromArgb(64, 64, 64);
                p.Border = Color.FromArgb(70, 70, 70);
                p.Track = Color.FromArgb(58, 58, 58);
                // Mica üzerinde yarı saydam katmanlar (WinUI 3 koyu tema değerleri).
                p.Card = Color.FromArgb(13, 255, 255, 255);
                p.CardBorder = Color.FromArgb(40, 0, 0, 0);
                p.CardHover = Color.FromArgb(21, 255, 255, 255);
                p.ControlBack = Color.FromArgb(15, 255, 255, 255);
                p.ControlHover = Color.FromArgb(21, 255, 255, 255);
                p.ControlBorder = Color.FromArgb(24, 255, 255, 255);
                p.NavSelected = Color.FromArgb(15, 255, 255, 255);
                p.NavHover = Color.FromArgb(10, 255, 255, 255);
                p.Success = Color.FromArgb(108, 203, 95);
            }
            else
            {
                p.Back = Color.FromArgb(249, 249, 249);
                p.Surface = Color.FromArgb(243, 243, 243);
                p.Text = Color.FromArgb(26, 26, 26);
                p.SubText = Color.FromArgb(110, 110, 110);
                p.Hover = Color.FromArgb(234, 234, 234);
                p.Pressed = Color.FromArgb(237, 237, 237);
                p.Separator = Color.FromArgb(229, 229, 229);
                p.Border = Color.FromArgb(220, 220, 220);
                p.Track = Color.FromArgb(228, 228, 228);
                // Mica üzerinde yarı saydam katmanlar (WinUI 3 açık tema değerleri).
                p.Card = Color.FromArgb(179, 255, 255, 255);
                p.CardBorder = Color.FromArgb(15, 0, 0, 0);
                p.CardHover = Color.FromArgb(128, 249, 249, 249);
                p.ControlBack = Color.FromArgb(179, 255, 255, 255);
                p.ControlHover = Color.FromArgb(128, 249, 249, 249);
                p.ControlBorder = Color.FromArgb(30, 0, 0, 0);
                p.NavSelected = Color.FromArgb(10, 0, 0, 0);
                p.NavHover = Color.FromArgb(6, 0, 0, 0);
                p.Success = Color.FromArgb(15, 123, 15);
            }
            return p;
        }

        static bool IsDarkMode()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception) { return false; }
        }

        // Kullanıcının vurgu rengi. Koyu temada Windows'un yaptığı gibi açık tonu kullanılır.
        static Color AccentColor(bool dark)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var palette = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (palette != null && palette.Length >= 32)
                    {
                        int i = dark ? 1 * 4 : 4 * 4; // 1: açık ton, 4: koyu ton
                        return Color.FromArgb(palette[i], palette[i + 1], palette[i + 2]);
                    }
                }
            }
            catch (Exception) { }
            return dark ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 95, 184);
        }

        static int Luma(Color c)
        {
            return (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
        }
    }

    public static class Theme
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_CAPTION_COLOR = 35;
        const int DWMWCP_ROUND = 2;

        // Uygulama logosu (src\app.ico, exe'ye gömülü kaynak). Pencere başlıkları ve tepsi bunu kullanır.
        static Icon appIcon;
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                    using (var s = typeof(Theme).Assembly.GetManifestResourceStream("DnsPilot.app.ico"))
                        appIcon = s != null ? new Icon(s) : SystemIcons.Application;
                return appIcon;
            }
        }

        // İstenen boyutta, en yakın çizimden yüksek kaliteyle ölçeklenmiş logo.
        public static Bitmap Logo(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var s = typeof(Theme).Assembly.GetManifestResourceStream("DnsPilot.app.ico"))
            using (var g = Graphics.FromImage(bmp))
            {
                if (s == null) return bmp;
                int pick = size <= 16 ? 16 : size <= 24 ? 24 : size <= 32 ? 32 : size <= 48 ? 48 : size <= 64 ? 64 : 256;
                using (var ico = new Icon(s, pick, pick))
                using (var src = ico.ToBitmap())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                }
            }
            return bmp;
        }

        public static readonly string UiFont = PickFont("Segoe UI Variable Text", "Segoe UI");
        public static readonly string IconFont = PickFont("Segoe Fluent Icons", "Segoe MDL2 Assets");

        public const string GlyphCheck = "";
        public const string GlyphChevron = "";

        static string PickFont(string preferred, string fallback)
        {
            using (var fonts = new InstalledFontCollection())
                foreach (var f in fonts.Families)
                    if (f.Name == preferred) return preferred;
            return fallback;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        static void SetAttr(IntPtr hwnd, int attr, int value)
        {
            try { DwmSetWindowAttribute(hwnd, attr, ref value, 4); }
            catch (Exception) { } // Windows 10: yok sayılır, düz köşeli kalır
        }

        // Pencerenin köşelerini yuvarlatır, kenar ve başlık rengini temaya uydurur.
        public static void ApplyWindow(IntPtr hwnd, Palette p, bool popup)
        {
            SetAttr(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            SetAttr(hwnd, DWMWA_BORDER_COLOR, ColorRef(p.Border));
            if (!popup)
            {
                SetAttr(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, p.Dark ? 1 : 0);
                SetAttr(hwnd, DWMWA_CAPTION_COLOR, ColorRef(p.Surface));
            }
        }

        public static float Scale(Control c)
        {
            return c.DeviceDpi / 96f;
        }

        // Menüyü ve tüm alt menülerini Windows 11 görünümüne geçirir. Her açılışta çağrılabilir.
        public static void StyleMenu(ToolStripDropDown menu)
        {
            var p = Palette.Current();
            var renderer = new FluentMenuRenderer(p);
            StyleDropDown(menu, renderer, p);
        }

        static void StyleDropDown(ToolStripDropDown dd, FluentMenuRenderer renderer, Palette p)
        {
            float s = Scale(dd);
            dd.Renderer = renderer;
            dd.DropShadowEnabled = false; // köşeli gölge yerine DWM'nin yuvarlak kenarı
            dd.BackColor = p.Back;
            dd.ForeColor = p.Text;
            dd.Font = new Font(UiFont, 9f);
            dd.Padding = new Padding((int)(4 * s), (int)(4 * s), (int)(4 * s), (int)(4 * s));
            var menu = dd as ToolStripDropDownMenu;
            if (menu != null) { menu.ShowImageMargin = true; menu.ShowCheckMargin = false; }

            if (dd.Tag as string != "styled")
            {
                dd.Tag = "styled";
                dd.HandleCreated += (o, e) => ApplyWindow(dd.Handle, Palette.Current(), true);
                dd.Opened += (o, e) => ApplyWindow(dd.Handle, Palette.Current(), true);
            }
            if (dd.IsHandleCreated) ApplyWindow(dd.Handle, p, true);

            foreach (ToolStripItem item in dd.Items)
            {
                var sep = item as ToolStripSeparator;
                if (sep != null)
                {
                    sep.AutoSize = false;
                    sep.Height = (int)(9 * s);
                    continue;
                }
                item.Padding = new Padding(0, (int)(6 * s), 0, (int)(6 * s));
                if (item.Font.Bold) item.Font = new Font(UiFont, 9f, FontStyle.Bold);
                else item.Font = new Font(UiFont, 9f);
                var mi = item as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) StyleDropDown(mi.DropDown, renderer, p);
            }
        }
    }

    public class FluentMenuRenderer : ToolStripRenderer
    {
        readonly Palette p;

        public FluentMenuRenderer(Palette palette)
        {
            p = palette;
        }

        static float S(ToolStripItem item)
        {
            return item.Owner != null ? item.Owner.DeviceDpi / 96f : 1f;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(p.Back)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Kenarı DWM çiziyor (yuvarlak). Windows 10'da ince bir çizgi yeterli.
            if (Environment.OSVersion.Version.Build >= 22000) return;
            using (var pen = new Pen(p.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (!item.Enabled || !(item.Selected || item.Pressed)) return;
            float s = S(item);
            var r = new RectangleF(0, 1 * s, item.Width, item.Height - 2 * s);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(r, 4 * s))
            using (var b = new SolidBrush(item.Pressed && !((ToolStripMenuItem)item).HasDropDownItems ? p.Pressed : p.Hover))
                g.FillPath(b, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var color = e.Item.Enabled ? p.Text : p.SubText;
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            var rect = e.TextRectangle;
            if (e.Item is ToolStripMenuItem && e.Item.IsOnDropDown)
            {
                // Kısayol/ok alanına taşmasın; sağa doğru genişlet ki uzun başlıklar kesilmesin.
                rect = new Rectangle(rect.X, 0, Math.Max(rect.Width, e.Item.Width - rect.X - 8), e.Item.Height);
            }
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, rect, color, flags);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            float s = S(e.Item);
            var r = e.ImageRectangle;
            using (var f = new Font(Theme.IconFont, 9f * 1.0f))
            {
                var box = new Rectangle(r.X, 0, r.Width, e.Item.Height);
                TextRenderer.DrawText(e.Graphics, Theme.GlyphCheck, f, box, p.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            using (var f = new Font(Theme.IconFont, 7f))
            {
                var r = e.ArrowRectangle;
                var box = new Rectangle(r.X - 4, 0, r.Width, e.Item.Height);
                TextRenderer.DrawText(e.Graphics, Theme.GlyphChevron, f, box,
                    e.Item.Enabled ? p.SubText : p.Separator,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float s = S(e.Item);
            int y = e.Item.Height / 2;
            using (var pen = new Pen(p.Separator, Math.Max(1f, s)))
                e.Graphics.DrawLine(pen, -4 * s, y, e.Item.Width + 4 * s, y);
        }
    }

    // Hız testi sonuçları: Windows 11 iletişim kutusu düzeninde, çubuk grafikli.
    public class SpeedTestForm : Form
    {
        public class Row
        {
            public string Name;
            public int Ms; // -1: yanıt yok
            public bool Current;
        }

        readonly List<Row> rows;
        readonly string note;
        readonly Palette p = Palette.Current();
        Rectangle buttonRect;
        bool buttonHover, buttonDown;
        float s = 1f;

        const int Pad = 24, RowH = 36, ButtonH = 32;

        public SpeedTestForm(List<Row> rows, string note)
        {
            this.rows = rows;
            this.note = note;
            Text = "DNS hız testi";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Icon = Theme.AppIcon;
            ShowIcon = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = p.Surface;
            Font = new Font(Theme.UiFont, 9f);
            KeyPreview = true;
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close(); };
            Relayout();
        }

        void Relayout()
        {
            s = DeviceDpi / 96f;
            int w = (int)(440 * s);
            int noteH = MeasureNote(w - (int)(2 * Pad * s));
            int h = (int)((Pad + 28 + 8) * s) + (int)(rows.Count * RowH * s) + (int)(12 * s) + noteH
                  + (int)((Pad + ButtonH + Pad) * s);
            ClientSize = new Size(w, h);
            buttonRect = new Rectangle(w - (int)((Pad + 120) * s), h - (int)((Pad + ButtonH) * s),
                                       (int)(120 * s), (int)(ButtonH * s));
        }

        int MeasureNote(int width)
        {
            using (var f = new Font(Theme.UiFont, 8.25f))
                return TextRenderer.MeasureText(note, f, new Size(width, 0), TextFormatFlags.WordBreak).Height;
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Relayout();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindow(Handle, p, false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width;
            int x = (int)(Pad * s), y = (int)(Pad * s);
            int inner = w - 2 * x;

            // Alt bölüm (düğme alanı) Windows 11 iletişim kutularındaki gibi ayrı tonda.
            int footerTop = ClientSize.Height - (int)((Pad + ButtonH + Pad) * s);
            using (var b = new SolidBrush(p.Back)) g.FillRectangle(b, 0, 0, w, footerTop);
            using (var pen = new Pen(p.Separator)) g.DrawLine(pen, 0, footerTop, w, footerTop);

            using (var title = new Font(Theme.UiFont.Replace(" Text", " Display"), 14f, FontStyle.Bold, GraphicsUnit.Point))
            using (var titleFallback = new Font(Theme.UiFont, 14f, FontStyle.Bold))
            {
                var tf = title.Name == Theme.UiFont.Replace(" Text", " Display") ? title : titleFallback;
                TextRenderer.DrawText(g, "Sonuçlar", tf, new Point(x - (int)(2 * s), y), p.Text, TextFormatFlags.NoPadding);
            }
            y += (int)((28 + 8) * s);

            int max = 1;
            foreach (var r in rows) if (r.Ms > max) max = r.Ms;
            int best = int.MaxValue;
            foreach (var r in rows) if (r.Ms >= 0 && r.Ms < best) best = r.Ms;

            int nameW = (int)(130 * s), msW = (int)(70 * s);
            int barX = x + nameW, barW = inner - nameW - msW - (int)(8 * s);
            int barH = Math.Max(4, (int)(4 * s));
            using (var bold = new Font(Theme.UiFont, 9f, FontStyle.Bold))
            {
                foreach (var r in rows)
                {
                    int rh = (int)(RowH * s);
                    var nameRect = new Rectangle(x, y, nameW - (int)(8 * s), rh);
                    TextRenderer.DrawText(g, r.Name, r.Current ? bold : Font, nameRect, p.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                    float by = y + rh / 2f - barH / 2f;
                    using (var track = Theme.RoundRect(new RectangleF(barX, by, barW, barH), barH / 2f))
                    using (var tb = new SolidBrush(p.Track))
                        g.FillPath(tb, track);
                    if (r.Ms >= 0)
                    {
                        float fw = Math.Max(barH, barW * (float)r.Ms / max);
                        using (var fill = Theme.RoundRect(new RectangleF(barX, by, fw, barH), barH / 2f))
                        using (var fb = new SolidBrush(r.Ms == best ? p.Accent : p.SubText))
                            g.FillPath(fb, fill);
                    }

                    var msRect = new Rectangle(x + inner - msW, y, msW, rh);
                    TextRenderer.DrawText(g, r.Ms < 0 ? "yanıt yok" : r.Ms + " ms", r.Ms == best ? bold : Font, msRect,
                        r.Ms < 0 ? p.SubText : p.Text,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    y += rh;
                }
            }

            y += (int)(12 * s);
            using (var small = new Font(Theme.UiFont, 8.25f))
                TextRenderer.DrawText(g, note, small, new Rectangle(x, y, inner, footerTop - y), p.SubText, TextFormatFlags.WordBreak);

            // Vurgu renkli "Tamam" düğmesi.
            var br = buttonRect;
            Color fillColor = p.Accent;
            if (buttonDown) fillColor = ControlPaint.Dark(p.Accent, 0.05f);
            else if (buttonHover) fillColor = p.Dark ? ControlPaint.Light(p.Accent, 0.1f) : ControlPaint.Light(p.Accent, 0.15f);
            using (var path = Theme.RoundRect(new RectangleF(br.X, br.Y, br.Width, br.Height), 4 * s))
            using (var b = new SolidBrush(fillColor))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, "Tamam", Font, br, p.AccentText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = buttonRect.Contains(e.Location);
            if (h != buttonHover) { buttonHover = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(buttonRect); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            buttonHover = buttonDown = false;
            Invalidate(buttonRect);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (buttonRect.Contains(e.Location)) { buttonDown = true; Invalidate(buttonRect); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool click = buttonDown && buttonRect.Contains(e.Location);
            buttonDown = false;
            Invalidate(buttonRect);
            if (click) Close();
        }
    }
}
