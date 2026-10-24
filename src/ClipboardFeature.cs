using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    enum ClipKind { Text, Image, Files }

    class ClipItem
    {
        public ClipKind Kind;
        public string Text = "";   // the text, or file paths separated by newlines
        public string ImagePath;   // PNG kept on disk so screenshots do not sit in memory
        public Bitmap Thumb;
        public Size ImageSize;
        public DateTime Time;

        public string[] Files { get { return Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries); } }

        public string Preview
        {
            get
            {
                if (Kind == ClipKind.Image) return "Gambar " + ImageSize.Width + "×" + ImageSize.Height;
                if (Kind == ClipKind.Files)
                {
                    var f = Files;
                    return f.Length == 1 ? Path.GetFileName(f[0].TrimEnd('\\')) : f.Length + " file · " + Path.GetFileName(f[0].TrimEnd('\\'));
                }
                string t = Text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
                while (t.Contains("  ")) t = t.Replace("  ", " ");
                return t.Length > 160 ? t.Substring(0, 160) : t;
            }
        }
    }

    /// <summary>
    /// Clipboard history: every copy (text, image, screenshot, files) flashes on the island and lands in the
    /// Clipboard tab, where a click copies it again. Text lives in memory only; images go to a temp folder.
    /// </summary>
    partial class IslandForm
    {
        const int MaxClips = 20, MaxImageClips = 6;
        static readonly string ClipDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gili", "clips");
        readonly List<ClipItem> clips = new List<ClipItem>();
        readonly WinTimer clipTimer = new WinTimer { Interval = 150 };
        readonly Spring clipScroll = new Spring(0, 320, 32, 0.2f);
        bool ignoreNextClip, clipListening;
        ClipItem flashedClip;
        DateTime flashedUntil;

        bool ClipboardEnabled { get { return settings.GetBool("clipboard", true); } }

        void StartClipboard()
        {
            try
            {
                if (Directory.Exists(ClipDir))
                    foreach (var f in Directory.GetFiles(ClipDir, "*.png")) File.Delete(f); // history does not survive a restart
            }
            catch (Exception ex) { Log.Write("clips cleanup: " + ex.Message); }
            clipTimer.Tick += delegate { clipTimer.Stop(); ReadClipboard(); };
            clipListening = Native.AddClipboardFormatListener(Handle);
            if (!clipListening) Log.Write("clipboard listener failed: " + Marshal.GetLastWin32Error());
        }

        void StopClipboard()
        {
            if (clipListening) Native.RemoveClipboardFormatListener(Handle);
            foreach (var c in clips) DeleteClipFile(c);
        }

        void OnClipboardUpdate()
        {
            if (!ClipboardEnabled) return;
            clipTimer.Stop(); // apps publish several formats in a burst; read once it settles
            clipTimer.Start();
        }

        void ReadClipboard()
        {
            if (ignoreNextClip) { ignoreNextClip = false; return; }
            IDataObject data = null;
            for (int attempt = 0; attempt < 3 && data == null; attempt++)
            {
                try { data = Clipboard.GetDataObject(); }
                catch (ExternalException) { Thread.Sleep(40); } // another app still holds the clipboard
            }
            if (data == null) return;

            // Password managers flag secrets so that clipboard tools leave them alone.
            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return;
            if (data.GetDataPresent("CanIncludeInClipboardHistory"))
            {
                var flag = data.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                if (flag != null && flag.Length >= 4 && BitConverter.ToInt32(flag.ToArray(), 0) == 0) return;
            }

            ClipItem item = null;
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0) item = new ClipItem { Kind = ClipKind.Files, Text = string.Join("\n", files) };
                }
                else if (data.GetDataPresent(DataFormats.UnicodeText))
                {
                    // Text wins over an attached picture (Excel and Word put both on the clipboard).
                    string text = data.GetData(DataFormats.UnicodeText) as string;
                    if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0)
                        item = new ClipItem { Kind = ClipKind.Text, Text = text.Length > 20000 ? text.Substring(0, 20000) : text };
                }
                else if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    using (var img = Clipboard.GetImage())
                        if (img != null) item = SaveImageClip(img);
                }
            }
            catch (Exception ex) { Log.Write("clipboard read: " + ex.Message); }
            if (item == null) return;
            item.Time = DateTime.Now;
            AddClip(item);
            ShowClipToast(item);
        }

        ClipItem SaveImageClip(Image img)
        {
            Directory.CreateDirectory(ClipDir);
            string path = Path.Combine(ClipDir, DateTime.Now.Ticks + ".png");
            img.Save(path, ImageFormat.Png);
            var item = new ClipItem { Kind = ClipKind.Image, ImagePath = path, ImageSize = img.Size };
            float k = Math.Min(128f / img.Width, 96f / img.Height);
            var thumb = new Bitmap(Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)), PixelFormat.Format32bppPArgb);
            using (var gr = Graphics.FromImage(thumb))
            {
                gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gr.DrawImage(img, 0, 0, thumb.Width, thumb.Height);
            }
            item.Thumb = thumb;
            return item;
        }

        void AddClip(ClipItem item)
        {
            for (int i = clips.Count - 1; i >= 0; i--)
            {
                var c = clips[i];
                if (c == item || (c.Kind == item.Kind && item.Kind != ClipKind.Image && c.Text == item.Text)) clips.RemoveAt(i);
            }
            clips.Insert(0, item);
            int images = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i].Kind == ClipKind.Image && ++images > MaxImageClips) { DeleteClipFile(clips[i]); clips.RemoveAt(i--); }
            }
            while (clips.Count > MaxClips) { DeleteClipFile(clips[clips.Count - 1]); clips.RemoveAt(clips.Count - 1); }
            Kick();
        }

        static void DeleteClipFile(ClipItem c)
        {
            if (c.ImagePath == null) return;
            try { File.Delete(c.ImagePath); } catch { }
        }

        void ShowClipToast(ClipItem item)
        {
            bool image = item.Kind == ClipKind.Image;
            // While hidden only images (screenshots) drop the island down; text copies happen far too often.
            var t = ShowToast(View.Clip, image ? 4.5 : 1.8, 1, image, image);
            if (t != null) t.Data = item;
        }

        void CopyClip(ClipItem item)
        {
            try
            {
                ignoreNextClip = true;
                if (item.Kind == ClipKind.Text) Clipboard.SetText(item.Text);
                else if (item.Kind == ClipKind.Files)
                {
                    var list = new StringCollection();
                    list.AddRange(item.Files);
                    Clipboard.SetFileDropList(list);
                }
                else
                {
                    using (var ms = new MemoryStream(File.ReadAllBytes(item.ImagePath)))
                    using (var img = Image.FromStream(ms))
                        Clipboard.SetImage(img);
                }
                item.Time = DateTime.Now;
                AddClip(item);
                flashedClip = item;
                flashedUntil = DateTime.UtcNow.AddSeconds(1.2);
                clipScroll.Target = 0;
            }
            catch (Exception ex)
            {
                ignoreNextClip = false;
                Log.Write("clipboard write: " + ex.Message);
            }
            Kick();
        }

        void SaveClipImage(ClipItem item)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "Gili " + item.Time.ToString("yyyy-MM-dd HHmmss") + ".png");
                File.Copy(item.ImagePath, target, true);
                DismissToast();
                ShowInfo(Theme.IconPin, Theme.Green, "Disimpan", "Gambar\\Screenshots", true);
            }
            catch (Exception ex) { Log.Write("save clip: " + ex.Message); }
        }

        void OpenClipImage(ClipItem item)
        {
            try { Process.Start(item.ImagePath); }
            catch (Exception ex) { Log.Write("open clip: " + ex.Message); }
            DismissToast();
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "baru";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " mnt";
            if (d.TotalHours < 24) return (int)d.TotalHours + " j";
            return (int)d.TotalDays + " h";
        }

        // ---- views -------------------------------------------------------------------------------

        float ClipToastWidth(ClipItem item)
        {
            return item != null && item.Kind == ClipKind.Image ? 384 : 330;
        }

        void DrawClipBadge(ClipItem item, RectangleF r)
        {
            if (item.Kind == ClipKind.Image && item.Thumb != null)
            {
                float k = Math.Min(r.Width / item.Thumb.Width, r.Height / item.Thumb.Height);
                var fit = new RectangleF(r.X + (r.Width - item.Thumb.Width * k) / 2, r.Y + (r.Height - item.Thumb.Height * k) / 2,
                                         item.Thumb.Width * k, item.Thumb.Height * k);
                paint.Image(item.Thumb, fit, Math.Min(6, fit.Height / 4));
                return;
            }
            paint.FillRound(r, Math.Min(8, r.Height / 3), Theme.Fill);
            if (item.Kind == ClipKind.Files) paint.Icon(Theme.IconFolder, r.Height * 0.48f, Theme.Sub, r.X + r.Width / 2, r.Y + r.Height / 2);
            else paint.TextCenter("Aa", Theme.Semi(r.Height * 0.4f), Theme.Sub, r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        void DrawClipToast(float w, float h)
        {
            var item = toast != null ? toast.Data as ClipItem : null;
            if (item == null) return;
            if (item.Kind == ClipKind.Image)
            {
                DrawClipBadge(item, new RectangleF(12, 10, 64, h - 20));
                paint.Text("Gambar disalin", Theme.Semi(13.5f), Theme.Text, 88, h / 2 - 9);
                paint.Text(item.ImageSize.Width + "×" + item.ImageSize.Height, Theme.Ui(11.5f), Theme.Sub, 88, h / 2 + 10);
                PillButton(new RectangleF(w - 18 - 76 - 8 - 62, h / 2 - 15, 76, 30), "clipSave", "Simpan", Theme.Fill, Theme.Text,
                           delegate { SaveClipImage(item); });
                PillButton(new RectangleF(w - 18 - 62, h / 2 - 15, 62, 30), "clipOpen", "Buka", Theme.Fill, Theme.Text,
                           delegate { OpenClipImage(item); });
                return;
            }
            DrawClipBadge(item, new RectangleF(9, 8, 28, 28));
            string title = item.Kind == ClipKind.Files ? (item.Files.Length == 1 ? "File disalin" : item.Files.Length + " file disalin") : "Teks disalin";
            paint.Text(title, Theme.Semi(12.5f), Theme.Text, 46, 15);
            paint.Text(item.Preview, Theme.Ui(11.5f), Theme.Sub, 46, 30, w - 46 - 16, StringAlignment.Near);
        }

        void DrawClipboardPage(float w, float h)
        {
            if (!ClipboardEnabled)
            {
                paint.TextCenter("Riwayat clipboard dimatikan", Theme.Semi(13.5f), Theme.Sub, w / 2, 110);
                paint.TextCenter("Nyalakan di tab Pengaturan", Theme.Ui(11.5f), Theme.Faint, w / 2, 132);
                return;
            }
            if (clips.Count == 0)
            {
                paint.Icon(Theme.IconPaste, 24, Theme.Faint, w / 2, 96);
                paint.TextCenter("Belum ada yang disalin", Theme.Semi(14f), Theme.Sub, w / 2, 130);
                paint.TextCenter("Teks, gambar, screenshot, dan file yang kamu salin muncul di sini", Theme.Ui(11.5f), Theme.Faint, w / 2, 152);
                return;
            }

            paint.Text("Riwayat salin · " + clips.Count, Theme.Ui(11.5f), Theme.Sub, 20, 54);
            var clear = new RectangleF(w - 18 - 92, 43, 92, 22);
            if (Hover("clipClear")) paint.FillRound(clear, 11, Theme.Fill);
            paint.TextCenter("Hapus semua", Theme.Ui(11.5f), Theme.Sub, clear.X + clear.Width / 2, clear.Y + 11);
            AddHit(clear, "clipClear", delegate
            {
                foreach (var c in clips) DeleteClipFile(c);
                clips.Clear();
                clipScroll.Snap(0);
                Kick();
            });

            const float top = 70, rowH = 34;
            var area = new RectangleF(12, top - 2, w - 24, h - top - 6);
            var state = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            for (int i = 0; i < clips.Count; i++)
            {
                float y = top + i * rowH - clipScroll.Value;
                if (y + rowH < area.Top || y > area.Bottom) continue;
                var item = clips[i];
                var row = new RectangleF(16, y, w - 32, rowH - 4);
                bool flash = item == flashedClip && DateTime.UtcNow < flashedUntil;
                string id = "clip" + i, del = "clipDel" + i;
                if (flash) paint.FillRound(row, 10, Theme.WithAlpha(Theme.Green, 0.22f));
                else if (Hover(id) || Hover(del)) paint.FillRound(row, 10, Theme.Fill);
                DrawClipBadge(item, new RectangleF(row.X + 6, row.Y + 3, item.Kind == ClipKind.Image ? 38 : 24, row.Height - 6));
                float tx = row.X + (item.Kind == ClipKind.Image ? 54 : 40);
                paint.Text(flash ? "Disalin lagi" : item.Preview, Theme.Ui(12.5f), flash ? Theme.Green : Theme.Text, tx, row.Y + row.Height / 2,
                           row.Right - tx - 64, StringAlignment.Near);
                if (Hover(id) || Hover(del))
                {
                    var x = new RectangleF(row.Right - 30, row.Y + row.Height / 2 - 11, 22, 22);
                    paint.FillCircle(x.X + 11, x.Y + 11, 11, Hover(del) ? Theme.FillHover : Theme.Fill);
                    paint.TextCenter("×", Theme.Semi(14f), Theme.Sub, x.X + 11, x.Y + 11);
                    var victim = item;
                    AddHit(x, del, delegate { DeleteClipFile(victim); clips.Remove(victim); Kick(); });
                }
                else paint.TextRight(Ago(item.Time), Theme.Ui(11f), Theme.Faint, row.Right - 10, row.Y + row.Height / 2);
                var copyItem = item;
                AddHit(new RectangleF(row.X, row.Y, row.Width - 36, row.Height), id, delegate { CopyClip(copyItem); });
            }
            g.Restore(state);
        }

        void ScrollClipboard(int delta)
        {
            float max = Math.Max(0, clips.Count * 34 - (PageHeight(ClipboardPage) - 76));
            clipScroll.Target = Math.Max(0, Math.Min(max, clipScroll.Target - delta / 120f * 34 * 2));
            Kick();
        }
    }
}
