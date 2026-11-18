namespace KeyMouseFactory;

// 页内嵌套类型：按键行/鼠标行控件组、滚轮过滤器、Tab 头。
public partial class MainForm : Form
{
    // ---------- 配置行 ----------

    private sealed class RowControls
    {
        public Panel RowPanel = null!;
        public AntCheckBox Chk = null!;
        public AntInput TxtKey = null!;
        public AntNumber TxtMs = null!;
        public AntSelect SelStop = null!;   // 松手停止 / 自动连按
        public AntNumber NumCount = null!;  // 自动连按次数（0 = 无限），仅自动模式显示
        public Label LblCount = null!;      // 次数单位「次」，仅自动连按显示
        public AntButton LnkDel = null!;
    }

    /// <summary>鼠标连点配置行（固定左/右/中/X1/X2 五行，无增删）。</summary>
    private sealed class MouseRow
    {
        public string Button = "L"; // L=左键 R=右键 M=中键 X1=侧键1 X2=侧键2
        public Panel RowPanel = null!;
        public AntCheckBox Chk = null!;
        public AntNumber NumMs = null!;
        public AntSelect SelStop = null!;
        public AntNumber NumValue = null!;
        public Label LblCount = null!; // 次数单位「次」，仅自动点击时显示
    }

    /// <summary>把行区域上空的鼠标滚轮统一转到自定义滚动条。</summary>
    private sealed class WheelFilter : IMessageFilter
    {
        private readonly MainForm _f;
        public WheelFilter(MainForm f) => _f = f;

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x20A;
            if (m.Msg != WM_MOUSEWHEEL || !_f.scrollBar.Visible) return false;
            var screenRect = _f.pnlRows.RectangleToScreen(_f.pnlRows.ClientRectangle);
            if (!screenRect.Contains(Cursor.Position)) return false;

            short delta = (short)((long)m.WParam >> 16);
            int step = Math.Sign(delta) * RowH * 2; // 每格滚 2 行
            _f.scrollBar.SetOffset(_f.scrollBar.Offset - step);
            return true; // 吞掉，避免文本框等自行处理
        }
    }

    /// <summary>
    /// 可接收焦点的 Tab 头容器。窗体启动/重新激活时 WinForms 会自动把焦点交给第一个可选控件，
    /// 若不接管就会落到第一个「按下 [ ] 键」输入框上（光标闪烁、像是被选中）。
    /// 让 Tab 头成为那个"第一个可选控件"，它不自绘焦点框，视觉上等于没有焦点。
    /// </summary>
    private sealed class TabHeaderPanel : Panel
    {
        public TabHeaderPanel()
        {
            // hover 时频繁 Invalidate：不开双缓冲会走「擦白底→再绘制」两步，鼠标划过页签就闪
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }
    }

}
