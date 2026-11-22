using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AK680V2Toolkit;

public partial class SettingsPage : Page
{
    private readonly DeviceService _device = new();
    public SettingsPage()
    {
        InitializeComponent();
        _device.Connect();
        (ThemeService.Theme switch { AppTheme.Light => LightTheme, AppTheme.Dark => DarkTheme, _ => SyncTheme }).IsChecked = true;
        int langIndex = ThemeService.Language switch { "中文" => 1, "日本語" => 2, _ => 0 };
        LanguageCombo.SelectedIndex = langIndex;
        _settingsLoaded = true;
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

    private bool _settingsLoaded;

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        // XAML sets IsChecked during initialization, before the other radio fields exist.
        if (!_settingsLoaded || LightTheme is null || DarkTheme is null || SyncTheme is null) return;
        if (LightTheme.IsChecked == true) ThemeService.Set(AppTheme.Light);
        else if (DarkTheme.IsChecked == true) ThemeService.Set(AppTheme.Dark);
        else ThemeService.Set(AppTheme.Sync);
        Status.Text = $"Theme set to {ThemeService.Theme}";
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoaded || LanguageCombo.SelectedItem is not ComboBoxItem item) return;
        ThemeService.SetLanguage(item.Content.ToString() ?? "English");
        LocalizationService.Refresh();
        Status.Text = $"Language preference saved: {ThemeService.Language}";
    }

    private string? _firmwarePath;

    private void ChooseFirmware_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Firmware image (*.bin;*.hex)|*.bin;*.hex|All files|*" };
        if (dlg.ShowDialog() != true) return;
        _firmwarePath = dlg.FileName;
        FirmwarePath.Text = dlg.FileName;
        FlashButton.IsEnabled = true;
        FlashStatus.Text = "Firmware selected. Verify and flash when ready.";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected && !_device.Connect()) { Status.Text = "No device connected"; return; }
        var info = await _device.GetDeviceInfoAsync();
        var ota = await _device.GetOtaSystemInfoAsync();
        var sn = await _device.GetSerialNumberAsync();
        if (ota == null)
        {
            SystemInfoLabel.Text = $"Firmware {info?.Version ?? "?"} · OTA info unavailable · S/N {sn}";
            Status.Text = "Device answered firmware version but not OTA system info";
            return;
        }
        SystemInfoLabel.Text =
            $"App {ota.AppVersion:0.00} · Boot {ota.BootloaderVersion:0.00} · " +
            $"Area {ota.RunningArea} · Manufacturer {ota.Manufacturer:X4} · " +
            $"Product {ota.Product:X4} · S/N {sn}";
        VersionLabel.Text = $"AJAZZ AK680 V2 v{info?.Version ?? ota.AppVersion.ToString("0.00")}";
        Status.Text = "Device firmware info read";
    }

    private async void Flash_Click(object sender, RoutedEventArgs e)
    {
        if (_firmwarePath == null || !File.Exists(_firmwarePath)) { FlashStatus.Text = "Choose a firmware file first"; return; }
        if (!_device.IsConnected && !_device.Connect()) { FlashStatus.Text = "No device connected"; return; }
        if (MessageBox.Show("Flashing firmware can brick the keyboard if the image is wrong. Continue?",
            "Confirm firmware update", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            var fw = File.ReadAllBytes(_firmwarePath);
            FlashStatus.Foreground = System.Windows.Media.Brushes.Orange;
            FlashStatus.Text = "Verifying firmware…";
            ushort manufacturer = 0, product = 0;
            var info = await _device.GetDeviceInfoAsync();
            if (info != null) { manufacturer = (ushort)info.Manufacturer; product = (ushort)info.Product; }
            var verify = await _device.VerifyFirmwareAsync(manufacturer, product);
            if (!verify) { FlashStatus.Text = "Firmware verification failed — aborted"; return; }

            FlashStatus.Text = "Entering boot mode…";
            if (!await _device.EnterBootAsync()) { FlashStatus.Text = "Could not enter boot mode"; return; }
            await Task.Delay(500);

            FlashStatus.Text = "Sending firmware…";
            var result = await _device.SendFirmwareAsync(fw, p => Dispatcher.BeginInvoke(new Action(() => FlashProgress.Value = p)));
            if (!result.ok)
            {
                FlashStatus.Text = $"Firmware transfer failed (status {result.status:X2}, detail {result.detail:X2})";
                return;
            }

            FlashStatus.Text = "Switching to app partition…";
            bool switched = await _device.SwitchPartitionAsync(app: true);
            FlashStatus.Foreground = switched
                ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
            FlashStatus.Text = switched
                ? "Firmware flashed — keyboard will reboot into the app"
                : "Transfer finished but app-switch failed";
        }
        catch (Exception ex)
        {
            FlashStatus.Foreground = System.Windows.Media.Brushes.Red;
            FlashStatus.Text = "Flash failed: " + ex.Message;
        }
    }
}
