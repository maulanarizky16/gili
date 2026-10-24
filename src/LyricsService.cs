using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Gili
{
    class LyricLine
    {
        public double TimeMs;
        public string Text;
    }

    enum LyricsStatus { Loading, Synced, NotFound }

    class Lyrics
    {
        public string TrackKey;
        public LyricsStatus Status;
        public List<LyricLine> Lines = new List<LyricLine>();
        public double OffsetMs; // user nudge for videos whose timing differs from the album version
        public bool Retryable;  // the lookup failed (network / server busy), not "no lyrics exist"
        public DateTime FailedAtUtc;

        /// <summary>Index of the line being sung at <paramref name="posMs"/>, or -1 before the first line.</summary>
        public int IndexAt(double posMs)
        {
            posMs += OffsetMs;
            int lo = 0, hi = Lines.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Lines[mid].TimeMs <= posMs) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        /// <summary>
        /// When the sweep across line <paramref name="i"/> should finish. LRC only stores start times, and the
        /// next line may come after a long instrumental break, so the length is estimated from the text.
        /// </summary>
        public double SweepEndMs(int i)
        {
            double start = Lines[i].TimeMs;
            double next = i + 1 < Lines.Count ? Lines[i + 1].TimeMs : start + 6000;
            double estimate = Math.Max(1500, Lines[i].Text.Length * 120 + 900);
            return start + Math.Min(next - start, estimate);
        }

        public double NextStartMs(int i) { return i + 1 < Lines.Count ? Lines[i + 1].TimeMs : double.MaxValue; }
    }

    /// <summary>
    /// Synced lyrics from LRCLIB (lrclib.net, free community library). Only the title, artist and duration are
    /// sent; found lyrics are cached on disk so a song is fetched once.
    /// </summary>
    class LyricsService
    {
        public event Action<Lyrics> Ready;

        readonly Dictionary<string, Lyrics> memory = new Dictionary<string, Lyrics>();
        static readonly string CacheDir = Path.Combine(Settings.Dir, "lyrics");
        static readonly string OffsetsFile = Path.Combine(CacheDir, "offsets.ini");
        Dictionary<string, double> offsets; // per track (title|artist|app): a music video and the album cut can differ

        double OffsetFor(string trackKey)
        {
            lock (memory)
            {
                if (offsets == null)
                {
                    offsets = new Dictionary<string, double>();
                    try
                    {
                        if (File.Exists(OffsetsFile))
                            foreach (var line in File.ReadAllLines(OffsetsFile, Encoding.UTF8))
                            {
                                int eq = line.IndexOf('=');
                                double ms;
                                if (eq > 0 && double.TryParse(line.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out ms))
                                    offsets[line.Substring(0, eq)] = ms;
                            }
                    }
                    catch (Exception ex) { Log.Write("lyric offsets: " + ex.Message); }
                }
                double v;
                return offsets.TryGetValue(Hash(trackKey), out v) ? v : 0;
            }
        }

        /// <summary>Remembers the user's nudge for this track; a zero offset removes the entry.</summary>
        public void SaveOffset(Lyrics l)
        {
            OffsetFor(l.TrackKey); // make sure the file has been loaded
            lock (memory)
            {
                string id = Hash(l.TrackKey);
                if (Math.Abs(l.OffsetMs) < 1) offsets.Remove(id);
                else offsets[id] = l.OffsetMs;
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    var sb = new StringBuilder();
                    foreach (var kv in offsets) sb.Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    File.WriteAllText(OffsetsFile, sb.ToString(), new UTF8Encoding(false));
                }
                catch (Exception ex) { Log.Write("lyric offsets save: " + ex.Message); }
            }
        }

        public bool ShouldRetry(Lyrics l)
        {
            return l.Retryable && (DateTime.UtcNow - l.FailedAtUtc).TotalSeconds > 30;
        }

        /// <summary>
        /// Cached lyrics or a Loading placeholder while fetching. Returns null while it is too early to tell:
        /// the duration is still unknown (it is what separates a 6-second ad from a song).
        /// </summary>
        public Lyrics Get(MediaInfo m, bool allowUnknownDuration)
        {
            if (!m.Present || m.Title.Length == 0) return null;
            Lyrics l;
            lock (memory)
            {
                if (memory.TryGetValue(m.TrackKey, out l) && !ShouldRetry(l)) return l;
                if (m.DurationMs > 0 && (m.DurationMs < 31000 || m.DurationMs > 15 * 60000)) // ads, previews, podcasts and long videos
                {
                    l = new Lyrics { TrackKey = m.TrackKey, Status = LyricsStatus.NotFound };
                    memory[m.TrackKey] = l;
                    return l;
                }
                if (m.DurationMs <= 0 && !allowUnknownDuration) return null;
                l = new Lyrics { TrackKey = m.TrackKey, Status = LyricsStatus.Loading };
                memory[m.TrackKey] = l;
            }
            string key = m.TrackKey, title = m.Title, artist = m.Artist;
            double duration = m.DurationMs;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var result = Fetch(key, title, artist, duration);
                lock (memory) memory[key] = result;
                var h = Ready;
                if (h != null) h(result);
            });
            return l;
        }

        Lyrics Fetch(string key, string rawTitle, string rawArtist, double durationMs)
        {
            var result = new Lyrics { TrackKey = key, Status = LyricsStatus.NotFound, OffsetMs = OffsetFor(key) };
            string title, artist;
            Clean(rawTitle, rawArtist, out title, out artist);
            string cacheFile = Path.Combine(CacheDir, Hash((artist + "|" + title).ToLowerInvariant()) + ".lrc");
            try
            {
                string lrc = File.Exists(cacheFile) ? File.ReadAllText(cacheFile, Encoding.UTF8) : null;
                if (lrc == null)
                {
                    try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)(3072 | 12288); } catch { } // TLS 1.2 + 1.3 (BMKG accepts only 1.3)
                    int dur = (int)Math.Round(durationMs / 1000);
                    if (artist.Length > 0)
                    {
                        var exact = Request("https://lrclib.net/api/get?artist_name=" + Uri.EscapeDataString(artist) +
                                            "&track_name=" + Uri.EscapeDataString(title) + (dur > 0 ? "&duration=" + dur : ""))
                                    as Dictionary<string, object>;
                        if (exact != null) lrc = Synced(exact);
                    }
                    if (lrc == null)
                        lrc = Best(Request("https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title) +
                                           (artist.Length > 0 ? "&artist_name=" + Uri.EscapeDataString(artist) : "")) as object[], dur);
                    if (lrc == null)
                        lrc = Best(Request("https://lrclib.net/api/search?q=" + Uri.EscapeDataString((artist + " " + title).Trim())) as object[], dur);
                    if (lrc != null)
                    {
                        Directory.CreateDirectory(CacheDir);
                        File.WriteAllText(cacheFile, lrc, new UTF8Encoding(false));
                    }
                }
                if (lrc != null)
                {
                    result.Lines = Parse(lrc);
                    if (result.Lines.Count > 0) result.Status = LyricsStatus.Synced;
                }
            }
            catch (Exception ex)
            {
                Log.Write("lyrics '" + artist + " - " + title + "': " + ex.Message);
                result.Retryable = true;
                result.FailedAtUtc = DateTime.UtcNow;
            }
            return result;
        }

        static object Request(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 9000;
            req.ReadWriteTimeout = 9000;
            req.UserAgent = "Gili/1.0 (Windows dynamic island)";
            try
            {
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(sr.ReadToEnd());
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                // 404 = no lyrics; 400 = LRCLIB rejected the query (e.g. a very long podcast title). Neither is worth retrying.
                if (http != null && (http.StatusCode == HttpStatusCode.NotFound || http.StatusCode == HttpStatusCode.BadRequest)) return null;
                throw;
            }
        }

        static string Synced(Dictionary<string, object> item)
        {
            object v;
            return item.TryGetValue("syncedLyrics", out v) && v is string && ((string)v).Trim().Length > 0 ? (string)v : null;
        }

        /// <summary>Picks the synced result whose length matches the playing track best.</summary>
        static string Best(object[] items, int durationSec)
        {
            if (items == null) return null;
            string best = null;
            double bestScore = double.MaxValue;
            foreach (var o in items)
            {
                var item = o as Dictionary<string, object>;
                if (item == null) continue;
                string lrc = Synced(item);
                if (lrc == null) continue;
                double score = 0;
                object d;
                if (durationSec > 0 && item.TryGetValue("duration", out d) && d != null)
                {
                    score = Math.Abs(Convert.ToDouble(d, CultureInfo.InvariantCulture) - durationSec);
                    if (score > 8) continue; // a different edit of the song would drift out of sync
                }
                if (score < bestScore) { bestScore = score; best = lrc; }
            }
            return best;
        }

        static List<LyricLine> Parse(string lrc)
        {
            var lines = new List<LyricLine>();
            var stamp = new Regex(@"\[(\d+):(\d+(?:[.:]\d+)?)\]");
            foreach (var raw in lrc.Split('\n'))
            {
                var matches = stamp.Matches(raw);
                if (matches.Count == 0) continue;
                var last = matches[matches.Count - 1];
                string text = raw.Substring(last.Index + last.Length).Trim();
                foreach (Match m in matches)
                {
                    double min = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    double sec = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                    lines.Add(new LyricLine { TimeMs = (min * 60 + sec) * 1000, Text = text });
                }
            }
            lines.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            return lines;
        }

        static readonly Regex Noise = new Regex(
            @"official|video|audio|lyric|lirik|\bmv\b|m/v|\bhd\b|\b4k\b|visuali[sz]er|music video|video clip|remaster|explicit|full album|live session",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// Turns what browsers report for a YouTube video ("Artist - Song (Official Music Video)" by "ArtistVEVO")
        /// into a clean artist and title.
        /// </summary>
        internal static void Clean(string rawTitle, string rawArtist, out string title, out string artist)
        {
            string t = rawTitle ?? "", a = (rawArtist ?? "").Trim();
            t = Regex.Replace(t, @"\s*[\(\[【]([^\)\]】]*)[\)\]】]", m => Noise.IsMatch(m.Groups[1].Value) ? "" : m.Value);
            t = Regex.Replace(t, @"\s+\|.*$", "");
            bool channelLike = a.Length == 0 ||
                               Regex.IsMatch(a, @"(?i)(vevo|\s-\s*topic|official|records|music|channel|\btv)$|^official");
            var dash = Regex.Match(t, @"^(.+?)\s+[-–—]\s+(.+)$");
            if (dash.Success && (channelLike || dash.Groups[1].Value.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 a.IndexOf(dash.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
            {
                a = dash.Groups[1].Value.Trim();
                t = dash.Groups[2].Value.Trim();
            }
            a = Regex.Replace(a, @"(?i)\s*-\s*topic$|vevo$", "");
            a = Regex.Replace(a, @"(?i)^official\s*", "");
            t = Regex.Replace(t, @"(?i)\s*[\(\[](ft\.?|feat\.?|featuring)\s[^\)\]]*[\)\]]", "");
            t = Regex.Replace(t, @"(?i)\s+(ft\.?|feat\.?|featuring)\s+.*$", "");
            t = Regex.Replace(t, @"\s*[\(\[][^\)\]]*[\)\]]\s*$", m => Noise.IsMatch(m.Value) ? "" : m.Value);
            title = t.Trim().Trim('"', '\'', '-', ' ');
            artist = a.Trim();
        }

        static string Hash(string s)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in s) h = (h ^ c) * 1099511628211UL;
                return h.ToString("x16");
            }
        }
    }
}
