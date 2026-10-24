using System;
using System.Threading;
using Windows.Networking.Connectivity;

namespace Gili
{
    enum NetKind { None, Wifi, Ethernet, Cellular, Other }

    class NetState
    {
        public NetKind Kind;
        public string Name = "";    // Wi-Fi network name (SSID) or adapter profile name
        public bool Internet;

        public bool SameAs(NetState o)
        {
            return o != null && o.Kind == Kind && o.Name == Name && o.Internet == Internet;
        }
    }

    /// <summary>
    /// Wi-Fi / LAN / internet state from Windows (NetworkInformation.NetworkStatusChanged), so nothing is polled.
    /// Changes are settled for a moment first because a reconnect fires a burst of events.
    /// </summary>
    class NetworkWatcher
    {
        public event Action<NetState> Changed;
        Timer settle;

        public void Start()
        {
            settle = new Timer(delegate { Publish(); }, null, Timeout.Infinite, Timeout.Infinite);
            try { NetworkInformation.NetworkStatusChanged += delegate { settle.Change(1500, Timeout.Infinite); }; }
            catch (Exception ex) { Log.Write("network events: " + ex.Message); }
            settle.Change(0, Timeout.Infinite);
        }

        void Publish()
        {
            NetState s;
            try { s = Read(); }
            catch (Exception ex) { Log.Write("network read: " + ex.Message); return; }
            var h = Changed;
            if (h != null) h(s);
        }

        static NetState Read()
        {
            var state = new NetState();
            ConnectionProfile best = null;
            NetworkConnectivityLevel bestLevel = NetworkConnectivityLevel.None;
            var internet = NetworkInformation.GetInternetConnectionProfile();
            if (internet != null)
            {
                best = internet;
                bestLevel = internet.GetNetworkConnectivityLevel();
            }
            // Without internet the default profile may be missing; still report the Wi-Fi/LAN we are attached to.
            if (best == null || bestLevel == NetworkConnectivityLevel.None)
            {
                foreach (var p in NetworkInformation.GetConnectionProfiles())
                {
                    var level = p.GetNetworkConnectivityLevel();
                    if (level == NetworkConnectivityLevel.None) continue;
                    if (best == null || level > bestLevel) { best = p; bestLevel = level; }
                }
            }
            if (best == null || bestLevel == NetworkConnectivityLevel.None) return state;

            state.Internet = bestLevel == NetworkConnectivityLevel.InternetAccess;
            if (best.IsWlanConnectionProfile)
            {
                state.Kind = NetKind.Wifi;
                string ssid = null;
                try { ssid = best.WlanConnectionProfileDetails.GetConnectedSsid(); } catch { } // may need location permission
                state.Name = !string.IsNullOrEmpty(ssid) ? ssid : best.ProfileName ?? "";
            }
            else if (best.IsWwanConnectionProfile)
            {
                state.Kind = NetKind.Cellular;
                state.Name = best.ProfileName ?? "";
            }
            else
            {
                uint iana = 0;
                try { if (best.NetworkAdapter != null) iana = best.NetworkAdapter.IanaInterfaceType; } catch { }
                state.Kind = iana == 6 ? NetKind.Ethernet : iana == 71 ? NetKind.Wifi : NetKind.Other;
                state.Name = best.ProfileName ?? "";
            }
            return state;
        }
    }
}
