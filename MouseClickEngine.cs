using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DnfAutoFire;

/// <summary>
/// 鼠标连点引擎：与 AutoFireEngine 同构的两种触发方式。
/// 松手停止：物理按住鼠标键才连点，松开即停；
/// 自动点击：开启后按间隔自动点击，不需要按住，次数跑完由引擎收尾（次数 0 = 一直点）。
/// 调度与键盘一致：按下/抬起各保持 ≥20ms，有效间隔下限 40ms（游戏按帧轮询）。
/// 松手停止行触发时拦截原生点击（吞掉按下与配对的抬起），由引擎统一注入，避免重复点击。
/// </summary>
internal sealed class MouseClickEngine
{
    /// <summary>触发方式：Infinite=物理按住才连点、松手即停；Auto=开启后自动点击，不需要按住。</summary>
    public enum StopMode { Infinite, Auto }

    /// <summary>一个目标键：鼠标键码（VK_LBUTTON/VK_RBUTTON/VK_MBUTTON）+ 间隔 + 触发方式 + 自动模式次数（0 = 无限）。</summary>
    public readonly record struct MouseTarget(int Button, int IntervalMs, StopMode Mode, int Count);

    private sealed class ClickState
    {
        public int Button;        // VK 键码：1=左键 2=右键 4=滚轮按下（中键）
        public int IntervalMs;
        public StopMode Mode;
        public int Remaining;     // 自动点击剩余次数，-1 = 无限
        public volatile bool Held; // 有效连点会话中（自动点击行启动即视为按住）
        public bool Done;         // 自动点击次数已用完
        public bool IsDown;       // 当前注入状态
        public long NextDue;
    }

    private readonly List<ClickState> _tasks = new();
    private readonly Stopwatch _sw = new();
    private MouseHook? _hook;
    private Thread? _worker;
    private volatile bool _stop;
    private readonly bool[] _swallowUp = new bool[5]; // 按 VK 键码索引（1/2/4）：按下被拦截时，配对的抬起也要拦截
    private bool _periodRaised;   // timeBeginPeriod/timeEndPeriod 必须成对：重复 Stop 不能再还给系统一次

    public bool Running { get; private set; }

    /// <summary>所有目标行都是「自动点击」且次数已跑完时触发（工作线程上回调，UI 需自行调度）。</summary>
    public event Action? AllDone;

    // 测试用：设置环境变量 DNF_DRYRUN=1 时不真正注入点击，只跑调度计数
    internal static readonly bool DryRun = Environment.GetEnvironmentVariable("DNF_DRYRUN") == "1";

    private static void Send(int button, bool up)
    {
        if (DryRun) return;
        NativeInput.SendMouseButton(button, up);
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    public void Start(List<MouseTarget> targets)
    {
        Stop();
        foreach (var t in targets)
        {
            if (_tasks.Any(k => k.Button == t.Button)) continue;
            bool auto = t.Mode == StopMode.Auto;
            _tasks.Add(new ClickState
            {
                Button = t.Button,
                IntervalMs = Math.Max(1, t.IntervalMs),
                Mode = t.Mode,
                Remaining = auto && t.Count > 0 ? t.Count : -1,
                Held = auto,  // 自动点击：启动即视为按住，不依赖物理按键
                NextDue = 0   // 立刻补一发
            });
        }

        _hook = new MouseHook();
        _hook.ButtonDown += OnButtonDown;
        _hook.ButtonUp += OnButtonUp;

        _stop = false;
        _sw.Restart();
        if (!_periodRaised)
        {
            timeBeginPeriod(1);
            _periodRaised = true;
        }
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "MouseClickEngine" };
        _worker.Start();
        _hook.Install();
        Running = true;
    }

    public void Stop()
    {
        _stop = true;
        try { _worker?.Join(1000); } catch { /* 线程可能尚未启动 */ }
        _worker = null;
        _sw.Stop();
        if (_periodRaised)
        {
            timeEndPeriod(1);
            _periodRaised = false;
        }
        // 释放仍处于"模拟按下"状态的鼠标键，避免卡键
        foreach (var t in _tasks)
        {
            if (t.IsDown)
            {
                try { Send(t.Button, up: true); } catch { /* 尽力释放 */ }
                t.IsDown = false;
            }
            t.Held = false;
        }
        _tasks.Clear();
        Array.Clear(_swallowUp);
        _hook?.Dispose();
        _hook = null;
        Running = false;
    }

    private void WorkerLoop()
    {
        while (!_stop)
        {
            long now = _sw.ElapsedTicks;
            foreach (var t in _tasks)
            {
                // 自动点击的次数已用完：释放并按空闲处理
                if (t.Done)
                {
                    if (t.IsDown)
                    {
                        Send(t.Button, up: true);
                        t.IsDown = false;
                    }
                    continue;
                }

                if (!t.Held)
                {
                    if (t.IsDown) // 中途松开：立刻抬起，不留卡键
                    {
                        Send(t.Button, up: true);
                        t.IsDown = false;
                    }
                    continue;
                }

                if (now < t.NextDue) continue;

                // 与键盘连发一致的帧兼容节奏：按下/抬起各 ≥20ms，有效间隔下限 40ms
                long period = Math.Max(t.IntervalMs, 40) * Stopwatch.Frequency / 1000;
                long hold = Math.Clamp(t.IntervalMs / 2, 20, 30) * Stopwatch.Frequency / 1000;
                if (!t.IsDown)
                {
                    Send(t.Button, up: false); // 按下
                    t.IsDown = true;
                    t.NextDue = _sw.ElapsedTicks + hold;
                }
                else
                {
                    Send(t.Button, up: true);  // 抬起（完成一次点击）
                    t.IsDown = false;
                    // 自动点击：一发完成，扣减剩余次数，用完即停
                    if (t.Mode == StopMode.Auto && t.Remaining > 0 && --t.Remaining == 0)
                    {
                        t.Done = true;
                        if (DryRun) Console.WriteLine($"[dry] button={t.Button} 次数已用完");
                        continue;
                    }
                    t.NextDue = Math.Max(_sw.ElapsedTicks + period - hold, _sw.ElapsedTicks);
                }
            }

            // 全部都是自动点击且次数跑完 → 没有事情可等，引擎自行收尾
            if (_tasks.Count > 0 && _tasks.All(t => t.Mode == StopMode.Auto && t.Done))
            {
                _stop = true;
                if (DryRun) Console.WriteLine("[dry] AllDone（全部自动点击已跑完）");
                AllDone?.Invoke();
                break;
            }
            Thread.Sleep(1);
        }
    }

    private void OnButtonDown(int button)
    {
        var t = _tasks.FirstOrDefault(x => x.Button == button);
        if (t == null) return;
        if (t.Mode == StopMode.Auto) return; // 自动点击不理会物理按压，也不吞掉原生点击

        // 拦截原生按下，配对的抬起也一并拦截（即使中途次数点满）
        _hook!.SuppressCurrent();
        _swallowUp[button] = true;

        if (t.Held) return; // 原生长按会反复触发 down
        // 开始一次新的连点会话
        t.Held = true;
        t.Done = false;
        t.IsDown = false;
        t.NextDue = 0; // 立刻补一发，模拟真实单击
    }

    private void OnButtonUp(int button)
    {
        var t = _tasks.FirstOrDefault(x => x.Button == button);
        if (t == null) return;
        if (t.Mode == StopMode.Auto) return; // 自动点击不受松手影响
        if (_swallowUp[button])
        {
            _hook!.SuppressCurrent();
            _swallowUp[button] = false;
        }
        t.Held = false; // 松开即停，抬起由 WorkerLoop 统一处理
    }
}
