using System.Runtime.InteropServices;

namespace DnfAutoFire;

/// <summary>
/// 低级鼠标钩子。只监听物理鼠标键（过滤程序自身注入的点击），
/// 处理程序调用 SuppressCurrent() 可吞掉当前这个原生鼠标键。
/// 事件参数为鼠标键码：1=左键 2=右键（VK_LBUTTON / VK_RBUTTON）。
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private NativeMethods.LowLevelHookProc _proc = null!;
    private IntPtr _hook = IntPtr.Zero;
    private bool _suppress;

    public event Action<int>? ButtonDown;
    public event Action<int>? ButtonUp;

    public void Install()
    {
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("安装鼠标钩子失败，错误码 " + Marshal.GetLastWin32Error());
    }

    /// <summary>在 ButtonDown/ButtonUp 事件处理器内调用，吞掉当前这个原生鼠标键。</summary>
    public void SuppressCurrent() => _suppress = true;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        _suppress = false;
        if (nCode >= 0)
        {
            var s = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            // 程序自己 SendInput 的点击带 injected 标志，直接放行，避免自我触发死循环
            if ((s.flags & NativeMethods.LLMHF_ANY_INJECTED) == 0)
            {
                int msg = wParam.ToInt32();
                int? button = msg switch
                {
                    NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP
                        => NativeMethods.VK_LBUTTON,
                    NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP
                        => NativeMethods.VK_RBUTTON,
                    _ => null
                };
                if (button.HasValue)
                {
                    bool down = msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN;
                    if (down) ButtonDown?.Invoke(button.Value);
                    else ButtonUp?.Invoke(button.Value);
                }
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
