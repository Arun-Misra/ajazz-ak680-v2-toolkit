using System.Windows;
using System.Windows.Controls;



namespace AjazzManager;

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
            _ => null
        };
        if (page != null) ContentFrame.Navigate(page);
    }
}