using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gili
{
    class CalEvent
    {
        public string Uid = "", Title = "", Location = "", Link, Description = "";
        public DateTime Start, End;   // local time
        public bool AllDay;
        public int RemindMin = 5;     // alert this many minutes before the start
        public Meeting Source;        // set for meetings added in Gili (null for iCal events)
    }

    /// <summary>
    /// Minimal iCalendar (RFC 5545) reader: enough for Google / Outlook "secret iCal address" feeds. Handles line
    /// folding, time zones (VTIMEZONE or common IANA names), all-day events, and recurring events (DAILY / WEEKLY /
    /// MONTHLY / YEARLY with INTERVAL, COUNT, UNTIL, BYDAY, BYMONTHDAY, EXDATE and moved instances).
    /// </summary>
    static class Ics
    {
        static readonly Regex MeetingLink = new Regex(
            @"https://(?:meet\.google\.com|[\w.-]*zoom\.us|teams\.microsoft\.com|teams\.live\.com|[\w.-]*webex\.com)/[^\s""<>\\]+",
            RegexOptions.IgnoreCase);

        static readonly Dictionary<string, int> KnownOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Asia/Jakarta", 420 }, { "Asia/Pontianak", 420 }, { "Asia/Makassar", 480 }, { "Asia/Jayapura", 540 },
            { "Asia/Singapore", 480 }, { "Asia/Kuala_Lumpur", 480 }, { "Asia/Bangkok", 420 }, { "Asia/Tokyo", 540 },
            { "UTC", 0 }, { "Etc/UTC", 0 }, { "GMT", 0 }
        };

        class Raw
        {
            public Dictionary<string, string> Props = new Dictionary<string, string>();
            public Dictionary<string, string> Params = new Dictionary<string, string>();
            public List<string> ExDates = new List<string>();
            public List<string> ExDateParams = new List<string>();
        }

        public static List<CalEvent> Parse(string text, DateTime fromLocal, DateTime toLocal)
        {
            var lines = Unfold(text);
            var tzOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var raws = new List<Raw>();
            Raw cur = null;
            string tzid = null;
            bool inStandard = false;
            foreach (var line in lines)
            {
                if (line == "BEGIN:VEVENT") { cur = new Raw(); continue; }
                if (line == "END:VEVENT") { if (cur != null) raws.Add(cur); cur = null; continue; }
                if (line == "BEGIN:VTIMEZONE") { tzid = null; continue; }
                if (line == "BEGIN:STANDARD") { inStandard = true; continue; }
                if (line == "END:STANDARD") { inStandard = false; continue; }
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string head = line.Substring(0, colon), value = line.Substring(colon + 1);
                string name = head, prms = "";
                int semi = head.IndexOf(';');
                if (semi > 0) { name = head.Substring(0, semi); prms = head.Substring(semi + 1); }
                name = name.ToUpperInvariant();
                if (cur == null)
                {
                    if (name == "TZID") tzid = value.Trim();
                    else if (inStandard && name == "TZOFFSETTO" && tzid != null) tzOffsets[tzid] = ParseOffset(value);
                    continue;
                }
                if (name == "EXDATE") { foreach (var v in value.Split(',')) { cur.ExDates.Add(v); cur.ExDateParams.Add(prms); } continue; }
                if (!cur.Props.ContainsKey(name)) { cur.Props[name] = value; cur.Params[name] = prms; }
            }

            // Moved / edited instances of a series: UID + original start (RECURRENCE-ID).
            var overrides = new HashSet<string>();
            foreach (var r in raws)
                if (r.Props.ContainsKey("RECURRENCE-ID"))
                {
                    bool d; DateTime rid = ParseDate(r.Props["RECURRENCE-ID"], r.Params["RECURRENCE-ID"], tzOffsets, out d);
                    overrides.Add(Get(r, "UID") + "|" + rid.ToString("yyyyMMddHHmm"));
                }

            var result = new List<CalEvent>();
            foreach (var r in raws)
            {
                if (!r.Props.ContainsKey("DTSTART")) continue;
                if (Get(r, "STATUS").Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
                bool allDay;
                DateTime start = ParseDate(r.Props["DTSTART"], r.Params["DTSTART"], tzOffsets, out allDay);
                DateTime end;
                bool endAllDay;
                if (r.Props.ContainsKey("DTEND")) end = ParseDate(r.Props["DTEND"], r.Params["DTEND"], tzOffsets, out endAllDay);
                else if (r.Props.ContainsKey("DURATION")) end = start + ParseDuration(r.Props["DURATION"]);
                else end = allDay ? start.AddDays(1) : start.AddHours(1);
                if (end <= start) end = start.AddMinutes(allDay ? 1440 : 30);
                var length = end - start;

                var template = new CalEvent
                {
                    Uid = Get(r, "UID"),
                    Title = Unescape(Get(r, "SUMMARY")),
                    Location = Unescape(Get(r, "LOCATION")),
                    Description = Unescape(Get(r, "DESCRIPTION")),
                    AllDay = allDay
                };
                foreach (var field in new[] { "X-GOOGLE-CONFERENCE", "LOCATION", "URL", "DESCRIPTION" })
                {
                    var m = MeetingLink.Match(Unescape(Get(r, field)));
                    if (m.Success) { template.Link = m.Value.TrimEnd('.', ',', ')'); break; }
                }
                if (template.Title.Length == 0) template.Title = "(Tanpa judul)";

                var excluded = new HashSet<string>();
                for (int i = 0; i < r.ExDates.Count; i++)
                {
                    bool d; excluded.Add(ParseDate(r.ExDates[i], r.ExDateParams[i], tzOffsets, out d).ToString("yyyyMMddHHmm"));
                }

                bool isOverride = r.Props.ContainsKey("RECURRENCE-ID");
                IEnumerable<DateTime> starts = !isOverride && r.Props.ContainsKey("RRULE")
                    ? Expand(start, r.Props["RRULE"], tzOffsets, toLocal)
                    : new[] { start };
                foreach (var s in starts)
                {
                    var key = s.ToString("yyyyMMddHHmm");
                    if (excluded.Contains(key)) continue;
                    if (!isOverride && overrides.Contains(template.Uid + "|" + key)) continue;
                    var e = s + length;
                    if (e <= fromLocal || s >= toLocal) continue;
                    result.Add(new CalEvent
                    {
                        Uid = template.Uid, Title = template.Title, Location = template.Location, Description = template.Description,
                        Link = template.Link, AllDay = template.AllDay, Start = s, End = e
                    });
                }
            }
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        static IEnumerable<DateTime> Expand(DateTime start, string rule, Dictionary<string, int> tz, DateTime until)
        {
            var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in rule.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) p[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            string freq = p.ContainsKey("FREQ") ? p["FREQ"].ToUpperInvariant() : "";
            int interval = p.ContainsKey("INTERVAL") ? Math.Max(1, int.Parse(p["INTERVAL"], CultureInfo.InvariantCulture)) : 1;
            int count = p.ContainsKey("COUNT") ? int.Parse(p["COUNT"], CultureInfo.InvariantCulture) : int.MaxValue;
            DateTime last = until;
            if (p.ContainsKey("UNTIL")) { bool d; var u = ParseDate(p["UNTIL"], "", tz, out d); if (d) u = u.AddDays(1).AddSeconds(-1); if (u < last) last = u; }
            var byDay = p.ContainsKey("BYDAY") ? p["BYDAY"].Split(',') : new string[0];
            var byMonthDay = new List<int>();
            if (p.ContainsKey("BYMONTHDAY")) foreach (var s in p["BYMONTHDAY"].Split(',')) byMonthDay.Add(int.Parse(s, CultureInfo.InvariantCulture));

            int produced = 0, guard = 0;
            var time = start.TimeOfDay;
            switch (freq)
            {
                case "DAILY":
                    for (var d = start; d <= last && produced < count && guard++ < 20000; d = d.AddDays(interval))
                    {
                        if (byDay.Length > 0 && !HasWeekday(byDay, d.DayOfWeek)) continue;
                        produced++;
                        yield return d;
                    }
                    break;
                case "WEEKLY":
                {
                    var days = new List<DayOfWeek>();
                    foreach (var bd in byDay) { DayOfWeek w; if (TryWeekday(bd, out w)) days.Add(w); }
                    if (days.Count == 0) days.Add(start.DayOfWeek);
                    var weekStart = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7)); // Monday
                    for (var w = weekStart; w <= last && produced < count && guard++ < 5000; w = w.AddDays(7 * interval))
                    {
                        var inWeek = new List<DateTime>();
                        foreach (var day in days) inWeek.Add(w.AddDays(((int)day + 6) % 7) + time);
                        inWeek.Sort();
                        foreach (var d in inWeek)
                        {
                            if (d < start || d > last || produced >= count) continue;
                            produced++;
                            yield return d;
                        }
                    }
                    break;
                }
                case "MONTHLY":
                    for (var m = new DateTime(start.Year, start.Month, 1); m <= last && produced < count && guard++ < 2000; m = m.AddMonths(interval))
                    {
                        var candidates = new List<DateTime>();
                        if (byMonthDay.Count > 0)
                        {
                            foreach (int md in byMonthDay)
                            {
                                int dim = DateTime.DaysInMonth(m.Year, m.Month);
                                int day = md > 0 ? md : dim + md + 1;
                                if (day >= 1 && day <= dim) candidates.Add(m.AddDays(day - 1) + time);
                            }
                        }
                        else if (byDay.Length > 0)
                        {
                            foreach (var bd in byDay)
                            {
                                DayOfWeek w; int n;
                                if (!TryWeekday(bd, out w)) continue;
                                n = OrdinalOf(bd);
                                var d = NthWeekday(m, w, n == 0 ? 1 : n);
                                if (d.HasValue) candidates.Add(d.Value + time);
                            }
                        }
                        else if (start.Day <= DateTime.DaysInMonth(m.Year, m.Month)) candidates.Add(m.AddDays(start.Day - 1) + time);
                        candidates.Sort();
                        foreach (var d in candidates)
                        {
                            if (d < start || d > last || produced >= count) continue;
                            produced++;
                            yield return d;
                        }
                    }
                    break;
                case "YEARLY":
                    for (int y = start.Year; y <= last.Year && produced < count && guard++ < 200; y += interval)
                    {
                        if (start.Month == 2 && start.Day == 29 && !DateTime.IsLeapYear(y)) continue;
                        var d = new DateTime(y, start.Month, start.Day) + time;
                        if (d < start || d > last) continue;
                        produced++;
                        yield return d;
                    }
                    break;
                default:
                    yield return start;
                    break;
            }
        }

        static bool HasWeekday(string[] byDay, DayOfWeek day)
        {
            foreach (var bd in byDay) { DayOfWeek w; if (TryWeekday(bd, out w) && w == day) return true; }
            return false;
        }

        static bool TryWeekday(string token, out DayOfWeek day)
        {
            string t = Regex.Replace(token.Trim().ToUpperInvariant(), @"^[+-]?\d+", "");
            switch (t)
            {
                case "MO": day = DayOfWeek.Monday; return true;
                case "TU": day = DayOfWeek.Tuesday; return true;
                case "WE": day = DayOfWeek.Wednesday; return true;
                case "TH": day = DayOfWeek.Thursday; return true;
                case "FR": day = DayOfWeek.Friday; return true;
                case "SA": day = DayOfWeek.Saturday; return true;
                case "SU": day = DayOfWeek.Sunday; return true;
            }
            day = DayOfWeek.Monday;
            return false;
        }

        static int OrdinalOf(string token)
        {
            var m = Regex.Match(token.Trim(), @"^([+-]?\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        static DateTime? NthWeekday(DateTime month, DayOfWeek day, int n)
        {
            if (n > 0)
            {
                var d = month;
                while (d.DayOfWeek != day) d = d.AddDays(1);
                d = d.AddDays(7 * (n - 1));
                return d.Month == month.Month ? d : (DateTime?)null;
            }
            var e = month.AddMonths(1).AddDays(-1);
            while (e.DayOfWeek != day) e = e.AddDays(-1);
            e = e.AddDays(7 * (n + 1));
            return e.Month == month.Month ? e : (DateTime?)null;
        }

        static string Get(Raw r, string key)
        {
            string v;
            return r.Props.TryGetValue(key, out v) ? v : "";
        }

        static List<string> Unfold(string text)
        {
            var result = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if ((raw.StartsWith(" ") || raw.StartsWith("\t")) && result.Count > 0) result[result.Count - 1] += raw.Substring(1);
                else if (raw.Length > 0) result.Add(raw);
            }
            return result;
        }

        static string Unescape(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
        }

        static int ParseOffset(string v)
        {
            var m = Regex.Match(v.Trim(), @"^([+-])(\d\d)(\d\d)");
            if (!m.Success) return 0;
            int minutes = int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value);
            return m.Groups[1].Value == "-" ? -minutes : minutes;
        }

        static TimeSpan ParseDuration(string v)
        {
            var m = Regex.Match(v, @"P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?");
            if (!m.Success) return TimeSpan.FromHours(1);
            Func<int, int> n = i => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
            return new TimeSpan(n(1) * 7 + n(2), n(3), n(4), n(5));
        }

        /// <summary>Converts an iCal date / date-time to local time. <paramref name="allDay"/> is set for VALUE=DATE.</summary>
        static DateTime ParseDate(string value, string prms, Dictionary<string, int> tz, out bool allDay)
        {
            value = value.Trim();
            allDay = value.Length == 8;
            if (allDay)
                return DateTime.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture);
            bool utc = value.EndsWith("Z");
            var dt = DateTime.ParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            if (utc) return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
            var m = Regex.Match(prms ?? "", @"TZID=""?([^;:""]+)""?", RegexOptions.IgnoreCase);
            if (!m.Success) return dt; // floating time: already local
            string id = m.Groups[1].Value;
            int offset;
            if (tz.TryGetValue(id, out offset) || KnownOffsets.TryGetValue(id, out offset))
                return DateTime.SpecifyKind(dt.AddMinutes(-offset), DateTimeKind.Utc).ToLocalTime();
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return TimeZoneInfo.ConvertTime(dt, zone, TimeZoneInfo.Local);
            }
            catch { return dt; }
        }
    }

    /// <summary>Fetches the user's iCal feeds (optional: Google / Outlook "secret iCal address").</summary>
    static class CalendarFeed
    {
        public static string Download(string url)
        {
            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)(3072 | 12288); } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.UserAgent = "Gili/1.0 (Windows dynamic island)";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        public static List<CalEvent> Events(string[] urls, DateTime fromLocal, DateTime toLocal)
        {
            var all = new List<CalEvent>();
            foreach (var u in urls)
            {
                if (u.Trim().Length == 0) continue;
                try { all.AddRange(Ics.Parse(Download(u.Trim()), fromLocal, toLocal)); }
                catch (Exception ex) { Log.Write("calendar: " + ex.Message); }
            }
            all.Sort((a, b) => a.Start.CompareTo(b.Start));
            return all;
        }

        public static bool LooksLikeIcs(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            return (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) &&
                   (s.IndexOf(".ics", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("/ical/", StringComparison.OrdinalIgnoreCase) >= 0) &&
                   !s.Contains(" ");
        }
    }
}
