using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DnsPilot.Pages
{
    // DNS hız testi: WinUI iletişim kutusunda, sunucu başına çubuk göstergeli sonuçlar.
    public static class SpeedTestDialog
    {
        public static async void Show(XamlRoot root)
        {
            var body = new StackPanel { Spacing = 12, MinWidth = 420 };
            body.Children.Add(new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 24) });

            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = "DNS hız testi",
                Content = body,
                CloseButtonText = "Kapat",
                DefaultButton = ContentDialogButton.Close,
            };
            var showing = dialog.ShowAsync();

            var results = await AppController.Instance.MeasureSpeed();
            body.Children.Clear();

            int max = Math.Max(1, results.Where(r => r.Ms >= 0).Select(r => r.Ms).DefaultIfEmpty(1).Max());
            int best = results.Where(r => r.Ms >= 0).Select(r => r.Ms).DefaultIfEmpty(-1).Min();
            foreach (var r in results)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                var name = new TextBlock { Text = r.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                if (r.Current) name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                row.Children.Add(name);

                var bar = new ProgressBar { Maximum = max, Value = r.Ms < 0 ? 0 : r.Ms, VerticalAlignment = VerticalAlignment.Center };
                if (r.Ms != best) bar.Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);

                var ms = new TextBlock
                {
                    Text = r.Ms < 0 ? "yanıt yok" : r.Ms + " ms",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (r.Ms == best) ms.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                if (r.Ms < 0) ms.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                Grid.SetColumn(ms, 2);
                row.Children.Add(ms);
                body.Children.Add(row);
            }
            body.Children.Add(new TextBlock
            {
                Text = "Ölçüm şifresiz UDP 53 üzerinden yapılır. Servis sağlayıcınız bu trafiğe müdahale ediyorsa sonuçlar yanıltıcı olabilir. Kalın yazılan sunucu şu anda kullanılandır.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            await showing;
        }
    }
}
