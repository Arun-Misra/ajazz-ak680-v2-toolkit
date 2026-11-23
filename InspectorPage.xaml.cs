using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AK680V2Toolkit;

public partial class InspectorPage : Page
{
    private readonly DeviceService _device = new();
    private DeviceSnapshot? _snapshot;

    public InspectorPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => _device.Disconnect();
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        Status.Foreground = System.Windows.Media.Brushes.Orange;
        Status.Text = "Reading all blocks…";
        if (!_device.IsConnected && !_device.Connect())
        {
            Status.Foreground = System.Windows.Media.Brushes.Red;
            Status.Text = "No device connected: " + _device.LastError;
            return;
        }
        try
        {
            _snapshot = await _device.GetSnapshotAsync();
            var liveRgb = await _device.GetAllLightsRgbAsync();
            var defaultFn = await _device.GetDefaultFnKeyMatrixAsync();
            LiveText.Text =
                "Live per-key RGB:\n" +
                (liveRgb == null ? "  no reply" : string.Join("\n", liveRgb.Where(l => l.Red != 0 || l.Green != 0 || l.Blue != 0)
                    .Select(l => $"led {l.LedId,3}: {l.Red,3},{l.Green,3},{l.Blue,3}"))) +
                "\n\nDefault FN matrix (non-default):\n" +
                (defaultFn == null ? "  no reply" : string.Join("\n", defaultFn.Select(k =>
                    $"key {k.Value,3}: page {k.Binding?.Page} p1={k.Binding?.Param1} p2={k.Binding?.Param2} p3={k.Binding?.Param3}")));
        }
        catch (Exception ex)
        {
            Status.Foreground = System.Windows.Media.Brushes.Red;
            Status.Text = "Read failed: " + ex.Message;
            return;
        }

        var s = _snapshot;
        OverviewText.Text =
            $"VID {s.Info?.Vid:X4}  PID {s.Info?.Pid:X4}  firmware {s.Info?.Version}\n" +
            $"Firmware status {s.Info?.FirmwareStatus}  frame version {s.Info?.FrameVersion}  RT precision {s.Info?.RtPrecision}\n" +
            $"Report rate {s.Game?.ReportRate}  game mode {s.Game?.GameMode}  NKRO {s.Game?.NkroSwitch}\n" +
            $"LED mode {s.Effect?.Mode}  brightness {s.Effect?.Brightness}  speed {s.Effect?.Speed}\n\n" +
            string.Join("\n", s.Raw.Select(kv => $"{kv.Key,-28} {(kv.Value.StartsWith("no reply") ? "—" : "OK")}"));

        KeysText.Text =
            "Non-default key bindings:\n" +
            string.Join("\n", s.Keys.Where(k => k.PageType != 0 || k.Param1 != 0 || k.Param2 != 0 || k.Param3 != 0)
                .Select(k => $"key {k.Index,3}: page {k.PageType,2}  {k.Param1,3} {k.Param2,3} {k.Param3,3}")) +
            "\n\nNon-black custom LEDs:\n" +
            string.Join("\n", s.Leds.Where(l => l.Red != 0 || l.Green != 0 || l.Blue != 0)
                .Select(l => $"led {l.LedId,3}: {l.Red,3},{l.Green,3},{l.Blue,3}"));

        RtText.Text =
            "Trigger points:\n" +
            string.Join("\n", s.Triggers.Where(t => t.FastTrigger || t.TriggerPoint != 0 || t.ReleasePoint != 0 || t.AxisType != 0)
                .Select(t => $"key {t.Index,3}: fast={(t.FastTrigger ? 1 : 0)} trigger {t.TriggerPoint} release {t.ReleasePoint} axis {t.AxisType}")) +
            "\n\nRapid Trigger settings:\n" +
            string.Join("\n", s.Rt.Where(r => r.AxisType != 0 || r.TriggerStroke > 0 || r.PressRt > 0 || r.ReleaseRt > 0)
                .Select(r => $"key {r.Index,3}: axis {r.AxisType} wholeFast={(r.WholeFast ? 1 : 0)} rampage={(r.Rampage ? 1 : 0)} " +
                             $"trigger {r.TriggerStroke:0.00}mm pressRT {r.PressRt:0.00} releaseRT {r.ReleaseRt:0.00}"));

        var usedMacros = s.MacroOffsets.Select((v, i) => (v, i)).Where(x => x.v != 0).ToArray();
        DksText.Text =
            "DKS slots:\n" +
            (s.Dks.Count == 0 ? "  none" : string.Join("\n", s.Dks.Select(d =>
                $"slot {d.Index,3}: make {d.MakeValue1},{d.MakeValue2} break {d.BreakValue1},{d.BreakValue2} " +
                $"actions {d.Action1},{d.Action2},{d.Action3},{d.Action4}"))) +
            "\n\nMacro offsets:\n" +
            (usedMacros.Length == 0 ? "  none" : string.Join("  ", usedMacros.Select(x => $"#{x.i}={x.v}")));

        RawText.Text = string.Join("\n\n", s.Raw.Select(kv => $"{kv.Key}:\n  {kv.Value}"));

        SaveButton.IsEnabled = true;
        Status.Foreground = System.Windows.Media.Brushes.Green;
        Status.Text = $"Read complete — {s.Keys.Count} keys, {s.Triggers.Count} triggers, {s.Rt.Count} RT entries";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot == null) return;
        var path = Path.Combine(Path.GetTempPath(), "ajazz-device-report.txt");
        try
        {
            File.WriteAllText(path, _snapshot.Dump());
            Status.Text = "Saved: " + path;
        }
        catch (Exception ex)
        {
            Status.Foreground = System.Windows.Media.Brushes.Red;
            Status.Text = "Save failed: " + ex.Message;
        }
    }
}
