using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DnsDegistirici
{
    // Ayarlar penceresinin uygulamadan istediği işler (TrayContext uygular).
    public interface IAppActions
    {
        bool Busy { get; }
        event Action StateChanged;
        void ApplyProfile(DnsProfile profile);
        void ResetDns();
        void FlushCache();
        void SpeedTest();
        void StartZapret();
        void StopZapret();
        UpdateState Updates { get; }
        void CheckUpdates();
        void InstallUpdates();
        bool AutostartEnabled { get; }
        void SetAutostart(bool on);
        void FindStrategy();
        bool StrategyTestRunning { get; }
        string StrategyTestStatus { get; }
    }

    // Windows 11 Ayarlar düzeni: solda kenar çubuğu, sağda sayfa.
    public class SettingsForm : Form
    {
        class Page
        {
            public string Glyph, Name;
            public Action<StackPanel> Build;
            public NavItem Nav;
        }

        readonly IAppActions app;
        readonly List<Page> pages = new List<Page>();
        readonly Panel sidebar = new BackdropPanel();
        readonly StackPanel content = new StackPanel();
        readonly FluentLabel appTitle;
        readonly PictureBox logoBox = new PictureBox(); // kenar çubuğunun başında uygulama logosu
        Page current;

        public SettingsForm(IAppActions app, string startPage = null)
        {
            this.app = app;
            Ui.P = Palette.Current();
            Ui.S = DeviceDpi / 96f;

            Text = "DNS Pilot · Ayarlar";
            Icon = Theme.AppIcon;
            ShowIcon = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Body;
            Size = new Size(Ui.D(1000), Ui.D(700));
            MinimumSize = new Size(Ui.D(520), Ui.D(420));
            DoubleBuffered = true;

            pages.Add(new Page { Glyph = "", Name = "Giriş", Build = BuildHome });
            pages.Add(new Page { Glyph = "", Name = "DNS", Build = BuildDns });
            pages.Add(new Page { Glyph = "", Name = "Zapret", Build = BuildZapret });
            pages.Add(new Page { Glyph = "", Name = "Sistem", Build = BuildSystem });
            pages.Add(new Page { Glyph = "\uE895", Name = "Güncellemeler", Build = BuildUpdates });

            sidebar.Dock = DockStyle.Left;
            content.Dock = DockStyle.Fill;
            content.MaxContentWidth = 1000;
            Controls.Add(content);
            Controls.Add(sidebar);

            // Logo ve uygulama adı başlık çubuğunda (Windows Ayarlar düzeni); kenar çubuğunda yalnızca sayfalar.
            appTitle = new FluentLabel("DNS Pilot", Ui.Caption);
            logoBox.SizeMode = PictureBoxSizeMode.Zoom;
            foreach (var p in pages)
            {
                var page = p;
                page.Nav = new NavItem(p.Glyph, p.Name);
                page.Nav.Pressed += (s, e) => Navigate(page);
                sidebar.Controls.Add(page.Nav);
            }
            sidebar.Resize += (s, e) => LayoutSidebar();

            ApplyColors();
            Navigate(pages.FirstOrDefault(x => x.Name == startPage) ?? pages[0]);

            app.StateChanged += OnStateChanged;
            SystemEvents.UserPreferenceChanged += OnSystemTheme;
        }

        // ---- Windows 11 Ayarlar düzeninde, pencereyle bütünleşik başlık çubuğu ----
        //
        // Standart başlık çubuğu WM_NCCALCSIZE ile kaldırılır; tüm pencere istemci alanı olur ve Mica arka planı
        // başlığın altına kesintisiz uzanır. Sürükleme, yeniden boyutlandırma ve düğmeler WM_NCHITTEST ile
        // sisteme bildirilir; büyütme düğmesinde Windows 11'in Snap düzenleri menüsü çalışır.

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, int flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct TRACKMOUSEEVENT { public int cbSize; public int dwFlags; public IntPtr hwnd; public int dwHoverTime; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1,
                  WM_NCLBUTTONUP = 0x00A2, WM_NCLBUTTONDBLCLK = 0x00A3, WM_NCMOUSELEAVE = 0x02A2,
                  WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
        const int HTCLIENT = 1, HTCAPTION = 2, HTMINBUTTON = 8, HTMAXBUTTON = 9, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
                  HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17, HTCLOSE = 20;

        int hoverButton, pressedButton;
        bool inSizeMove;

        int TitleH { get { return Ui.D(48); } }
        int ResizeBorder { get { return WindowState == FormWindowState.Maximized ? 0 : Ui.D(6); } }

        Rectangle ButtonRect(int code)
        {
            int w = Ui.D(46), h = Ui.D(32);
            int index = code == HTCLOSE ? 0 : code == HTMAXBUTTON ? 1 : 2; // sağdan sola
            return new Rectangle(ClientSize.Width - w * (index + 1), 0, w, h);
        }

        // Kenar çubuğu ve sayfa başlığın altından başlar; kenarlarda yeniden boyutlandırma payı kalır.
        // Pay da Mica ile boyandığı için görünmez.
        void UpdateChromePadding()
        {
            int b = ResizeBorder;
            var pad = new Padding(b, TitleH, b, b);
            if (Padding != pad) Padding = pad;
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCCALCSIZE:
                    if (m.WParam != IntPtr.Zero)
                    {
                        // Büyütülmüşken pencere ekran dışına taşan çerçeve kadar içeri alınır.
                        if (WindowState == FormWindowState.Maximized)
                        {
                            var r = (RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(RECT));
                            int pad = GetSystemMetrics(92); // SM_CXPADDEDBORDER
                            int fx = GetSystemMetrics(32) + pad, fy = GetSystemMetrics(33) + pad;
                            r.Left += fx; r.Top += fy; r.Right -= fx; r.Bottom -= fy;
                            System.Runtime.InteropServices.Marshal.StructureToPtr(r, m.LParam, false);
                        }
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case WM_NCHITTEST:
                    {
                        int x = (short)((long)m.LParam & 0xFFFF), y = (short)(((long)m.LParam >> 16) & 0xFFFF);
                        m.Result = (IntPtr)HitTest(PointToClient(new Point(x, y)));
                        return;
                    }

                case WM_NCMOUSEMOVE:
                    {
                        int code = (int)m.WParam;
                        int hover = code == HTMINBUTTON || code == HTMAXBUTTON || code == HTCLOSE ? code : 0;
                        if (hover != hoverButton) { hoverButton = hover; Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleH)); }
                        var tme = new TRACKMOUSEEVENT { cbSize = 16 + IntPtr.Size, dwFlags = 0x10 | 0x2, hwnd = Handle }; // TME_NONCLIENT | TME_LEAVE
                        TrackMouseEvent(ref tme);
                        if (hover != 0) { m.Result = IntPtr.Zero; return; }
                        break;
                    }

                case WM_NCMOUSELEAVE:
                    if (hoverButton != 0 || pressedButton != 0)
                    {
                        hoverButton = pressedButton = 0;
                        Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleH));
                    }
                    break;

                case WM_NCLBUTTONDOWN:
                case WM_NCLBUTTONDBLCLK:
                    {
                        int code = (int)m.WParam;
                        if (code == HTMINBUTTON || code == HTMAXBUTTON || code == HTCLOSE)
                        {
                            pressedButton = code;
                            Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleH));
                            m.Result = IntPtr.Zero;
                            return;
                        }
                        break;
                    }

                case WM_NCLBUTTONUP:
                    {
                        int code = (int)m.WParam;
                        if (pressedButton != 0)
                        {
                            int pressed = pressedButton;
                            pressedButton = 0;
                            Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleH));
                            if (code == pressed)
                            {
                                if (code == HTCLOSE) Close();
                                else if (code == HTMINBUTTON) WindowState = FormWindowState.Minimized;
                                else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                            }
                            m.Result = IntPtr.Zero;
                            return;
                        }
                        break;
                    }

                case WM_ENTERSIZEMOVE:
                    inSizeMove = true;
                    break;

                case WM_EXITSIZEMOVE:
                    inSizeMove = false;
                    RefreshBackdrop();
                    break;
            }
            base.WndProc(ref m);
        }

        int HitTest(Point p)
        {
            int b = ResizeBorder;
            if (b > 0)
            {
                bool left = p.X < b, right = p.X >= ClientSize.Width - b, top = p.Y < b, bottom = p.Y >= ClientSize.Height - b;
                if (top && left) return HTTOPLEFT;
                if (top && right) return HTTOPRIGHT;
                if (bottom && left) return HTBOTTOMLEFT;
                if (bottom && right) return HTBOTTOMRIGHT;
                if (left) return HTLEFT;
                if (right) return HTRIGHT;
                if (bottom) return HTBOTTOM;
                if (top) return HTTOP;
            }
            if (p.Y < TitleH)
            {
                if (ButtonRect(HTCLOSE).Contains(p)) return HTCLOSE;
                if (ButtonRect(HTMAXBUTTON).Contains(p)) return HTMAXBUTTON;
                if (ButtonRect(HTMINBUTTON).Contains(p)) return HTMINBUTTON;
                return HTCAPTION;
            }
            return HTCLIENT;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Backdrop.Paint(e.Graphics, this, e.ClipRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Ui.P;
            bool active = Backdrop.Active;

            // Logo + uygulama adı
            int logo = Ui.D(16);
            using (var bmp = Theme.Logo(logo))
                g.DrawImage(bmp, Ui.D(16), (Ui.D(32) - logo) / 2 + Ui.D(8));
            Ui.Text(g, "DNS Pilot", Ui.Caption, new Rectangle(Ui.D(44), Ui.D(8), Ui.D(300), Ui.D(32)),
                    active ? p.Text : p.SubText, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            // Başlık düğmeleri (Windows 11: 46×32, kapat üzerindeyken kırmızı)
            foreach (int code in new[] { HTMINBUTTON, HTMAXBUTTON, HTCLOSE })
            {
                var r = ButtonRect(code);
                bool hover = hoverButton == code, down = pressedButton == code;
                Color fore = active ? p.Text : p.SubText;
                if (code == HTCLOSE && (hover || down))
                {
                    using (var b = new SolidBrush(down ? Color.FromArgb(200, 196, 43, 28) : Color.FromArgb(196, 43, 28)))
                        g.FillRectangle(b, r);
                    fore = Color.White;
                }
                else if (hover || down)
                {
                    var c = p.Dark ? Color.FromArgb(down ? 11 : 15, 255, 255, 255) : Color.FromArgb(down ? 6 : 10, 0, 0, 0);
                    using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
                }
                string glyph = code == HTCLOSE ? "" : code == HTMINBUTTON ? ""
                             : WindowState == FormWindowState.Maximized ? "" : "";
                Ui.Glyph(g, glyph, 7.5f, r, fore);
            }
        }

        // Mica, pencerenin ekrandaki konumuna ve etkinliğine bağlı: değişince her şey yeniden çizilir.
        void RefreshBackdrop()
        {
            if (!IsHandleCreated) return;
            content.InvalidateAllPieces();
            Invalidate(true);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Backdrop.Active = true;
            RefreshBackdrop();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Backdrop.Active = false;
            RefreshBackdrop();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (!inSizeMove) RefreshBackdrop(); // Win+ok tuşlarıyla yerleştirme gibi anlık taşımalar
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsHandleCreated) { UpdateChromePadding(); Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleH)); }
        }

        // Kenar çubuğu: arka planı Mica.
        class BackdropPanel : Panel
        {
            public BackdropPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Backdrop.Paint(e.Graphics, this, e.ClipRectangle);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            app.StateChanged -= OnStateChanged;
            SystemEvents.UserPreferenceChanged -= OnSystemTheme;
            base.OnFormClosed(e);
        }

        // WS_EX_COMPOSITED: pencere ve tüm alt denetimler tek bir arka tamponda çizilip ekrana bir kerede aktarılır.
        // Olmadan, hızlı kaydırmada her kart ayrı ayrı boyandığı için anlık yırtılma/titreme görülüyordu.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindow(Handle, Ui.P, false);
            // Standart başlık çubuğunu kaldırmak için çerçeveyi yeniden hesaplat (WM_NCCALCSIZE).
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            UpdateChromePadding();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Ui.S = e.DeviceDpiNew / 96f;
            foreach (var p in pages) p.Nav.Height = Ui.D(38);
            ApplyColors();
            Rebuild();
        }

        void OnSystemTheme(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Desktop)
            {
                // Duvar kâğıdı değişti: Mica yeniden hesaplanır.
                BeginInvoke(new Action(() => { Backdrop.Reset(); RefreshBackdrop(); }));
                return;
            }
            if (e.Category != UserPreferenceCategory.General) return;
            BeginInvoke(new Action(() =>
            {
                Ui.P = Palette.Current();
                Theme.ApplyWindow(Handle, Ui.P, false);
                ApplyColors();
                Rebuild();
            }));
        }

        void OnStateChanged()
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Rebuild));
        }

        void ApplyColors()
        {
            BackColor = sidebar.BackColor = content.BackColor = Ui.P.Surface;
            if (IsHandleCreated) UpdateChromePadding();
            foreach (Control c in sidebar.Controls) c.Invalidate();
            appTitle.Color = Ui.P.Text;
            logoBox.BackColor = Ui.P.Surface;
            var old = logoBox.Image;
            logoBox.Image = Theme.Logo(Ui.D(20));
            if (old != null) old.Dispose();
            UpdateSidebarWidth();
            LayoutSidebar();
        }

        // Dar pencerede kenar çubuğu Windows 11'deki gibi yalnızca simgelere iner.
        bool Compact { get { return ClientSize.Width < Ui.D(860); } }

        void UpdateSidebarWidth()
        {
            int w = Compact ? Ui.D(68) : Ui.D(280);
            if (sidebar.Width != w) sidebar.Width = w;
            appTitle.Visible = !Compact;
            foreach (var p in pages) navTips.SetToolTip(p.Nav, Compact ? p.Name : null);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (pages.Count > 0 && pages[0].Nav != null) UpdateSidebarWidth();
        }

        readonly ToolTip navTips = new ToolTip();

        void LayoutSidebar()
        {
            int w = sidebar.Width - Ui.D(24);
            // Dar kenar çubuğunda yalnızca logo, ortalanmış; genişte logo + ad.
            logoBox.SetBounds(Compact ? (sidebar.Width - Ui.D(20)) / 2 : Ui.D(20), Ui.D(16), Ui.D(20), Ui.D(20));
            appTitle.SetBounds(Ui.D(50), Ui.D(18), Math.Max(1, w - Ui.D(30)), Ui.D(20));
            int y = Ui.D(4);
            foreach (var p in pages)
            {
                p.Nav.SetBounds(Ui.D(12), y, w, Ui.D(38));
                y += Ui.D(40);
            }
        }

        // Sayfaya adıyla git (bildirimden ya da başka sayfadan).
        public void GoTo(string name)
        {
            var page = pages.FirstOrDefault(x => x.Name == name);
            if (page != null) Navigate(page);
        }

        void Navigate(Page page)
        {
            current = page;
            foreach (var p in pages) { p.Nav.Selected = p == page; p.Nav.Invalidate(); }
            Rebuild();
            content.ScrollOffset = 0;
        }

        void Rebuild()
        {
            if (current == null) return;
            int scroll = content.ScrollOffset;
            content.SuspendLayout();
            content.Clear();
            content.Add(new FluentLabel(current.Name, Ui.Title), Ui.D(28));
            current.Build(content);
            content.ResumeLayout(true);
            content.Arrange();
            content.ScrollOffset = scroll;
        }

        // ---- Yardımcılar ----

        static void Section(StackPanel s, string text)
        {
            s.Add(new FluentLabel(text, Ui.BodyStrong), Ui.D(26));
        }

        // Ardışık kartları Windows 11'deki gibi bitişik bir grup yapar.
        static void Group(StackPanel s, int gapFirst, params SettingCard[] cards)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].GroupTop = i == 0;
                cards[i].GroupBottom = i == cards.Length - 1;
                s.Add(cards[i], i == 0 ? gapFirst : 0);
            }
        }

        static FluentLabel Badge(string text, Color color)
        {
            var l = new FluentLabel(text, Ui.Body, color);
            l.Size = new Size(Ui.TextWidth(text, Ui.Body) + Ui.D(4), Ui.D(22));
            return l;
        }

        static FluentButton Button(string text, Action onClick, bool enabled = true, bool accent = false)
        {
            var b = new FluentButton(text, accent);
            b.Enabled = enabled;
            b.Pressed += (s, e) => onClick();
            return b;
        }

        static void OpenInNotepad(string path)
        {
            Process.Start("notepad.exe", "\"" + path + "\"");
        }

        static string ShortSha(string sha)
        {
            return sha.Length >= 7 ? sha.Substring(0, 7) : "bilinmiyor";
        }

        static string StatusText(DnsState state)
        {
            if (!state.HasAdapter) return "Bağlantı yok";
            string name = !state.IsStatic ? "Otomatik" : (state.Matched ?? "Özel sunucu");
            return name + " · " + (state.DohOn ? "Şifreli (DoH)" : state.IsStatic ? "Şifresiz" : "Servis sağlayıcı DNS'i");
        }

        // ---- Sayfalar ----

        void BuildHome(StackPanel s)
        {
            var profiles = ProfileStore.Load();
            var state = DnsManager.GetState(profiles);
            bool zapretOn = Zapret.IsRunning();
            var strategy = CurrentStrategy();

            string servers = state.Servers.Count == 0 ? "—" : string.Join(", ", state.Servers.Take(4));
            var dnsCard = new SettingCard("", StatusText(state),
                state.HasAdapter ? state.AdapterName + " bağlantısı · Sunucular: " + servers
                                 : "Etkin bir ağ bağlantısı algılanmadı.",
                Button("Değiştir", () => GoTo("DNS")));

            var toggle = new FluentToggle(zapretOn);
            toggle.Enabled = !app.Busy && Zapret.Installed;
            toggle.Toggled += (o, e) => { if (toggle.Checked) app.StartZapret(); else app.StopZapret(); };
            var zapretCard = new SettingCard("", "Zapret",
                !Zapret.Installed ? "Zapret motoru bulunamadı. Güncellemeler sayfasından yeniden yükleyebilirsiniz."
                : zapretOn ? "Etkin · " + (strategy != null ? strategy.Name : "Varsayılan") + " stratejisi kullanılıyor"
                : "Devre dışı · Erişim kısıtlamalarını aşmak için etkinleştirin",
                toggle);
            Group(s, Ui.D(16), dnsCard, zapretCard);

            var u = app.Updates;
            if (u.ZapretAvailable && !u.Installing)
                Group(s, Ui.D(8), new SettingCard("", "Güncelleme kullanılabilir",
                    "Zapret motorunun yeni bir sürümü yüklenmeye hazır.",
                    Button("Görüntüle", () => GoTo("Güncellemeler"), true, true)));

            Section(s, "Araçlar");
            var speed = new SettingCard("", "DNS hız testi", "Kayıtlı DNS sunucularının yanıt sürelerini ölçer ve karşılaştırır.")
                .OnClick((o, e) => app.SpeedTest());
            var flush = new SettingCard("", "DNS önbelleğini temizle",
                "Bir site açılmıyorsa veya eski bir adrese yönleniyorsa, kayıtlı DNS yanıtlarını temizleyerek sorunu giderir.",
                Button("Temizle", app.FlushCache, !app.Busy));
            Group(s, Ui.D(8), speed, flush);
        }

        void BuildDns(StackPanel s)
        {
            var profiles = ProfileStore.Load();
            var state = DnsManager.GetState(profiles);

            s.Add(new FluentLabel("Bağlantınızın alan adlarını çözmek için kullanacağı DNS sunucusunu seçin. Şifreli (DoH) sunucular, " +
                                  "sorgularınızı internet servis sağlayıcınızın görmesini ve yönlendirmesini engeller.",
                                  Ui.Body, Ui.P.SubText), Ui.D(8));

            Section(s, "Sunucular");
            var cards = new List<SettingCard>();
            foreach (var p in profiles)
            {
                var profile = p;
                bool active = state.IsStatic && state.Matched == p.Name;
                string desc = string.Join(", ", p.V4) + " · " + (p.Doh != null ? "Şifreli (DNS over HTTPS)" : "Şifresiz");
                Control action = active ? (Control)Badge("✓ Kullanılıyor", Ui.P.Success)
                                        : Button("Kullan", () => app.ApplyProfile(profile), !app.Busy);
                var card = new SettingCard(p.Doh != null ? "" : "", p.Name, desc, action);
                card.Highlight = active;
                cards.Add(card);
            }
            bool auto = state.HasAdapter && !state.IsStatic;
            var autoCard = new SettingCard("", "Otomatik",
                "DNS sunucusunu modeminiz veya servis sağlayıcınız belirler. Bağlantı sorunlarında varsayılan ayara dönmek için kullanın.",
                auto ? (Control)Badge("✓ Kullanılıyor", Ui.P.Success) : Button("Kullan", app.ResetDns, !app.Busy));
            autoCard.Highlight = auto;
            cards.Add(autoCard);
            Group(s, Ui.D(8), cards.ToArray());

            Section(s, "Gelişmiş");
            Group(s, Ui.D(8),
                new SettingCard("", "DNS profillerini düzenle",
                    "Listeye özel sunucu ekleyin veya mevcut sunucuları kaldırın. Değişiklikler sayfa yeniden açıldığında uygulanır.")
                    .OnClick((o, e) => OpenInNotepad(ProfileStore.FilePath)));
        }

        static ZapretStrategy CurrentStrategy()
        {
            var list = Zapret.LoadStrategies();
            string name = Settings.Get("zapret.strateji", "");
            return list.FirstOrDefault(x => x.Name == name) ?? list.FirstOrDefault();
        }

        void BuildZapret(StackPanel s)
        {
            bool on = Zapret.IsRunning();
            var strategies = Zapret.LoadStrategies();
            var strategy = CurrentStrategy();

            var toggle = new FluentToggle(on);
            toggle.Enabled = !app.Busy && Zapret.Installed;
            toggle.Toggled += (o, e) => { if (toggle.Checked) app.StartZapret(); else app.StopZapret(); };
            Group(s, Ui.D(16), new SettingCard("", "Zapret",
                Zapret.Installed
                    ? "Derin paket incelemesi (DPI) yoluyla uygulanan erişim kısıtlamalarını aşar. Discord gibi yalnızca DNS değişikliğiyle açılamayan hizmetler için gereklidir."
                    : "Zapret motoru bulunamadı. Güncellemeler sayfasından yükleyebilirsiniz.",
                toggle));

            Section(s, "Yapılandırma");
            bool testing = app.StrategyTestRunning;
            List<StrategyResult> tested;
            lock (StrategyTest.Last) tested = StrategyTest.Last.Values.ToList();
            var best = StrategyTest.Best(tested);
            var strat = new FluentDropDown(Ui.D(280));
            foreach (var st in strategies)
            {
                StrategyResult res;
                lock (StrategyTest.Last) StrategyTest.Last.TryGetValue(st.Name, out res);
                string label = st.Name;
                if (res != null) label += (res == best ? "  ★ " : " · ") + res.Label;
                strat.Add(st.Name, label);
            }
            strat.Value = strategy != null ? strategy.Name : null;
            strat.Enabled = !app.Busy;
            strat.ValueChanged += (o, e) =>
            {
                Settings.Set("zapret.strateji", strat.Value);
                if (Zapret.IsRunning()) app.StartZapret();
            };

            var filter = new FluentDropDown(Ui.D(240));
            filter.Add(Zapret.FilterList, "Yalnızca listedeki siteler");
            filter.Add(Zapret.FilterAuto, "Otomatik algılama");
            filter.Add(Zapret.FilterAll, "Tüm siteler");
            filter.Value = Settings.Get("zapret.filtre", Zapret.FilterList);
            filter.Enabled = !app.Busy;
            filter.ValueChanged += (o, e) =>
            {
                Settings.Set("zapret.filtre", filter.Value);
                if (Zapret.IsRunning()) app.StartZapret();
            };
            Group(s, Ui.D(8),
                new SettingCard("", "En uygun stratejiyi bul",
                    app.StrategyTestStatus ?? "Tüm stratejileri bağlantınız üzerinde sırayla dener ve kısıtlanan hizmetlere en hızlı erişimi sağlayanı otomatik olarak seçer. İşlem yaklaşık bir dakika sürer.",
                    testing ? Button("Durdur", app.FindStrategy)
                            : Button("Bul", app.FindStrategy, !app.Busy && Zapret.Installed, true)),
                new SettingCard("", "Strateji",
                    "Kısıtlamaları aşmak için kullanılan yöntem. En iyi sonuç için internet servis sağlayıcınıza uygun olanı seçin.", strat),
                new SettingCard("", "Uygulanacak siteler",
                    "Önerilen ayar, yalnızca listedeki sitelerdir. Otomatik algılama, erişilemeyen siteleri kendiliğinden listeye ekler.", filter));

            Section(s, "Başlangıç ve DNS");
            var autoStart = new FluentToggle(Settings.Get("zapret.otomatik", "0") == "1");
            autoStart.Toggled += (o, e) => Settings.Set("zapret.otomatik", autoStart.Checked ? "1" : "0");
            var autoDns = new FluentToggle(Settings.Get("zapret.sifreli_dns", "1") == "1");
            autoDns.Toggled += (o, e) => { Settings.Set("zapret.sifreli_dns", autoDns.Checked ? "1" : "0"); Rebuild(); };

            var dns = new FluentDropDown(Ui.D(240));
            foreach (var p in ProfileStore.Load().Where(p => p.Doh != null)) dns.Add(p.Name, p.Name);
            string preferred = Settings.Get("zapret.dns", "AdGuard");
            var match = ProfileStore.Load().FirstOrDefault(p => p.Doh != null && p.Name.StartsWith(preferred, StringComparison.OrdinalIgnoreCase));
            dns.Value = match != null ? match.Name : preferred;
            dns.Enabled = autoDns.Checked;
            dns.ValueChanged += (o, e) => Settings.Set("zapret.dns", dns.Value);

            Group(s, Ui.D(8),
                new SettingCard("", "Uygulama açıldığında başlat",
                    "Zapret, uygulama her başlatıldığında otomatik olarak etkinleşir.", autoStart),
                new SettingCard("", "Şifreli DNS'e otomatik geç",
                    "Bazı servis sağlayıcılar kısıtlı sitelerin şifresiz DNS sorgularını yanıtlamaz. Bu seçenek, Zapret başlarken şifreli DNS'i etkinleştirir.", autoDns),
                new SettingCard("", "Tercih edilen şifreli DNS",
                    "Seçilen sunucuya ulaşılamazsa sıradaki şifreli sunucu kullanılır.", dns));

            Section(s, "Gelişmiş");
            Group(s, Ui.D(8),
                new SettingCard("", "Site listesini düzenle", "Zapret'in uygulanacağı alan adlarını ekleyin veya kaldırın.")
                    .OnClick((o, e) => { Zapret.EnsureUserFiles(); OpenInNotepad(Zapret.HostlistPath); }),
                new SettingCard("", "Stratejileri düzenle", "Yeni strateji tanımlayın veya mevcut stratejilerin parametrelerini değiştirin.")
                    .OnClick((o, e) => OpenInNotepad(Zapret.StrategiesPath)));
        }

        // Store sürümü: uygulama ve Zapret motoru birlikte paketlenir, güncellemeleri Microsoft Store dağıtır.
        void BuildStoreUpdates(StackPanel s)
        {
            Action openStore = () =>
            {
                try { Process.Start("ms-windows-store://downloadsandupdates"); } catch (Exception) { }
            };
            var hero = new SettingCard("", "Güncellemeler Microsoft Store'dan gelir",
                "DNS Pilot ve Zapret motoru birlikte güncellenir. Yeni sürümler Microsoft Store tarafından otomatik olarak yüklenir.",
                Button("Store'u aç", openStore, true, true));
            hero.Large = true;
            hero.GlyphColor = Ui.P.Accent;
            Group(s, Ui.D(16), hero);

            Section(s, "Bileşenler");
            string appVersion = typeof(SettingsForm).Assembly.GetName().Version.ToString(2);
            Group(s, Ui.D(8),
                new SettingCard("", "DNS Pilot", "Sürüm " + appVersion),
                new SettingCard("", "Zapret motoru",
                    "Sürüm " + ShortSha(Zapret.CurrentVersion()) + " · Kaynak: bol-van/zapret-win-bundle · Uygulamayla birlikte gelir"));
        }

        // Windows Update düzeni: üstte durum kartı, altında bileşenler ve seçenekler.
        void BuildUpdates(StackPanel s)
        {
            if (Settings.Packaged) { BuildStoreUpdates(s); return; }
            var u = app.Updates;
            string last = u.LastCheck.HasValue ? "Son denetim: " + UpdateState.FormatWhen(u.LastCheck.Value) : "Henüz denetim yapılmadı";

            SettingCard hero;
            if (u.Installing)
                hero = new SettingCard("", "Güncelleme yükleniyor…",
                    "Zapret motoru indiriliyor ve kuruluyor. Bu sırada Zapret kısa bir süre devre dışı kalabilir.",
                    Button("Yükleniyor…", () => { }, false));
            else if (u.Checking)
                hero = new SettingCard("", "Güncellemeler denetleniyor…", "Bu işlem birkaç saniye sürebilir.",
                    Button("Denetleniyor…", () => { }, false));
            else if (u.ZapretAvailable)
                hero = new SettingCard("", "Güncellemeler kullanılabilir", last,
                    Button("Şimdi yükle", app.InstallUpdates, !app.Busy, true));
            else if (u.LastCheck.HasValue && u.Error == null)
                hero = new SettingCard("", "Güncelsiniz", last,
                    Button("Güncellemeleri denetle", app.CheckUpdates, true, true));
            else
                hero = new SettingCard("", "Güncellemeleri denetleyin", last,
                    Button("Güncellemeleri denetle", app.CheckUpdates, true, true));
            hero.Large = true;
            hero.GlyphColor = !u.Checking && !u.Installing && !u.ZapretAvailable && u.LastCheck.HasValue && u.Error == null
                ? Ui.P.Success : Ui.P.Accent;
            Group(s, Ui.D(16), hero);

            if (u.Error != null)
                Group(s, Ui.D(8), new SettingCard("", "İşlem tamamlanamadı", u.Error));
            else if (u.Message != null)
                Group(s, Ui.D(8), new SettingCard("", "Güncelleme tamamlandı", u.Message));

            if (u.ZapretAvailable || u.Installing)
            {
                Group(s, Ui.D(8), new SettingCard("", "Zapret motoru güncellemesi",
                    "Sürüm " + ShortSha(u.ZapretCurrent) + " → " + ShortSha(u.ZapretLatest) +
                    " · Site listeleriniz ve strateji ayarlarınız korunur.",
                    Badge(u.Installing ? "Yükleniyor…" : "Yüklemeye hazır", Ui.P.SubText)));
            }

            Section(s, "Bileşenler");
            string appVersion = typeof(SettingsForm).Assembly.GetName().Version.ToString(2);
            var store = Button("Microsoft Store", () =>
            {
                try { Process.Start("ms-windows-store://downloadsandupdates"); } catch (Exception) { }
            });
            Group(s, Ui.D(8),
                new SettingCard("", "DNS Pilot",
                    "Sürüm " + appVersion + " · Uygulama güncellemeleri Microsoft Store tarafından otomatik olarak sunulur.", store),
                new SettingCard("", "Zapret motoru",
                    "Sürüm " + ShortSha(u.ZapretCurrent) + " · Kaynak: bol-van/zapret-win-bundle",
                    u.ZapretAvailable ? Badge("Yeni sürüm: " + ShortSha(u.ZapretLatest), Ui.P.Accent)
                    : u.LastCheck.HasValue ? Badge("✓ Güncel", Ui.P.Success) : null));

            Section(s, "Seçenekler");
            var auto = new FluentToggle(u.AutoCheck);
            auto.Toggled += (o, e) => Settings.Set("guncelleme.otomatik", auto.Checked ? "1" : "0");
            Group(s, Ui.D(8),
                new SettingCard("", "Güncellemeleri otomatik denetle",
                    "Uygulama başlatıldığında günde en fazla bir kez yeni sürümleri denetler ve kullanılabilir olduğunda bildirim gösterir.", auto));
        }

        void BuildSystem(StackPanel s)
        {
            var start = new FluentToggle(app.AutostartEnabled);
            start.Toggled += (o, e) => app.SetAutostart(start.Checked);
            Group(s, Ui.D(16),
                new SettingCard("", "Windows ile başlat",
                    "Oturum açtığınızda uygulama, yönetici onayı istemeden bildirim alanında başlar.", start),
                new SettingCard("", "Uygulama klasörünü aç",
                    "DNS profillerinin, ayarların ve Zapret dosyalarının bulunduğu klasörü Dosya Gezgini'nde açar.")
                    .OnClick((o, e) => Process.Start("explorer.exe", "\"" + Settings.AppDir + "\"")));

            Section(s, "Hakkında");
            Group(s, Ui.D(8),
                new SettingCard("", "DNS Pilot",
                    "Sürüm " + typeof(SettingsForm).Assembly.GetName().Version.ToString(2) +
                    " · Zapret motoru " + ShortSha(Zapret.CurrentVersion())));
        }
    }
}
