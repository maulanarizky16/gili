using System;
using System.Drawing;

namespace Gili
{
    /// <summary>The fourth tab: everything adjustable, changed with one click and applied immediately.</summary>
    partial class IslandForm
    {
        static readonly int[] RevealDelays = { 500, 1000, 2000, 3000 };
        bool autoStartCached;
        Color windowsAccentCached = Color.FromArgb(255, 0, 120, 212);

        /// <summary>Reads the registry-backed values once when the page opens rather than on every frame.</summary>
        void RefreshAutoStart()
        {
            try { autoStartCached = AutoStart.Enabled; }
            catch { autoStartCached = false; }
            windowsAccentCached = Theme.WindowsAccent();
        }

        void SetSetting(string key, object value)
        {
            settings.Set(key, value);
            settings.Save();
            switch (key)
            {
                case "theme":
                    Theme.Apply((string)value);
                    UpdateGlass();
                    break;
                case "weather":
                    nextWeatherUtc = DateTime.MinValue;
                    MaybeRefreshWeather();
                    break;
                case "size":
                    UpdateMonitor();
                    break;
                case "display":
                    if (AutoHide) Reveal(); // keep it on screen while the user is still in here
                    UpdateEdgeWatch();
                    break;
                case "lyrics":
                    lyrics = null;
                    SyncLyricsTrack();
                    break;
                case "notif":
                    UpdateNotifWatch();
                    break;
                case "notif.all":
                    notifPrimed = false; // re-list without announcing what was already there
                    FetchNotifs(true);
                    break;
            }
            Kick();
        }

        void DrawSettingsPage(float w, float h)
        {
            const float labelX = 18, controlX = 132;
            float y = 62;

            SettingLabel("Tampilan", labelX, y, true);
            Segmented("display", controlX, y, new[] { "Selalu tampil", "Sembunyi otomatis" }, AutoHide ? 1 : 0, true,
                      i => SetSetting("display", i == 1 ? "auto" : "always"));

            y += 38;
            int delayIndex = 0;
            for (int i = 0; i < RevealDelays.Length; i++)
                if (Math.Abs(RevealDelays[i] - RevealDelayMs) < Math.Abs(RevealDelays[delayIndex] - RevealDelayMs)) delayIndex = i;
            SettingLabel("Muncul setelah", labelX, y, AutoHide);
            Segmented("delay", controlX, y, new[] { "0,5 dtk", "1 dtk", "2 dtk", "3 dtk" }, delayIndex, AutoHide,
                      i => SetSetting("revealDelay", RevealDelays[i]));

            y += 38;
            SettingLabel("Buka panel", labelX, y, true);
            Segmented("open", controlX, y, new[] { "Arahkan mouse", "Klik" }, settings.Get("open", "hover") == "click" ? 1 : 0, true,
                      i => SetSetting("open", i == 1 ? "click" : "hover"));

            y += 38;
            SettingLabel("Tema", labelX, y, true);
            string theme = settings.Get("theme", "hitam");
            int themeIndex = Math.Max(0, Array.IndexOf(Theme.Names, theme));
            for (int i = 0; i < Theme.Names.Length; i++)
            {
                int k = i;
                float sx = controlX + 13 + i * 30;
                string id = "theme" + i;
                if (i == themeIndex) paint.FillCircle(sx, y, 14.5f, Theme.Text);
                else if (Hover(id)) paint.FillCircle(sx, y, 14.5f, Theme.Faint);
                paint.FillCircle(sx, y, 12.5f, i == themeIndex ? bgColor : Theme.Bg);
                DrawSwatch(i, sx, y, 10.5f);
                AddHit(new RectangleF(sx - 14, y - 14, 28, 28), id, delegate { SetSetting("theme", Theme.Names[k]); });
            }
            paint.Text(Theme.Labels[themeIndex], Theme.Ui(12f), Theme.Sub, controlX + Theme.Names.Length * 30 + 6, y);

            y += 38;
            SettingLabel("Bentuk", labelX, y, true);
            float end = Segmented("shape", controlX, y, new[] { "Melayang", "Notch" }, Notch ? 1 : 0, true,
                                  i => SetSetting("shape", i == 1 ? "notch" : "float"));
            SettingLabel("Ukuran", end + 18, y, true);
            string size = settings.Get("size", "m");
            Segmented("size", end + 18 + paint.Measure("Ukuran", Theme.Ui(12f)) + 10, y, new[] { "S", "M", "L" },
                      size == "s" ? 0 : size == "l" ? 2 : 1, true, i => SetSetting("size", i == 0 ? "s" : i == 2 ? "l" : "m"));

            y += 42;
            float x = labelX;
            x = Toggle("tgViz", x, y, "Visualizer", settings.GetBool("visualizer", true),
                       delegate { SetSetting("visualizer", !settings.GetBool("visualizer", true)); });
            x = Toggle("tgLyrics", x + 6, y, "Lirik", LyricsEnabled, delegate { SetSetting("lyrics", !LyricsEnabled); });
            x = Toggle("tgAdzan", x + 6, y, "Adzan", settings.GetBool("prayer.notify", true),
                       delegate { SetSetting("prayer.notify", !settings.GetBool("prayer.notify", true)); });
            Toggle("tgHint", x + 6, y, "Garis petunjuk", settings.GetBool("hintLine", true),
                   delegate { SetSetting("hintLine", !settings.GetBool("hintLine", true)); });
            y += 34;
            x = Toggle("tgAuto", labelX, y, "Mulai bersama Windows", autoStartCached, delegate
            {
                try { AutoStart.Enabled = !autoStartCached; }
                catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
                RefreshAutoStart();
                Kick();
            });
            x = Toggle("tgWeather", x + 6, y, "Cuaca", WeatherEnabled, delegate { SetSetting("weather", !WeatherEnabled); });
            x = Toggle("tgClip", x + 6, y, "Clipboard", ClipboardEnabled, delegate { SetSetting("clipboard", !ClipboardEnabled); });
            Toggle("tgMascot", x + 6, y, "Maskot", MascotEnabled, delegate { SetSetting("mascot", !MascotEnabled); });
            y += 34;
            x = Toggle("tgNotif", labelX, y, "Notifikasi pesan", NotifEnabled, delegate { SetSetting("notif", !NotifEnabled); });
            x = Toggle("tgNotifText", x + 6, y, "Tampilkan isi", NotifContent, delegate { SetSetting("notif.content", !NotifContent); });
            Toggle("tgNotifAll", x + 6, y, "Semua aplikasi", NotifAllApps, delegate { SetSetting("notif.all", !NotifAllApps); });
        }

        void SettingLabel(string text, float x, float cy, bool enabled)
        {
            paint.Text(text, Theme.Ui(12f), enabled ? Theme.Sub : Theme.Faint, x, cy);
        }

        /// <summary>Pill-shaped segmented control; returns its right edge.</summary>
        float Segmented(string id, float x, float cy, string[] options, int selected, bool enabled, Action<int> pick)
        {
            var font = Theme.Semi(11.5f);
            const float height = 26, pad = 11;
            var widths = new float[options.Length];
            float total = 4;
            for (int i = 0; i < options.Length; i++) { widths[i] = paint.Measure(options[i], font) + pad * 2; total += widths[i]; }

            float baseAlpha = paint.Alpha;
            if (!enabled) paint.Alpha = baseAlpha * 0.4f;
            paint.FillRound(new RectangleF(x, cy - height / 2, total, height), height / 2, Theme.Fill);
            float ox = x + 2;
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                var r = new RectangleF(ox, cy - height / 2 + 2, widths[i], height - 4);
                bool on = i == selected;
                if (on) paint.FillRound(r, r.Height / 2, Theme.IsLight ? Color.White : Theme.FillHover);
                else if (enabled && Hover(id + i)) paint.FillRound(r, r.Height / 2, Theme.Fill);
                paint.TextCenter(options[i], font, on ? Theme.Text : Theme.Sub, r.X + r.Width / 2, cy);
                if (enabled && !on) AddHit(r, id + i, delegate { pick(k); });
                ox += widths[i];
            }
            paint.Alpha = baseAlpha;
            return x + total;
        }

        /// <summary>On/off chip with a dot; returns its right edge.</summary>
        float Toggle(string id, float x, float cy, string label, bool on, Action toggle)
        {
            var font = Theme.Semi(11.5f);
            float width = paint.Measure(label, font) + 34;
            var r = new RectangleF(x, cy - 14, width, 28);
            Color accent = Theme.Prayer;
            paint.FillRound(r, 14, on ? Theme.WithAlpha(accent, 0.2f) : Hover(id) ? Theme.FillHover : Theme.Fill);
            if (on) paint.FillCircle(r.X + 14, cy, 4f, accent);
            else paint.G.DrawEllipse(paint.P(Theme.Sub, 1.3f), r.X + 10, cy - 4, 8, 8);
            paint.Text(label, font, on ? accent : Theme.Sub, r.X + 24, cy);
            AddHit(r, id, toggle);
            return r.Right;
        }

        void DrawSwatch(int index, float cx, float cy, float radius)
        {
            var rect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
            switch (Theme.Names[index])
            {
                case "hitam":
                    paint.FillCircle(cx, cy, radius, Color.Black);
                    paint.G.DrawEllipse(paint.P(Color.FromArgb(90, 255, 255, 255), 1f), rect);
                    break;
                case "adaptif":
                    paint.G.FillPie(paint.B(Color.FromArgb(255, 236, 72, 153)), rect.X, rect.Y, rect.Width, rect.Height, 90, 120);
                    paint.G.FillPie(paint.B(Color.FromArgb(255, 59, 130, 246)), rect.X, rect.Y, rect.Width, rect.Height, 210, 120);
                    paint.G.FillPie(paint.B(Color.FromArgb(255, 250, 204, 21)), rect.X, rect.Y, rect.Width, rect.Height, 330, 120);
                    break;
                case "terang":
                    paint.FillCircle(cx, cy, radius, Color.FromArgb(255, 246, 246, 248));
                    paint.G.DrawEllipse(paint.P(Color.FromArgb(70, 0, 0, 0), 1f), rect);
                    break;
                case "aksen":
                    paint.FillCircle(cx, cy, radius, windowsAccentCached);
                    break;
                case "kaca":
                    using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Color.FromArgb((int)(230 * paint.Alpha), 190, 220, 255),
                                                                                     Color.FromArgb((int)(150 * paint.Alpha), 120, 140, 200), 45f))
                        paint.G.FillEllipse(lg, rect);
                    paint.G.DrawArc(paint.P(Color.FromArgb(230, 255, 255, 255), 1.4f), RectangleF.Inflate(rect, -2.5f, -2.5f), 200, 110);
                    break;
                default:
                    paint.FillCircle(cx, cy, radius, Color.FromArgb(255, 10, 36, 27));
                    paint.FillCircle(cx + 2.5f, cy - 1.5f, radius * 0.42f, Color.FromArgb(255, 233, 196, 106));
                    break;
            }
        }
    }
}
