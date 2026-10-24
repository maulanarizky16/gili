using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    /// <summary>
    /// The System tab: CPU, memory and disk gauges plus live download / upload speed with a one-minute graph.
    /// Sampled once a second, and only while the tab is open.
    /// </summary>
    partial class IslandForm
    {
        [StructLayout(LayoutKind.Sequential)]
        struct FILETIME { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }

        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        const int History = 60;
        static readonly Color CpuColor = Color.FromArgb(255, 255, 140, 90), RamColor = Color.FromArgb(255, 170, 140, 255),
                              DiskColor = Color.FromArgb(255, 90, 200, 160), DownColor = Color.FromArgb(255, 90, 176, 255),
                              UpColor = Color.FromArgb(255, 255, 196, 61);
        readonly WinTimer statsTimer = new WinTimer { Interval = 1000 };
        readonly Queue<float> downHistory = new Queue<float>(), upHistory = new Queue<float>();
        ulong lastIdle, lastKernel, lastUser;
        long lastRx = -1, lastTx = -1;
        DateTime lastNetSample;
        float cpuLoad, ramLoad, diskLoad, downBps, upBps;
        double ramUsedGb, ramTotalGb, diskFreeGb;
        bool statsPrimed;

        void StartStats()
        {
            statsTimer.Tick += delegate
            {
                if (!expanded || page != SystemPage) { statsTimer.Stop(); statsPrimed = false; return; }
                SampleStats();
                Kick();
            };
        }

        /// <summary>Called when the System tab becomes visible.</summary>
        void ResumeStats()
        {
            if (statsTimer.Enabled) return;
            SampleStats(); // prime the counters so the first visible numbers are real
            statsTimer.Start();
        }

        void SampleStats()
        {
            try
            {
                FILETIME idle, kernel, user;
                if (GetSystemTimes(out idle, out kernel, out user))
                {
                    if (statsPrimed)
                    {
                        double di = idle.Value - lastIdle, dk = kernel.Value - lastKernel, du = user.Value - lastUser;
                        double total = dk + du; // kernel time includes idle time
                        cpuLoad = total > 0 ? (float)Math.Max(0, Math.Min(1, (total - di) / total)) : 0;
                    }
                    lastIdle = idle.Value; lastKernel = kernel.Value; lastUser = user.Value;
                }

                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    ramLoad = mem.dwMemoryLoad / 100f;
                    ramTotalGb = mem.ullTotalPhys / 1073741824.0;
                    ramUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
                }

                var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                if (sys.IsReady)
                {
                    diskLoad = 1f - (float)sys.AvailableFreeSpace / sys.TotalSize;
                    diskFreeGb = sys.AvailableFreeSpace / 1073741824.0;
                }

                long rx = 0, tx = 0;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var st = nic.GetIPStatistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
                var now = DateTime.UtcNow;
                if (statsPrimed && lastRx >= 0)
                {
                    double secs = Math.Max(0.2, (now - lastNetSample).TotalSeconds);
                    downBps = (float)Math.Max(0, (rx - lastRx) / secs);
                    upBps = (float)Math.Max(0, (tx - lastTx) / secs);
                    downHistory.Enqueue(downBps); upHistory.Enqueue(upBps);
                    while (downHistory.Count > History) downHistory.Dequeue();
                    while (upHistory.Count > History) upHistory.Dequeue();
                }
                lastRx = rx; lastTx = tx; lastNetSample = now;
                statsPrimed = true;
            }
            catch (Exception ex) { Log.Write("stats: " + ex.Message); }
        }

        static string Speed(float bytesPerSecond)
        {
            double bits = bytesPerSecond * 8;
            if (bits >= 1e6) return (bits / 1e6).ToString(bits >= 1e8 ? "0" : "0.0").Replace('.', ',') + " Mbps";
            if (bits >= 1e3) return (bits / 1e3).ToString("0") + " Kbps";
            return "0 Kbps";
        }

        static string Gb(double gb) { return gb.ToString(gb >= 100 ? "0" : "0.0").Replace('.', ',') + " GB"; }

        // ---- page -----------------------------------------------------------------------------------

        void DrawGauge(float cx, float cy, float value, Color c, string label, string detail)
        {
            const float r = 27;
            paint.Ring(cx, cy, r, 6, value, Theme.WithAlpha(c, 0.18f), c);
            paint.TextCenter((int)Math.Round(value * 100) + "%", Theme.Semi(14f), Theme.Text, cx, cy);
            paint.TextCenter(label, Theme.Semi(11.5f), Theme.Text, cx, cy + r + 15);
            paint.TextCenter(detail, Theme.Ui(10.5f), Theme.Sub, cx, cy + r + 30);
        }

        void DrawSystemPage(float w, float h)
        {
            if (!statsPrimed)
            {
                paint.TextCenter("Mengukur…", Theme.Ui(12f), Theme.Sub, w / 2, 120);
                return;
            }
            float gy = 92;
            DrawGauge(58, gy, cpuLoad, CpuColor, "CPU", "prosesor");
            DrawGauge(146, gy, ramLoad, RamColor, "RAM", Gb(ramUsedGb) + " / " + Gb(ramTotalGb));
            DrawGauge(234, gy, diskLoad, DiskColor, "Disk", Gb(diskFreeGb) + " bebas");

            // network: speeds and a one-minute graph
            var box = new RectangleF(286, 52, w - 286 - 18, 134);
            paint.FillRound(box, 14, Theme.Fill);
            paint.Text("↓", Theme.Semi(13f), DownColor, box.X + 12, box.Y + 18);
            paint.Text(Speed(downBps), Theme.Semi(12.5f), Theme.Text, box.X + 26, box.Y + 18);
            paint.Text("↑", Theme.Semi(13f), UpColor, box.X + 12, box.Y + 38);
            paint.Text(Speed(upBps), Theme.Semi(12.5f), Theme.Text, box.X + 26, box.Y + 38);
            var graph = new RectangleF(box.X + 10, box.Y + 54, box.Width - 20, box.Height - 64);
            float max = 1;
            foreach (var v in downHistory) max = Math.Max(max, v);
            foreach (var v in upHistory) max = Math.Max(max, v);
            DrawSpark(graph, upHistory, max, UpColor);
            DrawSpark(graph, downHistory, max, DownColor);
            paint.Text("60 detik terakhir", Theme.Ui(9.5f), Theme.Faint, graph.X, graph.Bottom + 1);
        }

        void DrawSpark(RectangleF r, Queue<float> values, float max, Color c)
        {
            if (values.Count < 2) return;
            var pts = new List<PointF>();
            int i = 0, n = values.Count;
            float step = r.Width / (History - 1), x0 = r.Right - step * (n - 1);
            foreach (var v in values)
            {
                pts.Add(new PointF(x0 + step * i, r.Bottom - r.Height * Math.Min(1, v / max)));
                i++;
            }
            using (var area = new GraphicsPath())
            {
                area.AddLine(pts[0].X, r.Bottom, pts[0].X, pts[0].Y);
                area.AddLines(pts.ToArray());
                area.AddLine(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, pts[pts.Count - 1].X, r.Bottom);
                area.CloseFigure();
                paint.G.FillPath(paint.B(Theme.WithAlpha(c, 0.18f)), area);
            }
            paint.G.DrawLines(paint.P(c, 1.6f), pts.ToArray());
        }
    }
}
