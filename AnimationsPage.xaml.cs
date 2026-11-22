using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace AjazzManager;

public partial class AnimationsPage : Page
{
    private readonly DeviceService _device = new();
    private readonly AnimationEngine _engine;
    private readonly KeyboardHook _hook = new();
    private bool _firmwareMode; // true = firmware renders everything; skip host bursts
    private bool _hybridMode;   // firmware wave idle, host stream only during a click burst
    private System.Threading.CancellationTokenSource? _restoreCts;

    public AnimationsPage()
    {
        InitializeComponent();
        _device.Connect();
        _engine = new AnimationEngine(_device);
        _engine.Status += s => Status.Text = s;
        FpsSlider.ValueChanged += (_, _) => FpsLabel.Text = ((int)FpsSlider.Value).ToString();
        SpeedSlider.ValueChanged += SpeedSlider_ValueChanged;

        _hook.KeyPressed += OnPhysicalKey;
        if (!_hook.Install()) Status.Text = "Keyboard hook unavailable";
        Unloaded += (_, _) => { _restoreCts?.Cancel(); _hook.Dispose(); };
    }

    private void OnPhysicalKey(int ledId)
    {
        if (_firmwareMode) return; // the keyboard handles key presses itself
        // Don't fire while the user is typing into a control.
        // Only skip real text entry; a focused ComboBox must not swallow key presses.
        if (Keyboard.FocusedElement is TextBox) return;
        if (!TryColor(PrimaryHex.Text, out var primary) || !TryColor(SecondaryHex.Text, out var secondary)) return;
        var effect = (ClickEffect)ClickEffectCombo.SelectedIndex;
        var color = effect == ClickEffect.SparkleBurst ? secondary : primary;
        FireBurst(ledId, effect, color);
    }

    /// <summary>In hybrid mode the HID link is idle while the firmware renders the
    /// ambient wave, so the custom burst streams into an empty queue. After the burst
    /// expires we hand rendering back to the firmware. Rapid presses reset the timer.</summary>
    private async void FireBurst(int ledId, ClickEffect effect, System.Windows.Media.Color color)
    {
        if (!_hybridMode)
        {
            _engine.TriggerKey(ledId, effect, color);
            return;
        }
        _restoreCts?.Cancel();
        _restoreCts = new System.Threading.CancellationTokenSource();
        var token = _restoreCts.Token;
        _engine.TriggerKey(ledId, effect, color); // starts custom mode + ClickReactive loop
        try { await System.Threading.Tasks.Task.Delay(2400, token); }
        catch (System.Threading.Tasks.TaskCanceledException) { return; }
        if (token.IsCancellationRequested || !_hybridMode) return;
        _engine.Stop();
        await ApplyFirmwareWave();
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML raises ValueChanged while the slider's Minimum/Maximum are being set,
        // before the constructor has created the engine and label.
        if (_engine is null || SpeedLabel is null) return;
        _engine.Speed = SpeedSlider.Value;
        SpeedLabel.Text = $"{SpeedSlider.Value:0.00}x";
    }

    private void PrimaryHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(PrimaryHex.Text);
            PrimaryPreview.Background = new SolidColorBrush(c);
        }
        catch { }
    }

    private void SecondaryHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(SecondaryHex.Text);
            SecondaryPreview.Background = new SolidColorBrush(c);
        }
        catch { }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!TryColor(PrimaryHex.Text, out var primary) || !TryColor(SecondaryHex.Text, out var secondary))
        { Status.Text = "Invalid colour value"; return; }
        _restoreCts?.Cancel();
        _firmwareMode = false;
        _hybridMode = false;
        _engine.UseMusicPath = MusicPathCheck.IsChecked == true;
        var pattern = (AnimationPattern)PatternCombo.SelectedIndex;
        _engine.Start(pattern, (int)FpsSlider.Value, primary, secondary);
        PlayButton.IsEnabled = false;
    }

    private void ClickPreview_KeyClicked(object? sender, string key)
    {
        if (!LightingPage.KeyValues.TryGetValue(key, out int ledId)) return;
        if (!TryColor(PrimaryHex.Text, out var primary) || !TryColor(SecondaryHex.Text, out var secondary)) return;
        var effect = (ClickEffect)ClickEffectCombo.SelectedIndex;
        var color = effect == ClickEffect.SparkleBurst ? secondary : primary;
        FireBurst(ledId, effect, color);
    }

    private async System.Threading.Tasks.Task ApplyFirmwareWave()
    {
        if (!_device.IsConnected && !_device.Connect()) return;
        TryColor(PrimaryHex.Text, out var primary);
        await _device.SetLedEffectAsync(new LedEffect
        {
            Mode = 11, ColorMode = 1,
            Red = primary.R, Green = primary.G, Blue = primary.B,
            Brightness = 5, Speed = 3, Direction = 0, DriverSetting = 255
        });
    }

    private async void FirmwareWave_Click(object sender, RoutedEventArgs e)
    {
        // Stop host streaming and let the keyboard's own effect engine render the
        // wave. This is smooth by construction - the firmware drives the LEDs at
        // its native scan rate instead of us pushing 512-byte frames over HID.
        _restoreCts?.Cancel();
        _engine.Stop();
        _firmwareMode = true;
        _hybridMode = false;
        PlayButton.IsEnabled = true;
        await ApplyFirmwareWave();
        Status.Foreground = System.Windows.Media.Brushes.Green;
        Status.Text = "Firmware wave running — rendered by the keyboard";
    }

    private async void Hybrid_Click(object sender, RoutedEventArgs e)
    {
        _restoreCts?.Cancel();
        _engine.Stop();
        _firmwareMode = false;
        _hybridMode = true;
        PlayButton.IsEnabled = true;
        await ApplyFirmwareWave();
        Status.Foreground = System.Windows.Media.Brushes.Green;
        Status.Text = "Hybrid — smooth firmware wave; press a key for a custom burst";
    }

    private async void FirmwareRipple_Click(object sender, RoutedEventArgs e)
    {
        int mode = int.Parse(((Button)sender).Tag.ToString()!);
        _restoreCts?.Cancel();
        _engine.Stop();
        _firmwareMode = true;
        _hybridMode = false;
        PlayButton.IsEnabled = true;
        if (!_device.IsConnected && !_device.Connect())
        { Status.Text = "No device connected: " + _device.LastError; return; }
        TryColor(PrimaryHex.Text, out var primary);
        bool ok = await _device.SetLedEffectAsync(new LedEffect
        {
            Mode = (byte)mode, ColorMode = 1,
            Red = primary.R, Green = primary.G, Blue = primary.B,
            Brightness = 5, Speed = 3, Direction = 0, DriverSetting = 255
        });
        Status.Foreground = ok ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        Status.Text = ok
            ? $"Firmware effect {mode} running — press keys to trigger it"
            : $"Failed to set firmware effect {mode}";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _restoreCts?.Cancel();
        _engine.Stop();
        _firmwareMode = false;
        _hybridMode = false;
        PlayButton.IsEnabled = true;
    }

    private static bool TryColor(string hex, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { color = Colors.Black; return false; }
    }
}
