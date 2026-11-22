using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AK680V2Toolkit;

/// <summary>
/// Global low-level keyboard hook. Maps Windows virtual keys to the AK680 V2's HID
/// usages and then to per-key LED ids so a physical key press can trigger an effect.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    private static readonly Dictionary<int, int> UsageByVk = BuildUsageMap();
    private static readonly Dictionary<int, int> LedByUsage = BuildLedMap();

    private HookProc? _callback;
    private IntPtr _hook = IntPtr.Zero;

    public event Action<int>? KeyPressed;

    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _callback = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    private IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            // Ignore auto-repeat.
            if ((k.flags & 0x80000000) == 0 &&
                UsageByVk.TryGetValue((int)k.vkCode, out int usage) &&
                LedByUsage.TryGetValue(usage, out int led))
            {
                KeyPressed?.Invoke(led);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _callback = null;
    }

    private static Dictionary<int, int> BuildUsageMap()
    {
        var m = new Dictionary<int, int>();
        for (char c = 'A'; c <= 'Z'; c++) m[c] = 4 + (c - 'A');
        for (char c = '0'; c <= '9'; c++) m[c] = c == '0' ? 39 : 30 + (c - '1');
        m[0x1B] = 41;  // Esc
        m[0x09] = 43;  // Tab
        m[0x08] = 42;  // Backspace
        m[0x14] = 57;  // Caps
        m[0x0D] = 40;  // Enter
        m[0x20] = 44;  // Space
        m[0x10] = 225; m[0xA0] = 225; m[0xA1] = 229; // Shift
        m[0x11] = 224; m[0xA2] = 224; m[0xA3] = 228; // Ctrl
        m[0x12] = 226; m[0xA4] = 226; m[0xA5] = 230; // Alt
        m[0x5B] = 227; m[0x5C] = 231;                // Win
        m[0xDB] = 47; m[0xDD] = 48; m[0xDC] = 49;    // [ ] \
        m[0xBA] = 51; m[0xDE] = 52;                  // ; '
        m[0xBC] = 54; m[0xBE] = 55; m[0xBF] = 56;    // , . /
        m[0xBD] = 45; m[0xBB] = 46;                  // - =
        m[0x24] = 74; m[0x21] = 75; m[0x22] = 78;    // Home PgUp PgDn
        m[0x2E] = 76;                                // Del
        m[0x25] = 80; m[0x27] = 79; m[0x26] = 82; m[0x28] = 81; // arrows
        return m;
    }

    private static Dictionary<int, int> BuildLedMap()
    {
        // HID usage -> LED id from the device config keyList.
        int[] usage = { 41,30,31,32,33,34,35,36,37,38,39,45,46,42,74,43,20,26,8,21,23,28,24,12,18,19,47,48,49,76,57,4,22,7,9,10,11,13,14,15,51,52,40,75,225,29,27,6,25,5,17,16,54,55,56,229,82,78,224,227,226,44,230,175,228,80,81,79 };
        int[] led   = {  0,17,18,19,20,21,22,23,24,25,26,27,28,92,104,32,33,34,35,36,37,38,39,40,41,42,43,44,60,106,48,49,50,51,52,53,54,55,56,57,58,59,76,105,64,65,66,67,68,69,70,71,72,73,74,75,90,108,80,81,82,83,84,85,87,88,89,91 };
        var m = new Dictionary<int, int>();
        for (int i = 0; i < usage.Length && i < led.Length; i++) m[usage[i]] = led[i];
        return m;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
