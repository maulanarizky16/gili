using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Gili
{
    class ShelfItem
    {
        public string Path, Name;
        public Bitmap Icon;
        public bool IsImage;
    }

    /// <summary>
    /// File shelf: drop files on the island to park them, drag them out later into a chat, an email or a folder.
    /// Only the paths are remembered; nothing is copied.
    /// </summary>
    partial class IslandForm
    {
        const int MaxShelf = 10;
        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };
        readonly List<ShelfItem> shelf = new List<ShelfItem>();
        bool dragOver, draggingOut;
        string dragCandidate;
        PointF dragStart;

        void LoadShelf()
        {
            foreach (var p in settings.Get("shelf", "").Split('|'))
                if (p.Length > 0 && (File.Exists(p) || Directory.Exists(p))) shelf.Add(MakeShelfItem(p));
        }

        void SaveShelf()
        {
            var paths = new List<string>();
            foreach (var s in shelf) paths.Add(s.Path);
            settings.Set("shelf", string.Join("|", paths));
            settings.Save();
        }

        static ShelfItem MakeShelfItem(string path)
        {
            var item = new ShelfItem { Path = path, Name = System.IO.Path.GetFileName(path.TrimEnd('\\')) };
            if (item.Name.Length == 0) item.Name = path;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(ImageExtensions, ext) >= 0 && File.Exists(path) && new FileInfo(path).Length < 40 * 1024 * 1024)
            {
                try
                {
                    using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                    using (var img = Image.FromStream(ms))
                    {
                        int side = Math.Min(img.Width, img.Height);
                        var thumb = new Bitmap(72, 72, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (var gr = Graphics.FromImage(thumb))
                        {
                            gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gr.DrawImage(img, new Rectangle(0, 0, 72, 72),
                                         new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                        }
                        item.Icon = thumb;
                        item.IsImage = true;
                    }
                }
                catch (Exception ex) { Log.Write("shelf thumb: " + ex.Message); }
            }
            if (item.Icon == null) item.Icon = Native.ShellIcon(path);
            return item;
        }

        void AddToShelf(string[] paths)
        {
            if (paths == null) return;
            foreach (var p in paths)
            {
                if (!File.Exists(p) && !Directory.Exists(p)) continue;
                shelf.RemoveAll(s => string.Equals(s.Path, p, StringComparison.OrdinalIgnoreCase));
                shelf.Insert(0, MakeShelfItem(p));
            }
            while (shelf.Count > MaxShelf) shelf.RemoveAt(shelf.Count - 1);
            SaveShelf();
            Kick();
        }

        // ---- drag in ---------------------------------------------------------------------------

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (draggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
            dragOver = true;
            pointerInside = true; // OLE drags send no mouse moves; keep the island open
            leaveTimer.Stop();
            if (!expanded || page != ShelfPage) Expand(ShelfPage);
            else Kick();
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = !draggingOut && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            dragOver = false;
            pointerInside = false;
            leaveTimer.Stop();
            leaveTimer.Start();
            Kick();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            dragOver = false;
            if (!draggingOut) AddToShelf(e.Data.GetData(DataFormats.FileDrop) as string[]);
        }

        // ---- drag out ----------------------------------------------------------------------------

        /// <summary>Called from mouse-move: starts dragging a shelf file out once the pointer has moved a little.</summary>
        bool TryDragOut(PointF p)
        {
            if (dragCandidate == null || (Control.MouseButtons & MouseButtons.Left) == 0) return false;
            if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) < 6) return false;
            string path = dragCandidate;
            dragCandidate = null;
            pressedId = null;
            draggingOut = true;
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                    DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            catch (Exception ex) { Log.Write("shelf drag: " + ex.Message); }
            finally { draggingOut = false; }
            if (!File.Exists(path) && !Directory.Exists(path)) // it was moved somewhere else
            {
                shelf.RemoveAll(s => s.Path == path);
                SaveShelf();
            }
            Kick();
            return true;
        }

        // ---- view ------------------------------------------------------------------------------

        void DrawShelfPage(float w, float h)
        {
            paint.Text(shelf.Count == 0 ? "Shelf" : "Shelf · " + shelf.Count + " file", Theme.Ui(11.5f), Theme.Sub, 20, 54);
            if (shelf.Count > 0)
            {
                var clear = new RectangleF(w - 18 - 84, 43, 84, 22);
                if (Hover("shelfClear")) paint.FillRound(clear, 11, Theme.Fill);
                paint.TextCenter("Kosongkan", Theme.Ui(11.5f), Theme.Sub, clear.X + clear.Width / 2, clear.Y + 11);
                AddHit(clear, "shelfClear", delegate { shelf.Clear(); SaveShelf(); Kick(); });
            }

            var zone = new RectangleF(18, 68, w - 36, h - 80);
            if (shelf.Count == 0 || dragOver)
            {
                Color edge = dragOver ? Theme.Prayer : Theme.Faint;
                paint.FillRound(zone, 16, dragOver ? Theme.WithAlpha(Theme.Prayer, 0.12f) : Theme.WithAlpha(Theme.Fill, 0.5f));
                using (var path = Theme.Round(zone, 16))
                using (var pen = new Pen(Theme.WithAlpha(edge, paint.Alpha), 1.4f) { DashStyle = DashStyle.Dash })
                    g.DrawPath(pen, path);
                if (shelf.Count == 0 || dragOver)
                {
                    paint.Icon(Theme.IconFolder, 22, edge, w / 2, zone.Y + zone.Height / 2 - 18);
                    paint.TextCenter(dragOver ? "Lepas untuk menyimpan di shelf" : "Seret file ke sini",
                                     Theme.Semi(13.5f), dragOver ? Theme.Prayer : Theme.Sub, w / 2, zone.Y + zone.Height / 2 + 10);
                    if (!dragOver)
                        paint.TextCenter("Simpan sementara, lalu seret keluar ke chat, email, atau folder", Theme.Ui(11f), Theme.Faint,
                                         w / 2, zone.Y + zone.Height / 2 + 30);
                }
                if (dragOver || shelf.Count == 0) return;
            }

            const int cols = 5;
            float gap = 8, tileW = (w - 36 - gap * (cols - 1)) / cols, tileH = 70;
            for (int i = 0; i < shelf.Count; i++)
            {
                var item = shelf[i];
                var r = new RectangleF(18 + (i % cols) * (tileW + gap), 68 + (i / cols) * (tileH + 8), tileW, tileH);
                string id = "shelf" + i, del = "shelfDel" + i;
                bool hot = Hover(id) || Hover(del);
                if (hot) paint.FillRound(r, 12, Theme.Fill);
                var iconRect = new RectangleF(r.X + r.Width / 2 - 18, r.Y + 8, 36, 36);
                if (item.Icon != null) paint.Image(item.Icon, iconRect, item.IsImage ? 8 : 0);
                else paint.Icon(Theme.IconFolder, 26, Theme.Sub, iconRect.X + 18, iconRect.Y + 18);
                paint.Text(item.Name, Theme.Ui(11f), Theme.Text, r.X + r.Width / 2, r.Y + 56, r.Width - 8, StringAlignment.Center);
                if (hot)
                {
                    var x = new RectangleF(r.Right - 20, r.Y + 2, 18, 18);
                    paint.FillCircle(x.X + 9, x.Y + 9, 9, Hover(del) ? Theme.FillHover : Theme.Fill);
                    paint.TextCenter("×", Theme.Semi(12f), Theme.Sub, x.X + 9, x.Y + 9);
                    var victim = item;
                    AddHit(x, del, delegate { shelf.Remove(victim); SaveShelf(); Kick(); });
                }
                var open = item;
                AddHit(r, id, delegate
                {
                    try { Process.Start(open.Path); }
                    catch (Exception ex) { Log.Write("shelf open: " + ex.Message); }
                });
            }
        }

        /// <summary>Mouse-down on a tile arms a possible drag-out (a click still opens the file).</summary>
        void ArmShelfDrag(string hitId, PointF p)
        {
            dragCandidate = null;
            int i;
            if (hitId == null || !hitId.StartsWith("shelf") || !int.TryParse(hitId.Substring(5), out i) || i < 0 || i >= shelf.Count) return;
            dragCandidate = shelf[i].Path;
            dragStart = p;
        }
    }
}
