using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    /// <summary>
    /// "Liquid Glass" theme: the island is drawn from a blurred copy of what is behind it, with a lens-like rim
    /// that magnifies the backdrop near the edge and a specular highlight on top. The window is excluded from
    /// screen capture so it can read the screen without seeing itself (side effect: it does not show up in
    /// screenshots or screen sharing while this theme is on).
    /// </summary>
    partial class IslandForm
    {
        readonly WinTimer glassTimer = new WinTimer { Interval = 200 };
        Bitmap glassBlur, glassSoft;
        Rectangle glassRectPx, lastWindowPx;
        View glassCapturedFor = View.Hint;
        float glassLuma = 0.5f;
        long glassHash;
        bool glassExcluded;

        // Developer aid: with GILI_DUMP=<folder> set, rendered frames are saved there (the glass theme hides the
        // island from screenshots, so this is the way to look at it).
        static readonly string DumpDir = Environment.GetEnvironmentVariable("GILI_DUMP");
        // GILI_DUMP_MS sets the minimum gap between dumped frames (default 400; 33 for video capture).
        static readonly int DumpEveryMs = ParseEnvInt("GILI_DUMP_MS", 400);
        static readonly System.Diagnostics.Stopwatch DumpClock = System.Diagnostics.Stopwatch.StartNew();
        DateTime lastDumpUtc;
        int dumpIndex, dumpsPending;

        static int ParseEnvInt(string name, int fallback)
        {
            int v;
            return int.TryParse(Environment.GetEnvironmentVariable(name), out v) && v > 0 ? v : fallback;
        }

        /// <summary>Frames are named &lt;ms since start&gt;_&lt;index&gt;_&lt;view&gt;.png and encoded off the UI thread.</summary>
        void DebugDump(DateTime now, int pxW, int pxH)
        {
            if (string.IsNullOrEmpty(DumpDir) || (now - lastDumpUtc).TotalMilliseconds < DumpEveryMs) return;
            if (System.Threading.Interlocked.CompareExchange(ref dumpsPending, 0, 0) > 6) return; // disk is behind: skip a frame
            lastDumpUtc = now;
            try
            {
                System.IO.Directory.CreateDirectory(DumpDir);
                var copy = canvas.Clone(new Rectangle(0, 0, pxW, pxH), PixelFormat.Format32bppArgb);
                string file = System.IO.Path.Combine(DumpDir, DumpClock.ElapsedMilliseconds.ToString("000000000") + "_" + (dumpIndex++).ToString("00000") + "_" + current + ".png");
                System.Threading.Interlocked.Increment(ref dumpsPending);
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { copy.Save(file, ImageFormat.Png); }
                    catch (Exception ex) { Log.Write("dump: " + ex.Message); }
                    finally { copy.Dispose(); System.Threading.Interlocked.Decrement(ref dumpsPending); }
                });
            }
            catch (Exception ex) { Log.Write("dump: " + ex.Message); }
        }

        /// <summary>Writes a caption cue for the video compositor (only when dumping).</summary>
        void DumpCue(string title, string subtitle)
        {
            if (string.IsNullOrEmpty(DumpDir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(DumpDir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(DumpDir, "cues.txt"),
                    DumpClock.ElapsedMilliseconds + "\t" + title + "\t" + subtitle + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        bool GlassTheme { get { return settings.Get("theme", "hitam") == "kaca"; } }
        bool GlassActive { get { return GlassTheme && glassExcluded; } }

        void UpdateGlass()
        {
            if (!IsHandleCreated) return;
            if (GlassTheme)
            {
                if (!glassExcluded)
                {
                    glassExcluded = Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
                    if (!glassExcluded) Log.Write("glass: exclude-from-capture unavailable (" + Marshal.GetLastWin32Error() + ")");
                }
                if (!glassTimer.Enabled) glassTimer.Start();
                CaptureBackdrop();
            }
            else
            {
                glassTimer.Stop();
                if (glassExcluded) { Native.SetWindowDisplayAffinity(Handle, Native.WDA_NONE); glassExcluded = false; }
                if (glassBlur != null) { glassBlur.Dispose(); glassBlur = null; }
                if (glassSoft != null) { glassSoft.Dispose(); glassSoft = null; }
            }
        }

        void GlassTick()
        {
            // The hint line and a hidden window do not need a backdrop.
            if (!GlassActive || !windowShown || (current == View.Hint && sh.Settled)) return;
            CaptureBackdrop();
        }

        void CaptureBackdrop()
        {
            if (!GlassActive) return;
            try
            {
                // Only the area around the island (plus room to grow) is read: a compact island needs a fraction
                // of the pixels the expanded panel does. A view change triggers an immediate re-capture.
                int margin = (int)(48 * scale);
                int w = Math.Max(lastWindowPx.Width, (int)(120 * scale)) + margin * 2, h = Math.Max(lastWindowPx.Height, (int)(60 * scale)) + margin;
                var rect = new Rectangle(monitorPx.Left + monitorPx.Width / 2 - w / 2, monitorPx.Top, w, h);
                Bitmap blur, soft;
                using (var full = new Bitmap(w, h, PixelFormat.Format32bppRgb))
                {
                    using (var gr = Graphics.FromImage(full)) gr.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
                    blur = Downsample(full, 7);
                    soft = Downsample(full, 3);
                }
                BoxBlur(blur, 2, 2);
                BoxBlur(soft, 1, 1);
                float luma;
                long hash = Fingerprint(blur, out luma);
                if (hash == glassHash && glassBlur != null) { blur.Dispose(); soft.Dispose(); return; } // nothing changed behind
                if (glassBlur != null) glassBlur.Dispose();
                if (glassSoft != null) glassSoft.Dispose();
                glassBlur = blur;
                glassSoft = soft;
                glassRectPx = rect;
                glassLuma = luma;
                glassHash = hash;
                Kick();
            }
            catch (Exception ex) { Log.Write("glass capture: " + ex.Message); }
        }

        static Bitmap Downsample(Bitmap src, int factor)
        {
            var dst = new Bitmap(Math.Max(1, src.Width / factor), Math.Max(1, src.Height / factor), PixelFormat.Format32bppRgb);
            using (var gr = Graphics.FromImage(dst))
            {
                gr.InterpolationMode = InterpolationMode.HighQualityBilinear;
                gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gr.DrawImage(src, new Rectangle(0, 0, dst.Width, dst.Height));
            }
            return dst;
        }

        /// <summary>Separable box blur, applied <paramref name="passes"/> times (three passes approximate a Gaussian).</summary>
        static void BoxBlur(Bitmap bmp, int radius, int passes)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
            var px = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            var tmp = new byte[px.Length];
            int stride = data.Stride;
            for (int pass = 0; pass < passes; pass++)
            {
                Blur1D(px, tmp, w, h, stride, radius, true);
                Blur1D(tmp, px, w, h, stride, radius, false);
            }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
        }

        static void Blur1D(byte[] src, byte[] dst, int w, int h, int stride, int r, bool horizontal)
        {
            int outer = horizontal ? h : w, inner = horizontal ? w : h;
            for (int o = 0; o < outer; o++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0, count = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int i = Math.Max(0, Math.Min(inner - 1, k));
                        sum += src[Index(o, i, stride, horizontal) + c];
                        count++;
                    }
                    for (int i = 0; i < inner; i++)
                    {
                        dst[Index(o, i, stride, horizontal) + c] = (byte)(sum / count);
                        int add = Math.Min(inner - 1, i + r + 1), rem = Math.Max(0, i - r);
                        sum += src[Index(o, add, stride, horizontal) + c] - src[Index(o, rem, stride, horizontal) + c];
                    }
                }
                for (int i = 0; i < inner; i++) dst[Index(o, i, stride, horizontal) + 3] = 255;
            }
        }

        static int Index(int outer, int inner, int stride, bool horizontal)
        {
            return horizontal ? outer * stride + inner * 4 : inner * stride + outer * 4;
        }

        static long Fingerprint(Bitmap bmp, out float luma)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            var px = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            long hash = 1469598103934665603L;
            double sum = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                sum += 0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2];
                unchecked { hash = (hash ^ (px[i] >> 3) ^ ((px[i + 1] >> 3) << 5) ^ ((px[i + 2] >> 3) << 10)) * 1099511628211L; }
            }
            luma = (float)(sum / (px.Length / 4) / 255.0);
            return hash;
        }

        /// <summary>Fills <paramref name="shape"/> with glass. <paramref name="bounds"/> is its rectangle in DIPs.</summary>
        void DrawGlass(GraphicsPath shape, RectangleF bounds, float radius)
        {
            // Solid fallback underneath: covers the first frame and any edge the last capture did not reach yet
            // while the island is growing.
            g.FillPath(paint.B(Color.FromArgb(215, 26, 26, 32)), shape);
            if (glassBlur == null) return;
            if (current != glassCapturedFor) { glassCapturedFor = current; BeginInvoke(new Action(CaptureBackdrop)); }
            // Where the captured screen area sits in canvas DIPs.
            var dest = new RectangleF((glassRectPx.X - windowLeftPx) / scale, (glassRectPx.Y - monitorPx.Top) / scale,
                                      glassRectPx.Width / scale, glassRectPx.Height / scale);
            var state = g.Save();
            g.SetClip(shape, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(glassBlur, dest);

            // Lens rim: near the edge the backdrop is magnified slightly, like light bending through a thick pane.
            float inset = Math.Min(9f, Math.Min(bounds.Width, bounds.Height) / 4);
            if (inset > 2)
            {
                using (var inner = Theme.Round(RectangleF.Inflate(bounds, -inset, -inset), Math.Max(0, radius - inset)))
                using (var ring = new Region(shape))
                {
                    ring.Exclude(inner);
                    var ringState = g.Save();
                    g.SetClip(ring, CombineMode.Intersect);
                    float cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2, k = 1.08f;
                    g.DrawImage(glassSoft, new RectangleF(cx + (dest.X - cx) * k, cy + (dest.Y - cy) * k, dest.Width * k, dest.Height * k));
                    g.Restore(ringState);
                }
            }

            // Tint keeps white text readable: brighter backdrops get a darker veil.
            int veil = (int)(70 + glassLuma * 120);
            g.FillPath(paint.B(Color.FromArgb(Math.Min(205, veil), 14, 14, 20)), shape);
            var gloss = new RectangleF(bounds.X, bounds.Y, bounds.Width, Math.Max(2, bounds.Height * 0.5f));
            // The brush rectangle is larger than the filled one: GDI+ gradients wrap around on their last row,
            // which would otherwise leave a bright hairline across the middle of the island.
            using (var brush = new LinearGradientBrush(RectangleF.Inflate(gloss, 0, 2), Color.FromArgb((int)(42 * paint.Alpha), 255, 255, 255),
                                                       Color.FromArgb(0, 255, 255, 255), 90f))
            {
                brush.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(brush, gloss);
            }
            g.Restore(state);

            // Specular edge: bright along the top, fading towards the bottom.
            var edgeRect = RectangleF.Inflate(bounds, 2f, 2f);
            using (var rim = new LinearGradientBrush(edgeRect, Color.FromArgb((int)(170 * paint.Alpha), 255, 255, 255),
                                                     Color.FromArgb((int)(30 * paint.Alpha), 255, 255, 255), 90f))
            {
                rim.WrapMode = WrapMode.TileFlipXY;
                var rimState = g.Save();
                // A notch is glued to the screen edge: no highlight along that edge.
                if (bounds.Y < 0.5f) g.ExcludeClip(new Rectangle((int)bounds.X - 40, -4, (int)bounds.Width + 80, 6));
                using (var pen = new Pen(rim, 1.1f)) g.DrawPath(pen, shape);
                g.Restore(rimState);
            }
        }
    }
}
