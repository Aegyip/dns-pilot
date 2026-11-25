using DnsDegistirici;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DnsPilot.Pages
{
    // Windows Update düzeni: üstte büyük durum alanı, altında bileşenler ve seçenekler.
    public sealed class UpdatesPage : SettingsPageBase
    {
        public UpdatesPage() : base("Güncellemeler") { }

        static Brush Res(string key) { return (Brush)Application.Current.Resources[key]; }

        // Büyük durum alanı (Windows Update'teki simge + başlık + son denetim + düğme).
        static FrameworkElement Hero(string glyph, bool success, string title, string subtitle, FrameworkElement action, bool busy = false)
        {
            var grid = new Grid
            {
                Padding = new Thickness(20, 20, 20, 20),
                Background = Res("CardBackgroundFillColorDefaultBrush"),
                BorderBrush = Res("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                ColumnSpacing = 20,
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement icon = busy
                ? new ProgressRing { IsActive = true, Width = 40, Height = 40 }
                : new FontIcon { Glyph = glyph, FontSize = 40, Foreground = Res(success ? "SystemFillColorSuccessBrush" : "AccentTextFillColorPrimaryBrush") };
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            text.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = subtitle, Foreground = Res("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (action != null)
            {
                action.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(action, 2);
                grid.Children.Add(action);
            }
            return grid;
        }

        protected override void Build(StackPanel s)
        {
            if (Settings.Packaged)
            {
                s.Children.Add(Hero("", false, "Güncellemeler Microsoft Store'dan gelir",
                    "DNS Pilot ve Zapret motoru birlikte güncellenir. Yeni sürümler Microsoft Store tarafından otomatik olarak yüklenir.",
                    Btn("Store'u aç", () => OpenUri("ms-windows-store://downloadsandupdates"), true, true)));
                Section(s, "Bileşenler");
                s.Children.Add(Card("", "DNS Pilot", "Sürüm " + AppVersion));
                s.Children.Add(Card("", "Zapret motoru", "Sürüm " + AppController.ShortSha(App.State.ZapretVersion) +
                    " · Kaynak: bol-van/zapret-win-bundle · Uygulamayla birlikte gelir"));
                return;
            }

            var u = App.Updates;
            string last = u.LastCheck.HasValue ? "Son denetim: " + UpdateState.FormatWhen(u.LastCheck.Value) : "Henüz denetim yapılmadı";

            if (u.Installing)
                s.Children.Add(Hero("", false, "Güncelleme yükleniyor…",
                    "Zapret motoru indiriliyor ve kuruluyor. Bu sırada Zapret kısa bir süre devre dışı kalabilir.", null, true));
            else if (u.Checking)
                s.Children.Add(Hero("", false, "Güncellemeler denetleniyor…", "Bu işlem birkaç saniye sürebilir.", null, true));
            else if (u.ZapretAvailable)
                s.Children.Add(Hero("", false, "Güncellemeler kullanılabilir", last, Btn("Şimdi yükle", App.InstallUpdates, !u.Installing, true)));
            else if (u.LastCheck.HasValue && u.Error == null)
                s.Children.Add(Hero("", true, "Güncelsiniz", last, Btn("Güncellemeleri denetle", App.CheckUpdates, true, true)));
            else
                s.Children.Add(Hero("", false, "Güncellemeleri denetleyin", last, Btn("Güncellemeleri denetle", App.CheckUpdates, true, true)));

            if (u.Error != null)
                s.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error, Title = "İşlem tamamlanamadı", Message = u.Error, Margin = new Thickness(0, 4, 0, 0) });
            else if (u.Message != null)
                s.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Success, Title = "Güncelleme tamamlandı", Message = u.Message, Margin = new Thickness(0, 4, 0, 0) });

            if (u.ZapretAvailable || u.Installing)
                s.Children.Add(Card("", "Zapret motoru güncellemesi",
                    "Sürüm " + AppController.ShortSha(u.ZapretCurrent) + " → " + AppController.ShortSha(u.ZapretLatest) +
                    " · Site listeleriniz ve strateji ayarlarınız korunur.",
                    Badge(u.Installing ? "Yükleniyor…" : "Yüklemeye hazır", false)));

            Section(s, "Bileşenler");
            s.Children.Add(Card("", "DNS Pilot",
                "Sürüm " + AppVersion + " · Uygulama güncellemeleri Microsoft Store tarafından otomatik olarak sunulur.",
                Btn("Microsoft Store", () => OpenUri("ms-windows-store://downloadsandupdates"))));
            s.Children.Add(Card("", "Zapret motoru",
                "Sürüm " + AppController.ShortSha(u.ZapretCurrent) + " · Kaynak: bol-van/zapret-win-bundle",
                u.ZapretAvailable ? (object)Badge("Yeni sürüm: " + AppController.ShortSha(u.ZapretLatest), false)
                : u.LastCheck.HasValue ? Badge("✓ Güncel", true) : null));

            Section(s, "Seçenekler");
            s.Children.Add(Card("", "Güncellemeleri otomatik denetle",
                "Uygulama başlatıldığında günde en fazla bir kez yeni sürümleri denetler ve kullanılabilir olduğunda bildirim gösterir.",
                Toggle(u.AutoCheck, v => Settings.Set("guncelleme.otomatik", v ? "1" : "0"))));
        }
    }
}
