using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    enum View { Idle, Media, Timer, PrayerSoon, Volume, Charge, LowBattery, Track, Privacy, TimerDone, Adzan, Info, Expanded, Hint, Lyric, Clip, Rain, Network, Device, Saver, Eyes, Meeting, MeetingSoon, Brightness, Notif }

    /// <summary>A short-lived "live activity" that temporarily takes over the island.</summary>
    class Toast
    {
        public View View;
        public int Priority;
        public DateTime Shown;
        public bool Interactive;
        public string Title = "", Detail = "";
        public char Glyph;
        public Color Color;
        public bool Flag;
        public object Data;
    }

    class Hit
    {
        public RectangleF Rect;
        public string Id;
        public Action Action;
    }

    /// <summary>
    /// The island: a per-pixel-alpha layered window at the top centre of the primary monitor.
    /// It only renders while something moves; otherwise the last frame stays on screen at no cost.
    /// </summary>
    partial class IslandForm : Form
    {
        const float FloatGap = 8f, Pad = 30f, BubbleSize = 34f, BubbleGap = 8f;
        const int AgendaPage = 3, SystemPage = 4, NotifPage = 5, ClipboardPage = 6, ShelfPage = 7, SettingsPage = 8;
        const int AppBarMessage = Native.WM_APP + 0x31;
        static readonly Guid PowerSourceGuid = new Guid("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
        static readonly Guid BatteryPercentGuid = new Guid("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly Settings settings = new Settings();
        readonly MediaService media = new MediaService();
        readonly AudioService audio = new AudioService();
        readonly Spectrum spectrum = new Spectrum();
        readonly PrivacyWatcher privacy = new PrivacyWatcher();
        readonly Pomodoro pomo = new Pomodoro();
        readonly Painter paint = new Painter();

        // ---- live state -------------------------------------------------------------------
        MediaInfo mediaInfo = MediaInfo.None;
        bool mediaSeen;
        string pendingTrackToast;
        DateTime pausedSinceUtc = DateTime.MinValue;
        float volume = 0.5f;
        bool muted, volumeKnown;
        bool hasBattery, onAc = true, powerKnown;
        int battery = 100;
        string[] micApps = new string[0], camApps = new string[0];
        bool fullscreen;
        PrayerSchedule prayers;
        string notifiedAdzan = "";
        bool locating;
        DateTime lastLocateUtc = DateTime.MinValue;
        Toast toast;
        DateTime volumeHudUntil;
        DateTime glowStart, glowUntil;
        Color glowColor = Theme.Green;

        // ---- interaction ------------------------------------------------------------------
        bool expanded, pointerInside;
        int page, bubblePage = 1, wheelAccumulator;
        PointF mouseDip = new PointF(-100, -100);
        string hoverId, pressedId;
        readonly List<Hit> hits = new List<Hit>();
        RectangleF shellRect, bubbleRect;

        // ---- animation --------------------------------------------------------------------
        readonly Spring sw = new Spring(12, 380, 27, 0.05f), sh = new Spring(12, 380, 27, 0.05f), sr = new Spring(6, 380, 27, 0.05f);
        readonly Spring bubble = new Spring(0, 170, 17, 0.002f), opacity = new Spring(0, 160, 25, 0.002f);
        readonly Spring pageSpring = new Spring(0, 320, 32, 0.002f), volumeBar = new Spring(0.5f, 320, 30, 0.001f);
        readonly Spring topGap = new Spring(FloatGap, 380, 27, 0.02f);
        Color bgColor;
        View current = View.Idle, previous = View.Idle;
        DateTime switchedUtc = DateTime.UtcNow, lastFrameUtc = DateTime.UtcNow;
        readonly float[] bands = new float[Spectrum.Bands];
        string lastMinute = "";

        // ---- window & canvas --------------------------------------------------------------
        float scale = 1f;
        Rectangle monitorPx;
        IntPtr memDC, dib, oldBitmap;
        Bitmap canvas;
        Graphics g;
        int canvasW, canvasH;
        bool windowShown = true;
        IntPtr winEventHook, powerNotifySource, powerNotifyPercent;
        Native.WinEventProc winEventProc;
        readonly WinTimer frameTimer = new WinTimer(), clockTimer = new WinTimer(), toastTimer = new WinTimer();
        readonly WinTimer hoverTimer = new WinTimer(), leaveTimer = new WinTimer(), fullscreenTimer = new WinTimer();
        NotifyIcon tray;
        ContextMenuStrip menu;
        ToolStripMenuItem autoStartItem, adzanItem, visualizerItem, locationItem;
        Icon trayIcon;

        public IslandForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Gili";
            Bounds = new Rectangle(-32000, -32000, 1, 1);

            Theme.Init();
            Theme.Apply(settings.Get("theme", "hitam"));
            bgColor = Theme.Bg;
            pomo.Load(settings);
            edgeTimer.Tick += delegate { EdgeTick(); };
            glassTimer.Tick += delegate { GlassTick(); };
            AllowDrop = true;
            LoadShelf();

            frameTimer.Interval = 15;
            frameTimer.Tick += delegate { Frame(); };
            clockTimer.Interval = 1000;
            clockTimer.Tick += delegate { ClockTick(); };
            toastTimer.Tick += delegate { toastTimer.Stop(); DismissToast(); };
            hoverTimer.Interval = 350; // long enough that passing over the island on the way to a tab does nothing
            hoverTimer.Tick += delegate
            {
                hoverTimer.Stop();
                if (pointerInside && !expanded && CanExpand() && settings.Get("open", "hover") == "hover") Expand(PointerPage());
            };
            leaveTimer.Interval = 320;
            leaveTimer.Tick += delegate
            {
                leaveTimer.Stop();
                if (!pointerInside && expanded) { expanded = false; MarkLively(); Kick(); }
            };
            fullscreenTimer.Interval = 350;
            fullscreenTimer.Tick += delegate { fullscreenTimer.Stop(); CheckFullscreen(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = Native.WS_POPUP;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateMonitor();

            var abd = new Native.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
            abd.hWnd = Handle;
            abd.uCallbackMessage = AppBarMessage;
            Native.SHAppBarMessage(0, ref abd); // ABM_NEW: lets the shell tell us about fullscreen apps

            winEventProc = delegate { fullscreenTimer.Stop(); fullscreenTimer.Start(); };
            winEventHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, winEventProc, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND

            Guid source = PowerSourceGuid, percent = BatteryPercentGuid;
            powerNotifySource = Native.RegisterPowerSettingNotification(Handle, ref source, 0);
            powerNotifyPercent = Native.RegisterPowerSettingNotification(Handle, ref percent, 0);
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BuildTray();

            media.Changed += m => Post(() => OnMedia(m));
            audio.VolumeChanged += (level, mute, initial) => Post(() => OnVolume(level, mute, initial));
            StartFeeds(); // also restarts the spectrum capture when the output device changes
            privacy.Changed += (mic, cam) => Post(() => OnPrivacy(mic, cam));
            lyricsService.Ready += l => Post(() => OnLyricsReady(l));
            media.Start();
            audio.Start();
            privacy.Start();
            ReadPower();
            SetupPrayers();
            CheckFullscreen();

            int hour = DateTime.Now.Hour;
            string greeting = hour < 11 ? "Selamat pagi" : hour < 15 ? "Selamat siang" : hour < 18 ? "Selamat sore" : "Selamat malam";
            UpdateEdgeWatch();
            StartClipboard();
            StartNotifications();
            StartMascot();
            StartBrightness();
            StartStats();
            RefreshAgenda();
            MaybeRefreshIcal();
            UpdateGlass();
            MaybeRefreshWeather();
            var t = ShowToast(View.Info, 2.6, 1, false, true);
            t.Glyph = hour >= 6 && hour < 18 ? Theme.IconSun : Theme.IconMoon;
            t.Color = hour >= 6 && hour < 18 ? Theme.Orange : Theme.Prayer;
            t.Title = greeting;
            t.Detail = ClockText();

            opacity.Target = 1;
            clockTimer.Start();
            Kick();

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--video") > 0)
            {
                var start = new WinTimer { Interval = 3500 };
                start.Tick += delegate { start.Stop(); start.Dispose(); StartVideo(); };
                start.Start();
            }
            else if (Array.IndexOf(Environment.GetCommandLineArgs(), "--selftest") > 0)
            {
                var start = new WinTimer { Interval = 4000 };
                start.Tick += delegate { start.Stop(); start.Dispose(); StartSelfTest(); };
                start.Start();
            }
            else if (Array.IndexOf(Environment.GetCommandLineArgs(), "--demo") > 0)
            {
                var start = new WinTimer { Interval = 3000 };
                start.Tick += delegate { start.Stop(); start.Dispose(); StartDemo(); };
                start.Start();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            var abd = new Native.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
            abd.hWnd = Handle;
            Native.SHAppBarMessage(1, ref abd); // ABM_REMOVE
            if (winEventHook != IntPtr.Zero) Native.UnhookWinEvent(winEventHook);
            if (powerNotifySource != IntPtr.Zero) Native.UnregisterPowerSettingNotification(powerNotifySource);
            if (powerNotifyPercent != IntPtr.Zero) Native.UnregisterPowerSettingNotification(powerNotifyPercent);
            if (demoStartedTimer) pomo.Reset(); // never persist the demo's pretend timer
            SavePomo();
            StopClipboard();
            StopNotifications();
            StopFeeds();
            StopBrightness();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_CLIPBOARDUPDATE:
                    OnClipboardUpdate();
                    return;
                case Native.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)Native.MA_NOACTIVATE;
                    return;
                case Native.WM_POWERBROADCAST:
                    ReadPower();
                    m.Result = (IntPtr)1;
                    return;
                case Native.WM_DEVICECHANGE:
                {
                    long ev = m.WParam.ToInt64();
                    if (ev == Native.DBT_DEVICEARRIVAL || ev == Native.DBT_DEVICEREMOVECOMPLETE)
                    {
                        var letters = Native.VolumeLetters(m.LParam);
                        bool arrived = ev == Native.DBT_DEVICEARRIVAL;
                        if (letters != null) Post(() => OnVolume(letters, arrived));
                    }
                    break;
                }
                case AppBarMessage:
                    if (m.WParam.ToInt64() == 2) { fullscreenTimer.Stop(); fullscreenTimer.Start(); } // ABN_FULLSCREENAPP
                    return;
                case Native.WM_DPICHANGED:
                case Native.WM_DISPLAYCHANGE:
                    BeginInvoke(new Action(OnDisplayChanged));
                    if (m.Msg == Native.WM_DPICHANGED) return;
                    break;
            }
            base.WndProc(ref m);
        }

        void Post(Action action)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        // ---- monitor / canvas -------------------------------------------------------------

        void OnDisplaySettingsChanged(object sender, EventArgs e) { Post(OnDisplayChanged); }

        void OnDisplayChanged()
        {
            UpdateMonitor();
            CheckMonitors();
            Kick();
        }

        void UpdateMonitor()
        {
            IntPtr mon = Native.MonitorFromPoint(new Native.POINT(0, 0), 1); // primary
            var mi = new Native.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            Native.GetMonitorInfo(mon, ref mi);
            monitorPx = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            float s = 1f;
            try
            {
                uint dx, dy;
                if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0) s = dx / 96f;
            }
            catch (Exception ex) { Log.Write("dpi: " + ex.Message); }
            float target = s * SizeFactor();
            float forced;
            if (float.TryParse(Environment.GetEnvironmentVariable("GILI_SCALE"), System.Globalization.NumberStyles.Float, Inv, out forced) && forced > 0.5f)
                target = forced; // video capture renders the island larger
            if (canvas == null || Math.Abs(target - scale) > 0.001f)
            {
                scale = target;
                CreateCanvas();
            }
        }

        void CreateCanvas()
        {
            if (g != null) { g.Dispose(); canvas.Dispose(); Native.SelectObject(memDC, oldBitmap); Native.DeleteObject(dib); Native.DeleteDC(memDC); }
            canvasW = (int)Math.Ceiling(2 * (250 + Pad) * scale) + 4;
            canvasH = (int)Math.Ceiling((FloatGap + 360 + Pad) * scale) + 4;
            var bmi = new Native.BITMAPINFOHEADER();
            bmi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bmi.biWidth = canvasW;
            bmi.biHeight = -canvasH; // top-down
            bmi.biPlanes = 1;
            bmi.biBitCount = 32;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            IntPtr bits;
            dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
            oldBitmap = Native.SelectObject(memDC, dib);
            canvas = new Bitmap(canvasW, canvasH, canvasW * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(canvas);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.AssumeLinear; // gamma-corrected blending would wash out the translucent greys
            paint.G = g;
        }

        // ---- state machine ------------------------------------------------------------------

        bool MediaVisible()
        {
            if (!mediaInfo.Present) return false;
            return mediaInfo.Playing || (DateTime.UtcNow - pausedSinceUtc).TotalSeconds < 120;
        }

        bool PrayerSoon()
        {
            if (prayers == null || !settings.GetBool("prayer.notify", true)) return false;
            int i; DateTime t;
            prayers.Next(DateTime.Now, out i, out t);
            double minutes = (t - DateTime.Now).TotalMinutes;
            return minutes > 0 && minutes <= 15;
        }

        View DesiredView()
        {
            if (expanded) return View.Expanded;
            if (toast != null) return toast.View;
            if (demoEyes) return View.Eyes;
            if (AutoHide && !revealed) return View.Hint;
            if (MediaVisible()) return LyricOnScreen(DateTime.UtcNow) ? View.Lyric : View.Media;
            if (pomo.Active) return View.Timer;
            if (SoonMeeting() != null) return View.MeetingSoon;
            if (PrayerSoon()) return View.PrayerSoon;
            return EyesWanted() ? View.Eyes : View.Idle;
        }

        bool BubbleWanted()
        {
            if (expanded || toast != null || (current != View.Media && current != View.Lyric)) return false;
            if (pomo.Active) { bubblePage = 1; return true; }
            if (SoonMeeting() != null) { bubblePage = AgendaPage; return true; }
            if (PrayerSoon()) { bubblePage = 2; return true; }
            return false;
        }

        float PrivacyExtra() { return micApps.Length + camApps.Length > 0 ? 16 : 0; }

        SizeF ViewSize(View v)
        {
            switch (v)
            {
                case View.Idle: return new SizeF(IdleWidth(), 34);
                case View.Media: return new SizeF(234 + PrivacyExtra() + UnreadExtra(), 36);
                case View.Lyric: return new SizeF(LyricWidth(), 36);
                case View.Timer: return new SizeF(214 + PrivacyExtra(), 36);
                case View.PrayerSoon: return new SizeF(232 + PrivacyExtra(), 36);
                case View.Volume: return new SizeF(300, 44);
                case View.Charge:
                case View.LowBattery: return new SizeF(340, 46);
                case View.Network: return new SizeF(330, 46);
                case View.Device: return new SizeF(360, 46);
                case View.Saver: return new SizeF(320, 46);
                case View.Eyes: return new SizeF(124 + UnreadExtra(), 38);
                case View.MeetingSoon: return new SizeF(260 + PrivacyExtra(), 36);
                case View.Brightness: return new SizeF(300, 44);
                case View.Meeting: return new SizeF(372, 116);
                case View.Track: return new SizeF(360, 74);
                case View.Privacy: return new SizeF(290, 38);
                case View.TimerDone: return new SizeF(372, 118);
                case View.Adzan: return new SizeF(372, 112);
                case View.Info: return new SizeF(InfoWidth(), 40);
                case View.Hint: return new SizeF(56 + 90 * Ease.OutCubic(dwellProgress), 4 + 2 * dwellProgress);
                case View.Clip: return new SizeF(ClipToastWidth(toast != null ? toast.Data as ClipItem : null), toast != null && toast.Data is ClipItem && ((ClipItem)toast.Data).Kind == ClipKind.Image ? 70 : 44);
                case View.Rain: return new SizeF(340, 44);
                case View.Notif: return new SizeF(380, 62);
                default: return new SizeF(440, PageHeight(page));
            }
        }

        float ViewRadius(View v, SizeF size)
        {
            float r;
            switch (v)
            {
                case View.Expanded: r = 40; break;
                case View.Hint: return 3;
                case View.TimerDone:
                case View.Meeting:
                case View.Adzan: r = 34; break;
                case View.Track: r = 28; break;
                default: r = size.Height / 2; break;
            }
            // A notch hangs from the screen edge: gently rounded bottom corners instead of pill ends.
            if (Notch) r = Math.Min(r, size.Height <= 46 ? 13f : size.Height <= 90 ? 20f : 30f);
            return r;
        }

        /// <summary>Re-evaluates what the island should show and starts animating towards it.</summary>
        void Kick()
        {
            if (g == null) return;
            View want = DesiredView();
            if (want != current)
            {
                previous = current;
                current = want;
                switchedUtc = DateTime.UtcNow;
            }
            SizeF size = ViewSize(current);
            sw.Target = size.Width;
            sh.Target = size.Height;
            sr.Target = ViewRadius(current, size);
            bubble.Target = BubbleWanted() ? 1 : 0;
            opacity.Target = fullscreen ? 0 : 1;
            topGap.Target = current == View.Hint || Notch ? 0 : FloatGap;
            UpdateClickThrough();
            spectrum.SetActive(VisualizerVisible() && !spectrum.Failed && !mediaInfo.TrackKey.StartsWith("demo"));
            if (!frameTimer.Enabled)
            {
                lastFrameUtc = DateTime.UtcNow;
                frameTimer.Interval = 15;
                frameTimer.Start();
            }
        }

        bool VisualizerVisible()
        {
            if (fullscreen || !mediaInfo.Present || !mediaInfo.Playing || !settings.GetBool("visualizer", true)) return false;
            return current == View.Media || current == View.Lyric || current == View.Track || (current == View.Expanded && page == 0);
        }

        bool SpringsMoving(DateTime now)
        {
            return !sw.Settled || !sh.Settled || !sr.Settled || !bubble.Settled || !opacity.Settled ||
                   !pageSpring.Settled || !volumeBar.Settled || !topGap.Settled || !lyricScroll.Settled || !clipScroll.Settled || !notifScroll.Settled || !agendaScroll.Settled || !brightnessBar.Settled || EyeSpringsMoving() || bgColor != TargetBg() ||
                   (now - switchedUtc).TotalSeconds < 0.45 ||
                   (toast != null && (now - toast.Shown).TotalSeconds < 1.0);
        }

        void Frame()
        {
            var now = DateTime.UtcNow;
            float dt = (float)Math.Min(0.05, Math.Max(0.001, (now - lastFrameUtc).TotalSeconds));
            lastFrameUtc = now;
            sw.Step(dt); sh.Step(dt); sr.Step(dt); bubble.Step(dt); opacity.Step(dt); pageSpring.Step(dt); volumeBar.Step(dt); topGap.Step(dt); lyricScroll.Step(dt); clipScroll.Step(dt); notifScroll.Step(dt); agendaScroll.Step(dt); brightnessBar.Step(dt); StepEyes(dt);
            bgColor = StepColor(bgColor, TargetBg(), dt);
            UpdateBands(dt, now);
            UpdateLyrics(now);

            if (opacity.Target == 0 && opacity.Value < 0.01f)
            {
                if (windowShown) { Native.ShowWindow(Handle, Native.SW_HIDE); windowShown = false; }
                frameTimer.Stop();
                return;
            }
            if (!windowShown)
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                windowShown = true;
            }

            try { Render(now); }
            catch (Exception ex) { Log.Write("render: " + ex); }

            bool moving = SpringsMoving(now);
            bool live = VisualizerVisible() || now < glowUntil || (expanded && page == 0 && mediaInfo.Playing) ||
                        (LyricsReady && mediaInfo.Playing && (current == View.Media || current == View.Lyric)) ||
                        (toast != null && IsAnimatedToast(current) && (now - toast.Shown).TotalSeconds < 3.5) ||
                        (current == View.Eyes && CurrentMood() == Mood.Asleep);
            if (!moving && !live) frameTimer.Stop();
            else frameTimer.Interval = moving ? 15 : 33;
        }

        void UpdateBands(float dt, DateTime now)
        {
            if (spectrum.Active)
            {
                spectrum.CopyBands(bands);
            }
            else if ((spectrum.Failed || mediaInfo.TrackKey.StartsWith("demo")) && VisualizerVisible())
            {
                // Loopback capture unavailable (or demo): gentle synthetic motion instead of a frozen bar.
                double t = (now - DateTime.Today.ToUniversalTime()).TotalSeconds;
                for (int i = 0; i < bands.Length; i++)
                    bands[i] = (float)(0.35 + 0.25 * Math.Sin(t * (2.3 + i * 0.41) + i) + 0.15 * Math.Sin(t * 5.1 + i * 1.7));
            }
            else
            {
                for (int i = 0; i < bands.Length; i++) bands[i] = Math.Max(0, bands[i] - dt * 2.5f);
            }
        }

        // ---- rendering ----------------------------------------------------------------------

        void Render(DateTime now)
        {
            float W = sw.Value, H = sh.Value;
            float R = Math.Max(0, Math.Min(sr.Value, Math.Min(W, H) / 2));
            float p = Math.Max(0, bubble.Value);
            float half = W / 2 + (p > 0.001f ? BubbleGap + BubbleSize : 0);
            int pxW = Math.Min(canvasW - (canvasW & 1), 2 * (int)Math.Ceiling((half + Pad) * scale));
            float gap = Math.Max(0, topGap.Value);
            int pxH = Math.Min(canvasH, (int)Math.Ceiling((gap + Math.Max(H, BubbleSize) + Pad) * scale));
            float cx = pxW / scale / 2f;
            windowLeftPx = monitorPx.Left + monitorPx.Width / 2 - pxW / 2; // needed while drawing (glass backdrop mapping)

            g.ResetTransform();
            g.ResetClip();
            g.Clear(Color.Transparent);
            g.ScaleTransform(scale, scale);
            paint.Alpha = 1f;
            paint.Crisp = !SpringsMoving(now);

            shellRect = new RectangleF(cx - W / 2, gap, W, H);
            float cy = gap + Math.Min(H, 40f) / 2f;
            // Notch shape: the top corners straighten as the island touches the screen edge.
            float topR = Notch ? R * Ease.Clamp01(gap / FloatGap) : R;

            // As the island shrinks into the hint line it changes from the theme background to the line colour.
            float hintness = Ease.Clamp01((22f - H) / 16f);
            Color shellColor = Theme.Mix(bgColor, Theme.WithAlpha(HintColor(), HintAlpha()), hintness);

            // bubble geometry: it slides out of the island's right end
            float bubbleR = BubbleSize / 2 * (0.45f + 0.55f * Ease.Clamp01(p));
            float bubbleX = shellRect.Right - BubbleSize / 2 + p * (BubbleGap + BubbleSize);
            bubbleRect = p > 0.02f ? new RectangleF(bubbleX - bubbleR, cy - bubbleR, bubbleR * 2, bubbleR * 2) : RectangleF.Empty;

            if (hintness < 0.9f)
            {
                paint.Alpha = 1 - hintness;
                DrawShadow(shellRect, R);
                if (!bubbleRect.IsEmpty) DrawShadow(bubbleRect, bubbleR);
                paint.Alpha = 1f;
            }
            DrawGlow(now, shellRect, R);
            bool glass = GlassActive && hintness < 0.6f;
            if (!bubbleRect.IsEmpty)
            {
                if (glass)
                    using (var circle = new GraphicsPath()) { circle.AddEllipse(bubbleRect); DrawGlass(circle, bubbleRect, bubbleR); }
                else DrawBubbleShape(cy, bubbleX, bubbleR);
            }
            // Notch: the ears (concave fillets) grow in as the island reaches the screen edge, so it looks like it
            // grows out of the bezel. Body and ears are one shape, so solid themes and glass both fill it.
            float ear = Notch ? NotchEar * Ease.Clamp01(1 - gap / FloatGap) * (1 - Ease.Clamp01(hintness * 1.5f)) : 0;
            using (var shape = ear > 0.5f ? Theme.NotchPath(shellRect, R, ear) : Theme.Round(shellRect, topR, R))
            {
                if (glass) DrawGlass(shape, shellRect, R);
                else g.FillPath(paint.B(shellColor), shape);
            }

            hits.Clear();
            if (current != View.Hint || previous != View.Hint)
            {
                using (var clip = Theme.Round(shellRect, topR, R)) g.SetClip(clip);
                DrawContent(now);
                g.ResetClip();
            }
            if (!bubbleRect.IsEmpty) DrawBubbleContent(now, cy, bubbleX, bubbleR, Ease.Clamp01((p - 0.55f) / 0.45f));

            g.Flush(FlushIntention.Sync);
            lastWindowPx = new Rectangle(windowLeftPx, monitorPx.Top, pxW, pxH);
            DebugDump(now, pxW, pxH);
            var dst = new Native.POINT(windowLeftPx, monitorPx.Top);
            var size = new Native.SIZE(pxW, pxH);
            var src = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION();
            blend.SourceConstantAlpha = (byte)(255 * Ease.Clamp01(opacity.Value));
            blend.AlphaFormat = 1; // AC_SRC_ALPHA
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
        }

        bool Notch { get { return settings.Get("shape", "float") == "notch"; } }

        float SizeFactor()
        {
            string s = settings.Get("size", "m");
            return s == "s" ? 0.88f : s == "l" ? 1.15f : 1f;
        }

        /// <summary>Theme background; the adaptive theme tints it with the artwork colour while music shows.</summary>
        Color TargetBg()
        {
            if (settings.Get("theme", "hitam") != "adaptif" || !mediaInfo.Present) return Theme.Bg;
            bool musicOnScreen = current == View.Media || current == View.Lyric || current == View.Track || (current == View.Expanded && page == 0);
            return musicOnScreen ? Theme.Tint(mediaInfo.Accent) : Theme.Bg;
        }

        static Color StepColor(Color from, Color to, float dt)
        {
            if (from == to) return to;
            float k = Math.Min(1f, dt * 7f);
            int r = from.R + (int)Math.Round((to.R - from.R) * k), gg = from.G + (int)Math.Round((to.G - from.G) * k),
                b = from.B + (int)Math.Round((to.B - from.B) * k);
            var next = Color.FromArgb(255, r, gg, b);
            return Math.Abs(next.R - to.R) + Math.Abs(next.G - to.G) + Math.Abs(next.B - to.B) <= 3 ? to : next;
        }

        /// <summary>Radius of the concave fillets that blend the notch into the top edge of the screen.</summary>
        const float NotchEar = 11f;

        void DrawShadow(RectangleF r, float radius)
        {
            for (int i = 0; i < 4; i++)
            {
                float grow = 1.2f + i * 2.4f;
                var rr = new RectangleF(r.X - grow, r.Y - grow + 2.5f + i, r.Width + grow * 2, r.Height + grow * 2);
                paint.FillRound(rr, radius + grow, Color.FromArgb(Math.Max(0, Theme.ShadowAlpha - i * 5), 0, 0, 0));
            }
        }

        void DrawGlow(DateTime now, RectangleF r, float radius)
        {
            if (now >= glowUntil) return;
            float age = (float)(now - glowStart).TotalSeconds;
            float env = Ease.Clamp01(age / 0.25f) * Ease.Clamp01((float)(glowUntil - now).TotalSeconds / 0.6f);
            float pulse = 0.6f + 0.4f * (float)Math.Sin(age * Math.PI * 2 * 0.8);
            for (int i = 0; i < 5; i++)
            {
                float grow = 1.5f + i * 2.8f;
                var rr = new RectangleF(r.X - grow, r.Y - grow, r.Width + grow * 2, r.Height + grow * 2);
                paint.FillRound(rr, radius + grow, Color.FromArgb((int)(env * pulse * (96 - i * 18)), glowColor));
            }
        }

        void Pulse(Color color, double seconds)
        {
            glowColor = color;
            glowStart = DateTime.UtcNow;
            glowUntil = glowStart.AddSeconds(seconds);
            Kick();
        }

        /// <summary>The bubble body plus a gooey neck while it is detaching from the island.</summary>
        void DrawBubbleShape(float cy, float bx, float br)
        {
            float islandRight = shellRect.Right;
            float gap = (bx - br) - islandRight;
            if (gap < 7f)
            {
                float t = Ease.Clamp01((7f - gap) / 10f);
                float waist = br * 0.75f * t;
                float x0 = islandRight - 6, x1 = bx - br * 0.5f;
                if (x1 > x0 + 1 && waist > 0.8f)
                {
                    float hA = Math.Min(shellRect.Height / 2, 18f) * 0.9f, hB = br * 0.9f, mid = (x0 + x1) / 2;
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(x0, cy - hA, mid, cy - waist, mid, cy - waist, x1, cy - hB);
                        path.AddLine(x1, cy - hB, x1, cy + hB);
                        path.AddBezier(x1, cy + hB, mid, cy + waist, mid, cy + waist, x0, cy + hA);
                        path.CloseFigure();
                        g.FillPath(paint.B(bgColor), path);
                    }
                }
            }
            paint.FillCircle(bx, cy, br, bgColor);
        }

        void DrawContent(DateTime now)
        {
            float t = (float)(now - switchedUtc).TotalSeconds;
            float outA = 1 - Ease.OutCubic(t / 0.13f);
            float inA = Ease.OutCubic((t - 0.06f) / 0.26f);
            if (previous != current && outA > 0.01f) DrawView(previous, outA, 1f - (1 - outA) * 0.05f, false, now);
            DrawView(current, inA, 0.93f + 0.07f * inA, inA > 0.85f, now);
        }

        // ---- input ----------------------------------------------------------------------------

        bool InShape(PointF p)
        {
            var s = shellRect;
            s.Inflate(1, 1);
            return s.Contains(p) || (!bubbleRect.IsEmpty && bubbleRect.Contains(p));
        }

        bool CanExpand() { return toast == null || !toast.Interactive; }

        int PointerPage()
        {
            if (!bubbleRect.IsEmpty && bubbleRect.Contains(mouseDip)) return bubblePage;
            return DefaultPage();
        }

        int DefaultPage()
        {
            if (mediaInfo.Present && MediaVisible()) return 0;
            if (unreadNotifs > 0 && NotifEnabled) return NotifPage;
            if (SoonMeeting() != null) return AgendaPage;
            if (pomo.Active) return 1;
            if (prayers != null) return 2;
            return mediaInfo.Present ? 0 : 1;
        }

        void Expand(int pg)
        {
            if (toast != null && toast.Interactive) return;
            toast = null;
            toastTimer.Stop();
            expanded = true;
            if (AutoHide && !revealed) Reveal();
            page = pg;
            if (pg == SettingsPage) RefreshAutoStart();
            if (pg == SystemPage) ResumeStats();
            if (pg == NotifPage) MarkNotifsRead();
            pageSpring.Snap(pg);
            Kick();
        }

        void SetPage(int pg)
        {
            if (pg == SettingsPage) RefreshAutoStart();
            page = pg;
            if (pg == SystemPage) ResumeStats();
            if (pg == NotifPage) MarkNotifsRead();
            pageSpring.Target = pg;
            Kick();
        }

        string HitAt(PointF p)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Rect.Contains(p)) return hits[i].Id;
            return null;
        }

        void PointerEnter()
        {
            pointerInside = true;
            leaveTimer.Stop();
            if (!expanded) { hoverTimer.Stop(); hoverTimer.Start(); }
        }

        void PointerLeave()
        {
            pointerInside = false;
            hoverTimer.Stop();
            leaveTimer.Stop();
            leaveTimer.Start();
            MarkLively();
            if (hoverId != null) { hoverId = null; Kick(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouseDip = new PointF(e.X / scale, e.Y / scale);
            if (TryDragOut(mouseDip)) return;
            bool inside = InShape(mouseDip);
            if (inside && !pointerInside) PointerEnter();
            else if (!inside && pointerInside) { PointerLeave(); return; }
            string id = inside ? HitAt(mouseDip) : null;
            if (id != hoverId) { hoverId = id; Kick(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (pointerInside) PointerLeave();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                var p = new PointF(e.X / scale, e.Y / scale);
                pressedId = HitAt(p);
                ArmShelfDrag(pressedId, p);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var p = new PointF(e.X / scale, e.Y / scale);
            if (e.Button == MouseButtons.Right)
            {
                menu.Show(Control.MousePosition);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            string id = HitAt(p);
            if (id != null && id == pressedId)
            {
                foreach (var h in hits)
                {
                    if (h.Id != id) continue;
                    try { h.Action(); } catch (Exception ex) { Log.Write("action " + id + ": " + ex.Message); }
                    break;
                }
            }
            else if (id == null && !expanded && InShape(p) && CanExpand())
            {
                hoverTimer.Stop();
                Expand(PointerPage());
            }
            pressedId = null;
            dragCandidate = null;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (expanded && page == ClipboardPage)
            {
                ScrollClipboard(e.Delta);
                return;
            }
            if (expanded && page == AgendaPage)
            {
                ScrollAgenda(e.Delta);
                return;
            }
            if (expanded && page == NotifPage)
            {
                ScrollNotifs(e.Delta);
                return;
            }
            if (expanded && page == 1 && hoverId == "ring" && !pomo.Running)
            {
                wheelAccumulator += e.Delta;
                while (Math.Abs(wheelAccumulator) >= 120)
                {
                    pomo.AdjustMinutes(wheelAccumulator > 0 ? 1 : -1);
                    wheelAccumulator -= Math.Sign(wheelAccumulator) * 120;
                }
                SavePomo();
                Kick();
                return;
            }
            volume = Ease.Clamp01(volume + e.Delta / 120f * 0.04f);
            volumeBar.Target = volume;
            audio.SetVolume(volume);
        }

        // ---- system events --------------------------------------------------------------------

        void OnMedia(MediaInfo m)
        {
            if (mediaInfo.TrackKey.StartsWith("demo")) { demoSavedMedia = m; return; } // the demo restores it afterwards
            var prev = mediaInfo;
            var now = DateTime.UtcNow;
            if (prev.Playing && !m.Playing) pausedSinceUtc = now;
            // Short clips (YouTube ads, previews) should not announce themselves as a new song.
            bool shortClip = m.DurationMs > 0 && m.DurationMs < 31000;
            if (m.Present && m.TrackKey != prev.TrackKey && mediaSeen && m.Title.Length > 0 && !shortClip) pendingTrackToast = m.TrackKey;
            mediaInfo = m;
            SyncLyricsTrack();
            if (m.Present) mediaSeen = true;
            if (pendingTrackToast != null && pendingTrackToast == m.TrackKey && m.Playing)
            {
                pendingTrackToast = null;
                if (!expanded) ShowToast(View.Track, 3.4, 1, false);
            }
            Kick();
        }

        void OnVolume(float level, bool mute, bool initial)
        {
            volume = level;
            muted = mute;
            volumeBar.Target = level;
            if (initial || !volumeKnown)
            {
                volumeKnown = true;
                volumeBar.Snap(level);
                return;
            }
            if (expanded) volumeHudUntil = DateTime.UtcNow.AddSeconds(1.3);
            else ShowToast(View.Volume, 1.6, 1, false, true); // a direct user action: show it even while hidden
            Kick();
        }

        void OnPrivacy(string[] mic, string[] cam)
        {
            string newMic = null, newCam = null;
            foreach (var a in mic) if (Array.IndexOf(micApps, a) < 0) { newMic = a; break; }
            foreach (var a in cam) if (Array.IndexOf(camApps, a) < 0) { newCam = a; break; }
            micApps = mic;
            camApps = cam;
            if (newCam != null || newMic != null)
            {
                var t = ShowToast(View.Privacy, 3.0, 2, false);
                if (t != null)
                {
                    t.Flag = newCam != null;
                    t.Title = newCam ?? newMic;
                }
            }
            Kick();
        }

        int batterySecondsLeft = -1;

        void ReadPower()
        {
            bool hb, ac, saver; int level, secondsLeft;
            Native.ReadPower(out hb, out ac, out level, out saver, out secondsLeft);
            batterySecondsLeft = secondsLeft;
            if (!powerKnown)
            {
                powerKnown = true;
                hasBattery = hb; onAc = ac; battery = level;
                CheckSaver(saver);
                return;
            }
            bool acChanged = ac != onAc;
            int prevLevel = battery;
            hasBattery = hb; onAc = ac; battery = level;
            CheckSaver(saver);
            if (!hb) return;
            if (acChanged)
            {
                var t = ShowToast(View.Charge, 3.2, 2, false);
                if (t != null) { t.Flag = ac; t.Data = ac ? "plug" : "unplug"; }
                if (ac) { Pulse(Theme.Green, 1.8); SetMood(Mood.Happy, 25); }
            }
            else if (ac && level >= 100 && prevLevel < 100)
            {
                var t = ShowToast(View.Charge, 4.5, 2, false);
                if (t != null) { t.Flag = true; t.Data = "full"; }
                SetMood(Mood.Happy, 25);
            }
            else if (!ac && ((prevLevel > 20 && level <= 20) || (prevLevel > 10 && level <= 10) || (prevLevel > 5 && level <= 5)))
            {
                ShowToast(View.LowBattery, level <= 5 ? 7 : 4.5, level <= 5 ? 3 : 2, false);
                Pulse(Theme.Red, level <= 5 ? 4 : 2.5);
            }
            Kick();
        }

        void CheckFullscreen()
        {
            bool fs = Native.IsFullscreenOnPrimary();
            if (!fs && windowShown)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (fs == fullscreen) return;
            fullscreen = fs;
            if (fs) expanded = false;
            Kick();
        }

        void ClockTick()
        {
            var nowUtc = DateTime.UtcNow;
            var local = DateTime.Now;
            if (pomo.Tick(nowUtc))
            {
                SavePomo();
                var t = ShowToast(View.TimerDone, 25, 3, true);
                if (t != null) Chime.FocusDone();
                SetMood(Mood.Happy, 30);
                Pulse(PomoColor(pomo.Mode), 3);
            }
            if (prayers == null || prayers.Day != local.Date) SetupPrayers();
            CheckAdzan(local);
            if (mediaInfo.Present) SyncLyricsTrack(); // late durations and retries after a busy server
            CheckImsak(local);
            MaybeRefreshWeather();
            CheckRain();
            if (local.Second == 0 || agenda.Count == 0 && meetings.Count > 0) RefreshAgenda(); // drop finished meetings, roll over at midnight
            CheckMeetings();
            MaybeRefreshIcal();
            CheckNotifPoll();
            userIdleSeconds = Native.IdleSeconds();

            string minute = local.ToString("HHmm", Inv);
            bool redraw = minute != lastMinute;
            lastMinute = minute;
            if (pomo.Running || PrayerSoon() || SoonMeeting() != null || expanded || DesiredView() != current || BubbleWanted() != (bubble.Target > 0.5f))
                redraw = true;
            if (redraw) Kick();
        }

        void CheckAdzan(DateTime local)
        {
            if (prayers == null || !settings.GetBool("prayer.notify", true)) return;
            foreach (int i in PrayerCalc.Adzan)
            {
                DateTime t = prayers.Times[i];
                if (local < t || local >= t.AddMinutes(2)) continue;
                string key = t.ToString("yyyyMMddHHmm", Inv) + i;
                if (key == notifiedAdzan) continue;
                notifiedAdzan = key;
                var ts = ShowToast(View.Adzan, 30, 3, true);
                if (ts != null)
                {
                    ts.Title = PrayerCalc.Names[i];
                    ts.Detail = Hm(t);
                }
                Chime.Adzan();
                Pulse(Theme.Prayer, 4);
            }
        }

        void SetupPrayers()
        {
            if (!settings.Has("lat") || !settings.Has("lon"))
            {
                prayers = null;
                if ((DateTime.UtcNow - lastLocateUtc).TotalMinutes > 10) DetectLocation(false);
                return;
            }
            prayers = PrayerSchedule.For(DateTime.Now, settings.GetDouble("lat", 0), settings.GetDouble("lon", 0),
                                         settings.Get("city", ""), settings.GetInt("prayer.ihtiyat", 2));
        }

        void DetectLocation(bool announce)
        {
            if (locating) return;
            locating = true;
            lastLocateUtc = DateTime.UtcNow;
            Kick();
            ThreadPool.QueueUserWorkItem(delegate
            {
                var r = LocationService.Detect();
                Post(() =>
                {
                    locating = false;
                    if (r == null)
                    {
                        if (announce) ShowInfo(Theme.IconPin, Theme.Red, "Lokasi gagal dideteksi", "cek internet");
                        Kick();
                        return;
                    }
                    settings.Set("lat", r.Lat.ToString("0.0000", Inv));
                    settings.Set("lon", r.Lon.ToString("0.0000", Inv));
                    settings.Set("city", r.City);
                    settings.Set("bmkg.adm4", ""); // re-resolve the weather station for the new place
                    nextWeatherUtc = DateTime.MinValue;
                    settings.Save();
                    SetupPrayers();
                    if (announce) ShowInfo(Theme.IconPin, Theme.Prayer, "Lokasi sholat", r.City);
                    Kick();
                });
            });
        }

        // ---- toasts ---------------------------------------------------------------------------

        Toast ShowToast(View v, double seconds, int priority, bool interactive, bool force = false)
        {
            var now = DateTime.UtcNow;
            if (toast != null && toast.Priority > priority) return null;
            // While hidden, only things worth interrupting for drop the island down (charger, mic, timer, adzan).
            if (AutoHide && !revealed && !force && !demoActive && priority < 2) return null;
            if (expanded)
            {
                if (priority < 3) return null;
                expanded = false;
            }
            bool same = toast != null && toast.View == v;
            var t = same ? toast : new Toast();
            t.View = v;
            t.Priority = priority;
            t.Interactive = interactive;
            if (!same) t.Shown = now;
            toast = t;
            toastTimer.Stop();
            toastTimer.Interval = Math.Max(1, (int)(seconds * 1000));
            toastTimer.Start();
            Kick();
            return t;
        }

        void ShowInfo(char glyph, Color color, string title, string detail, bool force = false)
        {
            var t = ShowToast(View.Info, 2.8, 1, false, force);
            if (t == null) return;
            t.Glyph = glyph;
            t.Color = color;
            t.Title = title;
            t.Detail = detail;
        }

        void DismissToast()
        {
            toastTimer.Stop();
            toast = null;
            MarkLively();
            Kick();
        }

        // ---- pomodoro actions -------------------------------------------------------------------

        void ToggleTimer()
        {
            if (pomo.Running) pomo.Pause(DateTime.UtcNow);
            else pomo.Start(DateTime.UtcNow);
            SavePomo();
            Kick();
        }

        void SavePomo()
        {
            pomo.Save(settings);
            settings.Save();
        }

        // ---- tray -----------------------------------------------------------------------------

        void BuildTray()
        {
            menu = new ContextMenuStrip();
            var title = new ToolStripMenuItem("Gili · Terinspirasi Dynamic Island") { Enabled = false };
            autoStartItem = new ToolStripMenuItem("Mulai bersama Windows", null, delegate
            {
                try { AutoStart.Enabled = !AutoStart.Enabled; }
                catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
            });
            adzanItem = new ToolStripMenuItem("Pengingat adzan", null, delegate
            {
                settings.Set("prayer.notify", !settings.GetBool("prayer.notify", true));
                settings.Save();
                Kick();
            });
            visualizerItem = new ToolStripMenuItem("Visualizer musik", null, delegate
            {
                settings.Set("visualizer", !settings.GetBool("visualizer", true));
                settings.Save();
                Kick();
            });
            locationItem = new ToolStripMenuItem("Lokasi sholat");
            locationItem.DropDownItems.Add("Deteksi ulang otomatis", null, delegate { DetectLocation(true); });
            locationItem.DropDownItems.Add("Ubah manual (buka settings.ini)", null, delegate
            {
                settings.Save();
                try { Process.Start("notepad.exe", "\"" + settings.FilePath + "\""); }
                catch (Exception ex) { Log.Write("notepad: " + ex.Message); }
            });
            locationItem.DropDownItems.Add("Muat ulang pengaturan", null, delegate
            {
                settings.Load();
                SetupPrayers();
                Kick();
            });
            var openSettings = new ToolStripMenuItem("Pengaturan", null, delegate { revealed = AutoHide; Expand(SettingsPage); });
            var addMeeting = new ToolStripMenuItem("Tambah rapat…", null, delegate { OpenMeetingDialog(null); });
            var demo = new ToolStripMenuItem("Lihat demo animasi", null, delegate { StartDemo(); });
            var exit = new ToolStripMenuItem("Keluar", null, delegate { Close(); });

            menu.Items.AddRange(new ToolStripItem[] {
                title, new ToolStripSeparator(), addMeeting, openSettings, autoStartItem, adzanItem, visualizerItem, locationItem,
                new ToolStripSeparator(), demo, exit });
            menu.Opening += delegate
            {
                try { autoStartItem.Checked = AutoStart.Enabled; } catch { }
                adzanItem.Checked = settings.GetBool("prayer.notify", true);
                visualizerItem.Checked = settings.GetBool("visualizer", true);
                string city = settings.Get("city", "");
                locationItem.Text = "Lokasi sholat: " + (city.Length > 0 ? city : prayers != null ? "manual" : "belum ada");
            };

            trayIcon = MakeTrayIcon();
            tray = new NotifyIcon { Icon = trayIcon, Text = "Gili · Terinspirasi Dynamic Island", ContextMenuStrip = menu, Visible = true };
        }

        static Icon MakeTrayIcon()
        {
            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var gr = Graphics.FromImage(bmp))
                {
                    gr.SmoothingMode = SmoothingMode.AntiAlias;
                    var pill = new RectangleF(2, 9, 28, 14);
                    using (var path = Theme.Round(pill, 7))
                    using (var lg = new LinearGradientBrush(pill, Color.FromArgb(255, 72, 220, 170), Color.FromArgb(255, 150, 120, 255), 0f))
                        gr.FillPath(lg, path);
                    gr.FillEllipse(Brushes.White, 20, 12.5f, 7, 7);
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }
    }
}
