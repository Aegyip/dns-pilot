using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DnsDegistirici
{
    // Ayarlar penceresinin ortak ölçü, yazı tipi ve renkleri. Pencere açılırken (ve DPI değişince) güncellenir.
    public static class Ui
    {
        public static float S = 1f;
        public static Palette P = Palette.Current();

        public static int D(float v) { return (int)Math.Round(v * S); }

        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        static Font Get(string family, string fallback, float size, FontStyle style = FontStyle.Regular)
        {
            string key = family + "|" + size + "|" + style;
            Font f;
            if (cache.TryGetValue(key, out f)) return f;
            f = new Font(family, size, style);
            if (f.Name != family) { f.Dispose(); f = new Font(fallback, size, style); }
            cache[key] = f;
            return f;
        }

        public static Font Body { get { return Get("Segoe UI Variable Text", "Segoe UI", 10.5f); } }
        public static Font BodyStrong { get { return Get("Segoe UI Variable Text Semibold", "Segoe UI Semibold", 10.5f); } }
        public static Font Caption { get { return Get("Segoe UI Variable Small", "Segoe UI", 9f); } }
        public static Font Subtitle { get { return Get("Segoe UI Variable Display Semib", "Segoe UI Semibold", 15f); } }
        public static Font Title { get { return Get("Segoe UI Variable Display Semib", "Segoe UI Semibold", 21f); } }
        public static Font Icon(float size) { return Get(Theme.IconFont, "Segoe MDL2 Assets", size); }

        public static void Text(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags extra = 0)
        {
            TextRenderer.DrawText(g, text, font, r, color,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | extra);
        }

        public static void Glyph(Graphics g, string glyph, float size, Rectangle r, Color color)
        {
            Text(g, glyph, Icon(size), r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        public static void FillRound(Graphics g, Rectangle r, float radius, Color fill, Color? border = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            using (var path = Theme.RoundRect(rf, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue)
                    using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
            }
        }

        // Metin ölçümü her çağrıda ekran DC'si açtığı için pahalı; aynı metin/genişlik için sonuç saklanır.
        // (Her çizimde ve kaydırma görüntüsü hazırlanırken kartlar onlarca kez ölçülüyor.)
        static readonly Dictionary<string, int> measureCache = new Dictionary<string, int>();

        static int Cached(string key, Func<int> measure)
        {
            int v;
            if (measureCache.TryGetValue(key, out v)) return v;
            if (measureCache.Count > 4000) measureCache.Clear();
            v = measure();
            measureCache[key] = v;
            return v;
        }

        public static int TextHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return Cached("h|" + font.Name + "|" + font.Size + "|" + (int)font.Style + "|" + width + "|" + text, () =>
                TextRenderer.MeasureText(text, font, new Size(width, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height);
        }

        public static int TextWidth(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return Cached("w|" + font.Name + "|" + font.Size + "|" + (int)font.Style + "|" + text, () =>
                TextRenderer.MeasureText(text, font, new Size(int.MaxValue, 0),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
        }
    }

    // Fare durumu (üzerinde / basılı) izleyen, kendini çizen temel denetim.
    public abstract class FluentControl : Control
    {
        protected bool Hover, Down;

        protected FluentControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Default;
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { Down = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool click = Down && ClientRectangle.Contains(e.Location) && e.Button == MouseButtons.Left;
            Down = false;
            Invalidate();
            base.OnMouseUp(e);
            if (click && Enabled) OnActivate();
        }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected virtual void OnActivate() { }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var bg = Parent != null ? Parent.BackColor : Ui.P.Surface;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, ClientRectangle);
        }
    }

    public class FluentButton : FluentControl
    {
        public bool Accent;
        public string Glyph;
        public event EventHandler Pressed;

        public FluentButton(string text, bool accent = false)
        {
            Text = text;
            Accent = accent;
            Font = Ui.Body;
            Size = new Size(Math.Max(Ui.D(96), Ui.TextWidth(text, Ui.Body) + Ui.D(32)), Ui.D(32));
        }

        protected override void OnActivate() { if (Pressed != null) Pressed(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Ui.P;
            var g = e.Graphics;
            var r = ClientRectangle;
            Color fill, fore, border;
            if (Accent)
            {
                fill = !Enabled ? p.Track : Down ? ControlPaint.Dark(p.Accent, 0.05f)
                     : Hover ? (p.Dark ? ControlPaint.Light(p.Accent, 0.1f) : ControlPaint.Light(p.Accent, 0.15f)) : p.Accent;
                fore = Enabled ? p.AccentText : p.SubText;
                border = fill;
            }
            else
            {
                fill = !Enabled ? p.Card : Down ? p.Card : Hover ? p.ControlHover : p.ControlBack;
                fore = Enabled ? (Down ? p.SubText : p.Text) : p.SubText;
                border = p.ControlBorder;
            }
            Ui.FillRound(g, r, Ui.D(4), fill, border);
            if (Glyph != null)
            {
                int w = Ui.TextWidth(Text, Font) + Ui.D(24);
                int x = (r.Width - w) / 2;
                Ui.Glyph(g, Glyph, 10f, new Rectangle(x, 0, Ui.D(16), r.Height), fore);
                Ui.Text(g, Text, Font, new Rectangle(x + Ui.D(24), 0, r.Width, r.Height), fore, TextFormatFlags.VerticalCenter);
            }
            else
            {
                Ui.Text(g, Text, Font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // Windows 11 açma/kapama anahtarı: solunda "Açık"/"Kapalı" yazar.
    public class FluentToggle : FluentControl
    {
        bool on;
        public event EventHandler Toggled;
        public string OnText = "Açık", OffText = "Kapalı";

        public FluentToggle(bool value)
        {
            on = value;
            Font = Ui.Body;
            Size = new Size(Ui.D(104), Ui.D(32));
        }

        public bool Checked
        {
            get { return on; }
            set { on = value; Invalidate(); }
        }

        protected override void OnActivate()
        {
            on = !on;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Ui.P;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int tw = Ui.D(40), th = Ui.D(20);
            var track = new Rectangle(Width - tw - 1, (Height - th) / 2, tw, th);

            Ui.Text(g, on ? OnText : OffText, Font, new Rectangle(0, 0, track.X - Ui.D(12), Height),
                    Enabled ? p.Text : p.SubText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            if (on)
            {
                var fill = !Enabled ? p.SubText : Hover ? (p.Dark ? ControlPaint.Light(p.Accent, 0.1f) : ControlPaint.Light(p.Accent, 0.15f)) : p.Accent;
                Ui.FillRound(g, track, th / 2f, fill);
            }
            else
            {
                Ui.FillRound(g, track, th / 2f, Hover && Enabled ? p.ControlHover : p.ControlBack, Enabled ? p.SubText : p.Track);
            }

            // Düğme basılıyken Windows 11'deki gibi uzar.
            float knob = Down ? Ui.D(14) : Hover ? Ui.D(14) : Ui.D(12);
            float kw = Down ? knob + Ui.D(3) : knob;
            float kx = on ? track.Right - Ui.D(4) - kw - (knob < Ui.D(14) ? Ui.D(1) : 0) : track.X + Ui.D(4) + (knob < Ui.D(14) ? Ui.D(1) : 0);
            float ky = track.Y + (th - knob) / 2f;
            var kc = on ? (Enabled ? p.AccentText : p.ControlBack) : (Enabled ? p.SubText : p.Track);
            using (var path = Theme.RoundRect(new RectangleF(kx, ky, kw, knob), knob / 2f))
            using (var b = new SolidBrush(kc))
                g.FillPath(b, path);
        }
    }

    // Açılır liste: tıklayınca Windows 11 menüsü açar.
    public class FluentDropDown : FluentControl
    {
        readonly List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
        string value;
        public event EventHandler ValueChanged;

        public FluentDropDown(int width)
        {
            Font = Ui.Body;
            Size = new Size(width, Ui.D(32));
        }

        public void Add(string value, string text) { options.Add(new KeyValuePair<string, string>(value, text)); }

        public string Value
        {
            get { return value; }
            set { this.value = value; Invalidate(); }
        }

        string CurrentText
        {
            get
            {
                foreach (var o in options) if (o.Key == value) return o.Value;
                return value ?? "";
            }
        }

        protected override void OnActivate()
        {
            var menu = new ContextMenuStrip();
            foreach (var o in options)
            {
                var opt = o;
                var item = new ToolStripMenuItem(o.Value);
                item.Checked = o.Key == value;
                item.Click += (s, e) =>
                {
                    if (opt.Key == value) return;
                    Value = opt.Key;
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                };
                menu.Items.Add(item);
            }
            Theme.StyleMenu(menu);
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, new Point(0, Height + Ui.D(2)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Ui.P;
            var g = e.Graphics;
            Ui.FillRound(g, ClientRectangle, Ui.D(4), Hover && Enabled ? p.ControlHover : p.ControlBack, p.ControlBorder);
            var tr = new Rectangle(Ui.D(11), 0, Width - Ui.D(44), Height);
            Ui.Text(g, CurrentText, Font, tr, Enabled ? p.Text : p.SubText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            Ui.Glyph(g, "", 8f, new Rectangle(Width - Ui.D(32), 0, Ui.D(24), Height), p.SubText);
        }
    }

    // Ayar satırı: solda simge + başlık + açıklama, sağda bir denetim. Tıklanabilir olabilir (sağda ok).
    public class SettingCard : FluentControl
    {
        public string Glyph, Title, Description;
        public Control Action;
        public bool Clickable;
        public bool Highlight; // seçili öğe: sol kenarda vurgu çizgisi
        public bool Large;     // Windows Update'teki gibi büyük durum kartı
        public Color? GlyphColor;

        Font TitleFont { get { return Large ? Ui.Subtitle : Ui.Body; } }
        public bool GroupTop = true, GroupBottom = true; // ardışık kartlar birleşik görünür
        public event EventHandler Clicked;

        public SettingCard(string glyph, string title, string description, Control action = null)
        {
            Glyph = glyph;
            Title = title;
            Description = description;
            Action = action;
            BackColor = Ui.P.Card;
            if (action != null)
            {
                action.BackColor = Ui.P.Card;
                Controls.Add(action);
            }
        }

        public SettingCard OnClick(EventHandler handler)
        {
            Clickable = true;
            Cursor = Cursors.Hand;
            Clicked += handler;
            return this;
        }

        protected override void OnActivate()
        {
            if (Clickable && Clicked != null) Clicked(this, EventArgs.Empty);
        }

        int LeftText { get { return Large ? Ui.D(96) : Glyph != null ? Ui.D(60) : Ui.D(18); } }

        int ActionWidth { get { return Action != null ? Action.Width + Ui.D(16) : (Clickable ? Ui.D(36) : 0); } }

        // Dar pencerede denetim, Windows 11 Ayarlar'daki gibi yazının altına iner.
        bool Stacked
        {
            get { return Action != null && Width - LeftText - ActionWidth - Ui.D(18) < Ui.D(220); }
        }

        int TextWidthFor(int width)
        {
            int reserve = Stacked ? (Clickable ? Ui.D(36) : 0) : ActionWidth;
            return Math.Max(Ui.D(80), width - LeftText - reserve - Ui.D(18));
        }

        int TextBlockHeight(int textW)
        {
            int h = Ui.TextHeight(Title, TitleFont, textW);
            if (!string.IsNullOrEmpty(Description)) h += Ui.D(2) + Ui.TextHeight(Description, Ui.Caption, textW);
            return h;
        }

        public int Measure(int width)
        {
            Width = width;
            int h = TextBlockHeight(TextWidthFor(width));
            if (Stacked) return h + Ui.D(13 + 10 + 14) + Action.Height;
            if (Large) return Math.Max(Ui.D(108), h + Ui.D(40));
            return Math.Max(Ui.D(string.IsNullOrEmpty(Description) ? 52 : 68), h + Ui.D(26));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Action == null) return;
            if (Stacked)
                Action.Location = new Point(LeftText, Height - Action.Height - Ui.D(14));
            else
                Action.Location = new Point(Width - Action.Width - Ui.D(16) - (Clickable ? Ui.D(28) : 0), (Height - Action.Height) / 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Ui.P;
            var g = e.Graphics;
            var bg = Clickable && Enabled && Hover ? p.CardHover : p.Card;
            BackColor = bg;
            if (Action != null && Action.BackColor != bg) { Action.BackColor = bg; Action.Invalidate(); }

            // Gruplu kartlarda yalnızca dış köşeler yuvarlak.
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Ui.D(4);
            using (var path = new GraphicsPath())
            {
                float d = rad * 2;
                if (GroupTop) { path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90); }
                else { path.AddLine(r.X, r.Y, r.Right, r.Y); }
                if (GroupBottom) { path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); }
                else { path.AddLine(r.Right, r.Bottom, r.X, r.Bottom); }
                path.CloseFigure();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var pen = new Pen(p.CardBorder)) g.DrawPath(pen, path);
            }

            if (Highlight)
            {
                using (var path = Theme.RoundRect(new RectangleF(0, Height / 2f - Ui.D(8), Ui.D(3), Ui.D(16)), Ui.D(1.5f)))
                using (var b = new SolidBrush(p.Accent))
                    g.FillPath(b, path);
            }

            if (Glyph != null)
            {
                // Alt alta düzende simge başlığın hizasında kalır.
                var gr = Large ? new Rectangle(Ui.D(24), 0, Ui.D(48), Height)
                       : Stacked ? new Rectangle(Ui.D(18), Ui.D(13), Ui.D(24), Ui.D(22)) : new Rectangle(Ui.D(18), 0, Ui.D(24), Height);
                Ui.Glyph(g, Glyph, Large ? 30f : 14f, gr, GlyphColor ?? (Enabled ? p.Text : p.SubText));
            }

            int textW = TextWidthFor(Width);
            int th = Ui.TextHeight(Title, TitleFont, textW);
            int dh = string.IsNullOrEmpty(Description) ? 0 : Ui.TextHeight(Description, Ui.Caption, textW) + Ui.D(2);
            int y = Stacked ? Ui.D(13) : (Height - th - dh) / 2;
            Ui.Text(g, Title, TitleFont, new Rectangle(LeftText, y, textW, th), Enabled ? p.Text : p.SubText, TextFormatFlags.WordBreak);
            if (dh > 0)
                Ui.Text(g, Description, Ui.Caption, new Rectangle(LeftText, y + th + Ui.D(2), textW, dh), p.SubText, TextFormatFlags.WordBreak);

            if (Clickable)
                Ui.Glyph(g, "", 9f, new Rectangle(Width - Ui.D(40), 0, Ui.D(24), Height), p.SubText);
        }
    }

    // Kenar çubuğundaki sayfa bağlantısı.
    public class NavItem : FluentControl
    {
        public string Glyph;
        public bool Selected;
        public event EventHandler Pressed;

        public NavItem(string glyph, string text)
        {
            Glyph = glyph;
            Text = text;
            Height = Ui.D(38);
        }

        protected override void OnActivate() { if (Pressed != null) Pressed(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Ui.P;
            var g = e.Graphics;
            if (Selected || Hover)
                Ui.FillRound(g, new Rectangle(0, Ui.D(1), Width, Height - Ui.D(2)), Ui.D(4), Selected ? p.NavSelected : p.NavHover);
            if (Selected)
            {
                float h = Down ? Ui.D(10) : Ui.D(16);
                using (var path = Theme.RoundRect(new RectangleF(0, (Height - h) / 2f, Ui.D(3), h), Ui.D(1.5f)))
                using (var b = new SolidBrush(p.Accent))
                    g.FillPath(b, path);
            }
            Ui.Glyph(g, Glyph, 12f, new Rectangle(Ui.D(14), 0, Ui.D(20), Height), p.Text);
            if (Width > Ui.D(80)) // dar pencerede yalnızca simge
                Ui.Text(g, Text, Ui.Body, new Rectangle(Ui.D(48), 0, Width - Ui.D(52), Height), p.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Düz metin (başlık, bölüm adı, açıklama). Arka planı ebeveyninden alır.
    public class FluentLabel : Control
    {
        public Color? Color;
        public bool Wrap = true;

        public FluentLabel(string text, Font font, Color? color = null)
        {
            Text = text;
            Font = font;
            Color = color;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public int Measure(int width)
        {
            Width = width;
            return Ui.TextHeight(Text, Font, width) + Ui.D(2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Ui.P.Surface))
                e.Graphics.FillRectangle(b, ClientRectangle);
            Ui.Text(e.Graphics, Text, Font, ClientRectangle, Color ?? Ui.P.Text, Wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Çocuklarını alt alta dizen, genişliğe uyduran, kendi kaydırmasını yapan panel.
    //
    // Akıcı kaydırma:
    //  - Kartlar tek bir taşıyıcı katmanda (host) durur. Hareket başlayınca yalnızca bu katman gizlenir
    //    ve panel, sayfanın hazır görüntüsünü kaydırarak çizer (tek blit, ~1 ms).
    //  - Her kartın görüntüsü ayrı saklanır; yalnızca değişen kart (Invalidated) yeniden çizilir.
    //    Sayfa açılınca görüntüler boşta (Application.Idle) önceden hazırlanır, ilk harekette takılma olmaz.
    //  - Kareler DwmFlush ile ekran yenilemesine (vsync) bağlanır; hız zamana göre hesaplanır.
    //  - Hareket bitince kartlar son konumlarına yerleştirilip katman tek seferde gösterilir.
    public class StackPanel : Panel
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmFlush();

        readonly Panel host = new HostPanel();
        readonly Dictionary<Control, int> gapBefore = new Dictionary<Control, int>();
        readonly Dictionary<Control, Bitmap> pieces = new Dictionary<Control, Bitmap>();
        readonly HashSet<Control> dirty = new HashSet<Control>();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public int MaxContentWidth = 1000;

        float offset;        // şu anki kaydırma (piksel)
        float target;        // yumuşak kaydırmanın hedefi
        int contentHeight;   // tüm içeriğin yüksekliği
        bool barHover, dragging;
        int dragStartY;
        float dragStartOffset;

        volatile bool animating;
        int framePending;
        long lastTicks;
        bool snapshotActive;
        bool prewarmQueued;

        // Ölçüm (test için)
        public int FrameCount { get; private set; }
        public double SnapshotMs { get; private set; }
        public double RenderMs { get; private set; }
        public double MaxFrameMs { get; private set; }
        public double TotalFrameMs { get; private set; }

        // Kartları tutan katman: kendi arka planını çizer, tekerleği panele bırakır.
        class HostPanel : Panel
        {
            public HostPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }
        }

        public StackPanel()
        {
            AutoScroll = false;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Controls.Add(host);
        }

        public Control.ControlCollection Items { get { return host.Controls; } }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            host.BackColor = BackColor;
            InvalidateAllPieces();
        }

        protected override void Dispose(bool disposing)
        {
            animating = false;
            if (disposing) { ClearPieces(); if (prewarmTimer != null) prewarmTimer.Dispose(); }
            base.Dispose(disposing);
        }

        int MaxOffset { get { return Math.Max(0, contentHeight - ClientSize.Height); } }

        public int ScrollOffset
        {
            get { return (int)Math.Round(offset); }
            set
            {
                StopAnimation();
                offset = target = Math.Max(0, Math.Min(MaxOffset, value));
                Arrange();
                Invalidate();
            }
        }

        public T Add<T>(T c, int gap) where T : Control
        {
            gapBefore[c] = gap;
            Watch(c, c);
            host.Controls.Add(c);
            dirty.Add(c);
            QueuePrewarm();
            return c;
        }

        // Kartın ya da içindeki düğme/anahtarın görünümü değişince o kartın görüntüsü yenilenir.
        void Watch(Control owner, Control c)
        {
            c.Invalidated += (s, e) => { if (!rendering) { dirty.Add(owner); QueuePrewarm(); } };
            c.ControlAdded += (s, e) => Watch(owner, e.Control);
            foreach (Control child in c.Controls) Watch(owner, child);
        }

        public void Clear()
        {
            StopAnimation();
            var old = new List<Control>();
            foreach (Control c in host.Controls) old.Add(c);
            host.Controls.Clear();
            gapBefore.Clear();
            foreach (var c in old) c.Dispose();
            ClearPieces();
        }

        void ClearPieces()
        {
            foreach (var b in pieces.Values) b.Dispose();
            pieces.Clear();
            dirty.Clear();
        }

        void InvalidateAllPieces()
        {
            foreach (Control c in host.Controls) dirty.Add(c);
            QueuePrewarm();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            Arrange();
            base.OnLayout(e);
        }

        bool arranging;

        public void Arrange()
        {
            if (arranging) return;
            arranging = true;
            try { ArrangeCore(); }
            finally { arranging = false; }
        }

        int Gutter { get { return Ui.D(12); } } // kaydırma çubuğunun alanı; kartlar ona taşmaz

        void ArrangeCore()
        {
            var hostBounds = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - Gutter), ClientSize.Height);
            if (host.Bounds != hostBounds) host.Bounds = hostBounds;

            int pad = Ui.D(36);
            int avail = Math.Max(Ui.D(120), hostBounds.Width - 2 * pad);
            int width = Math.Min(avail, Ui.D(MaxContentWidth));

            int y = Ui.D(4);
            var heights = new List<int>();
            foreach (Control c in host.Controls)
            {
                int gap;
                gapBefore.TryGetValue(c, out gap);
                y += gap;
                int h;
                var card = c as SettingCard;
                var label = c as FluentLabel;
                if (card != null) h = card.Measure(width);
                else if (label != null) h = label.Measure(width);
                else h = c.Height;
                heights.Add(h);
                y += h;
            }
            contentHeight = y + Ui.D(36);
            if (offset > MaxOffset) offset = target = MaxOffset;

            arrangedOffset = (int)Math.Round(offset);
            int top = Ui.D(4) - arrangedOffset;
            int i = 0;
            foreach (Control c in host.Controls)
            {
                int gap;
                gapBefore.TryGetValue(c, out gap);
                top += gap;
                int h = heights[i++];
                var b = new Rectangle(pad, top, c is FluentButton ? c.Width : width, h);
                if (c.Bounds != b) c.SetBounds(b.X, b.Y, b.Width, b.Height);
                top += h;
            }
        }

        // ---- Kart görüntüleri ----

        bool rendering;

        Timer prewarmTimer;

        // Değişen kartların görüntüsü kısa bir gecikmeyle, küçük parçalar halinde yeniden hazırlanır.
        void QueuePrewarm()
        {
            if (prewarmQueued || IsDisposed) return;
            prewarmQueued = true;
            if (prewarmTimer == null)
            {
                prewarmTimer = new Timer { Interval = 60 };
                prewarmTimer.Tick += Prewarm;
            }
            prewarmTimer.Start();
        }

        public int DirtyCount { get { return dirty.Count + host.Controls.Count - pieces.Count; } }

        void Prewarm(object sender, EventArgs e)
        {
            prewarmTimer.Stop();
            prewarmQueued = false;
            if (IsDisposed || animating) return;
            // Boştayken en fazla ~8 ms çalış; kalanı sonraki boşta.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (Control c in host.Controls)
            {
                if (sw.ElapsedMilliseconds > 8) { QueuePrewarm(); return; }
                GetPiece(c);
            }
        }

        Bitmap GetPiece(Control c)
        {
            Bitmap b;
            bool have = pieces.TryGetValue(c, out b);
            if (have && !dirty.Contains(c) && b.Width == c.Width && b.Height == c.Height) return b;
            if (have) b.Dispose();
            if (c.Width <= 0 || c.Height <= 0) { pieces.Remove(c); return null; }
            // Önce 32bppRgb'ye çizilir (GDI metni alfa kanalını bozmaz), sonra ekrana en hızlı
            // aktarılan biçime (önceden çarpılmış ARGB) çevrilir.
            rendering = true;
            try
            {
                using (var rgb = new Bitmap(c.Width, c.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (var g = Graphics.FromImage(rgb)) RenderTree(g, c, Point.Empty);
                    b = rgb.Clone(new Rectangle(0, 0, c.Width, c.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                }
            }
            finally { rendering = false; }
            pieces[c] = b;
            dirty.Remove(c);
            return b;
        }

        // Denetimi ve alt denetimlerini kendi çizim kodlarıyla bitmap'e çizer.
        // TextRenderer (GDI) Graphics'in konum dönüşümünü uygulamadığı için alt denetimler
        // dönüşümle değil, kendi bitmap'lerine çizilip yerlerine yapıştırılır.
        void RenderTree(Graphics g, Control c, Point origin)
        {
            using (var own = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height), System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (var cg = Graphics.FromImage(own))
                using (var pe = new PaintEventArgs(cg, new Rectangle(0, 0, c.Width, c.Height)))
                {
                    InvokePaintBackground(c, pe);
                    InvokePaint(c, pe);
                    for (int i = c.Controls.Count - 1; i >= 0; i--)
                    {
                        var child = c.Controls[i];
                        if (!child.Visible || child.Width <= 0 || child.Height <= 0) continue;
                        RenderTree(cg, child, child.Location);
                    }
                }
                g.DrawImageUnscaled(own, origin.X, origin.Y);
            }
        }
        // ---- Görüntü ile kaydırma ----

        void BeginSnapshot()
        {
            if (snapshotActive || !IsHandleCreated || host.Controls.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (Control c in host.Controls) GetPiece(c);
            RenderMs = sw.Elapsed.TotalMilliseconds;
            snapshotActive = true;
            host.Visible = false; // tek pencere gizlenir; panel görüntüyü çizer
            SnapshotMs = sw.Elapsed.TotalMilliseconds;
            Invalidate();
        }

        void EndSnapshot()
        {
            if (!snapshotActive) return;
            snapshotActive = false;
            if (IsHandleCreated && !IsDisposed)
            {
                host.SuspendLayout();
                Arrange();
                host.ResumeLayout(false);
                host.Visible = true;
                Invalidate(Track);
            }
        }

        // ---- vsync'e bağlı animasyon ----

        void StartAnimation()
        {
            BeginSnapshot();
            if (animating) return;
            animating = true;
            FrameCount = 0;
            MaxFrameMs = 0;
            lastTicks = clock.ElapsedTicks;
            framePending = 0;
            var thread = new System.Threading.Thread(() =>
            {
                while (animating)
                {
                    if (DwmFlush() != 0) System.Threading.Thread.Sleep(8);
                    if (!animating) break;
                    if (System.Threading.Interlocked.CompareExchange(ref framePending, 1, 0) != 0) continue;
                    try { BeginInvoke(new Action(Frame)); }
                    catch (Exception) { animating = false; }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        void StopAnimation()
        {
            animating = false;
            EndSnapshot();
        }

        void Frame()
        {
            System.Threading.Interlocked.Exchange(ref framePending, 0);
            if (!animating || IsDisposed) return;
            long now = clock.ElapsedTicks;
            double dt = Math.Min(0.05, (now - lastTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
            lastTicks = now;

            if (!dragging)
            {
                float diff = target - offset;
                if (Math.Abs(diff) < 0.5f)
                {
                    offset = target;
                    StopAnimation();
                    return;
                }
                offset += diff * (float)(1 - Math.Exp(-dt / 0.075));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Invalidate();
            Update();
            FrameCount++;
            MaxFrameMs = Math.Max(MaxFrameMs, sw.Elapsed.TotalMilliseconds);
            TotalFrameMs += sw.Elapsed.TotalMilliseconds;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            ScrollBy(-e.Delta / 120f * Ui.D(120));
        }

        public void ScrollBy(float delta)
        {
            if (MaxOffset <= 0) return;
            target = Math.Max(0, Math.Min(MaxOffset, target + delta));
            if (Math.Abs(target - offset) < 0.5f) return;
            StartAnimation();
        }

        // ---- Çizim ----

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, e.ClipRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (snapshotActive)
            {
                int off = (int)Math.Round(offset);
                var view = new Rectangle(0, 0, ClientSize.Width - Gutter, ClientSize.Height);
                g.SetClip(view);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                foreach (Control c in host.Controls)
                {
                    Bitmap b;
                    if (!pieces.TryGetValue(c, out b) || b == null) continue;
                    // Kartların konumu son Arrange'daki offset'e göre; şimdiki offset farkı kadar kaydır.
                    int y = c.Top + arrangedOffset - off;
                    if (y + b.Height <= 0 || y >= view.Height) continue;
                    g.DrawImage(b, c.Left, y, b.Width, b.Height);
                }
                g.ResetClip();
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
            }
            DrawThumb(g);
        }

        // Kartlar en son hangi kaydırma değerinde yerleştirildi (görüntüyle kaydırırken taşınmazlar).
        int arrangedOffset;

        // ---- Windows 11 tarzı kaydırma çubuğu ----

        Rectangle Track
        {
            get
            {
                int w = Gutter;
                return new Rectangle(ClientSize.Width - w, Ui.D(4), w, ClientSize.Height - Ui.D(8));
            }
        }

        Rectangle Thumb
        {
            get
            {
                var t = Track;
                if (MaxOffset <= 0 || contentHeight <= 0) return Rectangle.Empty;
                int h = Math.Max(Ui.D(40), (int)(t.Height * (float)ClientSize.Height / contentHeight));
                int y = t.Y + (int)((t.Height - h) * (offset / MaxOffset));
                int bw = barHover || dragging ? Ui.D(6) : Ui.D(3);
                return new Rectangle(t.Right - Ui.D(3) - bw, y, bw, h);
            }
        }

        void DrawThumb(Graphics g)
        {
            var th = Thumb;
            if (th.IsEmpty) return;
            var color = Ui.P.Dark ? Color.FromArgb(barHover || dragging ? 160 : 110, 255, 255, 255)
                                  : Color.FromArgb(barHover || dragging ? 140 : 100, 0, 0, 0);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(th.X, th.Y, th.Width, th.Height), th.Width / 2f))
            using (var b = new SolidBrush(color))
                g.FillPath(b, path);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                var t = Track;
                float range = Math.Max(1, t.Height - Thumb.Height);
                offset = target = Math.Max(0, Math.Min(MaxOffset, dragStartOffset + (e.Y - dragStartY) * MaxOffset / range));
                return;
            }
            bool h = Track.Contains(e.Location) && MaxOffset > 0;
            if (h != barHover) { barHover = h; Invalidate(Track); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!dragging && barHover) { barHover = false; Invalidate(Track); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || MaxOffset <= 0 || !Track.Contains(e.Location)) return;
            var th = Thumb;
            if (e.Y < th.Top) ScrollBy(-ClientSize.Height * 0.9f);
            else if (e.Y > th.Bottom) ScrollBy(ClientSize.Height * 0.9f);
            else
            {
                dragging = true;
                dragStartY = e.Y;
                dragStartOffset = offset;
                target = offset;
                Capture = true;
                StartAnimation();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            barHover = Track.Contains(e.Location);
            target = offset;
        }

        protected override void OnResize(EventArgs e)
        {
            StopAnimation();
            base.OnResize(e);
            Invalidate();
        }
    }
}
