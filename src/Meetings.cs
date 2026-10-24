using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Gili
{
    /// <summary>A meeting added in Gili. Repeat: "none", "daily", "weekdays", "weekly".</summary>
    class Meeting
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public DateTime Start;
        public int DurationMin = 30;
        public string Link = "";
        public string Repeat = "none";
        public int RemindMin = 5;

        public static readonly string[] RepeatKeys = { "none", "daily", "weekdays", "weekly" };
        public static readonly string[] RepeatLabels = { "Tidak diulang", "Setiap hari", "Hari kerja (Sen–Jum)", "Setiap minggu" };

        /// <summary>Occurrences that overlap [from, to).</summary>
        public IEnumerable<CalEvent> Occurrences(DateTime from, DateTime to)
        {
            var length = TimeSpan.FromMinutes(Math.Max(5, DurationMin));
            if (Repeat == "none")
            {
                if (Start + length > from && Start < to) yield return Make(Start, length);
                yield break;
            }
            // Walk day by day from whichever is later: the first meeting or a day before the window.
            var day = (Start.Date > from.Date.AddDays(-1) ? Start.Date : from.Date.AddDays(-1));
            for (; day < to; day = day.AddDays(1))
            {
                var s = day + Start.TimeOfDay;
                if (s < Start) continue;
                bool ok = Repeat == "daily" ||
                          (Repeat == "weekdays" && day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday) ||
                          (Repeat == "weekly" && day.DayOfWeek == Start.DayOfWeek);
                if (ok && s + length > from && s < to) yield return Make(s, length);
            }
        }

        CalEvent Make(DateTime s, TimeSpan length)
        {
            return new CalEvent
            {
                Uid = Id, Title = Title.Length > 0 ? Title : "Rapat", Start = s, End = s + length,
                Link = string.IsNullOrEmpty(Link) ? null : Link.Trim(), RemindMin = RemindMin, Source = this
            };
        }
    }

    /// <summary>Meetings are kept in %APPDATA%\Gili\meetings.json.</summary>
    static class MeetingStore
    {
        static string FilePath { get { return Path.Combine(Settings.Dir, "meetings.json"); } }

        public static List<Meeting> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<Meeting>();
                var list = new JavaScriptSerializer().Deserialize<List<Meeting>>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (list == null) return new List<Meeting>();
                // JavaScriptSerializer stores DateTime as UTC ticks; bring them back to local time.
                foreach (var m in list) if (m.Start.Kind == DateTimeKind.Utc) m.Start = m.Start.ToLocalTime();
                return list;
            }
            catch (Exception ex)
            {
                Log.Write("meetings load: " + ex.Message);
                return new List<Meeting>();
            }
        }

        public static void Save(List<Meeting> meetings)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(meetings), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("meetings save: " + ex.Message); }
        }
    }
}
