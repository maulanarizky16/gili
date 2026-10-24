using System;
using System.Drawing;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    /// <summary>
    /// "Sembunyi otomatis": the island collapses into a thin click-through line so it never covers browser
    /// tabs. Holding the pointer against the top edge (near the centre) for a moment brings it back.
    /// </summary>
    partial class IslandForm
    {
        readonly WinTimer edgeTimer = new WinTimer { Interval = 80 };
        bool revealed, clickThrough;
        DateTime dwellStartUtc = DateTime.MinValue, lastNearUtc, revealedAtUtc;
        float dwellProgress;
        int windowLeftPx;

        // New users start with the island always visible (easier to discover); hiding is opt-in in Settings.
        bool AutoHide { get { return settings.Get("display", "always") == "auto"; } }
        int RevealDelayMs { get { return Math.Max(200, Math.Min(5000, settings.GetInt("revealDelay", 1000))); } }

        void UpdateEdgeWatch()
        {
            if (AutoHide) { if (!edgeTimer.Enabled) edgeTimer.Start(); }
            else
            {
                edgeTimer.Stop();
                revealed = false;
                dwellStartUtc = DateTime.MinValue;
                dwellProgress = 0;
            }
        }

        void EdgeTick()
        {
            if (!AutoHide || fullscreen || !windowShown && opacity.Target == 0) return;
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            var now = DateTime.UtcNow;
            int centerX = monitorPx.Left + monitorPx.Width / 2;
            bool inZone = pt.Y <= monitorPx.Top + 1 && pt.X >= monitorPx.Left && pt.X < monitorPx.Right &&
                          Math.Abs(pt.X - centerX) <= (int)(240 * scale);

            if (!revealed)
            {
                if (inZone && toast == null && !expanded)
                {
                    if (dwellStartUtc == DateTime.MinValue) dwellStartUtc = now;
                    float p = (float)((now - dwellStartUtc).TotalMilliseconds / RevealDelayMs);
                    if (p >= 1f) Reveal();
                    else if (Math.Abs(p - dwellProgress) > 0.01f) { dwellProgress = p; Kick(); }
                }
                else if (dwellStartUtc != DateTime.MinValue)
                {
                    dwellStartUtc = DateTime.MinValue;
                    dwellProgress = 0;
                    Kick();
                }
                return;
            }

            // Revealed: slide away again once the pointer has left the island for a moment.
            var r = IslandScreenRect();
            r.Inflate((int)(24 * scale), (int)(24 * scale));
            bool near = pointerInside || expanded || toast != null || demoActive || inZone || r.Contains(pt.X, pt.Y);
            if (near) lastNearUtc = now;
            else if ((now - lastNearUtc).TotalMilliseconds > 1200 && (now - revealedAtUtc).TotalMilliseconds > 1500)
            {
                revealed = false;
                Kick();
            }
        }

        void Reveal()
        {
            revealed = true;
            revealedAtUtc = lastNearUtc = DateTime.UtcNow;
            dwellStartUtc = DateTime.MinValue;
            dwellProgress = 0;
            Kick();
        }

        Rectangle IslandScreenRect()
        {
            return new Rectangle(windowLeftPx + (int)(shellRect.X * scale), monitorPx.Top + (int)(shellRect.Y * scale),
                                 (int)(shellRect.Width * scale), (int)(shellRect.Height * scale));
        }

        /// <summary>While only the hint line shows, every click must reach the window underneath (e.g. Edge tabs).</summary>
        void UpdateClickThrough()
        {
            bool want = current == View.Hint;
            if (want == clickThrough || !IsHandleCreated) return;
            clickThrough = want;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex = want ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            if (want)
            {
                pointerInside = false;
                hoverId = null;
            }
        }

        /// <summary>The hint line quietly shows what is going on: timer, music or an upcoming prayer.</summary>
        Color HintColor()
        {
            if (pomo.Running) return PomoColor(pomo.Mode);
            if (mediaInfo.Playing) return mediaInfo.Accent;
            if (PrayerSoon()) return Theme.Prayer;
            return Theme.Hint;
        }

        float HintAlpha()
        {
            bool line = settings.GetBool("hintLine", true);
            return line ? 0.45f + 0.55f * dwellProgress : Ease.Clamp01(dwellProgress * 1.5f);
        }
    }
}
