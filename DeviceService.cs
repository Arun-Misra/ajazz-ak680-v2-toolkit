using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AjazzManager;

public class DeviceService
{
    public NativeHidDevice? Device { get; private set; }
    public bool IsConnected => Device?.IsOpen == true;
    public string? ProductName => Device?.ProductName;
    public string? LastError { get; private set; }

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
        var data = Commands.BuildCustomLedData(colors);
        int chunk = HidProtocol.ChunkDataSize;
        int packets = (int)Math.Ceiling((double)data.Length / chunk);
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
            buffer[0] = 0; // report id
            Buffer.BlockCopy(report, 0, buffer, 1, Math.Min(report.Length, reportLen - 1));
            Device.WriteReportFast(buffer);
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

    public Task<bool> SetGameModeAsync(GameMode g) =>
        SendCommandAsync(Cmd.SetGameMode, Commands.BuildGameMode(g), 56);

    public Task<bool> SetTriggerConfigAsync(TriggerConfig[] t) =>
        SendCommandAsync(Cmd.SetTriggerKey, Commands.BuildTriggerConfig(t), 512, timeoutMs: 2000);

    public Task<bool> SetProfileAsync(byte index) =>
        SendCommandAsync(Cmd.SetProfile, new[] { index }, 1, customHeader: Commands.BuildProfileHeader(index), timeoutMs: 2000);

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
