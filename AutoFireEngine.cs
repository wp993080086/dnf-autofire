namespace DnfAutoFire;

/// <summary>
/// 连发引擎：每个目标键独立一个定时器。
/// 按住目标键 → 按设定间隔循环 SendInput(press+release)；松开立刻停止。
/// 模式 A：放行原生按键，叠加模拟连发；模式 B：拦截原生按键，仅程序模拟（默认）。
/// </summary>
internal sealed class AutoFireEngine
{
    private readonly Dictionary<int, System.Windows.Forms.Timer> _timers = new();
    private KeyboardHook? _hook;

    public bool Running { get; private set; }

    /// <summary>"A" 保留原生并叠加；"B" 拦截原生仅模拟。</summary>
    public string Mode { get; set; } = "B";

    public void Start(List<(int Vk, int Ms)> targets)
    {
        Stop();
        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;

        foreach (var t in targets)
        {
            if (_timers.ContainsKey(t.Vk)) continue;
            var timer = new System.Windows.Forms.Timer { Interval = Math.Max(100, t.Ms) };
            int vk = t.Vk;
            timer.Tick += (_, _) => NativeInput.TapKey(vk);
            _timers[t.Vk] = timer;
        }

        _hook.Install();
        Running = true;
    }

    public void Stop()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }
        _timers.Clear();
        _hook?.Dispose();
        _hook = null;
        Running = false;
    }

    private void OnKeyDown(int vk)
    {
        if (!_timers.TryGetValue(vk, out var timer)) return;
        if (Mode == "B") _hook!.SuppressCurrent();
        // 原生长按会反复触发 keydown，定时器已在跑则忽略
        if (timer.Enabled) return;
        timer.Start();
    }

    private void OnKeyUp(int vk)
    {
        if (!_timers.TryGetValue(vk, out var timer)) return;
        if (Mode == "B") _hook!.SuppressCurrent();
        timer.Stop();
    }
}
