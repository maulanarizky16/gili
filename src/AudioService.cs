using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Gili
{
    /// <summary>
    /// Master volume of the default output device. Changes arrive through Core Audio callbacks,
    /// so nothing is polled. All COM work happens on MTA thread-pool threads.
    /// </summary>
    class AudioService
    {
        public event Action<float, bool, bool> VolumeChanged; // level 0..1, muted, initial
        public event Action<string, int> DeviceChanged; // friendly name, form factor (3 headphones, 5 headset, 1 speakers)
        string deviceName = "";
        int deviceFormFactor = 10;

        readonly object sync = new object();
        IMMDeviceEnumerator enumerator;
        IAudioEndpointVolume endpoint;
        VolumeCallback callback;
        DeviceNotifier notifier;
        bool sentInitial;
        float pendingLevel = -1f;
        int pendingWork;

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                    notifier = new DeviceNotifier(this);
                    enumerator.RegisterEndpointNotificationCallback(notifier);
                    Attach();
                }
                catch (Exception ex) { Log.Write("audio init: " + ex.Message); }
            });
        }

        void Attach()
        {
            lock (sync)
            {
                if (endpoint != null)
                {
                    try { endpoint.UnregisterControlChangeNotify(callback); } catch { }
                    Marshal.ReleaseComObject(endpoint);
                    endpoint = null;
                }
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return;
                try
                {
                    object o;
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out o));
                    endpoint = (IAudioEndpointVolume)o;
                }
                finally
                {
                    try { DeviceProps.Read(device, out deviceName, out deviceFormFactor); }
                    catch (Exception ex) { Log.Write("audio device props: " + ex.Message); }
                    Marshal.ReleaseComObject(device);
                }

                if (callback == null) callback = new VolumeCallback(this);
                endpoint.RegisterControlChangeNotify(callback);
                float level; bool muted;
                endpoint.GetMasterVolumeLevelScalar(out level);
                endpoint.GetMute(out muted);
                Raise(level, muted, !sentInitial);
                sentInitial = true;
            }
        }

        void Raise(float level, bool muted, bool initial)
        {
            var h = VolumeChanged;
            if (h != null) h(level, muted, initial);
        }

        /// <summary>Sets the master volume. Rapid calls (mouse wheel) collapse into the latest value.</summary>
        public void SetVolume(float level)
        {
            lock (sync) pendingLevel = Math.Max(0f, Math.Min(1f, level));
            if (Interlocked.Exchange(ref pendingWork, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Interlocked.Exchange(ref pendingWork, 0);
                lock (sync)
                {
                    if (endpoint == null || pendingLevel < 0) return;
                    Guid ctx = Guid.Empty;
                    endpoint.SetMasterVolumeLevelScalar(pendingLevel, ref ctx);
                    if (pendingLevel > 0) endpoint.SetMute(false, ref ctx);
                    pendingLevel = -1f;
                }
            });
        }

        public void ToggleMute()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (sync)
                {
                    if (endpoint == null) return;
                    bool muted;
                    endpoint.GetMute(out muted);
                    Guid ctx = Guid.Empty;
                    endpoint.SetMute(!muted, ref ctx);
                }
            });
        }

        internal void OnNotify(IntPtr data)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID context (16), BOOL muted (4), float master (4), ...
            bool muted = Marshal.ReadInt32(data, 16) != 0;
            float level = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
            Raise(level, muted, false);
        }

        internal void OnDefaultDeviceChanged()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(150);
                try { Attach(); } catch (Exception ex) { Log.Write("audio reattach: " + ex.Message); }
                var h = DeviceChanged;
                if (h != null) h(deviceName, deviceFormFactor);
            });
        }

        class VolumeCallback : IAudioEndpointVolumeCallback
        {
            readonly AudioService owner;
            public VolumeCallback(AudioService owner) { this.owner = owner; }
            public int OnNotify(IntPtr notifyData)
            {
                try { owner.OnNotify(notifyData); } catch { }
                return 0;
            }
        }

        class DeviceNotifier : IMMNotificationClient
        {
            readonly AudioService owner;
            public DeviceNotifier(AudioService owner) { this.owner = owner; }
            public int OnDeviceStateChanged(string id, int state) { return 0; }
            public int OnDeviceAdded(string id) { return 0; }
            public int OnDeviceRemoved(string id) { return 0; }
            public int OnPropertyValueChanged(string id, PropertyKey key) { return 0; }
            public int OnDefaultDeviceChanged(int flow, int role, string id)
            {
                if (flow == 0 && role == 1) owner.OnDefaultDeviceChanged(); // render, multimedia
                return 0;
            }
        }
    }

    /// <summary>
    /// Real audio spectrum: captures what the speakers play (WASAPI loopback), runs an FFT and
    /// exposes log-spaced bands in 0..1. The capture thread only runs while the visualizer is on screen.
    /// </summary>
    class Spectrum
    {
        public const int Bands = 24;
        const int N = 2048;
        const float MinHz = 45f, MaxHz = 14000f;

        readonly float[] bands = new float[Bands];
        readonly float[] ring = new float[N];
        readonly float[] re = new float[N], im = new float[N], window = new float[N];
        readonly int[] bitrev = new int[N];
        readonly float[] cosT = new float[N / 2], sinT = new float[N / 2];
        readonly ManualResetEvent wake = new ManualResetEvent(false);
        float[] buffer = new float[4096];
        int ringPos;
        float trackedPeakDb = -60f;
        volatile bool wanted, restart;
        Thread thread;
        int failures;

        /// <summary>True once loopback capture proved unusable; callers then fake the bars.</summary>
        public bool Failed { get; private set; }

        public Spectrum()
        {
            int bits = (int)Math.Log(N, 2);
            for (int i = 0; i < N; i++)
            {
                window[i] = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * i / (N - 1));
                int r = 0;
                for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                bitrev[i] = r;
            }
            for (int i = 0; i < N / 2; i++)
            {
                cosT[i] = (float)Math.Cos(-2 * Math.PI * i / N);
                sinT[i] = (float)Math.Sin(-2 * Math.PI * i / N);
            }
        }

        public bool Active { get { return wanted; } }

        public void SetActive(bool on)
        {
            if (on == wanted || Failed) return;
            wanted = on;
            if (on)
            {
                if (thread == null)
                {
                    thread = new Thread(Run) { IsBackground = true, Name = "spectrum" };
                    thread.SetApartmentState(ApartmentState.MTA);
                    thread.Start();
                }
                wake.Set();
            }
            else
            {
                wake.Reset();
            }
        }

        public void Restart() { restart = true; }

        public void CopyBands(float[] dst)
        {
            lock (bands) Array.Copy(bands, dst, Math.Min(dst.Length, Bands));
        }

        void Run()
        {
            while (true)
            {
                wake.WaitOne();
                try
                {
                    Capture();
                    failures = 0;
                }
                catch (Exception ex)
                {
                    Log.Write("spectrum: " + ex.Message);
                    if (++failures >= 3) { Failed = true; wanted = false; wake.Reset(); }
                    Thread.Sleep(1000);
                }
                lock (bands) Array.Clear(bands, 0, Bands);
            }
        }

        void Capture()
        {
            IMMDeviceEnumerator en = null;
            IMMDevice device = null;
            IAudioClient client = null;
            IAudioCaptureClient capture = null;
            IntPtr format = IntPtr.Zero;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 1, out device));
                object o;
                Guid iidClient = typeof(IAudioClient).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iidClient, 23, IntPtr.Zero, out o));
                client = (IAudioClient)o;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out format));

                int tag = Marshal.ReadInt16(format, 0) & 0xFFFF;
                int channels = Marshal.ReadInt16(format, 2);
                int rate = Marshal.ReadInt32(format, 4);
                int bitsPerSample = Marshal.ReadInt16(format, 14);
                bool isFloat = tag == 3;
                bool isPcm = tag == 1;
                if (tag == 0xFFFE) // WAVE_FORMAT_EXTENSIBLE: sub-format GUID sits at offset 24
                {
                    byte[] g = new byte[16];
                    Marshal.Copy(new IntPtr(format.ToInt64() + 24), g, 0, 16);
                    Guid sub = new Guid(g);
                    isFloat = sub == new Guid("00000003-0000-0010-8000-00aa00389b71");
                    isPcm = sub == new Guid("00000001-0000-0010-8000-00aa00389b71");
                }
                if (!(isFloat && bitsPerSample == 32) && !(isPcm && bitsPerSample == 16))
                    throw new NotSupportedException("mix format " + tag + "/" + bitsPerSample);

                const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
                Marshal.ThrowExceptionForHR(client.Initialize(0, AUDCLNT_STREAMFLAGS_LOOPBACK, 2000000, 0, format, IntPtr.Zero));
                Guid iidCapture = typeof(IAudioCaptureClient).GUID;
                Marshal.ThrowExceptionForHR(client.GetService(ref iidCapture, out o));
                capture = (IAudioCaptureClient)o;
                Marshal.ThrowExceptionForHR(client.Start());
                restart = false;

                DateTime lastPacket = DateTime.UtcNow, lastFft = DateTime.MinValue;
                short[] pcm = null;
                while (wanted && !restart)
                {
                    Thread.Sleep(12);
                    uint packet;
                    Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out packet));
                    while (packet > 0)
                    {
                        IntPtr data; uint frames, flags; ulong pos, qpc;
                        Marshal.ThrowExceptionForHR(capture.GetBuffer(out data, out frames, out flags, out pos, out qpc));
                        int samples = (int)frames * channels;
                        if (buffer.Length < samples) buffer = new float[samples];
                        if ((flags & 2) != 0) // AUDCLNT_BUFFERFLAGS_SILENT
                        {
                            Array.Clear(buffer, 0, samples);
                        }
                        else if (isFloat)
                        {
                            Marshal.Copy(data, buffer, 0, samples);
                        }
                        else
                        {
                            if (pcm == null || pcm.Length < samples) pcm = new short[samples];
                            Marshal.Copy(data, pcm, 0, samples);
                            for (int i = 0; i < samples; i++) buffer[i] = pcm[i] / 32768f;
                        }
                        capture.ReleaseBuffer(frames);
                        for (int f = 0; f < frames; f++)
                        {
                            float sum = 0;
                            for (int c = 0; c < channels; c++) sum += buffer[f * channels + c];
                            ring[ringPos] = sum / channels;
                            ringPos = (ringPos + 1) & (N - 1);
                        }
                        lastPacket = DateTime.UtcNow;
                        Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out packet));
                    }

                    var now = DateTime.UtcNow;
                    if ((now - lastPacket).TotalMilliseconds > 120) Array.Clear(ring, 0, N); // nothing is playing
                    if ((now - lastFft).TotalMilliseconds >= 30)
                    {
                        lastFft = now;
                        Analyze(rate);
                    }
                }
                client.Stop();
            }
            finally
            {
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                if (capture != null) Marshal.ReleaseComObject(capture);
                if (client != null) Marshal.ReleaseComObject(client);
                if (device != null) Marshal.ReleaseComObject(device);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        void Analyze(int rate)
        {
            for (int i = 0; i < N; i++)
            {
                int j = bitrev[i];
                re[j] = ring[(ringPos + i) & (N - 1)] * window[i];
                im[j] = 0;
            }
            for (int size = 2; size <= N; size <<= 1)
            {
                int half = size >> 1, step = N / size;
                for (int start = 0; start < N; start += size)
                {
                    for (int k = 0; k < half; k++)
                    {
                        float wr = cosT[k * step], wi = sinT[k * step];
                        int a = start + k, b = a + half;
                        float tr = re[b] * wr - im[b] * wi;
                        float ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                    }
                }
            }

            float binHz = (float)rate / N;
            float[] db = new float[Bands];
            float frameMax = -120f;
            for (int k = 0; k < Bands; k++)
            {
                float f0 = MinHz * (float)Math.Pow(MaxHz / MinHz, (double)k / Bands);
                float f1 = MinHz * (float)Math.Pow(MaxHz / MinHz, (double)(k + 1) / Bands);
                int b0 = Math.Max(1, (int)(f0 / binHz));
                int b1 = Math.Max(b0 + 1, Math.Min(N / 2, (int)Math.Ceiling(f1 / binHz)));
                float peak = 0;
                for (int b = b0; b < b1; b++)
                {
                    float m = re[b] * re[b] + im[b] * im[b];
                    if (m > peak) peak = m;
                }
                float amp = (float)Math.Sqrt(peak) * 4f / N; // Hann window coherent gain 0.5
                float centre = (float)Math.Sqrt(f0 * f1);
                // Music falls off ~3 dB/octave; tilt it back so treble bars move too.
                db[k] = 20f * (float)Math.Log10(amp + 1e-9f) + 3f * (float)(Math.Log(centre / 1000f, 2));
                if (db[k] > frameMax) frameMax = db[k];
            }

            // Automatic gain: follow the loudest band quickly, release slowly.
            trackedPeakDb = frameMax > trackedPeakDb ? frameMax : Math.Max(-60f, trackedPeakDb - 0.12f);
            const float Range = 40f;
            bool silent = frameMax < -80f;
            lock (bands)
            {
                for (int k = 0; k < Bands; k++)
                {
                    float v = silent ? 0 : (db[k] - (trackedPeakDb - Range)) / Range;
                    v = Math.Max(0, Math.Min(1, v));
                    v = v * v * (3 - 2 * v); // smoothstep for punchier motion
                    float cur = bands[k];
                    bands[k] = v > cur ? cur + (v - cur) * 0.7f : cur + (v - cur) * 0.22f;
                }
            }
        }
    }
}
