using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    /// <summary>
    /// "--video": a scripted tour of every feature with sample data, used (together with GILI_DUMP) to produce the
    /// promo video. Settings it touches are meant to be restored by whoever starts it.
    /// </summary>
    partial class IslandForm
    {
        Action Cue(string title, string subtitle, Action then)
        {
            return delegate { DumpCue(title, subtitle); then(); };
        }

        static Bitmap VideoCover()
        {
            var bmp = new Bitmap(128, 128, PixelFormat.Format32bppPArgb);
            using (var gr = Graphics.FromImage(bmp))
            {
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, 128, 128), Color.FromArgb(255, 255, 94, 140), Color.FromArgb(255, 255, 176, 70), 60f))
                    gr.FillRectangle(lg, 0, 0, 128, 128);
                gr.FillEllipse(new SolidBrush(Color.FromArgb(235, 255, 236, 180)), 40, 30, 48, 48);
                for (int i = 0; i < 4; i++)
                    gr.FillRectangle(new SolidBrush(Color.FromArgb(90 - i * 15, 120, 30, 90)), 0, 86 + i * 10, 128, 5);
            }
            return bmp;
        }

        void VideoMedia(bool withLyrics)
        {
            mediaInfo = new MediaInfo
            {
                Present = true, Playing = true, TrackKey = "demo-video", Title = "Pulau Kecil", Artist = "Gili Band", App = "Spotify",
                Cover = VideoCover(), Accent = Color.FromArgb(255, 255, 132, 120),
                PositionMs = 0, DurationMs = 214000, PositionAtUtc = DateTime.UtcNow
            };
            if (withLyrics)
            {
                lyrics = new Lyrics { TrackKey = "demo-video", Status = LyricsStatus.Synced };
                lyrics.Lines.Add(new LyricLine { TimeMs = 200, Text = "Ada pulau kecil di ujung layarku" });
                lyrics.Lines.Add(new LyricLine { TimeMs = 1450, Text = "Menyala pelan saat lagu berputar" });
                lyrics.Lines.Add(new LyricLine { TimeMs = 2700, Text = "Setiap kata ikut bernyanyi" });
                lyrics.Lines.Add(new LyricLine { TimeMs = 3900, Text = "Gili menemani harimu" });
                lyricIndex = lyricPrevIndex = -1;
            }
            else lyrics = null;
            Kick();
        }

        void StartVideo()
        {
            if (demoTimer == null)
            {
                demoTimer = new WinTimer();
                demoTimer.Tick += delegate { NextDemoStep(); };
            }
            var today = DateTime.Today;
            const string wa = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";
            var sampleNotifs = new List<NotifItem>
            {
                new NotifItem { Id = 1, AppId = wa, AppName = "WhatsApp", Title = "Tim Operasional", Body = "Sari: unit 12 sudah kembali ke pool", Time = DateTime.Now, Chat = true },
                new NotifItem { Id = 2, AppId = wa, AppName = "WhatsApp", Title = "Budi Santoso", Body = "Nanti jadi rapat jam 3? Saya bawa laporannya", Time = DateTime.Now, Chat = true },
                new NotifItem { Id = 3, AppId = "Microsoft.OutlookForWindows", AppName = "Outlook", Title = "Laporan bulanan", Body = "Terlampir laporan bulan Oktober", Time = DateTime.Now.AddMinutes(-35), Chat = true },
            };
            // ~53 s tour: short scenes, one caption each. "@fly" cues tell the video composer to animate a message
            // card flying into the island ("delay|title|body"); they are not captions.
            demoSteps = new List<KeyValuePair<int, Action>>
            {
                Step(300, delegate
                {
                    demoActive = videoRecording = true;
                    pointerInside = false;
                    SetSetting("theme", "hitam"); SetSetting("shape", "float"); SetSetting("display", "always"); SetSetting("lyricsPanel", false);
                    expanded = false; DismissToast();
                    mediaInfo = MediaInfo.None; lyrics = null;
                    notifs = new List<NotifItem>(); unreadNotifs = 0;
                }),
                Step(2000, Cue("Gili", "Terinspirasi Dynamic Island", delegate { ShowInfo(Theme.IconSun, Theme.Orange, "Selamat datang", ClockText(), true); })),
                Step(1400, Cue("Musik dari aplikasi apa saja", "Spotify · YouTube · Media Player", delegate { DismissToast(); VideoMedia(false); })),
                Step(1700, delegate { ShowToast(View.Track, 1.6, 1, false, true); }),
                Step(3700, Cue("Lirik karaoke tersinkron", "lebar island mengikuti tiap baris", delegate { DismissToast(); VideoMedia(true); })),
                Step(2400, Cue("Panel musik + visualizer", "warna mengikuti cover album", delegate { Expand(0); })),
                Step(950, Cue("Notifikasi pesan", "WhatsApp, Telegram, Teams langsung di island", delegate
                {
                    expanded = false;
                    VideoMedia(false); // compact music view, so the unread badge has a place
                    DumpCue("@fly", "0|Budi Santoso|Nanti jadi rapat jam 3? Saya bawa laporannya");
                })),
                Step(1500, delegate
                {
                    notifs = new List<NotifItem> { sampleNotifs[1], sampleNotifs[2] };
                    var t = ShowToast(View.Notif, 1.5, 2, true);
                    if (t != null) { t.Data = sampleNotifs[1]; notifBurst = 1; }
                    Pulse(AppColor(sampleNotifs[1]), 1.0);
                    DumpCue("@fly", "450|Tim Operasional|Sari: unit 12 sudah kembali ke pool");
                }),
                Step(1500, delegate
                {
                    notifs = new List<NotifItem>(sampleNotifs);
                    var t = ShowToast(View.Notif, 1.5, 2, true);
                    if (t != null) { t.Data = sampleNotifs[0]; notifBurst = 2; }
                    Pulse(AppColor(sampleNotifs[0]), 1.0);
                }),
                Step(1300, delegate { DismissToast(); unreadNotifs = 2; Kick(); }),
                Step(2000, delegate { Expand(NotifPage); }),
                Step(2400, Cue("Pomodoro + island membelah", "musik dan timer berjalan bersamaan", delegate
                {
                    expanded = false;
                    demoStartedTimer = !pomo.Active;
                    if (demoStartedTimer) pomo.Start(DateTime.UtcNow);
                    Kick();
                })),
                Step(2600, Cue("Jadwal sholat Kemenag RI", "plus cuaca BMKG & tanggal Hijriah", delegate { Expand(2); })),
                Step(1900, Cue("Jadwal rapat", "pengingat + tombol Gabung Meet / Zoom", delegate
                {
                    var now = DateTime.Now;
                    meetings.Add(new Meeting { Id = "video1", Title = "Standup tim", Start = now.AddMinutes(4), DurationMin = 15, Link = "https://meet.google.com/abc-defg-hij" });
                    meetings.Add(new Meeting { Id = "video2", Title = "Review laporan armada", Start = today.AddHours(16), DurationMin = 30, Repeat = "weekdays" });
                    meetings.Add(new Meeting { Id = "video3", Title = "Demo ke klien", Start = today.AddDays(1).AddHours(10), DurationMin = 60, Link = "https://zoom.us/j/1" });
                    RefreshAgenda();
                    SetPage(AgendaPage);
                })),
                Step(2100, delegate
                {
                    expanded = false;
                    var e = agenda.Find(x => x.Uid == "video1");
                    if (e != null) ShowMeetingAlert(e, false);
                }),
                Step(2300, Cue("Monitor sistem", "CPU · RAM · disk · kecepatan internet", delegate { DismissToast(); Expand(SystemPage); })),
                Step(2100, Cue("Riwayat clipboard", "teks, gambar, screenshot", delegate
                {
                    expanded = false;
                    AddClip(new ClipItem { Kind = ClipKind.Text, Text = "Rapat evaluasi armada jam 15.00", Time = DateTime.Now });
                    using (var bmp = SampleImage())
                    {
                        var image = SaveImageClip(bmp);
                        image.Time = DateTime.Now;
                        AddClip(image);
                        ShowClipToast(image);
                    }
                })),
                Step(2100, Cue("File shelf", "seret file masuk, seret keluar ke mana saja", delegate
                {
                    DismissToast();
                    using (var bmp = SampleImage()) bmp.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "foto-liburan.png"), ImageFormat.Png);
                    AddToShelf(new[] { System.IO.Path.Combine(System.IO.Path.GetTempPath(), "foto-liburan.png"), @"C:\Windows\win.ini", @"C:\Windows" });
                    Expand(ShelfPage);
                })),
                Step(1100, Cue("Animasi sistem", "charger · Wi-Fi · perangkat · brightness", delegate
                {
                    expanded = false;
                    var t = ShowToast(View.Charge, 1.1, 2, false);
                    if (t != null) { t.Flag = true; t.Data = "plug"; }
                    Pulse(Theme.Green, 1.0);
                })),
                Step(1100, delegate { ShowFeed(View.Network, 1.1, "wifiOn", "Wi-Fi tersambung", "Rumah 5G", false); }),
                Step(1100, delegate { ShowFeed(View.Device, 1.1, "headphones", "Headphone tersambung", "Galaxy Buds2", false); }),
                Step(1100, delegate { ShowFeed(View.Device, 1.1, "usbIn", "Flashdisk terpasang", "DATA (E:)", false); }),
                Step(900, delegate { DismissToast(); brightness = 40; brightnessBar.Snap(0.4f); OnBrightness(85); }),
                Step(700, Cue("Maskot dengan mood", "mengikuti kursor & suasana laptop", delegate
                {
                    DismissToast(); demoEyes = true; lastLivelyUtc = DateTime.UtcNow.AddMinutes(-1); SetMood(Mood.Neutral, 0.7);
                })),
                Step(700, delegate { SetMood(Mood.Happy, 0.7); }),
                Step(700, delegate { SetMood(Mood.Sad, 0.7); }),
                Step(700, delegate { SetMood(Mood.Surprised, 0.7); }),
                Step(900, delegate { SetMood(Mood.Asleep, 0.9); }),
                Step(900, Cue("Pilihan tema", "Hitam · Adaptif · Terang · Ramadan · Liquid Glass", delegate
                {
                    demoEyes = false; tempMoodUntil = DateTime.MinValue;
                    SetSetting("theme", "adaptif"); Expand(0);
                })),
                Step(900, delegate { SetSetting("theme", "terang"); }),
                Step(900, delegate { SetSetting("theme", "ramadan"); }),
                Step(1300, delegate { SetSetting("theme", "kaca"); }),
                Step(1500, Cue("Bentuk notch", "menempel di tepi atas layar", delegate { expanded = false; SetSetting("theme", "hitam"); SetSetting("shape", "notch"); })),
                Step(1000, Cue("Mode sembunyi", "tidak menghalangi tab browser", delegate { SetSetting("shape", "float"); SetSetting("display", "auto"); revealed = false; Kick(); })),
                Step(500, delegate { dwellProgress = 0.6f; Kick(); }),
                Step(1000, delegate { dwellProgress = 0; Reveal(); }),
                Step(2400, Cue("Gili", "ringan · di bawah 20 MB RAM · gratis", delegate { ShowInfo(Theme.IconSun, Theme.Orange, "Terima kasih!", "", true); })),
                Step(10, delegate
                {
                    DumpCue("end", "");
                    if (demoStartedTimer) { pomo.Reset(); demoStartedTimer = false; SavePomo(); }
                    meetings.RemoveAll(m => m.Id.StartsWith("video"));
                    RefreshAgenda();
                    demoActive = videoRecording = false;
                    mediaInfo = MediaInfo.None; lyrics = null;
                    unreadNotifs = 0;
                    notifPrimed = false; // back to the real notification list, without announcing it
                    FetchNotifs(true);
                    Kick();
                }),
            };
            demoIndex = 0;
            NextDemoStep();
        }
    }
}
