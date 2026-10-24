using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Gili
{
    /// <summary>Immutable snapshot of what is playing.</summary>
    class MediaInfo
    {
        public static readonly MediaInfo None = new MediaInfo();

        public bool Present;
        public string Title = "", Artist = "", App = "", TrackKey = "";
        public bool Playing, CanSeek;
        public double PositionMs, DurationMs;
        public DateTime PositionAtUtc;
        public Bitmap Cover;
        public Color Accent = Theme.DefaultAccent;

        public double CurrentPositionMs(DateTime nowUtc)
        {
            double p = PositionMs;
            if (Playing) p += (nowUtc - PositionAtUtc).TotalMilliseconds;
            if (DurationMs > 0) p = Math.Min(p, DurationMs);
            return Math.Max(0, p);
        }
    }

    /// <summary>
    /// Reads the Windows media overlay (Spotify, browsers, Media Player, ...). Driven by WinRT events
    /// on a dedicated worker thread; a slow safety refresh covers apps that forget to raise them.
    /// </summary>
    class MediaService
    {
        public event Action<MediaInfo> Changed;

        readonly AutoResetEvent signal = new AutoResetEvent(true);
        readonly Queue<Action<GlobalSystemMediaTransportControlsSession>> commands = new Queue<Action<GlobalSystemMediaTransportControlsSession>>();
        GlobalSystemMediaTransportControlsSessionManager manager;
        GlobalSystemMediaTransportControlsSession subscribed;
        string trackKey;
        Bitmap cover;
        Color accent = Theme.DefaultAccent;
        long coverHash;
        int coverAttempts;
        DateTime nextCoverTry = DateTime.MaxValue;
        bool lastPresent;

        public void Start()
        {
            var t = new Thread(Run) { IsBackground = true, Name = "media" };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
        }

        public void TogglePlay() { Enqueue(s => Fire(s.TryTogglePlayPauseAsync())); }
        public void Next() { Enqueue(s => Fire(s.TrySkipNextAsync())); }
        public void Previous() { Enqueue(s => Fire(s.TrySkipPreviousAsync())); }
        public void Seek(double ms) { Enqueue(s => Fire(s.TryChangePlaybackPositionAsync((long)(ms * 10000)))); }

        // Commands are fire-and-forget: the resulting state change arrives through the session events.
        static void Fire(IAsyncOperation<bool> op) { }

        void Enqueue(Action<GlobalSystemMediaTransportControlsSession> cmd)
        {
            lock (commands) commands.Enqueue(cmd);
            signal.Set();
        }

        void Run()
        {
            try
            {
                manager = Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                manager.CurrentSessionChanged += delegate { signal.Set(); };
                manager.SessionsChanged += delegate { signal.Set(); };
            }
            catch (Exception ex)
            {
                Log.Write("media unavailable: " + ex.Message);
                return;
            }

            while (true)
            {
                int timeout = 15000;
                if (nextCoverTry != DateTime.MaxValue)
                    timeout = Math.Max(10, (int)(nextCoverTry - DateTime.UtcNow).TotalMilliseconds);
                signal.WaitOne(timeout);
                Thread.Sleep(30); // let bursts of events settle

                RunCommands();
                try { Refresh(); }
                catch (Exception ex) { Log.Write("media refresh: " + ex.Message); }
            }
        }

        void RunCommands()
        {
            while (true)
            {
                Action<GlobalSystemMediaTransportControlsSession> cmd;
                lock (commands)
                {
                    if (commands.Count == 0) return;
                    cmd = commands.Dequeue();
                }
                try
                {
                    var s = manager.GetCurrentSession();
                    if (s != null) cmd(s);
                }
                catch (Exception ex) { Log.Write("media command: " + ex.Message); }
            }
        }

        void Subscribe(GlobalSystemMediaTransportControlsSession s)
        {
            if (subscribed != null)
            {
                try
                {
                    subscribed.MediaPropertiesChanged -= OnSessionEvent;
                    subscribed.PlaybackInfoChanged -= OnPlaybackEvent;
                    subscribed.TimelinePropertiesChanged -= OnTimelineEvent;
                }
                catch { }
            }
            subscribed = s;
            if (s != null)
            {
                s.MediaPropertiesChanged += OnSessionEvent;
                s.PlaybackInfoChanged += OnPlaybackEvent;
                s.TimelinePropertiesChanged += OnTimelineEvent;
            }
        }

        void OnSessionEvent(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) { signal.Set(); }
        void OnPlaybackEvent(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) { signal.Set(); }
        void OnTimelineEvent(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) { signal.Set(); }

        void Refresh()
        {
            var s = manager.GetCurrentSession();
            if (!ReferenceEquals(s, subscribed)) Subscribe(s);

            if (s == null)
            {
                trackKey = null;
                nextCoverTry = DateTime.MaxValue;
                if (lastPresent) { lastPresent = false; Publish(MediaInfo.None); }
                return;
            }

            var props = Await(s.TryGetMediaPropertiesAsync());
            var playback = s.GetPlaybackInfo();
            var timeline = s.GetTimelineProperties();
            string app = FriendlyApp(s.SourceAppUserModelId);
            string title = props.Title ?? "";
            string artist = props.Artist ?? "";
            if (artist.Length == 0 && !string.IsNullOrEmpty(props.AlbumArtist)) artist = props.AlbumArtist;
            // YouTube auto-generated channels: "The Panturas - Topic", "HindiaVEVO"
            artist = System.Text.RegularExpressions.Regex.Replace(artist, @"\s*-\s*Topic$|(?<=\S)VEVO$", "").Trim();
            string key = title + "|" + artist + "|" + app;

            var now = DateTime.UtcNow;
            if (key != trackKey)
            {
                trackKey = key;
                cover = null;
                coverHash = 0;
                accent = DefaultAccentFor(app);
                coverAttempts = 0;
                nextCoverTry = now;
            }

            // Players often publish the artwork a moment after the title (and sometimes swap it once more),
            // so try a few times shortly after a track change.
            if (now >= nextCoverTry)
            {
                coverAttempts++;
                if (cover == null || coverAttempts == 4) LoadCover(props.Thumbnail);
                nextCoverTry = coverAttempts >= 5 ? DateTime.MaxValue : now.AddMilliseconds(coverAttempts < 3 ? 500 : 1200);
            }

            var info = new MediaInfo();
            info.Present = true;
            info.Title = title;
            info.Artist = artist;
            info.App = app;
            info.TrackKey = key;
            info.Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            info.CanSeek = playback.Controls.IsPlaybackPositionEnabled;
            info.PositionMs = timeline.Position.TotalMilliseconds;
            info.DurationMs = (timeline.EndTime - timeline.StartTime).TotalMilliseconds;
            var updated = timeline.LastUpdatedTime.UtcDateTime;
            info.PositionAtUtc = updated.Year > 2000 && updated <= now.AddSeconds(1) ? updated : now;
            info.Cover = cover;
            info.Accent = accent;
            lastPresent = true;
            Publish(info);
        }

        void Publish(MediaInfo info)
        {
            var h = Changed;
            if (h != null) h(info);
        }

        void LoadCover(IRandomAccessStreamReference reference)
        {
            if (reference == null) return;
            try
            {
                byte[] bytes;
                using (var stream = Await(reference.OpenReadAsync()))
                {
                    ulong size = stream.Size;
                    if (size == 0 || size > 16 * 1024 * 1024) return;
                    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                    {
                        uint n = Await(reader.LoadAsync((uint)size));
                        bytes = new byte[n];
                        reader.ReadBytes(bytes);
                    }
                }
                long hash = Hash(bytes);
                if (hash == coverHash && cover != null) return;

                Bitmap square;
                using (var ms = new MemoryStream(bytes))
                using (var src = Image.FromStream(ms))
                {
                    square = SquareCrop(src, 128);
                }
                cover = square;
                coverHash = hash;
                accent = ExtractAccent(square);
            }
            catch (Exception ex) { Log.Write("cover: " + ex.Message); }
        }

        static Bitmap SquareCrop(Image src, int size)
        {
            // Video thumbnails (e.g. YouTube's 4:3 frames around a 16:9 picture) carry black bars; drop them first.
            var content = new Rectangle(0, 0, src.Width, src.Height);
            if (Math.Abs(src.Width - src.Height) > Math.Max(src.Width, src.Height) * 0.05)
            {
                using (var probe = new Bitmap(src)) content = ContentBounds(probe);
            }
            int side = Math.Min(content.Width, content.Height);
            var srcRect = new Rectangle(content.X + (content.Width - side) / 2, content.Y + (content.Height - side) / 2, side, side);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size), srcRect, GraphicsUnit.Pixel);
            }
            return bmp;
        }

        /// <summary>Bounds of the picture inside letterbox / pillarbox bars (pure black rows and columns).</summary>
        static Rectangle ContentBounds(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            int stride = data.Stride;
            bmp.UnlockBits(data);

            Func<int, int, int, int, bool> dark = (x0, y0, x1, y1) =>
            {
                for (int y = y0; y <= y1; y += 2)
                    for (int x = x0; x <= x1; x += 2)
                    {
                        int i = y * stride + x * 4;
                        if (px[i] > 24 || px[i + 1] > 24 || px[i + 2] > 24) return false;
                    }
                return true;
            };
            int top = 0, bottom = h - 1, left = 0, right = w - 1;
            while (top < h * 0.3 && dark(0, top, w - 1, top)) top++;
            while (bottom > h * 0.7 && dark(0, bottom, w - 1, bottom)) bottom--;
            while (left < w * 0.3 && dark(left, top, left, bottom)) left++;
            while (right > w * 0.7 && dark(right, top, right, bottom)) right--;
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        /// <summary>Picks a vivid colour from the artwork and lifts it so it reads well on black.</summary>
        static Color ExtractAccent(Bitmap bmp)
        {
            double r = 0, g = 0, b = 0, weight = 0;
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new byte[data.Stride * data.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                for (int y = 0; y < data.Height; y += 3)
                {
                    for (int x = 0; x < data.Width; x += 3)
                    {
                        int i = y * data.Stride + x * 4;
                        float pb = px[i] / 255f, pg = px[i + 1] / 255f, pr = px[i + 2] / 255f;
                        float max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                        float sat = max <= 0 ? 0 : (max - min) / max;
                        float w = sat * sat * (0.2f + max); // favour saturated, not-too-dark pixels
                        r += pr * w; g += pg * w; b += pb * w; weight += w;
                    }
                }
            }
            finally { bmp.UnlockBits(data); }

            if (weight < 1.5) return Color.FromArgb(255, 200, 200, 210); // greyscale artwork
            r /= weight; g /= weight; b /= weight;

            double h, s, l;
            Theme.RgbToHsl(r, g, b, out h, out s, out l);
            s = Math.Max(s, 0.55);
            l = Math.Max(0.6, Math.Min(0.7, l + 0.1));
            return Theme.HslToColor(h, s, l);
        }

        static Color DefaultAccentFor(string app)
        {
            if (app == "Spotify") return Color.FromArgb(255, 30, 215, 96);
            return Theme.DefaultAccent;
        }

        static long Hash(byte[] bytes)
        {
            unchecked
            {
                long h = (long)14695981039346656037UL;
                for (int i = 0; i < bytes.Length; i++) h = (h ^ bytes[i]) * 1099511628211L;
                return h ^ bytes.Length;
            }
        }

        static string FriendlyApp(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string l = id.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("brave")) return "Brave";
            if (l.Contains("opera")) return "Opera";
            if (l.Contains("vlc")) return "VLC";
            if (l.Contains("applemusic")) return "Apple Music";
            if (l.Contains("zunemusic") || l.Contains("zunevideo")) return "Media Player";
            string n = id.Split('!')[0];
            n = n.Substring(n.LastIndexOf('\\') + 1);
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            int us = n.IndexOf('_');
            if (us > 0)
            {
                n = n.Substring(0, us);
                n = n.Substring(n.LastIndexOf('.') + 1);
            }
            return n;
        }

        /// <summary>Blocks until a WinRT async operation completes (we are on a worker thread).</summary>
        static T Await<T>(IAsyncOperation<T> op)
        {
            using (var done = new ManualResetEvent(false))
            {
                op.Completed = delegate { try { done.Set(); } catch (ObjectDisposedException) { } };
                if (op.Status == AsyncStatus.Started && !done.WaitOne(5000))
                    throw new TimeoutException("WinRT operation timed out");
            }
            if (op.Status != AsyncStatus.Completed)
                throw new InvalidOperationException("WinRT operation " + op.Status);
            return op.GetResults();
        }
    }
}
