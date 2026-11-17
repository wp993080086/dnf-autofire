using System.Diagnostics;

namespace DnfAutoFire;

/// <summary>
/// 轨迹录制器：钩子回调里只做「过滤 + 追加到预分配 List」——不写文件、不碰 UI、不分配引用对象。
/// WH_MOUSE_LL 回调一旦超过 LowLevelHooksTimeout（默认 300ms）会被系统静默摘钩，
/// 所以采样路径必须保持到微秒级。
/// </summary>
internal sealed class MacroRecorder : IDisposable
{
    public const int MaxPoints = 60000;                              // 单次录制点数上限
    public const int MaxDurationMs = 10 * 60 * 1000;                  // 单次录制时长上限（10 分钟）
    private const int MinSampleGapMs = 8;                             // 最小采样间隔 ≈ 125 点/秒
    private const int MoveJitterPx2 = 4;                              // 抖动距离平方（2px）
    private const int GraceMs = 300;                                  // 启动宽限期：忽略"点开始录制"那一下

    private readonly List<RecordPoint> _points = new(MaxPoints);      // 预分配，避免扩容抖动
    private readonly object _sync = new();
    private readonly Stopwatch _sw = new();
    private MouseHook? _hook;
    private Rectangle _exclude;                                       // 悬浮条区域：落在里面的事件整块丢弃
    private int _lastX, _lastY, _lastT = -1;
    private volatile bool _limitReached;

    public bool Running { get; private set; }

    /// <summary>点数或时长触顶，UI 轮询到后应主动停止录制（回调里不直接停，避免在钩子内销毁钩子）。</summary>
    public bool LimitReached => _limitReached;

    public long ElapsedMs => _sw.ElapsedMilliseconds;

    public void Start(Rectangle excludeBounds)
    {
        StopHook();
        lock (_sync) _points.Clear();
        _lastT = -1;
        _limitReached = false;
        _exclude = Rectangle.Inflate(excludeBounds, 4, 4);
        _sw.Restart();

        _hook = new MouseHook();
        _hook.Raw += OnRaw;
        _hook.Install();
        Running = true;
    }

    /// <summary>悬浮条拖动后同步排除区域，否则会把自己拖拽的轨迹录进去。</summary>
    public void UpdateExclude(Rectangle overlayBounds) =>
        _exclude = Rectangle.Inflate(overlayBounds, 4, 4);

    /// <summary>停止并生成方案快照（调用方负责命名与落盘）。</summary>
    public RecordProfile Stop()
    {
        Running = false;
        StopHook();
        _sw.Stop();

        List<RecordPoint> points;
        lock (_sync) points = new List<RecordPoint>(_points);

        var v = NativeInput.VirtualScreen();
        return new RecordProfile
        {
            Id = RecordStore.NewId(),
            Name = "",
            CreatedUtc = DateTime.UtcNow,
            DurationMs = (int)_sw.ElapsedMilliseconds,
            Screen = new RecordScreen { X = v.X, Y = v.Y, W = v.Width, H = v.Height },
            Points = points
        };
    }

    private void StopHook()
    {
        _hook?.Dispose();
        _hook = null;
    }

    public void Dispose() => StopHook();

    // 钩子回调（UI 线程）：只有分支判断与一次 List.Add，禁止任何阻塞/分配
    private void OnRaw(MouseHook.RawEvent e)
    {
        if (!Running) return;
        int t = (int)_sw.ElapsedMilliseconds;
        if (t < GraceMs) return;                       // 宽限期
        if (_exclude.Contains(e.X, e.Y)) return;       // 悬浮条区域不录

        int k = e.Msg switch
        {
            NativeMethods.WM_MOUSEMOVE => RecordKind.Move,
            NativeMethods.WM_LBUTTONDOWN => RecordKind.LeftDown,
            NativeMethods.WM_LBUTTONUP => RecordKind.LeftUp,
            NativeMethods.WM_RBUTTONDOWN => RecordKind.RightDown,
            NativeMethods.WM_RBUTTONUP => RecordKind.RightUp,
            NativeMethods.WM_MBUTTONDOWN => RecordKind.MiddleDown,
            NativeMethods.WM_MBUTTONUP => RecordKind.MiddleUp,
            _ => -1
        };
        if (k < 0) return;                             // 滚轮等本期不录

        if (k == RecordKind.Move && _lastT >= 0)
        {
            int dt = t - _lastT;
            int dx = e.X - _lastX, dy = e.Y - _lastY;
            int dist2 = dx * dx + dy * dy;
            if (dt < MinSampleGapMs && dist2 < MoveJitterPx2) return; // 高频小抖动丢弃
            if (dt < 40 && dist2 == 0) return;                        // 停住时的重复事件丢弃
        }

        if (_limitReached) return;

        lock (_sync)
        {
            if (_points.Count >= MaxPoints || t >= MaxDurationMs)
            {
                _limitReached = true;
                return;
            }
            _points.Add(new RecordPoint { T = t, K = k, X = e.X, Y = e.Y });
        }

        _lastT = t;
        _lastX = e.X;
        _lastY = e.Y;
    }
}
