using System;
using System.Diagnostics;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DnsPilot.Pages
{
    // Windows Ayarlar sayfa düzeni: üstte büyük başlık, altında kaydırılabilir kart listesi.
    // Sayfa içeriği koddan kurulur ve uygulama durumu değişince (StateChanged) yeniden kurulur.
    public abstract class SettingsPageBase : Page
    {
        protected readonly AppController App = AppController.Instance;
        readonly ScrollViewer scroll;
        readonly StackPanel stack;

        protected SettingsPageBase(string title)
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
                Margin = new Thickness(56, 8, 56, 24),
            };
            root.Children.Add(header);

            stack = new StackPanel { Spacing = 4, MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
            scroll = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(56, 0, 56, 32), Child = stack },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Content = root;

            Loaded += (s, e) => { App.StateChanged += Rebuild; Rebuild(); };
            Unloaded += (s, e) => App.StateChanged -= Rebuild;
        }

        protected abstract void Build(StackPanel s);

        // Açık bir açılır liste varken sayfa yeniden kurulmaz (liste kapanıp seçim kaybolurdu); kapanınca kurulur.
        readonly System.Collections.Generic.List<ComboBox> combos = new System.Collections.Generic.List<ComboBox>();
        bool rebuildPending;

        protected void Rebuild()
        {
            foreach (var c in combos)
                if (c.IsDropDownOpen) { rebuildPending = true; return; }
            rebuildPending = false;
            combos.Clear();

            double offset = scroll.VerticalOffset;
            stack.Children.Clear();
            try
            {
                ActivityBar(stack);
                Build(stack);
            }
            catch (Exception ex)
            {
                // Sayfa kurulurken beklenmeyen hata uygulamayı kapatmasın; sayfada gösterilsin.
                DnsDegistirici.Diag.Log("Sayfa yüklenemedi (" + GetType().Name + "): " + ex);
                stack.Children.Add(new InfoBar
                {
                    IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error,
                    Title = "Bu sayfa yüklenemedi", Message = ex.Message,
                });
            }
            scroll.UpdateLayout();
            scroll.ChangeView(null, offset, null, true);
        }

        // ---- Yardımcılar ----

        protected static SettingsCard Card(string glyph, string header, string description, object content = null)
        {
            var card = new SettingsCard { Header = header, Description = description, Content = content };
            if (glyph != null) card.HeaderIcon = new FontIcon { Glyph = glyph };
            return card;
        }

        protected static SettingsCard Link(string glyph, string header, string description, Action onClick)
        {
            var card = Card(glyph, header, description);
            card.IsClickEnabled = true;
            card.Click += (s, e) => onClick();
            return card;
        }

        protected static void Section(StackPanel s, string text)
        {
            s.Children.Add(new TextBlock
            {
                Text = text,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = (Thickness)Application.Current.Resources["SectionHeaderMargin"],
            });
        }

        // Çalışan/sıradaki iş: sayfanın üstünde ilerleme çubuğu ve adım ("Zapret başlatılıyor: şifreli DNS sunucusu aranıyor…").
        // Önceden iş sürerken düğmeler sessizce soluklaşıyor, kullanıcı uygulamanın donduğunu sanıyordu.
        void ActivityBar(StackPanel s)
        {
            if (!AppController.Elevated)
                s.Children.Add(new InfoBar
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Warning,
                    Title = "Yönetici izni yok",
                    Message = "DNS Pilot yönetici izniyle çalışmıyor; DNS ayarları değiştirilemez ve Zapret başlatılamaz.",
                    ActionButton = Btn("Yönetici olarak yeniden başlat", App.RestartElevated),
                    Margin = new Thickness(0, 0, 0, 8),
                });

            string activity = App.Activity;
            if (activity == null) return;
            s.Children.Add(new Border
            {
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = activity, TextWrapping = TextWrapping.Wrap },
                        new ProgressBar { IsIndeterminate = true },
                    }
                },
            });
        }

        // Tıklama işleyicilerindeki hata uygulamayı kapatmaz; kayda geçer.
        protected static void Safe(Action action, string what)
        {
            try { action(); }
            catch (Exception ex) { DnsDegistirici.Diag.Error(what, ex); }
        }

        protected static Button Btn(string text, Action onClick, bool enabled = true, bool accent = false)
        {
            var b = new Button { Content = text, IsEnabled = enabled, MinWidth = 120 };
            if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            b.Click += (s, e) => Safe(onClick, "düğme: " + text);
            return b;
        }

        protected static ToggleSwitch Toggle(bool on, Action<bool> changed, bool enabled = true)
        {
            var t = new ToggleSwitch { IsOn = on, IsEnabled = enabled };
            t.Toggled += (s, e) => Safe(() => changed(t.IsOn), "anahtar");
            return t;
        }

        // Açılır liste: (değer, görünen metin) çiftleri; seçim değişince onChange(değer).
        protected ComboBox Combo(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> options,
                                 string value, Action<string> onChange, bool enabled = true, double width = 260)
        {
            var box = new ComboBox { MinWidth = width, IsEnabled = enabled };
            var keys = new System.Collections.Generic.List<string>();
            foreach (var o in options)
            {
                keys.Add(o.Key);
                box.Items.Add(new ComboBoxItem { Content = o.Value, Tag = o.Key });
            }
            box.SelectedIndex = Math.Max(0, keys.IndexOf(value));
            box.SelectionChanged += (s, e) =>
            {
                var item = box.SelectedItem as ComboBoxItem;
                if (item != null) Safe(() => onChange((string)item.Tag), "seçim");
            };
            box.DropDownClosed += (s, e) => { if (rebuildPending) DispatcherQueue.TryEnqueue(Rebuild); };
            combos.Add(box);
            return box;
        }

        protected static TextBlock Badge(string text, bool success)
        {
            var tb = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            tb.Foreground = (Brush)Application.Current.Resources[success ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush"];
            return tb;
        }

        // Zapret motoru yüklü değilse gösterilen kart: soluk anahtar yerine "Motoru yükle".
        protected SettingsCard EngineMissingCard()
        {
            object action;
            if (App.EngineInstalling)
                action = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children = { new ProgressRing { IsActive = true, Width = 20, Height = 20 }, new TextBlock { Text = "Yükleniyor…", VerticalAlignment = VerticalAlignment.Center } }
                };
            else
                action = Btn("Motoru yükle", App.InstallEngine, true, true);
            return Card("\uEA18", "Zapret",
                App.State.EngineRemoved
                    ? "Zapret dosyaları silinmiş. Bu genellikle antivirüsün winws2.exe / WinDivert dosyalarını karantinaya almasıyla olur. " +
                      "Antivirüste şu klasörü istisnalara ekleyip motoru yeniden yükleyin: " + DnsDegistirici.Zapret.EngineDir
                    : "Zapret motoru henüz yüklü değil. Erişim kısıtlamalarını aşabilmek için motoru yükleyin; birkaç saniye sürer.", action);
        }

        // Son Zapret hatası; kapatılana veya Zapret başarıyla açılana kadar görünür kalır.
        protected void ZapretErrorBar(StackPanel s)
        {
            string error = App.ZapretError;
            if (error == null) return;
            if (error == DnsDegistirici.Zapret.ElevationMessage && !AppController.Elevated) return; // üstteki uyarı şeridi anlatıyor
            var bar = new InfoBar
            {
                IsOpen = true,
                IsClosable = true,
                Severity = InfoBarSeverity.Error,
                Title = "Zapret açılamadı",
                Message = AppController.Clip(error, 900),
                Margin = new Thickness(0, 4, 0, 0),
            };
            bar.Closed += (sender, e) => App.ClearZapretError();
            s.Children.Add(bar);
        }
        protected static void OpenInNotepad(string path)
        {
            Safe(() => Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = false }), "not defteri");
        }

        protected static void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception) { }
        }

        protected static string AppVersion
        {
            get { return typeof(SettingsPageBase).Assembly.GetName().Version.ToString(2); }
        }
    }
}
