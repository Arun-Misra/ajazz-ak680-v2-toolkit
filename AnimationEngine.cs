using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace AjazzManager;

public enum AnimationPattern
{
    RainbowWave, Breathing, Chase, Ripple, Sparkle,
    MatrixRain, Fire, Checkerboard, KittScanner, Aurora, ColorFade, ClickReactive,
    Plasma, DiagonalWave, Spiral, Meteor, ColorWipe, BreathingRainbow, Equalizer, TypingTrail
}

public enum ClickEffect { Ripple, Flash, Spread, SparkleBurst }

/// <summary>
/// Host-side animation engine. Generates per-key RGB frames on a dedicated thread and
/// streams them to the keyboard with setCustomLEDData.
///
/// Two important details:
///  - LED ids are NOT laid out as a 15-column grid. Physical row/column comes from
///    OrderedLeds, otherwise click bursts and spatial effects land on unrelated keys.
///  - Stop/Start uses a generation token and joins the old thread before launching a new
///    one, so rapid pattern changes cannot leave two writer loops racing on the HID endpoint.
/// </summary>
public sealed class AnimationEngine
{
    // Physical key order: 15 + 15 + 14 + 14 + 10 keys.
    public static readonly int[] OrderedLeds =
    {
        0,17,18,19,20,21,22,23,24,25,26,27,28,92,104,
        32,33,34,35,36,37,38,39,40,41,42,43,44,60,106,
        48,49,50,51,52,53,54,55,56,57,58,59,76,105,
        64,65,66,67,68,69,70,71,72,73,74,75,90,108,
        80,81,82,83,84,85,87,88,89,91
    };
    private static readonly int[] RowLengths = { 15, 15, 14, 14, 10 };

    private static readonly Dictionary<int, (int row, int col)> RcByLed = BuildRcMap();
    private static readonly Dictionary<int, int> PosByLed = BuildPosMap();

    private static Dictionary<int, (int, int)> BuildRcMap()
    {
        var map = new Dictionary<int, (int, int)>();
        int row = 0, col = 0;
        foreach (int led in OrderedLeds)
        {
            if (col >= RowLengths[row]) { row++; col = 0; }
            if (row >= RowLengths.Length) row = RowLengths.Length - 1;
            map[led] = (row, col);
            col++;
        }
        return map;
    }
    private static Dictionary<int, int> BuildPosMap()
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < OrderedLeds.Length; i++) map[OrderedLeds[i]] = i;
        return map;
    }
    private static (int row, int col) GetRc(int led) =>
        RcByLed.TryGetValue(led, out var rc) ? rc : (0, 0);
    private static int GetPos(int led) =>
        PosByLed.TryGetValue(led, out int p) ? p : 0;
    private static int GridWidth => RowLengths[0];

    // For each key, the nearby LED ids grouped into contiguous runs so a burst can
    // update only its neighbourhood with a few HID packets instead of all 128 keys.
    private readonly DeviceService _device;
    private readonly Dispatcher _dispatcher;
    private Thread? _thread;
    private volatile bool _running;
    private int _generation;
    private int _targetFps = 30;
    private double _speed = 1.0;
    private AnimationPattern _pattern;
    private Color _primary, _secondary;

    private sealed class Burst
    {
        public int Center;
        public double Start;
        public Color Color;
        public ClickEffect Effect;
    }
    private readonly List<Burst> _bursts = new();
    private readonly object _burstLock = new();

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

    /// <summary>Multiplies pattern time (0.25 = slow, 3 = fast). Live while running.</summary>
    public double Speed { get => _speed; set => _speed = Math.Clamp(value, 0.25, 3.0); }

    /// <summary>Render through the firmware's cmd 0x32 path in music mode 100.
    /// This is the smooth path: the firmware keeps rendering from the streamed data
    /// instead of stalling on host-paced 0x24 custom-LED frames. Set false to fall
    /// back to mode 128 for diagnostics.</summary>
    public bool UseMusicPath { get; set; } = true;

    public bool IsRunning => _running;
    public long FramesRendered { get; private set; }
    public event Action<string>? Status;

    public AnimationEngine(DeviceService device)
    {
        _device = device;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>Starts (or restarts) the animation loop safely.</summary>
    public void Start(AnimationPattern pattern, int fps, Color primary, Color secondary) =>
        _ = StartCore(pattern, fps, primary, secondary);

    private async System.Threading.Tasks.Task StartCore(
        AnimationPattern pattern, int fps, Color primary, Color secondary)
    {
        int gen = Interlocked.Increment(ref _generation);
        StopLoop(wait: true);

        if (!_device.IsConnected && !_device.Connect())
        {
            Report("No device connected");
            return;
        }
        _targetFps = Math.Clamp(fps, 1, 120);
        var effect = new LedEffect { Mode = (byte)(UseMusicPath ? 100 : 128), ColorMode = 0,
            Red = primary.R, Green = primary.G, Blue = primary.B,
            Brightness = 6, Speed = 3, DriverSetting = 255 };
        if (!await _device.SetLedEffectAsync(effect))
        {
            Report("Failed to enter custom mode");
            return;
        }
        // One-time clear so the previous pattern's colours do not linger. This must
        // use the same command path as the frame loop, otherwise a click burst that
        // starts the engine in mode 100 would clear the wrong LED buffer.
        var clear = new List<LedColor>(128);
        for (int i = 0; i < 128; i++) clear.Add(new LedColor { LedId = i });
        if (UseMusicPath)
            _device.SendMusicFrameAsync(clear.Where(c => OrderedLeds.Contains(c.LedId)).ToArray());
        else
            _device.SendCustomFrameAsync(clear);
        if (gen != _generation) return;

        _pattern = pattern;
        _primary = primary;
        _secondary = secondary;
        lock (_burstLock) _bursts.Clear();

        timeBeginPeriod(1);
        _running = true;
        _thread = new Thread(() => FrameLoop(gen)) { IsBackground = true };
        _thread.Start();
        Report($"Playing {pattern} at up to {_targetFps} FPS");
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        StopLoop(wait: true);
        Report("Animation stopped");
    }

    private void StopLoop(bool wait)
    {
        _running = false;
        var old = _thread;
        if (wait && old != null && old.IsAlive && old != Thread.CurrentThread)
        {
            try { old.Join(300); } catch { }
        }
        _thread = null;
        timeEndPeriod(1);
    }

    /// <summary>
    /// Fires a one-shot effect centred on a key. When idle, the black Click Reactive
    /// loop is fully initialised (awaited) before the burst is queued.
    /// </summary>
    public async void TriggerKey(int ledId, ClickEffect effect, Color color)
    {
        if (!_running)
            await StartCore(AnimationPattern.ClickReactive, 30, color, color);
        _lastPress[ledId] = _clock.Elapsed.TotalSeconds;
        var (pr, pc) = GetRc(ledId);
        foreach (var other in PosByLed.Keys)
        {
            var (r, c) = GetRc(other);
            if (Math.Max(Math.Abs(r - pr), Math.Abs(c - pc)) <= 1)
                _lastPress[other] = _clock.Elapsed.TotalSeconds;
        }
        var burst = new Burst
        {
            Center = ledId,
            Start = _clock.Elapsed.TotalSeconds,
            Color = color,
            Effect = effect
        };
        lock (_burstLock)
        {
            _bursts.Add(burst);
            if (_bursts.Count > 12) _bursts.RemoveAt(0);
        }
        // No immediate full-frame write here: that used to push 10 HID packets and
        // blank the whole pattern on every press. The frame loop picks the burst up
        // within one frame (<=33ms) and updates only its neighbourhood.
        Report($"Click {effect} at key {ledId}");
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double[] _lastPress = new double[128];

    private Color ApplyBursts(int id, double t, Color baseColor)
    {
        // Never light unused LED slots — they have no physical key position.
        if (!PosByLed.ContainsKey(id)) return baseColor;
        lock (_burstLock)
        {
            if (_bursts.Count == 0) return baseColor;
            _bursts.RemoveAll(b => t - b.Start > 1.4);
        }
        Burst[] bursts;
        lock (_burstLock) bursts = _bursts.ToArray();

        Color result = baseColor;
        var (row, col) = GetRc(id);
        foreach (var b in bursts)
        {
            double age = t - b.Start;
            if (age < 0 || age > 1.6) continue;
            var (brow, bcol) = GetRc(b.Center);
            // Chebyshev distance = exact grid neighbours (no diagonal bleed into
            // keys that only look close because of irregular LED numbering).
            double dist = Math.Max(Math.Abs(row - brow), Math.Abs(col - bcol));
            double intensity = b.Effect switch
            {
                // Effects are tuned to stay within the 5-key neighbourhood we transmit,
                // so a ripple cannot be clipped by the region boundary.
                ClickEffect.Ripple => Math.Max(0, 1 - Math.Abs(dist - age * 2.8) / 1.6) * (1 - Math.Pow(age / 1.3, 2)),
                ClickEffect.Flash => dist <= 1 ? Math.Pow(1 - Math.Min(1, age / 0.7), 2) : 0,
                ClickEffect.Spread => Math.Max(0, 1 - dist / (1 + age * 3)) * (1 - Math.Pow(age / 1.3, 2)),
                ClickEffect.SparkleBurst => (((id * 31 + (int)(t * 40)) % 53) < 5 && dist <= 3) ? (1 - age / 1.1) : 0,
                _ => 0
            };
            if (intensity > 0) result = Mix(result, b.Color, Math.Clamp(intensity, 0, 1));
        }
        return result;
    }

    private void Report(string text)
    {
        if (_dispatcher.CheckAccess()) Status?.Invoke(text);
        else _dispatcher.BeginInvoke(new Action(() => Status?.Invoke(text)));
    }

    private void FrameLoop(int generation)
    {
        var sw = Stopwatch.StartNew();
        long next = 0, lastReport = 0;
        int frames = 0;
        int width = GridWidth;
        // The firmware only commits a full 512-byte custom-LED frame to the physical
        // LEDs. Keep one list alive and mutate it to avoid per-frame allocations.
        var fullFrame = new List<LedColor>(128);
        for (int i = 0; i < 128; i++) fullFrame.Add(new LedColor { LedId = i });

        while (_running && generation == _generation)
        {
            long now = sw.ElapsedMilliseconds;
            long remaining = next - now;
            if (remaining > 2) { Thread.Sleep(1); continue; } // don't burn a CPU core spinning
            if (remaining > 0) { Thread.SpinWait(50); continue; }
            next = now + 1000L / _targetFps;

            double rawT = _clock.Elapsed.TotalSeconds;
            double t = rawT * _speed;

            // Update EVERY real key every frame. A moving window updates only a slice,
            // so the update location jumps back to the start every few frames — that
            // reads as a rhythmic "doin doin doin stop" stutter. Writing the whole
            // full frame makes the picture a pure function of time, with no reset.
            for (int fi = 0; fi < OrderedLeds.Length; fi++)
            {
                int id = OrderedLeds[fi];
                var (row, col) = GetRc(id);
                Color c;
                switch (_pattern)
                {
                    case AnimationPattern.RainbowWave:
                        // Exactly 360 degrees across all 68 keys, so the hue at row 5
                        // meets the hue at row 1 with no seam. Negative time term makes
                        // the colours flow row 1 -> row 5 continuously.
                        c = HsvToRgb(GetPos(id) * (360.0 / OrderedLeds.Length) - t * 95, 1, 1);
                        break;
                    case AnimationPattern.Breathing:
                        c = Mix(Color.FromRgb(8, 8, 8), _primary, (Math.Sin(t * 2.2) + 1) / 2);
                        break;
                    case AnimationPattern.Chase:
                    {
                        int head = (int)(t * 45) % OrderedLeds.Length;
                        int pos = GetPos(id);
                        int dist = Math.Min(Math.Abs(pos - head), OrderedLeds.Length - Math.Abs(pos - head));
                        c = dist < 5 ? Mix(_primary, Color.FromRgb(0, 0, 0), dist / 5.0) : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.Ripple:
                    {
                        double d = Math.Sqrt((row - 2) * (row - 2) + (col - width / 2) * (col - width / 2));
                        c = Mix(Color.FromRgb(0, 0, 0), _primary, (Math.Sin(d * 1.1 - t * 4) + 1) / 2);
                        break;
                    }
                    case AnimationPattern.Sparkle:
                        c = (((id * 37 + (int)(t * 30) * 17) % 97) < 5) ? _secondary : Color.FromRgb(0, 0, 0);
                        break;
                    case AnimationPattern.MatrixRain:
                    {
                        double head = (t * 18 + col * 11) % 20;
                        double dist = Math.Abs(row - head);
                        c = dist < 1.2 ? Color.FromRgb(180, 255, 180)
                          : dist < 4 ? Mix(Color.FromRgb(0, 0, 0), Color.FromRgb(0, 255, 40), 1 - dist / 4)
                          : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.Fire:
                    {
                        double heat = Math.Clamp((4 - row) / 4.0 + Math.Sin(t * 7 + col * 1.3) * 0.12, 0, 1);
                        c = heat < 0.15 ? Color.FromRgb(0, 0, 0) : HsvToRgb(heat * 55, 1, heat);
                        break;
                    }
                    case AnimationPattern.Checkerboard:
                    {
                        bool flip = ((int)(t * 3)) % 2 == 0;
                        c = (((row + col) % 2 == 0) ^ flip) ? _primary : _secondary;
                        break;
                    }
                    case AnimationPattern.KittScanner:
                    {
                        double phase = (t * 18) % (2 * (width - 1));
                        double pos = phase < width - 1 ? phase : 2 * (width - 1) - phase;
                        double d = Math.Abs(col - pos);
                        c = d < 1 ? Color.FromRgb(255, 220, 120)
                          : d < 3.5 ? Mix(Color.FromRgb(0, 0, 0), Color.FromRgb(255, 40, 20), 1 - d / 3.5)
                          : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.Aurora:
                    {
                        double hue = 140 + 80 * Math.Sin(col * 0.35 + t * 0.7);
                        double v = 0.45 + 0.45 * Math.Sin(row * 0.7 - t * 1.1);
                        c = HsvToRgb(hue, 0.85, Math.Clamp(v, 0.05, 1));
                        break;
                    }
                    case AnimationPattern.ColorFade:
                        c = HsvToRgb(GetPos(id) * (360.0 / OrderedLeds.Length) - t * 24, 0.95, 1);
                        break;
                    case AnimationPattern.ClickReactive:
                        c = Color.FromRgb(0, 0, 0);
                        break;
                    case AnimationPattern.Plasma:
                    {
                        double v = Math.Sin(col * 0.55 + t * 1.7)
                                 + Math.Sin(row * 0.75 - t * 1.3)
                                 + Math.Sin((col + row) * 0.42 + t * 0.9);
                        c = HsvToRgb(v * 42 + t * 28, 0.9, 0.85);
                        break;
                    }
                    case AnimationPattern.DiagonalWave:
                    {
                        double phase = (col + row * 0.8) * 0.7 - t * 5;
                        c = HsvToRgb((phase * 22) % 360, 1, 0.55 + 0.45 * Math.Sin(phase));
                        break;
                    }
                    case AnimationPattern.Spiral:
                    {
                        double dx = col - 7, dy = row - 2;
                        double radius = Math.Sqrt(dx * dx + dy * dy);
                        double angle = Math.Atan2(dy, dx);
                        double v = Math.Sin(radius * 1.6 - angle * 2 - t * 5);
                        c = HsvToRgb(t * 45 + radius * 25, 0.95, (v + 1) / 2);
                        break;
                    }
                    case AnimationPattern.Meteor:
                    {
                        double head = (t * 22 + col * 13) % 22;
                        double d = row - head;
                        c = d > 0 && d < 4 ? Mix(_primary, Color.FromRgb(0, 0, 0), d / 4.0)
                          : d <= 0 && d > -1.2 ? Color.FromRgb(255, 255, 255)
                          : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.ColorWipe:
                    {
                        int pos = (int)((t * 38) % (OrderedLeds.Length + 24)) - 12;
                        int p = GetPos(id);
                        c = p <= pos ? HsvToRgb((int)(t * 60), 1, 1) : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.BreathingRainbow:
                    {
                        double pulse = (Math.Sin(t * 2.4) + 1) / 2;
                        c = HsvToRgb(GetPos(id) * 5 + t * 40, 1, 0.15 + 0.85 * pulse);
                        break;
                    }
                    case AnimationPattern.Equalizer:
                    {
                        double level = 1 + 3.2 * Math.Abs(Math.Sin(t * 3.1 + col * 0.8)
                                          * Math.Cos(t * 1.7 - col * 0.35));
                        c = (4 - row) <= level ? HsvToRgb(120 - (4 - row) * 28, 1, 1)
                          : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.TypingTrail:
                    {
                        double age = rawT - _lastPress[id];
                        c = age < 1.4 ? Mix(Color.FromRgb(0, 0, 0), _primary, 1 - age / 1.4)
                          : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    default:
                        c = _primary;
                        break;
                }
                c = ApplyBursts(id, rawT, c);
                var slot = fullFrame[id];
                slot.Red = c.R; slot.Green = c.G; slot.Blue = c.B;
            }

            try
            {
                // A full 512-byte frame is required for the firmware to refresh the
                // physical LEDs. Bursts are already blended in by ApplyBursts above.
                if (UseMusicPath)
                    _ = _device.SendMusicFrameAsync(fullFrame.Where(c => OrderedLeds.Contains(c.LedId)).ToArray());
                else
                    _ = _device.SendCustomFrameAsync(fullFrame);
            }
            catch { /* device may be busy — skip this frame, never kill the loop */ }
            frames++;
            FramesRendered++;

            if (now - lastReport >= 1000)
            {
                Report($"{_pattern} — {frames * 1000.0 / (now - lastReport):0} FPS");
                frames = 0;
                lastReport = now;
            }
        }
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R * (1 - t) + b.R * t),
            (byte)(a.G * (1 - t) + b.G * t),
            (byte)(a.B * (1 - t) + b.B * t));
    }
}
