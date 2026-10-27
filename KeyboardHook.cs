using System.Runtime.InteropServices;

namespace DnfAutoFire;

/// <summary>
/// 低级键盘钩子。只监听物理按键（过滤程序自身注入的按键），
/// 处理程序调用 SuppressCurrent() 可吞掉当前这个原生按键。
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private NativeMethods.LowLevelHookProc _proc = null!;
    private IntPtr _hook = IntPtr.Zero;
    private bool _suppress;

    public event Action<int>? KeyDown;
    public event Action<int>? KeyUp;

    public void Install()
    {
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("安装键盘钩子失败，错误码 " + Marshal.GetLastWin32Error());
    }

    /// <summary>在 KeyDown/KeyUp 事件处理器内调用，吞掉当前这个原生按键。</summary>
    public void SuppressCurrent() => _suppress = true;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        _suppress = false;
        if (nCode >= 0)
        {
            var s = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            // 程序自己 SendInput 的按键带 injected 标志，直接放行，避免自我触发
            if ((s.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                int msg = wParam.ToInt32();
                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                    KeyDown?.Invoke((int)s.vkCode);
                else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                    KeyUp?.Invoke((int)s.vkCode);
            }
        }
        return _suppress ? (IntPtr)1 : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
