using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public bool IsRunning => _running;
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
        var effect = new LedEffect { Mode = 128, ColorMode = 0,
            Red = primary.R, Green = primary.G, Blue = primary.B,
            Brightness = 6, Speed = 3, DriverSetting = 255 };
        if (!await _device.SetLedEffectAsync(effect))
        {
            Report("Failed to enter custom mode");
            return;
        }
        // One-time clear so the previous pattern's colours do not linger.
        var clear = new List<LedColor>(128);
        for (int i = 0; i < 128; i++) clear.Add(new LedColor { LedId = i });
        await _device.SendCustomFrameAsync(clear);
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
        // Immediate full-frame feedback: light the pressed key and its real grid
        // neighbours right away instead of waiting for the rotating 14-key sweep.
        if (_device.IsConnected)
        {
            var frame = new List<LedColor>(128);
            for (int i = 0; i < 128; i++) frame.Add(new LedColor { LedId = i });
            var (brow, bcol) = GetRc(ledId);
            foreach (var other in PosByLed.Keys)
            {
                var (row, col) = GetRc(other);
                int dist = Math.Max(Math.Abs(row - brow), Math.Abs(col - bcol));
                byte scale = dist == 0 ? (byte)255 : dist == 1 ? (byte)110 : (byte)0;
                if (scale > 0)
                    frame[other] = new LedColor
                    {
                        LedId = other,
                        Red = (byte)(burst.Color.R * scale / 255),
                        Green = (byte)(burst.Color.G * scale / 255),
                        Blue = (byte)(burst.Color.B * scale / 255)
                    };
            }
            await _device.SendCustomFrameAsync(frame);
        }
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
            _bursts.RemoveAll(b => t - b.Start > 1.8);
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
                ClickEffect.Ripple => Math.Max(0, 1 - Math.Abs(dist - age * 6) / 1.2) * (1 - age / 1.6),
                ClickEffect.Flash => dist <= 1 ? Math.Max(0, 1 - age / 0.55) : 0,
                ClickEffect.Spread => Math.Max(0, 1 - dist / (1 + age * 10)) * (1 - age / 1.4),
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
        const int Slice = 14; // 14 keys x 4 bytes = 56 bytes = one HID packet
        int width = GridWidth;

        while (_running && generation == _generation)
        {
            long now = sw.ElapsedMilliseconds;
            if (now < next) { Thread.SpinWait(40); continue; }
            next = now + 1000L / _targetFps;

            double rawT = _clock.Elapsed.TotalSeconds;
            double t = rawT * _speed;
            int start = (int)((t * 90) % 128);
            int count = Math.Min(Slice, 128 - start);
            var slice = new List<LedColor>(count);

            for (int k = 0; k < count; k++)
            {
                int id = start + k;
                var (row, col) = GetRc(id);
                Color c;
                switch (_pattern)
                {
                    case AnimationPattern.RainbowWave:
                        c = HsvToRgb(GetPos(id) * 5 + t * 95, 1, 1);
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
                        c = HsvToRgb(GetPos(id) * 6 + t * 24, 0.95, 1);
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
                slice.Add(new LedColor { LedId = id, Red = c.R, Green = c.G, Blue = c.B });
            }

            try { _ = _device.SendCustomLedRangeAsync(start, slice); }
            catch { /* device may be busy — skip this frame, never kill the loop */ }
            frames++;

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
