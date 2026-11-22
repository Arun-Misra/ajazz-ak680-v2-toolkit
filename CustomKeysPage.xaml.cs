using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace AK680V2Toolkit;

public partial class CustomKeysPage : Page
{
    private readonly DeviceService _device = new();
    private string? _selectedKey;
    private KeyBinding? _chosen;
    private readonly Dictionary<int, KeyBinding> _bindings = new();

    public CustomKeysPage()
    {
        InitializeComponent();
        _device.Connect();
        string basic = "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890-=[],;/'.`\\";
        string extended = "!@#$%^&*()_+{}|:<>?~";
        string special = "Esc Tab Caps Enter Backspace Space Shift Ctrl Alt Win Fn Home Del PgUp PgDn ← ↑ ↓ →";
        AddChars(BasicChars, basic.Select(c => c.ToString()));
        AddChars(ExtendedChars, extended.Select(c => c.ToString()));
        AddChars(SpecialChars, special.Split(' '));
    }

    private void AddChars(Panel panel, IEnumerable<string> chars)
    {
        foreach (var c in chars)
        {
            var b = new Button { Content = c, Width = 44, Height = 44, Margin = new Thickness(3), Style = (Style)FindResource("GhostButton"), Tag = c };
            b.Click += (s, _) =>
            {
                _chosen = new KeyBinding { Page = "KEYBOARD", Value = c.Length == 1 ? char.ToUpper(c[0]) : 0 };
                foreach (var child in panel.Children)
                    if (child is Button bb) bb.Background = System.Windows.Media.Brushes.White;
                ((Button)s!).Background = System.Windows.Media.Brushes.Orange;
            };
            panel.Children.Add(b);
        }
    }

    private void Preview_KeyClicked(object? sender, string key)
    {
        _selectedKey = key;
        SelectedKey.Text = $"Selected key: {key}";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedKey is null || _chosen is null) { Status.Text = "Select a key and a character first"; return; }
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        int value = _chosen.Value;
        var slots = new List<KeySlot> { new KeySlot { Value = value, Binding = _chosen } };
        bool ok = await _device.SetKeyDataAsync(slots);
        Status.Text = ok ? $"Mapped {_chosen.Page} to key {value}" : "Failed to write key data";
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        bool ok = await _device.FactoryResetAsync(FactoryResetType.KeyReset);
        Status.Text = ok ? "Keys reset" : "Reset failed";
    }
}
