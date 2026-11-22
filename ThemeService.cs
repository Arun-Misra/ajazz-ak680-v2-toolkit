using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AK680V2Toolkit;

public enum AppTheme { Light, Dark, Sync }

/// <summary>Applies and persists the interface theme and language preference.</summary>
public static class ThemeService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AK680V2Toolkit", "settings.json");

    private static bool _hooked;

    public static AppTheme Theme { get; private set; } = AppTheme.Light;
    public static string Language { get; private set; } = "English";

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("theme", out var t) &&
                    Enum.TryParse<AppTheme>(t.GetString(), true, out var theme)) Theme = theme;
                if (doc.RootElement.TryGetProperty("language", out var l))
                    Language = l.GetString() ?? "English";
            }
        }
        catch { }
        Apply(Theme);
    }

    public static void Set(AppTheme theme)
    {
        Theme = theme;
        Save();
        Apply(theme);
    }

    public static void SetLanguage(string language)
    {
        Language = language;
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
            {
                theme = Theme.ToString(),
                language = Language
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static void Apply(AppTheme theme)
    {
        if (Application.Current == null) return;
        bool dark = theme == AppTheme.Dark ||
                    (theme == AppTheme.Sync && !SystemUsesLightTheme());
        var r = Application.Current.Resources;
        Set(r, "PageBg", dark ? "#1B1E27" : "#F5F6FA");
        Set(r, "CardBg", dark ? "#262B36" : "#FFFFFF");
        Set(r, "TextMain", dark ? "#ECEFF5" : "#1F2430");
        Set(r, "TextMuted", dark ? "#9AA2B4" : "#8A90A0");
        Set(r, "Border", dark ? "#39404E" : "#ECEEF3");
        Set(r, "GhostBg", dark ? "#333A47" : "#F3F4F7");
        Set(r, "AccentLight", dark ? "#3A2A20" : "#FFF1E8");

        if (theme == AppTheme.Sync && !_hooked)
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General) Apply(AppTheme.Sync);
            };
            _hooked = true;
        }
    }

    private static void Set(ResourceDictionary dict, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (dict[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            dict[key] = new SolidColorBrush(color);
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return true; }
    }
}
