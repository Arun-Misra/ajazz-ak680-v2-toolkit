using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AK680V2Toolkit;

public partial class AdvancedKeysPage : Page
{
    private readonly DeviceService _device = new();
    private readonly Dictionary<string, KeyBinding> _bindings = new();
    private readonly List<DksConfig> _dkss = new();
    private readonly RtConfig[] _rt = new RtConfig[128];
    private string _type = "RS";
    private int _rtPrecision;
    private static readonly Dictionary<int, string> LabelById =
        KeyMap.IdByLabel.ToDictionary(kv => kv.Value, kv => kv.Key);

    public AdvancedKeysPage()
    {
        InitializeComponent();
        _device.Connect();
        for (int i = 0; i < _rt.Length; i++)
            _rt[i] = new RtConfig { AxisType = 1, TriggerKeyStroke = 1.2, PressRt = 0.2, ReleaseRt = 0.2 };
        Preview.AllowMultiSelect = true;
        Preview.SelectionChanged += (_, _) => UpdateSelection();
        Unloaded += (_, _) => { StopBindingTest(); _device.Disconnect(); };
        _ = LoadAsync();
        BuildConfig();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        var info = await _device.GetDeviceInfoAsync();
        if (info != null) _rtPrecision = info.RtPrecision;
        var rt = await _device.GetMagneticAxisRtAsync(_rtPrecision);
        if (rt != null) Array.Copy(rt, _rt, 128);

        // Load the existing state so a save merges instead of zeroing other keys/slots.
        var keys = await _device.GetKeyDataAsync();
        if (keys != null)
        {
            _bindings.Clear();
            foreach (var s in keys)
                if (s.Binding != null && LabelById.TryGetValue(s.Value, out var label))
                    _bindings[label] = s.Binding;
            _ = Dispatcher.BeginInvoke(new Action(RefreshBindings));
        }

        var dks = await _device.GetMagneticAxisDksDataAsync();
        if (dks != null)
        {
            _dkss.Clear();
            _dkss.AddRange(dks);
        }
    }

    private void UpdateSelection()
    {
        var keys = Preview.SelectedKeys.ToArray();
        SelectedLabel.Text = keys.Length == 0 ? "No keys selected" : string.Join(", ", keys);
    }

    private void Type_Click(object sender, RoutedEventArgs e)
    {
        _type = ((Button)sender).Tag.ToString()!;
        BuildConfig();
    }

    private static ComboBox KeyCombo()
    {
        var combo = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var kv in LightingPage.KeyValues.OrderBy(k => k.Key)) combo.Items.Add(kv.Key);
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }

    private static Slider TravelSlider(double value = 1.2)
    {
        return new Slider { Minimum = 0.1, Maximum = 3.4, Value = value, Width = 220,
            TickFrequency = 0.1, IsSnapToTickEnabled = true };
    }

    private void BuildConfig()
    {
        ConfigPanel.Children.Clear();
        ConfigTitle.Text = $"{_type} configuration";
        int needed = _type is "RS" or "SOCD" ? 2 : 1;

        ConfigPanel.Children.Add(new TextBlock
        {
            Text = $"Select {needed} key(s) on the keyboard above.",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var travel = TravelSlider();
        ConfigPanel.Children.Add(new TextBlock { Text = "Trigger travel (mm)", FontWeight = FontWeights.Bold });
        ConfigPanel.Children.Add(travel);

        var fast = new CheckBox { Content = "Fast Trigger", Margin = new Thickness(0, 8, 0, 4) };
        ConfigPanel.Children.Add(fast);

        var axis = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 0, 8) };
        axis.Items.Add("Default Axis"); axis.Items.Add("Magnetic Jade Pro"); axis.Items.Add("Magneto Shaft");
        axis.SelectedIndex = 0;
        ConfigPanel.Children.Add(axis);

        var combos = new List<ComboBox>();
        DksConfig? dks = null;

        if (_type == "RS")
        {
            ConfigPanel.Children.Add(new TextBlock { Text = "Two keys are linked (whichever is deeper wins).", Margin = new Thickness(0, 8, 0, 4) });
        }
        else if (_type == "SOCD")
        {
            var mode = new ComboBox { Width = 190, Margin = new Thickness(0, 8, 0, 8) };
            for (int i = 1; i <= 4; i++) mode.Items.Add($"SOCD mode {i}");
            mode.SelectedIndex = 0;
            ConfigPanel.Children.Add(new TextBlock { Text = "SOCD mode" });
            ConfigPanel.Children.Add(mode);
            combos.Add(mode);
        }
        else if (_type == "DKS")
        {
            var index = new ComboBox { Width = 190, Margin = new Thickness(0, 8, 0, 8) };
            for (int i = 0; i < 64; i++) index.Items.Add($"Slot {i}");
            index.SelectedIndex = 0;
            ConfigPanel.Children.Add(new TextBlock { Text = "DKS slot" });
            ConfigPanel.Children.Add(index);
            combos.Add(index);

            dks = new DksConfig { Index = 0, MakeValue1 = 16, MakeValue2 = 30, BreakValue1 = 30, BreakValue2 = 16 };
            for (int i = 0; i < 4; i++)
            {
                ConfigPanel.Children.Add(new TextBlock { Text = $"Action {i + 1}", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });
                var c = KeyCombo(); combos.Add(c);
                ConfigPanel.Children.Add(c);
                var tb = new TextBox { Text = i % 2 == 0 ? "16" : "30", Width = 190, Margin = new Thickness(0, 0, 0, 8) };
                ConfigPanel.Children.Add(new TextBlock { Text = "Travel value" });
                ConfigPanel.Children.Add(tb);
                combos.Add(new ComboBox()); // placeholder keeps indices simple; value read from tb via Tag
                combos[^1].Tag = tb;
            }
        }
        else if (_type == "MT")
        {
            ConfigPanel.Children.Add(new TextBlock { Text = "Tap action", Margin = new Thickness(0, 8, 0, 2) });
            var tap = KeyCombo(); combos.Add(tap); ConfigPanel.Children.Add(tap);
            ConfigPanel.Children.Add(new TextBlock { Text = "Hold action", Margin = new Thickness(0, 8, 0, 2) });
            var hold = KeyCombo(); combos.Add(hold); ConfigPanel.Children.Add(hold);
            var time = new Slider { Minimum = 10, Maximum = 1000, Value = 400, Width = 220 };
            ConfigPanel.Children.Add(new TextBlock { Text = "Hold time (ms)", Margin = new Thickness(0, 8, 0, 2) });
            ConfigPanel.Children.Add(time);
            combos.Add(new ComboBox { Tag = time });
        }
        else if (_type == "TGL")
        {
            ConfigPanel.Children.Add(new TextBlock { Text = "Toggle action", Margin = new Thickness(0, 8, 0, 2) });
            var c = KeyCombo(); combos.Add(c); ConfigPanel.Children.Add(c);
        }

        var save = new Button { Style = (Style)FindResource("AccentButton"), Content = $"Save {_type}", Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) => SaveConfig(travel, fast, axis, combos, dks);
        ConfigPanel.Children.Add(save);
    }

    private int ActionValue(ComboBox box, int fallbackIndex)
    {
        if (box.Tag is Slider) return (int)((Slider)box.Tag).Value / 10;
        if (box.Tag is TextBox tb && int.TryParse(tb.Text, out int v)) return v;
        if (box.SelectedItem is string s && LightingPage.KeyValues.TryGetValue(s, out int id)) return id;
        return fallbackIndex;
    }

    private async void SaveConfig(Slider travel, CheckBox fast, ComboBox axis, List<ComboBox> combos, DksConfig? dks)
    {
        var labels = Preview.SelectedKeys.ToArray();
        int needed = _type is "RS" or "SOCD" ? 2 : 1;
        if (labels.Length < needed) { Status.Text = $"Select at least {needed} key(s)"; return; }
        if (!_device.IsConnected && !_device.Connect()) { Status.Text = "No device connected"; return; }

        byte stroke = (byte)Math.Round(travel.Value * 100);
        byte axisType = (byte)(axis.SelectedIndex + 1);
        foreach (var label in labels)
        {
            if (!KeyMap.IdByLabel.TryGetValue(label, out int id)) continue;
            _rt[id].TriggerKeyStroke = travel.Value;
            _rt[id].AxisType = axisType;
            _rt[id].IsWholeFast = false;
            _rt[id].IsRampage = false;
            _rt[id].PressRt = fast.IsChecked == true ? 0.2 : 0;
            _rt[id].ReleaseRt = fast.IsChecked == true ? 0.2 : 0;
        }
        await _device.SetMagneticAxisRtAsync(_rt, _rtPrecision);

        int k1 = KeyMap.IdByLabel[labels[0]];
        int k2 = labels.Length > 1 ? KeyMap.IdByLabel[labels[1]] : 0;

        switch (_type)
        {
            case "RS":
                _bindings[labels[0]] = new KeyBinding { Page = "RS", Param1 = 0, Param2 = k1, Param3 = k2 };
                _bindings[labels[1]] = new KeyBinding { Page = "RS", Param1 = 0, Param2 = k1, Param3 = k2 };
                break;
            case "SOCD":
            {
                int mode = combos[0].SelectedIndex + 1;
                var b = new KeyBinding { Page = "SOCD", Param1 = mode, Param2 = k1, Param3 = k2 };
                _bindings[labels[0]] = b; _bindings[labels[1]] = b;
                break;
            }
            case "DKS":
            {
                var slot = dks!;
                slot.Index = combos[0].SelectedIndex;
                for (int i = 0; i < 4; i++)
                {
                    int action = ActionValue(combos[1 + i * 2], 0);
                    int travelValue = ActionValue(combos[2 + i * 2], i % 2 == 0 ? 16 : 30);
                    switch (i)
                    {
                        case 0: slot.Action1 = (byte)action; slot.MakeValue1 = (byte)travelValue; break;
                        case 1: slot.Action2 = (byte)action; slot.MakeValue2 = (byte)travelValue; break;
                        case 2: slot.Action3 = (byte)action; slot.BreakValue1 = (byte)travelValue; break;
                        case 3: slot.Action4 = (byte)action; slot.BreakValue2 = (byte)travelValue; break;
                    }
                    var states = new[] { slot.Action1States, slot.Action2States, slot.Action3States, slot.Action4States }[i];
                    states[0] = states[1] = states[2] = states[3] = 1;
                }
                int existing = _dkss.FindIndex(d => d.Index == slot.Index);
                if (existing >= 0) _dkss[existing] = slot; else _dkss.Add(slot);
                await _device.SetMagneticAxisDksAsync(_dkss);
                _bindings[labels[0]] = new KeyBinding { Page = "DKS", Value = slot.Index };
                break;
            }
            case "MT":
            {
                int tap = ActionValue(combos[0], 0);
                int hold = ActionValue(combos[1], 0);
                int time = (int)((Slider)combos[2].Tag).Value / 10;
                _bindings[labels[0]] = new KeyBinding { Page = "MT", Param1 = tap, Param2 = hold, Param3 = time };
                break;
            }
            case "TGL":
                _bindings[labels[0]] = new KeyBinding { Page = "TGL", Value = ActionValue(combos[0], 0) };
                break;
        }

        int advancedCount = _bindings.Values.Count(b => b.Page is "RS" or "SOCD" or "DKS" or "MT" or "TGL");
        if (advancedCount > 40) { Status.Text = "Maximum 40 advanced keys reached"; return; }

        var slots = _bindings.Select(kv => new KeySlot { Value = KeyMap.IdByLabel[kv.Key], Binding = kv.Value }).ToList();
        bool ok = await _device.SetKeyDataAsync(slots);
        RefreshBindings();
        Status.Text = ok ? $"Saved {_type} for {labels.Length} key(s)" : "Failed to write binding";
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (BindingList.SelectedItem is not string label || !_bindings.Remove(label)) return;
        if (!_device.IsConnected) return;
        var slots = _bindings.Select(kv => new KeySlot { Value = KeyMap.IdByLabel[kv.Key], Binding = kv.Value }).ToList();
        await _device.SetKeyDataAsync(slots);
        RefreshBindings();
        Status.Text = $"Removed {label}";
    }

    private HallStream? _testHall;
    private DispatcherTimer? _testTimer;

    private void TestBindingsStart_Click(object sender, RoutedEventArgs e)
    {
        if (_testHall != null) return;
        if (_bindings.Count == 0) { Status.Text = "No bindings to test yet"; return; }
        if (!_device.IsConnected && !_device.Connect()) { Status.Text = "No device connected"; return; }
        _testHall = new HallStream(_device);
        if (!_testHall.Start())
        {
            Status.Text = "Could not start Hall telemetry";
            _testHall = null;
            return;
        }
        _testTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _testTimer.Tick += UpdateBindingTest;
        _testTimer.Start();
        Preview.ShowValues = true;
        TestBindingsStart.IsEnabled = false;
        TestBindingsStop.IsEnabled = true;
        TestBindingsText.Text = "Press a bound key…";
    }

    private void TestBindingsStop_Click(object sender, RoutedEventArgs e) => StopBindingTest();

    private void StopBindingTest()
    {
        _testTimer?.Stop();
        _testTimer = null;
        _testHall?.Dispose();
        _testHall = null;
        Preview.ShowValues = false;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TestBindingsStart.IsEnabled = true;
            TestBindingsStop.IsEnabled = false;
            TestBindingsText.Text = "Not testing";
        }));
    }

    private void UpdateBindingTest(object? sender, EventArgs e)
    {
        if (_testHall == null) return;
        var lines = new List<string>();
        var preview = new Dictionary<string, string>();
        foreach (var label in _bindings.Keys.OrderBy(k => k))
        {
            if (!KeyMap.IdByLabel.TryGetValue(label, out int id)) continue;
            double mm = _testHall.GetStroke(id) / 100.0;
            lines.Add($"{label,-10} {mm:0.00} mm");
            preview[label] = $"{mm:0.00}mm";
        }
        TestBindingsText.Text = string.Join("\n", lines);
        Preview.SetValues(preview);
    }

    private void RefreshBindings()
    {
        BindingList.Items.Clear();
        foreach (var kv in _bindings.OrderBy(k => k.Key)) BindingList.Items.Add($"{kv.Key} — {kv.Value.Page}");
        int advancedCount = _bindings.Values.Count(b => b.Page is "RS" or "SOCD" or "DKS" or "MT" or "TGL");
        BindingCount.Text = $"{advancedCount}/40";
    }
}
