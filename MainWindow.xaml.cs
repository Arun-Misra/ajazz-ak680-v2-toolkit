using System.Windows;
using System.Windows.Controls;



namespace AK680V2Toolkit;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavList.SelectedIndex = 0;
        Loaded += (_, _) =>
        {
            var svc = new DeviceService();
            var ok = svc.Connect();
            StatusText.Text = ok ? $"Connected: {svc.ProductName}" : "No device: " + svc.LastError;
        };
    }

    private void CreateFile_Click(object sender, RoutedEventArgs e)
    {
        var svc = new ProfileService();
        string name = $"profile-{DateTime.Now:yyyyMMdd-HHmmss}";
        svc.CreateProfile(name);
        StatusText.Text = $"Created profile {name}";
        ContentFrame.Navigate(new ProfilesPage());
    }

    private void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Profile or macro JSON (*.json)|*.json",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        var profiles = new ProfileService();
        int count = 0;
        foreach (var path in dlg.FileNames)
        {
            try { profiles.ImportProfile(path); count++; } catch { }
        }
        StatusText.Text = $"Imported {count} file(s)";
        ContentFrame.Navigate(new ProfilesPage());
    }

    private void RefreshDevice_Click(object sender, RoutedEventArgs e)
    {
        var svc = new DeviceService();
        bool ok = svc.Connect();
        StatusText.Text = ok ? $"Connected: {svc.ProductName}" : "No device: " + svc.LastError;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item) return;
        var tag = item.Tag?.ToString();
        Page? page = tag switch
        {
            "CustomKeys" => new CustomKeysPage(),
            "Lighting" => new LightingPage(),
            "Macro" => new MacroPage(),
            "Performance" => new PerformancePage(),
            "AdvancedKeys" => new AdvancedKeysPage(),
            "Settings" => new SettingsPage(),
            "Profiles" => new ProfilesPage(),
            "Animations" => new AnimationsPage(),
            "Gamepad" => new GamepadPage(),
            "Inspector" => new InspectorPage(),
            "KeyTest" => new KeyTestPage(),
            "Debug" => new DebugPage(),
            _ => null
        };
        if (page != null) ContentFrame.Navigate(page);
    }
}