using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AK680V2Toolkit;

public class MacroRow
{
    public int Index { get; set; }
    public int KeyCode { get; set; }
    public string Action { get; set; } = "";
    public int Delay { get; set; }
}

public partial class MacroPage : Page
{
    private readonly DeviceService _device = new();
    private readonly ObservableCollection<MacroRow> _rows = new();
    private readonly List<MacroEntry> _macros = new();
    private MacroEntry? _current;

    public MacroPage()
    {
        InitializeComponent();
        _device.Connect();
        ActionGrid.ItemsSource = _rows;
        for (int i = 0; i < 8; i++) { _macros.Add(new MacroEntry { MacroId = i }); MacroCombo.Items.Add($"Macro {i + 1}"); }
        MacroCombo.SelectedIndex = 0;
    }

    private void MacroCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MacroCombo.SelectedIndex < 0) return;
        _current = _macros[MacroCombo.SelectedIndex];
        _rows.Clear();
        int n = 0;
        foreach (var a in _current.Actions)
            _rows.Add(new MacroRow { Index = n++, KeyCode = a.KeyCode, Action = a.IsPress ? "Press" : "Release", Delay = a.Delay });
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _current = new MacroEntry { MacroId = _macros.Count };
        _macros.Add(_current);
        MacroCombo.Items.Add($"Macro {_current.MacroId + 1}");
        MacroCombo.SelectedIndex = MacroCombo.Items.Count - 1;
        _rows.Clear();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MacroCombo.SelectedIndex < 0) return;
        _macros.RemoveAt(MacroCombo.SelectedIndex);
        MacroCombo.Items.RemoveAt(MacroCombo.SelectedIndex);
        _rows.Clear();
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (!int.TryParse(KeyCode.Text, out int key) || key < 0 || key > 255) { Status.Text = "Invalid key code"; return; }
        if (!int.TryParse(Delay.Text, out int delay)) delay = 10;
        var action = new MacroAction
        {
            KeyCode = key, Delay = delay, IsPress = IsPress.IsChecked == true,
            ActionType = ActionType.SelectedIndex == 0 ? 1 : 2
        };
        _current.Actions.Add(action);
        _rows.Add(new MacroRow { Index = _rows.Count, KeyCode = key, Action = action.IsPress ? "Press" : "Release", Delay = delay });
        Status.Text = "Action added";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected) { Status.Text = "No device connected"; return; }
        bool ok = await _device.SetMacroDataAsync(_macros);
        Status.Text = ok ? "Macro saved to keyboard" : "Failed to save macro";
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Macro JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<List<MacroEntry>>(json);
            if (loaded == null) { Status.Text = "Invalid macro file"; return; }
            _macros.Clear();
            _macros.AddRange(loaded);
            MacroCombo.Items.Clear();
            foreach (var m in _macros) MacroCombo.Items.Add($"Macro {m.MacroId + 1}");
            if (MacroCombo.Items.Count > 0) MacroCombo.SelectedIndex = 0;
            Status.Text = $"Imported {_macros.Count} macro(s) from {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { Status.Text = "Import failed: " + ex.Message; }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Macro JSON (*.json)|*.json", FileName = "macro.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_macros,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            Status.Text = $"Exported {_macros.Count} macro(s) to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { Status.Text = "Export failed: " + ex.Message; }
    }
}
