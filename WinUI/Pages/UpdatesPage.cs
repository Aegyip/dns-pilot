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
                s.Children.Add(Hero("\uE895", false, "Güncellemeler Microsoft Store'dan gelir",
                    "Yeni sürümler Microsoft Store tarafından otomatik olarak yüklenir.",
                    Btn("Store'u aç", () => OpenUri("ms-windows-store://downloadsandupdates"), true, true)));
            }
            else
            {
                s.Children.Add(Hero("\uE895", false, "Yeni sürümler GitHub'da yayımlanır",
                    "Bu kopya klasik kurulumla yüklendi. Yeni sürümü sürüm notları sayfasından indirebilir ya da Microsoft Store sürümüne geçerek otomatik güncelleme alabilirsiniz.",
                    Btn("Sürüm notları", () => OpenUri(BugReport.RepoUrl + "/releases"), true, true)));
            }
            Section(s, "Bileşenler");
            s.Children.Add(Card("\uE71D", "DNS Pilot", "Sürüm " + AppVersion));
        }
    }
}
