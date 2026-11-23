using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AK680V2Toolkit;

public partial class LightingPage : Page
{
    private sealed class EffectDef
    {
        public string Name = "";
        public byte Value, Speed, Direction;
        public bool ShowSpeed, ShowDirection, ShowColor;
    }

    // Values and capability flags from lighting-DzPku7KQ.js.
    private static readonly EffectDef[] Effects =
    {
        new(){Name="Static Bright",Value=1,Speed=3,ShowColor=true},
        new(){Name="Single Point On",Value=2,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Single Point Off",Value=3,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Starry Sky",Value=4,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Snowfall",Value=5,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Floral Competition",Value=6,Speed=3,ShowSpeed=true},
        new(){Name="Dynamic Breathing",Value=7,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Spectrum Cycle",Value=8,Speed=3,ShowSpeed=true},
        new(){Name="Color Fountain",Value=9,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Colorful Interchange",Value=10,Speed=3,ShowSpeed=true,ShowDirection=true,Direction=3,ShowColor=true},
        new(){Name="Flowing with the Waves",Value=11,Speed=3,ShowSpeed=true,ShowDirection=true,ShowColor=true},
        new(){Name="Turning Peaks",Value=12,Speed=3,ShowSpeed=true,ShowDirection=true,ShowColor=true},
        new(){Name="One Touch to Fire",Value=13,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Two Birds with One Stone",Value=14,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Ripples Spread",Value=15,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Endless Flow",Value=16,Speed=3,ShowSpeed=true,ShowDirection=true,ShowColor=true},
        new(){Name="Layered Mountains",Value=17,Speed=3,ShowSpeed=true,ShowColor=true},
        new(){Name="Gentle Rain and Wind",Value=18,Speed=3,ShowSpeed=true,ShowDirection=true,ShowColor=true},
        new(){Name="Back and Forth",Value=19,Speed=3,ShowSpeed=true,ShowColor=true},
        // Mode 254 (Heart) exists in the bundle for other keyboard models but is not
        // present in this device's lightingList — the official driver shows only these 20.
        new(){Name="Custom (per-key)",Value=128,ShowColor=true},
    };

    public static readonly Dictionary<string, int> KeyValues = new()
    {
        ["Esc"]=0,["1 !"]=17,["2 @"]=18,["3 #"]=19,["4 $"]=20,["5 %"]=21,["6 ^"]=22,["7 &"]=23,["8 *"]=24,["9 ("]=25,["0 )"]=26,
        ["ˉ -"]=27,["= +"]=28,["Backspace"]=92,["Home"]=104,["Tab"]=32,["Q"]=33,["W"]=34,["E"]=35,["R"]=36,["T"]=37,["Y"]=38,
        ["U"]=39,["I"]=40,["O"]=41,["P"]=42,["[ {"]=43,["] }"]=44,["\\ |"]=60,["Del"]=106,["Caps"]=48,["A"]=49,["S"]=50,["D"]=51,
        ["F"]=52,["G"]=53,["H"]=54,["J"]=55,["K"]=56,["L"]=57,["; :"]=58,["' \""]=59,["Enter"]=76,["PgUp"]=105,["L-Shift"]=64,
        ["Z"]=65,["X"]=66,["C"]=67,["V"]=68,["B"]=69,["N"]=70,["M"]=71,[", <"]=72,[". >"]=73,["/ ?"]=74,["R-Shift"]=75,
        ["L-Ctrl"]=80,["L-Win"]=81,["L-Alt"]=82,["Spacebar"]=83,["R-Alt"]=84,["Fn"]=85,["R-Ctrl"]=87,["PgDn"]=108
    };

    private readonly DeviceService _device = new();
    private readonly Dictionary<int, LedColor> _custom = new();
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _updating;
    private bool _loaded;
    private byte _secondaryR = 255, _secondaryG = 255, _secondaryB = 255;

    public LightingPage()
    {
        InitializeComponent();
        foreach (var e in Effects)
            EffectsList.Items.Add(new ListBoxItem { Content = e.Name, Tag = e.Value });
        string[] swatches = { "#000000", "#0000FF", "#00FFFF", "#00FF00", "#FFFF00", "#FF0000", "#FF00FF", "#FFFFFF" };
        foreach (var hex in swatches)
        {
            var b = new Button { Width = 30, Height = 30, Margin = new Thickness(2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), Tag = hex, Cursor = System.Windows.Input.Cursors.Hand };
            b.Click += (s, _) => SetPrimary((string)((Button)s!).Tag);
            Swatches.Children.Add(b);
        }
        _liveTimer.Tick += async (_, _) => { _liveTimer.Stop(); if (LiveCheck.IsChecked == true) await ApplyAsync(showStatus: false); };
        Loaded += async (_, _) =>
        {
            EffectsList.SelectedIndex = 0;
            await LoadFromDeviceAsync();
            _loaded = true;
            _musicLoaded = true;
            UpdateVisibility();
            await CheckMusicPluginAsync();
        };
    }

    private async System.Threading.Tasks.Task LoadFromDeviceAsync()
    {
        if (!_device.Connect()) { Status.Text = "No device connected"; return; }
        var e = await _device.GetLedEffectAsync();
        if (e is null) { Status.Text = "Could not read lighting state"; return; }
        _updating = true;
        for (int i = 0; i < Effects.Length; i++)
            if (Effects[i].Value == e.Mode) EffectsList.SelectedIndex = i;
        var def = Current;
        if (def.ShowSpeed) Speed.Value = Math.Clamp((int)e.Speed, 1, 6);
        Brightness.Value = Math.Clamp((int)e.Brightness, 1, 6);
        if (def.ShowDirection) DirectionCombo.SelectedIndex = Math.Min((int)e.Direction, 3);

        MonoRadio.IsChecked = e.ColorMode == 0;
        RgbRadio.IsChecked = e.ColorMode != 0;
        SetPrimary($"#{e.Red:X2}{e.Green:X2}{e.Blue:X2}");
        _secondaryR = e.SecondaryRed; _secondaryG = e.SecondaryGreen; _secondaryB = e.SecondaryBlue;
        HexSecondary.Text = $"#{_secondaryR:X2}{_secondaryG:X2}{_secondaryB:X2}";
        _updating = false;
        UpdateVisibility();
        Status.Text = $"Read from keyboard: {def.Name}";
    }

    private EffectDef Current => Effects[Math.Max(0, EffectsList.SelectedIndex)];

    private void UpdateVisibility()
    {
        if (SpeedPanel is null || HexBox is null || EffectsList is null) return;
        var e = Current;
        SpeedPanel.Visibility = e.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        SpeedLabel.Text = ((int)Speed.Value).ToString();
        BrightnessLabel.Text = ((int)Brightness.Value).ToString();
        DirectionPanel.Visibility = e.ShowDirection ? Visibility.Visible : Visibility.Collapsed;
        CustomHint.Visibility = e.Value == 128 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EffectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        var def = Current;
        _updating = true;
        Speed.Value = def.Speed;
        DirectionCombo.SelectedIndex = def.Direction;
        _updating = false;
        UpdateVisibility();
        QueueLive();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        UpdateVisibility();
        QueueLive();
    }

    private void RawMode_TextChanged(object sender, TextChangedEventArgs e) => QueueLive();

    private byte ResolveMode()
    {
        if (RawCheck.IsChecked == true && byte.TryParse(RawModeBox.Text, out byte raw)) return raw;
        return Current.Value;
    }

    private void QueueLive()
    {
        if (!_loaded || _updating) return;
        _liveTimer.Stop();
        _liveTimer.Start();
    }

    private void SetPrimary(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            _updating = true;
            HexBox.Text = hex.ToUpperInvariant();
            RBox.Text = c.R.ToString(); GBox.Text = c.G.ToString(); BBox.Text = c.B.ToString();
            ColorPreview.Background = new SolidColorBrush(c);
            _updating = false;
            QueueLive();
        }
        catch { }
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || ColorPreview is null || RBox is null || GBox is null || BBox is null) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(HexBox.Text);
            ColorPreview.Background = new SolidColorBrush(c);
            _updating = true;
            RBox.Text = c.R.ToString(); GBox.Text = c.G.ToString(); BBox.Text = c.B.ToString();
            _updating = false;
            QueueLive();
        }
        catch { }
    }

    private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || RBox is null || GBox is null || BBox is null || HexBox is null || ColorPreview is null) return;
        if (byte.TryParse(RBox.Text, out byte r) && byte.TryParse(GBox.Text, out byte g) && byte.TryParse(BBox.Text, out byte b))
        {
            ColorPreview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            _updating = true; HexBox.Text = $"#{r:X2}{g:X2}{b:X2}"; _updating = false;
            QueueLive();
        }
    }

    private void HexSecondary_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || SecondaryPreview is null) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(HexSecondary.Text);
            _secondaryR = c.R; _secondaryG = c.G; _secondaryB = c.B;
            SecondaryPreview.Background = new SolidColorBrush(c);
            QueueLive();
        }
        catch { }
    }

    private void Preview_KeyClicked(object? sender, string key)
    {
        if (Current.Value != 128 || ColorPreview.Background is not SolidColorBrush br) return;
        if (!KeyValues.TryGetValue(key, out int id)) return;
        var c = br.Color;
        _custom[id] = new LedColor { LedId = id, Red = c.R, Green = c.G, Blue = c.B };
        Status.Text = $"Painted {key} (led {id})";
    }

    private bool BuildEffect(out LedEffect effect)
    {
        effect = new LedEffect();
        if (!byte.TryParse(RBox.Text, out byte r) || !byte.TryParse(GBox.Text, out byte g) || !byte.TryParse(BBox.Text, out byte b)) return false;
        var def = Current;
        effect = new LedEffect
        {
            Mode = ResolveMode(), Red = r, Green = g, Blue = b,
            SecondaryRed = _secondaryR, SecondaryGreen = _secondaryG, SecondaryBlue = _secondaryB,
            ColorMode = (byte)(MonoRadio.IsChecked == true ? 0 : 1),
            Brightness = (byte)Brightness.Value,
            Speed = (byte)Speed.Value,
            Direction = (byte)DirectionCombo.SelectedIndex,
            EffectModeType = (byte)EffectModeCombo.SelectedIndex,
            DriverSetting = 255
        };
        return true;
    }

    private async System.Threading.Tasks.Task ApplyAsync(bool showStatus)
    {
        if (!_device.IsConnected && !_device.Connect()) { if (showStatus) Status.Text = "No device connected"; return; }
        if (!BuildEffect(out var effect)) { if (showStatus) Status.Text = "Invalid RGB value"; return; }
        bool ok = await _device.SetLedEffectAsync(effect);
        if (ok && effect.Mode == 128 && _custom.Count > 0)
        {
            var list = new List<LedColor>();
            for (int i = 0; i < 128; i++) list.Add(_custom.TryGetValue(i, out var c) ? c : new LedColor { LedId = i });
            ok = await _device.SetCustomLedDataAsync(list);
        }
        if (showStatus) Status.Text = ok ? $"Applied {Current.Name}" : "Failed to apply lighting";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyAsync(showStatus: true);

    private async void ClearLedData_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected && !_device.Connect()) { Status.Text = "No device connected"; return; }
        bool ok = await _device.ClearLedDataAsync();
        Status.Text = ok ? "Live LED buffer cleared" : "Failed to clear LED data";
    }

    private AudioMeter? _meter;
    private AnimationEngine? _musicEngine;
    private DispatcherTimer? _musicTimer;

    private readonly MusicPluginService _musicPlugin = new();
    private DispatcherTimer? _firmwareDebounce;
    private bool _musicLoaded;

    private async System.Threading.Tasks.Task CheckMusicPluginAsync()
    {
        bool available = await _musicPlugin.IsAvailableAsync();
        MusicPluginState.Text = available
            ? "Plugin status: connected — firmware effects ready"
            : "Plugin status: not running (install the AJAZZ music plugin for firmware effects)";
    }

    private int ComboTag(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int v) ? v : 0;

    private async System.Threading.Tasks.Task<bool> ApplyFirmwareMusicAsync()
    {
        bool ok = await _musicPlugin.SetEffectAsync(ComboTag(MusicModeCombo),
            (int)MusicAmplitude.Value, ComboTag(MusicBackModeCombo), (int)MusicBackBrightness.Value);
        MusicPluginState.Text = ok ? "Firmware effect applied" : "Plugin unavailable — effect not applied";
        return ok;
    }

    private void MusicSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_musicLoaded || MusicAmplitudeLabel is null || MusicBackBrightnessLabel is null) return;
        MusicAmplitudeLabel.Text = $"{MusicAmplitude.Value:0}%";
        MusicBackBrightnessLabel.Text = $"{MusicBackBrightness.Value:0}%";
        // The web driver debounces effect changes by 300 ms.
        _firmwareDebounce?.Stop();
        _firmwareDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _firmwareDebounce.Tick += async (_, _) =>
        {
            _firmwareDebounce!.Stop();
            await ApplyFirmwareMusicAsync();
        };
        _firmwareDebounce.Start();
    }

    private async void FwMusicApply_Click(object sender, RoutedEventArgs e) => await ApplyFirmwareMusicAsync();

    private async void FwMusicStop_Click(object sender, RoutedEventArgs e)
    {
        await _musicPlugin.StopAsync();
        MusicModeCombo.SelectedIndex = 0;
        MusicPluginState.Text = "Firmware effect stopped";
    }

    private void MusicStart_Click(object sender, RoutedEventArgs e)
    {
        if (_meter != null) return;
        Color primary;
        try { primary = (Color)ColorConverter.ConvertFromString(HexBox.Text); }
        catch { primary = Colors.White; }
        var secondary = Color.FromRgb(_secondaryR, _secondaryG, _secondaryB);

        _meter = new AudioMeter();
        _meter.Start();
        _musicEngine = new AnimationEngine(_device);
        _musicEngine.Start(AnimationPattern.MusicRhythm, 30, primary, secondary);
        _musicTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _musicTimer.Tick += (_, _) => { if (_musicEngine != null && _meter != null) _musicEngine.AudioLevel = _meter.Level; };
        _musicTimer.Start();
        MusicStart.IsEnabled = false;
        MusicStop.IsEnabled = true;
        Status.Text = "Music rhythm running — play audio to see the lights react";
    }

    private void MusicStop_Click(object sender, RoutedEventArgs e)
    {
        _musicTimer?.Stop();
        _musicTimer = null;
        _musicEngine?.Stop();
        _musicEngine = null;
        _meter?.Dispose();
        _meter = null;
        MusicStart.IsEnabled = true;
        MusicStop.IsEnabled = false;
        Status.Text = "Music rhythm stopped";
    }
}
