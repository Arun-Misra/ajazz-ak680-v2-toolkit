using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AK680V2Toolkit;

/// <summary>
/// Client for the AJAZZ music plugin's local HTTP API on 127.0.0.1:9191.
///
/// This is the exact channel the official web driver uses to control the
/// keyboard's firmware-native music effects: the page sends
///   GET /api/keyboard/musiceffect?param=&lt;mode&gt;&amp;&lt;amplitude&gt;&amp;&lt;backmode&gt;&amp;&lt;brightness&gt;&amp;1
/// and the plugin writes the HID reports. Recovered from the driver bundle
/// and confirmed by Ghidra analysis of MusicPlug.exe (the param string is
/// split into five integers and stored as mode / amplitude / backmode /
/// backmode-brightness, with amplitude scaled by 20 before transmission).
/// </summary>
public sealed class MusicPluginService : IDisposable
{
    private const string BaseUrl = "http://127.0.0.1:9191";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>True when the AJAZZ music plugin is running and answering.</summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _http.GetAsync(BaseUrl + "/api/hardware/info");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Apply a firmware music effect. Returns false if the plugin is not running.</summary>
    public async Task<bool> SetEffectAsync(int mode, int amplitude, int backMode, int brightness)
    {
        try
        {
            amplitude = Math.Clamp(amplitude, 1, 100);
            brightness = Math.Clamp(brightness, 1, 100);
            backMode = Math.Clamp(backMode, 0, 11);
            var url = $"{BaseUrl}/api/keyboard/musiceffect?param={mode}&{amplitude}&{backMode}&{brightness}&1";
            var response = await _http.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Stop the current firmware music effect (driver uses this on page unload).</summary>
    public async Task StopAsync()
    {
        try { await _http.GetAsync(BaseUrl + "/api/keyboard/musiceffect/close/"); }
        catch { }
    }

    public void Dispose() => _http.Dispose();
}
