using System.Runtime.InteropServices;

namespace DnfAutoFire;

internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const uint LLKHF_INJECTED = 0x10;

    // 低级鼠标钩子消息与 injected 标志（MSLLHOOKSTRUCT.flags）
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const uint LLMHF_INJECTED = 0x01;            // 事件由 SendInput 等注入
    public const uint LLMHF_LOWER_IL_INJECTED = 0x02;   // 低完整性进程注入
    public const uint LLMHF_ANY_INJECTED = LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED;

    // VK 鼠标键码（钩子事件统一用）
    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;

    // SendInput 鼠标事件标志
    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
}

/// <summary>通过 SendInput 发送键盘事件（使用扫描码，兼容性最好）。</summary>
internal static class NativeInput
{
    /// <summary>发送单个按下或抬起事件（up=true 抬起）。</summary>
    public static void SendKey(int vk, bool up)
    {
        ushort scan = (ushort)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
        input.U.ki = new NativeMethods.KEYBDINPUT
        {
            wVk = (ushort)vk,
            wScan = scan,
            dwFlags = NativeMethods.KEYEVENTF_SCANCODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>发送一次 press + release（零间隔，适合消息层消费；游戏内请用带保持时长的引擎调度）。</summary>
    public static void TapKey(int vk)
    {
        SendKey(vk, up: false);
        SendKey(vk, up: true);
    }

    /// <summary>发送鼠标按下/抬起事件（left=true 左键，否则右键；up=true 抬起）。位置为当前光标处。</summary>
    public static void SendMouseButton(bool left, bool up)
    {
        uint flag = left
            ? (up ? NativeMethods.MOUSEEVENTF_LEFTUP : NativeMethods.MOUSEEVENTF_LEFTDOWN)
            : (up ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_RIGHTDOWN);
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
        input.U.mi = new NativeMethods.MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = 0,
            dwFlags = flag,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
