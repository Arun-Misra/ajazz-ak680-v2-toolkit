using System;
using System.Windows;
using System.Windows.Controls;

namespace AK680V2Toolkit;

public partial class PerformancePage : Page
{
    private readonly DeviceService _device = new();
    public PerformancePage() { InitializeComponent(); _device.Connect(); TriggerDistance.ValueChanged += (_, _) => TriggerLabel.Text = $"{TriggerDistance.Value / 10.0:0.0}mm"; }

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
