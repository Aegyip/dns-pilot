using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace DnsPilot
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);

        bool allowClose;

        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var hwnd = WindowNative.GetWindowHandle(this);
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
            AppWindow.Title = "DNS Pilot";
            if (AppWindow.TitleBar != null)
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall; // Ayarlar'daki 48 px başlık

            // 1000x720 DIP; yüksek ölçekte (ör. 3000x2000 %250, 1920x1080 %175) çalışma alanına sığdırılır.
            double scale = GetDpiForWindow(hwnd) / 96.0;
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int w = Math.Min((int)(1000 * scale), (int)(work.Width * 0.94));
            int h = Math.Min((int)(720 * scale), (int)(work.Height * 0.94));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            CenterOnScreen();

            // Kapat düğmesi uygulamayı kapatmaz; tepside çalışmaya devam eder (Çıkış tepsi menüsünden).
            AppWindow.Closing += (s, e) =>
            {
                if (allowClose) return;
                e.Cancel = true;
                s.Hide();
            };

            Activated += (s, e) =>
                TitleText.Opacity = e.WindowActivationState == WindowActivationState.Deactivated ? 0.5 : 1.0;

            // Windows Ayarlar'daki gibi sayfa değişiminde aşağıdan yukarı kayarak giriş. İlk sayfa animasyonsuz açılır:
            // Store sertifikasyonundaki ekran görüntüsü (%250 ölçek) açılış animasyonunun ortasında yarım çizilmiş
            // içerik gösteriyordu.
            Nav.SelectedItem = Nav.MenuItems[0];
            ContentFrame.ContentTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection
            {
                new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition { FromVerticalOffset = 40 }
            };
        }

        void CenterOnScreen()
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Move(new Windows.Graphics.PointInt32(area.X + (area.Width - AppWindow.Size.Width) / 2,
                                                           area.Y + (area.Height - AppWindow.Size.Height) / 2));
        }

        public void BringToFront()
        {
            AppWindow.Show();
            if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) p.Restore();
            Activate();
            SetForegroundWindow(WindowNative.GetWindowHandle(this));
        }

        public void ForceClose()
        {
            allowClose = true;
            Close();
        }

        public void Navigate(string page)
        {
            var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == page);
            if (item != null) Nav.SelectedItem = item;
        }

        void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
            Type page;
            switch (tag)
            {
                case "DNS": page = typeof(Pages.DnsPage); break;
                case "Zapret": page = typeof(Pages.ZapretPage); break;
                case "Sistem": page = typeof(Pages.SystemPage); break;
                case "Güncellemeler": page = typeof(Pages.UpdatesPage); break;
                default: page = typeof(Pages.HomePage); break;
            }
            // Sayfalar koddan kurulduğu için XAML tür bilgisi yok; Frame.Navigate(Type) onları tanımaz ve çöker.
            // Sayfa doğrudan oluşturulur; geçiş animasyonu ContentTransitions ile verilir.
            ContentFrame.Content = (Page)Activator.CreateInstance(page);
        }
    }
}
