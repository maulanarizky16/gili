using System;
using System.Drawing;
using System.Management;

namespace Gili
{
    /// <summary>
    /// Screen brightness overlay. Windows reports changes of the built-in panel through the WMI event
    /// WmiMonitorBrightnessEvent (Fn keys, Quick Settings slider, auto-brightness), so nothing is polled.
    /// External monitors usually do not report brightness; then this simply stays quiet.
    /// </summary>
    partial class IslandForm
    {
        ManagementEventWatcher brightnessWatcher;
        int brightness = -1;
        readonly Spring brightnessBar = new Spring(0.5f, 320, 30, 0.001f);

        void StartBrightness()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var scope = new ManagementScope(@"root\WMI");
                    using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness")))
                        foreach (ManagementObject o in searcher.Get())
                        {
                            int level = Convert.ToInt32(o["CurrentBrightness"]);
                            Post(() => { brightness = level; brightnessBar.Snap(level / 100f); });
                            break;
                        }
                    brightnessWatcher = new ManagementEventWatcher(scope, new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                    brightnessWatcher.EventArrived += (s, e) =>
                    {
                        int level = Convert.ToInt32(e.NewEvent["Brightness"]);
                        Post(() => OnBrightness(level));
                    };
                    brightnessWatcher.Start();
                }
                catch (Exception ex) { Log.Write("brightness (no built-in panel?): " + ex.Message); }
            });
        }

        void StopBrightness()
        {
            try { if (brightnessWatcher != null) { brightnessWatcher.Stop(); brightnessWatcher.Dispose(); } } catch { }
        }

        void OnBrightness(int level)
        {
            if (level == brightness) return;
            brightness = level;
            brightnessBar.Target = level / 100f;
            ShowToast(View.Brightness, 1.6, 1, false, true); // a direct user action, like volume
        }

        void DrawBrightness(float w, float h)
        {
            float cy = h / 2, level = Ease.Clamp01(brightnessBar.Value);
            // a sun whose rays grow with the brightness
            Color c = Theme.Text;
            paint.FillCircle(25, cy, 4.2f, c);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                float r0 = 6.6f, r1 = r0 + 1.5f + 3.2f * level;
                paint.G.DrawLine(paint.P(c, 1.6f), 25 + (float)Math.Cos(a) * r0, cy + (float)Math.Sin(a) * r0,
                                 25 + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1);
            }
            var track = new RectangleF(48, cy - 3.5f, w - 48 - 60, 7);
            paint.FillRound(track, 3.5f, Theme.FillHover);
            if (level > 0.005f)
                paint.FillRound(new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * level), track.Height), 3.5f, c);
            paint.TextRight(Math.Max(0, brightness).ToString(), Theme.Semi(13.5f), Theme.Text, w - 20, cy);
        }
    }
}
