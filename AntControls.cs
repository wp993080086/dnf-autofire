using System.Drawing.Drawing2D;

namespace DnfAutoFire;

/// <summary>Ant Design v5 设计变量（常用色值/圆角）。</summary>
internal static class AntStyle
{
    public const string FontName = "Microsoft YaHei UI";

    public static readonly Color Primary = Color.FromArgb(22, 119, 255);       // #1677FF
    public static readonly Color PrimaryHover = Color.FromArgb(64, 150, 255);  // #4096FF
    public static readonly Color PrimaryBg = Color.FromArgb(230, 244, 255);    // #E6F4FF
    public static readonly Color Danger = Color.FromArgb(255, 77, 79);         // #FF4D4F
    public static readonly Color DangerHover = Color.FromArgb(255, 120, 117);  // #FF7875
    public static readonly Color DangerBg = Color.FromArgb(255, 235, 235);
    public static readonly Color Border = Color.FromArgb(217, 217, 217);       // #D9D9D9
    public static readonly Color Split = Color.FromArgb(240, 240, 240);        // #F0F0F0
    public static readonly Color TextPri = Color.FromArgb(38, 38, 38);         // rgba(0,0,0,0.88)
    public static readonly Color TextSec = Color.FromArgb(89, 89, 89);         // rgba(0,0,0,0.65)
    public static readonly Color TextTertiary = Color.FromArgb(140, 140, 140); // rgba(0,0,0,0.45)
    public static readonly Color TextDisabled = Color.FromArgb(191, 191, 191); // #BFBFBF
    public static readonly Color BgLayout = Color.FromArgb(245, 245, 245);     // #F5F5F5

    /// <summary>
    /// 圆角矩形填充：中央矩形 + 四个小扇形拼成。
    /// 不用大尺寸 FillPath —— 大路径在 WM_PRINT/离屏渲染下会出现形变伪影，
    /// 直线和微小图形（≤12px）在任何渲染路径下都稳定。
    /// </summary>
    public static void FillRounded(Graphics g, Color color, Rectangle r, int radius)
    {
        using var b = new SolidBrush(color);
        int rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (rad <= 0)
        {
            g.FillRectangle(b, r);
            return;
        }
        g.FillRectangle(b, r.X + rad, r.Y, r.Width - 2 * rad, r.Height);
        g.FillRectangle(b, r.X, r.Y + rad, r.Width, r.Height - 2 * rad);
        int d = rad * 2;
        g.FillPie(b, r.X, r.Y, d, d, 180, 90);                       // 左上
        g.FillPie(b, r.Right - d, r.Y, d, d, 270, 90);               // 右上
        g.FillPie(b, r.Right - d, r.Bottom - d, d, d, 0, 90);        // 右下
        g.FillPie(b, r.X, r.Bottom - d, d, d, 90, 90);               // 左下
    }

    /// <summary>圆角矩形描边：四条直线 + 四段小圆弧。</summary>
    public static void DrawRounded(Graphics g, Color color, Rectangle r, int radius, float width = 1f)
    {
        using var p = new Pen(color, width);
        int rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (rad <= 0)
        {
            g.DrawRectangle(p, r);
            return;
        }
        int d = rad * 2;
        g.DrawLine(p, r.X + rad, r.Y, r.Right - rad, r.Y);
        g.DrawLine(p, r.X + rad, r.Bottom, r.Right - rad, r.Bottom);
        g.DrawLine(p, r.X, r.Y + rad, r.X, r.Bottom - rad);
        g.DrawLine(p, r.Right, r.Y + rad, r.Right, r.Bottom - rad);
        g.DrawArc(p, r.X, r.Y, d, d, 180, 90);
        g.DrawArc(p, r.Right - d, r.Y, d, d, 270, 90);
        g.DrawArc(p, r.Right - d, r.Bottom - d, d, d, 0, 90);
        g.DrawArc(p, r.X, r.Bottom - d, d, d, 90, 90);
    }
}

/// <summary>
/// AntD 白色圆角卡片（radius 8）。
/// 不用 Region 裁剪（WinForms Region 有渲染 glitch）：控件背景 = 页面底色，
/// OnPaint 里再填白色圆角矩形，四角自然露出底色。
/// </summary>
internal sealed class AntCard : Panel
{
    public AntCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = AntStyle.BgLayout;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        AntStyle.FillRounded(e.Graphics, Color.White, new Rectangle(0, 0, Width, Height), 8);
        AntStyle.DrawRounded(e.Graphics, AntStyle.Split, new Rectangle(0, 0, Width - 1, Height - 1), 8);
    }
}

/// <summary>
/// AntD 按钮：Primary（蓝底白字）/ Default（白底描边）/ Text（文字按钮）。
/// Danger=true 时主色换成红色。
/// </summary>
internal sealed class AntButton : Control
{
    public enum AntKind { Primary, Default, Text }

    public AntKind Kind { get; set; } = AntKind.Primary;

    private bool _danger;
    public bool Danger
    {
        get => _danger;
        set { if (_danger == value) return; _danger = value; Invalidate(); }
    }

    private bool _hover;

    public AntButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.White; // 圆角外露出的四角与卡片底色一致
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    // 全自绘控件：Text/Enabled 变化不会自动重绘，必须手动失效
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color bg = Color.White, text = AntStyle.TextPri;
        Color? border = null;

        if (!Enabled)
        {
            switch (Kind)
            {
                case AntKind.Primary:
                    bg = AntStyle.TextDisabled;
                    text = Color.White;
                    break;
                default:
                    text = AntStyle.TextDisabled;
                    if (Kind == AntKind.Default) border = AntStyle.Border; // Text 按钮禁用时不带边框
                    break;
            }
        }
        else if (Kind == AntKind.Primary)
        {
            bg = Danger ? AntStyle.Danger : AntStyle.Primary;
            text = Color.White;
            if (_hover) bg = Danger ? AntStyle.DangerHover : AntStyle.PrimaryHover;
        }
        else if (Kind == AntKind.Default)
        {
            border = AntStyle.Border;
            text = AntStyle.TextPri;
            if (_hover) { border = AntStyle.Primary; text = AntStyle.Primary; }
        }
        else // Text
        {
            text = Danger ? AntStyle.Danger : AntStyle.Primary;
            if (_hover) bg = Danger ? AntStyle.DangerBg : AntStyle.PrimaryBg;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        AntStyle.FillRounded(e.Graphics, bg, new Rectangle(0, 0, Width, Height), 6);
        if (border.HasValue)
            AntStyle.DrawRounded(e.Graphics, border.Value, new Rectangle(0, 0, Width - 1, Height - 1), 6);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>AntD 复选框：16px 圆角方框，选中蓝底白勾，悬停蓝描边。</summary>
internal sealed class AntCheckBox : Control
{
    private bool _checked;
    private bool _hover;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    public AntCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = false;
        Size = new Size(64, 30);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (Enabled && e.Button == MouseButtons.Left) Checked = !Checked;
    }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var box = new Rectangle(1, (Height - 16) / 2, 16, 16);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_checked)
        {
            // 标准复选框样式：蓝底白勾（扇形拼合画法，避免 FillPath 渲染形变）
            using var brush = new SolidBrush(Enabled ? (_hover ? AntStyle.PrimaryHover : AntStyle.Primary) : AntStyle.TextDisabled);
            AntStyle.FillRounded(e.Graphics, brush.Color, box, 3);
            using var checkPen = new Pen(Color.White, 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLines(checkPen, new[]
            {
                new Point(box.X + 4, box.Y + 8),
                new Point(box.X + 7, box.Y + 11),
                new Point(box.X + 12, box.Y + 5)
            });
        }
        else
        {
            // 未选中：白底 + 边框，悬停/聚焦变蓝，标准 Checkbox 观感
            AntStyle.FillRounded(e.Graphics, Color.White, box, 3);
            var borderColor = !Enabled ? AntStyle.TextDisabled : _hover ? AntStyle.Primary : AntStyle.Border;
            AntStyle.DrawRounded(e.Graphics, borderColor, box, 3);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(24, 0, Width - 24, Height),
            Enabled ? AntStyle.TextPri : AntStyle.TextDisabled,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>
/// AntD 输入框：白底 + #D9D9D9 圆角描边，聚焦变蓝并带浅蓝光晕。
/// 内部是无边框 TextBox，通过 Inner 访问。
/// </summary>
internal class AntInput : Panel
{
    public readonly TextBox Inner = new() { BorderStyle = BorderStyle.None };
    private bool _focused;

    public AntInput()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White; // 四角露出白色，与卡片底色一致
        Height = 30;
        Controls.Add(Inner);
        Inner.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        Inner.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        Resize += (_, _) => LayoutInner();
        LayoutInner();
    }

#pragma warning disable CS8765
    public override string Text
    {
        get => Inner.Text;
        set => Inner.Text = value ?? "";
    }
#pragma warning restore CS8765

    /// <summary>右侧为附加元素（如数字步进按钮）预留的宽度。</summary>
    protected virtual int RightInset => 0;

    private void LayoutInner()
    {
        Inner.Location = new Point(1, Math.Max(1, (Height - Inner.Height) / 2));
        Inner.Width = Width - 2 - RightInset;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Inner.BackColor = Enabled ? Color.White : AntStyle.BgLayout;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_focused)
        {
            AntStyle.DrawRounded(e.Graphics, Color.FromArgb(40, 22, 119, 255), rect, 6, 3f);
        }
        AntStyle.DrawRounded(e.Graphics, _focused ? AntStyle.Primary : AntStyle.Border, rect, 6);
    }
}

/// <summary>
/// AntD 数字输入框（InputNumber）：右侧上/下步进按钮（整块右栏可点，上下各半），
/// 默认步长 50，支持点击步进与键盘 ↑/↓，范围 1~5000，数值居中。
/// 可点区域悬停为手型光标，禁用时为禁止光标。
/// </summary>
internal sealed class AntNumber : AntInput
{
    public int Step { get; set; } = 50;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 5000;

    public event EventHandler? ValueChanged;

    protected override int RightInset => 22;

    public AntNumber()
    {
        Inner.TextAlign = HorizontalAlignment.Center;
        // 键盘 ↑/↓ 步进
        Inner.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Up) { Nudge(1); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Nudge(-1); e.SuppressKeyPress = true; }
        };
    }

    public int Value => int.TryParse(Inner.Text, out int v) ? v : Min;

    public void SetValue(int v)
    {
        Inner.Text = Math.Clamp(v, Min, Max).ToString();
        Invalidate(); // 边界变化后立即更新箭头置灰状态
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Nudge(int direction)
    {
        SetValue(Value + direction * Step);
    }

    private bool OnButtons(int x) => x >= Width - RightInset;

    private bool CanStep(int dir) => dir > 0 ? Value < Max : Value > Min;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!Enabled || !OnButtons(e.X)) return;
        int dir = e.Y < Height / 2 ? 1 : -1;
        if (CanStep(dir)) Nudge(dir); // 已到边界时点击无效
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!OnButtons(e.X)) { Cursor = Cursors.Default; return; }
        if (!Enabled) { Cursor = Cursors.No; return; }
        int dir = e.Y < Height / 2 ? 1 : -1;
        // 能步进的方向 → 手型；到边界点不动 → 禁止光标
        Cursor = CanStep(dir) ? Cursors.Hand : Cursors.No;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int x = Width - RightInset;
        int midY = Height / 2;
        using (var pen = new Pen(AntStyle.Split))
        {
            g.DrawLine(pen, x, 5, x, Height - 5);          // 与文本区分隔线
            g.DrawLine(pen, x, midY, Width - 2, midY);     // 上下按钮分隔线
        }

        // 到达边界的那半边箭头置灰
        var upColor = Enabled && Value < Max ? AntStyle.TextSec : AntStyle.TextDisabled;
        var downColor = Enabled && Value > Min ? AntStyle.TextSec : AntStyle.TextDisabled;
        int cx = x + RightInset / 2;
        using (var b = new SolidBrush(upColor))
            g.FillPolygon(b, new[] { new Point(cx - 5, midY / 2 + 2), new Point(cx + 5, midY / 2 + 2), new Point(cx, midY / 2 - 3) });
        int cy = midY + (Height - midY) / 2;
        using (var b = new SolidBrush(downColor))
            g.FillPolygon(b, new[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 3) });
    }
}

/// <summary>
/// AntD 选择器（Select）：包装 ComboBox。
/// 原生 FlatStyle.Flat 箭头粗糙 → Combo 加宽让原生箭头移出可视区被裁掉，
/// 右侧用自绘箭头覆盖层（点击展开下拉）；下拉项自绘（AntD 高亮样式）。
/// 外层自绘 AntD 圆角描边，悬停变蓝。
/// </summary>
internal sealed class AntSelect : Panel
{
    public readonly ComboBox Inner;
    private readonly Control _face;   // 覆盖层：自绘 AntD 外观；禁用状态鼠标穿透到下层 Combo
    private bool _hover;
    private bool _dropped;

    public AntSelect()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;

        // 覆盖层先创建（事件回调中引用它）
        _face = new Control { Enabled = false };
        _face.Paint += Face_Paint;

        Inner = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 20
        };
        Inner.DrawItem += Inner_DrawItem;                            // 下拉弹出的列表项自绘
        Inner.SelectedIndexChanged += (_, _) =>
        {
            _face.Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        };
        Inner.MouseEnter += (_, _) => { _hover = true; _face.Invalidate(); };
        Inner.MouseLeave += (_, _) => { _hover = false; _face.Invalidate(); };
        Inner.DropDown += (_, _) => { _dropped = true; _face.Invalidate(); };
        Inner.DropDownClosed += (_, _) => { _dropped = false; _face.Invalidate(); };
        Inner.EnabledChanged += (_, _) => _face.Invalidate();

        // WinForms 先添加的在顶层 → 覆盖层盖住 Combo；禁用使鼠标事件穿透到 Combo
        Controls.Add(_face);
        Controls.Add(Inner);
        Resize += (_, _) => LayoutInner();
        LayoutInner();
    }

    private void LayoutInner()
    {
        // Combo 加宽 ~20px：原生箭头按钮与右边框移出父容器被裁剪
        Inner.SetBounds(0, 0, Width + 20, Inner.Height);
        _face.SetBounds(0, 0, Width, Height);
        // 容器高度自适应 Combo 实际高度
        if (Height != Inner.Height) Height = Inner.Height;
    }

    private void Inner_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Inner.Items.Count) return;
        bool disabled = (e.State & DrawItemState.Disabled) != 0;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(disabled ? AntStyle.BgLayout : selected ? AntStyle.PrimaryBg : Color.White);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var color = disabled ? AntStyle.TextDisabled : selected ? AntStyle.Primary : AntStyle.TextPri;
        TextRenderer.DrawText(e.Graphics, Inner.Items[e.Index]!.ToString(), Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void Face_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool on = Enabled;
        using (var bg = new SolidBrush(on ? Color.White : AntStyle.BgLayout))
            g.FillRectangle(bg, 0, 0, Width, Height);
        var border = !on ? AntStyle.Border : (_hover || _dropped) ? AntStyle.PrimaryHover : AntStyle.Border;
        AntStyle.DrawRounded(g, border, new Rectangle(0, 0, Width - 1, Height - 1), 6);

        // 选中文字（AntD 左对齐，11px 内边距）
        TextRenderer.DrawText(g, Inner.Text, Font,
            new Rectangle(11, 0, Width - 32, Height),
            on ? AntStyle.TextPri : AntStyle.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        // chevron 下拉箭头
        var arrowColor = !on ? AntStyle.TextDisabled : (_hover || _dropped) ? AntStyle.Primary : AntStyle.TextSec;
        using (var pen = new Pen(arrowColor, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            int cx = Width - 12, cy = Height / 2;
            g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
        }
    }

    public int SelectedIndex
    {
        get => Inner.SelectedIndex;
        set => Inner.SelectedIndex = value;
    }

    /// <summary>选中项变化（转发自内部 ComboBox）。</summary>
    public event EventHandler? SelectedIndexChanged;

#pragma warning disable CS8765
    public override string Text
    {
        get => Inner.Text;
        set => Inner.Text = value ?? "";
    }
#pragma warning restore CS8765

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Inner.Enabled = Enabled;
        _face.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // 外观全部由覆盖层 _face 绘制，父层仅铺底色
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

/// <summary>
/// AntD 风格细滚动条：6px 圆角滑块（#D9D9D9），支持拖动和轨道点击，
/// 宿主通过 OffsetChanged 移动内容。
/// </summary>
internal sealed class FlatScrollBar : Control
{
    private Rectangle _thumbRect;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;
    private int _travel;

    public int ContentHeight { get; private set; }
    public int ViewportHeight { get; private set; }
    public int Offset { get; private set; }

    public event Action<int>? OffsetChanged;

    public FlatScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Width = 8;
        BackColor = Color.White;
        TabStop = false;
    }

    public int MaxOffset() => Math.Max(0, ContentHeight - ViewportHeight);

    public void SetMetrics(int contentHeight, int viewportHeight)
    {
        ContentHeight = contentHeight;
        ViewportHeight = viewportHeight;
        Offset = Math.Clamp(Offset, 0, MaxOffset());
        Visible = contentHeight > viewportHeight;
        Invalidate();
    }

    public void SetOffset(int value)
    {
        value = Math.Clamp(value, 0, MaxOffset());
        if (value == Offset) return;
        Offset = value;
        OffsetChanged?.Invoke(Offset);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ContentHeight <= ViewportHeight) return;
        int trackH = Height - 8;
        int thumbH = Math.Max(28, (int)((double)ViewportHeight / ContentHeight * trackH));
        _travel = trackH - thumbH;
        double ratio = MaxOffset() == 0 ? 0 : (double)Offset / MaxOffset();
        int y = 4 + (int)(ratio * _travel);
        _thumbRect = new Rectangle(1, y, 6, thumbH);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        AntStyle.FillRounded(e.Graphics, AntStyle.Border, _thumbRect, 3);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _dragStartY = e.Y;
        _dragStartOffset = Offset;
        var hit = new Rectangle(_thumbRect.Location, _thumbRect.Size);
        hit.Inflate(2, 2);
        if (!hit.Contains(e.Location))
        {
            // 点击轨道：让滑块中心跳到点击处
            if (_travel > 0)
            {
                double ratio = (double)(e.Y - 4 - _thumbRect.Height / 2) / _travel;
                SetOffset((int)(ratio * MaxOffset()));
                _dragStartOffset = Offset;
            }
        }
        _dragging = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _travel <= 0) return;
        double ratio = (double)(e.Y - _dragStartY) / _travel;
        SetOffset(_dragStartOffset + (int)(ratio * MaxOffset()));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }
}
