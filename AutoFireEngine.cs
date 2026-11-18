using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyMouseFactory;

/// <summary>
/// 连发引擎：独立高精度线程按 Stopwatch 调度。
/// DNF 这类游戏按帧轮询键盘状态、以「上帧未按下→本帧按下」的边沿触发攻击：
/// 按住不放只算一次，按下/抬起窗口短于 1 帧又会被整段错过。
/// 因此调度上强制「按下保持 ≥20ms、抬起 ≥20ms」，有效间隔下限 40ms（60 帧游戏）。
/// 模式 A：放行原生按键，叠加模拟连发；模式 B：拦截原生按键，仅程序模拟（默认）。
/// 工作线程没有「固定节拍」：按每个键的到期时刻等待，没人按键时阻塞在事件上零唤醒。
/// </summary>
internal sealed class AutoFireEngine
{
    /// <summary>触发方式：Hold=物理按住才连发、松手即停；Auto=启动后自动连发，不需要按住。</summary>
    public enum TriggerMode { Hold, Auto }

    /// <summary>一个目标键：间隔毫秒 + 触发方式 + 自动模式下的次数（0 = 无限）。</summary>
    public readonly record struct KeyTarget(int Vk, int IntervalMs, TriggerMode Mode, int Count);

    private sealed class KeyState
    {
        public int Vk;
        public int IntervalMs;
        public TriggerMode Mode;
        public int Remaining;     // 自动模式剩余次数，-1 = 无限
        public bool Done;         // 自动模式次数用完
        public volatile bool Held;
        public bool IsDown;  // 当前注入状态：true=模拟按下中
        public long NextDue; // 下一次动作（按下或抬起）的 Stopwatch ticks
    }

    private readonly List<KeyState> _keys = new();
    private readonly AutoResetEvent _wake = new(false); // 按键 / 松手 / 停止时唤醒工作线程，空闲时它就一直睡着
    private KeyboardHook? _hook;
    private Thread? _worker;
    private volatile bool _stop;
    private readonly object _periodSync = new();
    private bool _periodRaised;   // timeBeginPeriod/timeEndPeriod 必须成对：重复 Stop 不能再还给系统一次

    // 测试用：设置环境变量 DNF_DRYRUN=1 时不真正注入按键，只跑调度计数
#if DEBUG
    internal static readonly bool DryRun = Environment.GetEnvironmentVariable("DNF_DRYRUN") == "1";
#else
    // 发布版恒为 false：环境变量不能把正式版的连发变成空跑，环境变量在发行版里完全无效
    internal static readonly bool DryRun = false;
#endif

    private void Send(int vk, bool up)
    {
        if (DryRun)
        {
#if DEBUG
            EngineProbe.Record(0, up); // 空跑时只记时间戳，供改前改后对比调度精度
#endif
            return;
        }
        if (!NativeInput.SendKey(vk, up)) MarkBlocked();
    }

    public bool Running { get; private set; }

    private volatile bool _blocked; // SendInput 被系统/UIPI 拦截（返回 0）

    /// <summary>注入被系统拦截（常见于未以管理员运行、或目标窗口权限更高）。</summary>
    public bool InjectBlocked => _blocked;

    /// <summary>注入首次被拦截时触发一次（工作线程上回调，UI 需自行调度）。</summary>
    public event Action? Blocked;

    /// <summary>所有目标键都是「自动连按」且次数已跑完时触发（工作线程上回调，UI 需自行调度）。</summary>
    public event Action? AllDone;

    /// <summary>"A" 保留原生并叠加；"B" 拦截原生仅模拟。</summary>
    public string Mode { get; set; } = "B";

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    public void Start(List<KeyTarget> targets)
    {
        Stop();
        _blocked = false; // 新一轮重新判定，避免上次的拦截状态带到下次开启
        foreach (var t in targets)
        {
            if (_keys.Any(k => k.Vk == t.Vk)) continue;
            bool auto = t.Mode == TriggerMode.Auto;
            _keys.Add(new KeyState
            {
                Vk = t.Vk,
                IntervalMs = Math.Max(1, t.IntervalMs),
                Mode = t.Mode,
                Remaining = auto && t.Count > 0 ? t.Count : -1,
                Held = auto,  // 自动连按：启动即视为按住，不依赖物理按键
                NextDue = 0   // 立刻打出第一发
            });
        }

        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;

        _stop = false;
        _wake.Reset();   // 上一次停止可能留下一次唤醒信号，先吃掉
        // 1ms 计时精度不在这里要：工作线程只在真要连发时才提精度，空闲时把系统计时器还回去
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "AutoFireEngine" };
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
        SetPeriod(false);
        // 释放仍处于"模拟按下"状态的键，避免卡键
        foreach (var k in _keys)
        {
            if (k.IsDown)
            {
                try { Send(k.Vk, up: true); } catch { /* 尽力释放 */ }
                k.IsDown = false;
            }
        }
        _keys.Clear();
        _hook?.Dispose();
        _hook = null;
        Running = false;
#if DEBUG
        if (DryRun) EngineProbe.Report(0, "连发");
#endif
    }

    private void WorkerLoop()
    {
        var sw = Stopwatch.StartNew();
        while (!_stop)
        {
            long now = sw.ElapsedTicks;
            foreach (var k in _keys)
            {
                // 自动模式的次数已用完：释放并按空闲处理
                if (k.Done)
                {
                    if (k.IsDown)
                    {
                        Send(k.Vk, up: true);
                        k.IsDown = false;
                    }
                    continue;
                }
                if (!k.Held)
                {
                    if (k.IsDown) // 中途松开：立刻抬起，不留卡键
                    {
                        Send(k.Vk, up: true);
                        k.IsDown = false;
                    }
                    continue;
                }
                if (now < k.NextDue) continue;

                // 游戏按帧（60fps≈16.7ms）轮询键盘状态、以边沿触发：
                // 按下/抬起各保持 ≥20ms 才能保证至少跨越一次帧轮询被识别，
                // 有效间隔下限 = 20+20 = 40ms；设置的间隔更小也按 40ms 处理
                long period = Math.Max(k.IntervalMs, 40) * Stopwatch.Frequency / 1000;
                long hold = Math.Clamp(k.IntervalMs / 2, 20, 30) * Stopwatch.Frequency / 1000;
                if (!k.IsDown)
                {
                    Send(k.Vk, up: false); // 按下
                    if (DryRun) Console.WriteLine($"[dry] vk={k.Vk} 按下 remaining={k.Remaining}");
                    k.IsDown = true;
                    k.NextDue = sw.ElapsedTicks + hold;   // 保持按压 holdMs 后抬起
                }
                else
                {
                    Send(k.Vk, up: true);  // 抬起
                    k.IsDown = false;
                    // 自动模式：一发完成，扣减剩余次数，用完即停
                    if (k.Mode == TriggerMode.Auto && k.Remaining > 0 && --k.Remaining == 0)
                    {
                        k.Done = true;
                        if (DryRun) Console.WriteLine($"[dry] vk={k.Vk} 次数已用完");
                        continue;
                    }
                    // 下一轮按下：本周期剩余时间
                    k.NextDue = Math.Max(sw.ElapsedTicks + period - hold, sw.ElapsedTicks);
                }
            }

            // 全部都是自动连按且次数跑完 → 没有事情可等，引擎自行收尾
            if (_keys.Count > 0 && _keys.All(k => k.Mode == TriggerMode.Auto && k.Done))
            {
                _stop = true;
                if (DryRun) Console.WriteLine("[dry] AllDone（全部自动连按已跑完）");
                AllDone?.Invoke();
                break;
            }
            if (_stop) break;
            WaitNext(sw);
        }
    }

    /// <summary>
    /// 等到「最早的到期时刻」，或有人按下/松开/停止时被立即叫醒。
    /// 一个键都没在连发时用无限等待——工作线程真正睡着，不再 1ms 轮询。
    /// </summary>
    private void WaitNext(Stopwatch sw)
    {
        long now = sw.ElapsedTicks;
        long earliest = long.MaxValue;
        foreach (var k in _keys)
        {
            if (k.Done || !k.Held) continue;
            if (k.NextDue < earliest) earliest = k.NextDue;
        }

        bool idle = earliest == long.MaxValue;
        // 只有真要连发时才需要 1ms 精度（按下/抬起各 20ms 靠它对齐帧），空闲时还给系统
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
        Logger.Error("AutoFireEngine", new InvalidOperationException(
            "连发注入被系统拦截（SendInput 返回 0）。请以管理员身份运行；若游戏以更高权限启动，注入无法生效。"));
        try { Blocked?.Invoke(); } catch (Exception ex) { Logger.Error("Blocked", ex); }
    }

    private void OnKeyDown(int vk)
    {
        var k = _keys.FirstOrDefault(x => x.Vk == vk);
        if (k == null) return;
        if (Mode == "B") _hook!.SuppressCurrent();
        if (k.Mode == TriggerMode.Auto) return; // 自动连按不理会物理按压
        if (k.Held) return; // 原生长按会反复触发 keydown
        k.Held = true;
        k.IsDown = false;
        k.NextDue = 0; // 立刻补一发，模拟真实单击
        _wake.Set();   // 工作线程可能正空闲等待，叫醒它马上开火
    }

    private void OnKeyUp(int vk)
    {
        var k = _keys.FirstOrDefault(x => x.Vk == vk);
        if (k == null) return;
        if (Mode == "B") _hook!.SuppressCurrent();
        if (k.Mode == TriggerMode.Auto) return; // 自动连按不受松手影响
        k.Held = false; // 抬起由 WorkerLoop 统一处理（若正按着会立即释放）
        _wake.Set();    // 可能正睡在「等下一次按下」，立刻醒来补抬起
    }
}
