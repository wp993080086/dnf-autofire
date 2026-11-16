using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DnfAutoFire;

/// <summary>
/// 连发引擎：独立高精度线程按 Stopwatch 调度。
/// DNF 这类游戏按帧轮询键盘状态、以「上帧未按下→本帧按下」的边沿触发攻击：
/// 按住不放只算一次，按下/抬起窗口短于 1 帧又会被整段错过。
/// 因此调度上强制「按下保持 ≥20ms、抬起 ≥20ms」，有效间隔下限 40ms（60 帧游戏）。
/// 模式 A：放行原生按键，叠加模拟连发；模式 B：拦截原生按键，仅程序模拟（默认）。
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
    private KeyboardHook? _hook;
    private Thread? _worker;
    private volatile bool _stop;

    // 测试用：设置环境变量 DNF_DRYRUN=1 时不真正注入按键，只跑调度计数
    internal static readonly bool DryRun = Environment.GetEnvironmentVariable("DNF_DRYRUN") == "1";

    private static void Send(int vk, bool up)
    {
        if (DryRun) return;
        NativeInput.SendKey(vk, up);
    }

    public bool Running { get; private set; }

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
        timeBeginPeriod(1); // 把系统计时精度提到 1ms，Sleep(1) 才真正约 1ms
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "AutoFireEngine" };
        _worker.Start();
        _hook.Install();
        Running = true;
    }

    public void Stop()
    {
        _stop = true;
        try { _worker?.Join(1000); } catch { /* 线程可能尚未启动 */ }
        _worker = null;
        timeEndPeriod(1);
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
            Thread.Sleep(1);
        }
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
    }

    private void OnKeyUp(int vk)
    {
        var k = _keys.FirstOrDefault(x => x.Vk == vk);
        if (k == null) return;
        if (Mode == "B") _hook!.SuppressCurrent();
        if (k.Mode == TriggerMode.Auto) return; // 自动连按不受松手影响
        k.Held = false; // 抬起由 WorkerLoop 统一处理（若正按着会立即释放）
    }
}
