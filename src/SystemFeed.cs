using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Gili
{
    /// <summary>What a device / network / power toast is about.</summary>
    class FeedEvent
    {
        public string Kind = "";     // wifiOn, wifiOff, noNet, ethOn, ethOff, headphones, speaker, usbIn, usbOut, monIn, monOut, on, off
        public string Title = "", Detail = "", Path;
        public int Code;
    }

    /// <summary>
    /// System happenings that get their own live activity: network, audio output, USB drives, monitors and
    /// battery saver. All of them arrive as Windows events.
    /// </summary>
    partial class IslandForm
    {
        readonly NetworkWatcher network = new NetworkWatcher();
        NetState netState;
        string lastAudioName;
        int monitorCount;
        bool saverOn, saverKnown;
        IntPtr powerNotifySaver;

        void StartFeeds()
        {
            network.Changed += s => Post(() => OnNetwork(s));
            network.Start();
            audio.DeviceChanged += (name, formFactor) => Post(() => OnAudioDevice(name, formFactor));
            monitorCount = Screen.AllScreens.Length;
            Guid saver = Native.PowerSavingStatusGuid;
            powerNotifySaver = Native.RegisterPowerSettingNotification(Handle, ref saver, 0);
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        void StopFeeds()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (powerNotifySaver != IntPtr.Zero) Native.UnregisterPowerSettingNotification(powerNotifySaver);
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) Post(() => SetMood(Mood.Surprised, 6)); // "oh, we're back!"
        }

        FeedEvent ShowFeed(View view, double seconds, string kind, string title, string detail, bool interactive)
        {
            var t = ShowToast(view, seconds, 2, interactive);
            if (t == null) return null;
            var e = new FeedEvent { Kind = kind, Title = title, Detail = detail ?? "" };
            t.Data = e;
            return e;
        }

        // ---- network -------------------------------------------------------------------------------

        void OnNetwork(NetState s)
        {
            var prev = netState;
            netState = s;
            if (prev == null || s.SameAs(prev)) return; // the first reading is the starting point, not news

            string name = s.Name.Length > 0 ? s.Name : prev.Name;
            if (s.Kind == NetKind.None)
            {
                if (prev.Kind == NetKind.Ethernet) ShowFeed(View.Network, 3.2, "ethOff", "Kabel LAN dicabut", prev.Name, false);
                else ShowFeed(View.Network, 3.4, "wifiOff", "Wi-Fi terputus", prev.Name, false);
                return;
            }
            bool newLink = s.Kind != prev.Kind || s.Name != prev.Name;
            if (!s.Internet && (newLink || prev.Internet))
            {
                ShowFeed(View.Network, 4, "noNet", "Tersambung, tanpa internet", name, false);
            }
            else if (s.Internet && (newLink || !prev.Internet))
            {
                bool lan = s.Kind == NetKind.Ethernet;
                ShowFeed(View.Network, 3.2, lan ? "ethOn" : "wifiOn",
                         lan ? "LAN tersambung" : !prev.Internet && !newLink ? "Internet kembali" : "Wi-Fi tersambung", name, false);
                SetMood(Mood.Happy, 20);
            }
        }

        bool Offline { get { return netState != null && (netState.Kind == NetKind.None || !netState.Internet); } }

        // ---- audio output --------------------------------------------------------------------------

        void OnAudioDevice(string name, int formFactor)
        {
            spectrum.Restart();
            if (string.IsNullOrEmpty(name) || name == lastAudioName) return;
            lastAudioName = name;
            // "Headphones (Galaxy Buds2)" -> kind "Headphones", device "Galaxy Buds2"
            string device = name;
            var m = Regex.Match(name, @"^(.*?)\s*\((.+)\)\s*$");
            if (m.Success) device = m.Groups[2].Value;
            bool ears = formFactor == 3 || formFactor == 5;
            ShowFeed(View.Device, 3.4, ears ? "headphones" : "speaker",
                     ears ? (formFactor == 5 ? "Headset tersambung" : "Headphone tersambung") : "Suara lewat speaker", device, false);
            if (ears) SetMood(Mood.Happy, 20);
        }

        // ---- USB drives & monitors ------------------------------------------------------------------

        void OnVolume(char[] letters, bool arrived)
        {
            if (letters == null || letters.Length == 0) return;
            char letter = letters[0];
            string root = letter + ":\\";
            if (!arrived)
            {
                ShowFeed(View.Device, 2.8, "usbOut", "Flashdisk dilepas", "(" + letter + ":)", false);
                return;
            }
            string label = "", title = "Drive terpasang";
            try
            {
                var di = new DriveInfo(root);
                if (di.DriveType == DriveType.Removable) title = "Flashdisk terpasang";
                if (di.IsReady) label = di.VolumeLabel;
            }
            catch (Exception ex) { Log.Write("drive info: " + ex.Message); }
            var e = ShowFeed(View.Device, 6, "usbIn", title, (label.Length > 0 ? label + " " : "") + "(" + letter + ":)", true);
            if (e != null) e.Path = root;
            SetMood(Mood.Surprised, 5);
        }

        void OpenDrive(string root)
        {
            try { Process.Start("explorer.exe", root); }
            catch (Exception ex) { Log.Write("open drive: " + ex.Message); }
            DismissToast();
        }

        void CheckMonitors()
        {
            int n = Screen.AllScreens.Length;
            if (n == monitorCount) return;
            bool more = n > monitorCount;
            monitorCount = n;
            ShowFeed(View.Device, 3.2, more ? "monIn" : "monOut", more ? "Monitor tersambung" : "Monitor dilepas", n + " layar", false);
            if (more) SetMood(Mood.Surprised, 5);
        }

        // ---- battery saver --------------------------------------------------------------------------

        void CheckSaver(bool on)
        {
            if (!saverKnown) { saverKnown = true; saverOn = on; return; }
            if (on == saverOn) return;
            saverOn = on;
            ShowFeed(View.Saver, 3.2, on ? "on" : "off", on ? "Hemat baterai aktif" : "Hemat baterai nonaktif",
                     on ? "Aktivitas latar dikurangi" : "", false);
        }

        static string TimeLeft(int seconds)
        {
            if (seconds <= 0) return "";
            int minutes = seconds / 60;
            return minutes >= 60 ? "sisa ~" + (minutes / 60) + " j " + (minutes % 60) + " m" : "sisa ~" + minutes + " menit";
        }
    }
}
