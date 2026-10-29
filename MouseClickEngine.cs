using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DnfAutoFire;

/// <summary>
/// 鼠标连点引擎：按住配置的鼠标键时自动连点，松开即停。
/// 与 AutoFireEngine 相同的 Stopwatch 调度：按下保持 ≥20ms、抬起 ≥20ms，
/// 有效间隔下限 40ms（游戏按帧轮询）。次数/时长模式按「每次按压」重新计数。
/// 触发时拦截原生点击（吞掉按下与配对的抬起），由引擎统一注入，避免重复点击。
/// </summary>
internal sealed class MouseClickEngine
{
    public enum StopMode { Count, Duration, Infinite }

    public readonly record struct MouseTarget(int Button, int IntervalMs, StopMode Mode, int StopValue);

    private sealed class ClickState
    {
        public int Button;    // 1=左键 2=右键
        public int IntervalMs;
        public StopMode Mode;
        public int StopValue;
        public volatile bool Held;  // 物理按住中（本次连点会话有效）
        public bool Done;     // 本次按压的停止条件已达成
        public bool IsDown;   // 当前注入状态
        public long NextDue;
        public int ClicksLeft;
        public long Deadline;
    }

    private readonly List<ClickState> _tasks = new();
    private readonly Stopwatch _sw = new();
    private MouseHook? _hook;
    private Thread? _worker;
    private volatile bool _stop;
    private readonly bool[] _swallowUp = new bool[3]; // 按下被拦截时，配对的抬起也要拦截

    public bool Running { get; private set; }

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
            _tasks.Add(new ClickState
            {
                Button = t.Button,
                IntervalMs = Math.Max(1, t.IntervalMs),
                Mode = t.Mode,
                StopValue = Math.Max(1, t.StopValue)
            });
        }

        _hook = new MouseHook();
        _hook.ButtonDown += OnButtonDown;
        _hook.ButtonUp += OnButtonUp;

        _stop = false;
        _sw.Restart();
        timeBeginPeriod(1);
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
        timeEndPeriod(1);
        // 释放仍处于"模拟按下"状态的鼠标键，避免卡键
        foreach (var t in _tasks)
        {
            if (t.IsDown)
            {
                try { NativeInput.SendMouseButton(t.Button == 1, up: true); } catch { /* 尽力释放 */ }
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
                if (!t.Held)
                {
                    if (t.IsDown) // 中途松开：立刻抬起，不留卡键
                    {
                        NativeInput.SendMouseButton(t.Button == 1, up: true);
                        t.IsDown = false;
                    }
                    continue;
                }

                // 停止条件已达成（次数点满 / 时长耗尽）：结束本次连点会话，等下次物理按压
                if (t.Done || (t.Mode == StopMode.Duration && now >= t.Deadline))
                {
                    if (t.IsDown)
                    {
                        NativeInput.SendMouseButton(t.Button == 1, up: true);
                        t.IsDown = false;
                    }
                    t.Held = false;
                    continue;
                }

                if (now < t.NextDue) continue;

                // 与键盘连发一致的帧兼容节奏：按下/抬起各 ≥20ms，有效间隔下限 40ms
                long period = Math.Max(t.IntervalMs, 40) * Stopwatch.Frequency / 1000;
                long hold = Math.Clamp(t.IntervalMs / 2, 20, 30) * Stopwatch.Frequency / 1000;
                if (!t.IsDown)
                {
                    NativeInput.SendMouseButton(t.Button == 1, up: false); // 按下
                    t.IsDown = true;
                    t.NextDue = _sw.ElapsedTicks + hold;
                }
                else
                {
                    NativeInput.SendMouseButton(t.Button == 1, up: true);  // 抬起（完成一次点击）
                    t.IsDown = false;
                    if (t.Mode == StopMode.Count && --t.ClicksLeft <= 0)
                    {
                        t.Done = true;
                        continue;
                    }
                    t.NextDue = Math.Max(_sw.ElapsedTicks + period - hold, _sw.ElapsedTicks);
                }
            }
            Thread.Sleep(1);
        }
    }

    private void OnButtonDown(int button)
    {
        var t = _tasks.FirstOrDefault(x => x.Button == button);
        if (t == null) return;

        // 拦截原生按下，配对的抬起也一并拦截（即使中途次数点满）
        _hook!.SuppressCurrent();
        _swallowUp[button] = true;

        if (t.Held) return; // 原生长按会反复触发 down
        // 开始一次新的连点会话：次数/时长按本次按压重新起算
        t.Held = true;
        t.Done = false;
        t.IsDown = false;
        t.ClicksLeft = t.StopValue;
        t.Deadline = _sw.ElapsedTicks + (long)t.StopValue * Stopwatch.Frequency;
        t.NextDue = 0; // 立刻补一发，模拟真实单击
    }

    private void OnButtonUp(int button)
    {
        var t = _tasks.FirstOrDefault(x => x.Button == button);
        if (t == null) return;
        if (_swallowUp[button])
        {
            _hook!.SuppressCurrent();
            _swallowUp[button] = false;
        }
        t.Held = false; // 松开即停，抬起由 WorkerLoop 统一处理
    }
}
