using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    class NotifItem
    {
        public uint Id;
        public string AppId = "", AppName = "", Title = "", Body = "";
        public DateTime Time;
        public bool Chat;
        public Bitmap Logo; // shared per app, owned by NotificationSource's cache
    }

    /// <summary>
    /// Reads the Windows notification centre through UserNotificationListener. Unpackaged apps may list and remove
    /// notifications but get no change event, so the island watches the notification database folder instead.
    /// Every call here blocks; use it from a worker thread.
    /// </summary>
    static class NotificationSource
    {
        // Chat and mail apps, matched against the app id and display name (lower case).
        static readonly string[] ChatApps = { "whatsapp", "telegram", "teams", "discord", "slack", "signal", "messenger", "skype", "viber",
                                              "wechat", "instagram", "yourphone", "phone link", "outlook", "windowscommunicationsapps",
                                              "thunderbird", "zoom", "naver.line" };
        static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };
        // Web apps that notify through the browser count as chat when their site shows up in the toast.
        static readonly string[] ChatSites = { "whatsapp", "telegram", "discord", "slack", "messenger", "instagram", "mail.google", "outlook" };
        static readonly Dictionary<string, Bitmap> logos = new Dictionary<string, Bitmap>();

        public static UserNotificationListenerAccessStatus Access()
        {
            return UserNotificationListener.Current.GetAccessStatus();
        }

        /// <summary>Current toasts, newest first.</summary>
        public static List<NotifItem> Read()
        {
            var list = new List<NotifItem>();
            foreach (var n in Await(UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast)))
            {
                var item = new NotifItem { Id = n.Id, Time = n.CreationTime.LocalDateTime };
                try
                {
                    item.AppId = n.AppInfo.AppUserModelId ?? "";
                    item.AppName = n.AppInfo.DisplayInfo.DisplayName ?? "";
                }
                catch (Exception) { } // some desktop apps carry no display info
                if (item.AppName.Length == 0) item.AppName = item.AppId;

                var texts = new List<string>();
                try
                {
                    var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    if (binding != null)
                        foreach (var t in binding.GetTextElements())
                        {
                            string s = Clean(t.Text);
                            if (s.Length > 0) texts.Add(s);
                        }
                }
                catch (Exception ex) { Log.Write("notif text: " + ex.Message); }
                if (texts.Count > 0) item.Title = texts[0];
                if (texts.Count > 1) item.Body = string.Join(" · ", texts.GetRange(1, texts.Count - 1));
                item.Chat = IsChat(item.AppId + " " + item.AppName, string.Join(" ", texts));
                item.Logo = LogoFor(item.AppId, n.AppInfo);
                list.Add(item);
            }
            list.Sort((a, b) => b.Time.CompareTo(a.Time));
            return list;
        }

        public static void Remove(uint id)
        {
            UserNotificationListener.Current.RemoveNotification(id);
        }

        // ---- change detection ----------------------------------------------------------------------
        // Listing notifications makes the notification service rewrite its database, so the folder watcher
        // cannot simply re-list on every write (that loops). Instead it asks the database, read-only through
        // the SQLite that ships with Windows, whether the set of toasts changed; only then the list is read.

        [DllImport("winsqlite3.dll")] static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll", CharSet = CharSet.Unicode)] static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll")] static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] static extern int sqlite3_finalize(IntPtr stmt);
        static IntPtr changeDb;

        /// <summary>A value that changes whenever a toast is added or removed; null when the database cannot be read.</summary>
        public static string ChangeKey(string dbPath)
        {
            try
            {
                if (changeDb == IntPtr.Zero)
                {
                    IntPtr db;
                    const int ReadOnly = 1;
                    if (sqlite3_open_v2(Encoding.UTF8.GetBytes(dbPath + "\0"), out db, ReadOnly, IntPtr.Zero) != 0)
                    {
                        if (db != IntPtr.Zero) sqlite3_close(db);
                        return null;
                    }
                    sqlite3_busy_timeout(db, 300);
                    changeDb = db;
                }
                IntPtr stmt;
                if (sqlite3_prepare16_v2(changeDb, "select count(*), coalesce(max(Id), 0) from Notification where Type = 'toast'", -1, out stmt, IntPtr.Zero) != 0)
                {
                    sqlite3_close(changeDb); // reopen next time (the service may have replaced the file)
                    changeDb = IntPtr.Zero;
                    return null;
                }
                try
                {
                    if (sqlite3_step(stmt) != 100) return null; // SQLITE_ROW
                    return sqlite3_column_int64(stmt, 0) + ":" + sqlite3_column_int64(stmt, 1);
                }
                finally { sqlite3_finalize(stmt); }
            }
            catch (Exception ex) // e.g. no winsqlite3.dll: fall back to slow polling
            {
                Log.Write("notif change key: " + ex.Message);
                return null;
            }
        }

        static bool IsChat(string app, string text)
        {
            app = app.ToLowerInvariant();
            foreach (var c in ChatApps) if (app.Contains(c)) return true;
            bool browser = false;
            foreach (var b in Browsers) if (app.Contains(b)) { browser = true; break; }
            if (!browser) return false;
            text = text.ToLowerInvariant();
            foreach (var s in ChatSites) if (text.Contains(s)) return true;
            return false;
        }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length > 300 ? s.Substring(0, 300) : s;
        }

        static Bitmap LogoFor(string appId, AppInfo info)
        {
            lock (logos)
            {
                Bitmap cached;
                if (logos.TryGetValue(appId, out cached)) return cached;
            }
            Bitmap logo = null;
            try
            {
                var reference = info.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
                using (var stream = Await(reference.OpenReadAsync()))
                {
                    ulong size = stream.Size;
                    if (size > 0 && size < 2 * 1024 * 1024)
                        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                        {
                            uint n = Await(reader.LoadAsync((uint)size));
                            var bytes = new byte[n];
                            reader.ReadBytes(bytes);
                            using (var ms = new MemoryStream(bytes))
                            using (var src = Image.FromStream(ms))
                            {
                                logo = new Bitmap(48, 48, PixelFormat.Format32bppPArgb);
                                using (var gr = Graphics.FromImage(logo))
                                {
                                    gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    gr.DrawImage(src, 0, 0, 48, 48);
                                }
                            }
                        }
                }
            }
            catch (Exception) { logo = null; } // desktop apps often have no logo here; a letter badge stands in
            lock (logos) logos[appId] = logo;
            return logo;
        }

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

    /// <summary>
    /// Message notifications (WhatsApp, Telegram, Teams, ...) on the island: a toast with sender and text, an unread
    /// badge on the small island, and a tab listing what is in the Windows notification centre. While Windows
    /// "Do not disturb" is on, messages are only counted.
    /// </summary>
    partial class IslandForm
    {
        static readonly string NotifDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications");
        List<NotifItem> notifs = new List<NotifItem>();
        readonly HashSet<uint> seenNotifs = new HashSet<uint>();
        readonly WinTimer notifDebounce = new WinTimer { Interval = 400 };
        readonly Spring notifScroll = new Spring(0, 320, 32, 0.2f);
        FileSystemWatcher notifWatcher;
        UserNotificationListenerAccessStatus notifAccess = UserNotificationListenerAccessStatus.Allowed;
        bool notifPrimed, notifBusy, notifAgain, notifAgainForced;
        int unreadNotifs, notifBurst;
        DateTime nextNotifPollUtc, nextNotifReadUtc;
        string notifKey; // last ChangeKey seen; only touched by the fetch worker (one at a time)

        bool NotifEnabled { get { return settings.GetBool("notif", true); } }
        bool NotifContent { get { return settings.GetBool("notif.content", true); } }
        bool NotifAllApps { get { return settings.GetBool("notif.all", false); } }

        void StartNotifications()
        {
            notifDebounce.Tick += delegate { notifDebounce.Stop(); FetchNotifs(false); };
            try
            {
                var listener = UserNotificationListener.Current;
                if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Unspecified)
                    listener.RequestAccessAsync().Completed = delegate { Post(() => FetchNotifs(true)); };
            }
            catch (Exception ex) { Log.Write("notif access: " + ex.Message); }
            UpdateNotifWatch();
        }

        void StopNotifications()
        {
            if (notifWatcher != null) { notifWatcher.Dispose(); notifWatcher = null; }
        }

        void UpdateNotifWatch()
        {
            if (!NotifEnabled)
            {
                StopNotifications();
                notifs = new List<NotifItem>();
                seenNotifs.Clear();
                notifPrimed = false;
                unreadNotifs = 0;
                Kick();
                return;
            }
            if (notifWatcher == null && Directory.Exists(NotifDir))
            {
                try
                {
                    notifWatcher = new FileSystemWatcher(NotifDir, "wpndatabase.db*") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler changed = delegate { Post(() => { notifDebounce.Stop(); notifDebounce.Start(); }); };
                    notifWatcher.Changed += changed;
                    notifWatcher.Created += changed;
                    notifWatcher.EnableRaisingEvents = true;
                }
                catch (Exception ex) { Log.Write("notif watch: " + ex.Message); notifWatcher = null; }
            }
            FetchNotifs(true);
        }

        /// <summary>Called every second: a slow safety check in case the folder watcher misses a change.</summary>
        void CheckNotifPoll()
        {
            if (NotifEnabled && DateTime.UtcNow >= nextNotifPollUtc) FetchNotifs(false);
        }

        /// <summary>
        /// Re-reads the notification list when the database says it changed (or always, when forced). Without
        /// a readable database it falls back to reading every 20 seconds.
        /// </summary>
        void FetchNotifs(bool force)
        {
            if (!NotifEnabled) return;
            if (notifBusy) { notifAgain = true; notifAgainForced |= force; return; }
            notifBusy = true;
            var now = DateTime.UtcNow;
            nextNotifPollUtc = now.AddSeconds(10);
            bool due = now >= nextNotifReadUtc;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var access = notifAccess;
                List<NotifItem> list = null;
                try
                {
                    string key = NotificationSource.ChangeKey(NotifDir + @"\wpndatabase.db");
                    bool changed = key == null ? due : key != notifKey;
                    if (force || changed)
                    {
                        access = NotificationSource.Access();
                        if (access == UserNotificationListenerAccessStatus.Allowed) list = NotificationSource.Read();
                        // The key from before the listing: a toast arriving meanwhile changes it and is read next time.
                        notifKey = key;
                    }
                }
                catch (Exception ex) { Log.Write("notif read: " + ex.Message); }
                Post(() =>
                {
                    notifBusy = false;
                    if (list != null || access != notifAccess) nextNotifReadUtc = DateTime.UtcNow.AddSeconds(20);
                    if (list != null || access != notifAccess) OnNotifs(access, list);
                    if (notifAgain)
                    {
                        bool again = notifAgainForced;
                        notifAgain = notifAgainForced = false;
                        if (again) FetchNotifs(true);
                        else { notifDebounce.Stop(); notifDebounce.Start(); }
                    }
                });
            });
        }

        void OnNotifs(UserNotificationListenerAccessStatus access, List<NotifItem> list)
        {
            notifAccess = access;
            if (demoActive) return; // the self-test shows sample messages, never the real ones
            if (list == null || !NotifEnabled) { Kick(); return; }
            var visible = list.FindAll(n => n.Chat || NotifAllApps);
            var fresh = new List<NotifItem>();
            if (notifPrimed)
                foreach (var n in visible)
                    if (!seenNotifs.Contains(n.Id) && (DateTime.Now - n.Time).TotalMinutes < 3) fresh.Add(n);
            seenNotifs.Clear();
            foreach (var n in list) seenNotifs.Add(n.Id);
            notifPrimed = true;
            notifs = visible;

            // Read elsewhere (on the phone, in the app) means gone from the centre: the badge follows.
            unreadNotifs = Math.Min(unreadNotifs, visible.Count);
            if (fresh.Count > 0)
            {
                if (expanded && page == NotifPage) unreadNotifs = 0;
                else unreadNotifs += fresh.Count;
                AnnounceNotif(fresh[0], fresh.Count);
            }
            Kick();
        }

        void AnnounceNotif(NotifItem item, int count)
        {
            if (Native.DoNotDisturb())
            {
                if (current == View.Eyes) SetMood(Mood.Surprised, 2.5); // a silent glance, nothing pops up
                return;
            }
            bool burst = toast != null && toast.View == View.Notif;
            var t = ShowToast(View.Notif, 5.5, 2, true);
            if (t == null) return;
            notifBurst = burst ? notifBurst + count : count;
            t.Data = item;
            Pulse(AppColor(item), 1.2);
            SetMood(Mood.Happy, 25); // still smiling when the eyes come back after the toast
        }

        void OpenNotifApp(NotifItem item)
        {
            try { Process.Start("explorer.exe", "shell:AppsFolder\\" + item.AppId); }
            catch (Exception ex) { Log.Write("open notif app: " + ex.Message); }
            unreadNotifs = 0;
            expanded = false;
            if (toast != null && toast.View == View.Notif) DismissToast();
            Kick();
        }

        /// <summary>Removes notifications from the Windows notification centre as well.</summary>
        void RemoveNotifs(List<NotifItem> items)
        {
            foreach (var n in items) notifs.Remove(n);
            unreadNotifs = Math.Min(unreadNotifs, notifs.Count);
            Kick();
            var ids = items.ConvertAll(n => n.Id);
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var id in ids)
                {
                    try { NotificationSource.Remove(id); }
                    catch (Exception ex) { Log.Write("notif remove: " + ex.Message); }
                }
                Post(() => FetchNotifs(false));
            });
        }

        void MarkNotifsRead()
        {
            if (unreadNotifs == 0) return;
            unreadNotifs = 0;
            Kick();
        }

        static Color AppColor(NotifItem item)
        {
            string a = (item.AppId + " " + item.AppName).ToLowerInvariant();
            if (a.Contains("whatsapp")) return Color.FromArgb(255, 37, 211, 102);
            if (a.Contains("telegram")) return Color.FromArgb(255, 42, 171, 238);
            if (a.Contains("teams")) return Color.FromArgb(255, 123, 131, 235);
            if (a.Contains("discord")) return Color.FromArgb(255, 88, 101, 242);
            if (a.Contains("slack")) return Color.FromArgb(255, 224, 30, 90);
            if (a.Contains("signal")) return Color.FromArgb(255, 58, 118, 240);
            if (a.Contains("instagram")) return Color.FromArgb(255, 225, 48, 108);
            if (a.Contains("messenger")) return Color.FromArgb(255, 160, 51, 255);
            if (a.Contains("outlook") || a.Contains("communicationsapps")) return Color.FromArgb(255, 40, 140, 230);
            return Theme.DefaultAccent;
        }

        // ---- views -------------------------------------------------------------------------------

        /// <summary>Extra width of the unread badge on the small island.</summary>
        float UnreadExtra()
        {
            if (unreadNotifs <= 0 || !NotifEnabled) return 0;
            return 40 + (unreadNotifs > 9 ? 7 : 0);
        }

        /// <summary>Green pill with a chat glyph and the unread count, ending at <paramref name="right"/>.</summary>
        void DrawUnreadBadge(float right, float cy)
        {
            float extra = UnreadExtra();
            if (extra <= 0) return;
            var r = new RectangleF(right - (extra - 8), cy - 10, extra - 8, 20);
            paint.FillRound(r, 10, Theme.WithAlpha(Theme.Green, 0.2f));
            paint.Icon(Theme.IconChat, 10.5f, Theme.Green, r.X + 11, cy + 0.5f);
            paint.TextCenter(unreadNotifs > 99 ? "99+" : unreadNotifs.ToString(), Theme.Semi(11.5f), Theme.Green, r.X + 11 + (r.Width - 11) / 2 + 1, cy);
        }

        void DrawNotifBadge(NotifItem item, RectangleF r)
        {
            Color c = AppColor(item);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            paint.FillCircle(cx, cy, r.Width / 2, Theme.WithAlpha(c, 0.2f));
            if (item.Logo != null)
            {
                var inner = RectangleF.Inflate(r, -r.Width * 0.17f, -r.Height * 0.17f);
                paint.Image(item.Logo, inner, inner.Width * 0.22f);
                return;
            }
            string name = item.AppName.Length > 0 ? item.AppName : "?";
            paint.TextCenter(name.Substring(0, 1).ToUpperInvariant(), Theme.Semi(r.Height * 0.42f), c, cx, cy);
        }

        string NotifTitle(NotifItem item) { return item.Title.Length > 0 ? item.Title : item.AppName; }

        string NotifBody(NotifItem item)
        {
            if (!NotifContent) return "Pesan baru";
            return item.Body.Length > 0 ? item.Body : item.AppName;
        }

        void DrawNotifToast(float w, float h)
        {
            var item = toast != null ? toast.Data as NotifItem : null;
            if (item == null) return;
            DrawNotifBadge(item, new RectangleF(12, h / 2 - 19, 38, 38));
            string meta = notifBurst > 1 ? notifBurst + " pesan · " + item.AppName : item.AppName;
            float mw = Math.Min(130, paint.Measure(meta, Theme.Ui(11f)));
            paint.TextRight(meta, Theme.Ui(11f), Theme.Faint, w - 22, h / 2 - 9);
            paint.Text(NotifTitle(item), Theme.Semi(13.5f), Theme.Text, 60, h / 2 - 9, w - 60 - 22 - mw - 10, StringAlignment.Near);
            paint.Text(NotifBody(item), Theme.Ui(12.5f), Theme.Sub, 60, h / 2 + 10, w - 60 - 22, StringAlignment.Near);
            AddHit(new RectangleF(0, 0, w, h), "notifOpen", delegate { OpenNotifApp(item); });
        }

        void DrawNotifPage(float w, float h)
        {
            if (!NotifEnabled)
            {
                paint.TextCenter("Notifikasi dimatikan", Theme.Semi(13.5f), Theme.Sub, w / 2, 110);
                paint.TextCenter("Nyalakan di tab Pengaturan", Theme.Ui(11.5f), Theme.Faint, w / 2, 132);
                return;
            }
            if (notifAccess != UserNotificationListenerAccessStatus.Allowed)
            {
                paint.Icon(Theme.IconBellOff, 24, Theme.Faint, w / 2, 92);
                paint.TextCenter("Gili belum diizinkan membaca notifikasi", Theme.Semi(13.5f), Theme.Sub, w / 2, 126);
                paint.TextCenter("Pengaturan Windows > Privasi > Notifikasi", Theme.Ui(11.5f), Theme.Faint, w / 2, 148);
                PillButton(new RectangleF(w / 2 - 80, 168, 160, 30), "notifPerm", "Buka pengaturan", Theme.Fill, Theme.Text, delegate
                {
                    try { Process.Start("ms-settings:privacy-notifications"); }
                    catch (Exception ex) { Log.Write("notif settings: " + ex.Message); }
                });
                return;
            }

            bool quiet = !demoActive && Native.DoNotDisturb(); // the demo shows toasts, so it must not claim quiet mode
            if (notifs.Count == 0)
            {
                paint.Icon(quiet ? Theme.IconBellOff : Theme.IconBell, 24, Theme.Faint, w / 2, 96);
                paint.TextCenter("Belum ada pesan", Theme.Semi(14f), Theme.Sub, w / 2, 130);
                paint.TextCenter(NotifAllApps ? "Notifikasi dari semua aplikasi muncul di sini"
                                               : "Pesan WhatsApp, Telegram, Teams, email, dan lainnya muncul di sini",
                                 Theme.Ui(11.5f), Theme.Faint, w / 2, 152);
                if (quiet) paint.TextCenter("Jangan ganggu Windows sedang aktif", Theme.Ui(11.5f), Theme.Orange, w / 2, 176);
                return;
            }

            string head = "Notifikasi · " + notifs.Count;
            paint.Text(head, Theme.Ui(11.5f), Theme.Sub, 20, 54);
            if (quiet)
            {
                float hx = 20 + paint.Measure(head, Theme.Ui(11.5f)) + 14;
                paint.Icon(Theme.IconBellOff, 11f, Theme.Orange, hx + 5, 54);
                paint.Text("Jangan ganggu", Theme.Ui(11.5f), Theme.Orange, hx + 15, 54);
            }
            var clear = new RectangleF(w - 18 - 92, 43, 92, 22);
            if (Hover("notifClear")) paint.FillRound(clear, 11, Theme.Fill);
            paint.TextCenter("Hapus semua", Theme.Ui(11.5f), Theme.Sub, clear.X + clear.Width / 2, clear.Y + 11);
            AddHit(clear, "notifClear", delegate { notifScroll.Snap(0); RemoveNotifs(new List<NotifItem>(notifs)); });

            const float top = 70, rowH = 46;
            var area = new RectangleF(12, top - 2, w - 24, h - top - 6);
            var state = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            for (int i = 0; i < notifs.Count; i++)
            {
                float y = top + i * rowH - notifScroll.Value;
                if (y + rowH < area.Top || y > area.Bottom) continue;
                var item = notifs[i];
                var row = new RectangleF(16, y, w - 32, rowH - 4);
                string id = "notif" + i, del = "notifDel" + i;
                bool hover = Hover(id) || Hover(del);
                if (hover) paint.FillRound(row, 12, Theme.Fill);
                DrawNotifBadge(item, new RectangleF(row.X + 6, row.Y + 6, 30, 30));
                float tx = row.X + 46;
                paint.Text(NotifTitle(item), Theme.Semi(12.5f), Theme.Text, tx, row.Y + 13, row.Right - tx - 70, StringAlignment.Near);
                paint.Text(NotifBody(item), Theme.Ui(11.5f), Theme.Sub, tx, row.Y + 29, row.Right - tx - 40, StringAlignment.Near);
                if (hover)
                {
                    var x = new RectangleF(row.Right - 30, row.Y + row.Height / 2 - 11, 22, 22);
                    paint.FillCircle(x.X + 11, x.Y + 11, 11, Hover(del) ? Theme.FillHover : Theme.Fill);
                    paint.TextCenter("×", Theme.Semi(14f), Theme.Sub, x.X + 11, x.Y + 11);
                    var victim = item;
                    AddHit(x, del, delegate { RemoveNotifs(new List<NotifItem> { victim }); });
                }
                else paint.TextRight(Ago(item.Time), Theme.Ui(11f), Theme.Faint, row.Right - 10, row.Y + 13);
                var open = item;
                AddHit(new RectangleF(row.X, row.Y, row.Width - 36, row.Height), id, delegate { OpenNotifApp(open); });
            }
            g.Restore(state);
        }

        void ScrollNotifs(int delta)
        {
            float max = Math.Max(0, notifs.Count * 46 - (PageHeight(NotifPage) - 76));
            notifScroll.Target = Math.Max(0, Math.Min(max, notifScroll.Target - delta / 120f * 46 * 2));
            Kick();
        }
    }
}
