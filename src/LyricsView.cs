using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Gili
{
    /// <summary>
    /// Synced lyrics, two ways: a karaoke line inside the compact island (the island breathes to fit each line and
    /// the colour sweeps across as it is sung) and a scrolling teleprompter in the music panel.
    /// </summary>
    partial class IslandForm
    {
        static readonly int[] LyricBands = { 3, 8, 13 };
        readonly LyricsService lyricsService = new LyricsService();
        readonly Spring lyricScroll = new Spring(0, 200, 25, 0.002f);
        Lyrics lyrics;
        int lyricIndex = -1, lyricPrevIndex = -1;
        DateTime lyricChangedUtc;

        bool LyricsEnabled { get { return settings.GetBool("lyrics", true); } }
        bool LyricsPanel { get { return settings.GetBool("lyricsPanel", false); } }
        bool LyricsReady { get { return lyrics != null && lyrics.Status == LyricsStatus.Synced && lyrics.TrackKey == mediaInfo.TrackKey; } }

        string lyricsSeenKey;
        DateTime lyricsSeenUtc;

        /// <summary>Called on every media update and once a second: (re)requests lyrics for the current track.</summary>
        void SyncLyricsTrack()
        {
            if (!LyricsEnabled || !mediaInfo.Present) { lyrics = null; return; }
            if (mediaInfo.TrackKey.StartsWith("demo")) return; // demo / video bring their own lyrics
            if (mediaInfo.TrackKey != lyricsSeenKey)
            {
                lyricsSeenKey = mediaInfo.TrackKey;
                lyricsSeenUtc = DateTime.UtcNow;
            }
            if (lyrics != null && lyrics.TrackKey == mediaInfo.TrackKey && !lyricsService.ShouldRetry(lyrics)) return;
            var l = lyricsService.Get(mediaInfo, (DateTime.UtcNow - lyricsSeenUtc).TotalSeconds > 5);
            if (l == lyrics) return;
            lyrics = l;
            lyricIndex = lyricPrevIndex = -1;
            lyricScroll.Snap(0);
        }

        void OnLyricsReady(Lyrics l)
        {
            if (!mediaInfo.Present || l.TrackKey != mediaInfo.TrackKey) return;
            lyrics = l;
            lyricIndex = lyricPrevIndex = -1;
            Kick();
        }

        /// <summary>Per-frame: follows the song position and re-plans the island when a new line starts.</summary>
        void UpdateLyrics(DateTime now)
        {
            if (!LyricsReady) return;
            int idx = lyrics.IndexAt(mediaInfo.CurrentPositionMs(now));
            if (idx != lyricIndex)
            {
                lyricPrevIndex = lyricIndex;
                lyricIndex = idx;
                lyricChangedUtc = now;
                lyricScroll.Target = Math.Max(0, idx);
                Kick();
            }
            else if (LyricOnScreen(now) != (current == View.Lyric) && (current == View.Lyric || current == View.Media))
            {
                Kick(); // an instrumental break started or ended
            }
        }

        double LyricPosition(DateTime now) { return mediaInfo.CurrentPositionMs(now) + (lyrics != null ? lyrics.OffsetMs : 0); }

        /// <summary>Whether the compact island should show a lyric line right now (not during instrumental breaks).</summary>
        bool LyricOnScreen(DateTime now)
        {
            if (!LyricsReady || !mediaInfo.Playing || lyricIndex < 0 || lyricIndex >= lyrics.Lines.Count) return false;
            if (lyrics.Lines[lyricIndex].Text.Length == 0) return false;
            return LyricPosition(now) < lyrics.SweepEndMs(lyricIndex) + 3500;
        }

        float LyricProgress(int i, DateTime now)
        {
            double start = lyrics.Lines[i].TimeMs, end = lyrics.SweepEndMs(i);
            return Ease.Clamp01((float)((LyricPosition(now) - start) / Math.Max(1, end - start)));
        }

        float LyricWidth()
        {
            if (!LyricsReady || lyricIndex < 0 || lyricIndex >= lyrics.Lines.Count) return 236;
            float tw = paint.Measure(lyrics.Lines[lyricIndex].Text, Theme.Semi(13.5f));
            return Math.Max(200, Math.Min(430, 84 + Math.Min(tw, 310) + PrivacyExtra()));
        }

        void DrawLyricCompact(float w, float h, DateTime now)
        {
            float cy = h / 2;
            DrawCover(mediaInfo, new RectangleF(7, 6, h - 12, h - 12), 7);
            float barsW = 3 * 3f + 2 * 2.4f, right = w - 15 - PrivacyExtra();
            DrawBars(right, cy, LyricBands, 3f, 2.4f, 14f, mediaInfo.Accent);
            DrawPrivacyDot(w, cy);
            if (!LyricsReady) return;

            float x0 = 7 + (h - 12) + 11, x1 = right - barsW - 12;
            var area = new RectangleF(x0, 0, x1 - x0, h);
            var state = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            float k = Ease.OutCubic((float)(now - lyricChangedUtc).TotalSeconds / 0.38f);
            var font = Theme.Semi(13.5f);
            if (k < 1 && lyricPrevIndex >= 0 && lyricPrevIndex < lyrics.Lines.Count)
                DrawLyricLine(lyrics.Lines[lyricPrevIndex].Text, font, area, -1, cy - 13 * k, 1 - k, 1f);
            if (lyricIndex >= 0 && lyricIndex < lyrics.Lines.Count)
                DrawLyricLine(lyrics.Lines[lyricIndex].Text, font, area, -1, cy + 13 * (1 - k), k, LyricProgress(lyricIndex, now));
            g.Restore(state);
        }

        /// <summary>
        /// One line with the karaoke sweep: dim text, overdrawn in full colour up to <paramref name="progress"/>.
        /// Lines wider than the area scroll so the sweep stays in view. centerX &lt; 0 means left-aligned.
        /// </summary>
        void DrawLyricLine(string text, Font font, RectangleF area, float centerX, float cy, float alpha, float progress)
        {
            if (alpha <= 0.01f || string.IsNullOrEmpty(text)) return;
            float tw = paint.Measure(text, font);
            float tx;
            if (tw > area.Width) tx = area.X - Math.Max(0, Math.Min(tw - area.Width, tw * progress - area.Width * 0.6f));
            else tx = centerX < 0 ? area.X : centerX - tw / 2;

            float a0 = paint.Alpha;
            paint.Alpha = a0 * alpha;
            paint.Text(text, font, Theme.WithAlpha(Theme.Text, 0.34f), tx, cy, tw + 24, StringAlignment.Near);
            if (progress > 0)
            {
                float edge = tx + tw * progress;
                var state = g.Save();
                g.SetClip(new RectangleF(tx - 4, cy - 30, edge - tx + 4, 60), CombineMode.Intersect);
                paint.Text(text, font, Theme.Text, tx, cy, tw + 24, StringAlignment.Near);
                g.Restore(state);
                if (progress < 1) // soft leading edge of the sweep
                {
                    state = g.Save();
                    g.SetClip(new RectangleF(edge, cy - 30, 7, 60), CombineMode.Intersect);
                    paint.Alpha = a0 * alpha * 0.55f;
                    paint.Text(text, font, Theme.Text, tx, cy, tw + 24, StringAlignment.Near);
                    g.Restore(state);
                }
            }
            paint.Alpha = a0;
        }

        /// <summary>Teleprompter in the music panel; click a line to jump there.</summary>
        void DrawLyricsPanel(float w, float h, DateTime now)
        {
            var m = mediaInfo;
            DrawCover(m, new RectangleF(18, 46, 32, 32), 8);
            paint.Text(m.Title, Theme.Semi(13f), Theme.Text, 60, 55, w - 60 - 96, StringAlignment.Near);
            paint.Text(m.Artist.Length > 0 ? m.Artist : m.App, Theme.Ui(11.5f), Theme.Sub, 60, 71, w - 60 - 96, StringAlignment.Near);

            const float mid = 124, lineH = 25;
            var area = new RectangleF(18, 84, w - 36, 86);
            if (lyrics == null || lyrics.TrackKey != m.TrackKey || lyrics.Status == LyricsStatus.Loading)
            {
                paint.TextCenter("Mencari lirik…", Theme.Ui(12.5f), Theme.Sub, w / 2, mid);
            }
            else if (lyrics.Status == LyricsStatus.NotFound)
            {
                paint.TextCenter("Lirik belum tersedia untuk lagu ini", Theme.Semi(13f), Theme.Sub, w / 2, mid - 8);
                paint.TextCenter("Sumber: LRCLIB", Theme.Ui(11f), Theme.Faint, w / 2, mid + 12);
            }
            else
            {
                var state = g.Save();
                g.SetClip(area, CombineMode.Intersect);
                float v = lyricScroll.Value;
                int first = (int)Math.Floor(v) - 2;
                for (int i = first; i <= first + 5; i++)
                {
                    if (i < 0 || i >= lyrics.Lines.Count) continue;
                    float ly = mid + (i - v) * lineH;
                    float alpha = Math.Max(0, 1 - Math.Abs(i - v) * 0.5f);
                    bool isCurrent = i == lyricIndex;
                    string text = lyrics.Lines[i].Text.Length > 0 ? lyrics.Lines[i].Text : "♪";
                    var row = new RectangleF(18, ly - 12, w - 36, 24);
                    string id = "lyr" + i;
                    if (Hover(id) && m.CanSeek) paint.FillRound(row, 12, Theme.Fill);
                    if (isCurrent)
                        DrawLyricLine(text, Theme.Semi(15f), new RectangleF(30, 0, w - 60, h), w / 2, ly, alpha, LyricProgress(i, now));
                    else
                        paint.Text(text, Theme.Ui(13f), Theme.WithAlpha(Theme.Text, 0.5f * alpha), w / 2, ly, w - 60, StringAlignment.Center);
                    if (m.CanSeek)
                    {
                        double target = Math.Max(0, lyrics.Lines[i].TimeMs - lyrics.OffsetMs + 50);
                        AddHit(row, id, delegate { media.Seek(target); });
                    }
                }
                g.Restore(state);
            }

            if (m.DurationMs > 1000)
            {
                float frac = Ease.Clamp01((float)(m.CurrentPositionMs(now) / m.DurationMs));
                var bar = new RectangleF(18, 178, w - 36, 3);
                paint.FillRound(bar, 1.5f, Theme.FillHover);
                paint.FillRound(new RectangleF(bar.X, bar.Y, Math.Max(3, bar.Width * frac), 3), 1.5f, Theme.Text);
            }

            float ccx = w / 2, ccy = 197;
            CircleButton(ccx - 46, ccy, 15, "prev", media.Previous);
            paint.SkipIcon(ccx - 46, ccy, 13, Theme.Text, false);
            CircleButton(ccx, ccy, 17, "play", media.TogglePlay);
            if (m.Playing) paint.PauseIcon(ccx, ccy, 14, Theme.Text);
            else paint.PlayIcon(ccx + 1, ccy, 15, Theme.Text);
            CircleButton(ccx + 46, ccy, 15, "next", media.Next);
            paint.SkipIcon(ccx + 46, ccy, 13, Theme.Text, true);

            if (LyricsReady)
            {
                // Music videos often run a few seconds ahead of or behind the album version; nudge to fit.
                CircleButton(30, ccy, 11, "lyrEarly", delegate { NudgeLyrics(-500); });
                paint.TextCenter("−", Theme.Semi(14f), Theme.Sub, 30, ccy);
                bool shifted = Math.Abs(lyrics.OffsetMs) >= 1;
                string off = (lyrics.OffsetMs >= 0 ? "+" : "−") + (Math.Abs(lyrics.OffsetMs) / 1000.0).ToString("0.0").Replace('.', ',') + "s";
                var offRect = new RectangleF(44, ccy - 11, 36, 22);
                if (shifted && Hover("lyrReset")) paint.FillRound(offRect, 11, Theme.Fill);
                paint.TextCenter(Hover("lyrReset") && shifted ? "reset" : off, Theme.Ui(11f), shifted ? Theme.Sub : Theme.Faint, 62, ccy);
                if (shifted) AddHit(offRect, "lyrReset", delegate { NudgeLyrics(-lyrics.OffsetMs); });
                CircleButton(94, ccy, 11, "lyrLate", delegate { NudgeLyrics(500); });
                paint.TextCenter("+", Theme.Semi(14f), Theme.Sub, 94, ccy);
            }
        }

        void NudgeLyrics(double deltaMs)
        {
            if (lyrics == null) return;
            lyrics.OffsetMs = Math.Max(-30000, Math.Min(30000, lyrics.OffsetMs + deltaMs));
            lyricsService.SaveOffset(lyrics);
            Kick();
        }

        /// <summary>"Lirik" switch in the top-right corner of the music panel.</summary>
        void DrawLyricsToggle(float w)
        {
            if (!LyricsEnabled) return;
            bool on = LyricsPanel;
            var r = new RectangleF(w - 18 - 62, 46, 62, 24);
            Color accent = mediaInfo.Accent;
            paint.FillRound(r, 12, on ? Theme.WithAlpha(accent, 0.24f) : Hover("lyrToggle") ? Theme.FillHover : Theme.Fill);
            paint.TextCenter("Lirik", Theme.Semi(11.5f), on ? accent : Theme.Sub, r.X + r.Width / 2, r.Y + 12);
            AddHit(r, "lyrToggle", delegate { SetSetting("lyricsPanel", !LyricsPanel); });
        }
    }
}
