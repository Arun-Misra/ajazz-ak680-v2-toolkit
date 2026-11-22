using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AjazzManager;

public partial class DebugPage : Page
{
    private readonly DeviceService _device = new();
    private bool _customModeReady;
    private Color _testColor = Color.FromRgb(255, 0, 0);

    public DebugPage()
    {
        InitializeComponent();
        _device.Connect();

        foreach (var row in KeyMap.Rows)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var label in row)
            {
                int id = KeyMap.Id(label);
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
                stack.Children.Add(new TextBlock { Text = $"id {id}", FontSize = 11, Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center });

                var button = new Button
                {
                    Width = 92, Height = 58, Margin = new Thickness(2),
                    Content = stack, Tag = (label, id),
                    Style = (Style)FindResource("GhostButton")
                };
                button.Click += Key_Click;
                panel.Children.Add(button);
            }
            KeyboardRows.Children.Add(panel);
        }
    }

    private void ColorHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            _testColor = (Color)ColorConverter.ConvertFromString(ColorHex.Text);
            ColorPreview.Background = new SolidColorBrush(_testColor);
        }
        catch { }
    }

    private async System.Threading.Tasks.Task EnsureModeAsync()
    {
        if (_customModeReady) return;
        if (!_device.IsConnected && !_device.Connect())
        {
            Status.Text = "No device connected";
            return;
        }
        var effect = new LedEffect { Mode = 128, ColorMode = 0, Brightness = 6,
            Red = _testColor.R, Green = _testColor.G, Blue = _testColor.B, DriverSetting = 255 };
        _customModeReady = await _device.SetLedEffectAsync(effect);
        if (!_customModeReady) Status.Text = "Failed to enter custom mode";
    }

    private async void Key_Click(object sender, RoutedEventArgs e)
    {
        var (label, id) = ((string, int))((Button)sender!).Tag;
        await EnsureModeAsync();
        if (!_customModeReady) return;

        var frame = new List<LedColor>(128);
        for (int i = 0; i < 128; i++) frame.Add(new LedColor { LedId = i });
        frame[id] = new LedColor { LedId = id, Red = _testColor.R, Green = _testColor.G, Blue = _testColor.B };
        bool ok = await _device.SendCustomFrameAsync(frame);
        LastKey.Text = $"{label}  →  id {id}";
        Status.Text = ok ? $"LED {id} lit ({ColorHex.Text})" : "Write failed";
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        await EnsureModeAsync();
        if (!_customModeReady) return;
        var frame = new List<LedColor>(128);
        for (int i = 0; i < 128; i++) frame.Add(new LedColor { LedId = i });
        await _device.SendCustomFrameAsync(frame);
        LastKey.Text = "—";
        Status.Text = "All LEDs cleared";
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        var info = await _device.GetDeviceInfoAsync();
        Status.Text = info is null ? "Read failed" : $"VID {info.Vid:X4} PID {info.Pid:X4} fw {info.Version}";
    }
}
