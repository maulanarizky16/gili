using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Gili
{
    /// <summary>Animated live activities for power, network and devices. "age" is seconds since the toast appeared.</summary>
    partial class IslandForm
    {
        static readonly Color Blue = Color.FromArgb(255, 90, 176, 255);

        static float BackOut(float t)
        {
            t = Ease.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1;
            float u = t - 1;
            return 1 + c3 * u * u * u + c1 * u * u;
        }

        float ToastAge(DateTime now) { return toast != null ? (float)(now - toast.Shown).TotalSeconds : 10f; }

        /// <summary>Views that keep animating while they are on screen.</summary>
        static bool IsAnimatedToast(View v)
        {
            return v == View.Charge || v == View.LowBattery || v == View.Network || v == View.Device || v == View.Saver;
        }

        void TwoLines(string title, string detail, Color titleColor, float x, float w, float h)
        {
            if (string.IsNullOrEmpty(detail))
            {
                paint.Text(title, Theme.Semi(13.5f), titleColor, x, h / 2, w - x - 16, StringAlignment.Near);
                return;
            }
            paint.Text(title, Theme.Semi(13f), titleColor, x, h / 2 - 8, w - x - 16, StringAlignment.Near);
            paint.Text(detail, Theme.Ui(11.5f), Theme.Sub, x, h / 2 + 9, w - x - 16, StringAlignment.Near);
        }

        // ---- power -----------------------------------------------------------------------------------

        void DrawCharge(float w, float h, DateTime now, bool low)
        {
            float age = ToastAge(now), cy = h / 2;
            string kind = low ? "low" : toast != null ? toast.Data as string ?? (toast.Flag ? "plug" : "unplug") : "plug";
            bool plug = kind == "plug", full = kind == "full", unplug = kind == "unplug";
            Color c = low ? Theme.Red : plug || full ? Theme.Green : Theme.Text;

            // icon on the left
            if (plug || full)
            {
                if (age < 0.6f) // spark ring
                {
                    float k = age / 0.6f;
                    paint.G.DrawEllipse(paint.P(Theme.WithAlpha(c, 0.6f * (1 - k)), 2f), 24 - 6 - k * 14, cy - 6 - k * 14, 12 + k * 28, 12 + k * 28);
                }
                DrawBolt(24, cy, 16 * BackOut(age / 0.45f), c);
            }
            else if (low)
            {
                float pulse = 0.75f + 0.25f * (float)Math.Sin(age * 7);
                paint.FillCircle(24, cy, 10 * pulse, Theme.WithAlpha(c, 0.22f));
                paint.TextCenter("!", Theme.Semi(14f), c, 24, cy);
            }
            else DrawPlug(24, cy, Ease.OutCubic(age / 0.5f) * 5, Theme.Sub);

            string title = plug ? "Mengisi daya" : full ? "Baterai penuh" : low ? "Baterai lemah" : "Memakai baterai";
            string detail = full ? "Cabut charger untuk menjaga baterai" : low ? "Colokkan charger" : unplug ? TimeLeft(batterySecondsLeft) : "";
            TwoLines(title, detail, low || plug || full ? c : Theme.Text, 42, w - 80, h);

            // battery on the right, with liquid
            string pct = battery + "%";
            float pw = paint.Measure(pct, Theme.Semi(13f));
            paint.TextRight(pct, Theme.Semi(13f), Theme.Text, w - 16, cy);
            float bx = w - 16 - pw - 10 - 32;
            if (low) bx += (float)Math.Sin(age * 46) * 2.2f * Math.Max(0, 1 - (age % 1.6f) / 0.45f); // shiver
            float level = battery / 100f * (plug ? Ease.OutCubic(age / 1.0f) : 1f);
            float slosh = plug ? Math.Max(0, 1 - age / 1.6f) : unplug ? Math.Max(0, 1 - age / 1.2f) * 1.4f : 0.15f;
            DrawLiquidBattery(bx, cy, 30, 15, level, c == Theme.Text ? Theme.Text : c, age, slosh, full ? age : -1);
        }

        void DrawLiquidBattery(float x, float cy, float bw, float bh, float level, Color color, float t, float slosh, float shimmer)
        {
            var body = new RectangleF(x, cy - bh / 2, bw, bh);
            using (var path = Theme.Round(body, 4f)) paint.G.DrawPath(paint.P(Theme.Sub, 1.3f), path);
            paint.FillRound(new RectangleF(body.Right + 1.3f, cy - 2.6f, 2.2f, 5.2f), 1.1f, Theme.Sub);
            var inner = RectangleF.Inflate(body, -2.2f, -2.2f);
            var state = paint.G.Save();
            using (var clip = Theme.Round(inner, 2.4f)) paint.G.SetClip(clip, CombineMode.Intersect);
            float surface = inner.Bottom - inner.Height * Ease.Clamp01(level);
            float amp = 0.5f + slosh * 1.8f;
            using (var liquid = new GraphicsPath())
            {
                var pts = new System.Collections.Generic.List<PointF>();
                pts.Add(new PointF(inner.Left - 1, inner.Bottom + 1));
                for (float px = inner.Left - 1; px <= inner.Right + 1; px += 1.5f)
                    pts.Add(new PointF(px, surface + (float)Math.Sin(px * 0.55f + t * 7f) * amp));
                pts.Add(new PointF(inner.Right + 1, inner.Bottom + 1));
                liquid.AddPolygon(pts.ToArray());
                paint.G.FillPath(paint.B(color), liquid);
            }
            if (shimmer >= 0) // a bright band sweeping across a full battery
            {
                float pos = inner.Left + ((shimmer * 0.9f) % 1.4f - 0.2f) * inner.Width;
                var band = new RectangleF(pos - 6, inner.Top, 12, inner.Height);
                using (var lg = new LinearGradientBrush(RectangleF.Inflate(band, 1, 0), Color.FromArgb(0, 255, 255, 255),
                                                        Color.FromArgb((int)(170 * paint.Alpha), 255, 255, 255), 0f))
                {
                    lg.SetBlendTriangularShape(0.5f);
                    paint.G.FillRectangle(lg, band);
                }
            }
            paint.G.Restore(state);
        }

        void DrawBolt(float cx, float cy, float size, Color c)
        {
            if (size < 0.5f) return;
            float s = size / 16f;
            using (var bolt = new GraphicsPath())
            {
                bolt.AddPolygon(new[] {
                    new PointF(cx + 1.5f * s, cy - 8 * s), new PointF(cx - 4.5f * s, cy + 1 * s), new PointF(cx - 0.5f * s, cy + 1 * s),
                    new PointF(cx - 1.5f * s, cy + 8 * s), new PointF(cx + 4.5f * s, cy - 1.5f * s), new PointF(cx + 0.5f * s, cy - 1.5f * s) });
                paint.G.FillPath(paint.B(c), bolt);
            }
        }

        /// <summary>A mains plug; <paramref name="gap"/> pulls it away from its socket.</summary>
        void DrawPlug(float cx, float cy, float gap, Color c)
        {
            paint.FillRound(new RectangleF(cx - 2 - gap, cy - 5, 8, 10), 2.5f, c);                 // plug body
            paint.G.DrawLine(paint.P(c, 1.6f), cx - 2 - gap, cy, cx - 8 - gap, cy);               // cable
            paint.G.DrawLine(paint.P(c, 1.4f), cx + 6 - gap, cy - 2.5f, cx + 9 - gap, cy - 2.5f);  // prongs
            paint.G.DrawLine(paint.P(c, 1.4f), cx + 6 - gap, cy + 2.5f, cx + 9 - gap, cy + 2.5f);
            paint.G.DrawLine(paint.P(Theme.WithAlpha(c, 0.6f), 1.6f), cx + 10, cy - 6, cx + 10, cy + 6); // socket
        }

        void DrawLeaf(float cx, float cy, float size, Color c)
        {
            if (size < 0.5f) return;
            float s = size / 16f;
            using (var leaf = new GraphicsPath())
            {
                leaf.AddBezier(cx - 6 * s, cy + 7 * s, cx - 8 * s, cy - 3 * s, cx + 1 * s, cy - 9 * s, cx + 7 * s, cy - 8 * s);
                leaf.AddBezier(cx + 7 * s, cy - 8 * s, cx + 8 * s, cy + 1 * s, cx + 1 * s, cy + 8 * s, cx - 6 * s, cy + 7 * s);
                leaf.CloseFigure();
                paint.G.FillPath(paint.B(c), leaf);
            }
            paint.G.DrawLine(paint.P(Theme.WithAlpha(Color.Black, 0.35f), 1.1f * s), cx - 5 * s, cy + 6 * s, cx + 4 * s, cy - 5 * s);
        }

        void DrawSaver(float w, float h, DateTime now)
        {
            var e = toast != null ? toast.Data as FeedEvent : null;
            if (e == null) return;
            float age = ToastAge(now);
            bool on = e.Kind == "on";
            Color c = on ? Theme.Green : Theme.Sub;
            float grow = on ? BackOut(age / 0.5f) : 1 - Ease.OutCubic(age / 0.6f) * 0.35f;
            DrawLeaf(24, h / 2, 17 * grow, c);
            TwoLines(e.Title, e.Detail, on ? c : Theme.Text, 42, w, h);
        }

        // ---- network -----------------------------------------------------------------------------------

        /// <summary>Wi-Fi fan: a dot and three arcs; each element has its own visibility 0..1.</summary>
        void DrawWifi(float cx, float cy, float[] arcs, Color c)
        {
            float baseY = cy + 6;
            paint.FillCircle(cx, baseY, 1.9f, Theme.WithAlpha(c, arcs[0]));
            for (int i = 1; i <= 3; i++)
            {
                float a = arcs[i];
                if (a <= 0.01f) continue;
                float r = 4.2f * i;
                paint.G.DrawArc(paint.P(Theme.WithAlpha(c, a), 2.1f), cx - r, baseY - r, r * 2, r * 2, 225 + (1 - a) * 20, 90 - (1 - a) * 40);
            }
        }

        void DrawNetwork(float w, float h, DateTime now)
        {
            var e = toast != null ? toast.Data as FeedEvent : null;
            if (e == null) return;
            float age = ToastAge(now), cy = h / 2;
            Color c = Theme.Text;
            switch (e.Kind)
            {
                case "wifiOn":
                {
                    var arcs = new float[4];
                    for (int i = 0; i < 4; i++) arcs[i] = Ease.OutCubic((age - 0.08f - i * 0.14f) / 0.22f);
                    DrawWifi(24, cy, arcs, Blue);
                    int chars = (int)Math.Max(0, (age - 0.45f) * 30);
                    TwoLines(e.Title, e.Detail.Substring(0, Math.Min(e.Detail.Length, chars)), Theme.Text, 44, w, h);
                    return;
                }
                case "wifiOff":
                {
                    var arcs = new float[4];
                    for (int i = 0; i < 4; i++) arcs[i] = Math.Max(0.18f, 1 - Ease.OutCubic((age - (3 - i) * 0.12f) / 0.25f));
                    DrawWifi(24, cy, arcs, Theme.Sub);
                    float k = Ease.OutCubic((age - 0.45f) / 0.25f);
                    if (k > 0) paint.G.DrawLine(paint.P(Theme.Red, 2.2f), 14, cy - 9, 14 + 20 * k, cy - 9 + 18 * k);
                    TwoLines(e.Title, e.Detail, Theme.Text, 44, w, h);
                    return;
                }
                case "noNet":
                {
                    DrawWifi(24, cy, new float[] { 1, 1, 1, 1 }, Theme.Sub);
                    float blink = 0.55f + 0.45f * (float)Math.Sin(age * 5.5f);
                    paint.FillCircle(31, cy - 6, 5.5f, Theme.WithAlpha(Theme.Orange, blink));
                    paint.TextCenter("!", Theme.Semi(9f), Theme.Ink, 31, cy - 6);
                    TwoLines(e.Title, e.Detail, Theme.Orange, 44, w, h);
                    return;
                }
                default: // ethOn / ethOff
                {
                    bool on = e.Kind == "ethOn";
                    float gap = on ? (1 - Ease.OutCubic(age / 0.4f)) * 9 : Ease.OutCubic(age / 0.45f) * 9;
                    DrawPlug(22, cy, gap, on ? Theme.Green : Theme.Sub);
                    if (on && age > 0.38f && age < 0.7f) // "click"
                        paint.FillCircle(32, cy, 9 * (age - 0.38f) / 0.32f, Theme.WithAlpha(Theme.Green, 0.35f * (1 - (age - 0.38f) / 0.32f)));
                    TwoLines(e.Title, e.Detail, on ? Theme.Green : Theme.Text, 44, w, h);
                    return;
                }
            }
        }

        // ---- devices -------------------------------------------------------------------------------------

        void DrawHeadphones(float cx, float cy, Color c)
        {
            paint.G.DrawArc(paint.P(c, 2.2f), cx - 8, cy - 9, 16, 16, 180, 180);
            paint.FillRound(new RectangleF(cx - 9.5f, cy - 1, 5, 9), 2.2f, c);
            paint.FillRound(new RectangleF(cx + 4.5f, cy - 1, 5, 9), 2.2f, c);
        }

        void DrawUsb(float cx, float cy, Color c)
        {
            paint.FillRound(new RectangleF(cx - 5, cy - 2, 10, 11), 2.5f, c);
            using (var head = Theme.Round(new RectangleF(cx - 3.5f, cy - 9, 7, 7), 1.2f)) paint.G.DrawPath(paint.P(c, 1.4f), head);
            paint.FillRound(new RectangleF(cx - 2, cy - 7, 1.5f, 1.8f), 0.4f, c);
            paint.FillRound(new RectangleF(cx + 0.6f, cy - 7, 1.5f, 1.8f), 0.4f, c);
        }

        void DrawMonitor(float cx, float cy, Color c)
        {
            using (var screen = Theme.Round(new RectangleF(cx - 7, cy - 6, 14, 10), 1.8f)) paint.G.DrawPath(paint.P(c, 1.6f), screen);
            paint.G.DrawLine(paint.P(c, 1.6f), cx, cy + 4, cx, cy + 7);
            paint.G.DrawLine(paint.P(c, 1.6f), cx - 3.5f, cy + 7.5f, cx + 3.5f, cy + 7.5f);
        }

        void DrawDevice(float w, float h, DateTime now)
        {
            var e = toast != null ? toast.Data as FeedEvent : null;
            if (e == null) return;
            float age = ToastAge(now), cy = h / 2;
            switch (e.Kind)
            {
                case "headphones":
                    for (int i = 0; i < 2; i++) // pulses
                    {
                        float k = ((age + i * 0.6f) % 1.2f) / 1.2f;
                        paint.G.DrawEllipse(paint.P(Theme.WithAlpha(Blue, 0.5f * (1 - k)), 1.6f), 24 - 9 - k * 9, cy - 9 - k * 9, 18 + k * 18, 18 + k * 18);
                    }
                    DrawHeadphones(24, cy, Blue);
                    TwoLines(e.Title, e.Detail, Theme.Text, 44, w, h);
                    break;
                case "speaker":
                    paint.Icon(Theme.IconVol3, 16, Theme.Text, 24, cy);
                    TwoLines(e.Title, e.Detail, Theme.Text, 44, w, h);
                    break;
                case "usbIn":
                case "usbOut":
                {
                    bool inn = e.Kind == "usbIn";
                    float dy = inn ? -(1 - BackOut(age / 0.45f)) * 14 : -Ease.OutCubic(age / 0.5f) * 10;
                    float a0 = paint.Alpha;
                    if (!inn) paint.Alpha = a0 * (1 - Ease.OutCubic(age / 0.6f) * 0.7f);
                    DrawUsb(24, cy + 1 + dy, inn ? Theme.Prayer : Theme.Sub);
                    paint.Alpha = a0;
                    TwoLines(e.Title, e.Detail, Theme.Text, 44, inn && e.Path != null ? w - 74 : w, h);
                    if (inn && e.Path != null)
                    {
                        string root = e.Path;
                        PillButton(new RectangleF(w - 16 - 64, cy - 15, 64, 30), "usbOpen", "Buka", Theme.Fill, Theme.Text, delegate { OpenDrive(root); });
                    }
                    break;
                }
                default: // monIn / monOut
                {
                    bool more = e.Kind == "monIn";
                    float split = more ? Ease.OutCubic(age / 0.5f) : 1 - Ease.OutCubic(age / 0.5f);
                    float a0 = paint.Alpha;
                    paint.Alpha = a0 * split;
                    DrawMonitor(24 + 5 * split, cy - 1, Theme.Sub);
                    paint.Alpha = a0;
                    DrawMonitor(24 - 5 * split, cy - 1, Theme.Text);
                    TwoLines(e.Title, e.Detail, Theme.Text, 44, w, h);
                    break;
                }
            }
        }
    }
}
