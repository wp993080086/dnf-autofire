using System.Runtime.InteropServices;

namespace DnfAutoFire;

/// <summary>
/// 低级鼠标钩子。只监听物理鼠标事件（过滤程序自身注入的），
/// 处理程序调用 SuppressCurrent() 可吞掉当前这个原生鼠标消息。
/// 两套事件：
/// <list type="bullet">
/// <item><c>ButtonDown/ButtonUp</c>：只报鼠标键，参数为 VK 键码（1=左 2=右 4=中 5=X1 6=X2），给连点引擎用；</item>
/// <item><c>Raw</c>：报所有消息（移动 + 按键）连同物理坐标与时间戳，给轨迹录制用。</item>
/// </list>
/// </summary>
internal sealed class MouseHook : IDisposable
{
    /// <summary>一条原始鼠标消息（已过滤注入事件）。</summary>
    public readonly record struct RawEvent(int Msg, int X, int Y, uint Time);

    private NativeMethods.LowLevelHookProc _proc = null!;
    private IntPtr _hook = IntPtr.Zero;
    private bool _suppress;

    public event Action<int>? ButtonDown;
    public event Action<int>? ButtonUp;

    /// <summary>所有物理鼠标消息（含 WM_MOUSEMOVE）。录制期间每个事件都会触发，回调内必须极轻。</summary>
    public event Action<RawEvent>? Raw;

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

    /// <summary>
    /// XBUTTON 消息里两颗侧键共用一个消息号，靠 mouseData 高字区分。
    /// 抽成纯函数供自检与单测直接验证，不用真去按鼠标。
    /// </summary>
    internal static int? XButtonVk(uint mouseData) => (mouseData >> 16) switch
    {
        NativeMethods.XBUTTON1 => NativeMethods.VK_XBUTTON1,
        NativeMethods.XBUTTON2 => NativeMethods.VK_XBUTTON2,
        _ => null
    };

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        _suppress = false;
        if (nCode >= 0)
        {
            var s = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            // 程序自己 SendInput 的事件带 injected 标志，直接放行，避免自我触发死循环
            if ((s.flags & NativeMethods.LLMHF_ANY_INJECTED) == 0)
            {
                int msg = wParam.ToInt32();
                if (Raw != null) Raw(new RawEvent(msg, s.pt.X, s.pt.Y, s.time));

                int? button = msg switch
                {
                    NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP
                        => NativeMethods.VK_LBUTTON,
                    NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP
                        => NativeMethods.VK_RBUTTON,
                    NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP
                        => NativeMethods.VK_MBUTTON,
                    NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP
                        => XButtonVk(s.mouseData),
                    _ => null
                };
                if (button.HasValue)
                {
                    bool down = msg is NativeMethods.WM_LBUTTONDOWN
                        or NativeMethods.WM_RBUTTONDOWN
                        or NativeMethods.WM_MBUTTONDOWN
                        or NativeMethods.WM_XBUTTONDOWN;
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
