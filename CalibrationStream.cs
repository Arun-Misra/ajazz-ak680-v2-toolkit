using System;
using System.Collections.Generic;
using System.Threading;

namespace AK680V2Toolkit;

/// <summary>
/// Hall switch calibration session. Mirrors the web driver's startCalibration /
/// stopCalibration flow: SET_CALIBRATION_ON makes the firmware push unsolicited
/// GET_MAGNETIC_AXIS_CALIBRATION_DATA (0xFB) reports while each key is pressed:
///   [0]=0x55 [1]=0xFB [2]=keyValue [3]=calibrationStatus
///   [4..5]=maxValue [6..7]=minValue [8..9]=currentValue (ADC)
///   [10..11]=keyStroke (0.01 mm) [12..13]=maxStroke
/// </summary>
public sealed class CalibrationStream : IDisposable
{
    public sealed class Sample
    {
        public int KeyValue, Status, MaxValue, MinValue, CurrentValue, KeyStroke, MaxStroke;
    }

    private readonly DeviceService _device;
    private readonly bool _v2;
    private Thread? _thread;
    private volatile bool _running;
    private readonly object _lock = new();
    private readonly Dictionary<int, Sample> _samples = new();

    public event Action<Sample>? Report;

    public CalibrationStream(DeviceService device, bool v2 = false)
    {
        _device = device;
        _v2 = v2;
    }

    public bool IsRunning => _running;

    public bool Start()
    {
        if (_running) return true;
        if (!_device.IsConnected && !_device.Connect()) return false;
        bool ok = _v2 ? _device.StartCalibrationV2Async().GetAwaiter().GetResult()
                      : _device.StartCalibrationAsync().GetAwaiter().GetResult();
        if (!ok) return false;
        lock (_lock) _samples.Clear();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "CalibrationStream" };
        _thread.Start();
        return true;
    }

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
            var sample = new Sample
            {
                KeyValue = key,
                Status = r[3],
                MaxValue = r[4] | (r[5] << 8),
                MinValue = r[6] | (r[7] << 8),
                CurrentValue = r[8] | (r[9] << 8),
                KeyStroke = r[10] | (r[11] << 8),
                MaxStroke = r[12] | (r[13] << 8)
            };
            lock (_lock) _samples[key] = sample;
            Report?.Invoke(sample);
        }
    }

    public List<Sample> GetSamples()
    {
        lock (_lock) return new List<Sample>(_samples.Values);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _thread?.Join(300);
        _thread = null;
        if (_v2) _device.StopCalibrationV2Async().GetAwaiter().GetResult();
        else _device.StopCalibrationAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => Stop();
}
