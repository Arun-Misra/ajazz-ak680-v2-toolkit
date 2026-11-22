using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace AK680V2Toolkit;

/// <summary>Thin wrapper over the ViGEmBus virtual XInput 360 controller.</summary>
public sealed class VirtualGamepad : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;

    public string? Error { get; private set; }
    public bool IsConnected => _pad != null;

    public bool Start()
    {
        if (_pad != null) return true;
        try
        {
            _client = new ViGEmClient();
            _pad = _client.CreateXbox360Controller();
            _pad.AutoSubmitReport = false;
            _pad.Connect();
            Error = null;
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _pad = null;
            _client?.Dispose();
            _client = null;
            return false;
        }
    }

    private static short ToAxis(double v) =>
        (short)Math.Clamp(Math.Round(v * short.MaxValue), short.MinValue, short.MaxValue);

    /// <summary>Set both thumbsticks. Values are -1..1.</summary>
    public void SetSticks(double leftX, double leftY, double rightX = 0, double rightY = 0)
    {
        if (_pad == null) return;
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxis(leftX));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxis(leftY));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, ToAxis(rightX));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, ToAxis(rightY));
        _pad.SubmitReport();
    }

    public void SetButton(Xbox360Button button, bool pressed)
    {
        if (_pad == null) return;
        _pad.SetButtonState(button, pressed);
        _pad.SubmitReport();
    }

    public void Reset()
    {
        if (_pad == null) return;
        _pad.ResetReport();
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        try { _pad?.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }
        _pad = null;
        _client = null;
    }
}
