using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AjazzManager;

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
}