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
    private readonly Dictionary<int, KeyBinding> _fnBindings = new();
    private bool _fnLoaded;

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
        for (int i = 0; i < 100; i++) MacroId.Items.Add($"M{i + 1}");
        _ = LoadBindingsAsync();
    }

    /// <summary>Load the current matrices so an edit merges with existing bindings.</summary>
    private async Task LoadBindingsAsync()
    {
        var normal = await _device.GetKeyDataAsync();
        var fn = await _device.GetFnKeyDataAsync();
        if (normal != null)
            foreach (var s in normal) if (s.Binding != null) _bindings[s.Value] = s.Binding;
        if (fn != null)
        {
            foreach (var s in fn) if (s.Binding != null) _fnBindings[s.Value] = s.Binding;
            _fnLoaded = true;
        }
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

    private void BindingType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MacroPanel is null) return;
        MacroPanel.Visibility = BindingType.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedKey is null) { Status.Text = "Select a key first"; return; }
        if (!KeyMap.IdByLabel.TryGetValue(_selectedKey, out int ledId)) { Status.Text = "Unknown key"; return; }
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }

        KeyBinding? binding;
        string description;
        if (BindingType.SelectedIndex == 1)
        {
            int id = MacroId.SelectedIndex;
            binding = new KeyBinding { Page = "MACRO", Param1 = id, Param2 = 1, Param3 = 0, Value = id };
            description = $"M{id + 1}";
        }
        else
        {
            if (_chosen is null) { Status.Text = "Choose a character first"; return; }
            binding = _chosen;
            description = _chosen.Page;
        }

        bool fn = FnLayerCheck.IsChecked == true;
        (fn ? _fnBindings : _bindings)[ledId] = binding;

        var normal = _bindings.Select(kv => new KeySlot { Value = kv.Key, Binding = kv.Value }).ToList();
        var fnKeys = _fnBindings.Select(kv => new KeySlot { Value = kv.Key, Binding = kv.Value }).ToList();
        bool ok = await _device.SetKeyDataAsync(normal);
        if (ok && _fnLoaded) ok = await _device.SetFnKeyDataAsync(fnKeys);
        Status.Text = ok
            ? $"Mapped {_selectedKey} → {description} ({(fn ? "FN layer" : "normal")})"
            : "Failed to write key data";
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        bool ok = await _device.FactoryResetAsync(FactoryResetType.KeyReset);
        Status.Text = ok ? "Keys reset" : "Reset failed";
    }
}
