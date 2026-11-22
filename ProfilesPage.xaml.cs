using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AK680V2Toolkit;

public partial class ProfilesPage : Page
{
    private readonly ProfileService _svc = new();

    public ProfilesPage() { InitializeComponent(); Loaded += (_, _) => Refresh(); }

    private void Refresh()
    {
        ProfileList.Items.Clear();
        foreach (var p in _svc.ListProfiles()) ProfileList.Items.Add(p);
    }

    private string? Selected => ProfileList.SelectedItem as string;

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is null) { ContentBox.Text = ""; return; }
        ContentBox.Text = _svc.ReadProfile(Selected) ?? "";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NewName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        _svc.CreateProfile(name);
        NewName.Text = "";
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        _svc.DeleteProfile(Selected);
        Refresh();
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var oldName = Selected;
        var newName = TargetName.Text.Trim();
        if (oldName is null) { MessageBox.Show("Select a profile to rename"); return; }
        if (string.IsNullOrWhiteSpace(newName)) { MessageBox.Show("Enter a new name"); return; }
        if (!_svc.RenameProfile(oldName, newName))
        {
            MessageBox.Show("Rename failed — the name may already be in use.");
            return;
        }
        TargetName.Text = "";
        Refresh();
        ProfileList.SelectedItem = newName;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var source = Selected;
        var newName = TargetName.Text.Trim();
        if (source is null) { MessageBox.Show("Select a profile to copy"); return; }
        if (string.IsNullOrWhiteSpace(newName)) { MessageBox.Show("Enter a name for the copy"); return; }
        if (!_svc.CopyProfile(source, newName))
        {
            MessageBox.Show("Copy failed — the name may already be in use.");
            return;
        }
        TargetName.Text = "";
        Refresh();
        ProfileList.SelectedItem = newName;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON profile (*.json)|*.json" };
        if (dlg.ShowDialog() == true)
        {
            _svc.ImportProfile(dlg.FileName);
            Refresh();
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        var dlg = new SaveFileDialog { Filter = "JSON profile (*.json)|*.json", FileName = Selected + ".json" };
        if (dlg.ShowDialog() == true)
        {
            _svc.ExportProfile(Selected, dlg.FileName);
            MessageBox.Show("Exported.");
        }
    }

    private readonly DeviceService _device = new();

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        if (!byte.TryParse(SlotBox.Text, out byte slot)) { MessageBox.Show("Invalid slot"); return; }
        if (!_device.IsConnected && !_device.Connect()) { MessageBox.Show("No device connected"); return; }
        bool ok = await _device.SetProfileAsync(slot);
        MessageBox.Show(ok ? $"Profile slot {slot} applied" : "Failed to apply profile");
    }

    private async void SaveFromDevice_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        if (!_device.IsConnected && !_device.Connect()) { MessageBox.Show("No device connected"); return; }
        var snapshot = await _device.GetSnapshotAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        _svc.WriteProfile(Selected, json);
        ContentBox.Text = json;
        MessageBox.Show($"Keyboard state saved to profile {Selected}");
    }
}