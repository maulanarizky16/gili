using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Gili
{
    /// <summary>Small dark form to add or edit a meeting (the island itself never takes keyboard focus).</summary>
    class MeetingDialog : Form
    {
        static readonly Color Back = Color.FromArgb(24, 24, 28), Field = Color.FromArgb(40, 40, 46), Fore = Color.FromArgb(240, 240, 245),
                              Muted = Color.FromArgb(150, 150, 160), Accent = Color.FromArgb(120, 150, 255);
        static readonly int[] Durations = { 15, 30, 45, 60, 90, 120 };
        static readonly int[] Reminders = { 1, 5, 10, 15, 30 };

        readonly TextBox title = new TextBox(), link = new TextBox();
        readonly DateTimePicker date = new DateTimePicker(), time = new DateTimePicker();
        readonly ComboBox duration = new ComboBox(), repeat = new ComboBox(), remind = new ComboBox();
        readonly Meeting meeting;
        public bool Deleted { get; private set; }

        public MeetingDialog(Meeting existing)
        {
            meeting = existing ?? new Meeting { Start = NextHalfHour() };
            Text = existing == null ? "Tambah rapat · Gili" : "Edit rapat · Gili";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = Back;
            ForeColor = Fore;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(420, 410);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 18;
            AddLabel("Judul rapat", y); y += 25;
            Style(title, y); title.Text = meeting.Title; y += 40;

            AddLabel("Tanggal", y); AddLabel("Jam mulai", y, 220); y += 25;
            date.Format = DateTimePickerFormat.Custom; date.CustomFormat = "ddd, dd MMM yyyy";
            date.Bounds = new Rectangle(18, y, 190, 28); date.Value = meeting.Start.Date;
            time.Format = DateTimePickerFormat.Custom; time.CustomFormat = "HH:mm"; time.ShowUpDown = true;
            time.Bounds = new Rectangle(220, y, 182, 28); time.Value = DateTime.Today + meeting.Start.TimeOfDay;
            Controls.Add(date); Controls.Add(time); y += 44;

            AddLabel("Durasi", y); AddLabel("Ingatkan", y, 220); y += 25;
            Combo(duration, new Rectangle(18, y, 190, 28));
            foreach (var d in Durations) duration.Items.Add(d < 60 ? d + " menit" : d % 60 == 0 ? d / 60 + " jam" : (d / 60) + " jam " + (d % 60) + " menit");
            duration.SelectedIndex = Math.Max(0, Array.IndexOf(Durations, meeting.DurationMin));
            if (Array.IndexOf(Durations, meeting.DurationMin) < 0) duration.SelectedIndex = 1;
            Combo(remind, new Rectangle(220, y, 182, 28));
            foreach (var r in Reminders) remind.Items.Add(r + " menit sebelum");
            remind.SelectedIndex = Math.Max(0, Array.IndexOf(Reminders, meeting.RemindMin));
            if (Array.IndexOf(Reminders, meeting.RemindMin) < 0) remind.SelectedIndex = 1;
            y += 44;

            AddLabel("Ulangi", y); y += 25;
            Combo(repeat, new Rectangle(18, y, 384, 28));
            repeat.Items.AddRange(Meeting.RepeatLabels);
            repeat.SelectedIndex = Math.Max(0, Array.IndexOf(Meeting.RepeatKeys, meeting.Repeat));
            y += 44;

            AddLabel("Link Meet / Zoom / Teams (opsional)", y); y += 25;
            Style(link, y); link.Text = meeting.Link ?? "";
            if (existing == null) link.Text = LinkFromClipboard();
            y += 48;

            var save = Button("Simpan", new Rectangle(ClientSize.Width - 18 - 110, y, 110, 34), true);
            save.Click += delegate { Save(); };
            var cancel = Button("Batal", new Rectangle(ClientSize.Width - 18 - 110 - 10 - 90, y, 90, 34), false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            if (existing != null)
            {
                var del = Button("Hapus", new Rectangle(18, y, 90, 34), false);
                del.ForeColor = Color.FromArgb(255, 110, 100);
                del.Click += delegate
                {
                    if (MessageBox.Show(this, "Hapus rapat \"" + meeting.Title + "\"" + (meeting.Repeat != "none" ? " (semua jadwal berulangnya)" : "") + "?",
                                        "Gili", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    Deleted = true;
                    DialogResult = DialogResult.OK;
                    Close();
                };
            }
            AcceptButton = save;
            CancelButton = cancel;
            ClientSize = new Size(ClientSize.Width, y + 34 + 18);
            Shown += delegate { Activate(); title.Focus(); };
        }

        public Meeting Result { get { return meeting; } }

        static DateTime NextHalfHour()
        {
            var n = DateTime.Now.AddMinutes(30);
            return n.Date.AddHours(n.Hour).AddMinutes(n.Minute < 30 ? 30 : 60);
        }

        static string LinkFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText()) return "";
                var m = Regex.Match(Clipboard.GetText(), @"https://(?:meet\.google\.com|[\w.-]*zoom\.us|teams\.microsoft\.com|teams\.live\.com|[\w.-]*webex\.com)/\S+",
                                    RegexOptions.IgnoreCase);
                return m.Success ? m.Value : "";
            }
            catch { return ""; }
        }

        void Save()
        {
            string t = title.Text.Trim();
            if (t.Length == 0) { title.Focus(); title.BackColor = Color.FromArgb(70, 40, 44); return; }
            string l = link.Text.Trim();
            if (l.Length > 0 && !l.StartsWith("http", StringComparison.OrdinalIgnoreCase)) l = "https://" + l;
            meeting.Title = t;
            meeting.Start = date.Value.Date.AddHours(time.Value.Hour).AddMinutes(time.Value.Minute);
            meeting.DurationMin = Durations[Math.Max(0, duration.SelectedIndex)];
            meeting.RemindMin = Reminders[Math.Max(0, remind.SelectedIndex)];
            meeting.Repeat = Meeting.RepeatKeys[Math.Max(0, repeat.SelectedIndex)];
            meeting.Link = l;
            DialogResult = DialogResult.OK;
            Close();
        }

        void AddLabel(string text, int y, int x = 18)
        {
            Controls.Add(new Label { Text = text, ForeColor = Muted, AutoSize = true, Location = new Point(x, y), Font = new Font("Segoe UI", 9f) });
        }

        void Style(TextBox box, int y)
        {
            box.Bounds = new Rectangle(18, y, 384, 28);
            box.BackColor = Field;
            box.ForeColor = Fore;
            box.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(box);
        }

        void Combo(ComboBox box, Rectangle bounds)
        {
            box.Bounds = bounds;
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.FlatStyle = FlatStyle.Flat;
            box.BackColor = Field;
            box.ForeColor = Fore;
            Controls.Add(box);
        }

        Button Button(string text, Rectangle bounds, bool primary)
        {
            var b = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, ForeColor = primary ? Color.White : Fore,
                                 BackColor = primary ? Accent : Field, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            Controls.Add(b);
            return b;
        }
    }
}
