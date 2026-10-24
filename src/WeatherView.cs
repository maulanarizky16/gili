using System;
using System.Drawing;
using System.Threading;

namespace Gili
{
    /// <summary>Weather (BMKG), Hijri date and Ramadan touches on the island.</summary>
    partial class IslandForm
    {
        WeatherReport weather;
        DateTime nextWeatherUtc = DateTime.MinValue;
        bool weatherBusy;
        string rainNotified = "", imsakNotified = "";

        bool WeatherEnabled { get { return settings.GetBool("weather", true); } }
        int HijriAdjust { get { return settings.GetInt("hijri.adjust", 0); } }

        bool RamadanToday(out int day) { return Hijri.IsRamadan(DateTime.Now, HijriAdjust, out day); }
        bool RamadanToday() { int d; return RamadanToday(out d); }

        /// <summary>Hourly refresh (BMKG publishes 3-hour slots); retries sooner after a failure.</summary>
        void MaybeRefreshWeather()
        {
            if (!WeatherEnabled || weatherBusy || DateTime.UtcNow < nextWeatherUtc) return;
            if (!settings.Has("bmkg.adm4") && (!settings.Has("lat") || !settings.Has("lon"))) return;
            weatherBusy = true;
            string adm4 = settings.Get("bmkg.adm4", "");
            double lat = settings.GetDouble("lat", 0), lon = settings.GetDouble("lon", 0);
            ThreadPool.QueueUserWorkItem(delegate
            {
                WeatherReport report = null;
                string resolved = adm4;
                try
                {
                    if (resolved.Length == 0) resolved = WeatherService.ResolveAdm4(lat, lon) ?? "";
                    if (resolved.Length > 0) report = WeatherService.Fetch(resolved);
                }
                catch (Exception ex) { Log.Write("weather: " + ex.Message); }
                Post(() =>
                {
                    weatherBusy = false;
                    if (resolved.Length > 0 && resolved != settings.Get("bmkg.adm4", ""))
                    {
                        settings.Set("bmkg.adm4", resolved);
                        settings.Save();
                    }
                    nextWeatherUtc = DateTime.UtcNow.AddMinutes(report != null ? 60 : 10);
                    if (report != null) weather = report;
                    Kick();
                });
            });
        }

        WeatherEntry CurrentWeather()
        {
            return WeatherEnabled && weather != null ? weather.Current(DateTime.Now) : null;
        }

        /// <summary>Once per rainy slot: a heads-up when rain is expected within the next two hours.</summary>
        void CheckRain()
        {
            if (!WeatherEnabled || weather == null) return;
            var rain = weather.NextRain(DateTime.Now, 2);
            if (rain == null) return;
            string key = rain.Local.ToString("yyyyMMddHH");
            if (key == rainNotified) return;
            rainNotified = key;
            var t = ShowToast(View.Rain, 6, 2, false);
            if (t != null) t.Data = rain;
        }

        /// <summary>During Ramadan: a reminder at imsak (10 minutes before Subuh).</summary>
        void CheckImsak(DateTime local)
        {
            if (prayers == null || !settings.GetBool("prayer.notify", true) || !RamadanToday()) return;
            DateTime t = prayers.Times[PrayerCalc.Imsak];
            if (local < t || local >= t.AddMinutes(2)) return;
            string key = t.ToString("yyyyMMddHHmm");
            if (key == imsakNotified) return;
            imsakNotified = key;
            var ts = ShowToast(View.Adzan, 30, 3, true);
            if (ts != null) { ts.Title = "Imsak"; ts.Detail = Hm(t); }
            Chime.Adzan();
            Pulse(Theme.Prayer, 4);
        }

        static string Temp(WeatherEntry e) { return Math.Round(e.TempC) + "°"; }

        static bool IsNight(DateTime local) { return local.Hour < 6 || local.Hour >= 18; }

        // ---- views -------------------------------------------------------------------------------

        void DrawRainToast(float w, float h)
        {
            var e = toast != null ? toast.Data as WeatherEntry : null;
            if (e == null) return;
            float cy = h / 2;
            WeatherArt.Draw(paint, e.Code, 24, cy, 20, IsNight(e.Local));
            bool soon = e.Local <= DateTime.Now;
            paint.Text(soon ? e.Desc + " sekarang" : e.Desc + " ~" + Hm(e.Local), Theme.Semi(13.5f), Theme.Text, 42, cy, w - 42 - 100, StringAlignment.Near);
            paint.TextRight(weather != null ? weather.Place : "", Theme.Ui(12f), Theme.Sub, w - 16, cy);
        }

        /// <summary>Top strip of the prayer page: Hijri date on the left, current weather on the right.</summary>
        void DrawTodayStrip(float w)
        {
            int ramadanDay;
            string hijri = DateText() + " · " + Hijri.Format(DateTime.Now, HijriAdjust);
            if (RamadanToday(out ramadanDay)) hijri = "Ramadan hari ke-" + ramadanDay + " · " + hijri;
            paint.Text(hijri, Theme.Ui(11.5f), Theme.Sub, 18, 54, w / 2, StringAlignment.Near);
            var now = CurrentWeather();
            if (now == null) return;
            string text = Temp(now) + " " + now.Desc;
            float tw = paint.Measure(text, Theme.Ui(11.5f));
            paint.TextRight(text, Theme.Ui(11.5f), Theme.Sub, w - 18, 54);
            WeatherArt.Draw(paint, now.Code, w - 18 - tw - 12, 54, 15, IsNight(DateTime.Now));
        }
    }
}
