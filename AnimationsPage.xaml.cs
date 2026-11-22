using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace AK680V2Toolkit;

public partial class AnimationsPage : Page
{
    private readonly DeviceService _device = new();
    private readonly AnimationEngine _engine;
    private readonly KeyboardHook _hook = new();

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
        Unloaded += (_, _) => _hook.Dispose();
    }

    private void OnPhysicalKey(int ledId)
    {
        // Don't fire while the user is typing into a control.
        // Only skip real text entry; a focused ComboBox must not swallow key presses.
        if (Keyboard.FocusedElement is TextBox) return;
        if (!TryColor(PrimaryHex.Text, out var primary) || !TryColor(SecondaryHex.Text, out var secondary)) return;
        var effect = (ClickEffect)ClickEffectCombo.SelectedIndex;
        var color = effect == ClickEffect.SparkleBurst ? secondary : primary;
        _engine.TriggerKey(ledId, effect, color);
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
        _engine.TriggerKey(ledId, effect, color);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _engine.Stop();
        PlayButton.IsEnabled = true;
    }

    private static bool TryColor(string hex, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { color = Colors.Black; return false; }
    }
}
