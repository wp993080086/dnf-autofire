using System.Drawing.Drawing2D;

namespace DnfAutoFire;

/// <summary>
/// 回放状态浮条：回放期间主窗口收进托盘，屏幕右下角留一条只读提示
/// （方案名 / 第几次 / 已用时），与录制浮条同一位置、同一视觉规格。
///
/// 与录制浮条的关键差异：整块**鼠标穿透**（WS_EX_TRANSPARENT）。
/// 回放时鼠标是程序按轨迹注入的，浮条若可点击，轨迹正好经过这里就会被浮条吃掉一次点击，
/// 命中按钮甚至会误停回放；所以这里只做展示——停止请按热键或用托盘菜单。
/// </summary>
internal sealed class PlayOverlayForm : Form
{
    private const int DotSize = 8;
    private const int PadLeft = 14;
    private const int PadRight = 14;
    private const int GapAfterDot = 6;
    private const int GapMid = 12;
    private const int GapSmall = 8;
    private const int Radius = 10;
    private const int MinWidth = 168;
    private const string BlockedText = "注入被拦截 · 请以管理员身份运行";
    private const TextFormatFlags Flags =
        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
    private const TextFormatFlags MeasFlags =
        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    private readonly Font _bold = new(AntStyle.FontName, 9f, FontStyle.Bold);
    private readonly Font _small = new(AntStyle.FontName, 8f);
    private readonly string _name;
    private readonly int _total;        // 总次数，0 = 一直放（不显示分母）
    private int _loop = 1;
    private long _elapsedMs;
    private bool _blocked;
    private bool _blinkOn = true;

    public PlayOverlayForm(string profileName, int total)
    {
        _name = string.IsNullOrWhiteSpace(profileName) ? "方案" : profileName.Trim();
        _total = Math.Max(0, total);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Height = 44;

        ResizeToContent(keepRight: false);

        // 构造时就把位置定好：靠 OnShown 定位会让首帧先在 (0,0) 闪一下再跳过去
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);
    }

    /// <summary>只做提示，绝不能把焦点从游戏手里抢走。</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // 穿透 + 不进 Alt+Tab + 不抢焦点
            cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = RoundedPath(new Rectangle(0, 0, Width, Height), Radius);
        Region = new Region(path);
    }

    /// <summary>
    /// 同步一次进度（由主窗口 200ms 定时器驱动）：当前第几次、已用时、注入是否被拦截。
    /// 顺手翻转状态点明暗，与录制浮条的红点节奏一致。
    /// </summary>
    public void UpdateProgress(int loop, long elapsedMs, bool blocked)
    {
        _loop = Math.Max(1, loop);
        _elapsedMs = Math.Max(0, elapsedMs);
        _blocked = blocked;
        _blinkOn = !_blinkOn;
        ResizeToContent(keepRight: true);
        Invalidate();
    }

    /// <summary>调用方先摘引用再调它，外部关闭（退出程序）也走这里。</summary>
    public void CloseSilently()
    {
        try { Close(); }
        finally { Dispose(); }
    }

    // 文本变长（次数从 9 变 10、被拦截换成警告）时重算宽度；固定右边缘，视觉上只向左长
    private void ResizeToContent(bool keepRight)
    {
        int w = _blocked
            ? PadLeft + DotSize + GapAfterDot + Measure(BlockedText, _small).Width + PadRight
            : PadLeft + DotSize + GapAfterDot
              + Measure("回放中", _bold).Width
              + GapMid + Measure(_name, _small).Width
              + GapSmall + Measure(LoopText(), _small).Width
              + GapSmall + Measure(ElapsedText(), _small).Width
              + PadRight;

        w = Math.Max(w, MinWidth);
        if (w == Width) return;
        int right = Right;
        Width = w;
        if (keepRight) Left = right - w;
    }

    private string LoopText() => _total > 0 ? $"第 {_loop}/{_total} 次" : $"第 {_loop} 次";

    private string ElapsedText() => FormatElapsed(_elapsedMs);

    private static string FormatElapsed(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    private static Size Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, MeasFlags);

    private int DrawSeg(Graphics g, string text, Font font, Color color, int x)
    {
        int w = Measure(text, font).Width;
        TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, w, Height), color, Flags);
        return w;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var all = new Rectangle(0, 0, Width - 1, Height - 1);
        AntStyle.FillRounded(g, Color.White, all, Radius);
        AntStyle.DrawRounded(g, _blocked ? AntStyle.Danger : AntStyle.Border, all, Radius);

        int x = PadLeft;
        Color dotColor = _blocked ? AntStyle.Danger
            : _blinkOn ? AntStyle.Primary
            : Color.FromArgb(163, 200, 255);
        using (var dot = new SolidBrush(dotColor))
            g.FillEllipse(dot, x, (Height - DotSize) / 2, DotSize, DotSize);
        x += DotSize + GapAfterDot;

        if (_blocked)
        {
            DrawSeg(g, BlockedText, _small, AntStyle.Danger, x);
            return;
        }

        x += DrawSeg(g, "回放中", _bold, AntStyle.Primary, x) + GapMid;
        x += DrawSeg(g, _name, _small, AntStyle.TextPri, x) + GapSmall;
        x += DrawSeg(g, LoopText(), _small, AntStyle.TextTertiary, x) + GapSmall;
        DrawSeg(g, ElapsedText(), _small, AntStyle.TextTertiary, x);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bold.Dispose();
            _small.Dispose();
        }
        base.Dispose(disposing);
    }
}
