using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DnsDegistirici
{
    // Kenar çubuğu için renkli, degradeli simgeler (Windows 11 Ayarlar uygulamasının simge diliyle).
    // Microsoft'un kendi görselleri kopyalanmaz; aynı tarzda vektör olarak çizilir, her boyutta keskindir.
    public static class Icons
    {
        public delegate void Painter(Graphics g, RectangleF r);

        public static readonly Dictionary<string, Painter> Nav = new Dictionary<string, Painter>
        {
            { "Giriş", Home },
            { "DNS", Globe },
            { "Zapret", Shield },
            { "Sistem", Monitor },
            { "Güncellemeler", Update },
        };

        static LinearGradientBrush Grad(RectangleF r, Color a, Color b, float angle = 90f)
        {
            return new LinearGradientBrush(new RectangleF(r.X - 0.5f, r.Y - 0.5f, r.Width + 1, r.Height + 1), a, b, angle);
        }

        static RectangleF Inset(RectangleF r, float fx, float fy, float fw, float fh)
        {
            return new RectangleF(r.X + r.Width * fx, r.Y + r.Height * fy, r.Width * fw, r.Height * fh);
        }

        // Ev: turuncu çatı, sarı gövde, koyu kapı.
        static void Home(Graphics g, RectangleF r)
        {
            var body = Inset(r, 0.18f, 0.42f, 0.64f, 0.50f);
            using (var path = Theme.RoundRect(body, r.Width * 0.06f))
            using (var b = Grad(body, Color.FromArgb(255, 200, 61), Color.FromArgb(247, 140, 12)))
                g.FillPath(b, path);

            var roof = new[]
            {
                new PointF(r.X + r.Width * 0.50f, r.Y + r.Height * 0.06f),
                new PointF(r.X + r.Width * 0.96f, r.Y + r.Height * 0.50f),
                new PointF(r.X + r.Width * 0.04f, r.Y + r.Height * 0.50f),
            };
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(roof);
                using (var b = Grad(Inset(r, 0, 0.06f, 1, 0.44f), Color.FromArgb(255, 113, 72), Color.FromArgb(214, 52, 37)))
                    g.FillPath(b, path);
            }

            var door = Inset(r, 0.40f, 0.62f, 0.20f, 0.30f);
            using (var path = Theme.RoundRect(door, r.Width * 0.04f))
            using (var b = new SolidBrush(Color.FromArgb(150, 72, 22)))
                g.FillPath(b, path);
        }

        // Küre: mavi degrade, beyaz enlem/boylam çizgileri.
        static void Globe(Graphics g, RectangleF r)
        {
            var c = Inset(r, 0.06f, 0.06f, 0.88f, 0.88f);
            using (var b = Grad(c, Color.FromArgb(92, 200, 255), Color.FromArgb(0, 103, 192)))
                g.FillEllipse(b, c);
            using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1f, r.Width * 0.06f)))
            {
                g.DrawEllipse(pen, Inset(r, 0.33f, 0.06f, 0.34f, 0.88f));
                g.DrawLine(pen, c.X + c.Width * 0.06f, c.Y + c.Height * 0.5f, c.Right - c.Width * 0.06f, c.Y + c.Height * 0.5f);
                g.DrawLine(pen, c.X + c.Width * 0.5f, c.Y + c.Height * 0.04f, c.X + c.Width * 0.5f, c.Bottom - c.Height * 0.04f);
                var arc = Inset(r, 0.10f, 0.22f, 0.80f, 0.56f);
                g.DrawArc(pen, arc.X, arc.Y - arc.Height * 0.55f, arc.Width, arc.Height, 30, 120);
                g.DrawArc(pen, arc.X, arc.Y + arc.Height * 0.55f, arc.Width, arc.Height, 210, 120);
            }
        }

        // Kalkan: gri-mavi degrade, sol yarı daha açık (Gizlilik ve güvenlik tarzı).
        static void Shield(Graphics g, RectangleF r)
        {
            using (var path = ShieldPath(Inset(r, 0.12f, 0.04f, 0.76f, 0.92f)))
            {
                using (var b = Grad(r, Color.FromArgb(140, 154, 172), Color.FromArgb(78, 92, 112)))
                    g.FillPath(b, path);
                var left = new RectangleF(r.X, r.Y, r.Width * 0.5f, r.Height);
                var state = g.Save();
                g.SetClip(left);
                using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                    g.FillPath(b, path);
                g.Restore(state);
            }
        }

        static GraphicsPath ShieldPath(RectangleF s)
        {
            var path = new GraphicsPath();
            float cx = s.X + s.Width / 2;
            path.AddLine(cx, s.Y, s.Right, s.Y + s.Height * 0.16f);
            path.AddLine(s.Right, s.Y + s.Height * 0.16f, s.Right, s.Y + s.Height * 0.48f);
            path.AddBezier(s.Right, s.Y + s.Height * 0.48f, s.Right, s.Y + s.Height * 0.78f,
                           cx + s.Width * 0.2f, s.Y + s.Height * 0.92f, cx, s.Bottom);
            path.AddBezier(cx, s.Bottom, cx - s.Width * 0.2f, s.Y + s.Height * 0.92f,
                           s.X, s.Y + s.Height * 0.78f, s.X, s.Y + s.Height * 0.48f);
            path.AddLine(s.X, s.Y + s.Height * 0.48f, s.X, s.Y + s.Height * 0.16f);
            path.CloseFigure();
            return path;
        }

        // Monitör: mavi ekran, gri ayak (Sistem tarzı).
        static void Monitor(Graphics g, RectangleF r)
        {
            var stand = Inset(r, 0.30f, 0.80f, 0.40f, 0.10f);
            using (var path = Theme.RoundRect(stand, r.Width * 0.04f))
            using (var b = new SolidBrush(Color.FromArgb(120, 130, 145)))
                g.FillPath(b, path);
            var neck = Inset(r, 0.45f, 0.70f, 0.10f, 0.12f);
            using (var b = new SolidBrush(Color.FromArgb(120, 130, 145))) g.FillRectangle(b, neck);

            var screen = Inset(r, 0.04f, 0.12f, 0.92f, 0.60f);
            using (var path = Theme.RoundRect(screen, r.Width * 0.07f))
            using (var b = Grad(screen, Color.FromArgb(92, 200, 255), Color.FromArgb(0, 95, 184)))
                g.FillPath(b, path);
            var shine = Inset(r, 0.10f, 0.18f, 0.80f, 0.22f);
            using (var path = Theme.RoundRect(shine, r.Width * 0.04f))
            using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                g.FillPath(b, path);
        }

        // Güncelleme: mavi daire, beyaz dönen oklar (Windows Update tarzı).
        static void Update(Graphics g, RectangleF r)
        {
            var c = Inset(r, 0.04f, 0.04f, 0.92f, 0.92f);
            using (var b = Grad(c, Color.FromArgb(76, 194, 255), Color.FromArgb(0, 103, 192)))
                g.FillEllipse(b, c);

            float w = Math.Max(1.2f, r.Width * 0.09f);
            var arc = Inset(r, 0.27f, 0.27f, 0.46f, 0.46f);
            using (var pen = new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, arc, 200, 120);
                g.DrawArc(pen, arc, 20, 120);
            }
            // Ok uçları
            Arrow(g, arc, 320, w);
            Arrow(g, arc, 140, w);
        }

        static void Arrow(Graphics g, RectangleF arc, float angleDeg, float w)
        {
            double a = angleDeg * Math.PI / 180;
            float cx = arc.X + arc.Width / 2, cy = arc.Y + arc.Height / 2;
            float px = cx + (float)Math.Cos(a) * arc.Width / 2, py = cy + (float)Math.Sin(a) * arc.Height / 2;
            // Teğet yönü (saat yönü)
            float tx = -(float)Math.Sin(a), ty = (float)Math.Cos(a);
            float nx = (float)Math.Cos(a), ny = (float)Math.Sin(a);
            float len = w * 2.0f;
            var tri = new[]
            {
                new PointF(px + tx * len, py + ty * len),
                new PointF(px + nx * len * 0.9f, py + ny * len * 0.9f),
                new PointF(px - nx * len * 0.9f, py - ny * len * 0.9f),
            };
            using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, tri);
        }
    }
}
