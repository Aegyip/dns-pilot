using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DnsDegistirici
{
    // Windows 11 Mica malzemesinin eşdeğeri.
    //
    // Mica, masaüstü duvar kâğıdının çok bulanık ve tema rengiyle karıştırılmış hâlidir; pencerenin ekrandaki
    // konumuna göre örneklenir ve pencere pasifken düz renge döner. WinForms'ta gerçek Mica (DWM) yalnızca
    // pencere alfa kanalıyla çizildiğinde görünür ve GDI metni alfa kanalını bozduğu için kullanılamıyor.
    // Bu yüzden aynı hesap burada yapılır ve opak olarak çizilir: görünüm aynıdır, metin bozulmaz,
    // Windows 10'da da çalışır.
    public static class Backdrop
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SystemParametersInfo(int action, int param, StringBuilder value, int winIni);
        const int SPI_GETDESKWALLPAPER = 0x0073;

        const int Downscale = 24; // bulanıklaştırılmış görüntü ekranın 1/24'ü kadar saklanır

        static Bitmap small;       // sanal ekranın bulanık + renklendirilmiş küçük kopyası
        static Rectangle virt;     // sanal ekran (tüm monitörler)
        static bool builtDark;

        // Pencere etkin değilken Mica düz renge döner (Windows'taki gibi).
        public static bool Active = true;

        public static void Reset()
        {
            if (small != null) { small.Dispose(); small = null; }
        }

        // Denetimin istemci alanındaki r dikdörtgenine Mica arka planını çizer.
        public static void Paint(Graphics g, Control c, Rectangle r)
        {
            var p = Ui.P;
            if (!Active || !Ensure(p.Dark) || !c.IsHandleCreated)
            {
                using (var b = new SolidBrush(p.Surface)) g.FillRectangle(b, r);
                return;
            }
            var screen = c.RectangleToScreen(r);
            var src = new RectangleF((screen.X - virt.X) / (float)Downscale, (screen.Y - virt.Y) / (float)Downscale,
                                     screen.Width / (float)Downscale, screen.Height / (float)Downscale);
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var attr = new ImageAttributes())
            {
                attr.SetWrapMode(WrapMode.TileFlipXY); // kenarlarda koyu şerit olmasın
                g.DrawImage(small, new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.X, r.Bottom) },
                            src, GraphicsUnit.Pixel, attr);
            }
            g.Restore(state);
        }

        static bool Ensure(bool dark)
        {
            if (small != null && builtDark == dark) return true;
            Reset();
            try
            {
                small = Build(dark);
                builtDark = dark;
            }
            catch (Exception) { small = null; }
            return small != null;
        }

        static Bitmap Build(bool dark)
        {
            virt = SystemInformation.VirtualScreen;
            int w = Math.Max(4, virt.Width / Downscale), h = Math.Max(4, virt.Height / Downscale);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(DesktopColor());
                Image wall = LoadWallpaper();
                if (wall != null)
                {
                    using (wall)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        string style = WallpaperStyle();
                        foreach (var s in Screen.AllScreens)
                        {
                            var d = new RectangleF((s.Bounds.X - virt.X) / (float)Downscale, (s.Bounds.Y - virt.Y) / (float)Downscale,
                                                   s.Bounds.Width / (float)Downscale, s.Bounds.Height / (float)Downscale);
                            g.DrawImage(wall, Place(wall.Size, d, style));
                        }
                    }
                }
            }

            Blur(bmp, 3, 3);
            Tint(bmp, dark);
            return bmp;
        }

        // Duvar kâğıdı yerleşimi: 10 = Doldur (varsayılan), 6 = Sığdır, 2 = Uzat, 0 = Ortala/Döşe, 22 = Yay.
        static RectangleF Place(Size img, RectangleF d, string style)
        {
            float sx = d.Width / img.Width, sy = d.Height / img.Height;
            float scale;
            switch (style)
            {
                case "2": return d;
                case "6": scale = Math.Min(sx, sy); break;
                case "0": scale = 1f / Downscale; break;
                default: scale = Math.Max(sx, sy); break;
            }
            float w = img.Width * scale, h = img.Height * scale;
            return new RectangleF(d.X + (d.Width - w) / 2, d.Y + (d.Height - h) / 2, w, h);
        }

        static Image LoadWallpaper()
        {
            try
            {
                var sb = new StringBuilder(520);
                SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
                string path = sb.ToString();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                // Dosyayı kilitlememek için belleğe al.
                using (var fs = new MemoryStream(File.ReadAllBytes(path)))
                using (var img = Image.FromStream(fs))
                    return new Bitmap(img);
            }
            catch (Exception) { return null; }
        }

        static string WallpaperStyle()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    return (k == null ? null : k.GetValue("WallpaperStyle") as string) ?? "10";
            }
            catch (Exception) { return "10"; }
        }

        static Color DesktopColor()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
                {
                    var v = (k == null ? null : k.GetValue("Background") as string);
                    var parts = v == null ? null : v.Split(' ');
                    if (parts != null && parts.Length == 3)
                        return Color.FromArgb(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
                }
            }
            catch (Exception) { }
            return Color.Black;
        }

        // Kutu bulanıklaştırma (birkaç geçiş Gauss'a yaklaşır).
        static void Blur(Bitmap bmp, int radius, int passes)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            var tmp = new int[px.Length];
            for (int p = 0; p < passes; p++)
            {
                BoxPass(px, tmp, w, h, radius, true);
                BoxPass(tmp, px, w, h, radius, false);
            }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
        }

        static void BoxPass(int[] src, int[] dst, int w, int h, int r, bool horizontal)
        {
            int outer = horizontal ? h : w, inner = horizontal ? w : h;
            for (int o = 0; o < outer; o++)
            {
                for (int i = 0; i < inner; i++)
                {
                    int sr = 0, sg = 0, sb = 0, n = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int j = Math.Max(0, Math.Min(inner - 1, i + k));
                        int c = horizontal ? src[o * w + j] : src[j * w + o];
                        sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255; n++;
                    }
                    int v = (255 << 24) | ((sr / n) << 16) | ((sg / n) << 8) | (sb / n);
                    if (horizontal) dst[o * w + i] = v; else dst[i * w + o] = v;
                }
            }
        }

        // Mica renklendirmesi: koyu temada %80 #202020, açık temada %85 #F3F3F3 ile karıştırılır
        // (açık Mica'da duvar kâğıdının rengi yalnızca hafif bir ton olarak kalır).
        static void Tint(Bitmap bmp, bool dark)
        {
            int baseC = dark ? 32 : 243;
            float a = dark ? 0.80f : 0.85f;
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new int[bmp.Width * bmp.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            for (int i = 0; i < px.Length; i++)
            {
                int c = px[i];
                int r = (int)(((c >> 16) & 255) * (1 - a) + baseC * a);
                int g = (int)(((c >> 8) & 255) * (1 - a) + baseC * a);
                int b = (int)((c & 255) * (1 - a) + baseC * a);
                px[i] = (255 << 24) | (r << 16) | (g << 8) | b;
            }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
        }
    }
}
