using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using WinTimer = System.Windows.Forms.Timer;

namespace Gili
{
    enum Mood { Neutral, Happy, Sad, Sleepy, Asleep, Surprised }

    /// <summary>
    /// The mascot: a pair of eyes that takes over the quiet island. They follow the mouse, blink, and change mood
    /// with what is going on: happy when the charger or Wi-Fi connects, worried on low battery or no internet,
    /// sleepy late at night or when you have been away, surprised when a drive or monitor appears.
    /// </summary>
    partial class IslandForm
    {
        readonly Spring eyeOpen = new Spring(1, 520, 34, 0.01f), eyeLookX = new Spring(0, 140, 20, 0.01f), eyeLookY = new Spring(0, 140, 20, 0.01f);
        readonly Spring eyeHappy = new Spring(0, 240, 26, 0.01f), eyeSad = new Spring(0, 200, 24, 0.01f), eyeBig = new Spring(1, 320, 16, 0.005f);
        // brows: raise (-1 low .. 1.2 high), tilt (+1 = inner ends up, a worried look); blush 0..1
        readonly Spring browRaise = new Spring(0, 260, 22, 0.01f), browTilt = new Spring(0, 260, 22, 0.01f), blush = new Spring(0.25f, 120, 20, 0.005f);
        readonly WinTimer eyeTimer = new WinTimer { Interval = 70 };
        readonly Random rng = new Random();
        Mood tempMood;
        DateTime tempMoodUntil, nextBlinkUtc, blinkEndUtc, lastLivelyUtc = DateTime.UtcNow, eyesSinceUtc;
        double userIdleSeconds;
        Mood lastMood = Mood.Neutral;

        bool MascotEnabled { get { return settings.GetBool("mascot", true); } }

        void StartMascot()
        {
            eyeTimer.Tick += delegate { EyeTick(); };
            eyeTimer.Start();
        }

        void SetMood(Mood m, double seconds)
        {
            tempMood = m;
            tempMoodUntil = DateTime.UtcNow.AddSeconds(seconds);
            Kick();
        }

        /// <summary>Something happened on the island: the eyes wait a while before taking over again.</summary>
        void MarkLively() { lastLivelyUtc = DateTime.UtcNow; }

        Mood CurrentMood()
        {
            if (DateTime.UtcNow < tempMoodUntil) return tempMood;
            if (userIdleSeconds > 600) return Mood.Asleep;
            int hour = DateTime.Now.Hour;
            if (userIdleSeconds > 180 || hour >= 23 || hour < 4) return Mood.Sleepy;
            if ((hasBattery && !onAc && battery <= 20) || Offline) return Mood.Sad;
            return Mood.Neutral;
        }

        bool EyesWanted()
        {
            return MascotEnabled && !pointerInside && (DateTime.UtcNow - lastLivelyUtc).TotalSeconds > 12;
        }

        void EyeTick()
        {
            if (current != View.Eyes) { eyesSinceUtc = DateTime.MinValue; return; }
            var now = DateTime.UtcNow;
            if (eyesSinceUtc == DateTime.MinValue) eyesSinceUtc = now;
            var mood = CurrentMood();
            eyeHappy.Target = mood == Mood.Happy ? 1 : 0;
            eyeSad.Target = mood == Mood.Sad ? 1 : 0;
            eyeBig.Target = mood == Mood.Surprised ? 1.28f : 1f;
            switch (mood)
            {
                case Mood.Happy: browRaise.Target = 0.6f; browTilt.Target = 0; blush.Target = 0.8f; break;
                case Mood.Sad: browRaise.Target = 0.15f; browTilt.Target = 1; blush.Target = 0.05f; break;
                case Mood.Surprised: browRaise.Target = 1.2f; browTilt.Target = 0; blush.Target = 0.45f; break;
                case Mood.Sleepy: browRaise.Target = -0.7f; browTilt.Target = -0.25f; blush.Target = 0.2f; break;
                case Mood.Asleep: browRaise.Target = -0.9f; browTilt.Target = 0; blush.Target = 0.3f; break;
                default: browRaise.Target = 0; browTilt.Target = 0; blush.Target = 0.28f; break;
            }
            float open = mood == Mood.Sleepy ? 0.42f : mood == Mood.Asleep ? 0.07f : 1f;
            if (mood != Mood.Asleep && now >= nextBlinkUtc)
            {
                blinkEndUtc = now.AddMilliseconds(mood == Mood.Sleepy ? 420 : 130);
                nextBlinkUtc = now.AddMilliseconds(rng.Next(2200, 6200));
            }
            if (now < blinkEndUtc) open = 0.06f;
            eyeOpen.Target = open;

            // look towards the mouse pointer
            float lx = 0, ly = 0;
            Native.POINT pt;
            if (mood != Mood.Asleep && Native.GetCursorPos(out pt))
            {
                var r = IslandScreenRect();
                lx = Math.Max(-1, Math.Min(1, (pt.X - (r.X + r.Width / 2f)) / (320 * scale)));
                ly = Math.Max(-1, Math.Min(1, (pt.Y - (r.Y + r.Height / 2f)) / (220 * scale)));
            }
            eyeLookX.Target = lx;
            eyeLookY.Target = ly;

            bool moving = !eyeOpen.Settled || !eyeLookX.Settled || !eyeLookY.Settled || !eyeHappy.Settled || !eyeSad.Settled || !eyeBig.Settled ||
                          !browRaise.Settled || !browTilt.Settled || !blush.Settled;
            if (moving || mood != lastMood || mood == Mood.Asleep)
            {
                lastMood = mood;
                if (!frameTimer.Enabled) Kick();
            }
        }

        bool EyeSpringsMoving()
        {
            return current == View.Eyes &&
                   (!eyeOpen.Settled || !eyeLookX.Settled || !eyeLookY.Settled || !eyeHappy.Settled || !eyeSad.Settled || !eyeBig.Settled ||
                    !browRaise.Settled || !browTilt.Settled || !blush.Settled);
        }

        void StepEyes(float dt)
        {
            eyeOpen.Step(dt); eyeLookX.Step(dt); eyeLookY.Step(dt); eyeHappy.Step(dt); eyeSad.Step(dt); eyeBig.Step(dt);
            browRaise.Step(dt); browTilt.Step(dt); blush.Step(dt);
        }

        static readonly Color EyeTop = Color.FromArgb(255, 214, 196, 255), EyeBottom = Color.FromArgb(255, 146, 112, 255);
        static readonly Color EyeGlow = Color.FromArgb(255, 150, 110, 255), BrowColor = Color.FromArgb(255, 200, 182, 255);
        static readonly Color BlushColor = Color.FromArgb(255, 255, 118, 176);

        void DrawEyes(float w, float h, DateTime now)
        {
            float big = eyeBig.Value, happy = Ease.Clamp01(eyeHappy.Value), sad = Ease.Clamp01(eyeSad.Value);
            float cx = w / 2 + eyeLookX.Value * 4.5f, cy = h / 2 + 2 + eyeLookY.Value * 2.2f;
            float ew = 9f * big, eh = 13f * big, gap = 14f;
            float open = Math.Max(0.05f, Math.Min(1f, eyeOpen.Value));
            float a0 = paint.Alpha;

            for (int side = -1; side <= 1; side += 2)
            {
                float ex = cx + side * gap;
                float hh = Math.Max(1.8f, eh * open);
                var eye = new RectangleF(ex - ew / 2, cy - hh / 2, ew, hh);
                float radius = Math.Min(ew, hh) / 2 * (big > 1.1f ? 1f : 0.9f);

                // blush on the cheeks, below and outside the eye
                float bl = Ease.Clamp01(blush.Value);
                if (bl > 0.01f)
                {
                    float bx = ex + side * ew * 0.85f, by = cy + eh * 0.5f + 2.5f;
                    paint.Alpha = a0 * bl * 0.35f;
                    paint.G.FillEllipse(paint.B(BlushColor), bx - 6.5f, by - 3.5f, 13, 7);
                    paint.Alpha = a0 * bl * 0.55f;
                    paint.G.FillEllipse(paint.B(BlushColor), bx - 4.2f, by - 2.2f, 8.4f, 4.4f);
                    paint.Alpha = a0;
                }

                // soft purple glow around the eye
                for (int i = 3; i >= 1; i--)
                {
                    paint.Alpha = a0 * (0.10f + 0.05f * (3 - i)) * (1 - happy * 0.4f);
                    var halo = RectangleF.Inflate(eye, i * 2.2f, i * 2.2f);
                    paint.FillRound(halo, radius + i * 2.2f, EyeGlow);
                }
                paint.Alpha = a0;

                var state = g.Save();
                if (sad > 0.01f) // drooping lid on the outer side: a worried look
                {
                    float outer = ex + side * ew / 2, inner = ex - side * ew / 2;
                    using (var lid = new GraphicsPath())
                    {
                        lid.AddPolygon(new[] {
                            new PointF(inner - side * 2, eye.Y - 2), new PointF(outer + side * 2, eye.Y - 2),
                            new PointF(outer + side * 2, eye.Y + hh * 0.58f * sad), new PointF(inner - side * 2, eye.Y + hh * 0.1f * sad) });
                        g.SetClip(lid, CombineMode.Exclude);
                    }
                }
                if (happy < 0.99f)
                {
                    using (var path = Theme.Round(eye, radius))
                    using (var fill = new LinearGradientBrush(RectangleF.Inflate(eye, 0, 1), Theme.WithAlpha(EyeTop, a0 * (1 - happy)),
                                                              Theme.WithAlpha(EyeBottom, a0 * (1 - happy)), 90f))
                        g.FillPath(fill, path);
                    if (open > 0.55f) // a tiny catch-light makes them look alive
                    {
                        paint.Alpha = a0 * (1 - happy) * 0.9f;
                        paint.FillCircle(eye.X + ew * 0.32f, eye.Y + hh * 0.26f, 1.5f * big, Color.White);
                        paint.Alpha = a0;
                    }
                }
                if (happy > 0.01f) // ^ ^
                {
                    paint.Alpha = a0 * happy;
                    g.DrawArc(paint.P(EyeTop, 2.8f * big), ex - ew * 0.6f, cy - eh * 0.12f, ew * 1.2f, eh * 0.75f, 200, 140);
                    paint.Alpha = a0;
                }
                g.Restore(state);

                // eyebrow: an arc from the outer to the inner end; the inner end lifts when worried
                float raise = browRaise.Value, tilt = browTilt.Value;
                float baseY = cy - eh * 0.5f - 4.5f - raise * 3f;
                float outerX = ex + side * ew * 0.8f, innerX = ex - side * ew * 0.55f;
                float outerY = baseY + tilt * 1.6f, innerY = baseY - tilt * 3f;
                float arch = 1.6f + Math.Max(0, raise) * 1.2f - Math.Max(0, tilt) * 1.2f;
                paint.Alpha = a0 * 0.95f;
                g.DrawBezier(paint.P(BrowColor, 1.9f), outerX, outerY, outerX - side * ew * 0.35f, outerY - arch,
                             innerX + side * ew * 0.35f, innerY - arch, innerX, innerY);
                paint.Alpha = a0;
            }

            if (CurrentMood() == Mood.Asleep) // z z z drifting up
            {
                float t = (float)(now - eyesSinceUtc).TotalSeconds;
                for (int i = 0; i < 3; i++)
                {
                    float phase = (t * 0.45f + i / 3f) % 1f;
                    float za = paint.Alpha;
                    paint.Alpha = za * (float)Math.Sin(phase * Math.PI) * 0.85f;
                    paint.TextCenter("z", Theme.Semi(7.5f + phase * 4.5f), EyeTop, w / 2 + 30 + phase * 9, h / 2 - 2 - phase * 9);
                    paint.Alpha = za;
                }
            }
        }
    }
}
