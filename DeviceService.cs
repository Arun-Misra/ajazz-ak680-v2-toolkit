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
    public void SendReport(byte reportId, byte[] data)
    {
        if (Device is null || !Device.IsOpen) throw new InvalidOperationException("Not connected");
        // Live WebHID evidence: reportId 0, 64-byte payload. Win32 HID class
        // expects the report-id byte first, followed by the payload.
        int length = Math.Max(Device.OutputReportLength, data.Length + 1);
        var report = new byte[length];
        report[0] = reportId;
        Buffer.BlockCopy(data, 0, report, 1, Math.Min(data.Length, length - 1));
        Device.WriteReport(report);
    }

    public async Task<bool> SendCommandAsync(byte cmd, byte[] data, int contentSize,
        int addrStart = 0, byte[]? otherHeader = null, byte[]? customHeader = null,
        int timeoutMs = 500, bool skipResponse = false, CancellationToken ct = default, bool needLastFlag = true)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected");
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
        int addrStart = 0, byte[]? otherHeader = null, int timeoutMs = 2000)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected");
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
            SendReport(0, HidProtocol.BuildReport(cmd, size, addr, slice, otherHeader, null, last));
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
        foreach (var r in responses) payload.AddRange(r);
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
        foreach (var r in responses) payload.AddRange(r);
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
}
