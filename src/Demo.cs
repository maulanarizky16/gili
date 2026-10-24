using System;
using System.Collections.Generic;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    /// <summary>Tray > "Lihat demo animasi": plays every live activity once, without touching real settings.</summary>
    partial class IslandForm
    {
        WinTimer demoTimer;
        List<KeyValuePair<int, Action>> demoSteps;
        int demoIndex;
        MediaInfo demoSavedMedia;
        bool demoStartedTimer, demoActive, demoEyes;

        /// <summary>Pretends something is playing (keeping the real artwork) so the music views can be shown.</summary>
        void DemoMedia()
        {
            if (mediaInfo.Playing) return;
            if (demoSavedMedia == null) demoSavedMedia = mediaInfo;
            var src = mediaInfo.Present ? mediaInfo : null;
            mediaInfo = new MediaInfo
            {
                Present = true, Playing = true, TrackKey = "demo",
                Title = src != null ? src.Title : "Lagu Contoh", Artist = src != null ? src.Artist : "Gili Demo",
                App = src != null ? src.App : "Spotify", Cover = src != null ? src.Cover : null,
                Accent = src != null ? src.Accent : Theme.Green,
                PositionMs = 42000, DurationMs = 213000, PositionAtUtc = DateTime.UtcNow
            };
        }

        void StartDemo()
        {
            if (demoTimer == null)
            {
                demoTimer = new WinTimer();
                demoTimer.Tick += delegate { NextDemoStep(); };
            }
            demoSteps = new List<KeyValuePair<int, Action>>
            {
                Step(2600, delegate
                {
                    volumeBar.Target = Math.Min(1f, volume + 0.25f);
                    ShowToast(View.Volume, 2.4, 1, false);
                }),
                Step(3000, delegate
                {
                    volumeBar.Target = volume;
                    var t = ShowToast(View.Charge, 2.8, 2, false);
                    if (t != null) { t.Flag = true; t.Data = "plug"; }
                    Pulse(Theme.Green, 1.8);
                }),
                Step(3000, delegate
                {
                    var t = ShowToast(View.Charge, 2.8, 2, false);
                    if (t != null) { t.Flag = false; t.Data = "unplug"; }
                }),
                Step(3000, delegate
                {
                    var t = ShowToast(View.Charge, 2.8, 2, false);
                    if (t != null) { t.Flag = true; t.Data = "full"; }
                }),
                Step(3000, delegate { ShowToast(View.LowBattery, 2.8, 2, false); Pulse(Theme.Red, 2.5); }),
                Step(3000, delegate { ShowFeed(View.Network, 2.8, "wifiOn", "Wi-Fi tersambung", netState != null && netState.Name.Length > 0 ? netState.Name : "Rumah 5G", false); }),
                Step(3000, delegate { ShowFeed(View.Network, 2.8, "noNet", "Tersambung, tanpa internet", "Rumah 5G", false); }),
                Step(3000, delegate { ShowFeed(View.Network, 2.8, "wifiOff", "Wi-Fi terputus", "Rumah 5G", false); }),
                Step(3000, delegate { ShowFeed(View.Network, 2.8, "ethOn", "LAN tersambung", "Ethernet", false); }),
                Step(3000, delegate { ShowFeed(View.Device, 2.8, "headphones", "Headphone tersambung", "Galaxy Buds2", false); }),
                Step(3400, delegate
                {
                    var e = ShowFeed(View.Device, 3.2, "usbIn", "Flashdisk terpasang", "DATA (E:)", true);
                    if (e != null) e.Path = null; // demo: no real drive to open
                }),
                Step(3000, delegate { ShowFeed(View.Device, 2.8, "monIn", "Monitor tersambung", "2 layar", false); }),
                Step(3000, delegate { ShowFeed(View.Saver, 2.8, "on", "Hemat baterai aktif", "Aktivitas latar dikurangi", false); }),
                Step(10, delegate { DismissToast(); demoEyes = true; lastLivelyUtc = DateTime.UtcNow.AddMinutes(-1); }), // let the eyes take over
                Step(3000, delegate { SetMood(Mood.Neutral, 2.9); Kick(); }),
                Step(3000, delegate { SetMood(Mood.Happy, 2.9); }),
                Step(3000, delegate { SetMood(Mood.Sad, 2.9); }),
                Step(3000, delegate { SetMood(Mood.Surprised, 2.9); }),
                Step(3000, delegate { SetMood(Mood.Sleepy, 2.9); }),
                Step(3600, delegate { SetMood(Mood.Asleep, 3.5); }),
                Step(10, delegate { demoEyes = false; tempMoodUntil = DateTime.MinValue; Kick(); }),
                Step(3400, delegate
                {
                    DemoMedia();
                    ShowToast(View.Track, 3.2, 1, false);
                }),
                Step(3000, delegate
                {
                    var t = ShowToast(View.Privacy, 2.8, 2, false);
                    if (t != null) t.Title = "Zoom";
                }),
                Step(2600, delegate { DismissToast(); }),
                Step(4000, delegate
                {
                    // music + timer at once: the island splits and a bubble drips out
                    demoStartedTimer = !pomo.Active;
                    if (demoStartedTimer) pomo.Start(DateTime.UtcNow);
                    Kick();
                }),
                Step(3400, delegate { Expand(0); }),
                Step(3400, delegate { SetPage(1); }),
                Step(3400, delegate { SetPage(2); }),
                Step(3400, delegate { SetPage(AgendaPage); }),
                Step(3400, delegate { SetPage(SystemPage); }),
                Step(4200, delegate
                {
                    expanded = false;
                    pomo.LastCompleted = PomoMode.Focus;
                    ShowToast(View.TimerDone, 4.0, 3, true);
                    Pulse(PomoColor(pomo.Mode), 3);
                }),
                Step(4200, delegate
                {
                    int i; DateTime when;
                    if (prayers != null) prayers.Next(DateTime.Now, out i, out when);
                    else { i = PrayerCalc.Maghrib; when = DateTime.Now; }
                    var t = ShowToast(View.Adzan, 4.0, 3, true);
                    if (t != null) { t.Title = PrayerCalc.Names[i]; t.Detail = Hm(when); }
                    Pulse(Theme.Prayer, 3.5);
                }),
                Step(10, delegate
                {
                    DismissToast();
                    if (demoStartedTimer) { pomo.Reset(); demoStartedTimer = false; SavePomo(); }
                    if (mediaInfo.TrackKey == "demo") mediaInfo = demoSavedMedia ?? MediaInfo.None;
                    demoSavedMedia = null;
                    demoActive = false;
                    lastNearUtc = DateTime.UtcNow; // auto-hide takes over again after the usual grace period
                    Kick();
                }),
            };
            demoIndex = 0;
            expanded = false;
            demoActive = true;
            if (AutoHide) Reveal();
            NextDemoStep();
        }

        static KeyValuePair<int, Action> Step(int ms, Action action) { return new KeyValuePair<int, Action>(ms, action); }

        /// <summary>
        /// Developer check (--selftest, usually with GILI_DUMP set): shows the clipboard, shelf, weather and glass views
        /// with sample data, without touching the real clipboard or mouse, then restores the user's settings.
        /// </summary>
        void StartSelfTest()
        {
            if (demoTimer == null)
            {
                demoTimer = new WinTimer();
                demoTimer.Tick += delegate { NextDemoStep(); };
            }
            string savedTheme = settings.Get("theme", "hitam"), savedShelf = settings.Get("shelf", "");
            string samplePng = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gili-selftest.png");
            using (var bmp = SampleImage()) bmp.Save(samplePng, System.Drawing.Imaging.ImageFormat.Png);

            var wa = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";
            var sampleNotifs = new List<NotifItem>
            {
                new NotifItem { Id = 1, AppId = wa, AppName = "WhatsApp", Title = "Budi Santoso", Body = "Nanti jadi rapat jam 3? Saya bawa laporan armada", Time = DateTime.Now, Chat = true },
                new NotifItem { Id = 2, AppId = wa, AppName = "WhatsApp", Title = "Tim Operasional", Body = "Sari: unit 12 sudah kembali ke pool", Time = DateTime.Now.AddMinutes(-4), Chat = true },
                new NotifItem { Id = 3, AppId = "Microsoft.OutlookForWindows", AppName = "Outlook", Title = "Laporan bulanan", Body = "Terlampir laporan bulan Oktober", Time = DateTime.Now.AddMinutes(-35), Chat = true },
            };
            demoSteps = new List<KeyValuePair<int, Action>>
            {
                Step(1800, delegate
                {
                    demoActive = true;
                    if (AutoHide) Reveal();
                    notifs = new List<NotifItem>(sampleNotifs);
                    var t = ShowToast(View.Notif, 2.4, 2, true);
                    if (t != null) { t.Data = sampleNotifs[0]; notifBurst = 1; }
                    Pulse(AppColor(sampleNotifs[0]), 1.2);
                }),
                Step(1600, delegate
                {
                    var t = ShowToast(View.Notif, 2.4, 2, true);
                    if (t != null) { t.Data = sampleNotifs[1]; notifBurst = 2; }
                }),
                Step(2600, delegate { DismissToast(); unreadNotifs = 2; Kick(); }),
                Step(2000, delegate { Expand(NotifPage); }),
                Step(2600, delegate
                {
                    expanded = false;
                    var text = new ClipItem { Kind = ClipKind.Text, Text = "Rapat evaluasi armada jam 15.00 di ruang 2", Time = DateTime.Now };
                    AddClip(text);
                    ShowClipToast(text);
                }),
                Step(2600, delegate
                {
                    using (var bmp = SampleImage())
                    {
                        var image = SaveImageClip(bmp);
                        image.Time = DateTime.Now;
                        AddClip(image);
                        ShowClipToast(image);
                    }
                }),
                Step(2600, delegate
                {
                    DismissToast();
                    AddClip(new ClipItem { Kind = ClipKind.Files, Text = @"C:\Windows\win.ini" + "\n" + @"C:\Windows\system.ini", Time = DateTime.Now });
                    Expand(ClipboardPage);
                }),
                Step(2600, delegate
                {
                    AddToShelf(new[] { @"C:\Windows", @"C:\Windows\win.ini", samplePng });
                    SetPage(ShelfPage);
                }),
                Step(2600, delegate { SetPage(2); }),
                Step(2600, delegate { SetPage(AgendaPage); }),
                Step(3000, delegate { SetPage(SystemPage); }),
                Step(2600, delegate
                {
                    expanded = false;
                    var t = ShowToast(View.Rain, 2.4, 2, false);
                    if (t != null) t.Data = new WeatherEntry { Local = DateTime.Now.AddHours(1), Code = 61, Desc = "Hujan Sedang", TempC = 27 };
                }),
                Step(3200, delegate { DismissToast(); SetSetting("theme", "kaca"); Expand(0); }),
                Step(2600, delegate { SetPage(2); }),
                Step(2600, delegate { SetPage(SettingsPage); }),
                Step(2600, delegate { expanded = false; Kick(); }),
                Step(10, delegate
                {
                    SetSetting("theme", savedTheme);
                    settings.Set("shelf", savedShelf);
                    settings.Save();
                    shelf.Clear();
                    LoadShelf();
                    foreach (var c in clips) DeleteClipFile(c);
                    clips.Clear();
                    try { System.IO.File.Delete(samplePng); } catch { }
                    demoActive = false;
                    unreadNotifs = 0;
                    notifPrimed = false; // re-list the real notifications without announcing them
                    FetchNotifs(true);
                    lastNearUtc = DateTime.UtcNow;
                    Kick();
                }),
            };
            demoIndex = 0;
            NextDemoStep();
        }

        static System.Drawing.Bitmap SampleImage()
        {
            var bmp = new System.Drawing.Bitmap(640, 360);
            using (var gr = System.Drawing.Graphics.FromImage(bmp))
            using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, 640, 360),
                       System.Drawing.Color.FromArgb(255, 72, 220, 170), System.Drawing.Color.FromArgb(255, 150, 120, 255), 30f))
            {
                gr.FillRectangle(lg, 0, 0, 640, 360);
                gr.FillEllipse(System.Drawing.Brushes.White, 440, 80, 120, 120);
            }
            return bmp;
        }

        void NextDemoStep()
        {
            demoTimer.Stop();
            if (demoSteps == null || demoIndex >= demoSteps.Count) return;
            var step = demoSteps[demoIndex++];
            step.Value();
            demoTimer.Interval = step.Key;
            demoTimer.Start();
        }
    }
}
