using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AjazzManager;

public partial class GamepadPage : Page
{
    private DeviceService? _device;
    private HallStream? _hall;
    private VirtualGamepad? _pad;
    private DispatcherTimer? _uiTimer;

    // Device key values from the config: W=34, A=49, S=50, D=51.
    private const int KeyW = 34, KeyA = 49, KeyS = 50, KeyD = 51;
    private readonly int[] _keys = { KeyW, KeyA, KeyS, KeyD };
    private readonly Dictionary<int, double> _full = new()
    {
        [KeyW] = 343, [KeyA] = 349, [KeyS] = 352, [KeyD] = 386
    };
    private readonly Dictionary<int, int> _peaks = new();
    private readonly Dictionary<int, double> _smooth = new();
    private bool _calibrating;
    private double _lx, _ly;

    public GamepadPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => Stop();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_hall != null) return;
        _device = new DeviceService();
        if (!_device.Connect())
        {
            Status.Text = "No keyboard connected: " + _device.LastError;
            return;
        }
        _hall = new HallStream(_device);
        if (!_hall.Start())
        {
            Status.Text = "Could not start the Hall stream";
            _hall.Dispose(); _hall = null;
            return;
        }
        _pad = new VirtualGamepad();
        if (!_pad.Start())
        {
            Status.Text = "Virtual controller failed: " + _pad.Error;
            _pad.Dispose(); _pad = null;
            _hall.Dispose(); _hall = null;
            return;
        }
        _hall.Sample += OnSample;
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += UpdateUi;
        _uiTimer.Start();
        Status.Foreground = System.Windows.Media.Brushes.Green;
        Status.Text = "Running — virtual Xbox 360 controller active";
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        CalibrateButton.IsEnabled = true;
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();

    private void Stop()
    {
        _uiTimer?.Stop();
        _uiTimer = null;
        if (_hall != null) { _hall.Sample -= OnSample; _hall.Dispose(); _hall = null; }
        _pad?.Reset();
        _pad?.Dispose();
        _pad = null;
        _device?.Disconnect();
        _device = null;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            CalibrateButton.IsEnabled = false;
            if (Status != null) { Status.Foreground = System.Windows.Media.Brushes.Gray; Status.Text = "Stopped"; }
        }));
    }

    private double Normalize(int key, int stroke)
    {
        double full = _full.TryGetValue(key, out double f) ? f : 350;
        double dead = 8; // 0.08 mm
        return Math.Clamp((stroke - dead) / Math.Max(1, full - dead), 0, 1);
    }

    private void OnSample(int key, int adc, int stroke)
    {
        if (!_keys.Contains(key)) return;
        if (_calibrating)
            _peaks[key] = Math.Max(_peaks.GetValueOrDefault(key), stroke);

        double target = Normalize(key, stroke);
        double prev = _smooth.GetValueOrDefault(key);
        double v = prev + (target - prev) * 0.35; // light smoothing at ~1 kHz
        _smooth[key] = v;

        double w = key == KeyW ? v : _smooth.GetValueOrDefault(KeyW);
        double s = key == KeyS ? v : _smooth.GetValueOrDefault(KeyS);
        double a = key == KeyA ? v : _smooth.GetValueOrDefault(KeyA);
        double d = key == KeyD ? v : _smooth.GetValueOrDefault(KeyD);
        _lx = d - a;
        _ly = w - s;
        _pad?.SetSticks(Deadzone(_lx), Deadzone(_ly));
    }

    private static double Deadzone(double v) => Math.Abs(v) < 0.06 ? 0 : v;

    private void UpdateUi(object? sender, EventArgs e)
    {
        if (_hall == null) return;
        int sw = _hall.GetStroke(KeyW), sa = _hall.GetStroke(KeyA);
        int ss = _hall.GetStroke(KeyS), sd = _hall.GetStroke(KeyD);
        SetKeyRow(WBar, WVal, sw);
        SetKeyRow(ABar, AVal, sa);
        SetKeyRow(SBar, SVal, ss);
        SetKeyRow(DBar, DVal, sd);
        LeftText.Text = $"LX {_lx,5:0.00}   LY {_ly,5:0.00}";
        StrokeText.Text = $"W {sw}   A {sa}   S {ss}   D {sd}";
        // Stick dot: centre (75,75), travel 55 px. Up is negative screen Y.
        LeftDot.Margin = new Thickness(66 + 55 * Deadzone(_lx), 66 - 55 * Deadzone(_ly), 0, 0);
    }

    private static void SetKeyRow(ProgressBar bar, TextBlock label, int stroke)
    {
        double mm = stroke / 100.0;
        bar.Value = Math.Clamp(mm / 3.4 * 100, 0, 100);
        label.Text = $"{mm:0.00} mm";
    }

    private async void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (_hall == null) return;
        _calibrating = true;
        _peaks.Clear();
        Status.Foreground = System.Windows.Media.Brushes.Orange;
        Status.Text = "Press W, A, S and D fully to the bottom, one at a time (6 s)…";
        CalibrateButton.IsEnabled = false;
        await System.Threading.Tasks.Task.Delay(6000);
        int learned = 0;
        foreach (int key in _keys)
        {
            int peak = _peaks.GetValueOrDefault(key);
            if (peak >= 150) { _full[key] = peak + 5; learned++; }
        }
        _calibrating = false;
        CalibrateButton.IsEnabled = true;
        Status.Foreground = System.Windows.Media.Brushes.Green;
        Status.Text = $"Calibrated {learned}/4 keys. Full travel: W {_full[KeyW]:0}  A {_full[KeyA]:0}  S {_full[KeyS]:0}  D {_full[KeyD]:0}";
    }
}
