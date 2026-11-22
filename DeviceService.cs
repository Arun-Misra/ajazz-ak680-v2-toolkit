using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AK680V2Toolkit;

public class DeviceService
{
    public NativeHidDevice? Device { get; private set; }
    public bool IsConnected => Device?.IsOpen == true;
    public string? ProductName => Device?.ProductName;
    public string? LastError { get; private set; }

    // Cached buffers for the 30 FPS animation path — avoids ~1200 allocations/sec.
    private byte[]? _frameData;
    private byte[][]? _frameReports;
    private byte[][]? _frameWrites;

    public static IReadOnlyList<NativeHidDevice> FindDevices() =>
        NativeHidDevice.Enumerate()
            .Where(d => (d.ProductName ?? "").Contains("AJAZZ", StringComparison.OrdinalIgnoreCase)
                     && d.OutputReportLength >= 32 && d.OutputReportLength <= 128)
            .OrderByDescending(d => d.OutputReportLength)
            .ToList();

    public bool Connect()
    {
        string? error = null;
        foreach (var candidate in FindDevices())
        {
            try
            {
                candidate.Open();
                Device = candidate;
                // A previous session that was killed while the Hall debugger was active
                // can leave the firmware streaming 0xFB telemetry. Those unsolicited
                // reports would otherwise be mistaken for command replies, so leave the
                // device in its normal state before doing anything else.
                try
                {
                    SendReport(0, HidProtocol.BuildReport(Cmd.SetSimulationTestOff, 0, 0, null, null,
                        new byte[] { 0xAA, Cmd.SetSimulationTestOff, 0, 0, 0, 0, 0, 0 }, false));
                    for (int i = 0; i < 16 && candidate.ReadReport(15) != null; i++) { }
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message.Contains("(5)")
                    ? "device is open in another app (close the AJAZZ browser tab)"
                    : ex.Message;
                candidate.Dispose();
            }
        }
        LastError = error ?? "no AJAZZ HID device found";
        return false;
    }

    public void Disconnect()
    {
        Device?.Dispose();
        Device = null;
    }

    /// <summary>
    /// Mirrors WebHID device.sendReport(reportId, data). The full report buffer is written
    /// with the native output report length, matching the HID class driver contract.
    /// </summary>
    public bool SendReport(byte reportId, byte[] data)
    {
        if (Device is null || !Device.IsOpen) return false;
        // Live WebHID evidence: reportId 0, 64-byte payload. Win32 HID class
        // expects the report-id byte first, followed by the payload.
        int length = Math.Max((int)Device.OutputReportLength, data.Length + 1);
        var report = new byte[length];
        report[0] = reportId;
        Buffer.BlockCopy(data, 0, report, 1, Math.Min(data.Length, length - 1));
        return Device.WriteReport(report);
    }

    public async Task<bool> SendCommandAsync(byte cmd, byte[] data, int contentSize,
        int addrStart = 0, byte[]? otherHeader = null, byte[]? customHeader = null,
        int timeoutMs = 500, bool skipResponse = false, CancellationToken ct = default, bool needLastFlag = true)
    {
        if (!IsConnected) return false;
        int chunk = HidProtocol.ChunkDataSize;
        int packets = Math.Max(1, (int)Math.Ceiling((double)contentSize / chunk));
        for (int t = 0; t < packets; t++)
        {
            if (ct.IsCancellationRequested) return false;
            int addr = addrStart + t * chunk;
            int remaining = contentSize - t * chunk;
            bool last = t == packets - 1;
            int size = last ? Math.Max(0, remaining) : chunk;
            byte[]? slice = null;
            if (data.Length > 0)
            {
                int start = t * chunk, end = Math.Min(start + chunk, data.Length);
                if (end > start) { slice = new byte[end - start]; Buffer.BlockCopy(data, start, slice, 0, slice.Length); }
            }
            var report = HidProtocol.BuildReport(cmd, size, addr, slice, otherHeader, customHeader, needLastFlag && last);
            try
            {
                SendReport(0, report);
                if (!skipResponse) Device!.ReadReport(timeoutMs);
            }
            catch (Exception ex) { Console.WriteLine($"SendCommand {cmd} packet {t}: {ex.Message}"); return false; }
        }
        return true;
    }

    public async Task<List<byte[]>?> SendCommandWithResponseAsync(byte cmd, byte[] data, int contentSize,
        int addrStart = 0, byte[]? otherHeader = null, int timeoutMs = 2000, byte[]? customHeader = null)
    {
        if (!IsConnected) return null;
        int chunk = HidProtocol.ChunkDataSize;
        int packets = Math.Max(1, (int)Math.Ceiling((double)contentSize / chunk));
        var result = new List<byte[]>();
        for (int t = 0; t < packets; t++)
        {
            int addr = addrStart + t * chunk;
            int remaining = contentSize - t * chunk;
            bool last = t == packets - 1;
            int size = last ? Math.Max(0, remaining) : chunk;
            byte[]? slice = null;
            if (data.Length > 0)
            {
                int start = t * chunk, end = Math.Min(start + chunk, data.Length);
                if (end > start) { slice = new byte[end - start]; Buffer.BlockCopy(data, start, slice, 0, slice.Length); }
            }
            SendReport(0, HidProtocol.BuildReport(cmd, size, addr, slice, otherHeader, customHeader, last));
            var response = Device!.ReadReport(timeoutMs);
            if (response != null) result.Add(response);
            else break; // unsupported command — don't burn the timeout on every packet
        }
        return result;
    }

    public Task<bool> FactoryResetAsync(byte type = FactoryResetType.ResetAll) =>
        SendCommandAsync(Cmd.SetFactoryReset, new[] { type }, 1, otherHeader: new[] { type });

    public async Task<DeviceInfo?> GetDeviceInfoAsync()
    {
        var responses = await SendCommandWithResponseAsync(Cmd.GetDeviceInfo, Array.Empty<byte>(), 48);
        if (responses == null || responses.Count == 0) return null;
        var payload = new List<byte>();
        foreach (var r in responses) payload.AddRange(r.Skip(HidProtocol.HeaderSize));
        return Commands.ParseDeviceInfo(payload.ToArray());
    }

    public Task<bool> SetKeyDataAsync(List<KeySlot> keys, List<KeySlot>? fnKeys = null) =>
        SendCommandAsync(Cmd.SetKey, Commands.BuildKeyData(keys, fnKeys), 512, timeoutMs: 2000);

    /// <summary>GET_KEY (18) — read the 512-byte normal key matrix.</summary>
    public async Task<List<KeySlot>?> GetKeyDataAsync(int timeoutMs = 700)
    {
        var d = await ReadBlockAsync(Cmd.GetKey, 512, timeoutMs: timeoutMs);
        return d == null ? null : Commands.ParseKeyData(d);
    }

    /// <summary>GET_FN_KEY (22) — read the 512-byte Fn-layer key matrix.</summary>
    public async Task<List<KeySlot>?> GetFnKeyDataAsync(int timeoutMs = 700)
    {
        var d = await ReadBlockAsync(Cmd.GetFnKey, 512, timeoutMs: timeoutMs);
        return d == null ? null : Commands.ParseKeyData(d);
    }

    /// <summary>SET_FN_KEY (38) — write the 512-byte Fn-layer matrix (separate from SET_KEY).</summary>
    public Task<bool> SetFnKeyDataAsync(IEnumerable<KeySlot> fnKeys) =>
        SendCommandAsync(Cmd.SetFnKey, Commands.BuildKeyData(fnKeys.ToList()), 512, timeoutMs: 2000);

    public Task<bool> SetLedEffectAsync(LedEffect e) =>
        SendCommandAsync(Cmd.SetLedEffect, Commands.BuildLedEffect(e), 16);

    public async Task<LedEffect?> GetLedEffectAsync()
    {
        var responses = await SendCommandWithResponseAsync(Cmd.GetLedEffect, Array.Empty<byte>(), 16);
        if (responses == null || responses.Count == 0) return null;
        var payload = new List<byte>();
        foreach (var r in responses) payload.AddRange(r.Skip(HidProtocol.HeaderSize));
        return Commands.ParseLedEffect(payload.ToArray());
    }

    public Task<bool> SetCustomLedDataAsync(List<LedColor> colors, bool skipResponse = false)
    {
        var data = Commands.BuildCustomLedData(colors);
        return SendCommandAsync(Cmd.SetCustomLedData, data, 512, timeoutMs: 2000, skipResponse: skipResponse);
    }

    /// <summary>
    /// Animation frame path: fire-and-forget writes, no per-packet wait.
    /// </summary>
    public Task<bool> SendCustomFrameAsync(List<LedColor> colors)
    {
        if (!IsConnected) return Task.FromResult(false);
        int chunk = HidProtocol.ChunkDataSize;
        int count = Math.Min(colors.Count, 128);
        int length = count * 4;
        int packets = Math.Max(1, (int)Math.Ceiling((double)length / chunk));
        int reportLen = Math.Max(1, (int)Device!.OutputReportLength);

        _frameData ??= new byte[512];
        _frameReports ??= new byte[packets][];
        _frameWrites ??= new byte[packets][];
        Array.Clear(_frameData, 0, length);
        for (int i = 0; i < count; i++)
        {
            var c = colors[i];
            int t = i * 4;
            _frameData[t] = (byte)c.LedId;
            _frameData[t + 1] = c.Red;
            _frameData[t + 2] = c.Green;
            _frameData[t + 3] = c.Blue;
        }
        var data = _frameData;

        for (int t = 0; t < packets; t++)
        {
            int start = t * chunk;
            int size = Math.Min(chunk, length - start);
            bool last = t == packets - 1;
            var report = _frameReports[t] ??= new byte[HidProtocol.ReportSize];
            // Build the report in place in the cached packet, then append payload.
            Array.Clear(report, 0, report.Length);
            report[0] = HidProtocol.StartByte;
            report[1] = Cmd.SetCustomLedData;
            report[2] = (byte)size;
            report[3] = (byte)(start & 0xFF);
            report[4] = (byte)((start >> 8) & 0xFF);
            report[6] = (byte)(last ? 1 : 0);
            Buffer.BlockCopy(data, start, report, HidProtocol.HeaderSize, size);

            var write = _frameWrites[t] ??= new byte[reportLen];
            write[0] = 0; // report id
            Buffer.BlockCopy(report, 0, write, 1, Math.Min(report.Length, reportLen - 1));
            if (last)
                Device.WriteReportFastWait(write, 8); // frame delivered before the next one
            else
                Device.WriteReportFast(write);
        }
        return Task.FromResult(true);
    }

    /// <summary>
    /// Sends a compact frame of only the real keys (68 x 4 = 272 bytes = 5 packets)
    /// so every key can be updated each frame without a full 128-key, 10-packet frame.
    /// Each entry carries its own LED id, so the address is just the byte offset.
    /// </summary>
    public Task<bool> SendCompactFrameAsync(LedColor[] colors)
    {
        if (!IsConnected) return Task.FromResult(false);
        var data = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            data[i * 4] = (byte)colors[i].LedId;
            data[i * 4 + 1] = colors[i].Red;
            data[i * 4 + 2] = colors[i].Green;
            data[i * 4 + 3] = colors[i].Blue;
        }
        int chunk = HidProtocol.ChunkDataSize;
        int packets = Math.Max(1, (int)Math.Ceiling((double)data.Length / chunk));
        int reportLen = Math.Max(1, (int)Device!.OutputReportLength);
        for (int t = 0; t < packets; t++)
        {
            int start = t * chunk;
            int end = Math.Min(start + chunk, data.Length);
            var slice = new byte[end - start];
            Buffer.BlockCopy(data, start, slice, 0, slice.Length);
            bool last = t == packets - 1;
            int size = last ? data.Length - start : chunk;
            var report = HidProtocol.BuildReport(Cmd.SetCustomLedData, size, start, slice, null, null, last);
            var buffer = new byte[reportLen];
            buffer[0] = 0;
            Buffer.BlockCopy(report, 0, buffer, 1, Math.Min(report.Length, reportLen - 1));
            Device.WriteReportFast(buffer);
        }
        return Task.FromResult(true);
    }

    /// <summary>
    /// Experimental: sends frames through the AJAZZ music plugin's command (0x32 /
    /// GET_LED_DATA used as a writer) in 56-byte chunks, with the per-packet reply
    /// read the plugin performs. Intended for use with firmware mode 100.
    /// </summary>
    public Task<bool> SendMusicFrameAsync(LedColor[] colors)
    {
        if (!IsConnected) return Task.FromResult(false);
        var data = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            data[i * 4] = (byte)colors[i].LedId;
            data[i * 4 + 1] = colors[i].Red;
            data[i * 4 + 2] = colors[i].Green;
            data[i * 4 + 3] = colors[i].Blue;
        }
        int chunk = HidProtocol.ChunkDataSize;
        for (int off = 0; off < data.Length; off += chunk)
        {
            int size = Math.Min(chunk, data.Length - off);
            var slice = new byte[size];
            Buffer.BlockCopy(data, off, slice, 0, size);
            bool last = off + chunk >= data.Length;
            var report = HidProtocol.BuildReport(Cmd.GetLedData, size, off, slice, null, null, last);
            SendReport(0, report);
            try { Device!.ReadReport(40); } catch { }
        }
        return Task.FromResult(true);
    }

    /// <summary>
    /// Updates only a contiguous range of LEDs (fast animation path).
    /// </summary>
    public Task<bool> SendCustomLedRangeAsync(int startLed, List<LedColor> colors)
    {
        if (!IsConnected) return Task.FromResult(false);
        var data = new byte[colors.Count * 4];
        for (int i = 0; i < colors.Count; i++)
        {
            data[i * 4] = (byte)colors[i].LedId;
            data[i * 4 + 1] = colors[i].Red;
            data[i * 4 + 2] = colors[i].Green;
            data[i * 4 + 3] = colors[i].Blue;
        }
        int chunk = HidProtocol.ChunkDataSize;
        int packets = Math.Max(1, (int)Math.Ceiling((double)data.Length / chunk));
        int reportLen = Math.Max(1, (int)Device!.OutputReportLength);
        for (int t = 0; t < packets; t++)
        {
            int start = t * chunk;
            int end = Math.Min(start + chunk, data.Length);
            var slice = new byte[end - start];
            Buffer.BlockCopy(data, start, slice, 0, slice.Length);
            bool last = t == packets - 1;
            int size = last ? data.Length - start : chunk;
            var report = HidProtocol.BuildReport(Cmd.SetCustomLedData, size, startLed * 4 + start, slice, null, null, last);
            var buffer = new byte[reportLen];
            buffer[0] = 0;
            Buffer.BlockCopy(report, 0, buffer, 1, Math.Min(report.Length, reportLen - 1));
            Device.WriteReportFast(buffer);
        }
        return Task.FromResult(true);
    }

    private async Task<byte[]?> ReadBlockAsync(byte cmd, int size, int addrStart = 0, int timeoutMs = 700)
    {
        var frames = await SendCommandWithResponseAsync(cmd, Array.Empty<byte>(), size,
            addrStart, timeoutMs: timeoutMs);
        if (frames == null || frames.Count == 0) return null;
        return frames.SelectMany(f => f.Skip(HidProtocol.HeaderSize)).Take(size).ToArray();
    }

    /// <summary>Reads every block the firmware answers, with the exact content size
    /// each command expects (the RT/DKS tables are 1024 bytes, not 512).</summary>
    public async Task<DeviceSnapshot> GetSnapshotAsync()
    {
        var snap = new DeviceSnapshot { ReadAt = DateTime.Now };

        void Raw(string name, byte[]? d) =>
            snap.Raw[name] = d == null ? "no reply" : Convert.ToHexString(d.Take(48).ToArray()) +
                             (d.Length > 48 ? $" … ({d.Length} bytes)" : "");

        var dInfo = await ReadBlockAsync(Cmd.GetDeviceInfo, 48);
        Raw("GetDeviceInfo", dInfo);
        snap.Info = dInfo == null ? null : Commands.ParseDeviceInfo(dInfo);

        var dGame = await ReadBlockAsync(Cmd.GetGameMode, 56);
        Raw("GetGameMode", dGame);
        if (dGame != null && dGame.Length >= 21)
            snap.Game = new GameInfo
            {
                GameMode = dGame[1], FnSwitch = dGame[2], SleepTime = dGame[3], KeyDelay = dGame[4],
                ReportRate = dGame[5], SystemMode = dGame[6], TftDisplayTime = dGame[7],
                TopDeadZone = dGame[8] / 100.0, BottomDeadZone = dGame[9] / 100.0,
                StabilityMode = dGame[11], AutoCalibration = dGame[14], SingleKeyWakeup = dGame[15],
                PushButtonMode = dGame[16], NkroSwitch = dGame[17],
                WirelessReportRate = dGame[18] | (dGame[19] << 8), PowerMode = dGame[20]
            };

        var dEffect = await ReadBlockAsync(Cmd.GetLedEffect, 16);
        Raw("GetLedEffect", dEffect);
        snap.Effect = dEffect == null ? null : Commands.ParseLedEffect(dEffect);

        int precision = snap.Info?.RtPrecision ?? 0;
        double travelScale = precision > 0 ? 1000.0 : 100.0;
        double rtScale = precision == 2 ? 1000.0 : 100.0;

        var dKeys = await ReadBlockAsync(Cmd.GetKey, 512);
        Raw("GetKey", dKeys);
        if (dKeys != null)
            for (int i = 0; i < 128 && i * 4 + 3 < dKeys.Length; i++)
                snap.Keys.Add(new KeyRaw { Index = i, PageType = dKeys[i * 4], Param1 = dKeys[i * 4 + 1],
                    Param2 = dKeys[i * 4 + 2], Param3 = dKeys[i * 4 + 3] });

        var dLeds = await ReadBlockAsync(Cmd.GetCustomLedData, 512);
        Raw("GetCustomLedData", dLeds);
        if (dLeds != null)
            for (int i = 0; i < 128 && i * 4 + 3 < dLeds.Length; i++)
                snap.Leds.Add(new LedColor { LedId = dLeds[i * 4], Red = dLeds[i * 4 + 1],
                    Green = dLeds[i * 4 + 2], Blue = dLeds[i * 4 + 3] });

        var dTrigger = await ReadBlockAsync(Cmd.GetTriggerKey, 512, timeoutMs: 700);
        Raw("GetTriggerKey", dTrigger);
        if (dTrigger != null)
            for (int i = 0; i < 128 && i * 4 + 3 < dTrigger.Length; i++)
                snap.Triggers.Add(new TriggerRaw { Index = i, FastTrigger = dTrigger[i * 4] == 1,
                    TriggerPoint = dTrigger[i * 4 + 1], ReleasePoint = dTrigger[i * 4 + 2],
                    AxisType = dTrigger[i * 4 + 3] });

        var dRt = await ReadBlockAsync(Cmd.GetMagneticAxisRt, 1024);
        Raw("GetMagneticAxisRt", dRt);
        if (dRt != null)
            for (int i = 0; i < 128 && i * 8 + 7 < dRt.Length; i++)
            {
                int o = i * 8;
                int flags = dRt[o + 1];
                snap.Rt.Add(new RtRaw
                {
                    Index = i, AxisType = dRt[o], WholeFast = (flags & 1) != 0, Rampage = (flags & 2) != 0,
                    TriggerStroke = (dRt[o + 2] | (dRt[o + 3] << 8)) / travelScale,
                    PressRt = (dRt[o + 4] | (dRt[o + 5] << 8)) / rtScale,
                    ReleaseRt = (dRt[o + 6] | (dRt[o + 7] << 8)) / rtScale
                });
            }

        var dDks = await ReadBlockAsync(Cmd.GetMagneticAxisDksData, 1024);
        Raw("GetMagneticAxisDksData", dDks);
        if (dDks != null)
            for (int i = 0; i < 64 && i * 16 + 15 < dDks.Length; i++)
            {
                int o = i * 16;
                if (dDks[o] == 0 && dDks[o + 1] == 0 && dDks[o + 2] == 0 && dDks[o + 3] == 0) continue;
                static string States(byte b) => (b & 15) != 0 ? "press" : ((b & 240) >> 4) != 0 ? "hold" : "-";
                snap.Dks.Add(new DksRaw
                {
                    Index = i, MakeValue1 = dDks[o], MakeValue2 = dDks[o + 1],
                    BreakValue1 = dDks[o + 2], BreakValue2 = dDks[o + 3],
                    Action1 = dDks[o + 5], Action2 = dDks[o + 7], Action3 = dDks[o + 9], Action4 = dDks[o + 11],
                    Action1States = States(dDks[o + 12]), Action2States = States(dDks[o + 13]),
                    Action3States = States(dDks[o + 14]), Action4States = States(dDks[o + 15])
                });
            }

        var dMacro = await ReadBlockAsync(Cmd.GetMacro, 400);
        Raw("GetMacro(index)", dMacro);
        if (dMacro != null)
            for (int i = 0; i < 100 && i * 4 + 3 < dMacro.Length; i++)
                snap.MacroOffsets[i] = dMacro[i * 4] | (dMacro[i * 4 + 1] << 8) |
                                       (dMacro[i * 4 + 2] << 16) | (dMacro[i * 4 + 3] << 24);

        return snap;
    }

    public Task<bool> SetGameModeAsync(GameMode g) =>
        SendCommandAsync(Cmd.SetGameMode, Commands.BuildGameMode(g), 56);

    public Task<bool> SetTriggerConfigAsync(TriggerConfig[] t) =>
        SendCommandAsync(Cmd.SetTriggerKey, Commands.BuildTriggerConfig(t), 512, timeoutMs: 2000);

    public async Task<TriggerConfig[]?> GetTriggerConfigAsync(int timeoutMs = 700)
    {
        var d = await ReadBlockAsync(Cmd.GetTriggerKey, 512, timeoutMs: timeoutMs);
        if (d == null) return null;
        var result = new TriggerConfig[128];
        for (int i = 0; i < 128; i++)
        {
            int o = i * 4;
            result[i] = new TriggerConfig
            {
                IsFastTrigger = d[o] == 1,
                TriggerPoint = d[o + 1],
                ReleasePoint = d[o + 2],
                AxisType = d[o + 3]
            };
        }
        return result;
    }

    public Task<bool> SetMagneticAxisRtAsync(RtConfig[] configs, int rtPrecision = 0) =>
        SendCommandAsync(Cmd.SetMagneticAxisRt, Commands.BuildMagneticAxisRt(configs, rtPrecision), 1024, timeoutMs: 2000);

    public Task<bool> SetMagneticAxisDksAsync(IEnumerable<DksConfig> configs) =>
        SendCommandAsync(Cmd.SetMagneticAxisDksData, Commands.BuildMagneticAxisDks(configs), 1024, timeoutMs: 2000);

    public async Task<bool> SetProfileAsync(byte index)
    {
        // The driver treats byte 2 of the reply as the acknowledgement: it must echo
        // the requested slot. A bare "some report arrived" check is not sufficient.
        var header = new byte[] { 0xAA, Cmd.SetProfile, index };
        var frames = await SendCommandWithResponseAsync(Cmd.SetProfile, new[] { index }, 1,
            customHeader: header, timeoutMs: 2000);
        return frames != null && frames.Count > 0 && frames[0].Length > 2 && frames[0][2] == index;
    }

    public async Task<bool> SetMacroDataAsync(List<MacroEntry> macros)
    {
        var (index, actions) = Commands.BuildMacroData(macros);
        bool ok1 = await SendCommandAsync(Cmd.SetMacro, index, 400, skipResponse: true, needLastFlag: false);
        if (!ok1 || actions.Length == 0) return ok1;
        return await SendCommandAsync(Cmd.SetMacro, actions, actions.Length, addrStart: 400, skipResponse: true);
    }

    // ---- Hall-effect / magnetic axis support -------------------------------------

    public sealed class HallReading
    {
        public int KeyValue;
        public int CalibrationStatus;
        public int AdcValue;
        public int CurrentStroke;
    }

    /// <summary>
    /// GET_MAGNETIC_AXIS_STATUS (104). Request is 8 bytes per key with the key value in
    /// byte 0; reply is 8 bytes per key: [status, adcLo, adcHi, strokeLo, strokeHi, ...].
    /// currentStroke is the raw Hall travel value (not yet normalised to millimetres).
    /// </summary>
    public async Task<List<HallReading>?> GetMagneticAxisStatusAsync(int[] keyValues, int timeoutMs = 2000)
    {
        if (!IsConnected || keyValues.Length == 0) return null;
        var request = new byte[keyValues.Length * 8];
        for (int i = 0; i < keyValues.Length; i++) request[i * 8] = (byte)keyValues[i];
        var frames = await SendCommandWithResponseAsync(Cmd.GetMagneticAxisStatus, request,
            request.Length, timeoutMs: timeoutMs);
        if (frames == null || frames.Count == 0) return null;
        var payload = frames.SelectMany(f => f.Skip(HidProtocol.HeaderSize)).ToArray();
        if (payload.Length < keyValues.Length * 5) return null;
        var result = new List<HallReading>(keyValues.Length);
        for (int i = 0; i < keyValues.Length; i++)
        {
            int o = i * 8;
            if (o + 5 > payload.Length) break;
            result.Add(new HallReading
            {
                KeyValue = keyValues[i],
                CalibrationStatus = payload[o],
                AdcValue = payload[o + 1] | (payload[o + 2] << 8),
                CurrentStroke = payload[o + 3] | (payload[o + 4] << 8)
            });
        }
        return result;
    }

    public sealed class OtaSystemInfo
    {
        public double BootloaderVersion;
        public double AppVersion;
        public int Manufacturer;
        public int Product;
        public string RunningArea = "unknown";
        public int WorkMode;
    }

    /// <summary>OTA_GET_DEVICE_SYSTEM_INFO (128) — read-only firmware identity.</summary>
    private static ushort Crc16(byte[] data, int start, int end)
    {
        ushort crc = 0xFFFF;
        for (int i = start; i < end; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            for (int b = 0; b < 8; b++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    public async Task<string> GetSerialNumberAsync(int timeoutMs = 2000)
    {
        var header = new byte[] { 0xAA, Cmd.OtaGetDeviceSn, 0, 0, 0, 0, 0, 0 };
        var frames = await SendCommandWithResponseAsync(Cmd.OtaGetDeviceSn, Array.Empty<byte>(), 56,
            customHeader: header, timeoutMs: timeoutMs);
        if (frames == null || frames.Count == 0) return "";
        var d = frames[0];
        int len = Math.Min(d.Length >= 3 ? d[2] : 0, d.Length - 8);
        var text = System.Text.Encoding.UTF8.GetString(d, 8, Math.Max(0, len)).TrimEnd('\0');
        return text;
    }

    public async Task<bool> VerifyFirmwareAsync(ushort manufacturer, ushort product, int timeoutMs = 2000)
    {
        var header = new byte[]
        {
            0xAA, Cmd.OtaVerifyFirmwareInfo,
            (byte)(manufacturer >> 8), (byte)manufacturer,
            (byte)(product >> 8), (byte)product, 0, 0
        };
        var frames = await SendCommandWithResponseAsync(Cmd.OtaVerifyFirmwareInfo, Array.Empty<byte>(), 24,
            customHeader: header, timeoutMs: timeoutMs);
        return frames != null && frames.Count > 0 && frames[0].Length > 2 && frames[0][2] == 1;
    }

    public async Task<bool> EnterBootAsync(int timeoutMs = 2000)
    {
        var header = new byte[] { 0xAA, Cmd.OtaDeviceEnterBoot, 0, 0, 0, 0, 0, 0 };
        var frames = await SendCommandWithResponseAsync(Cmd.OtaDeviceEnterBoot, Array.Empty<byte>(), 24,
            customHeader: header, timeoutMs: timeoutMs);
        return frames != null && frames.Count > 0;
    }

    public async Task<(bool ok, byte status, byte detail)> SendFirmwareAsync(byte[] firmware, Action<int>? progress)
    {
        if (!IsConnected) return (false, 255, 255);
        const int chunk = HidProtocol.ReportSize - 9; // 55 bytes of payload after the 9-byte OTA header
        int total = firmware.Length;
        if (total == 0) return (false, 255, 255);

        // Count packets, splitting so no packet crosses a 4096-byte CRC boundary.
        int packets = 0;
        for (int off = 0; off < total;)
        {
            int c = Math.Min(chunk, total - off);
            int end = off + c;
            if (off / 4096 != (end - 1) / 4096) c = ((off / 4096 + 1) * 4096) - off;
            off += c; packets++;
        }
        if (packets > 65535) return (false, 255, 255);

        int sent = 0;
        for (int seq = 1; seq <= packets; seq++)
        {
            int c = Math.Min(chunk, total - sent);
            int end = sent + c;
            if (sent / 4096 != (end - 1) / 4096) c = ((sent / 4096 + 1) * 4096) - sent;
            end = sent + c;

            byte crcHi = 0xFF, crcLo = 0xFF;
            if (end % 4096 == 0 || end == total)
            {
                ushort crc = Crc16(firmware, (end - 1) / 4096 * 4096, end);
                crcHi = (byte)(crc >> 8); crcLo = (byte)crc;
            }
            var header = new byte[]
            {
                0xAA, Cmd.OtaSendFirmwareInfo,
                (byte)(seq >> 8), (byte)seq,
                (byte)(packets >> 8), (byte)packets,
                (byte)c, crcHi, crcLo
            };
            var slice = new byte[c];
            Buffer.BlockCopy(firmware, sent, slice, 0, c);
            var report = HidProtocol.BuildReport(Cmd.OtaSendFirmwareInfo, c, 0, slice,
                null, header, false);
            SendReport(0, report);
            var response = Device!.ReadReport(end == total ? 600 : 2);
            sent = end;
            progress?.Invoke(Math.Min(99, (int)((double)seq / packets * 100)));
            if (end != total && (header[7] != 0xFF || header[8] != 0xFF)) continue;
            if (response != null && response.Length > 3)
                return (response[2] == 0, response[2], response[3]);
        }
        progress?.Invoke(100);
        return (true, 0, 0);
    }

    public async Task<bool> SwitchPartitionAsync(bool app, int timeoutMs = 2000)
    {
        byte target = app ? (byte)65 : (byte)66;
        var header = new byte[] { 0xAA, Cmd.OtaSwitchAppPartition, target, 0, 0, 0, 0, 0 };
        var frames = await SendCommandWithResponseAsync(Cmd.OtaSwitchAppPartition, Array.Empty<byte>(), 24,
            customHeader: header, timeoutMs: timeoutMs);
        return frames != null && frames.Count > 0 && frames[0].Length > 2 && frames[0][2] == 0;
    }

    public async Task<OtaSystemInfo?> GetOtaSystemInfoAsync(int timeoutMs = 2000)
    {
        if (!IsConnected) return null;
        var header = new byte[] { 0xAA, Cmd.OtaGetDeviceSystemInfo, 0, 0, 0, 0, 0, 0 };
        var frames = await SendCommandWithResponseAsync(Cmd.OtaGetDeviceSystemInfo,
            Array.Empty<byte>(), 56, customHeader: header, timeoutMs: timeoutMs);
        if (frames == null || frames.Count == 0) return null;
        var r = frames.SelectMany(f => f.Skip(HidProtocol.HeaderSize)).ToArray();
        if (r.Length < 10) return null;
        static double Ver(byte lo, byte hi) => ((lo & 15) + ((lo & 240) >> 4) * 10 + hi * 100) / 100.0;
        return new OtaSystemInfo
        {
            BootloaderVersion = Ver(r[0], r[1]),
            AppVersion = Ver(r[2], r[3]),
            Manufacturer = r[4] | (r[5] << 8),
            Product = r[6] | (r[7] << 8),
            RunningArea = r[8] == 16 ? "app" : r[8] == 48 ? "boot" : "unknown",
            WorkMode = r[9]
        };
    }
}
