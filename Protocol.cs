using System;

namespace AK680V2Toolkit;

/// <summary>
/// Command IDs extracted from the AJAZZ web driver bundle (protocols/default/index.ts -> HX).
/// </summary>
public static class Cmd
{
    public const byte CommunicationStart = 1;
    public const byte CommunicationEnd = 2;
    public const byte SetFactoryReset = 15;
    public const byte GetDeviceInfo = 16;
    public const byte GetGameMode = 17;
    public const byte GetKey = 18;
    public const byte GetLedEffect = 19;
    public const byte GetCustomLedData = 20;
    public const byte GetMacro = 21;
    public const byte GetFnKey = 22;
    public const byte GetMagneticAxisRt = 23;
    public const byte GetMagneticAxisDksData = 24;
    public const byte GetLightBox = 27;
    public const byte GetDefaultFnKeyMatrix = 28;
    public const byte GetSideLight = 29;
    public const byte GetTriggerKey = 30;
    public const byte GetDefaultKeyMatrix = 31;
    public const byte SetGameMode = 33;
    public const byte SetKey = 34;
    public const byte SetLedEffect = 35;
    public const byte SetCustomLedData = 36;
    public const byte SetMacro = 37;
    public const byte SetFnKey = 38;
    public const byte SetMagneticAxisRt = 39;
    public const byte SetMagneticAxisDksData = 40;
    public const byte SetDotMatrixMode = 42;
    public const byte SetLightBox = 43;
    public const byte SetSideLight = 45;
    public const byte SetTriggerKey = 46;
    public const byte SetKeyboardCustomFunctionOn = 48;
    public const byte SetKeyboardCustomFunctionOff = 49;
    public const byte GetLedData = 50;
    public const byte GetAllLightsRgb = 51;
    public const byte SetTemporaryCommandData = 52;
    public const byte SetMusicData = 53;
    public const byte ClearLedData = 54;
    public const byte GetAllLightsRgb24G = 55;
    public const byte GetAllLightsRgb24G64Byte = 59;
    public const byte SetMusicData24G64Byte = 60;
    public const byte GetDotMatrixConfig = 61;
    public const byte SetLedBootAnimation = 64;
    public const byte SetLedUserAnimation = 65;
    public const byte SetLedData = 66;
    public const byte SetTftMaterial = 77;
    public const byte SetFlashDownload = 79;
    public const byte SetTftUserAnimation = 80;
    public const byte SetTftBuiltInIndex = 81;
    public const byte GetMagneticAxisKeyStatus = 96;
    public const byte SetCalibrationOn = 100;
    public const byte SetCalibrationOff = 101;
    public const byte SetSimulationTestOn = 102;
    public const byte SetSimulationTestOff = 103;
    public const byte GetMagneticAxisStatus = 104;
    public const byte SetCalibrationOnV2 = 105;
    public const byte SetCalibrationOffV2 = 106;
    public const byte OtaGetDeviceSystemInfo = 128;
    public const byte OtaVerifyFirmwareInfo = 129;
    public const byte OtaDeviceEnterBoot = 130;
    public const byte OtaSendFirmwareInfo = 131;
    public const byte OtaGetDeviceSn = 132;
    public const byte OtaSwitchAppPartition = 133;
    public const byte OtaSetDeviceSn = 134;
    public const byte SetProfile = 144;
    public const byte GetDeviceNotify = 250;
    public const byte GetMagneticAxisCalibrationData = 251;
    public const byte Get24GDisconnectNotify = 252;
}

public static class FactoryResetType
{
    public const byte KeyReset = 1;
    public const byte LightingReset = 2;
    public const byte MacroReset = 4;
    public const byte ClearCalibration = 5;
    public const byte ResetAll = 255;
}

/// <summary>
/// HID report helpers mirroring the web driver's NX/PX functions.
/// Report layout (32 bytes): [0xAA, cmd, chunkSize, addrLo, addrHi, hdr0, hdr1, hdr2, data[24]]
/// Response frames start with 0x55.
/// </summary>
public static class HidProtocol
{
    // Live WebHID device reports: out report id 0, reportCount 64, 8 bits/item.
    public const int ReportSize = 64;
    public const int HeaderSize = 8;
    public const int ChunkDataSize = ReportSize - HeaderSize; // 56
    public const byte StartByte = 0xAA;
    public const byte ResponseStartByte = 0x55;

    /// <summary>
    /// Build a single 32-byte report (mirrors PX).
    /// </summary>
    public static byte[] BuildReport(byte cmd, int chunkSize, int addrStart,
        byte[]? dataSlice, byte[]? otherHeader = null, byte[]? customHeader = null, bool isLast = false)
    {
        var c = new byte[ReportSize]; // zero-filled
        if (customHeader != null)
        {
            int n = Math.Min(customHeader.Length, ReportSize);
            Buffer.BlockCopy(customHeader, 0, c, 0, n);
            if (dataSlice != null)
            {
                int off = customHeader.Length;
                int len = Math.Min(dataSlice.Length, ReportSize - off);
                if (len > 0) Buffer.BlockCopy(dataSlice, 0, c, off, len);
            }
            return c;
        }
        c[0] = StartByte;
        c[1] = cmd;
        c[2] = (byte)(chunkSize & 0xFF);
        c[3] = (byte)(addrStart & 0xFF);
        c[4] = (byte)((addrStart >> 8) & 0xFF);
        if (otherHeader != null)
        {
            for (int e = 0; e < Math.Min(otherHeader.Length, 3); e++)
                c[5 + e] = (byte)(otherHeader[e] & 0xFF);
        }
        // set last-packet flag at c[6] only when otherHeader absent/short (mirrors JS condition)
        if (otherHeader == null || otherHeader.Length < 2)
            c[6] = (byte)(isLast ? 1 : 0);
        if (dataSlice != null)
        {
            int len = Math.Min(dataSlice.Length, ReportSize - HeaderSize);
            if (len > 0) Buffer.BlockCopy(dataSlice, 0, c, HeaderSize, len);
        }
        return c;
    }

    /// <summary>
    /// Parse a response frame (mirrors FX). Returns null if not a valid frame.
    /// </summary>
    public static (byte cmd, int addr, byte[] data)? ParseResponse(byte[] frame)
    {
        if (frame == null || frame.Length < HeaderSize) return null;
        if (frame[0] != ResponseStartByte) return null;
        byte cmd = frame[1];
        int len = frame[2];
        int addr = frame[3] | (frame[4] << 8);
        var data = new byte[Math.Max(0, frame.Length - HeaderSize)];
        Buffer.BlockCopy(frame, HeaderSize, data, 0, data.Length);
        return (cmd, addr, data);
    }
}
