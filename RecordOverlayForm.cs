using System.Drawing.Drawing2D;

namespace DnfAutoFire;

/// <summary>
/// 录制状态悬浮条：主窗口收进托盘后，屏幕上留一条小的置顶提示。
/// 显示红点（闪烁）/「录制中」/ 计时 / 点数 / ■ 停止；可拖动，右上角位置默认贴工作区右下角。
/// 悬浮条自身的矩形会作为「排除区域」交给录制器，所以拖它、点■都不会进录制。
/// </summary>
internal sealed class RecordOverlayForm : Form
{
    private const int DotSize = 8;
    private static readonly Rectangle StopRect = new(200, 11, 22, 22);
    private static readonly Color RecordRed = Color.FromArgb(255, 77, 79);
    private static readonly Color RecordRedBg = Color.FromArgb(255, 241, 240);

    private readonly MacroRecorder _recorder;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private bool _blinkOn = true;
    private bool _stopHover;
    private bool _dragging;
    private Point _dragCursor;
    private Point _dragOrigin;
    private bool _closingSilently;

    /// <summary>点■、或点数/时长触顶时触发；由主窗口统一走停止流程。</summary>
    public event Action? StopRequested;

    /// <summary>拖动结束后触发，调用方据此刷新排除区域。</summary>
    public event Action? BoundsMoved;

    public RecordOverlayForm(MacroRecorder recorder)
    {
        _recorder = recorder;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        ClientSize = new Size(236, 44);

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);

        UpdateRegion();
        _timer.Tick += (_, _) => OnTickInternal();
        _timer.Start();
    }

    /// <summary>整块矩形（外扩在录制器侧做）用于排除录制。</summary>
    public Rectangle ExcludeBounds => new(Left, Top, Width, Height);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        using var path = RoundedPath(new Rectangle(0, 0, Width, Height), 10);
        Region = new Region(path);
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void OnTickInternal()
    {
        _blinkOn = !_blinkOn;
        Invalidate();
        if (_recorder.LimitReached) StopRequested?.Invoke();
    }

    private static string FormatElapsed(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var all = new Rectangle(0, 0, Width - 1, Height - 1);
        AntStyle.FillRounded(g, Color.White, all, 10);
        AntStyle.DrawRounded(g, Color.FromArgb(229, 230, 235), all, 10);

        // 红点（500ms 明暗交替）
        using (var dot = new SolidBrush(_blinkOn ? RecordRed : Color.FromArgb(255, 163, 158)))
            g.FillEllipse(dot, 14, (Height - DotSize) / 2, DotSize, DotSize);

        using var boldFont = new Font(AntStyle.FontName, 9f, FontStyle.Bold);
        using var font8 = new Font(AntStyle.FontName, 8f);
        TextRenderer.DrawText(g, "录制中", boldFont, new Rectangle(30, 0, 60, Height), RecordRed,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        TextRenderer.DrawText(g, FormatElapsed(_recorder.ElapsedMs), boldFont,
            new Rectangle(86, 0, 60, Height), AntStyle.TextPri,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        TextRenderer.DrawText(g, "· " + _recorder.PointCount + " 点", font8,
            new Rectangle(146, 0, 52, Height), AntStyle.TextTertiary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // ■ 停止
        AntStyle.FillRounded(g, _stopHover ? RecordRed : RecordRedBg, StopRect, 5);
        using var stopBrush = new SolidBrush(_stopHover ? Color.White : RecordRed);
        g.FillRectangle(stopBrush, StopRect.X + 7, StopRect.Y + 7, 8, 8);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = StopRect.Contains(e.Location);
        if (hover != _stopHover)
        {
            _stopHover = hover;
            Cursor = hover ? Cursors.Hand : Cursors.SizeAll;
            Invalidate();
        }
        if (!_dragging) return;
        var now = Cursor.Position;
        Location = new Point(_dragOrigin.X + (now.X - _dragCursor.X), _dragOrigin.Y + (now.Y - _dragCursor.Y));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (StopRect.Contains(e.Location))
        {
            StopRequested?.Invoke();
            return;
        }
        _dragging = true;
        _dragCursor = Cursor.Position;
        _dragOrigin = Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        BoundsMoved?.Invoke();
    }

    /// <summary>Alt+F4 等外部关闭也要变成"停止录制"，否则数据会丢。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closingSilently && e.CloseReason != CloseReason.ApplicationExitCall)
        {
            e.Cancel = true;
            StopRequested?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>由主窗口在完成保存后调用，跳过"关闭即停止"的保护逻辑。</summary>
    public void CloseSilently()
    {
        _closingSilently = true;
        _timer.Stop();
        _timer.Dispose();
        Close();
        Dispose();
    }
}
