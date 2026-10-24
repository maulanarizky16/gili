using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Gili
{
    /// <summary>Drawing of every island view. All coordinates are DIPs relative to the view's top-left.</summary>
    partial class IslandForm
    {
        static readonly string[] DayNames = { "Min", "Sen", "Sel", "Rab", "Kam", "Jum", "Sab" };
        static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des" };
        static readonly int[] CompactBands = { 1, 4, 7, 10, 14 };
        static readonly int[] TrackBands = { 2, 6, 10, 15 };
        static readonly Color Ink = Color.FromArgb(255, 22, 22, 24);

        PointF hitOrigin;
        bool collectHits;

        void DrawView(View v, float alpha, float viewScale, bool interactive, DateTime now)
        {
            SizeF size = ViewSize(v);
            float ox = shellRect.X + (shellRect.Width - size.Width) / 2f, oy = shellRect.Y;
            var state = g.Save();
            g.TranslateTransform(ox + size.Width / 2, oy + size.Height / 2);
            g.ScaleTransform(viewScale, viewScale);
            g.TranslateTransform(-size.Width / 2, -size.Height / 2);
            paint.Alpha = alpha;
            hitOrigin = new PointF(ox, oy);
            collectHits = interactive;

            float w = size.Width, h = size.Height;
            switch (v)
            {
                case View.Idle: DrawIdle(w, h); break;
                case View.Media: DrawMediaCompact(w, h); break;
                case View.Timer: DrawTimerCompact(w, h, now); break;
                case View.PrayerSoon: DrawPrayerSoon(w, h); break;
                case View.Volume: DrawVolume(w, h); break;
                case View.Charge: DrawCharge(w, h, now, false); break;
                case View.LowBattery: DrawCharge(w, h, now, true); break;
                case View.Track: DrawTrack(w, h); break;
                case View.Privacy: DrawPrivacy(w, h); break;
                case View.TimerDone: DrawTimerDone(w, h); break;
                case View.Adzan: DrawAdzan(w, h); break;
                case View.Info: DrawInfo(w, h); break;
                case View.Expanded: DrawExpanded(w, h, now); break;
                case View.Hint: break; // the shell itself is the line
                case View.Lyric: DrawLyricCompact(w, h, now); break;
                case View.Clip: DrawClipToast(w, h); break;
                case View.Rain: DrawRainToast(w, h); break;
                case View.Network: DrawNetwork(w, h, now); break;
                case View.Device: DrawDevice(w, h, now); break;
                case View.Saver: DrawSaver(w, h, now); break;
                case View.Eyes: DrawEyes(w - UnreadExtra(), h, now); DrawUnreadBadge(w - 8, h / 2); break;
                case View.MeetingSoon: DrawMeetingSoon(w, h); break;
                case View.Brightness: DrawBrightness(w, h); break;
                case View.Meeting: DrawMeetingAlert(w, h); break;
                case View.Notif: DrawNotifToast(w, h); break;
            }

            g.Restore(state);
            paint.Alpha = 1f;
            collectHits = false;
        }

        void AddHit(RectangleF r, string id, Action action)
        {
            if (!collectHits) return;
            hits.Add(new Hit { Rect = new RectangleF(r.X + hitOrigin.X, r.Y + hitOrigin.Y, r.Width, r.Height), Id = id, Action = action });
        }

        bool Hover(string id) { return hoverId == id; }

        // ---- text helpers ---------------------------------------------------------------------

        static string Hm(DateTime t) { return t.ToString("HH':'mm", CultureInfo.InvariantCulture); }
        static string ClockText() { return Hm(DateTime.Now); }

        static string DateText()
        {
            var d = DateTime.Now;
            return DayNames[(int)d.DayOfWeek] + ", " + d.Day + " " + MonthNames[d.Month - 1];
        }

        static string Mmss(double ms)
        {
            int total = (int)Math.Ceiling(Math.Max(0, ms) / 1000.0);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        static string MediaTime(double ms)
        {
            int total = (int)(Math.Max(0, ms) / 1000.0);
            if (total >= 3600) return (total / 3600) + ":" + (total / 60 % 60).ToString("00") + ":" + (total % 60).ToString("00");
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        static string Countdown(TimeSpan left)
        {
            if (left.TotalSeconds < 60) return "dalam " + Math.Max(1, (int)Math.Ceiling(left.TotalSeconds)) + " detik";
            int minutes = (int)Math.Ceiling(left.TotalMinutes);
            if (minutes < 60) return "dalam " + minutes + " menit";
            return "dalam " + (minutes / 60) + " jam " + (minutes % 60) + " menit";
        }

        static string ModeName(PomoMode m)
        {
            return m == PomoMode.Focus ? "Fokus" : m == PomoMode.Short ? "Istirahat" : "Istirahat panjang";
        }

        static Color PomoColor(PomoMode m)
        {
            return m == PomoMode.Focus ? Theme.Focus : m == PomoMode.Short ? Theme.Break : Theme.LongBreak;
        }

        string IdlePrayerText()
        {
            if (prayers == null) return null;
            int i; DateTime t;
            prayers.Next(DateTime.Now, out i, out t);
            return PrayerCalc.Names[i] + " " + Hm(t);
        }

        float IdleWidth()
        {
            float w = 16 + paint.Measure(ClockText(), Theme.Semi(13.5f)) + 16 + PrivacyExtra() + UnreadExtra();
            var wx = CurrentWeather();
            if (wx != null) w += 12 + 20 + paint.Measure(Temp(wx), Theme.Semi(12.5f));
            string pt = IdlePrayerText();
            if (pt != null) w += 24 + 14 + paint.Measure(pt, Theme.Ui(12.5f));
            return Math.Max(76, w);
        }

        float InfoWidth()
        {
            if (toast == null) return 200;
            float w = 40 + paint.Measure(toast.Title, Theme.Semi(13.5f)) + 18;
            if (!string.IsNullOrEmpty(toast.Detail)) w += 18 + paint.Measure(toast.Detail, Theme.Ui(12.5f));
            return Math.Min(360, Math.Max(150, w));
        }

        // ---- shared pieces ----------------------------------------------------------------------

        void DrawCover(MediaInfo m, RectangleF r, float radius)
        {
            if (m.Cover != null)
            {
                paint.Image(m.Cover, r, radius);
                return;
            }
            paint.FillRound(r, radius, Theme.WithAlpha(m.Accent, 0.22f));
            paint.Icon(Theme.IconMusic, r.Height * 0.46f, m.Accent, r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        void DrawBars(float right, float cy, int[] which, float barW, float gap, float maxH, Color c)
        {
            float x = right - which.Length * barW - (which.Length - 1) * gap;
            for (int i = 0; i < which.Length; i++)
            {
                float v = mediaInfo.Playing ? bands[which[i]] : 0;
                float h = Math.Max(barW, 3 + (maxH - 3) * v);
                paint.FillRound(new RectangleF(x + i * (barW + gap), cy - h / 2, barW, h), barW / 2, c);
            }
        }

        void DrawPrivacyDot(float w, float cy)
        {
            if (PrivacyExtra() <= 0) return;
            paint.FillCircle(w - 13, cy, 3.5f, camApps.Length > 0 ? Theme.Green : Theme.Orange);
        }

        void PillButton(RectangleF r, string id, string label, Color fill, Color text, Action action)
        {
            Color c = Hover(id) ? Theme.Mix(fill, Color.White, fill.A < 128 ? 0.12f : 0.18f) : fill;
            paint.FillRound(r, r.Height / 2, c);
            paint.TextCenter(label, Theme.Semi(13f), text, r.X + r.Width / 2, r.Y + r.Height / 2);
            AddHit(r, id, action);
        }

        void CircleButton(float cx, float cy, float radius, string id, Action action)
        {
            if (Hover(id)) paint.FillCircle(cx, cy, radius, Theme.FillHover);
            AddHit(new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2), id, action);
        }

        // ---- compact views --------------------------------------------------------------------

        void DrawIdle(float w, float h)
        {
            float cy = h / 2;
            paint.Text(ClockText(), Theme.Semi(13.5f), Theme.Text, 16, cy);
            var wx = CurrentWeather();
            if (wx != null)
            {
                float x = 16 + paint.Measure(ClockText(), Theme.Semi(13.5f)) + 12;
                WeatherArt.Draw(paint, wx.Code, x + 8, cy, 15, IsNight(DateTime.Now));
                paint.Text(Temp(wx), Theme.Semi(12.5f), Theme.Sub, x + 19, cy);
            }
            string pt = IdlePrayerText();
            if (pt != null)
            {
                float right = w - 16 - PrivacyExtra() - UnreadExtra();
                float tw = paint.Measure(pt, Theme.Ui(12.5f));
                paint.TextRight(pt, Theme.Ui(12.5f), Theme.Sub, right, cy);
                paint.Icon(Theme.IconMoon, 11.5f, Theme.Prayer, right - tw - 10, cy);
            }
            DrawUnreadBadge(w - 16 - PrivacyExtra() + 4, cy);
            DrawPrivacyDot(w, cy);
        }

        void DrawMediaCompact(float w, float h)
        {
            float cy = h / 2;
            DrawCover(mediaInfo, new RectangleF(7, 6, h - 12, h - 12), 7);
            Color bars = mediaInfo.Playing ? mediaInfo.Accent : Theme.Faint;
            DrawBars(w - 15 - PrivacyExtra(), cy, CompactBands, 3f, 2.4f, 16f, bars);
            DrawUnreadBadge(w - 15 - PrivacyExtra() - 25 - 2, cy);
            DrawPrivacyDot(w, cy);
        }

        void DrawTimerCompact(float w, float h, DateTime now)
        {
            float cy = h / 2;
            double rem = pomo.Remaining(now);
            Color c = PomoColor(pomo.Mode);
            bool run = pomo.Running;
            paint.Ring(20, cy, 8, 2.6f, (float)(rem / pomo.TotalMs()), Theme.WithAlpha(c, 0.25f), run ? c : Theme.Sub);
            paint.Text(run ? ModeName(pomo.Mode) : "Dijeda", Theme.Semi(12.5f), run ? Theme.Text : Theme.Sub, 36, cy, w - 36 - 70, StringAlignment.Near);
            paint.TextRight(Mmss(rem), Theme.Semi(14.5f), run ? c : Theme.Sub, w - 16 - PrivacyExtra(), cy);
            DrawPrivacyDot(w, cy);
        }

        void DrawPrayerSoon(float w, float h)
        {
            if (prayers == null) return;
            float cy = h / 2;
            int i; DateTime t;
            prayers.Next(DateTime.Now, out i, out t);
            paint.Icon(Theme.IconMoon, 13, Theme.Prayer, 20, cy);
            paint.Text(PrayerCalc.Names[i], Theme.Semi(12.5f), Theme.Text, 34, cy);
            paint.TextRight("−" + Mmss((t - DateTime.Now).TotalMilliseconds), Theme.Semi(14.5f), Theme.Prayer, w - 16 - PrivacyExtra(), cy);
            DrawPrivacyDot(w, cy);
        }

        // ---- live activities ------------------------------------------------------------------

        void DrawVolume(float w, float h)
        {
            float cy = h / 2;
            float level = Ease.Clamp01(volumeBar.Value);
            char icon = muted ? Theme.IconMute : level < 0.01f ? Theme.IconVol0 : level < 0.34f ? Theme.IconVol1 : level < 0.67f ? Theme.IconVol2 : Theme.IconVol3;
            paint.Icon(icon, 16, Theme.Text, 25, cy);
            var track = new RectangleF(48, cy - 3.5f, w - 48 - 60, 7);
            paint.FillRound(track, 3.5f, Theme.FillHover);
            if (level > 0.005f)
                paint.FillRound(new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * level), track.Height), 3.5f, muted ? Theme.Sub : Theme.Text);
            paint.TextRight(muted ? "Bisu" : ((int)Math.Round(Ease.Clamp01(volumeBar.Target) * 100)).ToString(), Theme.Semi(13.5f), Theme.Text, w - 20, cy);
        }

        void DrawTrack(float w, float h)
        {
            var m = mediaInfo;
            DrawCover(m, new RectangleF(12, 12, 50, 50), 12);
            float tx = 74, maxW = w - tx - 50;
            paint.Text(m.Title, Theme.Semi(14.5f), Theme.Text, tx, 29, maxW, StringAlignment.Near);
            paint.Text(m.Artist.Length > 0 ? m.Artist : m.App, Theme.Ui(12.5f), Theme.Sub, tx, 49, maxW, StringAlignment.Near);
            DrawBars(w - 18, h / 2, TrackBands, 3.2f, 2.6f, 22f, m.Accent);
        }

        void DrawPrivacy(float w, float h)
        {
            if (toast == null) return;
            float cy = h / 2;
            bool cam = toast.Flag;
            Color c = cam ? Theme.Green : Theme.Orange;
            paint.Icon(cam ? Theme.IconCam : Theme.IconMic, 14, c, 23, cy);
            paint.Text(cam ? "Kamera aktif" : "Mikrofon aktif", Theme.Semi(13f), c, 39, cy);
            paint.Text(toast.Title, Theme.Ui(12.5f), Theme.Sub, w - 16, cy, 110, StringAlignment.Far);
        }

        void DrawTimerDone(float w, float h)
        {
            bool wasFocus = pomo.LastCompleted == PomoMode.Focus;
            PomoMode next = pomo.Mode;
            Color c = PomoColor(next);
            paint.FillCircle(37, 37, 19, Theme.WithAlpha(c, 0.2f));
            paint.Icon(Theme.IconTimer, 17, c, 37, 37);
            string title = wasFocus ? "Sesi fokus selesai!" : "Istirahat selesai";
            string sub = !wasFocus ? "Siap fokus lagi? " + pomo.FocusMin + " menit."
                       : next == PomoMode.Long ? "Kerja bagus! Istirahat panjang " + pomo.LongMin + " menit."
                       : "Saatnya istirahat " + pomo.ShortMin + " menit.";
            paint.Text(title, Theme.Semi(15f), Theme.Text, 68, 29, w - 86, StringAlignment.Near);
            paint.Text(sub, Theme.Ui(12.5f), Theme.Sub, 68, 49, w - 86, StringAlignment.Near);

            float bw = (w - 36 - 10) * 0.62f;
            PillButton(new RectangleF(18, 72, bw, 32), "doneStart", wasFocus ? "Mulai istirahat" : "Mulai fokus", c, Ink, delegate
            {
                pomo.Start(DateTime.UtcNow);
                SavePomo();
                DismissToast();
            });
            PillButton(new RectangleF(18 + bw + 10, 72, w - 36 - bw - 10, 32), "doneLater", "Nanti", Theme.Fill, Theme.Text, DismissToast);
        }

        void DrawAdzan(float w, float h)
        {
            if (toast == null) return;
            paint.FillCircle(37, 36, 19, Theme.WithAlpha(Theme.Prayer, 0.2f));
            paint.Icon(Theme.IconMoon, 16, Theme.Prayer, 37, 36);
            string headline = toast.Title == "Imsak" ? "Waktu imsak"
                            : toast.Title == "Maghrib" && RamadanToday() ? "Waktunya berbuka puasa"
                            : "Waktunya sholat " + toast.Title;
            paint.Text(headline, Theme.Semi(15f), Theme.Text, 68, 28, w - 86, StringAlignment.Near);
            string city = prayers != null && prayers.City.Length > 0 ? " · " + prayers.City : "";
            paint.Text(toast.Detail + city, Theme.Ui(12.5f), Theme.Sub, 68, 48, w - 86, StringAlignment.Near);
            PillButton(new RectangleF(18, 68, w - 36, 30), "adzanClose", "Tutup", Theme.Fill, Theme.Text, DismissToast);
        }

        void DrawInfo(float w, float h)
        {
            if (toast == null) return;
            float cy = h / 2;
            paint.Icon(toast.Glyph, 14, toast.Color, 23, cy);
            float dw = string.IsNullOrEmpty(toast.Detail) ? 0 : paint.Measure(toast.Detail, Theme.Ui(12.5f)) + 18;
            paint.Text(toast.Title, Theme.Semi(13.5f), Theme.Text, 40, cy, w - 40 - 16 - dw, StringAlignment.Near);
            if (dw > 0) paint.TextRight(toast.Detail, Theme.Ui(12.5f), Theme.Sub, w - 16, cy);
        }

        // ---- expanded -----------------------------------------------------------------------

        void DrawExpanded(float w, float h, DateTime now)
        {
            char[] tabs = { Theme.IconMusic, Theme.IconTimer, Theme.IconMoon, Theme.IconCalendar, Theme.IconSystem, Theme.IconBell, Theme.IconPaste, Theme.IconFolder, Theme.IconSettings };
            paint.FillRound(new RectangleF(14 + pageSpring.Value * 36, 12, 32, 26), 13, Theme.FillHover);
            for (int i = 0; i < tabs.Length; i++)
            {
                int k = i;
                var r = new RectangleF(14 + i * 36, 12, 32, 26);
                if (Hover("tab" + i) && i != page) paint.FillRound(r, 13, Theme.Fill);
                paint.Icon(tabs[i], 14, i == page ? Theme.Text : Theme.Sub, r.X + 16, r.Y + 13);
                if (i == NotifPage && unreadNotifs > 0) paint.FillCircle(r.X + 23, r.Y + 6, 3.5f, Theme.Green);
                AddHit(r, "tab" + i, delegate { SetPage(k); });
            }

            float right = w - 18, cy = 25;
            if (now < volumeHudUntil)
            {
                string s = muted ? "Bisu" : (int)Math.Round(volume * 100) + "%";
                float sw2 = paint.Measure(s, Theme.Semi(12.5f));
                paint.TextRight(s, Theme.Semi(12.5f), Theme.Text, right, cy);
                paint.Icon(muted ? Theme.IconMute : Theme.IconVol3, 13, Theme.Text, right - sw2 - 12, cy);
            }
            else
            {
                if (hasBattery)
                {
                    string b = battery + "%";
                    float bw = paint.Measure(b, Theme.Ui(12f));
                    paint.TextRight(b, Theme.Ui(12f), Theme.Sub, right, cy);
                    Color fill = onAc ? Theme.Green : battery <= 20 ? Theme.Red : Theme.Text;
                    paint.Battery(right - bw - 7 - 25, cy, battery / 100f, fill);
                    right -= bw + 7 + 25 + 14;
                }
            }

            float baseAlpha = paint.Alpha;
            bool baseCollect = collectHits;
            for (int i = 0; i < tabs.Length; i++)
            {
                float d = i - pageSpring.Value;
                if (Math.Abs(d) >= 0.999f) continue;
                var state = g.Save();
                g.TranslateTransform(d * 46, 0);
                // Steep cross-fade: the old page is mostly gone before the new one shows, so they never muddle together.
                paint.Alpha = baseAlpha * Ease.InOut(Ease.Clamp01(1 - Math.Abs(d) * 1.8f));
                collectHits = baseCollect && Math.Abs(d) < 0.05f;
                if (i == 0) DrawMediaPage(w, h, now);
                else if (i == 1) DrawFocusPage(w, h, now);
                else if (i == 2) DrawPrayerPage(w, h);
                else if (i == AgendaPage) DrawAgendaPage(w, h);
                else if (i == SystemPage) DrawSystemPage(w, h);
                else if (i == NotifPage) DrawNotifPage(w, h);
                else if (i == ClipboardPage) DrawClipboardPage(w, h);
                else if (i == ShelfPage) DrawShelfPage(w, h);
                else DrawSettingsPage(w, h);
                g.Restore(state);
            }
            paint.Alpha = baseAlpha;
            collectHits = baseCollect;
        }

        void DrawMediaPage(float w, float h, DateTime now)
        {
            var m = mediaInfo;
            if (!m.Present)
            {
                paint.Icon(Theme.IconMusic, 24, Theme.Faint, w / 2, 98);
                paint.TextCenter("Tidak ada yang diputar", Theme.Semi(14f), Theme.Sub, w / 2, 134);
                paint.TextCenter("Putar lagu di Spotify, YouTube, atau aplikasi lain", Theme.Ui(11.5f), Theme.Faint, w / 2, 156);
                return;
            }

            if (LyricsEnabled && LyricsPanel)
            {
                DrawLyricsPanel(w, h, now);
                DrawLyricsToggle(w);
                return;
            }

            // ambient light from the artwork colour
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(new RectangleF(-70, -10, 280, 230));
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Theme.WithAlpha(m.Accent, 0.30f * paint.Alpha);
                    brush.SurroundColors = new[] { Color.FromArgb(0, m.Accent) };
                    g.FillPath(brush, path);
                }
            }

            // full-width spectrum behind the controls
            int n = Spectrum.Bands;
            float left = 18, gap = 3f, barW = (w - 36 - gap * (n - 1)) / n, bottom = h - 6;
            for (int i = 0; i < n; i++)
            {
                float v = m.Playing ? bands[i] : 0;
                float bh = 3 + 44 * v;
                paint.FillRound(new RectangleF(left + i * (barW + gap), bottom - bh, barW, bh), Math.Min(barW / 2, 3f),
                                Theme.WithAlpha(m.Accent, 0.14f + 0.16f * v));
            }

            DrawCover(m, new RectangleF(18, 48, 66, 66), 14);
            DrawLyricsToggle(w);
            float tx = 98, maxW = w - tx - 18 - (LyricsEnabled ? 70 : 0);
            paint.Text(m.Title, Theme.Semi(15.5f), Theme.Text, tx, 63, maxW, StringAlignment.Near);
            paint.Text(m.Artist, Theme.Ui(13f), Theme.Sub, tx, 85, maxW, StringAlignment.Near);
            paint.Text(m.App, Theme.Semi(11f), m.Accent, tx, 105, maxW, StringAlignment.Near);

            if (m.DurationMs > 1000)
            {
                double pos = m.CurrentPositionMs(now);
                float frac = (float)(pos / m.DurationMs);
                bool hov = Hover("seek") && m.CanSeek;
                var bar = new RectangleF(18, hov ? 130 : 131, w - 36, hov ? 6 : 4);
                paint.FillRound(bar, bar.Height / 2, Theme.FillHover);
                paint.FillRound(new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * Ease.Clamp01(frac)), bar.Height), bar.Height / 2, Theme.Text);
                paint.Text(MediaTime(pos), Theme.Ui(11f), Theme.Sub, 18, 147);
                paint.TextRight("-" + MediaTime(m.DurationMs - pos), Theme.Ui(11f), Theme.Sub, w - 18, 147);
                if (m.CanSeek)
                {
                    var windowBar = new RectangleF(bar.X + hitOrigin.X, bar.Y, bar.Width, bar.Height);
                    double duration = m.DurationMs;
                    AddHit(new RectangleF(bar.X - 2, 123, bar.Width + 4, 18), "seek", delegate
                    {
                        float f = Ease.Clamp01((mouseDip.X - windowBar.X) / windowBar.Width);
                        media.Seek(f * duration);
                    });
                }
            }

            float ccx = w / 2, ccy = 180;
            CircleButton(ccx - 62, ccy, 19, "prev", media.Previous);
            paint.SkipIcon(ccx - 62, ccy, 16, Theme.Text, false);
            CircleButton(ccx, ccy, 23, "play", media.TogglePlay);
            if (m.Playing) paint.PauseIcon(ccx, ccy, 18, Theme.Text);
            else paint.PlayIcon(ccx + 1.5f, ccy, 19, Theme.Text);
            CircleButton(ccx + 62, ccy, 19, "next", media.Next);
            paint.SkipIcon(ccx + 62, ccy, 16, Theme.Text, true);
        }

        void DrawFocusPage(float w, float h, DateTime now)
        {
            double rem = pomo.Remaining(now), total = pomo.TotalMs();
            Color c = PomoColor(pomo.Mode);
            float rcx = 84, rcy = 124, rr = 46;
            float frac = (float)(rem / total);
            if (Hover("ring")) paint.FillCircle(rcx, rcy, rr + 9, Theme.Fill);
            paint.Ring(rcx, rcy, rr, 6, frac, Theme.WithAlpha(c, 0.18f), c);
            if (pomo.Running)
            {
                double a = (-90 + 360 * frac) * Math.PI / 180;
                paint.FillCircle(rcx + rr * (float)Math.Cos(a), rcy + rr * (float)Math.Sin(a), 4.5f, Theme.Text);
            }
            paint.TextCenter(Mmss(rem), Theme.Semi(23f), Theme.Text, rcx, rcy - 6);
            paint.TextCenter(ModeName(pomo.Mode), Theme.Ui(11.5f), Theme.Sub, rcx, rcy + 17);
            AddHit(new RectangleF(rcx - rr - 8, rcy - rr - 8, rr * 2 + 16, rr * 2 + 16), "ring", ToggleTimer);

            float x = 166;
            string[] labels = { "Fokus " + pomo.FocusMin, "Pendek " + pomo.ShortMin, "Panjang " + pomo.LongMin };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var mode = (PomoMode)i;
                float cw = paint.Measure(labels[i], Theme.Semi(11.5f)) + 22;
                var r = new RectangleF(x, 50, cw, 26);
                bool selected = pomo.Mode == mode;
                paint.FillRound(r, 13, selected ? Theme.WithAlpha(PomoColor(mode), 0.22f) : Hover("mode" + i) ? Theme.FillHover : Theme.Fill);
                paint.TextCenter(labels[i], Theme.Semi(11.5f), selected ? PomoColor(mode) : Theme.Sub, r.X + cw / 2, r.Y + 13);
                AddHit(r, "mode" + i, delegate
                {
                    if (pomo.Running) return;
                    pomo.SetMode((PomoMode)k);
                    SavePomo();
                    Kick();
                });
                x += cw + 6;
            }

            for (int i = 0; i < 4; i++) paint.FillCircle(171 + i * 12, 96, 3.5f, i < pomo.Cycle % 4 ? c : Theme.Faint);
            paint.Text(pomo.DoneToday + " sesi selesai hari ini", Theme.Ui(11.5f), Theme.Sub, 171 + 4 * 12 + 2, 96);

            var main = new RectangleF(166, 122, 118, 38);
            paint.FillRound(main, 19, Hover("pomoMain") ? Theme.Mix(c, Color.White, 0.15f) : c);
            string label = pomo.Running ? "Jeda" : pomo.RemainingMs < total - 500 ? "Lanjut" : "Mulai";
            float lw = paint.Measure(label, Theme.Semi(13.5f));
            float ix = main.X + main.Width / 2 - (lw + 20) / 2 + 6;
            if (pomo.Running) paint.PauseIcon(ix, main.Y + 19, 12, Ink);
            else paint.PlayIcon(ix, main.Y + 19, 12, Ink);
            paint.Text(label, Theme.Semi(13.5f), Ink, ix + 14, main.Y + 19);
            AddHit(main, "pomoMain", ToggleTimer);

            CircleButton(308, 141, 19, "pomoReset", delegate { pomo.Reset(); SavePomo(); Kick(); });
            paint.Icon(Theme.IconReset, 14, Theme.Text, 308, 141);
            CircleButton(352, 141, 19, "pomoSkip", delegate { pomo.Skip(); SavePomo(); Kick(); });
            paint.SkipIcon(352, 141, 13, Theme.Text, true);

            if (!pomo.Running) paint.Text("Scroll di lingkaran untuk ubah durasi", Theme.Ui(10.5f), Theme.Faint, 166, 188);
        }

        void DrawPrayerPage(float w, float h)
        {
            if (prayers == null)
            {
                paint.Icon(Theme.IconPin, 22, Theme.Faint, w / 2, 96);
                paint.TextCenter(locating ? "Mendeteksi lokasi..." : "Lokasi belum diketahui", Theme.Semi(14f), Theme.Sub, w / 2, 130);
                if (!locating) PillButton(new RectangleF(w / 2 - 70, 150, 140, 30), "locate", "Deteksi lokasi", Theme.Fill, Theme.Text, delegate { DetectLocation(true); });
                return;
            }

            DrawTodayStrip(w);
            var shifted = g.Save();
            g.TranslateTransform(0, 22); // the Hijri / weather strip sits above the schedule

            var local = DateTime.Now;
            int ni; DateTime nt;
            prayers.Next(local, out ni, out nt);
            bool ramadan = RamadanToday();
            string label = ramadan && ni == PrayerCalc.Maghrib ? "Buka puasa" : ramadan && ni == PrayerCalc.Subuh ? "Subuh · imsak " + Hm(prayers.Times[PrayerCalc.Imsak]) : "Berikutnya";
            paint.Text(label, Theme.Ui(11.5f), ramadan ? Theme.Prayer : Theme.Sub, 18, 56);
            paint.Text(PrayerCalc.Names[ni], Theme.Semi(22f), Theme.Text, 18, 80);
            paint.Text(Hm(nt), Theme.Semi(30f), Theme.Prayer, 17, 114);
            paint.Text(Countdown(nt - local), Theme.Ui(12f), Theme.Sub, 18, 145);
            if (prayers.City.Length > 0)
            {
                paint.Icon(Theme.IconPin, 11, Theme.Faint, 24, 188);
                paint.Text(prayers.City, Theme.Ui(11.5f), Theme.Faint, 34, 188, 140, StringAlignment.Near);
            }

            int[] order = { PrayerCalc.Subuh, PrayerCalc.Terbit, PrayerCalc.Dzuhur, PrayerCalc.Ashar, PrayerCalc.Maghrib, PrayerCalc.Isya };
            float gx = 186, cw = (w - 18 - gx - 8) / 2, ch = 40;
            for (int k = 0; k < order.Length; k++)
            {
                int pi = order[k];
                var r = new RectangleF(gx + (k / 3) * (cw + 8), 46 + (k % 3) * (ch + 6), cw, ch);
                DateTime t = prayers.Times[pi];
                bool isNext = pi == ni && nt.Date == local.Date;
                bool passed = t <= local;
                paint.FillRound(r, 11, isNext ? Theme.WithAlpha(Theme.Prayer, 0.2f) : Theme.Fill);
                float a0 = paint.Alpha;
                if (passed) paint.Alpha = a0 * 0.45f;
                paint.Text(PrayerCalc.Names[pi], Theme.Ui(11.5f), isNext ? Theme.Prayer : Theme.Sub, r.X + 12, r.Y + 13);
                paint.Text(Hm(t), Theme.Semi(15f), isNext ? Theme.Prayer : Theme.Text, r.X + 12, r.Y + 28);
                paint.Alpha = a0;
            }
            string credit = "Imsak " + Hm(prayers.Times[PrayerCalc.Imsak]) + " · Kemenag RI";
            if (CurrentWeather() != null) credit += " · Cuaca: BMKG"; // BMKG's open data requires naming BMKG as the source
            paint.TextRight(credit, Theme.Ui(10.5f), Theme.Faint, w - 18, 192);
            g.Restore(shifted);
        }

        static int PageHeight(int p)
        {
            switch (p)
            {
                case 2: return 238;           // prayer + Hijri/weather strip
                case AgendaPage: return 250;
                case NotifPage: return 250;
                case ClipboardPage: return 226;
                case ShelfPage: return 232;
                case SettingsPage: return 352;
                default: return 212;
            }
        }

        // ---- bubble ---------------------------------------------------------------------------

        void DrawBubbleContent(DateTime now, float cy, float bx, float br, float alpha)
        {
            if (alpha <= 0.01f) return;
            paint.Alpha = alpha;
            if (bubblePage == 1)
            {
                double rem = pomo.Remaining(now);
                Color c = pomo.Running ? PomoColor(pomo.Mode) : Theme.Sub;
                paint.Ring(bx, cy, br - 5.5f, 2.4f, (float)(rem / pomo.TotalMs()), Theme.WithAlpha(c, 0.25f), c);
                paint.TextCenter(((int)Math.Ceiling(rem / 60000)).ToString(), Theme.Semi(10.5f), c, bx, cy);
            }
            else if (bubblePage == AgendaPage)
            {
                var e = SoonMeeting();
                if (e != null)
                {
                    float frac = (float)((e.Start - DateTime.Now).TotalMinutes / 15.0);
                    paint.Ring(bx, cy, br - 5.5f, 2.4f, frac, Theme.WithAlpha(MeetColor, 0.25f), MeetColor);
                    DrawCalendarIcon(bx, cy, 10, MeetColor);
                }
            }
            else if (prayers != null)
            {
                int i; DateTime t;
                prayers.Next(DateTime.Now, out i, out t);
                float frac = (float)((t - DateTime.Now).TotalMinutes / 15.0);
                paint.Ring(bx, cy, br - 5.5f, 2.4f, frac, Theme.WithAlpha(Theme.Prayer, 0.25f), Theme.Prayer);
                paint.Icon(Theme.IconMoon, 10, Theme.Prayer, bx, cy);
            }
            paint.Alpha = 1f;
        }
    }
}
