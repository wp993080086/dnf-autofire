namespace KeyMouseFactory;

// MainForm 主体：字段与常量、构造函数、窗口生命周期、配置读写、托盘、状态刷新。
// 其余功能拆分在同名 partial 文件：Ui / Hotkey / Keys / Mouse / Record / Debug / Types。
public partial class MainForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_START = 1;  // 按键连发热键
    private const int HOTKEY_ID_MOUSE = 2;  // 鼠标连点热键
    private const int HOTKEY_ID_RECORD = 3; // 录制热键
    private const int HOTKEY_ID_PLAY = 4;   // 回放热键
    private const int WM_SETCURSOR = 0x0020; // 光标形状：禁用控件收不到鼠标消息，靠窗体补「禁止」光标
    private const int HTCLIENT = 1;
    // 取值范围统一放在 Limits：配置加载时套用同一套上下限
    private const int MinMs = Limits.MsMin;
    private const int MaxMs = Limits.MsMax;
    private const int KeyStepMs = Limits.MsStep;          // 按键间隔的步进值（上下按钮/↑↓每次走 50ms）
    private const int MouseStepMs = Limits.MouseMsStep;   // 鼠标连点间隔步进
    private const int MouseMinMs = Limits.MouseMsMin;     // 鼠标连点间隔下限
    private const int MouseMaxMs = Limits.MouseMsMax;     // 鼠标连点间隔上限（10 秒）
    private const int MouseDefaultMs = Limits.MouseMsDefault; // 鼠标连点间隔默认值
    private const int MaxKeyRepeat = Limits.KeyCountMax;  // 自动连按次数上限（0 = 无限）
    private const int MaxMouseRepeat = Limits.MouseCountMax; // 自动点击次数上限（0 = 无限）
    private const int RowH = 34;
    private const int RowWidth = 584;   // 配置行宽度（与鼠标页对齐），按键页内容区同宽
    private const int MaxRows = Limits.KeyRowsMax;  // 最多 5 行（默认 2 + 新增 3）
    private const int RecRowH = 30;        // 录制方案列表行高
    private const int RecListHeight = 120; // 方案列表可视高度（正好 4 行，超出滚动，不留半行）
    // 录制页底部那行：空闲时是操作提示，录制/回放时被状态文案顶替
    private const string RecordHint = "录制时主窗口收进托盘，右下角留状态条。";

    // Tab 索引与标题：页面顺序 = Tab 顺序，加页只改这里与 BuildUi 的构建调用
    private const int TabKeys = 0;
    private const int TabMouse = 1;
    private const int TabRecord = 2;
    private static readonly string[] TabTitles = { "按键", "鼠标", "录制" };

    private readonly List<RowControls> _rows = new();
    private readonly List<MouseRow> _mouseRows = new();
    private readonly AutoFireEngine _engine = new();
    private readonly MouseClickEngine _mouseEngine = new();
    private AppConfig _config = new();
    private bool _exiting;
    private readonly WheelFilter _wheelFilter;

    // 配置落盘防抖：点箭头/连续勾选会连着触发保存，静默 400ms 后只写一次盘。
    // 注意 _config 是立刻更新的，只有「写文件」这一步被推迟。
    private const int SaveDebounceMs = 400;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private AppConfig? _pendingSave;   // 已生效但还没写进 config.json 的配置

    // 自定义 Tab
    private int _tabIndex;
    private int _tabHover = -1;
    private readonly List<(Rectangle Rect, int Index)> _tabRects = new();
    private Panel tabHeader = null!;
    private readonly Panel[] _pages = new Panel[TabTitles.Length];  // 下标与 TabTitles 一致，只显示当前页

    // Tab 下划线平滑位移动画
    private readonly System.Windows.Forms.Timer _tabAnimTimer;
    private Rectangle _underlineCurrent;   // 当前下划线位置
    private Rectangle _animFrom, _animTo;  // 动画起止
    private float _animT = 1f;             // 0~1，>=1 表示无动画
    private readonly int[] _tabWidths = new int[TabTitles.Length];     // 页签命中区宽度（文字宽 + 左右留白），构造时测量
    private readonly int[] _tabTextWidths = new int[TabTitles.Length]; // 纯文字宽度，下划线据此对称收短
    private int _underlineWidth;  // 所有页签共用的下划线宽度（取最短文字宽），保证长度一致

    private readonly Font _tabFont; // Tab 文字：加大加粗，突出页签
    private readonly Font _fontBold;

    private Panel pnlRows = null!;
    private Panel pnlContent = null!;   // 行内容容器，滚动时整体位移
    private FlatScrollBar scrollBar = null!;
    private AntButton btnAdd = null!;

    // 录制页
    private readonly List<RecordProfile> _profiles = new();
    private readonly List<Panel> _recRowPanels = new();
    private readonly List<(RecordProfile Profile, AntCheckBox Chk)> _recSelection = new();
    private Panel pnlRecRows = null!;
    private Panel pnlRecContent = null!;
    private FlatScrollBar recScroll = null!;
    private AntButton btnRecord = null!;
    private AntButton btnPlay = null!;      // 录制页「开始/停止回放」，与开始录制并排
    private Label lblRecStatus = null!;     // 空闲时是操作提示，录制/回放时是状态
    private Label lblRecEmpty = null!;
    private MacroRecorder? _recorder;
    private RecordOverlayForm? _overlay;
    private string _selectedProfileId = "";
    private bool _syncingSelection; // 单选互斥内部改勾选时置位，防止 CheckedChanged 互相触发

    // 回放
    private MacroPlayer? _player;
    private PlayOverlayForm? _playOverlay;   // 回放期间的右下角状态条（只读、鼠标穿透）
    private AntSelect cboPlayMode = null!, cboPlayKey = null!;
    private AntNumber numPlayValue = null!, numPlayGap = null!;
    private Label lblPlay = null!, lblPlayUnit = null!;
    private Label lblGap = null!, lblGapUnit = null!;
    private readonly System.Windows.Forms.Timer _playTimer; // 回放进度 200ms 刷新

    private AntButton btnMain = null!;      // 唯一主按钮：控制当前 Tab 对应的引擎
    private Label lblTarget = null!;        // "当前控制：按键连发/鼠标连点" 标识
    private Label lblStatus = null!;
    private AntSelect cboStartKey = null!, cboMode = null!;
    private Label lblHotkey = null!;   // "连发热键/连点热键" 标签，随 Tab 切换
    private Label lblMode = null!;     // "连发模式" 标签（仅按键页显示）
    private Label lblModeTip = null!;  // 底部提示文字，随 Tab 切换
    private bool _loading = true;      // 构造/加载配置期间不触发自动保存
    private bool _hotkeySyncing;       // Tab 切换同步热键下拉框时不触发保存/重注册
    private bool _keyFixing;           // 键名框内程序改写文本时，避免 TextChanged 递归
    private readonly ToolTip _keyTip = new(); // 键名输入框的填写规则提示（所有行共用）
    private readonly Icon _appIcon; // 应用图标：EXE 内嵌资源，窗口与托盘共用
    private NotifyIcon trayIcon = null!;
    private ContextMenuStrip trayMenu = null!;

    // 整窗背景渐变：顶部 12% 主题色(#E8F2FF) → 一半高度处纯白，下半纯白。
    // 各大容器设为透明，透出这张图；控件本身仍是白底卡片，文字颜色不动。
    private Bitmap? _background;

    public MainForm()
    {
        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                   ?? SystemIcons.Application;
        Icon = _appIcon;

        Font = AntStyle.GetFont(9f);
        Text = "键鼠工厂";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        ClientSize = new Size(640, 400);
        _background = BuildBackgroundGradient(ClientSize);
        BackgroundImage = _background;
        BackgroundImageLayout = ImageLayout.None;

        BuildUi();
        BuildTray();

        // Tab 下划线滑动动画
        _tabAnimTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _tabAnimTimer.Tick += TabAnim_Tick;
        // 回放进度：200ms 刷新「第 N 次 · mm:ss」，同时驱动右下角状态条
        _playTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _playTimer.Tick += PlayTimer_Tick;
        // 配置写盘防抖
        _saveTimer = new System.Windows.Forms.Timer { Interval = SaveDebounceMs };
        _saveTimer.Tick += (_, _) => FlushConfig();
        _tabFont = new Font(AntStyle.FontName, 11f, FontStyle.Bold);
        _fontBold = new Font(Font, FontStyle.Bold);
        for (int i = 0; i < TabTitles.Length; i++)
        {
            _tabTextWidths[i] = TextRenderer.MeasureText(TabTitles[i], _tabFont).Width;
            _tabWidths[i] = _tabTextWidths[i] + 16;
        }
        // 下划线统一宽度：取最短页签的文字宽再左右各收 4px，长度不随文字多少变化
        int minTextWidth = int.MaxValue;
        for (int i = 0; i < TabTitles.Length; i++)
            minTextWidth = Math.Min(minTextWidth, _tabTextWidths[i]);
        _underlineWidth = Math.Max(12, minTextWidth - 8);

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

        // 录制方案：独立文件 recordings.json，读失败回落空库（绝不覆盖原文件）
        _profiles.AddRange(RecordStore.Load());
        // 启动时默认选中第一个方案，省一次点击（回放/删除都以选中项为目标）
        if (_selectedProfileId.Length == 0 && _profiles.Count > 0) _selectedProfileId = _profiles[0].Id;
        RebuildRecordList();

        btnMain.Click += (_, _) => ToggleCurrentEngine();
        btnAdd.Click += (_, _) => AddEmptyRow();

        // 全部是自动连按且次数跑完 → 引擎自行收尾，UI 同步回停止态（回调在工作线程，需切回 UI）
        _engine.AllDone += () =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(OnAutoRepeatFinished)); }
            catch (Exception ex) { Logger.Error("AllDone", ex); }
        };

        // 鼠标侧同构：自动点击行全部跑完 → 引擎自停，界面回到停止态
        _mouseEngine.AllDone += () =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(OnMouseAutoFinished)); }
            catch (Exception ex) { Logger.Error("MouseAllDone", ex); }
        };

        // 注入被系统拦截：窗口此时已收进托盘，气泡是用户唯一能看到的提示，必须弹一次
        _engine.Blocked += () => NotifyInjectBlocked("连发");
        _mouseEngine.Blocked += () => NotifyInjectBlocked("连点");

        Shown += (_, _) =>
        {
            RegisterHotkeys();
            // 启动时不把焦点交给第一个按键输入框：焦点停在 Tab 栏（无光标、无焦点框）
            tabHeader.Focus();
        };
        _wheelFilter = new WheelFilter(this);
        Application.AddMessageFilter(_wheelFilter);
        FormClosed += (_, _) =>
        {
            Application.RemoveMessageFilter(_wheelFilter);
            _tabAnimTimer.Dispose();
            _playTimer.Dispose();
            _fontBold.Dispose();
            _tabFont.Dispose();
            _keyTip.Dispose();
            _engine.Stop();
            _mouseEngine.Stop();
            _player?.Stop();
            ClosePlayOverlay();
            UnregisterHotkeys();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            _appIcon.Dispose();
            BackgroundImage = null;
            _background?.Dispose();
        };
    }

    /// <summary>整窗背景渐变：顶部 12% 主题色到一半高度处纯白，下半纯白。</summary>
    private static Bitmap BuildBackgroundGradient(Size size)
    {
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        using var g = Graphics.FromImage(bmp);
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Point(0, 0), new Point(0, Math.Max(1, size.Height / 2)),
            Color.FromArgb(232, 242, 255), Color.White);
        g.FillRectangle(brush, 0, 0, bmp.Width, Math.Max(1, size.Height / 2));
        g.FillRectangle(Brushes.White, 0, size.Height / 2, bmp.Width, bmp.Height - size.Height / 2);
        return bmp;
    }

    /// <summary>主按钮开关当前 Tab 对应的引擎（按钮点击与热键共用此入口）。</summary>
    private void ToggleCurrentEngine()
    {
        switch (_tabIndex)
        {
            case TabKeys:
                if (_engine.Running) DoStop(); else DoStart();
                break;
            case TabMouse:
                if (_mouseEngine.Running) DoMouseStop(); else DoMouseStart();
                break;
            case TabRecord:
                if (_player != null) StopPlayback(); else StartPlayback();
                break;
        }
    }

    /// <summary>状态栏：两个引擎各自的运行状态，独立开关一目了然；被系统拦截时优先告警。</summary>
    private void RefreshStatus()
    {
        if (_engine.InjectBlocked || _mouseEngine.InjectBlocked)
        {
            string which = _engine.InjectBlocked && _mouseEngine.InjectBlocked ? "连发/连点"
                : _engine.InjectBlocked ? "连发" : "连点";
            lblStatus.ForeColor = AntStyle.Danger;
            UpdateStatus($"警告：{which}注入被系统拦截，请以管理员运行");
            return;
        }
        lblStatus.ForeColor = AntStyle.TextTertiary;
        string keys = _engine.Running ? $"按键连发 运行中（{(_engine.Mode == "A" ? "保留原生" : "拦截原生")}）" : "按键连发 未开启";
        string mouse = _mouseEngine.Running ? "鼠标连点 运行中" : "鼠标连点 未开启";
        UpdateStatus($"{keys} · {mouse}");
    }

    /// <summary>注入被拦截的工作线程回调：切回 UI 弹一次托盘气泡并刷新状态栏/托盘文字。</summary>
    private void NotifyInjectBlocked(string what)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                RefreshStatus();
                UpdateTrayText();
                trayIcon.ShowBalloonTip(3600, $"键鼠工厂 · {what}被系统拦截",
                    "按键/点击没有真正发出去。请以管理员身份运行本程序；若游戏以更高权限启动则无法注入。",
                    ToolTipIcon.Warning);
            }));
        }
        catch (Exception ex) { Logger.Error("InjectBlocked", ex); }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 用户点 X 已在 WndProc 的 SC_CLOSE 分支里拦下并问过一遍；
        // 走到这里的是程序自身关窗（退出流程）或系统关机，直接放行。
        FlushConfig();   // 兜底：防抖窗口内退出也不会丢掉最后的改动
        base.OnFormClosing(e);
    }

    /// <summary>用户点了标题栏 X / 系统菜单关闭 / Alt+F4：问一句再决定关还是收进托盘。</summary>
    private void HandleUserCloseRequest()
    {
        switch (AskCloseAction())
        {
            case ClosePromptForm.Choice.Exit:
                ExitApp();
                break;
            case ClosePromptForm.Choice.Minimize:
                HideToTray();
                break;
        }
    }

    /// <summary>弹出关闭询问框，返回用户选择（None=取消，保持窗口）。</summary>
    private ClosePromptForm.Choice AskCloseAction()
    {
        bool busy = _engine.Running || _mouseEngine.Running || _recorder != null || _player != null;
        using var dlg = new ClosePromptForm(busy);
        dlg.ShowDialog(this);
        return dlg.Result;
    }

    /// <summary>最小化到托盘：窗口隐藏、程序继续跑（连发/连点/录制回放都不中断）。</summary>
    private void HideToTray()
    {
        Hide();
        if (_engine.Running || _mouseEngine.Running || _recorder != null || _player != null)
            trayIcon.ShowBalloonTip(1600, "键鼠工厂",
                "仍在后台运行，双击托盘图标可重新打开。", ToolTipIcon.Info);
    }

    private void ExitApp()
    {
        // 录制中退出：先把已采到的轨迹落盘，再关窗；回放中退出：先停并释放按键
        if (_recorder != null) StopRecording();
        if (_player != null) StopPlayback();
        _exiting = true;
        Close();
    }

    protected override void WndProc(ref Message m)
    {
        // 第二份实例启动时广播这条消息：不管窗口是隐藏在托盘还是被别的窗口盖住，都叫到前台
        if (SingleInstanceSignal.Message != 0 && m.Msg == SingleInstanceSignal.Message)
        {
            ShowWindow();
            m.Result = (IntPtr)1;
            return;
        }

        // 点标题栏 X（以及系统菜单关闭、Alt+F4）都是 WM_SYSCOMMAND + SC_CLOSE：
        // 在这里拦下先问「最小化还是退出」，不交给默认处理，所以窗口不会直接关掉。
        if (m.Msg == NativeMethods.WM_SYSCOMMAND
            && (m.WParam.ToInt64() & 0xFFF0) == NativeMethods.SC_CLOSE
            && !_exiting)
        {
            HandleUserCloseRequest();
            return;
        }

        // 禁用控件收不到鼠标消息，系统把光标交回窗体：这里补「禁止」光标，
        // 否则鼠标停在灰掉的按钮上还是普通箭头，看不出不能点（各容器 AntPanel 里也各看一眼）
        if (m.Msg == WM_SETCURSOR && ((long)m.LParam & 0xFFFF) == HTCLIENT && AntPanel.HasDisabledChildAt(this))
        {
            Cursor.Current = Cursors.No;
            m.Result = (IntPtr)1;
            return;
        }

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
                case HOTKEY_ID_RECORD:
                    ToggleRecording();
                    break;
                case HOTKEY_ID_PLAY:
                    TogglePlayback();
                    break;
            }
        }
    }

    // ---------- 配置 ----------

    private void SaveConfig()
    {
        // 热键下拉框跟随 Tab：当前 Tab 编辑对应引擎的热键，另一侧沿用已存值
        // （录制页不回写热键，两个引擎的热键都沿用已存值）
        var cfg = new AppConfig
        {
            StartHotkey = _tabIndex == TabKeys ? cboStartKey.Text : _config.StartHotkey,
            MouseStartHotkey = _tabIndex == TabMouse ? cboStartKey.Text : _config.MouseStartHotkey,
            RecordHotkey = _tabIndex == TabRecord ? cboStartKey.Text : _config.RecordHotkey,
            PlaybackHotkey = _tabIndex == TabRecord ? cboPlayKey.Text : _config.PlaybackHotkey,
            PlayMode = PlayModeKey(),
            PlayValue = Math.Max(1, numPlayValue.Value),
            PlayGapSec = Math.Clamp(numPlayGap.Value, 0, MacroPlayer.MaxGapSec),
            Mode = cboMode.SelectedIndex == 0 ? "A" : "B",
            Keys = _rows.Select(r => new KeyConfig
            {
                Key = r.TxtKey.Text.Trim().ToUpper(),
                IntervalMs = NormalizeMs(r.TxtMs.Text),
                StopMode = r.SelStop.SelectedIndex == 1 ? "auto" : "hold",
                StopValue = NormalizeKeyRepeat(r.NumCount.Text),
                Enabled = r.Chk.Checked
            }).ToList(),
            MouseClicks = _mouseRows.Select(r => new MouseClickConfig
            {
                Button = r.Button,
                IntervalMs = NormalizeMouseMs(r.NumMs.Text),
                StopMode = r.SelStop.SelectedIndex == 1 ? "auto" : "infinite",
                StopValue = NormalizeMouseRepeat(r.NumValue.Text),
                Enabled = r.Chk.Checked
            }).ToList()
        };

        // 内存里立刻生效：热键校验、引擎启动参数都读 _config，不能等写盘；
        // 写文件推迟到静默 400ms 后，连续调值时只落一次盘
        _config = cfg;
        _pendingSave = cfg;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>把挂起的配置真正写进 config.json（防抖到期、窗体关闭时调用）。</summary>
    private void FlushConfig()
    {
        _saveTimer.Stop();
        var cfg = _pendingSave;
        if (cfg == null) return;
        try
        {
            ConfigManager.Save(cfg);
            _pendingSave = null;
            UpdateStatus("配置已保存到 config.json");
        }
        catch (Exception ex)
        {
            // 保留 _pendingSave，下次改动或关闭时再试一次
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
            AddRow(SanitizeKeyText(k.Key), ClampMs(k.IntervalMs), k.Enabled,
                k.StopMode == "auto" ? "auto" : "hold", k.StopValue);
        // 兜底补齐到 2 行，与默认配置语义一致：X 勾选、A 不勾选
        while (_rows.Count < 2)
        {
            bool isFirst = _rows.Count == 0;
            AddRow(isFirst ? "X" : "A", MinMs, isFirst);
        }

        RelayoutRows();
        UpdateRowButtons();
        cboStartKey.Text = _config.StartHotkey; // 初始在按键页 → 显示连发热键
        cboMode.SelectedIndex = _config.Mode == "A" ? 0 : 1;

        // 鼠标连点行：固定左/右/滚轮三行，按 Button 匹配配置
        var mouseCfg = _config.MouseClicks ?? new List<MouseClickConfig>();
        foreach (var r in _mouseRows)
        {
            var cfg = mouseCfg.FirstOrDefault(m => m.Button == r.Button);
            if (cfg == null)
            {
                // 旧配置（v1.0.0 无 MouseClicks、v1.1.x 无滚轮行）：按默认"松手停止"初始化，不能跳过
                ApplyMouseStopMode(r, "infinite", 0);
                continue;
            }
            r.Chk.Checked = cfg.Enabled;
            r.NumMs.Text = Math.Clamp(cfg.IntervalMs, MouseMinMs, MouseMaxMs).ToString();
            // v1.1.x 的 count（按住点 N 次）并入"自动点击"
            string mode = cfg.StopMode is "auto" or "count" ? "auto" : "infinite";
            r.SelStop.SelectedIndex = mode == "auto" ? 1 : 0; // 先同步下拉框
            ApplyMouseStopMode(r, mode, cfg.StopValue);       // 再以配置值收口（最终生效）
        }

        // 回放设置：模式 + 次数/分钟
        string playMode = _config.PlayMode is "count" or "duration" ? _config.PlayMode : "infinite";
        cboPlayMode.SelectedIndex = playMode == "count" ? 0 : playMode == "duration" ? 2 : 1;
        numPlayValue.Max = playMode == "duration" ? Limits.PlayMinuteMax : Limits.PlayCountMax;
        numPlayValue.SetValue(Math.Clamp(_config.PlayValue, 1, numPlayValue.Max));
        numPlayGap.SetValue(Math.Clamp(_config.PlayGapSec, 0, MacroPlayer.MaxGapSec));
        ApplyPlayMode();

        SetKeysUiLocked(_engine.Running);
        SetMouseUiLocked(_mouseEngine.Running);
        RefreshRunCard();
        RefreshScroll();
        // 启动态也要收口一次：热键下拉的取值、启用状态、别的槽位置灰都靠它，
        // 少了这一步得先切一次 Tab 才正常
        SyncHotkeyUI();
    }

    private void BuildTray()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("打开日志", null, (_, _) =>
        {
            string? error = Logger.OpenLog();
            if (error != null)
                MessageBox.Show("打开日志失败：" + error, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }));

        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApp());

        trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "键鼠工厂",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
    }

    private void UpdateStatus(string text) => lblStatus.Text = text;
}
