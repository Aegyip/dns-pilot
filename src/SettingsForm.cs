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
        readonly Panel sidebar = new Panel();
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

            appTitle = new FluentLabel("DNS Pilot", Ui.Caption);
            sidebar.Controls.Add(appTitle);
            logoBox.SizeMode = PictureBoxSizeMode.Zoom;
            sidebar.Controls.Add(logoBox);
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
            int y = Ui.D(60);
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
