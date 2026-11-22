using System;
using System.Diagnostics;
using System.Threading;

namespace AjazzManager;

/// <summary>
/// Streams per-key Hall sensor travel from the keyboard at ~1 kHz.
///
/// The vendor interface normally only answers commands. SET_SIMULATION_TEST_ON (102)
/// makes the firmware push unsolicited 0xFB input reports containing the live sensor
/// state of one key per report:
///   [0]=0x55 [1]=0xFB [2]=keyValue [3]=calibrationStatus
///   [4..5]=maxValue [6..7]=minValue [8..9]=currentValue (ADC)
///   [10..11]=keyStroke (0.01 mm units) [12..13]=maxStroke
/// The stream is turned off again on Dispose so the keyboard is left in normal mode.
/// </summary>
public sealed class HallStream : IDisposable
{
    private readonly DeviceService _device;
    private Thread? _thread;
    private volatile bool _running;
    private readonly int[] _stroke = new int[128];
    private readonly int[] _adc = new int[128];
    private readonly long[] _stamp = new long[128];
    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public event Action<int, int, int>? Sample; // keyValue, adc, stroke

    /// <summary>Firmware-reported maximum travel. Bytes 12-13 of the 0xFB report
    /// carry this as 0.1 mm units (e.g. 34 = 3.4 mm), so multiply by 10 to compare
    /// with keyStroke, which is in 0.01 mm units.</summary>
    public int MaxStroke { get; private set; }

    public HallStream(DeviceService device) => _device = device;

    public bool IsRunning => _running;

    public bool Start()
    {
        if (_running) return true;
        if (!_device.IsConnected && !_device.Connect()) return false;
        if (!Send(Cmd.SetSimulationTestOn)) return false;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "HallStream" };
        _thread.Start();
        return true;
    }

    private bool Send(byte cmd) => _device.SendReport(0,
        HidProtocol.BuildReport(cmd, 24, 0, null, null,
            new byte[] { 0xAA, cmd, 0, 0, 0, 0, 0, 0 }, false));

    private void Loop()
    {
        while (_running)
        {
            byte[]? r;
            try { r = _device.Device?.ReadReport(100); }
            catch { continue; }
            if (r == null || r.Length < 14 || r[0] != HidProtocol.ResponseStartByte ||
                r[1] != Cmd.GetMagneticAxisCalibrationData) continue;

            int key = r[2];
            if (key < 0 || key >= 128) continue;
            int adc = r[8] | (r[9] << 8);
            int stroke = r[10] | (r[11] << 8);
            int maxStroke = r[12] | (r[13] << 8);
            if (maxStroke > 0 && maxStroke < 100) MaxStroke = maxStroke;
            long now = _clock.ElapsedMilliseconds;
            lock (_lock) { _adc[key] = adc; _stroke[key] = stroke; _stamp[key] = now; }
            Sample?.Invoke(key, adc, stroke);
        }
    }

    /// <summary>Latest travel for a key in 0.01 mm units, or 0 if its report is stale.</summary>
    public int GetStroke(int key, int maxAgeMs = 60)
    {
        if (key < 0 || key >= 128) return 0;
        lock (_lock)
            return _clock.ElapsedMilliseconds - _stamp[key] <= maxAgeMs ? _stroke[key] : 0;
    }

    public void Dispose()
    {
        _running = false;
        var old = _thread;
        if (old != null && old.IsAlive && old != Thread.CurrentThread)
        {
            try { old.Join(300); } catch { }
        }
        _thread = null;
        try { Send(Cmd.SetSimulationTestOff); } catch { }
    }
}
