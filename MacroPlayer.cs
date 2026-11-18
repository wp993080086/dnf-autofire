using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyMouseFactory;

/// <summary>回放模式：回放 N 次 / 一直放 / 定时 N 分钟。</summary>
internal enum PlayMode { Count, Infinite, Duration }

/// <summary>
/// 轨迹回放器：独立高精度线程按录制时间轴注入鼠标事件。
/// 与录制对称——录的是「相对开始时间的物理坐标 + 事件类型」，放的时候按同一时间轴重放。
///
/// 几个刻意为之的细节：
/// - 定位用 MOUSEEVENTF_ABSOLUTE + VIRTUALDESK 归一化（不用 SetCursorPos），
///   多屏含负坐标、125% 缩放下都与录制时的物理像素一致。
/// - 相邻两次移动间隔 &gt;32ms 且位移 &gt;4px 时按 16ms 步长插值：游戏按帧轮询光标，
///   直接瞬移可能整段跳过，插值后至少每帧有一个位置。
/// - 按下/抬起间隔 &lt;20ms 时补到 20ms（与连发引擎同一个下限结论：一帧 60fps≈16.7ms）。
/// - 轮与轮之间按「回放间隔」等待（默认 1 秒，可设 0 = 轮尾直接接轮头）。
/// - 停止时补发所有处于按下状态的键，避免"回放停在按住那一帧"导致卡键。
/// - 工作线程不轮询：按时间轴下一个点的到期时刻等待，到点前线程一直睡着；
///   轮间隔里连 1ms 系统计时精度也还回去，最后 50ms 再提回来定点，避免长间隔累积漂移。
/// </summary>
internal sealed class MacroPlayer : IDisposable
{
    public const int DefaultGapSec = 1;    // 回放间隔默认 1 秒
    public const int MaxGapSec = 999999;   // 回放间隔上限（秒，约 11.5 天）
    private const int MinHoldMs = 20;     // 按下保持下限
    private const int InterpGapMs = 32;   // 相邻移动间隔超过它才插值
    private const int InterpStepMs = 16;  // 插值步长（对齐 60fps 轮询）
    private const int InterpMinPx2 = 16;  // 位移平方（4px）：太小不动也没必要插
    private const int WaitFineWindowMs = 50; // 剩余时间超过它先粗等，把最后 50ms 留给小步
    private const int WaitSliceMs = 10;      // 尾段单次等待上限：被调度延迟后下一轮能立刻补偿

    // 测试用：KEYMOUSE_DRYRUN=1 时只跑时间轴、不注入任何输入
#if DEBUG
    internal static readonly bool DryRun = Environment.GetEnvironmentVariable("KEYMOUSE_DRYRUN") == "1";
#else
    // 发布版恒为 false：环境变量不能把正式版的回放变成空跑，环境变量在发行版里完全无效
    internal static readonly bool DryRun = false;
#endif

    private readonly object _sync = new();
    private readonly HashSet<int> _pressed = new(); // 当前处于按下状态的鼠标键（VK）
    private readonly AutoResetEvent _wake = new(false); // Stop 时叫醒睡在等待上的工作线程
    private readonly object _periodSync = new();
    private bool _periodRaised; // 1ms 计时精度是否已申请（timeBeginPeriod/timeEndPeriod 必须成对）
    private Thread? _worker;
    private volatile bool _stop;
    private volatile bool _running;
    private Stopwatch _sw = new();
    private RecordProfile? _profile;
    private PlayMode _mode;
    private int _value;   // Count=次数，Duration=分钟，Infinite 忽略
    private int _loop;
    private int _current; // 正在进行（或刚放完）的是第几次，第 1 次进行中 = 1
    private int _sent;
    private int _gapMs = DefaultGapSec * 1000;   // 两轮之间的等待（0 = 不等待，轮尾直接接轮头）
    private Rectangle _virtualScreen;            // 虚拟桌面范围：开始时取一次，逐点定位不必再查系统度量
    private volatile bool _blocked; // SendInput 被系统/UIPI 拦截（返回 0）

#if DEBUG
    private readonly List<int> _late = new(); // DryRun 下每个到点的迟到毫秒，回放结束时打印分布
#endif

    public bool Running => _running;

    /// <summary>注入被系统拦截（常见于未以管理员运行、或目标窗口权限更高）。</summary>
    public bool InjectBlocked => _blocked;

    /// <summary>已完成轮数（第一轮进行中为 0）。</summary>
    public int Loop => Volatile.Read(ref _loop);

    /// <summary>正在进行的是第几次（从 1 起，轮间隔里仍是刚放完的那一次）。状态条按它显示更准。</summary>
    public int CurrentLoop => Volatile.Read(ref _current);

    public long ElapsedMs => _sw.ElapsedMilliseconds;

    public string ProfileName => _profile?.Name ?? "";

    /// <summary>回放自然结束（跑完次数 / 定时到点）时在工作线程上触发；手动 Stop 不触发语义上的"完成"。</summary>
    public event Action? Finished;

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    public void Start(RecordProfile profile, PlayMode mode, int value, int gapSec = DefaultGapSec)
    {
        Stop();
        _profile = profile;
        _mode = mode;
        _value = Math.Max(1, value);
        // 界面按「秒」填，时间轴内部仍是毫秒：999999 秒 ≈ 1.0e9 ms，不会溢出
        _gapMs = Math.Clamp(gapSec, 0, MaxGapSec) * 1000;
        _loop = 0;
        _current = 0;
        _sent = 0;
#if DEBUG
        _late.Clear();
#endif
        _stop = false;
        _running = true;
        _wake.Reset();   // 上一次 Stop 可能留下唤醒信号，先吃掉，否则第一轮等待会立即醒
        _virtualScreen = NativeInput.VirtualScreen();
        _sw = Stopwatch.StartNew();

        // 1ms 精度不在这里申请：工作线程真正开始放点时才要，轮间隔里会还回去
        try
        {
            _worker = new Thread(Run) { IsBackground = true, Name = "MacroPlayer" };
            _worker.Start();
        }
        catch
        {
            _running = false;
            throw;
        }
    }

    public void Stop()
    {
        _stop = true;
        _wake.Set();   // 线程可能正睡在长间隔或等待下一个点上，叫醒它才能及时收手
        try { _worker?.Join(1500); } catch { /* 线程可能尚未启动 */ }
        _worker = null;
        ReleaseAll(); // 兜底：线程已退出但仍有键处于按下状态
        _running = false;
    }

    public void Dispose() => Stop();

    private void Run()
    {
        var profile = _profile!;
        int totalMs = _mode == PlayMode.Duration ? _value * 60000 : int.MaxValue;
        int loops = 0;

        try
        {
            SetPeriod(true); // 真要按时间轴放点，需要 1ms 精度

            while (!_stop)
            {
                Volatile.Write(ref _current, loops + 1);
                int loopStart = (int)_sw.ElapsedMilliseconds;
                PlayOnce(profile, loopStart, totalMs);

                if (_stop) break;

                loops++;
                Volatile.Write(ref _loop, loops);
                if (_mode == PlayMode.Count && loops >= _value) break;

                int now = (int)_sw.ElapsedMilliseconds;
                if (_mode == PlayMode.Duration)
                {
                    if (now >= totalMs) break;
                    WaitGap(Math.Min(now + _gapMs, totalMs));
                }
                else
                {
                    WaitGap(now + _gapMs);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MacroPlayer.Run", ex);
        }
        finally
        {
            ReleaseAll();
            _running = false;
            SetPeriod(false);
            if (DryRun) Logger.Info($"[dry] 回放结束：{profile.Name} {loops} 轮 {_sent} 点 {_sw.ElapsedMilliseconds}ms");
#if DEBUG
            if (DryRun && _late.Count > 0) LogLateStats();
#endif
            try { Finished?.Invoke(); } catch (Exception ex) { Logger.Error("MacroPlayer.Finished", ex); }
        }
    }

    /// <summary>回放一轮：按相对时间轴逐点等待并注入。定时模式会在半轮处截断。</summary>
    private void PlayOnce(RecordProfile profile, int loopStart, int totalMs)
    {
        var pts = profile.Points;
        int cx = int.MinValue, cy = int.MinValue; // 上次注入的光标位置（省略重复移动）
        int downVk = 0, downAt = 0;

        for (int i = 0; i < pts.Count; i++)
        {
            if (_stop) return;
            if (_mode == PlayMode.Duration && _sw.ElapsedMilliseconds >= totalMs) return;

            var p = pts[i];
            WaitUntil(loopStart + p.T);
            if (_stop) return;

            switch (p.K)
            {
                case RecordKind.Move:
                    MoveTo(p.X, p.Y, ref cx, ref cy);

                    // 段间插值：慢速大跨度移动补中间点，避免游戏里"瞬移"
                    if (i + 1 < pts.Count && pts[i + 1].K == RecordKind.Move)
                    {
                        var next = pts[i + 1];
                        int dt = next.T - p.T;
                        int dx = next.X - p.X, dy = next.Y - p.Y;
                        if (dt >= InterpGapMs && dx * dx + dy * dy >= InterpMinPx2)
                        {
                            int steps = Math.Max(1, dt / InterpStepMs);
                            for (int s = 1; s < steps; s++)
                            {
                                if (_stop) return;
                                if (_mode == PlayMode.Duration && _sw.ElapsedMilliseconds >= totalMs) return;
                                WaitUntil(loopStart + p.T + s * InterpStepMs);
                                MoveTo(p.X + dx * s / steps, p.Y + dy * s / steps, ref cx, ref cy);
                            }
                        }
                    }
                    break;

                case RecordKind.LeftDown:
                case RecordKind.RightDown:
                case RecordKind.MiddleDown:
                    // 按下点自带坐标：先对准再按下（录制时可能没有紧邻的 Move）
                    if (p.X != cx || p.Y != cy) MoveTo(p.X, p.Y, ref cx, ref cy);
                    downVk = DownToVk(p.K);
                    SendButton(downVk, up: false);
                    downAt = (int)_sw.ElapsedMilliseconds;
                    break;

                case RecordKind.LeftUp:
                case RecordKind.RightUp:
                case RecordKind.MiddleUp:
                    int upVk = UpToVk(p.K);
                    if (downVk == upVk)
                    {
                        // 按住时间过短时补到 20ms，否则游戏可能整段错过这一下
                        if ((int)_sw.ElapsedMilliseconds - downAt < MinHoldMs)
                            WaitUntil(downAt + MinHoldMs);
                        downVk = 0;
                    }
                    SendButton(upVk, up: true);
                    break;
            }

            Volatile.Write(ref _sent, i + 1);
        }

        // 一轮结束还有键没抬起（录制被截断）：补一次抬起，避免带到下一轮
        if (downVk != 0) SendButton(downVk, up: true);
    }

    /// <summary>
    /// 等到时间轴的某个绝对毫秒点。线程一直睡到点才醒，到点精度仍是 1ms 级；
    /// 中途被 Stop 叫醒会立刻返回，由调用方检查 _stop。
    ///
    /// 分两段：剩余超过 50ms 先一次性粗等（长间隔不必反复唤醒），最后 50ms 每 10ms 醒一次。
    /// 单次长等待一旦被系统调度推迟就会整段迟到（实测 25ms 间隔偶发 40ms 以上偏差），
    /// 小步等待每醒一次重算剩余，能把迟到压在一个步长内并立即补偿。
    /// </summary>
    private void WaitUntil(int targetMs)
    {
        while (!_stop)
        {
            int left = targetMs - (int)_sw.ElapsedMilliseconds;
            if (left <= 0)
            {
#if DEBUG
                if (DryRun) _late.Add(-left);
#endif
                return;
            }
            _wake.WaitOne(left > WaitFineWindowMs ? left - WaitFineWindowMs : Math.Min(left, WaitSliceMs));
        }
    }

    /// <summary>
    /// 轮与轮之间的间隔等待：长间隔里把 1ms 系统计时精度还回去（只剩系统默认的 ~15.6ms 定时器），
    /// 临门 30ms 再提回来精确定点。否则默认粒度会让每轮多出十几毫秒，
    /// 定时模式下几千轮累积下来总时长会明显偏长。
    /// </summary>
    private void WaitGap(int targetMs)
    {
        if (_gapMs <= 0) return;   // 0 = 轮尾直接接轮头，没有间隔可等
        SetPeriod(false);
        int coarse = targetMs - WaitFineWindowMs - (int)_sw.ElapsedMilliseconds;
        if (coarse > 0) _wake.WaitOne(coarse);
        SetPeriod(true);
        WaitUntil(targetMs);
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

    private void MoveTo(int x, int y, ref int cx, ref int cy)
    {
        if (x == cx && y == cy) return;
        cx = x;
        cy = y;
        if (DryRun) return;
        if (!NativeInput.SendMouseMoveAbsolute(x, y, _virtualScreen)) MarkBlocked();
    }

    private void SendButton(int vk, bool up)
    {
        lock (_sync)
        {
            if (up) _pressed.Remove(vk);
            else _pressed.Add(vk);
        }
        if (DryRun) return;
        if (!NativeInput.SendMouseButton(vk, up)) MarkBlocked();
    }

    /// <summary>只在第一次失败时记日志，避免一分钟写几万行。</summary>
    private void MarkBlocked()
    {
        if (_blocked) return;
        _blocked = true;
        Logger.Error("MacroPlayer", new InvalidOperationException(
            "鼠标注入被系统拦截（SendInput 返回 0）。请以管理员身份运行；若游戏以更高权限启动，注入无法生效。"));
    }

    private void ReleaseAll()
    {
        int[] keys;
        lock (_sync)
        {
            keys = _pressed.ToArray();
            _pressed.Clear();
        }
        if (DryRun || keys.Length == 0) return;
        foreach (var vk in keys)
        {
            try { NativeInput.SendMouseButton(vk, up: true); } catch { /* 尽力释放 */ }
        }
    }

    private static int DownToVk(int kind) => kind switch
    {
        RecordKind.RightDown => NativeMethods.VK_RBUTTON,
        RecordKind.MiddleDown => NativeMethods.VK_MBUTTON,
        _ => NativeMethods.VK_LBUTTON
    };

#if DEBUG
    /// <summary>Debug + DryRun 专用：打印每个时间点的迟到分布，用来验证等待策略有没有被调度推迟。</summary>
    private void LogLateStats()
    {
        var arr = _late.OrderBy(v => v).ToArray();
        int p50 = arr[arr.Length / 2];
        int p99 = arr[Math.Min(arr.Length - 1, (int)(arr.Length * 0.99))];
        Logger.Info($"[probe] 回放等待偏差：{arr.Length} 次 均值 {arr.Average():F2}ms  p50 {p50}  p99 {p99}  " +
                    $"最大 {arr[^1]}  迟到>10ms {arr.Count(v => v > 10)} 次  迟到>30ms {arr.Count(v => v > 30)} 次");
    }
#endif

    private static int UpToVk(int kind) => kind switch
    {
        RecordKind.RightUp => NativeMethods.VK_RBUTTON,
        RecordKind.MiddleUp => NativeMethods.VK_MBUTTON,
        _ => NativeMethods.VK_LBUTTON
    };
}
