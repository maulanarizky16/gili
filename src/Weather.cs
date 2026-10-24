using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Gili
{
    class WeatherEntry
    {
        public DateTime Local;
        public double TempC;
        public int Humidity, Code;
        public string Desc;
        public bool IsRain { get { return Code >= 60; } }
    }

    class WeatherReport
    {
        public string Place = "", Adm4 = "";
        public List<WeatherEntry> Entries = new List<WeatherEntry>();
        public DateTime FetchedUtc;

        /// <summary>The 3-hour slot covering <paramref name="localNow"/> (or the nearest one).</summary>
        public WeatherEntry Current(DateTime localNow)
        {
            WeatherEntry best = null;
            double bestDist = double.MaxValue;
            foreach (var e in Entries)
            {
                double d = (localNow - e.Local).TotalHours;
                double dist = d >= 0 && d < 3 ? 0 : Math.Abs(d);
                if (dist < bestDist) { bestDist = dist; best = e; }
            }
            return best;
        }

        /// <summary>First rainy slot starting within the next <paramref name="hours"/> hours.</summary>
        public WeatherEntry NextRain(DateTime localNow, double hours)
        {
            foreach (var e in Entries)
                if (e.IsRain && e.Local > localNow.AddHours(-1.5) && e.Local <= localNow.AddHours(hours)) return e;
            return null;
        }
    }

    /// <summary>
    /// Weather from BMKG (api.bmkg.go.id), which needs a village code (adm4). The code is found once from the
    /// coordinates: OpenStreetMap reverse geocoding gives the names, wilayah.id maps names to official codes.
    /// </summary>
    static class WeatherService
    {
        const string UserAgent = "Gili/1.0 (Windows dynamic island)";

        public static WeatherReport Fetch(string adm4)
        {
            var root = Json("https://api.bmkg.go.id/publik/prakiraan-cuaca?adm4=" + Uri.EscapeDataString(adm4)) as Dictionary<string, object>;
            if (root == null) return null;
            var report = new WeatherReport { Adm4 = adm4, FetchedUtc = DateTime.UtcNow };
            var lokasi = root.ContainsKey("lokasi") ? root["lokasi"] as Dictionary<string, object> : null;
            if (lokasi != null) report.Place = Str(lokasi, "desa");
            var data = root.ContainsKey("data") ? root["data"] as object[] : null;
            if (data == null || data.Length == 0) return null;
            var first = data[0] as Dictionary<string, object>;
            var days = first != null && first.ContainsKey("cuaca") ? first["cuaca"] as object[] : null;
            if (days == null) return null;
            foreach (var day in days)
            {
                var slots = day as object[];
                if (slots == null) continue;
                foreach (var o in slots)
                {
                    var s = o as Dictionary<string, object>;
                    if (s == null) continue;
                    DateTime local;
                    if (!DateTime.TryParseExact(Str(s, "local_datetime"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out local)) continue;
                    report.Entries.Add(new WeatherEntry
                    {
                        Local = local,
                        TempC = Num(s, "t"),
                        Humidity = (int)Num(s, "hu"),
                        Code = (int)Num(s, "weather"),
                        Desc = Str(s, "weather_desc")
                    });
                }
            }
            report.Entries.Sort((a, b) => a.Local.CompareTo(b.Local));
            return report.Entries.Count > 0 ? report : null;
        }

        /// <summary>Finds the BMKG village code nearest to the coordinates. Returns null when it cannot.</summary>
        public static string ResolveAdm4(double lat, double lon)
        {
            var reverse = Json(string.Format(CultureInfo.InvariantCulture,
                "https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={0}&lon={1}&zoom=14&accept-language=id", lat, lon))
                as Dictionary<string, object>;
            var address = reverse != null && reverse.ContainsKey("address") ? reverse["address"] as Dictionary<string, object> : null;
            if (address == null) return null;

            string province = Pick(List("https://wilayah.id/api/provinces.json"), Str(address, "state"));
            if (province == null) return null;

            var regencies = List("https://wilayah.id/api/regencies/" + province + ".json");
            string city = Str(address, "city"), county = First(address, "county", "regency", "state_district");
            string regency = city.Length > 0 ? Pick(regencies, "Kota " + city) ?? Pick(regencies, city) : null;
            if (regency == null && county.Length > 0) regency = Pick(regencies, "Kabupaten " + county) ?? Pick(regencies, county);
            if (regency == null && city.Length > 0) regency = Pick(regencies, "Kabupaten " + city);
            if (regency == null) return null;

            var districts = List("https://wilayah.id/api/districts/" + regency + ".json");
            if (districts.Count == 0) return null;
            string district = Pick(districts, First(address, "city_district", "district", "municipality", "suburb", "town"));
            if (district == null) district = districts[0].Key;

            var villages = List("https://wilayah.id/api/villages/" + district + ".json");
            if (villages.Count == 0) return null;
            string village = Pick(villages, First(address, "village", "suburb", "quarter", "neighbourhood", "hamlet"));
            return village ?? villages[0].Key;
        }

        static string First(Dictionary<string, object> d, params string[] keys)
        {
            foreach (var k in keys) { string v = Str(d, k); if (v.Length > 0) return v; }
            return "";
        }

        static List<KeyValuePair<string, string>> List(string url)
        {
            var result = new List<KeyValuePair<string, string>>();
            var root = Json(url) as Dictionary<string, object>;
            var data = root != null && root.ContainsKey("data") ? root["data"] as object[] : null;
            if (data == null) return result;
            foreach (var o in data)
            {
                var d = o as Dictionary<string, object>;
                if (d != null) result.Add(new KeyValuePair<string, string>(Str(d, "code"), Str(d, "name")));
            }
            return result;
        }

        /// <summary>Code of the entry whose name matches (exact after normalising, else containment).</summary>
        static string Pick(List<KeyValuePair<string, string>> items, string name)
        {
            string n = Norm(name);
            if (n.Length == 0) return null;
            foreach (var kv in items) if (Norm(kv.Value) == n) return kv.Key;
            foreach (var kv in items)
            {
                string v = Norm(kv.Value);
                if (v.Length > 3 && (v.Contains(n) || n.Contains(v))) return kv.Key;
            }
            return null;
        }

        static string Norm(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            s = Regex.Replace(s, @"^(provinsi|daerah istimewa|daerah khusus ibukota|dki|kota administrasi|kabupaten administrasi|kecamatan|kelurahan|desa)\s+", "");
            return Regex.Replace(s, "[^a-z0-9]", "");
        }

        static object Json(string url)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)(3072 | 12288); } catch { } // TLS 1.2 + 1.3 (BMKG accepts only 1.3)
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 12000;
            req.ReadWriteTimeout = 12000;
            req.UserAgent = UserAgent;
            req.AllowAutoRedirect = true;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(sr.ReadToEnd());
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }
    }

    static class Hijri
    {
        static readonly string[] Months = { "Muharram", "Safar", "Rabiulawal", "Rabiulakhir", "Jumadilawal", "Jumadilakhir",
                                            "Rajab", "Syakban", "Ramadan", "Syawal", "Zulkaidah", "Zulhijah" };
        static readonly UmAlQuraCalendar Calendar = new UmAlQuraCalendar();

        /// <summary>Hijri date; <paramref name="adjust"/> shifts by whole days to follow the Kemenag (isbat) decision.</summary>
        public static void Of(DateTime date, int adjust, out int day, out int month, out int year)
        {
            var d = date.Date.AddDays(adjust);
            day = Calendar.GetDayOfMonth(d);
            month = Calendar.GetMonth(d);
            year = Calendar.GetYear(d);
        }

        public static string Format(DateTime date, int adjust)
        {
            int d, m, y;
            Of(date, adjust, out d, out m, out y);
            return d + " " + Months[m - 1] + " " + y + " H";
        }

        public static bool IsRamadan(DateTime date, int adjust, out int day)
        {
            int m, y;
            Of(date, adjust, out day, out m, out y);
            return m == 9;
        }
    }

    static class WeatherArt
    {
        static readonly Color SunColor = Color.FromArgb(255, 255, 196, 61);
        static readonly Color RainColor = Color.FromArgb(255, 96, 176, 255);

        /// <summary>Small vector weather icon for a BMKG weather code, centred on (cx, cy).</summary>
        public static void Draw(Painter p, int code, float cx, float cy, float size, bool night)
        {
            float s = size / 20f;
            bool partly = code == 1 || code == 2;
            bool cloudy = code == 3 || code == 4 || code >= 60;
            bool rain = code >= 60 && code < 95, storm = code >= 95, fog = code == 5 || code == 10 || code == 45;

            if (fog)
            {
                for (int i = 0; i < 3; i++)
                    p.G.DrawLine(p.P(Theme.Sub, 1.8f * s), cx - (8 - i * 2) * s, cy + (i * 5 - 5) * s, cx + (8 - i) * s, cy + (i * 5 - 5) * s);
                return;
            }
            if (!cloudy || partly)
            {
                float ox = partly ? cx - 3.5f * s : cx, oy = partly ? cy - 3.5f * s : cy, r = partly ? 4.5f * s : 5.5f * s;
                if (night) DrawMoon(p, ox, oy, r * 1.25f);
                else
                {
                    p.FillCircle(ox, oy, r, SunColor);
                    if (!partly)
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            p.G.DrawLine(p.P(SunColor, 1.6f * s), ox + (float)Math.Cos(a) * r * 1.45f, oy + (float)Math.Sin(a) * r * 1.45f,
                                         ox + (float)Math.Cos(a) * r * 1.85f, oy + (float)Math.Sin(a) * r * 1.85f);
                        }
                }
                if (!partly) return;
            }
            float cyCloud = rain || storm ? cy - 2.5f * s : cy + (partly ? 2 * s : 0);
            DrawCloud(p, cx + (partly ? 2 * s : 0), cyCloud, s);
            if (rain)
                for (int i = 0; i < 3; i++)
                {
                    float x = cx + (i - 1) * 4.5f * s;
                    p.G.DrawLine(p.P(RainColor, 1.6f * s), x + 1 * s, cy + 5 * s, x - 1 * s, cy + 9 * s);
                }
            if (storm)
            {
                using (var bolt = new GraphicsPath())
                {
                    bolt.AddPolygon(new[] {
                        new PointF(cx + 1 * s, cy + 2 * s), new PointF(cx - 2.5f * s, cy + 7.5f * s), new PointF(cx, cy + 7.5f * s),
                        new PointF(cx - 1.5f * s, cy + 11 * s), new PointF(cx + 3 * s, cy + 5.5f * s), new PointF(cx + 0.5f * s, cy + 5.5f * s) });
                    p.G.FillPath(p.B(SunColor), bolt);
                }
            }
        }

        static void DrawCloud(Painter p, float cx, float cy, float s)
        {
            using (var path = new GraphicsPath(FillMode.Winding))
            {
                path.AddEllipse(cx - 8 * s, cy - 1 * s, 8 * s, 7 * s);
                path.AddEllipse(cx - 4.5f * s, cy - 6 * s, 9.5f * s, 9.5f * s);
                path.AddEllipse(cx + 1 * s, cy - 2.5f * s, 7.5f * s, 8.5f * s);
                path.AddRectangle(new RectangleF(cx - 4 * s, cy + 1.5f * s, 8.5f * s, 4.5f * s));
                p.G.FillPath(p.B(Theme.WithAlpha(Theme.Text, 0.9f)), path);
            }
        }

        static void DrawMoon(Painter p, float cx, float cy, float r)
        {
            using (var moon = new GraphicsPath())
            using (var bite = new GraphicsPath())
            {
                moon.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                bite.AddEllipse(cx - r * 0.35f, cy - r * 1.25f, r * 2, r * 2);
                using (var region = new Region(moon))
                {
                    region.Exclude(bite);
                    p.G.FillRegion(p.B(Color.FromArgb(255, 255, 226, 140)), region);
                }
            }
        }
    }
}
