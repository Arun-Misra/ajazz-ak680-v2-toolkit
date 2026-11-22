using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AjazzManager;

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
        if (dlg.ShowDialog() == true) Status.Text = $"Imported {Path.GetFileName(dlg.FileName)}";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Macro JSON (*.json)|*.json", FileName = "macro.json" };
        if (dlg.ShowDialog() == true) Status.Text = $"Exported to {dlg.FileName}";
    }
}
