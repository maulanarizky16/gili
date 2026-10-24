using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Gili
{
    static class Theme
    {
        // The palette is swapped by Apply(); everything reads these fields at draw time.
        public static Color Bg, Text, Sub, Faint, Fill, FillHover, Ink, Hint;
        public static Color Green, Orange, Red, Focus, Break, LongBreak, Prayer, DefaultAccent;
        public static int ShadowAlpha;
        public static bool IsLight, IsGlass;

        public static readonly string[] Names = { "hitam", "adaptif", "terang", "aksen", "ramadan", "kaca" };
        public static readonly string[] Labels = { "Hitam", "Adaptif", "Terang", "Aksen Windows", "Ramadan", "Liquid Glass" };

        static Theme() { Apply("hitam"); }

        public static void Apply(string name)
        {
            IsLight = name == "terang";
            IsGlass = name == "kaca";
            Ink = Color.FromArgb(255, 22, 22, 24);
            Green = Color.FromArgb(255, 52, 211, 120);
            Orange = Color.FromArgb(255, 255, 159, 10);
            Red = Color.FromArgb(255, 255, 69, 58);
            Focus = Color.FromArgb(255, 255, 122, 89);
            Break = Color.FromArgb(255, 48, 209, 140);
            LongBreak = Color.FromArgb(255, 90, 176, 255);
            Prayer = Color.FromArgb(255, 72, 220, 170);
            DefaultAccent = Color.FromArgb(255, 170, 150, 255);
            ShadowAlpha = 24;

            if (IsLight)
            {
                Bg = Color.FromArgb(255, 246, 246, 248);
                Text = Color.FromArgb(255, 20, 20, 24);
                Sub = Color.FromArgb(160, 40, 40, 48);
                Faint = Color.FromArgb(95, 40, 40, 48);
                Fill = Color.FromArgb(20, 0, 0, 0);
                FillHover = Color.FromArgb(36, 0, 0, 0);
                Hint = Color.FromArgb(150, 60, 60, 70);
                Green = Color.FromArgb(255, 22, 163, 74);
                Orange = Color.FromArgb(255, 230, 126, 0);
                Red = Color.FromArgb(255, 220, 38, 38);
                Focus = Color.FromArgb(255, 234, 88, 52);
                Break = Color.FromArgb(255, 16, 160, 100);
                LongBreak = Color.FromArgb(255, 37, 120, 220);
                Prayer = Color.FromArgb(255, 13, 148, 110);
                DefaultAccent = Color.FromArgb(255, 110, 86, 230);
                ShadowAlpha = 34;
                return;
            }

            Bg = Color.Black;
            Text = Color.FromArgb(255, 245, 245, 247);
            Sub = Color.FromArgb(150, 235, 235, 245);
            Faint = Color.FromArgb(80, 235, 235, 245);
            Fill = Color.FromArgb(36, 255, 255, 255);
            FillHover = Color.FromArgb(58, 255, 255, 255);
            Hint = Color.FromArgb(150, 235, 235, 245);

            if (name == "aksen")
            {
                Color accent = WindowsAccent();
                double h, s, l;
                RgbToHsl(accent.R / 255.0, accent.G / 255.0, accent.B / 255.0, out h, out s, out l);
                Bg = HslToColor(h, Math.Min(s, 0.55) * 0.8, 0.13);
                DefaultAccent = HslToColor(h, Math.Max(s, 0.6), 0.68);
            }
            else if (name == "kaca")
            {
                // The glass itself is drawn from the backdrop; Bg is only the fallback for the bubble neck.
                Bg = Color.FromArgb(255, 30, 30, 38);
                Sub = Color.FromArgb(185, 245, 245, 250);
                Faint = Color.FromArgb(110, 245, 245, 250);
                Fill = Color.FromArgb(44, 255, 255, 255);
                FillHover = Color.FromArgb(72, 255, 255, 255);
                ShadowAlpha = 14;
            }
            else if (name == "ramadan")
            {
                Bg = Color.FromArgb(255, 10, 36, 27);
                Text = Color.FromArgb(255, 255, 247, 230);
                Sub = Color.FromArgb(165, 240, 228, 200);
                Faint = Color.FromArgb(90, 240, 228, 200);
                Fill = Color.FromArgb(34, 233, 196, 106);
                FillHover = Color.FromArgb(58, 233, 196, 106);
                Hint = Color.FromArgb(170, 233, 196, 106);
                Prayer = Color.FromArgb(255, 233, 196, 106);
                DefaultAccent = Prayer;
            }
        }

        /// <summary>Dark background tinted with a colour (used by the adaptive theme).</summary>
        public static Color Tint(Color accent)
        {
            double h, s, l;
            RgbToHsl(accent.R / 255.0, accent.G / 255.0, accent.B / 255.0, out h, out s, out l);
            return HslToColor(h, Math.Min(0.6, s * 0.75), 0.12);
        }

        /// <summary>The accent colour chosen in Windows Settings > Personalisation > Colours.</summary>
        public static Color WindowsAccent()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = k != null ? k.GetValue("AccentColor") : null;
                    if (v != null)
                    {
                        int abgr = Convert.ToInt32(v);
                        return Color.FromArgb(255, abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    }
                }
            }
            catch (Exception ex) { Log.Write("accent: " + ex.Message); }
            return Color.FromArgb(255, 0, 120, 212);
        }

        public const char IconSettings = '', IconEye = '', IconFolder = '', IconPaste = '', IconCalendar = '', IconSystem = '';

        // Segoe Fluent Icons (Windows 11) / Segoe MDL2 Assets (Windows 10) share these code points.
        public const char IconVol0 = '', IconVol1 = '', IconVol2 = '', IconVol3 = '', IconMute = '';
        public const char IconBolt = '', IconMic = '', IconCam = '', IconMusic = '';
        public const char IconMoon = '', IconSun = '', IconTimer = '', IconReset = '', IconPin = '';
        public const char IconBell = '', IconBellOff = '', IconChat = '';

        static string ui = "Segoe UI", semi = "Segoe UI Semibold", icons;
        static FontStyle semiStyle = FontStyle.Regular;
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static void Init()
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fc = new InstalledFontCollection())
                foreach (var f in fc.Families) installed.Add(f.Name);
            if (!installed.Contains(ui)) ui = SystemFonts.MessageBoxFont.FontFamily.Name;
            if (!installed.Contains(semi)) { semi = ui; semiStyle = FontStyle.Bold; }
            icons = installed.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                  : installed.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;
        }

        public static bool HasIcons { get { return icons != null; } }
        public static Font Ui(float px) { return Get(ui, px, FontStyle.Regular); }
        public static Font Semi(float px) { return Get(semi, px, semiStyle); }
        public static Font Icons(float px) { return Get(icons ?? ui, px, FontStyle.Regular); }

        static Font Get(string family, float px, FontStyle style)
        {
            string key = family + "|" + px + "|" + style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(family, px, style, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        public static Color WithAlpha(Color c, float a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * a))), c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Ease.Clamp01(t);
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                                  (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (max - min < 1e-6) { h = 0; s = 0; return; }
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        public static Color HslToColor(double h, double s, double l)
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            return Color.FromArgb(255, (int)(255 * Hue(p, q, h + 1.0 / 3)), (int)(255 * Hue(p, q, h)), (int)(255 * Hue(p, q, h - 1.0 / 3)));
        }

        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        /// <summary>
        /// Notch outline: flat top on the screen edge that flares out into concave "ears" of radius
        /// <paramref name="ear"/> on both sides, straight sides, and rounded bottom corners.
        /// </summary>
        public static GraphicsPath NotchPath(RectangleF r, float bottom, float ear)
        {
            var p = new GraphicsPath();
            bottom = Math.Min(bottom, Math.Min(r.Width, r.Height) / 2f);
            ear = Math.Min(ear, r.Height / 2f);
            float top = r.Y;
            p.AddLine(r.X - ear, top, r.Right + ear, top);
            p.AddArc(r.Right, top, ear * 2, ear * 2, 270, -90);              // right ear, ends at (Right, top+ear)
            if (bottom > 0.5f)
            {
                p.AddArc(r.Right - bottom * 2, r.Bottom - bottom * 2, bottom * 2, bottom * 2, 0, 90);
                p.AddArc(r.X, r.Bottom - bottom * 2, bottom * 2, bottom * 2, 90, 90);
            }
            else
            {
                p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            }
            p.AddArc(r.X - ear * 2, top, ear * 2, ear * 2, 0, -90);             // left ear, back to (X-ear, top)
            p.CloseFigure();
            return p;
        }

        /// <summary>Rounded rectangle with separate top and bottom radii (the notch shape has square top corners).</summary>
        public static GraphicsPath Round(RectangleF r, float top, float bottom)
        {
            float max = Math.Min(r.Width, r.Height) / 2f;
            top = Math.Min(top, max);
            bottom = Math.Min(bottom, max);
            if (Math.Abs(top - bottom) < 0.25f) return Round(r, bottom);
            var p = new GraphicsPath();
            if (top < 0.5f) { p.AddLine(r.X, r.Y, r.Right, r.Y); }
            else
            {
                p.AddArc(r.X, r.Y, top * 2, top * 2, 180, 90);
                p.AddArc(r.Right - top * 2, r.Y, top * 2, top * 2, 270, 90);
            }
            if (bottom < 0.5f) { p.AddLine(r.Right, r.Bottom, r.X, r.Bottom); }
            else
            {
                p.AddArc(r.Right - bottom * 2, r.Bottom - bottom * 2, bottom * 2, bottom * 2, 0, 90);
                p.AddArc(r.X, r.Bottom - bottom * 2, bottom * 2, bottom * 2, 90, 90);
            }
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
            if (radius < 0.5f) { p.AddRectangle(r); return p; }
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>
    /// Drawing helpers with a global opacity multiplier, so whole views can cross-fade.
    /// Brushes and pens are reused to keep allocations per frame low.
    /// </summary>
    class Painter
    {
        public Graphics G;
        public float Alpha = 1f;
        public bool Crisp; // set while nothing is moving
        readonly SolidBrush brush = new SolidBrush(Color.White);
        readonly Pen pen = new Pen(Color.White, 1f);
        readonly StringFormat format;
        readonly ImageAttributes imageAttributes = new ImageAttributes();
        readonly ColorMatrix matrix = new ColorMatrix();

        public Painter()
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.LineAlignment = StringAlignment.Center;
        }

        public Brush B(Color c)
        {
            brush.Color = Theme.WithAlpha(c, Alpha);
            return brush;
        }

        public Pen P(Color c, float width)
        {
            pen.Color = Theme.WithAlpha(c, Alpha);
            pen.Width = width;
            return pen;
        }

        public void FillRound(RectangleF r, float radius, Color c)
        {
            using (var path = Theme.Round(r, radius)) G.FillPath(B(c), path);
        }

        public void FillCircle(float cx, float cy, float radius, Color c)
        {
            G.FillEllipse(B(c), cx - radius, cy - radius, radius * 2, radius * 2);
        }

        public float Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return G.MeasureString(s, f, PointF.Empty, format).Width;
        }

        /// <summary>Draws text vertically centred on <paramref name="cy"/>; trims with an ellipsis past maxWidth.</summary>
        public void Text(string s, Font f, Color c, float x, float cy, float maxWidth, StringAlignment align)
        {
            if (string.IsNullOrEmpty(s)) return;
            format.Alignment = align;
            float h = f.Size * 2f;
            float left = align == StringAlignment.Near ? x : align == StringAlignment.Far ? x - maxWidth : x - maxWidth / 2;
            // ClearType is sharper but only correct on settled, fully opaque text; fades use greyscale AA.
            G.TextRenderingHint = Alpha >= 0.99f && Crisp ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAlias;
            if (Theme.IsGlass && c.A > 60) // a soft shadow keeps text legible on bright glass
            {
                var shadow = Color.FromArgb((int)(70 * c.A / 255f), 0, 0, 0);
                G.DrawString(s, f, B(shadow), new RectangleF(left, cy - h / 2 + f.Size * 0.06f + 0.9f, maxWidth, h), format);
            }
            G.DrawString(s, f, B(c), new RectangleF(left, cy - h / 2 + f.Size * 0.06f, maxWidth, h), format);
        }

        public void Text(string s, Font f, Color c, float x, float cy) { Text(s, f, c, x, cy, 1000, StringAlignment.Near); }
        public void TextRight(string s, Font f, Color c, float right, float cy) { Text(s, f, c, right, cy, 1000, StringAlignment.Far); }
        public void TextCenter(string s, Font f, Color c, float cx, float cy) { Text(s, f, c, cx, cy, 1000, StringAlignment.Center); }

        public void Icon(char glyph, float px, Color c, float cx, float cy)
        {
            format.Alignment = StringAlignment.Center;
            var f = Theme.Icons(px);
            G.DrawString(glyph.ToString(), f, B(c), new RectangleF(cx - px, cy - px, px * 2, px * 2), format);
        }

        public void Image(Image img, RectangleF r, float radius)
        {
            matrix.Matrix33 = Alpha;
            imageAttributes.SetColorMatrix(matrix);
            using (var path = Theme.Round(r, radius))
            using (var tb = new TextureBrush(img, new Rectangle(0, 0, img.Width, img.Height), imageAttributes))
            {
                tb.WrapMode = WrapMode.Clamp;
                tb.TranslateTransform(r.X, r.Y);
                tb.ScaleTransform(r.Width / img.Width, r.Height / img.Height);
                G.FillPath(tb, path);
            }
        }

        /// <summary>Arc from 12 o'clock, clockwise, covering <paramref name="fraction"/> of the circle.</summary>
        public void Ring(float cx, float cy, float radius, float width, float fraction, Color track, Color fill)
        {
            var rect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
            G.DrawEllipse(P(track, width), rect);
            fraction = Ease.Clamp01(fraction);
            if (fraction > 0.002f) G.DrawArc(P(fill, width), rect, -90, 360 * fraction);
        }

        public void Battery(float x, float cy, float level, Color fill)
        {
            var body = new RectangleF(x, cy - 5.5f, 22, 11);
            using (var path = Theme.Round(body, 3.2f)) G.DrawPath(P(Theme.Sub, 1.2f), path);
            FillRound(new RectangleF(body.Right + 1.2f, cy - 2f, 1.8f, 4f), 0.9f, Theme.Sub);
            float w = Math.Max(1.5f, (body.Width - 4) * Ease.Clamp01(level));
            FillRound(new RectangleF(body.X + 2, body.Y + 2, w, body.Height - 4), 1.6f, fill);
        }

        // ---- Vector media glyphs (filled, like the iOS ones) --------------------------------

        public void PlayIcon(float cx, float cy, float s, Color c)
        {
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] {
                    new PointF(cx - s * 0.36f, cy - s * 0.5f),
                    new PointF(cx + s * 0.52f, cy),
                    new PointF(cx - s * 0.36f, cy + s * 0.5f) });
                G.FillPath(B(c), path);
                G.DrawPath(P(c, s * 0.12f), path);
            }
        }

        public void PauseIcon(float cx, float cy, float s, Color c)
        {
            float w = s * 0.3f, gap = s * 0.2f;
            FillRound(new RectangleF(cx - gap / 2 - w, cy - s / 2, w, s), w * 0.3f, c);
            FillRound(new RectangleF(cx + gap / 2, cy - s / 2, w, s), w * 0.3f, c);
        }

        public void SkipIcon(float cx, float cy, float s, Color c, bool forward)
        {
            float dir = forward ? 1 : -1, half = s * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                float x0 = cx + dir * (-half + i * half * 0.95f);
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] {
                        new PointF(x0, cy - s * 0.36f),
                        new PointF(x0 + dir * half * 1.02f, cy),
                        new PointF(x0, cy + s * 0.36f) });
                    G.FillPath(B(c), path);
                    G.DrawPath(P(c, s * 0.08f), path);
                }
            }
        }
    }
}
