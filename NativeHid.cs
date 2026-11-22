using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AjazzManager;

/// <summary>
/// Native Win32 HID access: enumeration via SetupAPI, I/O via CreateFile/ReadFile/WriteFile,
/// capabilities via HidD_GetPreparsedData/HidP_GetCaps. No third-party HID wrapper.
/// </summary>
public sealed class NativeHidDevice : IDisposable
{
    private static Guid GuidDevInterfaceHid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    private SafeFileHandle? _handle;
    private SafeFileHandle? _writeHandle;
    private IntPtr _readOverlappedPtr = IntPtr.Zero;
    private IntPtr _readEvent = IntPtr.Zero;
    private byte[] _readBuffer = Array.Empty<byte>();
    private GCHandle _readPin;
    private IntPtr _readBufferPtr = IntPtr.Zero;
    private IntPtr _writeOverlappedPtr = IntPtr.Zero;

    // Pool of pinned buffers + overlapped structures for fire-and-forget writes.
    private const int FastWriteSlots = 24;
    private readonly GCHandle[] _fastPins = new GCHandle[FastWriteSlots];
    private readonly IntPtr[] _fastBuffers = new IntPtr[FastWriteSlots];
    private readonly IntPtr[] _fastOverlapped = new IntPtr[FastWriteSlots];
    private readonly IntPtr[] _fastEvents = new IntPtr[FastWriteSlots];
    private int _fastIndex;
    private IntPtr _writeEvent = IntPtr.Zero;

    private static readonly int EventHandleOffset =
        Marshal.OffsetOf<NativeOverlapped>(nameof(NativeOverlapped.EventHandle)).ToInt32();

    private static void InitOverlapped(IntPtr ptr, IntPtr hEvent)
    {
        Marshal.StructureToPtr(new NativeOverlapped(), ptr, false);
        Marshal.WriteIntPtr(ptr, EventHandleOffset, hEvent);
    }

    public string Path { get; }
    public string ProductName { get; private set; } = "";
    public string Manufacturer { get; private set; } = "";
    public ushort VendorId { get; private set; }
    public ushort ProductId { get; private set; }
    public ushort Version { get; private set; }
    public ushort InputReportLength { get; private set; }
    public ushort OutputReportLength { get; private set; }
    public ushort FeatureReportLength { get; private set; }
    public bool UsesReportIds { get; private set; }
    public bool IsOpen => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

    private NativeHidDevice(string path) { Path = path; }

    public static IReadOnlyList<NativeHidDevice> Enumerate()
    {
        var result = new List<NativeHidDevice>();
        IntPtr set = SetupDiGetClassDevs(ref GuidDevInterfaceHid, IntPtr.Zero, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
        try
        {
            for (uint index = 0; ; index++)
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref GuidDevInterfaceHid, index, ref iface))
                    break;
                uint detailSize = 0;
                SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, ref detailSize, IntPtr.Zero);
                if (detailSize <= 0) continue;
                IntPtr detail = Marshal.AllocHGlobal((int)detailSize);
                try
                {
                    Marshal.WriteInt32(detail, 0, IntPtr.Size == 8 ? 8 : 5);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, detailSize, ref detailSize, IntPtr.Zero))
                        continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? "";
                    var dev = new NativeHidDevice(path);
                    if (dev.ReadCapabilities()) result.Add(dev);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private bool ReadCapabilities()
    {
        IntPtr preparsed = IntPtr.Zero;
        var handle = CreateFile(Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return false;
        try
        {
            if (!HidD_GetAttributes(handle, out var attr)) return false;
            VendorId = attr.VendorID; ProductId = attr.ProductID; Version = attr.VersionNumber;

            var buf = new char[256];
            if (HidD_GetProductString(handle, buf, (uint)(buf.Length * 2))) ProductName = new string(buf).TrimEnd('\0');
            if (HidD_GetManufacturerString(handle, buf, (uint)(buf.Length * 2))) Manufacturer = new string(buf).TrimEnd('\0');

            bool gotPreparsed = HidD_GetPreparsedData(handle, ref preparsed);
            if (gotPreparsed)
            {
                int status = HidP_GetCaps(preparsed, out var caps);
                const int HidpStatusSuccess = 0x00110000;
                if (status == HidpStatusSuccess)
                {
                    InputReportLength = caps.InputReportByteLength;
                    OutputReportLength = caps.OutputReportByteLength;
                    FeatureReportLength = caps.FeatureReportByteLength;
                    UsesReportIds = false;
                }
                HidD_FreePreparsedData(preparsed);
            }
            return true;
        }
        finally { handle.Close(); }
    }

    public void Open()
    {
        if (IsOpen) return;
        _handle = CreateFile(Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            _handle.Dispose(); _handle = null;
            throw new Win32Exception(err, $"Cannot open HID device ({err})");
        }
        // Separate synchronous handle for writes: avoids overlapped/IOCP overhead.
        _writeHandle = CreateFile(Path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (_writeHandle.IsInvalid) { _writeHandle.Dispose(); _writeHandle = null; }
        _readEvent = CreateEvent(IntPtr.Zero, true, false, null);
        _writeEvent = CreateEvent(IntPtr.Zero, true, false, null);
        if (_readEvent == IntPtr.Zero || _writeEvent == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(err, $"Cannot create HID event ({err})");
        }
        _readBuffer = new byte[Math.Max(1, (int)InputReportLength)];
        _readPin = GCHandle.Alloc(_readBuffer, GCHandleType.Pinned);
        _readBufferPtr = _readPin.AddrOfPinnedObject();
        _readOverlappedPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        _writeOverlappedPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        InitOverlapped(_readOverlappedPtr, _readEvent);
        InitOverlapped(_writeOverlappedPtr, _writeEvent);
        for (int i = 0; i < FastWriteSlots; i++)
        {
            var buf = new byte[Math.Max(1, (int)OutputReportLength)];
            _fastPins[i] = GCHandle.Alloc(buf, GCHandleType.Pinned);
            _fastBuffers[i] = _fastPins[i].AddrOfPinnedObject();
            _fastEvents[i] = CreateEvent(IntPtr.Zero, true, false, null);
            _fastOverlapped[i] = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
            InitOverlapped(_fastOverlapped[i], _fastEvents[i]);
        }
        StartRead();
    }

    private void StartRead()
    {
        if (_handle == null || _readEvent == IntPtr.Zero || _readOverlappedPtr == IntPtr.Zero || _readBufferPtr == IntPtr.Zero) return;
        ResetEvent(_readEvent);
        ReadFile(_handle, _readBufferPtr, (uint)_readBuffer.Length, out _, _readOverlappedPtr);
    }

    /// <summary>
    /// Write an output report. data must be the full report buffer (report id first if used).
    /// </summary>
    public bool WriteReport(byte[] data, int timeoutMs = 2000)
    {
        // Hardware can stall (re-enumeration, profile switch, another app touching the
        // device). A failed/timeout write must be a normal failure result, never an
        // exception escaping onto a background animation thread and killing the app.
        if (!IsOpen || _writeEvent == IntPtr.Zero || _writeOverlappedPtr == IntPtr.Zero) return false;
        unsafe
        {
            fixed (byte* p = data)
            {
                ResetEvent(_writeEvent);
                InitOverlapped(_writeOverlappedPtr, _writeEvent);
                if (!WriteFile(_handle!, (IntPtr)p, (uint)data.Length, out _, _writeOverlappedPtr))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_IO_PENDING) return false;
                }
                if (WaitForSingleObject(_writeEvent, (uint)timeoutMs) != WAIT_OBJECT_0)
                {
                    CancelIoEx(_handle!, _writeOverlappedPtr);
                    WaitForSingleObject(_writeEvent, 100);
                    return false;
                }
                if (!GetOverlappedResult(_handle!, _writeOverlappedPtr, out _, false))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Fire-and-forget write for animation frames. Uses a rotating pool of pinned
    /// buffers so several writes can be in flight without buffer reuse races.
    /// </summary>
    public void WriteReportFast(byte[] data)
    {
        if (!IsOpen || _fastBuffers[0] == IntPtr.Zero) return;
        int slot = _fastIndex++ % FastWriteSlots;
        int len = Math.Min(data.Length, _fastBuffers[slot] == IntPtr.Zero ? data.Length : Math.Max(1, (int)OutputReportLength));
        Marshal.Copy(data, 0, _fastBuffers[slot], Math.Min(data.Length, len));
        ResetEvent(_fastEvents[slot]);
        WriteFile(_handle!, _fastBuffers[slot], (uint)data.Length, out _, _fastOverlapped[slot]);
    }

    /// <summary>
    /// Read an input report with timeout. Returns null on timeout.
    /// </summary>
    public byte[]? ReadReport(int timeoutMs = 500)
    {
        if (!IsOpen) throw new InvalidOperationException("Device not open");
        int wait = (int)WaitForSingleObject(_readEvent, (uint)timeoutMs);
        if (wait != WAIT_OBJECT_0) return null;
        if (!GetOverlappedResult(_handle!, _readOverlappedPtr, out uint count, false)) return null;
        var copy = new byte[Math.Min((int)count, _readBuffer.Length)];
        Buffer.BlockCopy(_readBuffer, 0, copy, 0, copy.Length);
        StartRead();
        // Win32 HID input reports include a leading report-id byte; WebHID's
        // HIDInputReportEvent.data does not. Normalise to the WebHID shape so
        // the protocol parsers (which assume 0x55 at offset 0) work correctly.
        if (copy.Length > 1 && copy[0] == 0)
        {
            var data = new byte[copy.Length - 1];
            Buffer.BlockCopy(copy, 1, data, 0, data.Length);
            return data;
        }
        return copy;
    }

    public bool SetFeature(byte[] report)
    {
        if (!IsOpen) return false;
        return HidD_SetFeature(_handle!, report, (uint)report.Length);
    }

    public bool GetFeature(byte[] report)
    {
        if (!IsOpen) return false;
        return HidD_GetFeature(_handle!, report, (uint)report.Length);
    }

    public void Dispose()
    {
        try { if (_readEvent != IntPtr.Zero) CloseHandle(_readEvent); } catch { }
        _readEvent = IntPtr.Zero;
        try { if (_writeEvent != IntPtr.Zero) CloseHandle(_writeEvent); } catch { }
        _writeEvent = IntPtr.Zero;
        if (_readBufferPtr != IntPtr.Zero) { try { if (_readPin.IsAllocated) _readPin.Free(); } catch { } _readBufferPtr = IntPtr.Zero; }
        for (int i = 0; i < FastWriteSlots; i++)
        {
            if (_fastEvents[i] != IntPtr.Zero) { try { CloseHandle(_fastEvents[i]); } catch { } _fastEvents[i] = IntPtr.Zero; }
            if (_fastOverlapped[i] != IntPtr.Zero) { Marshal.FreeHGlobal(_fastOverlapped[i]); _fastOverlapped[i] = IntPtr.Zero; }
            try { if (_fastPins[i].IsAllocated) _fastPins[i].Free(); } catch { }
            _fastBuffers[i] = IntPtr.Zero;
        }
        if (_readOverlappedPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_readOverlappedPtr); _readOverlappedPtr = IntPtr.Zero; }
        if (_writeOverlappedPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_writeOverlappedPtr); _writeOverlappedPtr = IntPtr.Zero; }
        _handle?.Dispose();
        _handle = null;
    }

    // ---- Win32 ----
    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;
    private const int ERROR_IO_PENDING = 997;
    private const uint WAIT_OBJECT_0 = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES { public uint Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        public ushort NumberInputReportIDs, NumberOutputReportIDs, NumberFeatureReportIDs;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadFile(SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool WriteFile(SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle hFile, IntPtr lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);
    [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr hEvent);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, out HIDD_ATTRIBUTES Attributes);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle HidDeviceObject, char[] Buffer, uint BufferLength);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetManufacturerString(SafeFileHandle HidDeviceObject, char[] Buffer, uint BufferLength);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, ref IntPtr PreparsedData);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);
}
