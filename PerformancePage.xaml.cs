using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AK680V2Toolkit;

public partial class PerformancePage : Page
{
    private readonly DeviceService _device = new();
    private readonly TriggerConfig[] _triggers = new TriggerConfig[128];
    private readonly KeyboardHook _recordHook = new();
    private byte _axisType = 1;
    private bool _recording;

    private static readonly Dictionary<int, string> LabelById =
        KeyMap.IdByLabel.ToDictionary(kv => kv.Value, kv => kv.Key);

    public PerformancePage()
    {
        InitializeComponent();
        _device.Connect();

        for (int i = 0; i < _triggers.Length; i++)
            _triggers[i] = new TriggerConfig { TriggerPoint = 12, ReleasePoint = 12, AxisType = 1 };

        Preview.ShowValues = true;
        Preview.AllowMultiSelect = true;
        Preview.SelectionChanged += (_, _) => UpdateStatus();
        UpdateKeyValues();

        _recordHook.KeyPressed += OnRecordedKey;
        Unloaded += (_, _) => { _recording = false; _recordHook.Dispose(); };
    }

    private void UpdateKeyValues()
    {
        var values = new Dictionary<string, string>();
        foreach (var kv in KeyMap.IdByLabel)
            values[kv.Key] = $"{_triggers[kv.Value].TriggerPoint / 10.0:0.0}";
        Preview.SetValues(values);
    }

    private void UpdateStatus()
    {
        int count = Preview.SelectedKeys.Count;
        Status.Text = count == 0
            ? "Select one or more keys, or enable Record and press keys."
            : $"{count} key(s) selected — trigger {TriggerDistance.Value / 10.0:0.0} mm — axis {_axisType}";
    }

    private void TriggerDistance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TriggerLabel is null) return;
        TriggerLabel.Text = $"{e.NewValue / 10.0:0.0}mm";
        UpdateStatus();
    }

    private void QuickSelect_Click(object sender, RoutedEventArgs e)
    {
        if (SelectAllCheck.IsChecked == true) Preview.SelectAll();
        else if (WasdCheck.IsChecked == true) Preview.SelectWhere(k => k is "W" or "A" or "S" or "D");
        else if (NumericCheck.IsChecked == true) Preview.SelectWhere(k => k.Length > 0 && char.IsDigit(k[0]));
        else if (ArrowCheck.IsChecked == true) Preview.SelectWhere(k => k is "↑" or "↓" or "←" or "→");
        else Preview.ClearSelection();
        UpdateStatus();
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        _recording = RecordCheck.IsChecked == true;
        if (_recording)
        {
            _recordHook.Install();
            Status.Text = "Recording — press keys to add them to the selection.";
        }
        else Status.Text = "Recording stopped.";
    }

    private void OnRecordedKey(int ledId)
    {
        if (!_recording || !LabelById.TryGetValue(ledId, out var label)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Preview.SelectWhere(k => Preview.SelectedKeys.Contains(k) || k == label);
            UpdateStatus();
        }));
    }

    private void Axle_Click(object sender, RoutedEventArgs e)
    {
        _axisType = (byte)((Button)sender).Tag;
        UpdateStatus();
    }

    private async void ApplyTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (Preview.SelectedKeys.Count == 0) { Status.Text = "Select keys first"; return; }
        byte point = (byte)TriggerDistance.Value;
        byte release = (byte)(ReleasePoint.SelectedIndex == 0 ? 12 : 20);
        foreach (var label in Preview.SelectedKeys)
        {
            if (!KeyMap.IdByLabel.TryGetValue(label, out int id)) continue;
            _triggers[id].TriggerPoint = point;
            _triggers[id].ReleasePoint = release;
            _triggers[id].AxisType = _axisType;
            _triggers[id].IsFastTrigger = FastTrigger.IsChecked == true;
        }
        UpdateKeyValues();
        if (!_device.IsConnected && !_device.Connect()) { Status.Text = "No device connected"; return; }
        bool ok = await _device.SetTriggerConfigAsync(_triggers);
        Status.Text = ok ? $"Applied to {Preview.SelectedKeys.Count} key(s)" : "Failed to apply trigger settings";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Preview.ClearSelection();
        SelectAllCheck.IsChecked = WasdCheck.IsChecked = NumericCheck.IsChecked = ArrowCheck.IsChecked = false;
        UpdateStatus();
    }

    private async void Mode_Click(object sender, RoutedEventArgs e)
    {
        int mode = int.Parse(((Button)sender).Tag.ToString()!);
        var preset = new GameMode
        {
            Mode = (byte)mode,
            StabilityMode = (byte)(Stability.IsChecked == true ? 1 : 0),
            AutoCalibration = (byte)(Adaptive.IsChecked == true ? 1 : 0),
            ReportRate = (byte)(new byte[] { 2, 4, 6 })[Math.Min(ReturnRate.SelectedIndex, 2)],
            TopDeadZone = mode == 3 ? 0 : 0.05,
            BottomDeadZone = mode == 3 ? 0 : 0.05
        };
        if (!_device.IsConnected && !_device.Connect()) { Status.Text = "No device connected"; return; }
        bool ok = await _device.SetGameModeAsync(preset);
        Status.Text = ok ? $"{((Button)sender).Content} preset applied" : "Failed to apply preset";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        var mode = new GameMode
        {
            StabilityMode = (byte)(Stability.IsChecked == true ? 1 : 0),
            AutoCalibration = (byte)(Adaptive.IsChecked == true ? 1 : 0),
            ReportRate = (byte)(new byte[] { 2, 4, 6 })[Math.Min(ReturnRate.SelectedIndex, 2)]
        };
        bool ok = await _device.SetGameModeAsync(mode);
        Status.Text = ok ? "Settings applied" : "Failed to apply settings";
    }

    private async void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        bool ok = await _device.SendCommandAsync(Cmd.SetCalibrationOn, Array.Empty<byte>(), 0);
        Status.Text = ok ? "Calibration started" : "Failed to start calibration";
    }
}
