using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AK680V2Toolkit;

/// <summary>Captures the system audio loopback and reports a smoothed 0..1 loudness level.</summary>
public sealed class AudioMeter : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private float _level;

    public double Level => _level;
    public bool IsRunning => _capture != null;

    public void Start()
    {
        if (_capture != null) return;
        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnData;
        _capture.StartRecording();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded < 4) return;
        int count = e.BytesRecorded / 4;
        var samples = new float[count];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, count * 4);
        double sum = 0;
        for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
        double rms = Math.Sqrt(sum / count);
        // Typical system audio RMS is 0.01..0.3; map that range to 0..1.
        double target = Math.Clamp(rms * 5.0, 0, 1);
        double current = _level;
        _level = (float)(current + (target - current) * 0.25);
    }

    public void Dispose()
    {
        if (_capture == null) return;
        try { _capture.StopRecording(); } catch { }
        _capture.Dispose();
        _capture = null;
        _level = 0;
    }
}
