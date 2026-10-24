using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Gili
{
    /// <summary>
    /// Meeting schedule: meetings added in Gili (plus an optional iCal feed), a countdown on the island in the last
    /// 15 minutes, an alert with a "Gabung" button before the start and another when it begins, and the Agenda tab.
    /// </summary>
    partial class IslandForm
    {
        static readonly Color MeetColor = Color.FromArgb(255, 110, 150, 255);
        List<Meeting> meetings = MeetingStore.Load();
        List<CalEvent> icalEvents = new List<CalEvent>();
        List<CalEvent> agenda = new List<CalEvent>();
        DateTime nextIcalUtc = DateTime.MinValue;
        bool icalBusy;
        readonly HashSet<string> meetingAlerts = new HashSet<string>();
        readonly Spring agendaScroll = new Spring(0, 320, 32, 0.2f);
        bool meetingDialogOpen;

        bool MeetingAlertsOn { get { return settings.GetBool("meeting.notify", true); } }

        /// <summary>Rebuilds the merged agenda for today and the next week.</summary>
        void RefreshAgenda()
        {
            var from = DateTime.Today;
            var to = from.AddDays(8);
            var list = new List<CalEvent>();
            foreach (var m in meetings) list.AddRange(m.Occurrences(from, to));
            foreach (var e in icalEvents) if (!e.AllDay && e.End > from && e.Start < to) list.Add(e);
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            agenda = list;
        }

        void MaybeRefreshIcal()
        {
            string urls = settings.Get("calendar.ics", "");
            if (urls.Length == 0 || icalBusy || DateTime.UtcNow < nextIcalUtc) return;
            icalBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var events = CalendarFeed.Events(urls.Split('|'), DateTime.Today, DateTime.Today.AddDays(8));
                Post(() =>
                {
                    icalBusy = false;
                    icalEvents = events;
                    nextIcalUtc = DateTime.UtcNow.AddMinutes(10);
                    RefreshAgenda();
                    Kick();
                });
            });
        }

        /// <summary>The meeting to count down to: starting within 15 minutes (or in progress for its first 5).</summary>
        CalEvent SoonMeeting()
        {
            if (!MeetingAlertsOn) return null;
            var now = DateTime.Now;
            foreach (var e in agenda)
            {
                if (e.Start > now.AddMinutes(15)) break;
                if (e.Start > now || now < e.Start.AddMinutes(5)) return e;
            }
            return null;
        }

        CalEvent OngoingOrNext()
        {
            var now = DateTime.Now;
            foreach (var e in agenda) if (e.End > now) return e;
            return null;
        }

        /// <summary>Called every second: the reminder before the start and the "it's starting" alert.</summary>
        void CheckMeetings()
        {
            if (!MeetingAlertsOn) return;
            var now = DateTime.Now;
            foreach (var e in agenda)
            {
                if (e.Start > now.AddMinutes(e.RemindMin + 1)) break;
                string key = e.Uid + "|" + e.Start.ToString("yyyyMMddHHmm");
                var remindAt = e.Start.AddMinutes(-e.RemindMin);
                if (e.RemindMin > 0 && now >= remindAt && now < e.Start && !meetingAlerts.Contains(key + "|soon"))
                {
                    meetingAlerts.Add(key + "|soon");
                    ShowMeetingAlert(e, false);
                }
                else if (now >= e.Start && now < e.Start.AddMinutes(2) && !meetingAlerts.Contains(key + "|start"))
                {
                    meetingAlerts.Add(key + "|start");
                    meetingAlerts.Add(key + "|soon");
                    ShowMeetingAlert(e, true);
                }
            }
        }

        void ShowMeetingAlert(CalEvent e, bool started)
        {
            var t = ShowToast(View.Meeting, started ? 45 : 30, 3, true);
            if (t == null) return;
            t.Data = e;
            t.Flag = started;
            Chime.Meeting();
            Pulse(MeetColor, 3);
        }

        void JoinMeeting(CalEvent e)
        {
            if (e == null || string.IsNullOrEmpty(e.Link)) return;
            try { Process.Start(e.Link); }
            catch (Exception ex) { Log.Write("join meeting: " + ex.Message); }
            DismissToast();
        }

        /// <summary>Opens the add / edit form. The island keeps running; the agenda refreshes when it closes.</summary>
        void OpenMeetingDialog(Meeting existing)
        {
            if (meetingDialogOpen) return;
            meetingDialogOpen = true;
            expanded = false;
            Kick();
            BeginInvoke(new Action(() =>
            {
                try
                {
                    using (var dlg = new MeetingDialog(existing))
                    {
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        if (dlg.Deleted) meetings.RemoveAll(m => m.Id == dlg.Result.Id);
                        else if (existing == null) meetings.Add(dlg.Result);
                        MeetingStore.Save(meetings);
                        RefreshAgenda();
                        if (!dlg.Deleted)
                        {
                            var r = dlg.Result;
                            ShowInfo(Theme.IconTimer, MeetColor, existing == null ? "Rapat ditambahkan" : "Rapat diperbarui",
                                     r.Start.ToString("ddd HH:mm").Replace(':', '.'), true);
                        }
                    }
                }
                catch (Exception ex) { Log.Write("meeting dialog: " + ex.Message); }
                finally { meetingDialogOpen = false; Kick(); }
            }));
        }

        void LinkIcalFromClipboard()
        {
            string text = "";
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText().Trim(); } catch { }
            if (!CalendarFeed.LooksLikeIcs(text))
            {
                ShowInfo(Theme.IconPaste, Theme.Orange, "Salin dulu link iCal", "Google Calendar › Setelan", true);
                return;
            }
            settings.Set("calendar.ics", text);
            settings.Save();
            nextIcalUtc = DateTime.MinValue;
            MaybeRefreshIcal();
            ShowInfo(Theme.IconTimer, MeetColor, "Google Calendar tersambung", "", true);
        }

        static string MeetTime(DateTime t) { return t.ToString("HH':'mm", System.Globalization.CultureInfo.InvariantCulture); }

        static string DayLabel(DateTime d)
        {
            if (d.Date == DateTime.Today) return "Hari ini";
            if (d.Date == DateTime.Today.AddDays(1)) return "Besok";
            return DayNames[(int)d.DayOfWeek] + ", " + d.Day + " " + MonthNames[d.Month - 1];
        }

        static string Until(TimeSpan left)
        {
            if (left.TotalSeconds <= 0) return "sedang berlangsung";
            int minutes = (int)Math.Ceiling(left.TotalMinutes);
            if (minutes < 60) return "dalam " + minutes + " menit";
            if (minutes < 24 * 60) return "dalam " + (minutes / 60) + " j " + (minutes % 60) + " m";
            return "";
        }

        // ---- views ---------------------------------------------------------------------------------

        void DrawCalendarIcon(float cx, float cy, float size, Color c)
        {
            float s = size / 16f;
            var body = new RectangleF(cx - 7 * s, cy - 6 * s, 14 * s, 13 * s);
            using (var path = Theme.Round(body, 2.5f * s)) paint.G.DrawPath(paint.P(c, 1.5f * s), path);
            paint.G.DrawLine(paint.P(c, 1.5f * s), body.X, body.Y + 4 * s, body.Right, body.Y + 4 * s);
            paint.G.DrawLine(paint.P(c, 1.6f * s), cx - 3.5f * s, cy - 8 * s, cx - 3.5f * s, cy - 4.5f * s);
            paint.G.DrawLine(paint.P(c, 1.6f * s), cx + 3.5f * s, cy - 8 * s, cx + 3.5f * s, cy - 4.5f * s);
            paint.FillCircle(cx - 2.5f * s, cy + 3 * s, 1.2f * s, c);
            paint.FillCircle(cx + 2.5f * s, cy + 3 * s, 1.2f * s, c);
        }

        void DrawMeetingSoon(float w, float h)
        {
            var e = SoonMeeting();
            if (e == null) return;
            float cy = h / 2;
            DrawCalendarIcon(20, cy, 15, MeetColor);
            var left = e.Start - DateTime.Now;
            string right = left.TotalSeconds > 0 ? "−" + Mmss(left.TotalMilliseconds) : "mulai";
            float rw = paint.Measure(right, Theme.Semi(14.5f));
            paint.Text(e.Title, Theme.Semi(12.5f), Theme.Text, 34, cy, w - 34 - rw - 30 - PrivacyExtra(), StringAlignment.Near);
            paint.TextRight(right, Theme.Semi(14.5f), MeetColor, w - 16 - PrivacyExtra(), cy);
            DrawPrivacyDot(w, cy);
        }

        void DrawMeetingAlert(float w, float h)
        {
            var e = toast != null ? toast.Data as CalEvent : null;
            if (e == null) return;
            bool started = toast.Flag;
            paint.FillCircle(37, 36, 19, Theme.WithAlpha(MeetColor, 0.2f));
            DrawCalendarIcon(37, 36, 18, MeetColor);
            string head = started ? "Rapat dimulai" : "Rapat " + Math.Max(1, (int)Math.Ceiling((e.Start - DateTime.Now).TotalMinutes)) + " menit lagi";
            paint.Text(head, Theme.Ui(11.5f), MeetColor, 68, 22, w - 86, StringAlignment.Near);
            paint.Text(e.Title, Theme.Semi(15f), Theme.Text, 68, 42, w - 86, StringAlignment.Near);
            float bw = w - 36;
            if (!string.IsNullOrEmpty(e.Link))
            {
                float joinW = bw * 0.62f;
                PillButton(new RectangleF(18, 70, joinW, 32), "meetJoin", "Gabung", MeetColor, Color.White, delegate { JoinMeeting(e); });
                PillButton(new RectangleF(18 + joinW + 10, 70, bw - joinW - 10, 32), "meetClose", "Tutup", Theme.Fill, Theme.Text, DismissToast);
            }
            else PillButton(new RectangleF(18, 70, bw, 32), "meetClose", "Oke", Theme.Fill, Theme.Text, DismissToast);
        }

        void DrawAgendaPage(float w, float h)
        {
            var now = DateTime.Now;
            float top = 50;
            var add = new RectangleF(w - 18 - 118, top - 10, 118, 26);
            PillButton(add, "meetAdd", "+ Tambah rapat", Theme.WithAlpha(MeetColor, 0.9f), Color.White, delegate { OpenMeetingDialog(null); });
            var next = OngoingOrNext();
            string headline = next == null ? "Belum ada rapat" : next.Start <= now ? "Sedang berlangsung" : DayLabel(next.Start) + " · " + Until(next.Start - now);
            paint.Text("Agenda", Theme.Semi(14f), Theme.Text, 20, top + 3);
            paint.Text(headline, Theme.Ui(11.5f), Theme.Sub, 20 + paint.Measure("Agenda", Theme.Semi(14f)) + 10, top + 3, w - 200, StringAlignment.Near);

            float listTop = top + 24, rowH = 38;
            var area = new RectangleF(12, listTop - 2, w - 24, h - listTop - 34);
            if (agenda.Count == 0)
            {
                DrawCalendarIcon(w / 2, listTop + 34, 22, Theme.Faint);
                paint.TextCenter("Tambahkan rapat supaya Gili mengingatkanmu", Theme.Ui(12f), Theme.Sub, w / 2, listTop + 66);
            }
            var state = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            string lastDay = null;
            float y = listTop - agendaScroll.Value;
            for (int i = 0; i < agenda.Count; i++)
            {
                var e = agenda[i];
                string day = DayLabel(e.Start);
                if (day != lastDay)
                {
                    lastDay = day;
                    paint.Text(day.ToUpperInvariant(), Theme.Semi(10f), Theme.Faint, 22, y + 8);
                    y += 18;
                }
                var row = new RectangleF(16, y, w - 32, rowH - 4);
                bool ongoing = e.Start <= now && e.End > now, past = e.End <= now;
                string id = "agenda" + i;
                if (ongoing) paint.FillRound(row, 11, Theme.WithAlpha(MeetColor, 0.2f));
                else if (Hover(id)) paint.FillRound(row, 11, Theme.Fill);
                float a0 = paint.Alpha;
                if (past) paint.Alpha = a0 * 0.45f;
                paint.Text(MeetTime(e.Start), Theme.Semi(12.5f), ongoing ? MeetColor : Theme.Text, row.X + 10, row.Y + row.Height / 2 - 7);
                paint.Text(MeetTime(e.End), Theme.Ui(10.5f), Theme.Faint, row.X + 10, row.Y + row.Height / 2 + 8);
                bool hasJoin = !string.IsNullOrEmpty(e.Link) && !past;
                float textRight = row.Right - (hasJoin ? 80 : 12);
                string title = e.Title + (e.Source != null && e.Source.Repeat != "none" ? "  ↻" : "") + (e.Source == null ? "  ·  Google" : "");
                paint.Text(title, Theme.Semi(12.5f), Theme.Text, row.X + 58, row.Y + row.Height / 2, textRight - row.X - 58, StringAlignment.Near);
                paint.Alpha = a0;
                if (hasJoin)
                {
                    var ev = e;
                    var join = new RectangleF(row.Right - 72, row.Y + row.Height / 2 - 12, 64, 24);
                    PillButton(join, "agendaJoin" + i, "Gabung", ongoing || (e.Start - now).TotalMinutes <= 10 ? MeetColor : Theme.Fill,
                               ongoing || (e.Start - now).TotalMinutes <= 10 ? Color.White : Theme.Text, delegate { JoinMeeting(ev); });
                }
                if (e.Source != null)
                {
                    var src = e.Source;
                    AddHit(new RectangleF(row.X, row.Y, textRight - row.X, row.Height), id, delegate { OpenMeetingDialog(src); });
                }
                y += rowH;
            }
            g.Restore(state);

            // footer: optional Google Calendar link
            bool linked = settings.Get("calendar.ics", "").Length > 0;
            var foot = new RectangleF(16, h - 30, w - 32, 22);
            string footText = linked ? "Google Calendar tersambung · ganti link" : "Hubungkan Google Calendar (salin link iCal rahasia, lalu klik)";
            if (Hover("icalLink")) paint.FillRound(foot, 11, Theme.Fill);
            paint.TextCenter(footText, Theme.Ui(10.5f), Theme.Faint, w / 2, foot.Y + 11);
            AddHit(foot, "icalLink", LinkIcalFromClipboard);
        }

        void ScrollAgenda(int delta)
        {
            float content = 0;
            string lastDay = null;
            foreach (var e in agenda) { string d = DayLabel(e.Start); if (d != lastDay) { lastDay = d; content += 18; } content += 38; }
            float max = Math.Max(0, content - (PageHeight(AgendaPage) - 110));
            agendaScroll.Target = Math.Max(0, Math.Min(max, agendaScroll.Target - delta / 120f * 38));
            Kick();
        }
    }
}
