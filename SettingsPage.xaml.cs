using System;
using System.Windows;
using System.Windows.Controls;

namespace AK680V2Toolkit;

public partial class SettingsPage : Page
{
    private readonly DeviceService _device = new();
    public SettingsPage()
    {
        InitializeComponent();
        _device.Connect();
        _ = LoadInfoAsync();
    }

    private async System.Threading.Tasks.Task LoadInfoAsync()
    {
        var info = await _device.GetDeviceInfoAsync();
        if (info != null)
        {
            DeviceLabel.Text = $"AJAZZ AK680 V2 v{info.Version}";
            VersionLabel.Text = $"AJAZZ AK680 V2 v{info.Version}";
        }
    }

    private void Sections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML raises SelectionChanged for the initially selected item before the panels exist.
        if (InterfacePanel is null || KeyboardPanel is null || UpdatePanel is null) return;
        string tag = (Sections.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "Interface";
        InterfacePanel.Visibility = tag == "Interface" ? Visibility.Visible : Visibility.Collapsed;
        KeyboardPanel.Visibility = tag == "Keyboard" ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanel.Visibility = tag == "Update" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ApplyKeyboard_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        var mode = new GameMode
        {
            StabilityMode = (byte)(StabilityCheck.IsChecked == true ? 1 : 0),
            AutoCalibration = (byte)(AdaptiveCheck.IsChecked == true ? 1 : 0),
            ReportRate = (byte)(new byte[] { 2, 4, 6 })[Math.Min(ReturnRate.SelectedIndex, 2)]
        };
        bool ok = await _device.SetGameModeAsync(mode);
        Status.Text = ok ? "Keyboard settings applied" : "Failed to apply settings";
    }

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        bool ok = await _device.FactoryResetAsync(FactoryResetType.ResetAll);
        Status.Text = ok ? "All settings reset" : "Reset failed";
    }

    private void CheckUpdate_Click(object sender, RoutedEventArgs e) => Status.Text = "Already the latest version";
}
