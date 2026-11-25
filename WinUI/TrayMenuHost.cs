using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace DnsPilot
{
    // Tepsi menüsü: Windows 11'in kendi tepsi menüleri gibi akrilik arka planlı, yuvarlak köşeli, simgeli.
    //
    // Menü ayrı, başlıksız küçük bir penceredir (görev çubuğunda ve Alt+Tab'de görünmez). İmlecin üstünde açılır,
    // başka bir yere tıklanınca (pencere etkinliğini kaybedince) ya da Esc ile kapanır. WinUI MenuFlyout'u gizli bir
    // kök pencereden açmak (kütüphanenin "ikinci pencere" kipi ya da 1×1 pencere) bu yapıda çalışmıyordu.
    public sealed class TrayMenuHost
    {
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;

        public abstract class Entry { }
        public sealed class Separator : Entry { }
        public sealed class Item : Entry
        {
            public string Text, Glyph;
            public bool Enabled = true, Checked;
            public Action Action;
        }

        const double Width = 320, RowHeight = 36, SepHeight = 9, Padding = 4;

        Window window;
        StackPanel list;
        IntPtr hwnd;
        bool shown;

        void Ensure()
        {
            if (window != null) return;
            list = new StackPanel { Padding = new Thickness(Padding) };
            var root = new Grid { Children = { list } };
            root.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Escape) Hide(); };
            window = new Window { Content = root, Title = "DNS Pilot", SystemBackdrop = new DesktopAcrylicBackdrop() };
            hwnd = WindowNative.GetWindowHandle(window);

            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(true, false); // kenarlık + gölge, başlık çubuğu yok
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            window.AppWindow.SetPresenter(presenter);
            window.AppWindow.IsShownInSwitchers = false;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

            window.Activated += (s, e) =>
            {
                if (e.WindowActivationState == WindowActivationState.Deactivated && shown) Hide();
            };
            window.AppWindow.Closing += (s, e) => { e.Cancel = true; Hide(); };
        }

        void Hide()
        {
            shown = false;
            window.AppWindow.Hide();
        }

        static Brush Res(string key) { return (Brush)Application.Current.Resources[key]; }

        FrameworkElement Row(Item item)
        {
            var grid = new Grid
            {
                Height = RowHeight - 4,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                ColumnSpacing = 12,
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var fore = Res(item.Enabled ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");
            if (item.Glyph != null || item.Checked)
            {
                var icon = new FontIcon
                {
                    Glyph = item.Checked ? "" : item.Glyph,
                    FontSize = 16,
                    Foreground = item.Checked ? Res("AccentTextFillColorPrimaryBrush") : fore,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                grid.Children.Add(icon);
            }
            var text = new TextBlock
            {
                Text = item.Text,
                Foreground = fore,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (item.Enabled && item.Action != null)
            {
                grid.PointerEntered += (s, e) => grid.Background = Res("SubtleFillColorSecondaryBrush");
                grid.PointerExited += (s, e) => grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                grid.PointerPressed += (s, e) => grid.Background = Res("SubtleFillColorTertiaryBrush");
                grid.PointerReleased += (s, e) =>
                {
                    Hide();
                    try { item.Action(); }
                    catch (Exception ex) { DnsDegistirici.Diag.Error("tepsi menüsü: " + item.Text, ex); }
                };
            }
            return grid;
        }

        public void Show(List<Entry> entries)
        {
            Ensure();
            list.Children.Clear();
            double height = Padding * 2;
            foreach (var e in entries)
            {
                if (e is Separator)
                {
                    list.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(-Padding, 4, -Padding, 4),
                        Background = Res("DividerStrokeColorDefaultBrush"),
                    });
                    height += SepHeight;
                }
                else
                {
                    list.Children.Add(Row((Item)e));
                    height += RowHeight;
                }
            }

            // Boyut ve konum fiziksel piksel: imlecin üstünde, menünün sağ kenarı imleçte; çalışma alanına sığdır.
            POINT p;
            GetCursorPos(out p);
            double scale = GetDpiForWindow(hwnd) / 96.0;
            int w = (int)Math.Ceiling(Width * scale), h = (int)Math.Ceiling(height * scale) + 2;
            var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(p.X, p.Y), DisplayAreaFallback.Nearest).WorkArea;
            int x = Math.Max(area.X, Math.Min(p.X - w, area.X + area.Width - w));
            int y = Math.Max(area.Y, Math.Min(p.Y - h, area.Y + area.Height - h));
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));

            window.AppWindow.Show();
            window.Activate();
            shown = true;
            // Odak alınamazsa (ör. tam ekran bir uygulama önde) menü "etkinliğini kaybetme" olayını hiç almaz ve ekranın
            // üstünde asılı kalırdı; o durumda hemen kapatılır.
            if (!SetForegroundWindow(hwnd) && GetForegroundWindow() != hwnd)
            {
                var timer = window.DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(400);
                timer.IsRepeating = false;
                timer.Tick += (s, e) => { if (shown && GetForegroundWindow() != hwnd) Hide(); };
                timer.Start();
            }
        }
    }
}
