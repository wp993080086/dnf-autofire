namespace DnfAutoFire;

public class MainForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_START = 1;
    private const int MinMs = 100;
    private const int MaxMs = 5000;
    private const int RowH = 34;
    private const int MaxRows = 5;  // 最多 5 行（默认 2 + 新增 3）

    private readonly List<RowControls> _rows = new();
    private readonly AutoFireEngine _engine = new();
    private AppConfig _config = new();
    private bool _exiting;
    private readonly WheelFilter _wheelFilter;

    // 自定义 Tab
    private int _tabIndex;
    private int _tabHover = -1;
    private readonly List<(Rectangle Rect, int Index)> _tabRects = new();
    private Panel tabHeader = null!;
    private Panel keyPage = null!;
    private Panel mousePage = null!;

    // Tab 下划线平滑位移动画
    private readonly System.Windows.Forms.Timer _tabAnimTimer;
    private Rectangle _underlineCurrent;   // 当前下划线位置
    private Rectangle _animFrom, _animTo;  // 动画起止
    private float _animT = 1f;             // 0~1，>=1 表示无动画
    private readonly int[] _tabWidths = new int[2]; // 构造时测量一次，避免每次 Paint 重复测量不一致
    private readonly Font _fontBold;

    private Panel pnlRows = null!;
    private Panel pnlContent = null!;   // 行内容容器，滚动时整体位移
    private FlatScrollBar scrollBar = null!;
    private AntButton btnAdd = null!;
    private AntButton btnMain = null!;
    private Label lblStatus = null!;
    private AntSelect cboStartKey = null!, cboMode = null!;
    private bool _loading = true; // 构造/加载配置期间不触发自动保存
    private NotifyIcon trayIcon = null!;
    private ContextMenuStrip trayMenu = null!;

    public MainForm()
    {
        Font = new Font(AntStyle.FontName, 9f);
        Text = "DNF 连发工具";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AntStyle.BgLayout;
        ClientSize = new Size(600, 400);

        BuildUi();
        BuildTray();

        // Tab 下划线滑动动画
        _tabAnimTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _tabAnimTimer.Tick += TabAnim_Tick;
        _fontBold = new Font(Font, FontStyle.Bold);
        _tabWidths[0] = TextRenderer.MeasureText("按键", Font).Width + 16;
        _tabWidths[1] = TextRenderer.MeasureText("鼠标", Font).Width + 16;

        try
        {
            _config = ConfigManager.Load();
        }
        catch (Exception ex)
        {
            _config = AppConfig.Default();
            Logger.Error("LoadConfig", ex);
        }
        LoadConfigToUi();
        _loading = false; // 之后任何配置变化都自动保存

        btnMain.Click += (_, _) => { if (_engine.Running) DoStop(); else DoStart(); };
        btnAdd.Click += (_, _) => AddEmptyRow();

        Shown += (_, _) => RegisterHotkeys();
        _wheelFilter = new WheelFilter(this);
        Application.AddMessageFilter(_wheelFilter);
        FormClosed += (_, _) =>
        {
            Application.RemoveMessageFilter(_wheelFilter);
            _tabAnimTimer.Dispose();
            _fontBold.Dispose();
            _engine.Stop();
            UnregisterHotkeys();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        };
    }

    /// <summary>调试入口：--screenshot 模拟新增行。</summary>
    internal void DebugAddEmptyRow() => AddEmptyRow();

    /// <summary>
    /// 调试入口：--uitest &lt;prefix&gt;。窗口全程可见，模拟"进入运行态→回到停止态"的按钮状态切换
    /// （与热键关闭完全相同的 UI 更新路径），用 PrintWindow 抓真实画面验证自绘按钮是否即时重绘。
    /// </summary>
    internal void RunUiRepaintTest(string prefix)
    {
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        int step = 0;
        t.Tick += (_, _) =>
        {
            step++;
            switch (step)
            {
                case 1: // 模拟 DoStart 的 UI 更新（不隐藏窗口）
                    SetUiLocked(true);
                    btnMain.Text = "关闭";
                    btnMain.Danger = true;
                    UpdateStatus("运行中 · 测试");
                    break;
                case 2: // 确认运行态已上屏，随后模拟 DoStop 的 UI 更新（窗口可见 = 用户反馈的场景）
                    CaptureWindow(prefix + "_running.png");
                    SetUiLocked(false);
                    btnMain.Text = "开启";
                    btnMain.Danger = false;
                    UpdateStatus("未开启");
                    break;
                case 3: // 确认停止态已上屏
                    CaptureWindow(prefix + "_stopped.png");
                    t.Stop();
                    t.Dispose();
                    Application.Exit();
                    break;
            }
        };
        t.Start();
    }

    private void CaptureWindow(string path)
    {
        using var bmp = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(bmp);
        IntPtr hdc = g.GetHdc();
        try { NativeMethods.PrintWindow(Handle, hdc, NativeMethods.PW_RENDERFULLCONTENT); }
        finally { g.ReleaseHdc(hdc); }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // ---------- 启动 / 停止 ----------

    private void DoStart()
    {
        var targets = new List<(int Vk, int Ms)>();
        foreach (var r in _rows)
        {
            if (!r.Chk.Checked || string.IsNullOrWhiteSpace(r.TxtKey.Text)) continue;
            int vk = KeyCharToVk(r.TxtKey.Text.Trim().ToUpper()[0]);
            if (vk < 0) continue;
            targets.Add((vk, NormalizeMs(r.TxtMs.Text)));
        }

        // 空配置拦截：没有可连发的键时不开引擎，直接引导用户去配置
        if (targets.Count == 0)
        {
            ShowWindow();
            MessageBox.Show("没有可连发的按键：请至少勾选一行并填写键名。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _engine.Mode = cboMode.SelectedIndex == 0 ? "B" : "A";
        try
        {
            _engine.Start(targets);
        }
        catch (Exception ex)
        {
            Logger.Error("Start", ex);
            MessageBox.Show(ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Logger.Info($"开启：目标键 {targets.Count} 个，模式 {_engine.Mode}");

        SetUiLocked(true);
        btnMain.Text = "关闭";
        btnMain.Danger = true;
        UpdateStatus($"运行中 · 目标键 {targets.Count} 个 · 模式 {_engine.Mode} · 按 {_config.StartHotkey} 关闭");

        Hide();
        trayIcon.ShowBalloonTip(1500, "DNF 连发",
            $"已开启（{targets.Count} 个目标键），最小化到托盘。\n按 {_config.StartHotkey} 或托盘菜单可关闭。",
            ToolTipIcon.Info);
    }

    private void DoStop()
    {
        _engine.Stop();
        Logger.Info("关闭");
        SetUiLocked(false);
        btnMain.Text = "开启";
        btnMain.Danger = false;
        UpdateStatus("未开启");
        // 窗口本就可见 → 只刷新状态，不抢焦点；隐藏时用托盘气泡告知
        if (Visible) return;
        trayIcon.ShowBalloonTip(1500, "DNF 连发", "连发已关闭。", ToolTipIcon.Info);
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 运行中点 X → 隐藏到托盘，不退出
        if (!_exiting && _engine.Running)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_HOTKEY)
        {
            if (m.WParam.ToInt32() == HOTKEY_ID_START)
            {
                // 单键开关：按一下开、再按一下关
                if (_engine.Running) DoStop(); else DoStart();
            }
        }
    }

    // ---------- 配置 ----------

    private void SaveConfig()
    {
        var cfg = new AppConfig
        {
            StartHotkey = cboStartKey.Text,
            Mode = cboMode.SelectedIndex == 0 ? "B" : "A",
            Keys = _rows.Select(r => new KeyConfig
            {
                Key = r.TxtKey.Text.Trim().ToUpper(),
                IntervalMs = NormalizeMs(r.TxtMs.Text),
                Enabled = r.Chk.Checked
            }).ToList()
        };
        try
        {
            ConfigManager.Save(cfg);
            _config = cfg;
            UpdateStatus("配置已保存到 config.json");
        }
        catch (Exception ex)
        {
            Logger.Error("SaveConfig", ex);
            MessageBox.Show("保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadConfigToUi()
    {
        foreach (var r in _rows)
            r.RowPanel.Dispose();
        _rows.Clear();
        pnlContent.Controls.Clear();

        var keys = _config.Keys ?? new List<KeyConfig>();
        foreach (var k in keys)
            AddRow(k.Key ?? "", ClampMs(k.IntervalMs), k.Enabled);
        while (_rows.Count < 2)
            AddRow(_rows.Count == 0 ? "X" : "A", MinMs, true);

        RelayoutRows();
        UpdateRowButtons();
        cboStartKey.Text = _config.StartHotkey;
        cboMode.SelectedIndex = _config.Mode == "A" ? 1 : 0;
        SetUiLocked(_engine.Running);
        RefreshScroll();
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        var handle = Handle; // 强制创建句柄

        bool ok = NativeMethods.RegisterHotKey(handle, HOTKEY_ID_START, 0, (uint)HotkeyStringToVk(cboStartKey.Text));
        if (!ok)
        {
            Logger.Info($"热键注册失败：{cboStartKey.Text}（可能被其他程序占用）");
            UpdateStatus("警告：热键注册失败（可能被其他程序占用）");
        }
    }

    private void UnregisterHotkeys()
    {
        if (IsHandleCreated)
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_START);
    }

    // ---------- UI 构建 ----------

    private void BuildUi()
    {
        var mainCard = new AntCard { Location = new Point(12, 12), Size = new Size(576, 268) };

        // 自定义 Tab 头（AntD 风格：文字 + 蓝色下划线）
        tabHeader = new Panel { Location = Point.Empty, Size = new Size(576, 44), BackColor = Color.White };
        tabHeader.Paint += TabHeader_Paint;
        tabHeader.MouseClick += TabHeader_Click;
        tabHeader.MouseMove += TabHeader_Move;
        tabHeader.MouseLeave += (_, _) => { if (_tabHover != -1) { _tabHover = -1; tabHeader.Invalidate(); } };

        keyPage = new Panel { Location = new Point(0, 44), Size = new Size(576, 216), BackColor = Color.White };
        mousePage = new Panel { Location = new Point(0, 44), Size = new Size(576, 216), BackColor = Color.White, Visible = false };
        var lblPlaceholder = new Label
        {
            Text = "鼠标连点 · 预留开发中",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AntStyle.TextTertiary
        };
        mousePage.Controls.Add(lblPlaceholder);

        BuildKeysPage(keyPage);

        mainCard.Controls.Add(tabHeader);
        mainCard.Controls.Add(keyPage);
        mainCard.Controls.Add(mousePage);
        Controls.Add(mainCard);

        // 启动卡片（左下）
        var cardRun = new AntCard { Location = new Point(12, 288), Size = new Size(272, 100) };
        btnMain = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "开启",
            Location = new Point(14, 12),
            Size = new Size(244, 44),
            Font = new Font(AntStyle.FontName, 10f)
        };
        lblStatus = new Label
        {
            Text = "未开启",
            Location = new Point(14, 64),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary
        };
        cardRun.Controls.Add(btnMain);
        cardRun.Controls.Add(lblStatus);
        Controls.Add(cardRun);

        // 热键与设置卡片（右下）：下拉框高度自适应（约 27px），行距 32 → 间隙 5px，两行垂直居中
        var cardHot = new AntCard { Location = new Point(292, 288), Size = new Size(296, 100) };
        var lblS = new Label { Text = "启动热键", Location = new Point(14, 24), AutoSize = true, ForeColor = AntStyle.TextSec };
        var lblM = new Label { Text = "连发模式", Location = new Point(14, 56), AutoSize = true, ForeColor = AntStyle.TextSec };

        cboStartKey = MakeHotkeySelect(new Point(86, 18));
        cboMode = new AntSelect { Location = new Point(86, 50), Width = 120 };
        cboMode.Inner.DropDownWidth = 120;
        cboMode.Inner.Items.Add("B 拦截原生");
        cboMode.Inner.Items.Add("A 保留原生叠加");
        cboMode.SelectedIndex = 0;

        // 自动保存：热键变化需重新注册，模式/其余配置只写文件
        cboStartKey.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            SaveConfig();
            RegisterHotkeys();
        };
        cboMode.SelectedIndexChanged += (_, _) => { if (_loading) return; SaveConfig(); };

        // B 模式全局拦截提示
        var lblModeTip = new Label
        {
            Text = "B 模式将全局拦截目标键，游戏外同样无法输入",
            Location = new Point(14, 79),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f)
        };

        cardHot.Controls.AddRange(new Control[] { lblS, lblM, cboStartKey, cboMode, lblModeTip });
        Controls.Add(cardHot);

        // 点击非输入区域时移除输入框焦点样式（面板类控件点击不会自动夺走焦点）
        AttachClearFocus(Controls);
    }

    private void AttachClearFocus(Control.ControlCollection controls)
    {
        foreach (Control c in controls)
        {
            // Ant 系控件（输入框/数字框/选择器）内部一律不拦截：
            // ComboBox 的下拉在 MouseDown 时弹出，此时 ActiveControl=null 会立刻把下拉关掉；
            // 点击输入框自身也不应清除它自己的焦点
            if (c is AntInput or AntNumber or AntSelect) continue;
            c.MouseDown += (_, _) => ActiveControl = null;
            AttachClearFocus(c.Controls);
        }
    }

    private void TabHeader_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var line = new Pen(AntStyle.Split))
            g.DrawLine(line, 0, 43, tabHeader.Width, 43);

        _tabRects.Clear();
        int x = 24;
        for (int i = 0; i < 2; i++)
        {
            string text = i == 0 ? "按键" : "鼠标";
            int w = _tabWidths[i];
            bool active = i == _tabIndex;
            var color = active ? AntStyle.Primary
                : i == _tabHover ? AntStyle.TextPri
                : AntStyle.TextSec;
            TextRenderer.DrawText(g, text, active ? _fontBold : Font, new Point(x, 12), color);
            _tabRects.Add((new Rectangle(x, 0, w, 44), i));
            x += w + 24;
        }

        // 下划线：无动画时落在当前 Tab（含首次初始化），动画中由 Tick 更新位置
        if (_animT >= 1f)
        {
            _underlineCurrent = UnderlineOf(_tabIndex);
        }
        if (!_underlineCurrent.IsEmpty)
        {
            using var underline = new SolidBrush(AntStyle.Primary);
            g.FillRectangle(underline, _underlineCurrent);
        }
    }

    private Rectangle UnderlineOf(int index) =>
        index >= 0 && index < _tabRects.Count
            ? new Rectangle(_tabRects[index].Rect.X, 41, _tabRects[index].Rect.Width, 3)
            : Rectangle.Empty;

    private void TabHeader_Click(object? sender, MouseEventArgs e)
    {
        int idx = HitTab(e.Location);
        if (idx < 0 || idx == _tabIndex) return;

        // 平滑位移动画：从当前位置滑到目标 Tab
        var from = _underlineCurrent.IsEmpty ? UnderlineOf(_tabIndex) : _underlineCurrent;
        _animFrom = from;
        _animTo = UnderlineOf(idx);
        if (_animFrom.IsEmpty || _animTo.IsEmpty) return;
        _tabIndex = idx;
        _animT = 0f;
        _underlineCurrent = from;
        _tabAnimTimer.Start();

        keyPage.Visible = idx == 0;
        mousePage.Visible = idx == 1;
        tabHeader.Invalidate();
    }

    private void TabAnim_Tick(object? sender, EventArgs e)
    {
        _animT = Math.Min(1f, _animT + 0.15f);
        float t = _animT * _animT * (3 - 2 * _animT); // smoothstep 缓动
        _underlineCurrent = Rectangle.Round(new Rectangle(
            Lerp(_animFrom.X, _animTo.X, t),
            Lerp(_animFrom.Y, _animTo.Y, t),
            Lerp(_animFrom.Width, _animTo.Width, t),
            Lerp(_animFrom.Height, _animTo.Height, t)));
        if (_animT >= 1f)
        {
            _underlineCurrent = _animTo;
            _tabAnimTimer.Stop();
        }
        tabHeader.Invalidate();
    }

    private static int Lerp(int a, int b, float t) => a + (int)Math.Round((b - a) * t);

    private void TabHeader_Move(object? sender, MouseEventArgs e)
    {
        int idx = HitTab(e.Location);
        tabHeader.Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
        if (idx != _tabHover)
        {
            _tabHover = idx;
            tabHeader.Invalidate();
        }
    }

    private int HitTab(Point p)
    {
        foreach (var (rect, index) in _tabRects)
            if (rect.Contains(p)) return index;
        return -1;
    }

    private AntSelect MakeHotkeySelect(Point loc)
    {
        var sel = new AntSelect { Location = loc, Width = 120 };
        sel.Inner.DropDownWidth = 120; // 弹出列表与框同宽
        for (int i = 1; i <= 12; i++) sel.Inner.Items.Add("F" + i);
        sel.SelectedIndex = 5; // F6
        return sel;
    }

    private void BuildKeysPage(Control page)
    {
        var lblTitle = new Label
        {
            Text = "按键配置",
            Location = new Point(16, 12),
            AutoSize = true,
            ForeColor = AntStyle.TextPri,
            Font = new Font(AntStyle.FontName, 9.5f)
        };
        page.Controls.Add(lblTitle);

        pnlRows = new Panel
        {
            Location = new Point(16, 40),
            Size = new Size(528, 168),
            BackColor = Color.White
        };
        scrollBar = new FlatScrollBar
        {
            Location = new Point(pnlRows.Width - 8, 0),
            Size = new Size(8, pnlRows.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        scrollBar.OffsetChanged += off => pnlContent.Top = -off;

        pnlContent = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(516, RowH),
            BackColor = Color.White
        };

        pnlRows.Controls.Add(pnlContent);
        pnlRows.Controls.Add(scrollBar);
        page.Controls.Add(pnlRows);

        btnAdd = new AntButton
        {
            Kind = AntButton.AntKind.Text,
            Text = "＋ 新增一行",
            Size = new Size(92, 28)
        };
    }

    private void BuildTray()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApp());

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DNF 连发",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
    }

    // ---------- 配置行 ----------

    private sealed class RowControls
    {
        public Panel RowPanel = null!;
        public AntCheckBox Chk = null!;
        public AntInput TxtKey = null!;
        public AntNumber TxtMs = null!;
        public AntButton LnkDel = null!;
    }

    private void AddRow(string key, int ms, bool enabled)
    {
        var row = new RowControls();
        var panel = new Panel { Size = new Size(516, RowH), BackColor = Color.White };
        row.RowPanel = panel;

        row.Chk = new AntCheckBox
        {
            Text = "启用",
            Checked = enabled,
            Location = new Point(0, 2),
            Size = new Size(56, 28)
        };
        var lblPress = new Label { Text = "按下", Location = new Point(58, 9), AutoSize = true, ForeColor = AntStyle.TextSec };
        row.TxtKey = new AntInput
        {
            Location = new Point(94, 3),
            Size = new Size(56, 28)
        };
        row.TxtKey.Inner.MaxLength = 1;
        row.TxtKey.Inner.CharacterCasing = CharacterCasing.Upper;
        row.TxtKey.Inner.TextAlign = HorizontalAlignment.Center;
        row.TxtKey.Text = key;
        var lblGap = new Label { Text = "键，间隔", Location = new Point(156, 9), AutoSize = true, ForeColor = AntStyle.TextSec };
        row.TxtMs = new AntNumber
        {
            Location = new Point(220, 3),
            Size = new Size(84, 28),
            Step = 50,
            Min = MinMs,
            Max = MaxMs
        };
        row.TxtMs.Text = ms.ToString();
        var lblMs = new Label { Text = "毫秒", Location = new Point(310, 9), AutoSize = true, ForeColor = AntStyle.TextSec };
        row.LnkDel = new AntButton
        {
            Kind = AntButton.AntKind.Text,
            Danger = true,
            Text = "删除",
            Location = new Point(352, 3),
            Size = new Size(52, 26),
            Visible = false
        };
        row.LnkDel.Click += (_, _) => DeleteRow(row);

        row.TxtKey.Inner.KeyPress += KeyBox_KeyPress;
        row.TxtKey.Inner.KeyDown += KeyBox_KeyDown;
        row.TxtKey.Inner.TextChanged += KeyBox_TextChanged;
        row.TxtKey.Inner.TextChanged += (_, _) => { if (!_loading) SaveConfig(); };
        row.TxtMs.Inner.KeyPress += MsBox_KeyPress;
        row.TxtMs.Inner.Leave += MsBox_Leave;
        row.TxtMs.ValueChanged += (_, _) => { if (!_loading) SaveConfig(); };
        row.Chk.CheckedChanged += (_, _) => { if (!_loading) SaveConfig(); };

        panel.Controls.AddRange(new Control[]
        {
            row.Chk, lblPress, row.TxtKey, lblGap, row.TxtMs, lblMs, row.LnkDel
        });

        _rows.Add(row);
    }

    /// <summary>按顺序重排所有行 + 尾部"＋ 新增一行"，行距统一 RowH。</summary>
    private void RelayoutRows()
    {
        pnlContent.SuspendLayout();
        int y = 0;
        foreach (var r in _rows)
        {
            r.RowPanel.Location = new Point(0, y);
            if (r.RowPanel.Parent != pnlContent)
                pnlContent.Controls.Add(r.RowPanel);
            y += RowH;
        }
        btnAdd.Location = new Point(0, y + 3); // 与配置行同间距
        if (btnAdd.Parent != pnlContent)
            pnlContent.Controls.Add(btnAdd);
        pnlContent.Height = y + RowH;
        pnlContent.ResumeLayout();
    }

    private void AddEmptyRow()
    {
        if (_rows.Count >= MaxRows) return; // 已达上限（按钮此时应已置灰，双保险）
        try
        {
            AddRow("", MinMs, false);
            RelayoutRows();
            UpdateRowButtons();
            SetUiLocked(_engine.Running);
            RefreshScroll();
            scrollBar.SetOffset(scrollBar.MaxOffset());
            _rows[^1].TxtKey.Inner.Focus();
        }
        catch (Exception ex)
        {
            Logger.Error("AddEmptyRow", ex);
            throw;
        }
    }

    private void DeleteRow(RowControls row)
    {
        int idx = _rows.IndexOf(row);
        if (idx < 0) return;
        _rows.RemoveAt(idx);
        pnlContent.Controls.Remove(row.RowPanel);
        row.RowPanel.Dispose();
        RelayoutRows();
        UpdateRowButtons();
        SetUiLocked(_engine.Running);
        RefreshScroll();
        UpdateStatus("已删除一行");
    }

    /// <summary>第 3 行起显示删除按钮（前 2 行固定保留）；达到行数上限时"新增"置灰。</summary>
    private void UpdateRowButtons()
    {
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].LnkDel.Visible = i >= 2;
        btnAdd.Enabled = _rows.Count < MaxRows;
    }

    private void RefreshScroll()
    {
        scrollBar.SetMetrics((_rows.Count + 1) * RowH, pnlRows.Height);
        pnlContent.Top = -scrollBar.Offset;
    }

    // ---------- 输入校验 ----------

    private void KeyBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        char c = e.KeyChar;
        if (char.IsControl(c)) return; // 退格等
        if (c >= 'a' && c <= 'z') c = char.ToUpper(c);
        if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            e.KeyChar = c;
            return;
        }
        e.Handled = true; // 仅允许 A-Z、0-9
    }

    private void KeyBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && sender is TextBox txt)
        {
            txt.Clear(); // Delete 清空该行键名
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void KeyBox_TextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextBox txt || string.IsNullOrEmpty(txt.Text)) return;
        string value = txt.Text.Trim().ToUpper();
        if (value.Length == 0) return;

        // 与其他行键名重复 → 直接置空本行
        foreach (var r in _rows)
        {
            if (ReferenceEquals(r.TxtKey.Inner, txt)) continue;
            if (r.TxtKey.Text.Trim().ToUpper() == value)
            {
                txt.Clear();
                UpdateStatus("键名 " + value + " 与其他行重复，已置空");
                return;
            }
        }
    }

    private void MsBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        // 仅正整数，禁止负号、小数点
        if (char.IsControl(e.KeyChar)) return;
        if (!char.IsDigit(e.KeyChar)) e.Handled = true;
    }

    private void MsBox_Leave(object? sender, EventArgs e)
    {
        if (sender is not TextBox txt) return;
        int fixedMs = NormalizeMs(txt.Text);
        if (txt.Text != fixedMs.ToString())
        {
            txt.Text = fixedMs.ToString();
            UpdateStatus("间隔已修正为 " + fixedMs + " 毫秒");
        }
        if (!_loading) SaveConfig(); // 手动输入的毫秒值离开输入框时落盘
    }

    private static int NormalizeMs(string text)
    {
        if (!int.TryParse(text?.Trim(), out int v) || v <= 0) return MinMs;
        return ClampMs(((v + 25) / 50) * 50); // 就近取整到 50 的倍数（步长 50）
    }

    private static int ClampMs(int v) => Math.Clamp(v, MinMs, MaxMs);

    private static int KeyCharToVk(char c) =>
        c is >= 'A' and <= 'Z' or >= '0' and <= '9' ? c : -1;

    private static int HotkeyStringToVk(string s)
    {
        s = s.Trim().ToUpper();
        if (s.Length >= 2 && s[0] == 'F' && int.TryParse(s[1..], out int n) && n is >= 1 and <= 12)
            return 0x70 + n - 1; // F1=0x70
        if (s.Length == 1 && char.IsLetterOrDigit(s[0])) return s[0];
        return 0x75; // 兜底 F6
    }

    // ---------- 辅助 ----------

    private void SetUiLocked(bool locked)
    {
        foreach (var r in _rows)
        {
            r.Chk.Enabled = !locked;
            r.TxtKey.Enabled = !locked;
            r.TxtMs.Enabled = !locked;
            r.LnkDel.Enabled = !locked;
        }
        btnAdd.Enabled = !locked && _rows.Count < MaxRows;
        cboStartKey.Enabled = !locked;
        cboMode.Enabled = !locked;
    }

    private void UpdateStatus(string text) => lblStatus.Text = text;

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
}
