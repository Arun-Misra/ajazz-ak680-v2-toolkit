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
    MatrixRain, Fire, Checkerboard, KittScanner, Aurora, ColorFade, ClickReactive
}

public enum ClickEffect { Ripple, Flash, Spread, SparkleBurst }

/// <summary>
/// Host-side animation engine. Generates per-key RGB frames on a dedicated thread and
/// streams them to the keyboard with setCustomLEDData — no firmware animation needed.
/// One HID packet (14 keys) per frame keeps the USB queue light and the motion smooth.
/// </summary>
public sealed class AnimationEngine
{
    public static readonly int[] OrderedLeds =
    {
        0,17,18,19,20,21,22,23,24,25,26,27,28,92,104,
        32,33,34,35,36,37,38,39,40,41,42,43,44,60,106,
        48,49,50,51,52,53,54,55,56,57,58,59,76,105,
        64,65,66,67,68,69,70,71,72,73,74,75,90,108,
        80,81,82,83,84,85,87,88,89,91
    };

    private readonly DeviceService _device;
    private readonly Dispatcher _dispatcher;
    private Thread? _thread;
    private volatile bool _running;
    private int _targetFps = 60;
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

    public bool IsRunning => _running;
    public event Action<string>? Status;

    public AnimationEngine(DeviceService device)
    {
        _device = device;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public async void Start(AnimationPattern pattern, int fps, Color primary, Color secondary)
    {
        if (_running) return;
        if (!_device.IsConnected && !_device.Connect())
        {
            Report("No device connected");
            return;
        }
        _pattern = pattern; _primary = primary; _secondary = secondary;
        _targetFps = Math.Clamp(fps, 1, 120);

        var effect = new LedEffect { Mode = 128, ColorMode = 0, Red = primary.R, Green = primary.G, Blue = primary.B,
            Brightness = 6, Speed = 3, DriverSetting = 255 };
        if (!await _device.SetLedEffectAsync(effect))
        {
            Report("Failed to enter custom mode");
            return;
        }

        // One-time full clear so colours from the previous pattern don't linger.
        var clear = new List<LedColor>(128);
        for (int i = 0; i < 128; i++) clear.Add(new LedColor { LedId = i });
        await _device.SendCustomFrameAsync(clear);

        timeBeginPeriod(1);
        _running = true;
        _thread = new Thread(FrameLoop) { IsBackground = true };
        _thread.Start();
        Report($"Playing {pattern} at up to {_targetFps} FPS");
    }

    public void Stop()
    {
        _running = false;
        _thread = null;
        timeEndPeriod(1);
        Report("Animation stopped");
    }

    /// <summary>
    /// Fires a one-shot effect centred on a key. If the engine is idle it starts a
    /// black base loop automatically so the burst can animate.
    /// </summary>
    public async void TriggerKey(int ledId, ClickEffect effect, Color color)
    {
        if (!_running)
        {
            Start(AnimationPattern.ClickReactive, 30, color, color);
            // Wait briefly for the engine to enter custom mode and start its loop.
            await System.Threading.Tasks.Task.Delay(120);
        }
        lock (_burstLock)
        {
            _bursts.Add(new Burst { Center = ledId, Start = _clock.Elapsed.TotalSeconds, Color = color, Effect = effect });
            if (_bursts.Count > 12) _bursts.RemoveAt(0);
        }
        Report($"Click {effect} at key {ledId}");
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Color ApplyBursts(int id, double t, Color baseColor)
    {
        if (_bursts.Count == 0) return baseColor;
        Burst[] bursts;
        lock (_burstLock) bursts = _bursts.ToArray();
        Color result = baseColor;
        foreach (var b in bursts)
        {
            double age = t - b.Start;
            if (age < 0 || age > 1.6) continue;
            int row = id / 15, col = id % 15;
            int brow = b.Center / 15, bcol = b.Center % 15;
            double dist = Math.Sqrt((row - brow) * (row - brow) + (col - bcol) * (col - bcol));
            double intensity;
            switch (b.Effect)
            {
                case ClickEffect.Ripple:
                    intensity = Math.Max(0, 1 - Math.Abs(dist - age * 9) / 1.8) * (1 - age / 1.6);
                    break;
                case ClickEffect.Flash:
                    intensity = Math.Max(0, 1 - dist / 4) * Math.Max(0, 1 - age / 0.7);
                    break;
                case ClickEffect.Spread:
                    intensity = Math.Max(0, 1 - dist / (1 + age * 14)) * (1 - age / 1.6);
                    break;
                case ClickEffect.SparkleBurst:
                    intensity = (((id * 31 + (int)(t * 40)) % 53) < 4 && dist < 6) ? (1 - age / 1.2) : 0;
                    break;
                default:
                    intensity = 0;
                    break;
            }
            if (intensity > 0) result = Mix(result, b.Color, Math.Clamp(intensity, 0, 1));
        }
        return result;
    }

    private void Report(string text)
    {
        if (_dispatcher.CheckAccess()) Status?.Invoke(text);
        else _dispatcher.BeginInvoke(new Action(() => Status?.Invoke(text)));
    }

    private void FrameLoop()
    {
        var sw = Stopwatch.StartNew();
        long next = 0;
        int frames = 0;
        long lastReport = 0;
        const int Slice = 14; // 14 keys x 4 bytes = 56 bytes = 1 HID packet

        while (_running)
        {
            long now = sw.ElapsedMilliseconds;
            if (now < next) { Thread.SpinWait(40); continue; }
            next = now + 1000L / _targetFps;

            double t = _clock.Elapsed.TotalSeconds;
            int start = (int)((t * 90) % 128); // sweep across LEDs ~90 ids/sec
            int count = Math.Min(Slice, 128 - start);
            var slice = new List<LedColor>(count);

            for (int k = 0; k < count; k++)
            {
                int id = start + k;
                Color c;
                switch (_pattern)
                {
                    case AnimationPattern.RainbowWave:
                        c = HsvToRgb(id * 2.8 + t * 95, 1, 1);
                        break;
                    case AnimationPattern.Breathing:
                        c = Mix(Color.FromRgb(8, 8, 8), _primary, (Math.Sin(t * 2.2) + 1) / 2);
                        break;
                    case AnimationPattern.Chase:
                    {
                        int head = (int)(t * 45) % OrderedLeds.Length;
                        int pos = Array.IndexOf(OrderedLeds, id);
                        int dist = pos < 0 ? 99 : Math.Min(Math.Abs(pos - head), OrderedLeds.Length - Math.Abs(pos - head));
                        c = dist < 5 ? Mix(_primary, Color.FromRgb(0, 0, 0), dist / 5.0) : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.Ripple:
                    {
                        int row = id / 15, col = id % 15;
                        double d = Math.Sqrt((row - 2) * (row - 2) + (col - 7) * (col - 7));
                        c = Mix(Color.FromRgb(0, 0, 0), _primary, (Math.Sin(d * 1.1 - t * 4) + 1) / 2);
                        break;
                    }
                    case AnimationPattern.Sparkle:
                        c = (((id * 37 + (int)(t * 30) * 17) % 97) < 5) ? _secondary : Color.FromRgb(0, 0, 0);
                        break;
                    case AnimationPattern.MatrixRain:
                    {
                        int col = id % 15;
                        double head = (t * 28 + col * 17) % 24;
                        double dist = Math.Abs((id / 15) - head);
                        c = dist < 1.5 ? Color.FromRgb(180, 255, 180)
                          : dist < 5 ? Mix(Color.FromRgb(0, 0, 0), Color.FromRgb(0, 255, 40), 1 - dist / 5)
                          : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.Fire:
                    {
                        int row = id / 15;
                        double heat = Math.Clamp((5 - row) / 5.0 + Math.Sin(t * 7 + id * 1.3) * 0.12, 0, 1);
                        c = heat < 0.15 ? Color.FromRgb(0, 0, 0) : HsvToRgb(heat * 55, 1, heat);
                        break;
                    }
                    case AnimationPattern.Checkerboard:
                    {
                        int row = id / 15, col = id % 15;
                        bool flip = ((int)(t * 3)) % 2 == 0;
                        bool light = ((row + col) % 2 == 0) ^ flip;
                        c = light ? _primary : _secondary;
                        break;
                    }
                    case AnimationPattern.KittScanner:
                    {
                        int col = id % 15;
                        double phase = (t * 18) % 28;
                        double pos = phase < 14 ? phase : 28 - phase;
                        double d = Math.Abs(col - pos);
                        c = d < 1 ? Color.FromRgb(255, 220, 120)
                          : d < 4 ? Mix(Color.FromRgb(0, 0, 0), Color.FromRgb(255, 40, 20), 1 - d / 4)
                          : Color.FromRgb(0, 0, 0);
                        break;
                    }
                    case AnimationPattern.Aurora:
                    {
                        int row = id / 15, col = id % 15;
                        double hue = 140 + 80 * Math.Sin(col * 0.35 + t * 0.7);
                        double v = 0.45 + 0.45 * Math.Sin(row * 0.7 - t * 1.1);
                        c = HsvToRgb(hue, 0.85, Math.Clamp(v, 0.05, 1));
                        break;
                    }
                    case AnimationPattern.ColorFade:
                        c = HsvToRgb(id * 47 + t * 24, 0.95, 1);
                        break;
                    case AnimationPattern.ClickReactive:
                        c = Color.FromRgb(0, 0, 0);
                        break;
                    default:
                        c = _primary;
                        break;
                }
                c = ApplyBursts(id, t, c);
                slice.Add(new LedColor { LedId = id, Red = c.R, Green = c.G, Blue = c.B });
            }

            _ = _device.SendCustomLedRangeAsync(start, slice);
            frames++;

            if (now - lastReport >= 1000)
            {
                lock (_burstLock) _bursts.RemoveAll(b => t - b.Start > 2.0);
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
