using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AK680V2Toolkit;

public partial class KeyTestPage : Page
{
    private readonly DeviceService _device = new();
    private HallStream? _hall;
    private DispatcherTimer? _timer;
    private readonly Dictionary<int, double> _travel = new();
    private static readonly Dictionary<int, string> LabelById =
        KeyMap.IdByLabel.ToDictionary(kv => kv.Value, kv => kv.Key);

    public KeyTestPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => Stop();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_hall != null) return;
        if (!_device.IsConnected && !_device.Connect())
        { Status.Text = "No device connected"; return; }
        _hall = new HallStream(_device);
        if (!_hall.Start())
        { Status.Text = "Could not start key telemetry"; _hall = null; return; }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += UpdateUi;
        _timer.Start();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        Status.Text = "Monitoring — press keys";
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _hall?.Dispose();
        _hall = null;
        _travel.Clear();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            Status.Text = "Stopped";
        }));
    }

    private void UpdateUi(object? sender, EventArgs e)
    {
        if (_hall == null) return;
        var values = new Dictionary<string, string>();
        foreach (var kv in LabelById)
        {
            int stroke = _hall.GetStroke(kv.Key);
            if (stroke > 0) _travel[kv.Key] = stroke / 100.0;
            else if (_travel.ContainsKey(kv.Key) && stroke == 0) _travel.Remove(kv.Key);
            values[kv.Value] = _travel.TryGetValue(kv.Key, out double mm) ? $"{mm:0.00}mm" : "0.00mm";
        }
        Preview.SetValues(values);
        int active = _travel.Count;
        Status.Text = active == 0 ? "Monitoring — press keys" : $"{active} key(s) pressed";
    }
}
