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
    private sealed class KeyState
    {
        public int Vk;
        public int IntervalMs;
        public volatile bool Held;
        public bool IsDown;  // 当前注入状态：true=模拟按下中
        public long NextDue; // 下一次动作（按下或抬起）的 Stopwatch ticks
    }

    private readonly List<KeyState> _keys = new();
    private KeyboardHook? _hook;
    private Thread? _worker;
    private volatile bool _stop;

    public bool Running { get; private set; }

    /// <summary>"A" 保留原生并叠加；"B" 拦截原生仅模拟。</summary>
    public string Mode { get; set; } = "B";

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    public void Start(List<(int Vk, int Ms)> targets)
    {
        Stop();
        foreach (var t in targets)
        {
            if (_keys.Any(k => k.Vk == t.Vk)) continue;
            _keys.Add(new KeyState { Vk = t.Vk, IntervalMs = Math.Max(1, t.Ms) });
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
                try { NativeInput.SendKey(k.Vk, up: true); } catch { /* 尽力释放 */ }
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
                if (!k.Held)
                {
                    if (k.IsDown) // 中途松开：立刻抬起，不留卡键
                    {
                        NativeInput.SendKey(k.Vk, up: true);
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
                    NativeInput.SendKey(k.Vk, up: false); // 按下
                    k.IsDown = true;
                    k.NextDue = sw.ElapsedTicks + hold;   // 保持按压 holdMs 后抬起
                }
                else
                {
                    NativeInput.SendKey(k.Vk, up: true);  // 抬起
                    k.IsDown = false;
                    // 下一轮按下：本周期剩余时间
                    k.NextDue = Math.Max(sw.ElapsedTicks + period - hold, sw.ElapsedTicks);
                }
            }
            Thread.Sleep(1);
        }
    }

    private void OnKeyDown(int vk)
    {
        var k = _keys.FirstOrDefault(x => x.Vk == vk);
        if (k == null) return;
        if (Mode == "B") _hook!.SuppressCurrent();
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
        k.Held = false; // 抬起由 WorkerLoop 统一处理（若正按着会立即释放）
    }
}
