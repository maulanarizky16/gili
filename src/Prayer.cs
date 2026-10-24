using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gili
{
    /// <summary>
    /// Prayer times computed offline from coordinates, using the Kemenag RI convention:
    /// Subuh at sun depression 20°, Isya 18°, Ashar shadow factor 1 (Syafi'i), 2 minutes ihtiyat (3 for Dzuhur),
    /// Imsak 10 minutes before Subuh.
    /// </summary>
    static class PrayerCalc
    {
        public const int Imsak = 0, Subuh = 1, Terbit = 2, Dzuhur = 3, Ashar = 4, Maghrib = 5, Isya = 6;
        public static readonly string[] Names = { "Imsak", "Subuh", "Terbit", "Dzuhur", "Ashar", "Maghrib", "Isya" };
        public static readonly int[] Adzan = { Subuh, Dzuhur, Ashar, Maghrib, Isya };

        // Horizon 1.0° = refraction + semi-diameter + a small elevation allowance; calibrated against Bimas Islam tables.
        const double FajrAngle = 20.0, IshaAngle = 18.0, HorizonAngle = 1.0;

        /// <summary>Local times for the given local date. <paramref name="tzHours"/> is the UTC offset of that date.</summary>
        public static DateTime[] Compute(DateTime date, double lat, double lng, double tzHours, int ihtiyat)
        {
            double jd = Julian(date.Year, date.Month, date.Day) - lng / (15.0 * 24.0);
            double[] t = { 5, 6, 12, 13, 18, 18 }; // fajr, sunrise, dhuhr, asr, sunset, isha (hours)
            for (int iter = 0; iter < 2; iter++)
            {
                double[] p = new double[6];
                for (int i = 0; i < 6; i++) p[i] = t[i] / 24.0;
                t[0] = SunAngleTime(jd, FajrAngle, p[0], lat, true);
                t[1] = SunAngleTime(jd, HorizonAngle, p[1], lat, true);
                t[2] = MidDay(jd, p[2]);
                t[3] = AsrTime(jd, 1, p[3], lat);
                t[4] = SunAngleTime(jd, HorizonAngle, p[4], lat, false);
                t[5] = SunAngleTime(jd, IshaAngle, p[5], lat, false);
            }
            for (int i = 0; i < 6; i++) t[i] += tzHours - lng / 15.0;

            var day = date.Date;
            var result = new DateTime[7];
            result[Subuh] = CeilMinute(day, t[0] * 60 + ihtiyat);
            result[Terbit] = FloorMinute(day, t[1] * 60 - ihtiyat);
            result[Dzuhur] = CeilMinute(day, t[2] * 60 + ihtiyat + 1); // Kemenag adds an extra minute after the meridian transit
            result[Ashar] = CeilMinute(day, t[3] * 60 + ihtiyat);
            result[Maghrib] = CeilMinute(day, t[4] * 60 + ihtiyat);
            result[Isya] = CeilMinute(day, t[5] * 60 + ihtiyat);
            result[Imsak] = result[Subuh].AddMinutes(-10);
            return result;
        }

        static DateTime CeilMinute(DateTime day, double minutes) { return day.AddMinutes(Math.Ceiling(minutes - 1e-6)); }
        static DateTime FloorMinute(DateTime day, double minutes) { return day.AddMinutes(Math.Floor(minutes)); }

        static double Julian(int year, int month, int day)
        {
            if (month <= 2) { year -= 1; month += 12; }
            double a = Math.Floor(year / 100.0);
            double b = 2 - a + Math.Floor(a / 4.0);
            return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day + b - 1524.5;
        }

        static void SunPosition(double jd, out double decl, out double eqt)
        {
            double d = jd - 2451545.0;
            double g = FixAngle(357.529 + 0.98560028 * d);
            double q = FixAngle(280.459 + 0.98564736 * d);
            double l = FixAngle(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g));
            double e = 23.439 - 0.00000036 * d;
            double ra = ArcTan2(Cos(e) * Sin(l), Cos(l)) / 15.0;
            eqt = q / 15.0 - FixHour(ra);
            decl = ArcSin(Sin(e) * Sin(l));
        }

        static double MidDay(double jd, double time)
        {
            double decl, eqt;
            SunPosition(jd + time, out decl, out eqt);
            return FixHour(12 - eqt);
        }

        static double SunAngleTime(double jd, double angle, double time, double lat, bool beforeNoon)
        {
            double decl, eqt;
            SunPosition(jd + time, out decl, out eqt);
            double noon = MidDay(jd, time);
            double cos = (-Sin(angle) - Sin(decl) * Sin(lat)) / (Cos(decl) * Cos(lat));
            double t = ArcCos(Math.Max(-1, Math.Min(1, cos))) / 15.0;
            return noon + (beforeNoon ? -t : t);
        }

        static double AsrTime(double jd, double factor, double time, double lat)
        {
            double decl, eqt;
            SunPosition(jd + time, out decl, out eqt);
            double angle = -ArcCot(factor + Tan(Math.Abs(lat - decl)));
            return SunAngleTime(jd, angle, time, lat, false);
        }

        const double Rad = Math.PI / 180.0;
        static double Sin(double d) { return Math.Sin(d * Rad); }
        static double Cos(double d) { return Math.Cos(d * Rad); }
        static double Tan(double d) { return Math.Tan(d * Rad); }
        static double ArcSin(double x) { return Math.Asin(x) / Rad; }
        static double ArcCos(double x) { return Math.Acos(x) / Rad; }
        static double ArcTan2(double y, double x) { return Math.Atan2(y, x) / Rad; }
        static double ArcCot(double x) { return Math.Atan(1 / x) / Rad; }
        static double FixAngle(double a) { a = a - 360.0 * Math.Floor(a / 360.0); return a < 0 ? a + 360.0 : a; }
        static double FixHour(double h) { h = h - 24.0 * Math.Floor(h / 24.0); return h < 0 ? h + 24.0 : h; }
    }

    /// <summary>Today's schedule plus the logic to find the next adzan.</summary>
    class PrayerSchedule
    {
        public DateTime Day;
        public DateTime[] Times;
        public DateTime[] Tomorrow;
        public string City;

        public static PrayerSchedule For(DateTime localNow, double lat, double lon, string city, int ihtiyat)
        {
            var s = new PrayerSchedule();
            s.Day = localNow.Date;
            s.City = city;
            s.Times = PrayerCalc.Compute(s.Day, lat, lon, Offset(s.Day), ihtiyat);
            var next = s.Day.AddDays(1);
            s.Tomorrow = PrayerCalc.Compute(next, lat, lon, Offset(next), ihtiyat);
            return s;
        }

        static double Offset(DateTime day) { return TimeZoneInfo.Local.GetUtcOffset(day.AddHours(12)).TotalHours; }

        /// <summary>The next adzan at or after <paramref name="localNow"/> (index into PrayerCalc.Names).</summary>
        public void Next(DateTime localNow, out int index, out DateTime time)
        {
            foreach (int i in PrayerCalc.Adzan)
            {
                if (Times[i] > localNow) { index = i; time = Times[i]; return; }
            }
            index = PrayerCalc.Subuh;
            time = Tomorrow[PrayerCalc.Subuh];
        }
    }

    /// <summary>Finds approximate coordinates from the public IP (city level), once; the result is cached in settings.</summary>
    static class LocationService
    {
        public class Result { public double Lat, Lon; public string City; }

        public static Result Detect()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)(3072 | 12288); } catch { } // TLS 1.2 + 1.3 (BMKG accepts only 1.3)
            var r = Fetch("https://ipwho.is/?fields=success,city,latitude,longitude", "latitude", "longitude", "city");
            if (r == null) r = Fetch("http://ip-api.com/json/?fields=status,city,lat,lon", "lat", "lon", "city");
            return r;
        }

        static Result Fetch(string url, string latKey, string lonKey, string cityKey)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "Gili/1.0";
                string json;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();
                double lat, lon;
                if (!Number(json, latKey, out lat) || !Number(json, lonKey, out lon)) return null;
                return new Result { Lat = lat, Lon = lon, City = Text(json, cityKey) ?? "" };
            }
            catch (Exception ex)
            {
                Log.Write("location " + url + ": " + ex.Message);
                return null;
            }
        }

        static bool Number(string json, string key, out double value)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)");
            value = 0;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static string Text(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }
    }
}
