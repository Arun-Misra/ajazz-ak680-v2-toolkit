using System;
using System.Collections.Generic;

namespace AK680V2Toolkit;

/// <summary>
/// Payload builders reverse-engineered from the AJAZZ web driver (REA analyze_javascript_application).
/// Each builder mirrors the corresponding minified function in protocols/default/index.ts.
/// </summary>
public static class Commands
{
    // Key page types (fX)
    public const byte PageDefault = 0, PageMouse = 1, PageKeyboard = 2, PageConsumer = 3;
    public const byte PageSystem = 4, PageExtra = 5, PageMacro = 6, PageCb = 7, PageDks = 8;
    public const byte PageMt = 9, PageTgl = 10, PageSocd = 11, PageRs = 12, PageFunc = 13;
    public const byte PageEnd = 14, PageMpt = 15;

    /// <summary>
    /// Encodes one userKey into a 4-byte slot. Mirrors pX().
    /// </summary>
    public static void WriteKey(byte[] buffer, int offset, KeyBinding key)
    {
        switch (key.Page)
        {
            case "DEFAULT": break;
            case "MOUSE":
                buffer[offset] = 1; buffer[offset + 1] = (byte)(key.Param1 != 0 ? key.Param1 : 1); buffer[offset + 2] = (byte)key.Value; break;
            case "KEYBOARD":
                buffer[offset] = 2; buffer[offset + 2] = (byte)key.Value; break;
            case "CONSUMER_KEY":
                int c = key.Value; buffer[offset] = 3; buffer[offset + 1] = (byte)(c & 255); buffer[offset + 2] = (byte)((c >> 8) & 255); break;
            case "MACRO": case "MT": case "SOCD": case "RS": case "CB": case "MPT":
                buffer[offset] = (byte)(key.Page switch { "MACRO" => PageMacro, "MT" => PageMt, "SOCD" => PageSocd, "RS" => PageRs, "CB" => PageCb, _ => PageMpt });
                buffer[offset + 1] = (byte)key.Param1; buffer[offset + 2] = (byte)key.Param2; buffer[offset + 3] = (byte)key.Param3; break;
            case "DKS": buffer[offset] = 8; buffer[offset + 1] = (byte)key.Value; break;
            case "TGL": buffer[offset] = 10; buffer[offset + 1] = (byte)key.Value; break;
            case "FUNC":
                int f = key.Value; buffer[offset] = 13; buffer[offset + 1] = (byte)((f >> 16) & 255); buffer[offset + 2] = (byte)((f >> 8) & 255); buffer[offset + 3] = (byte)(f & 255); break;
            case "END": buffer[offset] = 14; buffer[offset + 1] = (byte)key.Param1; buffer[offset + 2] = (byte)key.Param2; break;
        }
    }

    /// <summary>
    /// setKeyData (fZ): 512-byte key matrix. Mirrors fZ().
    /// </summary>
    public static byte[] BuildKeyData(List<KeySlot> keys, List<KeySlot>? fnKeys = null)
    {
        var o = new byte[512];
        foreach (var slot in keys)
        {
            if (slot.Value < 0 || slot.Value >= 128 || slot.Binding is null) continue;
            WriteKey(o, slot.Value * 4, slot.Binding);
        }
        if (fnKeys != null)
            foreach (var slot in fnKeys)
                if (slot.Binding is not null && slot.Value >= 0 && slot.Value < 128)
                    WriteKey(o, slot.Value * 4, slot.Binding);
        return o;
    }

    /// <summary>
    /// Parses a 512-byte GET_KEY / GET_FN_KEY matrix back into KeySlots so an
    /// edit can be merged with the existing bindings instead of zeroing them.
    /// </summary>
    public static List<KeySlot> ParseKeyData(byte[] data)
    {
        var result = new List<KeySlot>();
        for (int i = 0; i < 128 && i * 4 + 3 < data.Length; i++)
        {
            int o = i * 4;
            KeyBinding? binding = data[o] switch
            {
                PageMouse => new KeyBinding { Page = "MOUSE", Param1 = data[o + 1], Value = data[o + 2] },
                PageKeyboard => new KeyBinding { Page = "KEYBOARD", Value = data[o + 2] },
                PageConsumer => new KeyBinding { Page = "CONSUMER_KEY", Value = data[o + 1] | (data[o + 2] << 8) },
                PageMacro => new KeyBinding { Page = "MACRO", Param1 = data[o + 1], Param2 = data[o + 2], Param3 = data[o + 3] },
                PageMt => new KeyBinding { Page = "MT", Param1 = data[o + 1], Param2 = data[o + 2], Param3 = data[o + 3] },
                PageSocd => new KeyBinding { Page = "SOCD", Param1 = data[o + 1], Param2 = data[o + 2], Param3 = data[o + 3] },
                PageRs => new KeyBinding { Page = "RS", Param1 = data[o + 1], Param2 = data[o + 2], Param3 = data[o + 3] },
                PageCb => new KeyBinding { Page = "CB", Param1 = data[o + 1], Param2 = data[o + 2], Param3 = data[o + 3] },
                PageMpt => new KeyBinding { Page = "MPT", Param1 = data[o + 1], Param2 = data[o + 2], Param3 = data[o + 3] },
                PageDks => new KeyBinding { Page = "DKS", Value = data[o + 1] },
                PageTgl => new KeyBinding { Page = "TGL", Value = data[o + 1] },
                PageFunc => new KeyBinding { Page = "FUNC", Value = (data[o + 1] << 16) | (data[o + 2] << 8) | data[o + 3] },
                PageEnd => new KeyBinding { Page = "END", Param1 = data[o + 1], Param2 = data[o + 2] },
                _ => null
            };
            if (binding != null) result.Add(new KeySlot { Value = i, Binding = binding });
        }
        return result;
    }

    /// <summary>
    /// setLEDEffect (vZ): 16-byte payload. Mirrors vZ().
    /// </summary>
    public static byte[] BuildLedEffect(LedEffect e)
    {
        var a = new byte[16];
        a[0] = e.Mode; a[1] = e.Red; a[2] = e.Green; a[3] = e.Blue; a[4] = 255;
        a[5] = e.SecondaryRed; a[6] = e.SecondaryGreen; a[7] = e.SecondaryBlue;
        a[8] = e.ColorMode; a[9] = e.Brightness; a[10] = e.Speed; a[11] = e.Direction;
        a[12] = e.EffectModeType; a[14] = 170; a[15] = 85;
        return a;
    }

    /// <summary>
    /// Parses GET_LED_EFFECT (16 bytes) — mirrors _Z().
    /// </summary>
    public static LedEffect ParseLedEffect(byte[] r)
    {
        var b = new byte[16];
        Buffer.BlockCopy(r, 0, b, 0, Math.Min(r.Length, 16));
        return new LedEffect
        {
            Mode = b[0], Red = b[1], Green = b[2], Blue = b[3], DriverSetting = b[4],
            SecondaryRed = b[5], SecondaryGreen = b[6], SecondaryBlue = b[7],
            ColorMode = b[8], Brightness = b[9], Speed = b[10], Direction = b[11],
            EffectModeType = b[12], CheckCodeL = b[14], CheckCodeH = b[15]
        };
    }

    /// <summary>
    /// setCustomLEDData (bZ): 128 entries x 4 bytes [ledId, red, green, blue].
    /// </summary>
    public static byte[] BuildCustomLedData(List<LedColor> colors)
    {
        var data = new byte[512];
        for (int i = 0; i < colors.Count && i < 128; i++)
        {
            var c = colors[i];
            int t = i * 4;
            data[t] = (byte)c.LedId; data[t + 1] = c.Red; data[t + 2] = c.Green; data[t + 3] = c.Blue;
        }
        return data;
    }

    /// <summary>setMagneticAxisRT (cmd 39): 1024 bytes, 128 x 8. Mirrors IZ().</summary>
    public static byte[] BuildMagneticAxisRt(RtConfig[] configs, int rtPrecision)
    {
        var data = new byte[1024];
        double scale = rtPrecision > 0 ? 1000.0 : 100.0;
        for (int i = 0; i < 128 && i < configs.Length; i++)
        {
            var c = configs[i];
            if (c is null) continue;
            int flags = 0;
            if (c.IsWholeFast) flags |= 1;
            if (c.IsRampage) flags |= 2;
            int trigger = (int)Math.Round(c.TriggerKeyStroke * scale);
            int press = (int)Math.Round(c.PressRt * scale);
            int release = (int)Math.Round(c.ReleaseRt * scale);
            int o = i * 8;
            data[o] = c.AxisType;
            data[o + 1] = (byte)flags;
            data[o + 2] = (byte)(trigger & 255);
            data[o + 3] = (byte)((trigger >> 8) & 255);
            data[o + 4] = (byte)(press & 255);
            data[o + 5] = (byte)((press >> 8) & 255);
            data[o + 6] = (byte)(release & 255);
            data[o + 7] = (byte)((release >> 8) & 255);
        }
        return data;
    }

    /// <summary>setMagneticAxisDKSData (cmd 40): 1024 bytes, 64 x 16. Mirrors BZ().</summary>
    public static byte[] BuildMagneticAxisDks(IEnumerable<DksConfig> configs)
    {
        var data = new byte[1024];
        foreach (var c in configs)
        {
            if (c is null || c.Index < 0 || c.Index >= 64) continue;
            int o = c.Index * 16;
            data[o] = c.MakeValue1;
            data[o + 1] = c.MakeValue2;
            data[o + 2] = c.BreakValue1;
            data[o + 3] = c.BreakValue2;
            data[o + 5] = c.Action1;
            data[o + 7] = c.Action2;
            data[o + 9] = c.Action3;
            data[o + 11] = c.Action4;
            var states = new[] { c.Action1States, c.Action2States, c.Action3States, c.Action4States };
            for (int t = 0; t < 4; t++)
            {
                int pressMask = 0, holdMask = 0;
                for (int a = 0; a < 4; a++)
                {
                    var s = states[a];
                    if (s != null && s.Length > t)
                    {
                        if (s[t] == 1) pressMask |= 1 << a;
                        if (s[t] == 2) holdMask |= 1 << a;
                    }
                }
                data[o + 12 + t] = (byte)(pressMask | (holdMask << 4));
            }
        }
        return data;
    }

    /// <summary>
    /// Parses a 1024-byte GET_MAGNETIC_AXIS_DKS_DATA block back into DksConfig
    /// objects. The four state bytes are nibble-packed press/hold masks.
    /// </summary>
    public static List<DksConfig> ParseMagneticAxisDks(byte[] data)
    {
        var result = new List<DksConfig>();
        for (int i = 0; i < 64 && i * 16 + 15 < data.Length; i++)
        {
            int o = i * 16;
            if (data[o] == 0 && data[o + 1] == 0 && data[o + 2] == 0 && data[o + 3] == 0) continue;
            var c = new DksConfig
            {
                Index = i, MakeValue1 = data[o], MakeValue2 = data[o + 1],
                BreakValue1 = data[o + 2], BreakValue2 = data[o + 3],
                Action1 = data[o + 5], Action2 = data[o + 7],
                Action3 = data[o + 9], Action4 = data[o + 11]
            };
            var all = new[] { c.Action1States, c.Action2States, c.Action3States, c.Action4States };
            for (int t = 0; t < 4; t++)
            {
                byte b = data[o + 12 + t];
                int press = b & 15, hold = (b >> 4) & 15;
                for (int a = 0; a < 4; a++)
                    all[a][t] = (byte)((press & (1 << a)) != 0 ? 1 : (hold & (1 << a)) != 0 ? 2 : 0);
            }
            result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// setGameMode (qX): 56-byte payload. Mirrors qX().
    /// </summary>
    public static byte[] BuildGameMode(GameMode g)
    {
        var a = new byte[56];
        a[1] = g.Mode; a[2] = g.FnSwitch; a[3] = g.SleepTime; a[4] = g.KeyDelay;
        a[5] = g.ReportRate; a[6] = g.SystemMode; a[7] = g.TftDisplayTime;
        a[8] = (byte)(g.TopDeadZone * 100); a[9] = (byte)(g.BottomDeadZone * 100);
        a[11] = g.StabilityMode; a[14] = g.AutoCalibration; a[15] = g.SingleKeyWakeup;
        a[16] = g.PushButtonMode; a[17] = g.NkroSwitch;
        int rate = g.WirelessReportRate; a[18] = (byte)(rate & 255); a[19] = (byte)((rate >> 8) & 255);
        a[20] = g.PowerMode;
        return a;
    }

    /// <summary>
    /// setTriggerConfig (RZ): 512-byte payload. Mirrors RZ().
    /// </summary>
    public static byte[] BuildTriggerConfig(TriggerConfig[] triggers)
    {
        var i = new byte[512];
        for (int e = 0; e < 128 && e < triggers.Length; e++)
        {
            var n = triggers[e];
            if (n is null) continue;
            int t = e * 4;
            i[t] = (byte)(n.IsFastTrigger ? 1 : 0);
            i[t + 1] = n.TriggerPoint; i[t + 2] = n.ReleasePoint; i[t + 3] = n.AxisType;
        }
        return i;
    }

    /// <summary>
    /// setProfile (iQ): custom header [0xAA, SET_PROFILE, index]. Mirrors iQ().
    /// </summary>
    public static byte[] BuildProfileHeader(byte index) => new byte[] { 0xAA, Cmd.SetProfile, index };

    /// <summary>
    /// setMacroData (PZ): builds the 400-byte macro index plus concatenated action blocks.
    /// Returns (indexBuffer, actionBuffer). The caller sends index first with no last-packet
    /// flag, then actions at addrStart=400 with the last-packet flag.
    /// </summary>
    public static (byte[] index, byte[] actions) BuildMacroData(List<MacroEntry> macros)
    {
        var index = new byte[400];
        int addr = 400;
        var blocks = new List<(byte[] data, int size)>();
        foreach (var m in macros)
        {
            if (m.MacroId < 0 || m.MacroId >= 100) throw new ArgumentOutOfRangeException(nameof(m.MacroId));
            if (m.Actions.Count == 0) continue;
            int size = 4 + m.Actions.Count * 4;
            var block = new byte[size];
            int len = m.Actions.Count * 2;
            block[0] = (byte)(len & 255); block[1] = (byte)(len >> 8 & 255);
            for (int i = 0; i < m.Actions.Count; i++)
            {
                var a = m.Actions[i];
                int p = 4 + i * 4;
                block[p] = (byte)(a.Delay & 255);
                block[p + 1] = (byte)(a.Delay >> 8 & 255);
                block[p + 2] = (byte)(a.KeyCode & 255);
                block[p + 3] = (byte)((a.ActionType == 1 || a.ActionType == 2)
                    ? (a.IsPress ? 144 : 16)
                    : (a.IsPress ? 176 : 48));
            }
            int u = m.MacroId * 4;
            if (u + 3 < index.Length)
            {
                index[u] = (byte)(addr & 255);
                index[u + 1] = (byte)(addr >> 8 & 255);
                index[u + 2] = (byte)(addr >> 16 & 255);
                index[u + 3] = (byte)(addr >> 24 & 255);
            }
            blocks.Add((block, size));
            addr += size;
        }
        int total = 0; foreach (var b in blocks) total += b.size;
        var actions = new byte[total];
        int offset = 0;
        foreach (var b in blocks) { Buffer.BlockCopy(b.data, 0, actions, offset, b.size); offset += b.size; }
        return (index, actions);
    }

    /// <summary>
    /// Parses GET_DEVICE_INFO response (GX).
    /// </summary>
    public static DeviceInfo ParseDeviceInfo(byte[] payload)
    {
        var i = new byte[48];
        Buffer.BlockCopy(payload, 0, i, 0, Math.Min(payload.Length, 48));
        int U16(int a) => i[a] | (i[a + 1] << 8);
        return new DeviceInfo
        {
            RomSize = i[0], MacroSpaceSize = U16(2), Vid = U16(4), Pid = U16(6),
            Version = (((i[8] & 15) + ((i[8] & 240) >> 4) * 10 + i[9] * 100) / 100.0).ToString("0.00"),
            Sensor = U16(10), Manufacturer = U16(12), Product = U16(14),
            WorkMode = i[16], BatteryLevel = i[17], ChargeStatus = i[18], CurrentProfile = i[19],
            AxisInfo = U16(20), TftMaxFrames = U16(22), GifMaxFrames = U16(24), LedMaxFrames = U16(26),
            TftDirection = i[28], RtPrecision = i[29], FrameVersion = i[30], LightingVersion = i[31],
            FirmwareStatus = i[32], MaxProfile = i[33], TftColorByteOrder = i[35]
        };
    }
}

public class KeyBinding
{
    public string Page { get; set; } = "DEFAULT";
    public int Value { get; set; }
    public int Param1 { get; set; }
    public int Param2 { get; set; }
    public int Param3 { get; set; }
}
public class KeySlot { public int Value { get; set; } public KeyBinding? Binding { get; set; } }
public class LedEffect
{
    public byte Mode, Red, Green, Blue, DriverSetting;
    public byte SecondaryRed, SecondaryGreen, SecondaryBlue;
    public byte ColorMode, Brightness, Speed, Direction, EffectModeType;
    public byte CheckCodeL, CheckCodeH;
}

public class LedColor { public int LedId; public byte Red, Green, Blue; }
public class GameMode
{
    public byte Mode, FnSwitch, SleepTime, KeyDelay, ReportRate, SystemMode, TftDisplayTime;
    public double TopDeadZone, BottomDeadZone;
    public byte StabilityMode, AutoCalibration, SingleKeyWakeup;
    public byte PushButtonMode, NkroSwitch, PowerMode;
    public int WirelessReportRate;
}
public class TriggerConfig
{
    public bool IsFastTrigger;
    public byte TriggerPoint, ReleasePoint, AxisType;
}

public class RtConfig
{
    public byte AxisType;
    public bool IsWholeFast, IsRampage;
    public double TriggerKeyStroke, PressRt, ReleaseRt;
}

public class DksConfig
{
    public int Index;
    public byte MakeValue1, MakeValue2, BreakValue1, BreakValue2;
    public byte Action1, Action2, Action3, Action4;
    public byte[] Action1States = new byte[4];
    public byte[] Action2States = new byte[4];
    public byte[] Action3States = new byte[4];
    public byte[] Action4States = new byte[4];
}
public class MacroEntry
{
    public int MacroId { get; set; }
    public List<MacroAction> Actions { get; set; } = new();
}
public class MacroAction
{
    public int Delay { get; set; }
    public int KeyCode { get; set; }
    public int ActionType { get; set; }
    public bool IsPress { get; set; }
}
public class DeviceInfo
{
    public byte RomSize; public int MacroSpaceSize, Vid, Pid; public string Version = "";
    public int Sensor, Manufacturer, Product; public byte WorkMode, BatteryLevel, ChargeStatus, CurrentProfile;
    public int AxisInfo, TftMaxFrames, GifMaxFrames, LedMaxFrames;
    public byte TftDirection, RtPrecision, FrameVersion, LightingVersion, FirmwareStatus, MaxProfile, TftColorByteOrder;
}