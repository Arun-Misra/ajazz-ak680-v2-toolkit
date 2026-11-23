using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AK680V2Toolkit;

public partial class PerformancePage : Page
{
    private readonly DeviceService _device = new();
    private readonly TriggerConfig[] _triggers = new TriggerConfig[128];
    private RtConfig[] _rt = new RtConfig[128];
    private int _rtPrecision;
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
        for (int i = 0; i < _rt.Length; i++)
            _rt[i] = new RtConfig { AxisType = 1, TriggerKeyStroke = 1.2, PressRt = 0.2, ReleaseRt = 0.2 };

        _ = LoadFromDeviceAsync();
        Preview.ShowValues = true;
        Preview.AllowMultiSelect = true;
        Preview.SelectionChanged += (_, _) => UpdateStatus();
        UpdateKeyValues();

        _recordHook.KeyPressed += OnRecordedKey;
        Unloaded += (_, _) =>
        {
            _recording = false;
            _recordHook.Dispose();
            if (_calibration?.IsRunning == true)
            {
                _calibration.Stop();
                _calibration = null;
            }
        };
    }

    private async System.Threading.Tasks.Task LoadFromDeviceAsync()
    {
        if (!_device.IsConnected && !_device.Connect()) return;
        var info = await _device.GetDeviceInfoAsync();
        _rtPrecision = info?.RtPrecision ?? 0;
        var rt = await _device.GetMagneticAxisRtAsync(_rtPrecision);
        var mode = await _device.GetGameModeAsync();
        if (rt != null || mode != null)
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                if (rt != null)
                {
                    _rt = rt;
                    _axisType = rt.FirstOrDefault()?.AxisType ?? 1;
                    UpdateKeyValues();
                }
                if (mode != null)
                {
                    Stability.IsChecked = mode.StabilityMode != 0;
                    Adaptive.IsChecked = mode.AutoCalibration != 0;
                    ReturnRate.SelectedIndex = mode.ReportRate switch { 2 => 0, 4 => 1, _ => 2 };
                    TopDeadZone.Value = Math.Min(30, mode.TopDeadZone * 100);
                    BottomDeadZone.Value = Math.Min(30, mode.BottomDeadZone * 100);
                }
                UpdateStatus();
            }));
    }

    private void UpdateKeyValues()
    {
        var values = new Dictionary<string, string>();
        foreach (var kv in KeyMap.IdByLabel)
            values[kv.Key] = $"{_rt[kv.Value].TriggerKeyStroke:0.0}";
        Preview.SetValues(values);
    }

    private void UpdateStatus()
    {
        int count = Preview.SelectedKeys.Count;
        Status.Text = count == 0
            ? "Select one or more keys, or enable Record and press keys."
            : $"{count} key(s) selected — trigger {TriggerDistance.Value / 10.0:0.0} mm — axis {_axisType}";
    }

    private void DeadZone_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TopDeadZoneLabel is null || BottomDeadZoneLabel is null) return;
        TopDeadZoneLabel.Text = $"{TopDeadZone.Value / 100.0:0.00} mm";
        BottomDeadZoneLabel.Text = $"{BottomDeadZone.Value / 100.0:0.00} mm";
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
        double travel = TriggerDistance.Value / 10.0;
        double fastRt = FastTrigger.IsChecked == true ? 0.2 : 0.0;
        byte point = (byte)TriggerDistance.Value;
        byte release = (byte)(ReleasePoint.SelectedIndex == 0 ? 12 : 20);
        foreach (var label in Preview.SelectedKeys)
        {
            if (!KeyMap.IdByLabel.TryGetValue(label, out int id)) continue;
            _triggers[id].TriggerPoint = point;
            _triggers[id].ReleasePoint = release;
            _triggers[id].AxisType = _axisType;
            _triggers[id].IsFastTrigger = FastTrigger.IsChecked == true;
            _rt[id].AxisType = _axisType;
            _rt[id].TriggerKeyStroke = travel;
            _rt[id].PressRt = fastRt;
            _rt[id].ReleaseRt = fastRt;
        }
        UpdateKeyValues();
        if (!_device.IsConnected && !_device.Connect()) { Status.Text = "No device connected"; return; }
        bool ok = await _device.SetMagneticAxisRtAsync(_rt, _rtPrecision);
        ok = await _device.SetTriggerConfigAsync(_triggers) && ok;
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
            TopDeadZone = mode == 3 ? 0 : TopDeadZone.Value / 100.0,
            BottomDeadZone = mode == 3 ? 0 : BottomDeadZone.Value / 100.0
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
            ReportRate = (byte)(new byte[] { 2, 4, 6 })[Math.Min(ReturnRate.SelectedIndex, 2)],
            TopDeadZone = TopDeadZone.Value / 100.0,
            BottomDeadZone = BottomDeadZone.Value / 100.0
        };
        bool ok = await _device.SetGameModeAsync(mode);
        Status.Text = ok ? "Settings applied" : "Failed to apply settings";
    }

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected && !_device.Connect())
        {
            Status.Text = "No device connected";
            return;
        }
        bool ok = await _device.FactoryResetAsync(FactoryResetType.ResetAll);
        Status.Text = ok ? "All keyboard settings reset" : "Failed to reset keyboard settings";
    }

    private sealed class CalibrationRow
    {
        public string Key { get; set; } = "";
        public string Status { get; set; } = "";
        public string Min { get; set; } = "";
        public string Max { get; set; } = "";
        public string Adc { get; set; } = "";
        public string Travel { get; set; } = "";
    }

    private readonly ObservableCollection<CalibrationRow> _calibrationRows = new();
    private CalibrationStream? _calibration;
    private static readonly Dictionary<int, string> KeyLabels =
        KeyMap.IdByLabel.ToDictionary(kv => kv.Value, kv => kv.Key);

    private void CalibrationStart_Click(object sender, RoutedEventArgs e)
    {
        if (_calibration?.IsRunning == true) return;
        if (!_device.IsConnected && !_device.Connect())
        {
            CalibrationStatus.Text = "No device connected";
            return;
        }
        _calibration = new CalibrationStream(_device);
        if (!_calibration.Start())
        {
            CalibrationStatus.Text = "Device refused calibration mode";
            _calibration = null;
            return;
        }
        _calibration.Report += sample => Dispatcher.BeginInvoke(new Action(() =>
        {
            var row = _calibrationRows.FirstOrDefault(r => r.Key == (KeyLabels.TryGetValue(sample.KeyValue, out var k) ? k : sample.KeyValue.ToString()));
            var label = KeyLabels.TryGetValue(sample.KeyValue, out var key) ? key : sample.KeyValue.ToString();
            if (row == null)
            {
                row = new CalibrationRow { Key = label };
                _calibrationRows.Add(row);
            }
            row.Status = sample.Status.ToString();
            row.Min = sample.MinValue.ToString();
            row.Max = sample.MaxValue.ToString();
            row.Adc = sample.CurrentValue.ToString();
            row.Travel = $"{sample.KeyStroke / 100.0:0.00} mm";
            CalibrationGrid.ItemsSource = _calibrationRows;
        }));
        _calibrationRows.Clear();
        CalibrationGrid.ItemsSource = _calibrationRows;
        CalibrationStatus.Text = "Calibrating — press each key fully, then release it";
        CalibrationStart.IsEnabled = false;
        CalibrationStop.IsEnabled = true;
    }

    private void CalibrationStop_Click(object sender, RoutedEventArgs e)
    {
        _calibration?.Stop();
        _calibration = null;
        CalibrationStatus.Text = $"Calibration finished — {_calibrationRows.Count} key(s) reported";
        CalibrationStart.IsEnabled = true;
        CalibrationStop.IsEnabled = false;
    }

    private async void CalibrationClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected && !_device.Connect())
        {
            CalibrationStatus.Text = "No device connected";
            return;
        }
        bool ok = await _device.ClearCalibrationAsync();
        CalibrationStatus.Text = ok ? "Calibration data cleared" : "Failed to clear calibration";
    }
}
