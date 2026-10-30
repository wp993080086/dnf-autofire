namespace DnfAutoFire;

public class MainForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_START = 1;  // 按键连发热键
    private const int HOTKEY_ID_MOUSE = 2;  // 鼠标连点热键
    private const int MinMs = 50;
    private const int MaxMs = 5000;
    private const int MouseMinMs = 100;      // 鼠标连点间隔下限
    private const int MouseMaxMs = 10000;    // 鼠标连点间隔上限（10 秒）
    private const int MouseDefaultMs = 300;  // 鼠标连点间隔默认值
    private const int MaxClicks = 9999;   // 鼠标连点：次数上限
    private const int RowH = 34;
    private const int MaxRows = 5;  // 最多 5 行（默认 2 + 新增 3）

    private readonly List<RowControls> _rows = new();
    private readonly List<MouseRow> _mouseRows = new();
    private readonly AutoFireEngine _engine = new();
    private readonly MouseClickEngine _mouseEngine = new();
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
    private readonly Font _tabFont; // Tab 文字：加大加粗，突出两个页签
    private readonly Font _fontBold;

    private Panel pnlRows = null!;
    private Panel pnlContent = null!;   // 行内容容器，滚动时整体位移
    private FlatScrollBar scrollBar = null!;
    private AntButton btnAdd = null!;
    private AntButton btnMain = null!;      // 唯一主按钮：控制当前 Tab 对应的引擎
    private Label lblTarget = null!;        // "当前控制：按键连发/鼠标连点" 标识
    private Label lblStatus = null!;
    private AntSelect cboStartKey = null!, cboMode = null!;
    private Label lblHotkey = null!;   // "连发热键/连点热键" 标签，随 Tab 切换
    private Label lblMode = null!;     // "连发模式" 标签（仅按键页显示）
    private Label lblModeTip = null!;  // 底部提示文字，随 Tab 切换
    private bool _loading = true;      // 构造/加载配置期间不触发自动保存
    private bool _hotkeySyncing;       // Tab 切换同步热键下拉框时不触发保存/重注册
    private readonly Icon _appIcon; // 应用图标：EXE 内嵌资源，窗口与托盘共用
    private NotifyIcon trayIcon = null!;
    private ContextMenuStrip trayMenu = null!;

    public MainForm()
    {
        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                   ?? SystemIcons.Application;
        Icon = _appIcon;

        Font = new Font(AntStyle.FontName, 9f);
        Text = "DNF 连发工具";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White; // 无留白卡片：白色底 + 深色边框划分区域
        ClientSize = new Size(600, 400);

        BuildUi();
        BuildTray();

        // Tab 下划线滑动动画
        _tabAnimTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _tabAnimTimer.Tick += TabAnim_Tick;
        _tabFont = new Font(AntStyle.FontName, 11f, FontStyle.Bold);
        _fontBold = new Font(Font, FontStyle.Bold);
        _tabWidths[0] = TextRenderer.MeasureText("按键", _tabFont).Width + 16;
        _tabWidths[1] = TextRenderer.MeasureText("鼠标", _tabFont).Width + 16;

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

        btnMain.Click += (_, _) => ToggleCurrentEngine();
        btnAdd.Click += (_, _) => AddEmptyRow();

        Shown += (_, _) => RegisterHotkeys();
        _wheelFilter = new WheelFilter(this);
        Application.AddMessageFilter(_wheelFilter);
        FormClosed += (_, _) =>
        {
            Application.RemoveMessageFilter(_wheelFilter);
            _tabAnimTimer.Dispose();
            _fontBold.Dispose();
            _tabFont.Dispose();
            _engine.Stop();
            _mouseEngine.Stop();
            UnregisterHotkeys();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            _appIcon.Dispose();
        };
    }

    /// <summary>调试入口：--screenshot 模拟新增行。</summary>
    internal void DebugAddEmptyRow() => AddEmptyRow();

    /// <summary>调试入口：--screenshot mouse 切到鼠标 Tab 再截图。</summary>
    internal void DebugShowMouseTab()
    {
        _tabIndex = 1;
        keyPage.Visible = false;
        mousePage.Visible = true;
        tabHeader.Invalidate();
        RefreshRunCard(); // 与 TabHeader_Click 保持一致，主按钮跟随 Tab
        SyncHotkeyUI();   // 热键下拉框/标签同步跟随 Tab
    }

    /// <summary>调试入口：--enginetest &lt;A|B&gt; &lt;秒&gt;。窗口显示后自动开启连发引擎（默认配置行），数秒后自动关闭退出。</summary>
    internal void DebugEngineTest(string mode, int seconds)
    {
        cboMode.SelectedIndex = mode == "A" ? 1 : 0;
        var t = new System.Windows.Forms.Timer { Interval = 800 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            DoStart();
            var stop = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
            stop.Tick += (_, _) => { stop.Stop(); DoStop(); Close(); };
            stop.Start();
        };
        t.Start();
    }

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
                    SetKeysUiLocked(true);
                    btnMain.Text = "关闭连发";
                    btnMain.Danger = true;
                    UpdateStatus("运行中 · 测试");
                    break;
                case 2: // 确认运行态已上屏，随后模拟 DoStop 的 UI 更新（窗口可见 = 用户反馈的场景）
                    CaptureWindow(prefix + "_running.png");
                    SetKeysUiLocked(false);
                    btnMain.Text = "开启连发";
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

        SetKeysUiLocked(true);
        RefreshRunCard();

        Hide();
        trayIcon.ShowBalloonTip(1500, "DNF 连发",
            $"已开启连发（{targets.Count} 个目标键），最小化到托盘。\n按 {_config.StartHotkey} 或托盘菜单可关闭。",
            ToolTipIcon.Info);
    }

    private void DoStop()
    {
        _engine.Stop();
        Logger.Info("关闭连发");
        SetKeysUiLocked(false);
        RefreshRunCard();
        // 窗口本就可见 → 只刷新状态，不抢焦点；隐藏时用托盘气泡告知
        if (Visible) return;
        trayIcon.ShowBalloonTip(1500, "DNF 连发", "连发已关闭。", ToolTipIcon.Info);
    }

    // ---------- 鼠标连点：启动 / 停止 ----------

    private void DoMouseStart()
    {
        var targets = new List<MouseClickEngine.MouseTarget>();
        foreach (var r in _mouseRows)
        {
            if (!r.Chk.Checked) continue;
            targets.Add(new MouseClickEngine.MouseTarget(
                r.Button == "L" ? 1 : 2,
                NormalizeMs(r.NumMs.Text),
                r.SelStop.SelectedIndex == 0 ? MouseClickEngine.StopMode.Count : MouseClickEngine.StopMode.Infinite,
                Math.Max(1, r.NumValue.Value)));
        }

        // 空配置拦截：没有启用的行时不开引擎，直接引导用户去配置
        if (targets.Count == 0)
        {
            ShowWindow();
            MessageBox.Show("没有启用的鼠标连点行：请至少勾选一行。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _mouseEngine.Start(targets);
        }
        catch (Exception ex)
        {
            Logger.Error("MouseStart", ex);
            MessageBox.Show(ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Logger.Info($"开启鼠标连点：{targets.Count} 行");

        SetMouseUiLocked(true);
        RefreshRunCard();

        Hide();
        string desc = string.Join("、", targets.Select(t =>
            $"{(t.Button == 1 ? "左键" : "右键")}({DescribeStopMode(t.Mode, t.StopValue)})"));
        trayIcon.ShowBalloonTip(2000, "鼠标连点",
            $"已开启（{desc}），最小化到托盘。\n按住对应鼠标键即连点，松开即停。",
            ToolTipIcon.Info);
    }

    private void DoMouseStop()
    {
        _mouseEngine.Stop();
        Logger.Info("关闭鼠标连点");
        SetMouseUiLocked(false);
        RefreshRunCard();
        if (Visible) return;
        trayIcon.ShowBalloonTip(1500, "鼠标连点", "鼠标连点已关闭。", ToolTipIcon.Info);
    }

    /// <summary>主按钮开关当前 Tab 对应的引擎（按钮点击与热键共用此入口）。</summary>
    private void ToggleCurrentEngine()
    {
        if (_tabIndex == 0)
        {
            if (_engine.Running) DoStop(); else DoStart();
        }
        else
        {
            if (_mouseEngine.Running) DoMouseStop(); else DoMouseStart();
        }
    }

    /// <summary>刷新主按钮与"当前控制"标识：跟随 Tab，且反映对应引擎的运行状态。</summary>
    private void RefreshRunCard()
    {
        bool mouse = _tabIndex == 1;
        bool running = mouse ? _mouseEngine.Running : _engine.Running;
        lblTarget.Text = mouse ? "当前控制：鼠标连点" : "当前控制：按键连发";
        btnMain.Text = mouse
            ? (running ? "关闭连点" : "开启连点")
            : (running ? "关闭连发" : "开启连发");
        btnMain.Danger = running;
        RefreshStatus(); // 状态栏同时展示两引擎状态
    }

    private static string DescribeStopMode(MouseClickEngine.StopMode mode, int value) => mode switch
    {
        MouseClickEngine.StopMode.Count => $"{value} 次",
        _ => "松手停止"
    };

    /// <summary>状态栏：两个引擎各自的运行状态，独立开关一目了然。</summary>
    private void RefreshStatus()
    {
        string keys = _engine.Running ? $"按键连发 运行中({_engine.Mode})" : "按键连发 未开启";
        string mouse = _mouseEngine.Running ? "鼠标连点 运行中" : "鼠标连点 未开启";
        UpdateStatus($"{keys} · {mouse}");
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 运行中点 X → 隐藏到托盘，不退出（任一引擎在跑都一样）
        if (!_exiting && (_engine.Running || _mouseEngine.Running))
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
            // 每个热键固定对应自己的引擎：连发热键开关连发、连点热键开关连点，与当前 Tab 无关
            switch (m.WParam.ToInt32())
            {
                case HOTKEY_ID_START:
                    if (_engine.Running) DoStop(); else DoStart();
                    break;
                case HOTKEY_ID_MOUSE:
                    if (_mouseEngine.Running) DoMouseStop(); else DoMouseStart();
                    break;
            }
        }
    }

    // ---------- 配置 ----------

    private void SaveConfig()
    {
        // 热键下拉框跟随 Tab：当前 Tab 编辑对应引擎的热键，另一侧沿用已存值
        var cfg = new AppConfig
        {
            StartHotkey = _tabIndex == 0 ? cboStartKey.Text : _config.StartHotkey,
            MouseStartHotkey = _tabIndex == 1 ? cboStartKey.Text : _config.MouseStartHotkey,
            Mode = cboMode.SelectedIndex == 0 ? "B" : "A",
            Keys = _rows.Select(r => new KeyConfig
            {
                Key = r.TxtKey.Text.Trim().ToUpper(),
                IntervalMs = NormalizeMs(r.TxtMs.Text),
                Enabled = r.Chk.Checked
            }).ToList(),
            MouseClicks = _mouseRows.Select(r => new MouseClickConfig
            {
                Button = r.Button,
                IntervalMs = NormalizeMouseMs(r.NumMs.Text),
                StopMode = r.SelStop.SelectedIndex == 0 ? "count" : "infinite",
                StopValue = Math.Max(1, r.NumValue.Value),
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
        // 兜底补齐到 2 行，与默认配置语义一致：X 勾选、A 不勾选
        while (_rows.Count < 2)
        {
            bool isFirst = _rows.Count == 0;
            AddRow(isFirst ? "X" : "A", MinMs, isFirst);
        }

        RelayoutRows();
        UpdateRowButtons();
        cboStartKey.Text = _config.StartHotkey; // 初始在按键页 → 显示连发热键
        cboMode.SelectedIndex = _config.Mode == "A" ? 1 : 0;

        // 鼠标连点行：固定左/右两行，按 Button 匹配配置
        var mouseCfg = _config.MouseClicks ?? new List<MouseClickConfig>();
        foreach (var r in _mouseRows)
        {
            var cfg = mouseCfg.FirstOrDefault(m => m.Button == r.Button);
            if (cfg == null)
            {
                // 旧配置（v1.0.0 无 MouseClicks）或缺失该键条目：按默认"松手停止"初始化，不能跳过
                ApplyMouseStopMode(r, "infinite", 50);
                continue;
            }
            r.Chk.Checked = cfg.Enabled;
            r.NumMs.Text = Math.Clamp(cfg.IntervalMs, MouseMinMs, MouseMaxMs).ToString();
            r.SelStop.SelectedIndex = cfg.StopMode == "count" ? 0 : 1; // 先同步下拉框
            ApplyMouseStopMode(r, cfg.StopMode, cfg.StopValue);        // 再以配置值收口（最终生效）
        }

        SetKeysUiLocked(_engine.Running);
        SetMouseUiLocked(_mouseEngine.Running);
        RefreshRunCard();
        RefreshScroll();
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        var handle = Handle; // 强制创建句柄

        // 连发热键（按键页配置，默认 F6）
        bool okKeys = NativeMethods.RegisterHotKey(handle, HOTKEY_ID_START, 0, (uint)HotkeyStringToVk(_config.StartHotkey));
        // 连点热键（鼠标页配置，默认 F8）
        bool okMouse = NativeMethods.RegisterHotKey(handle, HOTKEY_ID_MOUSE, 0, (uint)HotkeyStringToVk(_config.MouseStartHotkey));

        var failed = new List<string>();
        if (!okKeys) failed.Add(_config.StartHotkey);
        if (!okMouse) failed.Add(_config.MouseStartHotkey);
        if (failed.Count > 0)
        {
            string names = string.Join("、", failed);
            Logger.Info($"热键注册失败：{names}（可能被其他程序占用）");
            UpdateStatus("警告：热键 " + names + " 注册失败（可能被其他程序占用）");
        }
    }

    private void UnregisterHotkeys()
    {
        if (!IsHandleCreated) return;
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_START);
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_MOUSE);
    }

    // ---------- UI 构建 ----------

    private void BuildUi()
    {
        // 主区域（Tab + 配置页）：三个区域（主区/启动区/设置区）以 2px 深色边框分隔，无留白卡片
        var pnlMain = new Panel { Location = new Point(2, 2), Size = new Size(596, 302), BackColor = Color.White };

        // 自定义 Tab 头（AntD 风格：文字 + 蓝色下划线，下边框 1px 样式不变）
        tabHeader = new Panel { Location = Point.Empty, Size = new Size(596, 44), BackColor = Color.White };
        tabHeader.Paint += TabHeader_Paint;
        tabHeader.MouseClick += TabHeader_Click;
        tabHeader.MouseMove += TabHeader_Move;
        tabHeader.MouseLeave += (_, _) => { if (_tabHover != -1) { _tabHover = -1; tabHeader.Invalidate(); } };

        // Tab 栏右侧空白处：小号浅灰的版本号与作者（版本随 csproj 自动更新）
        var lblVersion = new Label
        {
            // ProductVersion 形如 "1.1.0+<commit>"，只取主版本号
            Text = $"v{Application.ProductVersion.Split('+')[0]} · 鹏多多",
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f),
            BackColor = Color.Transparent,
            Enabled = false // 纯展示，不响应鼠标（事件穿透到 tabHeader）
        };
        tabHeader.Controls.Add(lblVersion);
        lblVersion.Location = new Point(tabHeader.Width - lblVersion.Width - 16, (44 - lblVersion.Height) / 2);

        keyPage = new Panel { Location = new Point(0, 44), Size = new Size(596, 258), BackColor = Color.White };
        mousePage = new Panel { Location = new Point(0, 44), Size = new Size(596, 258), BackColor = Color.White, Visible = false };
        BuildMousePage(mousePage);

        BuildKeysPage(keyPage);

        pnlMain.Controls.Add(tabHeader);
        pnlMain.Controls.Add(keyPage);
        pnlMain.Controls.Add(mousePage);
        Controls.Add(pnlMain);

        // 启动区（左下）：单按钮跟随当前 Tab——按键页控制连发、鼠标页控制连点
        var pnlRun = new Panel { Location = new Point(2, 306), Size = new Size(294, 92), BackColor = Color.White };
        btnMain = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "开启连发",
            Location = new Point(14, 6),
            Size = new Size(244, 40),
            Font = new Font(AntStyle.FontName, 10f)
        };
        lblTarget = new Label
        {
            Text = "当前控制：按键连发",
            Location = new Point(14, 50),
            AutoSize = true,
            ForeColor = AntStyle.Primary,
            Font = new Font(AntStyle.FontName, 9f, FontStyle.Bold)
        };
        lblStatus = new Label
        {
            Text = "未开启",
            Location = new Point(14, 70),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary
        };
        pnlRun.Controls.Add(btnMain);
        pnlRun.Controls.Add(lblTarget);
        pnlRun.Controls.Add(lblStatus);
        Controls.Add(pnlRun);

        // 设置区（右下）：热键下拉框跟随当前 Tab——按键页编辑连发热键（默认 F6）、
        // 鼠标页编辑连点热键（默认 F8）；两个热键同时全局生效
        var pnlSet = new Panel { Location = new Point(298, 306), Size = new Size(300, 92), BackColor = Color.White };
        lblHotkey = new Label { Text = "连发热键", Location = new Point(14, 12), AutoSize = true, ForeColor = AntStyle.TextSec };
        lblMode = new Label { Text = "连发模式", Location = new Point(14, 44), AutoSize = true, ForeColor = AntStyle.TextSec };

        cboStartKey = MakeHotkeySelect(new Point(86, 8));
        cboMode = new AntSelect { Location = new Point(86, 40), Width = 120 };
        cboMode.Inner.DropDownWidth = 120;
        cboMode.Inner.Items.Add("B 拦截原生");
        cboMode.Inner.Items.Add("A 保留原生叠加");
        cboMode.SelectedIndex = 0;

        // 自动保存：热键变化需重新注册，模式/其余配置只写文件
        cboStartKey.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _hotkeySyncing) return;
            SaveConfig();
            RegisterHotkeys();
        };
        cboMode.SelectedIndexChanged += (_, _) => { if (_loading) return; SaveConfig(); };

        // B 模式全局拦截提示
        lblModeTip = new Label
        {
            Text = "B 模式全局拦截目标键；间隔范围 50-5000ms，步进 1",
            Location = new Point(14, 70),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f)
        };

        pnlSet.Controls.AddRange(new Control[] { lblHotkey, lblMode, cboStartKey, cboMode, lblModeTip });
        Controls.Add(pnlSet);

        // 点击非输入区域时移除输入框焦点样式（面板类控件点击不会自动夺走焦点）
        AttachClearFocus(Controls);
    }

    /// <summary>区域分隔：仅在模块相邻处画 2px 浅灰线（颜色与 Tab 下边框一致），无外框。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var line = new SolidBrush(AntStyle.Split);
        // 主区与底部两区之间的水平分隔（2px）
        g.FillRectangle(line, 0, 304, Width, 2);
        // 启动区与设置区之间的垂直分隔（2px）
        g.FillRectangle(line, 296, 304, 2, Height - 304);
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
            TextRenderer.DrawText(g, text, _tabFont, new Point(x, 11), color);
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
        RefreshRunCard(); // 主按钮与"当前控制"标识跟随 Tab 切换
        SyncHotkeyUI();   // 热键下拉框跟随 Tab：连发热键 / 连点热键
    }

    /// <summary>设置卡片跟随当前 Tab：按键页=连发热键 + 连发模式 + 键盘提示；鼠标页=连点热键 + 鼠标提示（无模式概念）。</summary>
    private void SyncHotkeyUI()
    {
        bool mouse = _tabIndex == 1;
        lblHotkey.Text = mouse ? "连点热键" : "连发热键";
        _hotkeySyncing = true;
        cboStartKey.Text = mouse ? _config.MouseStartHotkey : _config.StartHotkey;
        _hotkeySyncing = false;
        // 连发模式是按键页专属设置，鼠标页隐藏；提示文字分别描述各自的间隔范围
        lblMode.Visible = !mouse;
        cboMode.Visible = !mouse;
        lblModeTip.Text = mouse
            ? "连点热键全局开关鼠标连点，与连发热键相互独立"
            : "B 模式全局拦截目标键；间隔范围 50-5000ms，步进 1";
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
            Size = new Size(528, 210),
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

    /// <summary>鼠标连点页：固定左/右两行 + 停止条件下拉（点 N 次 / 松手停止，默认松手停止）。</summary>
    private void BuildMousePage(Control page)
    {
        var lblTitle = new Label
        {
            Text = "鼠标连点",
            Location = new Point(16, 12),
            AutoSize = true,
            ForeColor = AntStyle.TextPri,
            Font = new Font(AntStyle.FontName, 9.5f)
        };
        page.Controls.Add(lblTitle);

        foreach (var (button, label, y) in new[] { ("L", "左键", 44), ("R", "右键", 82) })
        {
            var row = new MouseRow { Button = button };
            var panel = new Panel { Location = new Point(16, y), Size = new Size(544, RowH), BackColor = Color.White };
            row.RowPanel = panel;

            row.Chk = new AntCheckBox
            {
                Text = "启用",
                Checked = false,
                Location = new Point(0, 2),
                Size = new Size(56, 28)
            };
            var lblHold = MakeRowText("按住", 58);
            var lblBtn = MakeRowText(label, 92, AntStyle.TextPri, bold: true);
            var lblGap = MakeRowText("键，间隔", 128);
            row.NumMs = new AntNumber
            {
                Location = new Point(188, 3),
                Size = new Size(84, 28),
                Step = 100,
                Min = MouseMinMs,
                Max = MouseMaxMs
            };
            row.NumMs.Text = MouseDefaultMs.ToString();
            var lblMs = MakeRowText("毫秒，", 278);
            row.SelStop = new AntSelect { Location = new Point(322, 2), Width = 110 };
            row.SelStop.Inner.DropDownWidth = 110;
            row.SelStop.Inner.Items.Add("点 N 次");
            row.SelStop.Inner.Items.Add("松手停止");
            row.SelStop.SelectedIndex = 1; // 默认松手停止
            row.NumValue = new AntNumber
            {
                Location = new Point(440, 3),
                Size = new Size(76, 28),
                Step = 10,
                Min = 1,
                Max = MaxClicks
            };
            row.NumValue.Text = "50";
            ApplyMouseStopMode(row, "infinite", 50); // 初始即收口：默认松手停止时隐藏数值框（配置缺失时不会再漏显示）

            row.SelStop.SelectedIndexChanged += (_, _) =>
            {
                // 切换停止条件：调整数值框可见性/范围，并按当前值重新钳制
                string mode = row.SelStop.SelectedIndex == 0 ? "count" : "infinite";
                ApplyMouseStopMode(row, mode, row.NumValue.Value);
                if (!_loading) SaveConfig();
            };
            row.NumMs.Inner.KeyPress += MsBox_KeyPress;
            row.NumMs.Inner.Leave += (_, _) =>
            {
                int fixedMs = NormalizeMouseMs(row.NumMs.Text);
                if (row.NumMs.Text != fixedMs.ToString())
                {
                    row.NumMs.Text = fixedMs.ToString();
                    UpdateStatus("间隔已修正为 " + fixedMs + " 毫秒");
                }
                if (!_loading) SaveConfig();
            };
            row.NumMs.ValueChanged += (_, _) => { if (!_loading) SaveConfig(); };
            row.NumValue.Inner.KeyPress += MsBox_KeyPress;
            row.NumValue.Inner.Leave += (_, _) =>
            {
                if (_loading) return;
                row.NumValue.SetValue(row.NumValue.Value); // 离开输入框时钳制到当前模式范围
                SaveConfig();
            };
            row.NumValue.ValueChanged += (_, _) => { if (!_loading) SaveConfig(); };
            row.Chk.CheckedChanged += (_, _) => { if (!_loading) SaveConfig(); };

            panel.Controls.AddRange(new Control[]
            {
                row.Chk, lblHold, lblBtn, lblGap, row.NumMs, lblMs, row.SelStop, row.NumValue
            });
            page.Controls.Add(panel);
            _mouseRows.Add(row);
        }

        var lblHint = new Label
        {
            Text = "开启后按住对应鼠标键即自动连点，松开即停；间隔范围 100-10000ms，游戏内有效间隔下限 40ms。",
            Location = new Point(16, 130),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f)
        };
        page.Controls.Add(lblHint);
    }

    /// <summary>按停止条件模式调整数值框：count=次数(1-9999)显示可编辑，infinite(松手停止)=隐藏。</summary>
    private void ApplyMouseStopMode(MouseRow r, string mode, int value)
    {
        bool infinite = mode != "count";
        r.NumValue.Visible = !infinite;
        r.NumValue.Enabled = !infinite && !_mouseEngine.Running; // 修复：切回"点 N 次"时必须恢复可编辑
        if (infinite) return;
        r.NumValue.Min = 1;
        r.NumValue.Max = MaxClicks;
        r.NumValue.Step = 10;
        r.NumValue.SetValue(value);
    }

    private void BuildTray()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApp());

        trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
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

    /// <summary>鼠标连点配置行（固定左/右两行，无增删）。</summary>
    private sealed class MouseRow
    {
        public string Button = "L"; // L=左键 R=右键
        public Panel RowPanel = null!;
        public AntCheckBox Chk = null!;
        public AntNumber NumMs = null!;
        public AntSelect SelStop = null!;
        public AntNumber NumValue = null!;
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
        var lblPress = MakeRowText("按下", 58);
        row.TxtKey = new AntInput
        {
            Location = new Point(94, 3),
            Size = new Size(56, 28)
        };
        row.TxtKey.Inner.MaxLength = 1;
        row.TxtKey.Inner.CharacterCasing = CharacterCasing.Upper;
        row.TxtKey.Inner.TextAlign = HorizontalAlignment.Center;
        row.TxtKey.Text = key;
        var lblGap = MakeRowText("键，间隔", 156);
        row.TxtMs = new AntNumber
        {
            Location = new Point(220, 3),
            Size = new Size(84, 28),
            Step = 10,
            Min = MinMs,
            Max = MaxMs
        };
        row.TxtMs.Text = ms.ToString();
        var lblMs = MakeRowText("毫秒", 310);
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
            SetKeysUiLocked(_engine.Running);
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
        SetKeysUiLocked(_engine.Running);
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

    /// <summary>行内静态文字：保持 AutoSize（不裁切），用 MeasureText 把文字垂直中心对齐到 28px 控件带中线。</summary>
    private static Label MakeRowText(string text, int x, Color? color = null, bool bold = false)
    {
        var font = bold ? new Font(AntStyle.FontName, 9f, FontStyle.Bold) : new Font(AntStyle.FontName, 9f);
        int textH = TextRenderer.MeasureText(text, font).Height;
        return new Label
        {
            Text = text,
            Location = new Point(x, 2 + (28 - textH) / 2),
            AutoSize = true,
            ForeColor = color ?? AntStyle.TextSec,
            Font = font
        };
    }

    private static int NormalizeMs(string text)
    {
        if (!int.TryParse(text?.Trim(), out int v) || v <= 0) return MinMs;
        return ClampMs(v); // 允许 1~5000 任意整数（步进按钮仍按 50 步进）
    }

    private static int ClampMs(int v) => Math.Clamp(v, MinMs, MaxMs);

    /// <summary>鼠标连点间隔钳制：100~10000ms，非法输入回落默认 300。</summary>
    private static int NormalizeMouseMs(string text)
    {
        if (!int.TryParse(text?.Trim(), out int v) || v <= 0) return MouseDefaultMs;
        return Math.Clamp(v, MouseMinMs, MouseMaxMs);
    }

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

    /// <summary>按键连发运行中锁定按键页配置；两个引擎互不影响。</summary>
    private void SetKeysUiLocked(bool locked)
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

    /// <summary>鼠标连点运行中锁定鼠标页配置。</summary>
    private void SetMouseUiLocked(bool locked)
    {
        foreach (var r in _mouseRows)
        {
            r.Chk.Enabled = !locked;
            r.NumMs.Enabled = !locked;
            r.SelStop.Enabled = !locked;
            r.NumValue.Enabled = !locked && r.SelStop.SelectedIndex == 0; // 仅"点 N 次"有数值框
        }
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
