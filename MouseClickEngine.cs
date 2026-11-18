using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyMouseFactory;

/// <summary>
/// 鼠标连点引擎：与 AutoFireEngine 同构的两种触发方式。
/// 松手停止：物理按住鼠标键才连点，松开即停；
/// 自动点击：开启后按间隔自动点击，不需要按住，次数跑完由引擎收尾（次数 0 = 一直点）。
/// 调度与键盘一致：按下/抬起各保持 ≥20ms，有效间隔下限 40ms（游戏按帧轮询）。
/// 松手停止行触发时拦截原生点击（吞掉按下与配对的抬起），由引擎统一注入，避免重复点击。
/// 工作线程与键盘同构：按到期时刻等待，没人按键时阻塞在事件上零唤醒。
/// </summary>
internal sealed class MouseClickEngine
{
    /// <summary>触发方式：Infinite=物理按住才连点、松手即停；Auto=开启后自动点击，不需要按住。</summary>
    public enum StopMode { Infinite, Auto }

    /// <summary>一个目标键：鼠标键码（左右中键 + X1/X2 侧键）+ 间隔 + 触发方式 + 自动模式次数（0 = 无限）。</summary>
    public readonly record struct MouseTarget(int Button, int IntervalMs, StopMode Mode, int Count);

    private sealed class ClickState
    {
        public int Button;        // VK 键码：1=左键 2=右键 4=中键 5=X1 6=X2
        public int IntervalMs;
        public StopMode Mode;
        public int Remaining;     // 自动点击剩余次数，-1 = 无限
        public volatile bool Held; // 有效连点会话中（自动点击行启动即视为按住）
        public bool Done;         // 自动点击次数已用完
        public bool IsDown;       // 当前注入状态
        public long NextDue;
    }

    private readonly List<ClickState> _tasks = new();
    private readonly AutoResetEvent _wake = new(false); // 按下 / 松开 / 停止时唤醒工作线程，空闲时它就一直睡着
    private readonly Stopwatch _sw = new();
    private MouseHook? _hook;
    private Thread? _worker;
    private volatile bool _stop;
    private readonly bool[] _swallowUp = new bool[7]; // 按 VK 键码索引（1/2/4/5/6）：按下被拦截时，配对的抬起也要拦截
    private readonly object _periodSync = new();
    private bool _periodRaised;   // timeBeginPeriod/timeEndPeriod 必须成对：重复 Stop 不能再还给系统一次

    public bool Running { get; private set; }

    private volatile bool _blocked; // SendInput 被系统/UIPI 拦截（返回 0）

    /// <summary>注入被系统拦截（常见于未以管理员运行、或目标窗口权限更高）。</summary>
    public bool InjectBlocked => _blocked;

    /// <summary>注入首次被拦截时触发一次（工作线程上回调，UI 需自行调度）。</summary>
    public event Action? Blocked;

    /// <summary>所有目标行都是「自动点击」且次数已跑完时触发（工作线程上回调，UI 需自行调度）。</summary>
    public event Action? AllDone;

    // 测试用：设置环境变量 KEYMOUSE_DRYRUN=1 时不真正注入点击，只跑调度计数
#if DEBUG
    internal static readonly bool DryRun = Environment.GetEnvironmentVariable("KEYMOUSE_DRYRUN") == "1";
#else
    // 发布版恒为 false：环境变量不能把正式版的连点变成空跑，环境变量在发行版里完全无效
    internal static readonly bool DryRun = false;
#endif

    private void Send(int button, bool up)
    {
        if (DryRun)
        {
#if DEBUG
            EngineProbe.Record(1, up); // 空跑时只记时间戳，供改前改后对比调度精度
#endif
            return;
        }
        if (!NativeInput.SendMouseButton(button, up)) MarkBlocked();
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    public void Start(List<MouseTarget> targets)
    {
        Stop();
        _blocked = false; // 新一轮重新判定，避免上次的拦截状态带到下次开启
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
        _wake.Reset();   // 上一次停止可能留下一次唤醒信号，先吃掉
        // 1ms 计时精度不在这里要：工作线程只在真要连点时才提精度，空闲时把系统计时器还回去
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "MouseClickEngine" };
        _worker.Start();
        _hook.Install();
        Running = true;
    }

    public void Stop()
    {
        _stop = true;
        _wake.Set();   // 线程可能正睡在无限等待里，叫醒它才能及时退出
        try { _worker?.Join(1000); } catch { /* 线程可能尚未启动 */ }
        _worker = null;
        _sw.Stop();
        SetPeriod(false);
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
#if DEBUG
        if (DryRun) EngineProbe.Report(1, "连点");
#endif
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

                // 与键盘连按一致的帧兼容节奏：按下/抬起各 ≥20ms，有效间隔下限 40ms
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
            if (_stop) break;
            WaitNext();
        }
    }

    /// <summary>
    /// 等到「最早的到期时刻」，或有人按下/松开/停止时被立即叫醒。
    /// 一行都没在连点时用无限等待——工作线程真正睡着，不再 1ms 轮询。
    /// </summary>
    private void WaitNext()
    {
        long now = _sw.ElapsedTicks;
        long earliest = long.MaxValue;
        foreach (var t in _tasks)
        {
            if (t.Done || !t.Held) continue;
            if (t.NextDue < earliest) earliest = t.NextDue;
        }

        bool idle = earliest == long.MaxValue;
        // 只有真要连点时才需要 1ms 精度（按下/抬起各 20ms 靠它对齐帧），空闲时还给系统
        SetPeriod(!idle);
        if (idle)
        {
            _wake.WaitOne();
            return;
        }

        long left = earliest - now;
        if (left <= 0) return; // 已经到期，直接回循环处理，不用睡
        int ms = (int)Math.Min((left * 1000 + Stopwatch.Frequency - 1) / Stopwatch.Frequency, 1000);
        if (ms <= 0) return;
        _wake.WaitOne(ms);
    }

    /// <summary>提/还系统计时精度（1ms）。加锁只为防 Stop 与工作线程同时改（Join 超时的极端情况）。</summary>
    private void SetPeriod(bool on)
    {
        lock (_periodSync)
        {
            if (_periodRaised == on) return;
            if (on) timeBeginPeriod(1); else timeEndPeriod(1);
            _periodRaised = on;
        }
    }

    /// <summary>只在第一次失败时记日志并通知 UI，避免一分钟写几万行/弹几万个气泡。</summary>
    private void MarkBlocked()
    {
        if (_blocked) return;
        _blocked = true;
        Logger.Error("MouseClickEngine", new InvalidOperationException(
            "鼠标连点注入被系统拦截（SendInput 返回 0）。请以管理员身份运行；若游戏以更高权限启动，注入无法生效。"));
        try { Blocked?.Invoke(); } catch (Exception ex) { Logger.Error("Blocked", ex); }
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
        _wake.Set();   // 工作线程可能正空闲等待，叫醒它马上开点
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
        _wake.Set();    // 可能正睡在「等下一次按下」，立刻醒来补抬起
    }
}
