using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AjazzManager;

public sealed class GameInfo
{
    public byte GameMode, FnSwitch, SleepTime, KeyDelay, ReportRate, SystemMode, TftDisplayTime;
    public double TopDeadZone, BottomDeadZone;
    public byte StabilityMode, AutoCalibration, SingleKeyWakeup, PushButtonMode, NkroSwitch, PowerMode;
    public int WirelessReportRate;
}

public sealed class KeyRaw
{
    public int Index, PageType, Param1, Param2, Param3;
}

public sealed class TriggerRaw
{
    public int Index;
    public bool FastTrigger;
    public int TriggerPoint, ReleasePoint, AxisType;
}

public sealed class RtRaw
{
    public int Index, AxisType;
    public bool WholeFast, Rampage;
    public double TriggerStroke, PressRt, ReleaseRt;
}

public sealed class DksRaw
{
    public int Index;
    public byte MakeValue1, MakeValue2, BreakValue1, BreakValue2;
    public byte Action1, Action2, Action3, Action4;
    public string Action1States = "", Action2States = "", Action3States = "", Action4States = "";
}

/// <summary>Everything the AK680 V2 firmware is willing to report in one read pass.</summary>
public sealed class DeviceSnapshot
{
    public DateTime ReadAt;
    public DeviceInfo? Info;
    public GameInfo? Game;
    public LedEffect? Effect;
    public readonly List<KeyRaw> Keys = new();
    public readonly List<LedColor> Leds = new();
    public readonly List<TriggerRaw> Triggers = new();
    public readonly List<RtRaw> Rt = new();
    public readonly List<DksRaw> Dks = new();
    public readonly int[] MacroOffsets = new int[100];
    public readonly Dictionary<string, string> Raw = new();

    private static string U16(byte[] d, int o) => (d[o] | (d[o + 1] << 8)).ToString();

    public string Dump()
    {
        var s = new StringBuilder();
        s.AppendLine($"AK680 V2 device snapshot — {ReadAt:yyyy-MM-dd HH:mm:ss}");
        s.AppendLine(new string('=', 60));

        s.AppendLine("\n[Device info]");
        if (Info == null) s.AppendLine("  no reply");
        else s.AppendLine(
            $"  VID {Info.Vid:X4}  PID {Info.Pid:X4}  firmware {Info.Version}\n" +
            $"  ROM {Info.RomSize}  macro space {Info.MacroSpaceSize}  sensor {Info.Sensor}\n" +
            $"  manufacturer {Info.Manufacturer:X4}  product {Info.Product:X4}\n" +
            $"  work mode {Info.WorkMode}  battery {Info.BatteryLevel}  charge {Info.ChargeStatus}  profile {Info.CurrentProfile}\n" +
            $"  axis info {Info.AxisInfo}  TFT frames {Info.TftMaxFrames}  GIF frames {Info.GifMaxFrames}  LED frames {Info.LedMaxFrames}\n" +
            $"  TFT direction {Info.TftDirection}  RT precision {Info.RtPrecision}  frame version {Info.FrameVersion}\n" +
            $"  lighting version {Info.LightingVersion}  firmware status {Info.FirmwareStatus}  max profiles {Info.MaxProfile}");

        s.AppendLine("\n[Game / system settings]");
        if (Game == null) s.AppendLine("  no reply");
        else s.AppendLine(
            $"  game mode {Game.GameMode}  FN switch {Game.FnSwitch}  sleep {Game.SleepTime}  key delay {Game.KeyDelay}\n" +
            $"  report rate byte {Game.ReportRate} (2=1k,4=4k,6=8k)  system mode {Game.SystemMode}  TFT time {Game.TftDisplayTime}\n" +
            $"  dead zones top {Game.TopDeadZone:0.00} bottom {Game.BottomDeadZone:0.00}  stability {Game.StabilityMode}\n" +
            $"  auto calibration {Game.AutoCalibration}  single-key wake {Game.SingleKeyWakeup}  push button {Game.PushButtonMode}\n" +
            $"  NKRO {Game.NkroSwitch}  wireless rate {Game.WirelessReportRate}  power mode {Game.PowerMode}");

        s.AppendLine("\n[LED effect]");
        if (Effect == null) s.AppendLine("  no reply");
        else s.AppendLine(
            $"  mode {Effect.Mode}  primary {Effect.Red},{Effect.Green},{Effect.Blue}  secondary {Effect.SecondaryRed},{Effect.SecondaryGreen},{Effect.SecondaryBlue}\n" +
            $"  driver {Effect.DriverSetting}  colorMode {Effect.ColorMode}  brightness {Effect.Brightness}  speed {Effect.Speed}\n" +
            $"  direction {Effect.Direction}  effectModeType {Effect.EffectModeType}  checks {Effect.CheckCodeL:X2}/{Effect.CheckCodeH:X2}");

        s.AppendLine($"\n[Key bindings — {Keys.Count} entries]");
        foreach (var k in Keys.Where(k => k.PageType != 0 || k.Param1 != 0 || k.Param2 != 0 || k.Param3 != 0))
            s.AppendLine($"  key {k.Index,3}: page {k.PageType,2}  {k.Param1,3} {k.Param2,3} {k.Param3,3}");

        s.AppendLine($"\n[Custom LED colours — {Leds.Count} entries]");
        foreach (var l in Leds.Where(l => l.Red != 0 || l.Green != 0 || l.Blue != 0))
            s.AppendLine($"  led {l.LedId,3}: {l.Red,3},{l.Green,3},{l.Blue,3}");

        s.AppendLine($"\n[Trigger points — {Triggers.Count} entries]");
        foreach (var t in Triggers.Where(t => t.FastTrigger || t.TriggerPoint != 0 || t.ReleasePoint != 0 || t.AxisType != 0))
            s.AppendLine($"  key {t.Index,3}: fast={(t.FastTrigger ? 1 : 0)}  trigger {t.TriggerPoint}  release {t.ReleasePoint}  axis {t.AxisType}");

        s.AppendLine($"\n[Rapid Trigger — {Rt.Count} entries]");
        foreach (var r in Rt.Where(r => r.AxisType != 0 || r.TriggerStroke > 0 || r.PressRt > 0 || r.ReleaseRt > 0))
            s.AppendLine($"  key {r.Index,3}: axis {r.AxisType}  wholeFast={(r.WholeFast ? 1 : 0)} rampage={(r.Rampage ? 1 : 0)}" +
                         $"  trigger {r.TriggerStroke:0.00}mm  pressRT {r.PressRt:0.00}  releaseRT {r.ReleaseRt:0.00}");

        s.AppendLine($"\n[DKS — {Dks.Count} entries]");
        foreach (var d in Dks)
            s.AppendLine($"  slot {d.Index,3}: make {d.MakeValue1},{d.MakeValue2}  break {d.BreakValue1},{d.BreakValue2}" +
                         $"  actions {d.Action1},{d.Action2},{d.Action3},{d.Action4}" +
                         $"  states [{d.Action1States}|{d.Action2States}|{d.Action3States}|{d.Action4States}]");

        s.AppendLine("\n[Macro offsets]");
        var used = MacroOffsets.Select((v, i) => (v, i)).Where(x => x.v != 0).ToArray();
        s.AppendLine(used.Length == 0 ? "  none" : string.Join("  ", used.Select(x => $"#{x.i}={x.v}")));

        s.AppendLine("\n[Raw replies]");
        foreach (var kv in Raw)
            s.AppendLine($"  {kv.Key}: {kv.Value}");
        return s.ToString();
    }
}
