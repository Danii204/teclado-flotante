using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace TecladoFlotante
{
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(28, 28, 30);
        public static readonly Color Border = Color.FromArgb(60, 60, 64);
        public static readonly Color Key = Color.FromArgb(58, 58, 62);
        public static readonly Color KeySpecial = Color.FromArgb(44, 44, 48);
        public static readonly Color KeyHover = Color.FromArgb(76, 76, 82);
        public static readonly Color Accent = Color.FromArgb(0, 120, 212);
        public static readonly Color AccentDark = Color.FromArgb(0, 84, 150);
        public static readonly Color Text = Color.FromArgb(245, 245, 245);
        public static readonly Color SubText = Color.FromArgb(150, 150, 156);
        public static readonly Color AltGrText = Color.FromArgb(110, 185, 255);
        public static readonly Color Grip = Color.FromArgb(90, 90, 96);

        public const string TextFamily = "Segoe UI";
        static string iconFamily;

        public static string IconFamily
        {
            get
            {
                if (iconFamily == null)
                {
                    iconFamily = "Segoe MDL2 Assets";
                    using (InstalledFontCollection fonts = new InstalledFontCollection())
                        foreach (FontFamily f in fonts.Families)
                            if (f.Name == "Segoe Fluent Icons") { iconFamily = f.Name; break; }
                }
                return iconFamily;
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Caché de fuentes: al redimensionar se piden muchos tamaños distintos.</summary>
    internal class FontCache : IDisposable
    {
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public Font Get(string family, float px, FontStyle style)
        {
            px = Math.Max(6f, (float)Math.Round(px * 2) / 2f);
            string key = family + "|" + px + "|" + (int)style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                if (fonts.Count > 120) Dispose();
                f = new Font(family, px, style, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        public void Dispose()
        {
            foreach (Font f in fonts.Values) f.Dispose();
            fonts.Clear();
        }
    }

    /// <summary>Icono de la aplicación dibujado por código (bandeja, botón flotante y .ico del ejecutable).</summary>
    public static class IconArt
    {
        public static void Draw(Graphics g, RectangleF r, Color bg, Color fg)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.RoundRect(r, r.Width * 0.22f))
            using (SolidBrush b = new SolidBrush(bg))
                g.FillPath(b, p);

            float w = r.Width, h = r.Height;
            RectangleF kb = new RectangleF(r.X + w * 0.14f, r.Y + h * 0.27f, w * 0.72f, h * 0.48f);
            float gap = Math.Max(1f, kb.Width * 0.07f);
            float kw = (kb.Width - gap * 3) / 4f;
            float kh = (kb.Height - gap * 2) / 3f;
            bool small = w < 24;
            using (SolidBrush fb = new SolidBrush(fg))
            {
                for (int row = 0; row < 2; row++)
                    for (int c = 0; c < 4; c++)
                    {
                        RectangleF k = new RectangleF(kb.X + c * (kw + gap), kb.Y + row * (kh + gap), kw, kh);
                        if (small) g.FillRectangle(fb, k);
                        else using (GraphicsPath kp = Theme.RoundRect(k, kw * 0.2f)) g.FillPath(fb, kp);
                    }
                RectangleF space = new RectangleF(kb.X + kw * 0.5f + gap, kb.Y + 2 * (kh + gap), kb.Width - kw - gap * 2, kh);
                if (small) g.FillRectangle(fb, space);
                else using (GraphicsPath sp = Theme.RoundRect(space, kh * 0.3f)) g.FillPath(fb, sp);
            }
        }

        public static Bitmap Render(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                float inset = size <= 16 ? 0 : size * 0.02f;
                Draw(g, new RectangleF(inset, inset, size - inset * 2, size - inset * 2), Theme.Accent, Color.White);
            }
            return bmp;
        }

        public static Icon CreateIcon(int size)
        {
            using (Bitmap bmp = Render(size))
                return Icon.FromHandle(bmp.GetHicon());
        }

        /// <summary>Escribe un .ico multi-resolución con entradas PNG (usado por el script de compilación).</summary>
        public static void WriteIcoFile(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            List<byte[]> pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = Render(s))
                using (MemoryStream ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }

            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length);
                    w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) w.Write(p);
            }
        }
    }
}
