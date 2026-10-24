using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Gili
{
    static class Log
    {
        static readonly object sync = new object();

        public static string Path { get { return System.IO.Path.Combine(Settings.Dir, "gili.log"); } }

        public static void Write(string message)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(Settings.Dir);
                    var info = new FileInfo(Path);
                    if (info.Exists && info.Length > 256 * 1024) info.Delete();
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    /// <summary>Tiny key=value store in %APPDATA%\Gili\settings.ini.</summary>
    class Settings
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gili"); }
        }

        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public Settings() { Load(); }

        public void Load()
        {
            values.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Write("settings load: " + ex.Message); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# Gili settings. lat/lon/city = prayer location (edit, then tray > Muat ulang pengaturan).");
                var keys = new List<string>(values.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var k in keys) sb.AppendLine(k + "=" + values[k]);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("settings save: " + ex.Message); }
        }

        public bool Has(string key) { return values.ContainsKey(key) && values[key].Length > 0; }

        public string Get(string key, string fallback)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        public double GetDouble(string key, double fallback)
        {
            double v;
            return double.TryParse(Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public int GetInt(string key, int fallback)
        {
            int v;
            return int.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public long GetLong(string key, long fallback)
        {
            long v;
            return long.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            string v = Get(key, "");
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        public void Set(string key, object value)
        {
            if (value is bool) value = (bool)value ? "1" : "0";
            values[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Gili";

        public static bool Enabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else k.DeleteValue(Name, false);
                }
            }
        }
    }

    /// <summary>Damped spring used for every size, position and fade in the island.</summary>
    class Spring
    {
        public float Value, Velocity, Target;
        readonly float stiffness, damping, epsilon;

        public Spring(float value, float stiffness, float damping, float epsilon)
        {
            Value = Target = value;
            this.stiffness = stiffness;
            this.damping = damping;
            this.epsilon = epsilon;
        }

        public void Step(float dt)
        {
            if (Settled) { Value = Target; Velocity = 0; return; }
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004f)); // fixed sub-steps keep it stable
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float force = -stiffness * (Value - Target) - damping * Velocity;
                Velocity += force * h;
                Value += Velocity * h;
            }
        }

        public bool Settled { get { return Math.Abs(Value - Target) < epsilon && Math.Abs(Velocity) < epsilon * 4; } }

        public void Snap(float v) { Value = Target = v; Velocity = 0; }
    }

    static class Ease
    {
        public static float Clamp01(float t) { return t < 0 ? 0 : t > 1 ? 1 : t; }
        public static float OutCubic(float t) { t = Clamp01(t); float u = 1 - t; return 1 - u * u * u; }
        public static float InOut(float t) { t = Clamp01(t); return t * t * (3 - 2 * t); }
    }

    enum PomoMode { Focus, Short, Long }

    /// <summary>Pomodoro: 25/5 with a long break after every fourth focus session.</summary>
    class Pomodoro
    {
        public PomoMode Mode = PomoMode.Focus;
        public int FocusMin = 25, ShortMin = 5, LongMin = 15;
        public bool Running;
        public DateTime EndUtc;
        public double RemainingMs = 25 * 60000;
        public int DoneToday, Cycle;
        public string Day = "";
        public PomoMode LastCompleted;

        public int Minutes(PomoMode m) { return m == PomoMode.Focus ? FocusMin : m == PomoMode.Short ? ShortMin : LongMin; }
        public double TotalMs(PomoMode m) { return Minutes(m) * 60000.0; }
        public double TotalMs() { return TotalMs(Mode); }
        public double Remaining(DateTime nowUtc) { return Running ? Math.Max(0, (EndUtc - nowUtc).TotalMilliseconds) : RemainingMs; }
        public bool Active { get { return Running || RemainingMs < TotalMs() - 500; } }

        public void Start(DateTime nowUtc)
        {
            if (Running) return;
            if (RemainingMs <= 0) RemainingMs = TotalMs();
            EndUtc = nowUtc.AddMilliseconds(RemainingMs);
            Running = true;
        }

        public void Pause(DateTime nowUtc)
        {
            if (!Running) return;
            RemainingMs = Remaining(nowUtc);
            Running = false;
        }

        public void Reset() { Running = false; RemainingMs = TotalMs(); }

        public void SetMode(PomoMode m) { Mode = m; Reset(); }

        /// <summary>Jumps to the next phase without counting the current one.</summary>
        public void Skip()
        {
            Mode = Mode == PomoMode.Focus ? ((Cycle + 1) % 4 == 0 ? PomoMode.Long : PomoMode.Short) : PomoMode.Focus;
            Reset();
        }

        public void AdjustMinutes(int delta)
        {
            if (Running) return;
            if (Mode == PomoMode.Focus) FocusMin = Math.Max(5, Math.Min(90, FocusMin + delta));
            else if (Mode == PomoMode.Short) ShortMin = Math.Max(1, Math.Min(30, ShortMin + delta));
            else LongMin = Math.Max(5, Math.Min(60, LongMin + delta));
            RemainingMs = TotalMs();
        }

        /// <summary>Returns true when the running phase just finished.</summary>
        public bool Tick(DateTime nowUtc)
        {
            RollDay();
            if (!Running || nowUtc < EndUtc) return false;
            LastCompleted = Mode;
            Running = false;
            if (Mode == PomoMode.Focus)
            {
                DoneToday++;
                Cycle++;
                Mode = Cycle % 4 == 0 ? PomoMode.Long : PomoMode.Short;
            }
            else
            {
                Mode = PomoMode.Focus;
            }
            RemainingMs = TotalMs();
            return true;
        }

        void RollDay()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (Day == today) return;
            Day = today;
            DoneToday = 0;
            Cycle = 0;
        }

        public void Save(Settings s)
        {
            s.Set("pomo.mode", (int)Mode);
            s.Set("pomo.focus", FocusMin);
            s.Set("pomo.short", ShortMin);
            s.Set("pomo.long", LongMin);
            s.Set("pomo.running", Running);
            s.Set("pomo.end", EndUtc.Ticks);
            s.Set("pomo.remaining", (long)RemainingMs);
            s.Set("pomo.done", DoneToday);
            s.Set("pomo.cycle", Cycle);
            s.Set("pomo.day", Day);
        }

        public void Load(Settings s)
        {
            Mode = (PomoMode)Math.Max(0, Math.Min(2, s.GetInt("pomo.mode", 0)));
            FocusMin = s.GetInt("pomo.focus", 25);
            ShortMin = s.GetInt("pomo.short", 5);
            LongMin = s.GetInt("pomo.long", 15);
            Running = s.GetBool("pomo.running", false);
            EndUtc = new DateTime(Math.Max(0, s.GetLong("pomo.end", 0)), DateTimeKind.Utc);
            RemainingMs = s.GetLong("pomo.remaining", (long)TotalMs());
            if (RemainingMs <= 0 || RemainingMs > TotalMs()) RemainingMs = TotalMs();
            DoneToday = s.GetInt("pomo.done", 0);
            Cycle = s.GetInt("pomo.cycle", 0);
            Day = s.Get("pomo.day", "");
        }
    }

    /// <summary>Synthesised bell sounds, so no audio files ship with the app.</summary>
    static class Chime
    {
        static SoundPlayer player; // keeps the buffer alive while it plays

        public static void FocusDone() { Play(new[] { 659.25, 830.61, 987.77, 1318.51 }, 0.13, 3.2, 0.55); }
        public static void Adzan() { Play(new[] { 587.33, 880.0, 1174.66 }, 0.32, 1.8, 0.5); }
        public static void Meeting() { Play(new[] { 783.99, 1046.5, 783.99, 1046.5 }, 0.16, 4.0, 0.5); }

        static void Play(double[] notes, double spacing, double decay, double volume)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var p = new SoundPlayer(new MemoryStream(Synth(notes, spacing, decay, volume)));
                    p.Load();
                    p.Play();
                    player = p;
                }
                catch (Exception ex) { Log.Write("chime: " + ex.Message); }
            });
        }

        static byte[] Synth(double[] notes, double spacing, double decay, double volume)
        {
            const int rate = 44100;
            double length = spacing * (notes.Length - 1) + 2.2;
            int count = (int)(rate * length);
            var samples = new double[count];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * spacing * rate);
                for (int i = start; i < count; i++)
                {
                    double t = (double)(i - start) / rate;
                    double env = Math.Exp(-t * decay) * Math.Min(1.0, t / 0.004);
                    double f = notes[n];
                    samples[i] += env * (Math.Sin(2 * Math.PI * f * t) + 0.28 * Math.Sin(2 * Math.PI * f * 2.0 * t) * Math.Exp(-t * 3));
                }
            }
            double peak = 1e-9;
            foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
            for (int i = 0; i < count; i++) w.Write((short)(samples[i] / peak * volume * 32767));
            w.Flush();
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Which apps are using the microphone or camera right now. Reads the same registry data that
    /// drives the Windows privacy indicator and sleeps until that data changes.
    /// </summary>
    class PrivacyWatcher
    {
        public event Action<string[], string[]> Changed; // mic apps, camera apps

        const string Base = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        public void Start()
        {
            var t = new Thread(Run) { IsBackground = true, Name = "privacy" };
            t.Start();
        }

        void Run()
        {
            try
            {
                using (var mic = Registry.CurrentUser.OpenSubKey(Base + "microphone"))
                using (var cam = Registry.CurrentUser.OpenSubKey(Base + "webcam"))
                using (var micEvent = new AutoResetEvent(false))
                using (var camEvent = new AutoResetEvent(false))
                {
                    string last = null;
                    while (true)
                    {
                        const int filter = 0x1 | 0x4; // name | last-set
                        if (mic != null) Native.RegNotifyChangeKeyValue(mic.Handle, true, filter, micEvent.SafeWaitHandle, true);
                        if (cam != null) Native.RegNotifyChangeKeyValue(cam.Handle, true, filter, camEvent.SafeWaitHandle, true);

                        var micApps = Users("microphone");
                        var camApps = Users("webcam");
                        string sig = string.Join("|", micApps) + "#" + string.Join("|", camApps);
                        if (sig != last)
                        {
                            last = sig;
                            var h = Changed;
                            if (h != null) h(micApps, camApps);
                        }
                        WaitHandle.WaitAny(new WaitHandle[] { micEvent, camEvent });
                        Thread.Sleep(250); // writes come in bursts
                    }
                }
            }
            catch (Exception ex) { Log.Write("privacy: " + ex.Message); }
        }

        static string[] Users(string capability)
        {
            var names = new List<string>();
            Collect(Base + capability, names);
            Collect(Base + capability + @"\NonPackaged", names);
            return names.ToArray();
        }

        static void Collect(string path, List<string> names)
        {
            using (var root = Registry.CurrentUser.OpenSubKey(path))
            {
                if (root == null) return;
                foreach (string sub in root.GetSubKeyNames())
                {
                    if (sub == "NonPackaged") continue;
                    using (var k = root.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        object start = k.GetValue("LastUsedTimeStart"), stop = k.GetValue("LastUsedTimeStop");
                        if (start == null || stop == null) continue;
                        try
                        {
                            if (Convert.ToInt64(start) > 0 && Convert.ToInt64(stop) == 0)
                            {
                                string name = FriendlyName(sub);
                                if (!names.Contains(name)) names.Add(name);
                            }
                        }
                        catch (InvalidCastException) { }
                    }
                }
            }
        }

        static string FriendlyName(string key)
        {
            string name = key;
            int hash = name.LastIndexOf('#');
            if (hash >= 0)
            {
                name = name.Substring(hash + 1); // C:#Program Files#Zoom#bin#Zoom.exe
            }
            else
            {
                int us = name.IndexOf('_'); // Publisher.AppName_hash
                if (us > 0) name = name.Substring(0, us);
                name = name.Substring(name.LastIndexOf('.') + 1);
            }
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name.Substring(1);
            return name;
        }
    }
}
