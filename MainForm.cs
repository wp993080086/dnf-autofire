namespace DnfAutoFire;

public class MainForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_START = 1;  // 按键连发热键
    private const int HOTKEY_ID_MOUSE = 2;  // 鼠标连点热键
    private const int HOTKEY_ID_RECORD = 3; // 录制热键
    private const int HOTKEY_ID_PLAY = 4;   // 回放热键
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
    private const int MaxRows = 5;  // 最多 5 行（默认 2 + 新增 3）
    private const int RecRowH = 30;        // 录制方案列表行高
    private const int RecListHeight = 120; // 方案列表可视高度（正好 4 行，超出滚动，不留半行）
    // 录制页底部那行：空闲时是操作提示，录制/回放时被状态文案顶替
    private const string RecordHint = "F9 开始/结束录制，F10 开始/停止回放；间隔＝两轮之间的等待，0 为不等待。";

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

    // 页面切换白幕淡入（WinForms 子控件不支持逐像素 alpha，用分层窗口铺一层白幕实现）
    private FadeVeil? _veil;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private int _fadeElapsed;
    private const float VeilStartOpacity = 0.55f;
    private const int FadeMs = 300;
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
    private ToolStripMenuItem trayRecordItem = null!;
    private ToolStripMenuItem trayPlayItem = null!;

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
        ClientSize = new Size(640, 400);

        BuildUi();
        BuildTray();

        // Tab 下划线滑动动画
        _tabAnimTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _tabAnimTimer.Tick += TabAnim_Tick;
        // 页面切换白幕淡入
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _fadeTimer.Tick += Fade_Tick;
        // 回放进度：200ms 刷新「第 N 次 · mm:ss」，同时驱动右下角状态条
        _playTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _playTimer.Tick += PlayTimer_Tick;
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
            _fadeTimer.Dispose();
            _playTimer.Dispose();
            _veil?.Dispose();
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
        };
    }

    /// <summary>调试入口：--screenshot 模拟新增行。</summary>
    internal void DebugAddEmptyRow() => AddEmptyRow();

    /// <summary>调试入口：--screenshot 切页。index 与 TabTitles 下标一致。</summary>
    internal void DebugShowTab(int index)
    {
        if (index < 0 || index >= _pages.Length) return;
        _tabIndex = index;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visible = i == index;
        _underlineCurrent = Rectangle.Empty; // 下次重绘时按新 Tab 重新落位，跳过动画
        _animT = 1f;
        tabHeader.Invalidate();
        RefreshRunCard(); // 与 TabHeader_Click 保持一致，主按钮跟随 Tab
        SyncHotkeyUI();   // 热键下拉框/标签同步跟随 Tab
    }

    /// <summary>调试入口：--screenshot … mouse 切到鼠标 Tab 再截图。</summary>
    internal void DebugShowMouseTab() => DebugShowTab(TabMouse);

    /// <summary>调试入口：--mouselayout 打印鼠标页各控件实际边界，核对行内排布。</summary>
    internal void DebugDumpMouseLayout()
    {
        foreach (var r in _mouseRows)
            foreach (Control c in r.RowPanel.Controls)
                Console.WriteLine($"{r.Button} {c.GetType().Name} '{c.Text}' loc={c.Location} size={c.Size} right={c.Right}");
    }

    /// <summary>调试入口：--screenshot … record 切到录制 Tab 再截图。</summary>
    internal void DebugShowRecordTab() => DebugShowTab(TabRecord);

    /// <summary>
    /// 调试入口：--seltest 模拟「点第二个方案」，等价于用户在列表里切换选中项。
    /// 用来验证单选互斥不会因为两个复选框互相改 Checked 而无限递归。
    /// </summary>
    internal void DebugSelectionTest()
    {
        if (_recSelection.Count < 2)
        {
            Logger.Info($"[seltest] 方案数 {_recSelection.Count}，需要至少 2 个才能测切换");
            return;
        }
        var (target, chk) = _recSelection[1];
        Logger.Info($"[seltest] 点击 {target.Name}（原选中 {SelectedProfile()?.Name ?? "无"}）");
        chk.Checked = true; // 与用户鼠标点击走同一条路径
        Logger.Info($"[seltest] 完成，当前选中 {SelectedProfile()?.Name ?? "无"}");

        // 再点一次已选中项：应当被弹回勾选，不会变成"一个都没选"
        chk.Checked = false;
        Logger.Info($"[seltest] 取消已选中项后：Checked={chk.Checked} 选中={SelectedProfile()?.Name ?? "无"}");

        // 切回第一个：验证反向切换同样不会递归
        var (first, chk0) = _recSelection[0];
        chk0.Checked = true;
        Logger.Info($"[seltest] 切回 {first.Name}：选中={SelectedProfile()?.Name ?? "无"} {target.Name}.Checked={chk.Checked}");
    }

    /// <summary>调试入口：--keytest 跑一遍开启前校验，返回结果（不启动引擎、不弹窗）。</summary>
    internal string DebugStartCheck()
    {
        string? err = CollectTargets(out var targets, out _, out _);
        if (err != null) return "BLOCK: " + err.Replace("\n", " | ");
        return $"OK targets={targets.Count} vks=[{string.Join(", ", targets.Select(t => "0x" + t.Vk.ToString("X2")))}]";
    }

    /// <summary>调试入口：--fadetest 切页并走白幕淡入，供抓帧验证。</summary>
    internal void DebugShowTabFade(int index)
    {
        DebugShowTab(index);
        BeginPageFade();
    }

    /// <summary>调试入口：当前页在屏幕上的矩形（抓帧用，跟随页面尺寸变化）。</summary>
    internal Rectangle DebugPageScreenRect() =>
        new(_pages[_tabIndex].PointToScreen(Point.Empty), _pages[_tabIndex].Size);

    /// <summary>调试入口：--enginetest &lt;A|B&gt; &lt;秒&gt;。窗口显示后自动开启连发引擎（默认配置行），数秒后自动关闭退出。</summary>
    internal void DebugEngineTest(string mode, int seconds)
    {
        cboMode.SelectedIndex = mode == "A" ? 0 : 1;
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

    /// <summary>调试入口：--mousetest &lt;秒&gt; 按当前配置开启鼠标连点，数秒后关闭退出（配 DNF_DRYRUN=1 只跑调度不注入）。</summary>
    internal void DebugMouseTest(int seconds)
    {
        var t = new System.Windows.Forms.Timer { Interval = 800 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            DoMouseStart();
            var stop = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
            stop.Tick += (_, _) =>
            {
                stop.Stop();
                if (_mouseEngine.Running) DoMouseStop();
                Close();
            };
            stop.Start();
        };
        t.Start();
    }

    /// <summary>调试入口：--recordtest &lt;秒&gt; &lt;png&gt; 自动开录、抓悬浮条、停止并退出（验证录制链路与悬浮条绘制）。</summary>
    internal void DebugRecordTest(int seconds, string pngPath)
    {
        var t = new System.Windows.Forms.Timer { Interval = 700 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            StartRecording();
            var cap = new System.Windows.Forms.Timer { Interval = 900 };
            cap.Tick += (_, _) =>
            {
                cap.Stop();
                if (_overlay != null && !_overlay.IsDisposed)
                {
                    var b = _overlay.Bounds;
                    using var bmp = new Bitmap(b.Width, b.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                    Logger.Info($"[recordtest] 悬浮条 {b} → {pngPath}");
                }
                var stop = new System.Windows.Forms.Timer { Interval = Math.Max(500, seconds * 1000) };
                stop.Tick += (_, _) =>
                {
                    stop.Stop();
                    StopRecording();
                    Logger.Info($"[recordtest] 结束：profiles={_profiles.Count} recorder={(_recorder == null ? "null" : "running")}");
                    Close();
                };
                stop.Start();
            };
            cap.Start();
        };
        t.Start();
    }

    /// <summary>
    /// 调试入口：--playtest &lt;方案名&gt; &lt;值&gt; [count|infinite|duration] [最长秒数] [截图路径]。
    /// 按指定模式开启回放，到点/跑完自动关闭退出（配 DNF_DRYRUN=1 只跑时间轴，不注入鼠标）。
    /// </summary>
    internal void DebugPlayTest(string name, int value, string mode, int guardSeconds, string? shotPath = null)
    {
        DebugShowTab(TabRecord);
        var profile = _profiles.FirstOrDefault(p => p.Name == name) ?? _profiles.FirstOrDefault();
        if (profile == null)
        {
            Logger.Info("[playtest] 没有可用方案");
            Close();
            return;
        }

        _selectedProfileId = profile.Id;
        RebuildRecordList();
        cboPlayMode.SelectedIndex = mode switch { "count" => 0, "duration" => 2, _ => 1 };
        numPlayValue.SetValue(Math.Max(1, value));
        var before = MousePosition;
        Logger.Info($"[playtest] 开始：{profile.Name} {profile.Points.Count} 点 模式={mode} 值={value} 光标={before.X},{before.Y}");

        StartPlayback();

        // 等托盘气泡（2400ms）散掉再抓状态条，否则截到的是气泡
        if (shotPath != null)
        {
            var shot = new System.Windows.Forms.Timer { Interval = 3000 };
            shot.Tick += (_, _) =>
            {
                shot.Stop();
                shot.Dispose();
                var ov = _playOverlay;
                if (ov == null || ov.IsDisposed)
                {
                    Logger.Info("[playtest] 状态条不存在");
                    return;
                }
                var b = ov.Bounds;
                using (var bmp = new Bitmap(b.Width, b.Height))
                {
                    // 不用 CopyFromScreen：测试进程常常拿不到桌面 DC，直接让窗口自己画
                    ov.DrawToBitmap(bmp, new Rectangle(0, 0, b.Width, b.Height));
                    bmp.Save(shotPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                Logger.Info($"[playtest] 状态条 {b} 第 {_player?.CurrentLoop ?? 0} 次 → {shotPath}");
            };
            shot.Start();
        }

        // 兜底：到点无论是否跑完都停，避免测试挂住
        var guard = new System.Windows.Forms.Timer { Interval = Math.Max(3, guardSeconds) * 1000 };
        guard.Tick += (_, _) =>
        {
            guard.Stop();
            if (_player != null) StopPlayback();
            var after = MousePosition;
            Logger.Info($"[playtest] 结束：profiles={_profiles.Count} player={(_player == null ? "null" : "running")} 光标={after.X},{after.Y}");
            Close();
        };
        guard.Start();
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
        if (RefuseWhileRecording()) return;

        string? error = CollectTargets(out var targets, out var badRow, out string status);
        if (error != null)
        {
            ShowWindow();
            UpdateStatus(status);
            MessageBox.Show(error, "无法开启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (badRow != null) FocusKeyBox(badRow);
            return;
        }

        // 一行都没勾选时不开引擎，直接引导用户去配置
        if (targets.Count == 0)
        {
            ShowWindow();
            MessageBox.Show("没有可连发的按键：请至少勾选一行并填写键名。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _engine.Mode = cboMode.SelectedIndex == 0 ? "A" : "B";
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
        string autoDesc = string.Join("、", targets
            .Where(t => t.Mode == AutoFireEngine.TriggerMode.Auto)
            .Select(t => t.Count <= 0 ? "自动连按(无限)" : $"自动连按({t.Count} 次)"));
        trayIcon.ShowBalloonTip(1800, "DNF 连发",
            $"已开启连发（{targets.Count} 个目标键）{(autoDesc.Length > 0 ? "\n" + autoDesc : "")}\n最小化到托盘，按 {_config.StartHotkey} 或托盘菜单可关闭。",
            ToolTipIcon.Info);
    }

    /// <summary>
    /// 收集所有已勾选行的目标键并校验：键名为空 / 不合法 / 两行同键都会返回错误文案。
    /// 返回 null 表示校验通过。
    /// </summary>
    private string? CollectTargets(out List<AutoFireEngine.KeyTarget> targets,
        out RowControls? badRow, out string status)
    {
        targets = new List<AutoFireEngine.KeyTarget>();
        badRow = null;
        status = "";
        var used = new Dictionary<int, int>(); // VK → 行索引，用于同键冲突提示

        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (!r.Chk.Checked) continue;

            string text = r.TxtKey.Text.Trim().ToUpperInvariant();
            int vk = KeyTextToVk(text);
            // 勾选了就必须有合法键名：空或非法一律拒绝开启，不再静默跳过
            if (vk < 0)
            {
                badRow = r;
                bool empty = text.Length == 0;
                status = empty ? $"第 {i + 1} 行未填键名，无法开启" : $"第 {i + 1} 行键名 {text} 不合法，无法开启";
                return empty
                    ? $"第 {i + 1} 行勾选了「启用」但没有填键名。\n请填写键名，或取消这一行的勾选。"
                    : $"第 {i + 1} 行的键名「{text}」不合法。\n只支持 A-Z、0-9；小键盘数字用 N0-N9（例如 N5）。";
            }
            if (used.TryGetValue(vk, out int other))
            {
                badRow = r;
                status = $"第 {i + 1} 行与第 {other + 1} 行键名重复，无法开启";
                return $"第 {i + 1} 行与第 {other + 1} 行用了同一个键「{text}」。\n请改成不同的键，或取消其中一行的勾选。";
            }
            used[vk] = i;

            targets.Add(new AutoFireEngine.KeyTarget(
                vk,
                NormalizeMs(r.TxtMs.Text),
                r.SelStop.SelectedIndex == 1 ? AutoFireEngine.TriggerMode.Auto : AutoFireEngine.TriggerMode.Hold,
                NormalizeKeyRepeat(r.NumCount.Text)));
        }

        return null;
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

    /// <summary>自动连按把设定次数跑完：引擎已经自行退出，这里把界面拉回停止态。</summary>
    private void OnAutoRepeatFinished()
    {
        if (IsDisposed) return;
        _engine.Stop();
        Logger.Info("自动连按已完成设定次数，引擎自动关闭");
        SetKeysUiLocked(false);
        RefreshRunCard();
        trayIcon.ShowBalloonTip(1600, "DNF 连发", "自动连按已完成设定次数，已自动关闭。", ToolTipIcon.Info);
    }

    // ---------- 鼠标连点：启动 / 停止 ----------

    private void DoMouseStart()
    {
        if (RefuseWhileRecording()) return;

        var targets = new List<MouseClickEngine.MouseTarget>();
        foreach (var r in _mouseRows)
        {
            if (!r.Chk.Checked) continue;
            targets.Add(new MouseClickEngine.MouseTarget(
                MouseButtonVk(r.Button),
                NormalizeMouseMs(r.NumMs.Text),
                r.SelStop.SelectedIndex == 1 ? MouseClickEngine.StopMode.Auto : MouseClickEngine.StopMode.Infinite,
                NormalizeMouseRepeat(r.NumValue.Text)));
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
            $"{MouseButtonName(t.Button)}({DescribeStopMode(t.Mode, t.Count)})"));
        trayIcon.ShowBalloonTip(2000, "鼠标连点",
            $"已开启（{desc}），最小化到托盘。\n松手停止行按住对应鼠标键即连点，自动点击行无需按住。",
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

    /// <summary>自动点击把设定次数跑完：引擎已经自行退出，这里把界面拉回停止态。</summary>
    private void OnMouseAutoFinished()
    {
        if (IsDisposed) return;
        _mouseEngine.Stop();
        Logger.Info("自动点击已完成设定次数，引擎自动关闭");
        SetMouseUiLocked(false);
        RefreshRunCard();
        trayIcon.ShowBalloonTip(1600, "鼠标连点", "自动点击已完成设定次数，已自动关闭。", ToolTipIcon.Info);
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

    /// <summary>刷新主按钮与"当前控制"标识：跟随 Tab，且反映对应引擎的运行状态。</summary>
    private void RefreshRunCard()
    {
        // 录制页在同一个位置放「开始录制 + 开始回放」两个按钮，其它 Tab 仍是单个主按钮
        bool record = _tabIndex == TabRecord;
        btnMain.Visible = !record;
        btnRecord.Visible = record;
        btnPlay.Visible = record;

        switch (_tabIndex)
        {
            case TabRecord:
                lblTarget.Text = "当前控制：录制回放";
                SyncRecordButtons(); // 切到录制页时按当前状态刷一遍按钮
                break;

            case TabMouse:
                lblTarget.Text = "当前控制：鼠标连点";
                btnMain.Text = _mouseEngine.Running ? "关闭连点" : "开启连点";
                btnMain.Enabled = true;
                btnMain.Danger = _mouseEngine.Running;
                break;

            default: // TabKeys
                lblTarget.Text = "当前控制：按键连发";
                btnMain.Text = _engine.Running ? "关闭连发" : "开启连发";
                btnMain.Enabled = true;
                btnMain.Danger = _engine.Running;
                break;
        }
        RefreshStatus(); // 状态栏同时展示各引擎状态
        UpdateTrayText();
    }

    private static string DescribeStopMode(MouseClickEngine.StopMode mode, int value) => mode switch
    {
        MouseClickEngine.StopMode.Auto => value > 0 ? $"自动点击 {value} 次" : "自动点击 一直点",
        _ => "松手停止"
    };

    /// <summary>配置行按钮标识 → VK 键码（L=左键 R=右键 M=滚轮按下/中键）。</summary>
    private static int MouseButtonVk(string button) => button switch
    {
        "R" => NativeMethods.VK_RBUTTON,
        "M" => NativeMethods.VK_MBUTTON,
        _ => NativeMethods.VK_LBUTTON
    };

    private static string MouseButtonName(int vk) => vk switch
    {
        NativeMethods.VK_RBUTTON => "右键",
        NativeMethods.VK_MBUTTON => "滚轮",
        _ => "左键"
    };

    /// <summary>状态栏：两个引擎各自的运行状态，独立开关一目了然。</summary>
    private void RefreshStatus()
    {
        string keys = _engine.Running ? $"按键连发 运行中（{(_engine.Mode == "A" ? "保留原生" : "拦截原生")}）" : "按键连发 未开启";
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
        // 用户点 X 已在 WndProc 的 SC_CLOSE 分支里拦下并问过一遍；
        // 走到这里的是程序自身关窗（退出流程）或系统关机，直接放行。
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
            trayIcon.ShowBalloonTip(1600, "DNF 连发",
                "仍在后台运行，双击托盘图标可重新打开。", ToolTipIcon.Info);
    }

    /// <summary>主窗口隐藏（最小化到托盘）时立刻收起白幕，避免屏幕上残留一块白色浮层。</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible || _veil == null || _veil.IsDisposed) return;
        _fadeTimer.Stop();
        _veil.Hide();
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
        // 点标题栏 X（以及系统菜单关闭、Alt+F4）都是 WM_SYSCOMMAND + SC_CLOSE：
        // 在这里拦下先问「最小化还是退出」，不交给默认处理，所以窗口不会直接关掉。
        if (m.Msg == NativeMethods.WM_SYSCOMMAND
            && (m.WParam.ToInt64() & 0xFFF0) == NativeMethods.SC_CLOSE
            && !_exiting)
        {
            HandleUserCloseRequest();
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
        numPlayValue.Max = playMode == "duration" ? 600 : 9999;
        numPlayValue.SetValue(Math.Clamp(_config.PlayValue, 1, numPlayValue.Max));
        numPlayGap.SetValue(Math.Clamp(_config.PlayGapSec, 0, MacroPlayer.MaxGapSec));
        ApplyPlayMode();

        SetKeysUiLocked(_engine.Running);
        SetMouseUiLocked(_mouseEngine.Running);
        RefreshRunCard();
        RefreshScroll();
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        var handle = Handle; // 强制创建句柄

        // 四个热键同表处理：同一个键重复配置时只注册一次。
        // 否则后一个 RegisterHotKey 必然失败，会被误报成"被其他程序占用"。
        var defs = new (int Id, string Name, string Key)[]
        {
            (HOTKEY_ID_START, "连发", _config.StartHotkey),
            (HOTKEY_ID_MOUSE, "连点", _config.MouseStartHotkey),
            (HOTKEY_ID_RECORD, "录制", _config.RecordHotkey),
            (HOTKEY_ID_PLAY, "回放", _config.PlaybackHotkey)
        };

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var duplicated = new List<string>();
        var occupied = new List<string>();

        foreach (var (id, name, key) in defs)
        {
            string k = (key ?? "").Trim();
            if (k.Length == 0) continue;

            if (owners.TryGetValue(k, out string? owner))
            {
                duplicated.Add($"{k}（{owner}/{name}）");
                continue;
            }
            if (!NativeMethods.RegisterHotKey(handle, id, 0, (uint)HotkeyStringToVk(k)))
                occupied.Add(k);
            else
                owners[k] = name;
        }

        if (duplicated.Count > 0)
        {
            string names = string.Join("、", duplicated);
            Logger.Info($"热键重复配置：{names}（同名键只注册一次，另一项不可用）");
            UpdateStatus("警告：热键重复 " + names);
        }
        if (occupied.Count > 0)
        {
            string names = string.Join("、", occupied);
            Logger.Info($"热键注册失败：{names}（可能被其他程序占用）");
            UpdateStatus("警告：热键 " + names + " 注册失败（可能被其他程序占用）");
        }
    }

    private void UnregisterHotkeys()
    {
        if (!IsHandleCreated) return;
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_START);
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_MOUSE);
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_RECORD);
        NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_PLAY);
    }

    // ---------- UI 构建 ----------

    private void BuildUi()
    {
        // 主区域（Tab + 配置页）：三个区域（主区/启动区/设置区）以 2px 深色边框分隔，无留白卡片
        var pnlMain = new Panel { Location = new Point(2, 2), Size = new Size(636, 302), BackColor = Color.White };

        // 自定义 Tab 头（AntD 风格：文字 + 蓝色下划线，下边框 1px 样式不变）
        tabHeader = new TabHeaderPanel { Location = Point.Empty, Size = new Size(636, 44), BackColor = Color.White, TabStop = true, TabIndex = 0 };
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

        // 页面容器：各页面叠放在同一位置，只显示当前 Tab 对应的那一页
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i] = new Panel
            {
                Location = new Point(0, 44),
                Size = new Size(636, 258),
                BackColor = Color.White,
                Visible = i == TabKeys,
                TabIndex = i + 1   // 排在 tabHeader 之后：Tab 键/tab 顺序里页签始终是第一个可选控件
            };
            pnlMain.Controls.Add(_pages[i]);
        }

        BuildKeysPage(_pages[TabKeys]);
        BuildMousePage(_pages[TabMouse]);
        BuildRecordPage(_pages[TabRecord]);

        pnlMain.Controls.Add(tabHeader);
        Controls.Add(pnlMain);

        // 启动区（左下）：按键/鼠标页是一个主按钮；录制页换成「开始录制 / 开始回放」两个等宽按钮
        var pnlRun = new Panel { Location = new Point(2, 306), Size = new Size(294, 92), BackColor = Color.White };
        const int RunBtnW = 114;              // (244 - 间距 16) / 2，两个按钮均分同一块宽度
        const int RunBtnGap = 16;
        btnMain = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "开启连发",
            Location = new Point(14, 6),
            Size = new Size(244, 40),
            Font = new Font(AntStyle.FontName, 10f)
        };
        btnRecord = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "● 开始录制",
            Location = new Point(14, 6),
            Size = new Size(RunBtnW, 40),
            Font = new Font(AntStyle.FontName, 10f),
            Visible = false
        };
        btnRecord.Click += (_, _) => ToggleRecording();
        btnPlay = new AntButton
        {
            Kind = AntButton.AntKind.Default,
            Text = "▶ 开始回放",
            Location = new Point(14 + RunBtnW + RunBtnGap, 6),
            Size = new Size(RunBtnW, 40),
            Font = new Font(AntStyle.FontName, 10f),
            Visible = false
        };
        btnPlay.Click += (_, _) => TogglePlayback();
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
        pnlRun.Controls.Add(btnRecord);
        pnlRun.Controls.Add(btnPlay);
        pnlRun.Controls.Add(lblTarget);
        pnlRun.Controls.Add(lblStatus);
        Controls.Add(pnlRun);

        // 设置区（右下）：热键下拉框跟随当前 Tab——按键页编辑连发热键（默认 F6）、
        // 鼠标页编辑连点热键（默认 F8）；两个热键同时全局生效
        var pnlSet = new Panel { Location = new Point(298, 306), Size = new Size(340, 92), BackColor = Color.White };
        lblHotkey = new Label { Text = "连发热键", Location = new Point(14, 12), AutoSize = true, ForeColor = AntStyle.TextSec };
        lblMode = new Label { Text = "连发模式", Location = new Point(14, 44), AutoSize = true, ForeColor = AntStyle.TextSec };

        cboStartKey = MakeHotkeySelect(new Point(86, 8));
        cboMode = new AntSelect { Location = new Point(86, 40), Width = 120 };
        cboMode.Inner.DropDownWidth = 120;
        cboMode.Inner.Items.Add("保留原生");
        cboMode.Inner.Items.Add("拦截原生");
        cboMode.SelectedIndex = 1; // 默认拦截原生：物理按键不进游戏，避免和模拟连发打架

        // 录制页第二行：回放热键（与"连发模式"同位置，按 Tab 二选一显示）
        cboPlayKey = MakeHotkeySelect(new Point(86, 40));
        cboPlayKey.Visible = false;

        // 自动保存：热键变化需重新注册，模式/其余配置只写文件
        cboStartKey.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _hotkeySyncing) return;
            SaveConfig();
            RegisterHotkeys();
        };
        cboMode.SelectedIndexChanged += (_, _) =>
        {
            ApplyKeyModeTip();
            if (_loading) return;
            SaveConfig();
        };
        cboPlayKey.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _hotkeySyncing) return;
            SaveConfig();
            RegisterHotkeys();
        };

        // 模式说明（跟着下拉框当前选项变）
        lblModeTip = new Label
        {
            Text = "",
            Location = new Point(14, 70),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f)
        };

        pnlSet.Controls.AddRange(new Control[] { lblHotkey, lblMode, cboStartKey, cboMode, cboPlayKey, lblModeTip });
        Controls.Add(pnlSet);
        ApplyKeyModeTip();

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
            // 焦点收到页签栏，而不是留空：留空时原生焦点仍停在输入框里，
            // 最小化再还原时 Windows 会把焦点还给那个输入框
            c.MouseDown += (_, _) => tabHeader.Focus();
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
        for (int i = 0; i < TabTitles.Length; i++)
        {
            string text = TabTitles[i];
            int w = _tabWidths[i];
            bool active = i == _tabIndex;
            var color = active ? AntStyle.Primary
                : i == _tabHover ? AntStyle.TextPri
                : AntStyle.TextSec;
            // 文字在页签内水平居中，下划线才能以文字为基准对称
            TextRenderer.DrawText(g, text, _tabFont, new Rectangle(x, 11, w, 24), color,
                TextFormatFlags.HorizontalCenter);
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

    /// <summary>下划线：宽度统一（_underlineWidth），在页签内水平居中，长度不随文字多少变化。</summary>
    private Rectangle UnderlineOf(int index)
    {
        if (index < 0 || index >= _tabRects.Count) return Rectangle.Empty;
        var rect = _tabRects[index].Rect;
        int w = Math.Min(_underlineWidth, rect.Width);
        return new Rectangle(rect.X + (rect.Width - w) / 2, 41, w, 3);
    }

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

        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visible = i == idx;
        BeginPageFade();  // 新页面从白幕里淡入
        tabHeader.Invalidate();
        RefreshRunCard(); // 主按钮与"当前控制"标识跟随 Tab 切换
        SyncHotkeyUI();   // 热键下拉框跟随 Tab
    }

    /// <summary>
    /// 设置卡片跟随当前 Tab：按键页=连发热键 + 连发模式 + 键盘提示；
    /// 鼠标页=连点热键 + 鼠标提示（无模式概念）；录制页=录制热键 + 录制提示。
    /// </summary>
    private void SyncHotkeyUI()
    {
        bool mouse = _tabIndex == TabMouse;
        bool record = _tabIndex == TabRecord;
        bool locked = _recorder != null || _player != null;

        lblHotkey.Text = record ? "录制热键" : mouse ? "连点热键" : "连发热键";
        _hotkeySyncing = true;
        cboStartKey.Text = record ? _config.RecordHotkey : mouse ? _config.MouseStartHotkey : _config.StartHotkey;
        cboPlayKey.Text = _config.PlaybackHotkey;
        cboStartKey.Enabled = !locked;  // 录制/回放中不允许改热键
        cboPlayKey.Enabled = !locked;
        _hotkeySyncing = false;

        // 第二行：按键页=连发模式，录制页=回放热键，鼠标页没有这一行
        lblMode.Text = record ? "回放热键" : "连发模式";
        lblMode.Visible = !mouse;
        cboMode.Visible = !mouse && !record;
        cboPlayKey.Visible = record;
        lblModeTip.Text = record
            ? "F9 录制、F10 回放，互不干扰；回放时主窗口收进托盘"
            : mouse
                ? "连点热键全局开关鼠标连点，与连发热键相互独立"
                : "";
        if (!record && !mouse) ApplyKeyModeTip(); // 按键页：说明跟着「保留原生 / 拦截原生」走
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

    /// <summary>
    /// 页面切换的白幕淡入：在页面区域盖一层白色蒙层，透明度从 55% 递减到 0。
    /// 子控件画不出真正的 alpha（GDI 会丢弃），所以白幕只能是独立的无激活顶层窗口。
    /// </summary>
    private void BeginPageFade()
    {
        var page = _pages[_tabIndex];
        if (_veil == null || _veil.IsDisposed)
            _veil = new FadeVeil { Owner = this };
        _veil.Bounds = new Rectangle(page.PointToScreen(Point.Empty), page.Size);
        _veil.Opacity = VeilStartOpacity;
        _veil.Show();
        _fadeElapsed = 0;
        _fadeTimer.Start();
    }

    private void Fade_Tick(object? sender, EventArgs e)
    {
        if (_veil == null || _veil.IsDisposed || !Visible)
        {
            _fadeTimer.Stop();
            return;
        }
        _fadeElapsed += _fadeTimer.Interval;
        float t = Math.Min(1f, (float)_fadeElapsed / FadeMs);
        t = t * t * (3 - 2 * t); // smoothstep：起止更柔和
        _veil.Opacity = VeilStartOpacity * (1 - t);
        if (t >= 1f)
        {
            _fadeTimer.Stop();
            _veil.Hide();
        }
    }

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
        AddPageTitle(page, "按键配置", $"间隔范围 {MinMs}-{MaxMs}ms，步进 {KeyStepMs}");

        pnlRows = new Panel
        {
            Location = new Point(16, 40),
            Size = new Size(RowWidth + 12, 210),
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
            Size = new Size(RowWidth, RowH),
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

    /// <summary>鼠标连点页：固定左/右/滚轮三行 + 停止条件下拉（松手停止 / 自动点击，默认松手停止）。</summary>
    private void BuildMousePage(Control page)
    {
        AddPageTitle(page, "鼠标连点", $"间隔范围 {MouseMinMs}-{MouseMaxMs}ms，步进 {MouseStepMs}");

        foreach (var (button, label, y) in new[] { ("L", "左键", 44), ("R", "右键", 82), ("M", "滚轮", 120) })
        {
            var row = new MouseRow { Button = button };
            var panel = new Panel { Location = new Point(16, y), Size = new Size(RowWidth, RowH), BackColor = Color.White };
            row.RowPanel = panel;

            row.Chk = new AntCheckBox
            {
                Text = "启用",
                Checked = false,
                Location = new Point(0, 2),
                Size = new Size(56, 28)
            };
            // 与按键页同构：文字宽度按实测值顺排（Label.AutoSize 要挂到父控件后才生效，布局时不能读 Right）
            var lblBtn = MakeRowText(label, 58, AntStyle.TextPri, bold: true);
            int x = 58 + RowTextWidth(label, bold: true) + 6;
            var lblGap = MakeRowText("键，间隔", x);
            x += RowTextWidth("键，间隔") + 8;
            row.NumMs = new AntNumber
            {
                Location = new Point(x, 3),
                Size = new Size(84, 28),
                Step = MouseStepMs,
                Min = MouseMinMs,
                Max = MouseMaxMs
            };
            row.NumMs.Text = MouseDefaultMs.ToString();
            x += 84 + 6;
            var lblMs = MakeRowText("毫秒，", x);
            x += RowTextWidth("毫秒，") + 8;
            row.SelStop = new AntSelect { Location = new Point(x, 2), Width = 96 };
            row.SelStop.Inner.DropDownWidth = 96;
            row.SelStop.Inner.Items.Add("松手停止");
            row.SelStop.Inner.Items.Add("自动点击");
            row.SelStop.SelectedIndex = 0; // 默认松手停止
            x += 96 + 6;
            row.NumValue = new AntNumber
            {
                Location = new Point(x, 3),
                Size = new Size(72, 28),
                Step = 1,
                Min = 0,
                Max = MaxMouseRepeat
            };
            row.NumValue.Text = "1"; // 自动点击默认点 1 次；0 = 无限
            row.NumValue.Inner.MaxLength = 5;
            row.LblCount = MakeRowText("次", x + 72 + 6); // 单位跟着次数框一起显隐
            ApplyMouseStopMode(row, "infinite", 0); // 初始即收口：默认松手停止时隐藏数值框（配置缺失时不会再漏显示）

            row.SelStop.SelectedIndexChanged += (_, _) =>
            {
                // 切换停止条件：调整数值框可见性/范围，并按当前值重新钳制
                string mode = row.SelStop.SelectedIndex == 1 ? "auto" : "infinite";
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
                row.Chk, lblBtn, lblGap, row.NumMs, lblMs, row.SelStop, row.NumValue, row.LblCount
            });
            page.Controls.Add(panel);
            _mouseRows.Add(row);
        }

        // 三种触发方式各占一行，避免折行后行数随窗口宽度变化
        const int hintX = 16, hintY = 168, hintLine = 19;
        string[] hints =
        {
            "松手停止：按住对应鼠标键，自动连点，松手即停",
            "自动点击：无需按下鼠标，设置好次数（0=无限次），自动点击 N 次",
            "滚轮=滚轮按下（中键）"
        };
        for (int i = 0; i < hints.Length; i++)
        {
            page.Controls.Add(new Label
            {
                Text = hints[i],
                Location = new Point(hintX, hintY + i * hintLine),
                AutoSize = true,
                ForeColor = AntStyle.TextTertiary,
                Font = new Font(AntStyle.FontName, 8f)
            });
        }
    }

    /// <summary>录制页：方案列表（单选 + 行内改名 + 删除）+ 开始/停止录制。</summary>
    private void BuildRecordPage(Control page)
    {
        // 与按键/鼠标页同一套标题样式：10.5pt 标题 + 8pt 灰字说明
        AddPageTitle(page, "鼠标录制回放",
            $"最多 {RecordStore.MaxProfiles} 个方案，单次录制上限 {MacroRecorder.MaxDurationMs / 60000} 分钟");

        // 方案列表：行高 30，正好 4 行，超出滚动（最多 10 个方案）
        pnlRecRows = new Panel
        {
            Location = new Point(16, 38),
            Size = new Size(RowWidth + 12, RecListHeight),
            BackColor = Color.White
        };
        recScroll = new FlatScrollBar
        {
            Location = new Point(pnlRecRows.Width - 8, 0),
            Size = new Size(8, pnlRecRows.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        recScroll.OffsetChanged += off => pnlRecContent.Top = -off;
        pnlRecContent = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(RowWidth, RecListHeight),
            BackColor = Color.White
        };
        pnlRecRows.Controls.Add(pnlRecContent);
        pnlRecRows.Controls.Add(recScroll);
        page.Controls.Add(pnlRecRows);

        lblRecEmpty = new Label
        {
            Text = "还没有录制方案：点下方「开始录制」录一个",
            Location = new Point(0, 50),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            BackColor = Color.White
        };
        pnlRecRows.Controls.Add(lblRecEmpty);
        lblRecEmpty.BringToFront();

        // 录制/回放两个按钮在左下角的启动区里并排（见 BuildUi 的 pnlRun），页面内不再放按钮

        // 回放行：模式 + 数值 + 单位 + 间隔
        const int PlayRowY = 170;
        lblPlay = new Label
        {
            Text = "回放",
            Location = new Point(16, PlayRowY + 7),
            AutoSize = true,
            ForeColor = AntStyle.TextSec
        };
        cboPlayMode = new AntSelect { Location = new Point(52, PlayRowY + 2), Width = 118 };
        cboPlayMode.Inner.DropDownWidth = 118;
        cboPlayMode.Inner.Items.Add("回放 N 次");
        cboPlayMode.Inner.Items.Add("一直放");
        cboPlayMode.Inner.Items.Add("定时 N 分钟");
        cboPlayMode.SelectedIndex = 1; // 默认一直放
        cboPlayMode.SelectedIndexChanged += (_, _) =>
        {
            ApplyPlayMode();
            if (!_loading) SaveConfig();
        };

        numPlayValue = new AntNumber
        {
            Location = new Point(178, PlayRowY),
            Size = new Size(80, 28),
            Step = 1,
            Min = 1,
            Max = 9999
        };
        numPlayValue.Text = "1";
        numPlayValue.ValueChanged += (_, _) => { if (!_loading) SaveConfig(); };

        lblPlayUnit = new Label
        {
            Text = "次",
            Location = new Point(264, PlayRowY + 7),
            AutoSize = true,
            ForeColor = AntStyle.TextSec
        };

        // 回放间隔：两轮之间等多久（默认 1 秒，0 = 轮尾直接接轮头）
        lblGap = new Label
        {
            Text = "间隔",
            Location = new Point(300, PlayRowY + 7),
            AutoSize = true,
            ForeColor = AntStyle.TextSec
        };
        numPlayGap = new AntNumber
        {
            Location = new Point(332, PlayRowY),
            Size = new Size(84, 28),
            Step = 1,
            Min = 0,
            Max = MacroPlayer.MaxGapSec
        };
        numPlayGap.Text = MacroPlayer.DefaultGapSec.ToString();
        numPlayGap.ValueChanged += (_, _) => { if (!_loading) SaveConfig(); };
        lblGapUnit = new Label
        {
            Text = "秒",
            Location = new Point(420, PlayRowY + 7),
            AutoSize = true,
            ForeColor = AntStyle.TextSec
        };

        page.Controls.AddRange(new Control[]
        {
            lblPlay, cboPlayMode, numPlayValue, lblPlayUnit, lblGap, numPlayGap, lblGapUnit
        });
        ApplyPlayMode(); // 初始收口：默认"一直放"隐藏数值框

        // 这一行空闲时是操作提示，录制/回放时变成状态（录制中… / 回放第几次）
        lblRecStatus = new Label
        {
            Location = new Point(16, 202),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f)
        };
        var lblRecHint = new Label
        {
            Text = "录制时主窗口收进托盘，右下角留状态条。",
            Location = new Point(16, 222),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f)
        };
        page.Controls.Add(lblRecStatus);
        page.Controls.Add(lblRecHint);
    }

    /// <summary>回放模式收口：一直放＝隐藏数值框；其余按"次/分钟"给范围与单位。</summary>
    private void ApplyPlayMode()
    {
        bool infinite = cboPlayMode.SelectedIndex == 1;
        bool timed = cboPlayMode.SelectedIndex == 2;

        numPlayValue.Visible = !infinite;
        lblPlayUnit.Visible = !infinite;
        lblPlayUnit.Text = timed ? "分钟" : "次";
        // 一直放没有数值框：把「间隔」整组左移，避免中间空一大块
        lblGap.Left = infinite ? 184 : 300;
        numPlayGap.Left = infinite ? 216 : 332;
        lblGapUnit.Left = infinite ? 304 : 420;

        if (infinite) return;
        numPlayValue.Min = 1;
        numPlayValue.Max = timed ? 600 : 9999;
        numPlayValue.SetValue(numPlayValue.Value); // 切换模式后按新范围重新钳制
        numPlayValue.Enabled = _recorder == null && _player == null;
    }

    /// <summary>连发模式说明：跟着下拉框当前选项走（原来固定写着 B 模式的长句）。</summary>
    private void ApplyKeyModeTip()
    {
        if (lblModeTip == null || cboMode == null) return;
        lblModeTip.Text = cboMode.SelectedIndex == 0
            ? "保留原生：放行物理按键，叠加模拟连按"
            : "拦截原生：拦下物理按键，只发模拟连按";
    }

    /// <summary>重建方案列表（≤10 行，成本极低，改名/删除后整体重建最省心）。</summary>
    private void RebuildRecordList()
    {
        foreach (var p in _recRowPanels) p.Dispose();
        _recRowPanels.Clear();
        _recSelection.Clear();
        pnlRecContent.Controls.Clear();

        for (int i = 0; i < _profiles.Count; i++)
        {
            var profile = _profiles[i];
            var row = new Panel
            {
                Location = new Point(0, i * RecRowH),
                Size = new Size(RowWidth, RecRowH),
                BackColor = Color.White
            };

            var chk = new AntCheckBox
            {
                Text = "",
                Checked = profile.Id == _selectedProfileId,
                Location = new Point(0, 0),
                Size = new Size(24, RecRowH)
            };
            chk.CheckedChanged += (_, _) => OnProfileChecked(profile, chk);

            var name = new AntInput
            {
                Location = new Point(26, 1),
                Size = new Size(200, 28), // 总宽固定 200（含边框），行内其余控件一律跟着它往后排
                TextInset = 8             // 名字贴边框太挤，左右各留 8px
            };
            name.Inner.MaxLength = 15;
            name.Text = profile.Name;
            name.Inner.Leave += (_, _) => RenameProfile(profile, name);
            name.Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                tabHeader.Focus(); // 触发 Leave → 落盘
            };

            // 时长紧跟在名字后面，删除按钮再紧跟时长——不再拉到行尾留一大段空白
            var summary = new Label
            {
                Text = profile.Summary,
                Location = new Point(234, 6),
                AutoSize = true,
                ForeColor = AntStyle.TextSec
            };

            var del = new AntButton
            {
                Kind = AntButton.AntKind.Text,
                Danger = true,
                Text = "删除",
                Location = new Point(234 + RowTextWidth(profile.Summary) + 12, 2),
                Size = new Size(44, 26)
            };
            del.Click += (_, _) => DeleteProfile(profile);

            row.Controls.AddRange(new Control[] { chk, name, summary, del });
            pnlRecContent.Controls.Add(row);
            _recRowPanels.Add(row);
            _recSelection.Add((profile, chk));
        }

        int contentH = Math.Max(RecRowH, _profiles.Count * RecRowH);
        pnlRecContent.Height = contentH;
        recScroll.SetMetrics(contentH, pnlRecRows.Height);
        pnlRecContent.Top = -recScroll.Offset;

        lblRecEmpty.Visible = _profiles.Count == 0;
        lblRecEmpty.BringToFront();
        UpdateRecordUi();
        RefreshRunCard(); // 主按钮可用性依赖"有没有选中方案"
    }

    /// <summary>单选：点已选中的方案不会取消勾选（等价单选按钮）。</summary>
    private void OnProfileChecked(RecordProfile profile, AntCheckBox chk)
    {
        // 程序自己取消别的勾选时也会回到这里：不挡住就是 A 取消 B、B 反弹勾选 A……一路递归到栈溢出
        if (_syncingSelection) return;

        if (!chk.Checked)
        {
            // 不允许通过再次点击取消选中，只有切换才能改变选中
            _syncingSelection = true;
            try { chk.Checked = true; }
            finally { _syncingSelection = false; }
            return;
        }
        if (_selectedProfileId == profile.Id) return;
        _selectedProfileId = profile.Id;

        _syncingSelection = true;
        try
        {
            foreach (var (p, c) in _recSelection)
                if (p.Id != profile.Id && c.Checked) c.Checked = false;
        }
        finally { _syncingSelection = false; }

        UpdateRecordUi();
        RefreshRunCard();
    }

    private void RenameProfile(RecordProfile profile, AntInput input)
    {
        if (_recorder != null) return;
        string name = (input.Text ?? "").Trim();
        if (name.Length == 0) name = profile.Name;      // 空名不覆盖，回填原值
        if (name == profile.Name) { input.Text = name; return; }
        profile.Name = name;
        input.Text = name;
        if (SaveProfiles()) UpdateStatus("已重命名为 " + name);
    }

    private void DeleteProfile(RecordProfile profile)
    {
        if (_recorder != null) return;
        _profiles.Remove(profile);
        if (_selectedProfileId == profile.Id) _selectedProfileId = "";
        SaveProfiles();
        RebuildRecordList();
        UpdateStatus("已删除 " + profile.Name);
    }

    private bool SaveProfiles()
    {
        try
        {
            RecordStore.Save(_profiles);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("RecordStore.Save", ex);
            UpdateStatus("方案保存失败：" + ex.Message);
            return false;
        }
    }

    // ---------- 录制 ----------

    /// <summary>录制期间拒绝开启连发/连点：注入事件会污染轨迹，连点拦截还会吞掉真实点击。</summary>
    private bool RefuseWhileRecording()
    {
        if (_recorder == null) return false;
        ShowWindow();
        MessageBox.Show("正在录制轨迹，请先结束录制（点右下角状态条的■或按录制热键）。", "提示",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    private void ToggleRecording()
    {
        if (_recorder != null) StopRecording();
        else StartRecording();
    }

    private void StartRecording()
    {
        if (_recorder != null) return;

        if (_player != null)
        {
            ShowWindow();
            MessageBox.Show("正在回放轨迹，请先停止回放再录制。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 录制期间不能有注入引擎在跑：注入事件虽然带 injected 标志会被过滤，
        // 但连点拦截会吞掉真实点击，轨迹会缺事件
        if (_engine.Running || _mouseEngine.Running)
        {
            ShowWindow();
            MessageBox.Show("连发或连点正在运行，请先关闭再录制。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_profiles.Count >= RecordStore.MaxProfiles)
        {
            ShowWindow();
            MessageBox.Show($"方案已满 {RecordStore.MaxProfiles} 个，请先删除旧方案。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var recorder = new MacroRecorder();
            var overlay = new RecordOverlayForm(recorder);
            _recorder = recorder;
            _overlay = overlay;
            overlay.StopRequested += StopRecording;
            overlay.BoundsMoved += () => _recorder?.UpdateExclude(overlay.ExcludeBounds);
            overlay.Show();
            recorder.Start(overlay.ExcludeBounds); // 悬浮条区域整块排除，点■/拖它都不会进轨迹
        }
        catch (Exception ex)
        {
            Logger.Error("StartRecording", ex);
            _overlay?.CloseSilently();
            _overlay = null;
            _recorder?.Dispose();
            _recorder = null;
            UpdateRecordUi();
            MessageBox.Show("开始录制失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Logger.Info("开始录制轨迹");
        UpdateRecordUi();
        Hide(); // 主窗口收进托盘，屏幕只留状态条
        trayIcon.ShowBalloonTip(1600, "轨迹录制",
            $"录制中，结束后点状态条的■或按 {_config.RecordHotkey}。", ToolTipIcon.Info);
    }

    private void StopRecording()
    {
        if (_recorder == null) return;
        var recorder = _recorder;
        var overlay = _overlay;
        _recorder = null;
        _overlay = null;

        RecordProfile profile;
        try
        {
            profile = recorder.Stop();
        }
        finally
        {
            recorder.Dispose();
        }

        if (overlay != null)
        {
            overlay.StopRequested -= StopRecording;
            overlay.CloseSilently();
        }
        ShowWindow();

        if (profile.Points.Count > 0)
        {
            profile.Name = RecordStore.NextName(_profiles);
            _profiles.Add(profile);
            _selectedProfileId = profile.Id; // 录完自动选中，方便直接回放
            SaveProfiles();
            RebuildRecordList();
            Logger.Info($"录制完成：{profile.Name} {profile.Summary}");
            trayIcon.ShowBalloonTip(2000, "轨迹录制",
                $"已保存 {profile.Name}：{profile.Summary}", ToolTipIcon.Info);
        }
        else
        {
            RebuildRecordList();
            Logger.Info("录制结束但没有采样点，已丢弃");
            trayIcon.ShowBalloonTip(1500, "轨迹录制", "没有采到轨迹，本次已丢弃。", ToolTipIcon.Warning);
        }
        UpdateRecordUi();
    }

    /// <summary>录制/回放两个按钮的文案与可用性（Tab 切换与状态变化都要刷新）。</summary>
    private void SyncRecordButtons()
    {
        bool rec = _recorder != null;
        bool play = _player != null;

        btnRecord.Text = rec ? "■ 停止录制" : "● 开始录制";
        btnRecord.Danger = rec;
        btnRecord.Enabled = !play;   // 回放中不能开始录制
        btnRecord.Invalidate();

        btnPlay.Text = play ? "■ 停止回放" : "▶ 开始回放";
        btnPlay.Kind = play ? AntButton.AntKind.Primary : AntButton.AntKind.Default;
        btnPlay.Danger = play;
        btnPlay.Enabled = play || (!rec && SelectedProfile() != null); // 未选方案/录制中不可开始
        btnPlay.Invalidate();
    }

    /// <summary>录制/回放状态与按钮文案、列表锁定统一在这里收口（所有入口都调它）。</summary>
    private void UpdateRecordUi()
    {
        bool rec = _recorder != null;
        bool play = _player != null;
        bool locked = rec || play;
        bool canPlay = play || (!rec && SelectedProfile() != null);

        SyncRecordButtons();

        if (rec)
        {
            lblRecStatus.Text = "● 录制中…（按 F9 或点按钮停止）";
            lblRecStatus.ForeColor = AntStyle.Danger;
        }
        else if (play)
        {
            lblRecStatus.ForeColor = AntStyle.Primary; // 进度文案由 _playTimer 刷新
        }
        else
        {
            lblRecStatus.Text = RecordHint;
            lblRecStatus.ForeColor = AntStyle.TextTertiary;
        }

        foreach (var (_, chk) in _recSelection) chk.Enabled = !locked;
        foreach (var row in _recRowPanels)
            foreach (Control c in row.Controls)
                if (c is AntInput || c is AntButton) c.Enabled = !locked;

        cboPlayMode.Enabled = !locked;
        numPlayValue.Enabled = !locked && cboPlayMode.SelectedIndex != 1;
        numPlayGap.Enabled = !locked;

        if (trayRecordItem != null)
            trayRecordItem.Text = rec ? "停止录制" : "开始录制";
        if (trayPlayItem != null)
        {
            trayPlayItem.Text = play ? "停止回放" : "开始回放";
            trayPlayItem.Enabled = canPlay;
        }
        if (cboStartKey != null && _tabIndex == TabRecord) cboStartKey.Enabled = !locked;
        if (cboPlayKey != null && _tabIndex == TabRecord) cboPlayKey.Enabled = !locked;
        UpdateTrayText();
    }

    /// <summary>托盘悬停提示：录制 &gt; 回放 &gt; 引擎运行 &gt; 空闲（Windows 限制长度，超出截断）。</summary>
    private void UpdateTrayText(string? progress = null)
    {
        if (trayIcon == null) return;
        string text =
            _recorder != null ? "DNF 连发 · 录制中"
            : _player != null ? progress ?? "DNF 连发 · 回放中"
            : _engine.Running || _mouseEngine.Running ? "DNF 连发 · 运行中"
            : "DNF 连发";
        if (text.Length > 60) text = text[..60];
        trayIcon.Text = text;
    }

    /// <summary>当前选中的方案（列表单选，<see cref="_selectedProfileId"/> 为空表示没选）。</summary>
    private RecordProfile? SelectedProfile() =>
        _profiles.FirstOrDefault(p => p.Id == _selectedProfileId);

    private static string FormatDuration(long ms) =>
        $"{ms / 60000:00}:{ms % 60000 / 1000:00}";

    private string PlayModeKey() => cboPlayMode.SelectedIndex switch
    {
        0 => "count",
        2 => "duration",
        _ => "infinite"
    };

    private static string DescribePlayMode(PlayMode mode, int value) => mode switch
    {
        PlayMode.Count => $"回放 {value} 次",
        PlayMode.Duration => $"定时 {value} 分钟",
        _ => "一直放"
    };

    // ---------- 回放 ----------

    /// <summary>
    /// 回放进度：200ms 刷一次「第 N 次 · mm:ss」，同时驱动右下角状态条。
    /// 主窗口已收进托盘，这两处是用户唯一的进度来源。
    /// </summary>
    private void PlayTimer_Tick(object? sender, EventArgs e)
    {
        var p = _player;
        if (p == null)
        {
            _playTimer.Stop();
            return;
        }

        int loop = Math.Max(1, p.CurrentLoop);
        lblRecStatus.Text = p.InjectBlocked
            ? "回放中 · 注入被系统拦截，请以管理员运行"
            : $"回放中 {p.ProfileName} · 第 {loop} 次 · {FormatDuration(p.ElapsedMs)}";
        _playOverlay?.UpdateProgress(loop, p.ElapsedMs, p.InjectBlocked);
        UpdateTrayText($"DNF 连发 · 回放 {p.ProfileName} 第 {loop} 次 {FormatDuration(p.ElapsedMs)}");
    }

    private void TogglePlayback()
    {
        if (_player != null) StopPlayback();
        else StartPlayback();
    }

    private void StartPlayback()
    {
        if (_player != null) return;

        if (_recorder != null)
        {
            ShowWindow();
            MessageBox.Show("正在录制轨迹，请先结束录制再回放。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var profile = SelectedProfile();
        if (profile == null)
        {
            ShowWindow();
            MessageBox.Show("请先在列表里勾选一个方案。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (profile.Points.Count == 0)
        {
            ShowWindow();
            MessageBox.Show($"{profile.Name} 里没有轨迹点，请重新录制。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var mode = PlayModeKey() switch
        {
            "count" => PlayMode.Count,
            "duration" => PlayMode.Duration,
            _ => PlayMode.Infinite
        };
        int value = mode == PlayMode.Infinite ? 0 : Math.Max(1, numPlayValue.Value);
        int gapSec = Math.Clamp(numPlayGap.Value, 0, MacroPlayer.MaxGapSec);

        var player = new MacroPlayer();
        _player = player;
        // 回调在工作线程：切回 UI 线程收尾
        player.Finished += () =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(OnPlaybackFinished)); }
            catch (Exception ex) { Logger.Error("PlaybackFinished", ex); }
        };

        try
        {
            player.Start(profile, mode, value, gapSec);
        }
        catch (Exception ex)
        {
            Logger.Error("StartPlayback", ex);
            _player = null;
            player.Dispose();
            UpdateRecordUi();
            RefreshRunCard();
            MessageBox.Show("开始回放失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Logger.Info($"开始回放：{profile.Name} {DescribePlayMode(mode, value)} 间隔 {gapSec} 秒");
        _playTimer.Start();
        UpdateRecordUi();
        RefreshRunCard();

        // 与录制对称：窗口收进托盘后，右下角留一条状态条（方案名 / 第几次 / 计时）
        try
        {
            _playOverlay = new PlayOverlayForm(profile.Name, mode == PlayMode.Count ? value : 0);
            _playOverlay.Show();
            _playOverlay.UpdateProgress(1, 0, false); // 首帧先给内容，别等第一个 200ms
            Logger.Info($"回放状态条已显示：{_playOverlay.Bounds}");
        }
        catch (Exception ex)
        {
            // 状态条只是辅助展示，起不来不影响回放本身
            Logger.Error("PlayOverlay", ex);
            _playOverlay = null;
        }

        Hide(); // 与连发一致：让游戏拿到焦点

        // 录制与当前虚拟桌面不一致时只提示、不阻止（换过分辨率/显示器的情况很常见）
        var v = NativeInput.VirtualScreen();
        var rs = profile.Screen;
        string warn = rs.W > 0 && (rs.X != v.X || rs.Y != v.Y || rs.W != v.Width || rs.H != v.Height)
            ? $"\n注意：录制时桌面 {rs.W}×{rs.H}，当前 {v.Width}×{v.Height}，回放位置可能有偏移。"
            : "";
        trayIcon.ShowBalloonTip(2400, "轨迹回放",
            $"正在回放 {profile.Name}（{DescribePlayMode(mode, value)}），按 {_config.PlaybackHotkey} 可停止。{warn}",
            ToolTipIcon.Info);
    }

    /// <summary>摘掉右下角状态条：手动停止、自然结束、退出程序都走这里。</summary>
    private void ClosePlayOverlay()
    {
        var overlay = _playOverlay;
        _playOverlay = null;
        if (overlay == null) return;
        try { overlay.CloseSilently(); }
        catch (Exception ex) { Logger.Error("ClosePlayOverlay", ex); }
        Logger.Info("回放状态条已关闭");
    }

    private void StopPlayback()
    {
        var player = _player;
        if (player == null) return;
        _player = null;      // 先摘引用：Finished 回来时按"手动停止"处理
        _playTimer.Stop();
        ClosePlayOverlay();
        player.Stop();       // 线程内补发抬起，避免卡键
        player.Dispose();
        UpdateRecordUi();
        RefreshRunCard();
        UpdateStatus("回放已停止");
        Logger.Info("回放已手动停止");
    }

    /// <summary>回放自然结束（次数跑完 / 定时到点）：引擎已自行收尾，这里把界面拉回停止态。</summary>
    private void OnPlaybackFinished()
    {
        if (_player == null) return; // 手动停止的收尾已在 StopPlayback 里做过
        var player = _player;
        _player = null;
        _playTimer.Stop();
        ClosePlayOverlay();
        player.Dispose();
        UpdateRecordUi();
        RefreshRunCard();
        Logger.Info("回放完成");
        trayIcon.ShowBalloonTip(1800, "轨迹回放", "回放已结束。", ToolTipIcon.Info);
    }

    /// <summary>按停止条件模式调整数值框：auto=自动点击（0 = 无限，1-99999）显示可编辑，infinite(松手停止)=隐藏。</summary>
    private void ApplyMouseStopMode(MouseRow r, string mode, int value)
    {
        bool infinite = mode != "auto";
        r.NumValue.Visible = !infinite;
        r.LblCount.Visible = !infinite;
        r.NumValue.Enabled = !infinite && !_mouseEngine.Running; // 切回"自动点击"时必须恢复可编辑
        if (infinite) return;
        r.NumValue.Min = 0;
        r.NumValue.Max = MaxMouseRepeat;
        r.NumValue.Step = 1;
        r.NumValue.SetValue(Math.Clamp(value, 0, MaxMouseRepeat));
    }

    private void BuildTray()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayRecordItem = new ToolStripMenuItem("开始录制");
        trayRecordItem.Click += (_, _) => ToggleRecording();
        trayMenu.Items.Add(trayRecordItem);

        // 回放期间主窗口在托盘里，这里留一个出口（热键注册失败时也能停）
        trayPlayItem = new ToolStripMenuItem("开始回放");
        trayPlayItem.Click += (_, _) => TogglePlayback();
        trayMenu.Items.Add(trayPlayItem);

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
        public AntSelect SelStop = null!;   // 松手停止 / 自动连按
        public AntNumber NumCount = null!;  // 自动连按次数（0 = 无限），仅自动模式显示
        public Label LblCount = null!;      // 次数单位「次」，仅自动连按显示
        public AntButton LnkDel = null!;
    }

    /// <summary>鼠标连点配置行（固定左/右/滚轮三行，无增删）。</summary>
    private sealed class MouseRow
    {
        public string Button = "L"; // L=左键 R=右键 M=滚轮按下（中键）
        public Panel RowPanel = null!;
        public AntCheckBox Chk = null!;
        public AntNumber NumMs = null!;
        public AntSelect SelStop = null!;
        public AntNumber NumValue = null!;
        public Label LblCount = null!; // 次数单位「次」，仅自动点击时显示
    }

    private void AddRow(string key, int ms, bool enabled, string stopMode = "hold", int stopValue = 0)
    {
        var row = new RowControls();
        var panel = new Panel { Size = new Size(RowWidth, RowH), BackColor = Color.White };
        row.RowPanel = panel;

        row.Chk = new AntCheckBox
        {
            Text = "启用",
            Checked = enabled,
            Location = new Point(0, 2),
            Size = new Size(56, 28)
        };
        row.TxtKey = new AntInput
        {
            Location = new Point(58, 3),
            Size = new Size(56, 28)
        };
        row.TxtKey.Inner.MaxLength = 2;   // 单字符 A-Z/0-9；小键盘数字为 N0-N9 两字符
        row.TxtKey.Inner.CharacterCasing = CharacterCasing.Upper;
        row.TxtKey.Inner.TextAlign = HorizontalAlignment.Center;
        row.TxtKey.Text = key;

        // 控件按实测文字宽度顺排，间距统一 8px（Label.AutoSize 挂到父控件后 Right 才有值）。
        // 宽度沿用原来的一版：键名 56、间隔 84、下拉 96、次数 72；「次」紧跟次数框，行尾留给「删除」。
        int x = 58 + 56 + 8;
        var lblGap = MakeRowText("键，间隔", x);
        x += RowTextWidth("键，间隔") + 8;
        row.TxtMs = new AntNumber
        {
            Location = new Point(x, 3),
            Size = new Size(84, 28),
            Step = KeyStepMs,
            Min = MinMs,
            Max = MaxMs
        };
        row.TxtMs.Text = ms.ToString();
        x += 84 + 8;
        var lblMs = MakeRowText("毫秒，", x);
        x += RowTextWidth("毫秒，") + 8;
        row.SelStop = new AntSelect { Location = new Point(x, 2), Width = 96 };
        row.SelStop.Inner.DropDownWidth = 96;
        row.SelStop.Inner.Items.Add("松手停止");
        row.SelStop.Inner.Items.Add("自动连按");
        row.SelStop.SelectedIndex = stopMode == "auto" ? 1 : 0; // 先定下拉框，事件尚未挂上
        x += 96 + 8;
        row.NumCount = new AntNumber
        {
            Location = new Point(x, 3),
            Size = new Size(72, 28),
            Step = 1,
            Min = 0,
            Max = MaxKeyRepeat
        };
        row.NumCount.Text = "0"; // 0 = 无限
        row.NumCount.Inner.MaxLength = 5;
        x += 72 + 6;
        row.LblCount = MakeRowText("次", x); // 单位跟着次数框一起显隐
        ApplyKeyStopMode(row, stopMode, stopValue); // 初始收口：松手停止时隐藏次数框
        row.LnkDel = new AntButton
        {
            Kind = AntButton.AntKind.Text,
            Danger = true,
            Text = "删除",
            Location = new Point(RowWidth - 48, 3), // 固定在行尾右侧，各行的「删除」上下对齐
            Size = new Size(44, 26),
            Visible = false
        };
        row.LnkDel.Click += (_, _) => DeleteRow(row);

        row.SelStop.SelectedIndexChanged += (_, _) =>
        {
            ApplyKeyStopMode(row, row.SelStop.SelectedIndex == 1 ? "auto" : "hold", row.NumCount.Value);
            if (!_loading) SaveConfig();
        };
        // 次数框：只允许数字，离开时钳制到 0-99999（0 = 无限）
        row.NumCount.Inner.KeyPress += MsBox_KeyPress;
        row.NumCount.Inner.Leave += (_, _) =>
        {
            if (_loading) return;
            int fixedCount = NormalizeKeyRepeat(row.NumCount.Text);
            if (row.NumCount.Text != fixedCount.ToString())
            {
                row.NumCount.Text = fixedCount.ToString();
                UpdateStatus(fixedCount == 0
                    ? "次数已修正为 0（无限次）"
                    : "次数已修正为 " + fixedCount + " 次");
            }
            SaveConfig();
        };
        row.NumCount.ValueChanged += (_, _) => { if (!_loading) SaveConfig(); };
        row.TxtKey.Inner.KeyPress += KeyBox_KeyPress;
        row.TxtKey.Inner.KeyDown += KeyBox_KeyDown;
        row.TxtKey.Inner.TextChanged += KeyBox_TextChanged;
        row.TxtKey.Inner.TextChanged += (_, _) => { if (!_loading) SaveConfig(); };
        _keyTip.SetToolTip(row.TxtKey, "支持 A-Z、0-9；小键盘数字直接按小键盘（显示为 N0-N9）");
        _keyTip.SetToolTip(row.TxtKey.Inner, "支持 A-Z、0-9；小键盘数字直接按小键盘（显示为 N0-N9）");
        row.TxtMs.Inner.KeyPress += MsBox_KeyPress;
        row.TxtMs.Inner.Leave += MsBox_Leave;
        row.TxtMs.ValueChanged += (_, _) => { if (!_loading) SaveConfig(); };
        row.Chk.CheckedChanged += (_, _) => { if (!_loading) SaveConfig(); };

        panel.Controls.AddRange(new Control[]
        {
            row.Chk, row.TxtKey, lblGap, row.TxtMs, lblMs, row.SelStop, row.NumCount, row.LblCount, row.LnkDel
        });

        _rows.Add(row);
    }

    /// <summary>按停止条件模式调整次数框：auto=显示（0-99999，0 表示无限），hold=隐藏（连「次」一起）。</summary>
    private void ApplyKeyStopMode(RowControls r, string mode, int value)
    {
        bool auto = mode == "auto";
        r.NumCount.Visible = auto;
        r.LblCount.Visible = auto;
        r.NumCount.Enabled = auto && !_engine.Running;
        if (!auto) return;
        r.NumCount.Min = 0;
        r.NumCount.Max = MaxKeyRepeat;
        r.NumCount.Step = 1;
        r.NumCount.SetValue(Math.Clamp(value, 0, MaxKeyRepeat));
    }

    /// <summary>自动连按次数：0 = 无限，非法输入（空/负数）回落 0，上限 99999。</summary>
    private static int NormalizeKeyRepeat(string text) => NormalizeCount(text, MaxKeyRepeat);

    /// <summary>自动点击次数：0 = 无限，非法输入（空/负数）回落 0，上限 99999。</summary>
    private static int NormalizeMouseRepeat(string text) => NormalizeCount(text, MaxMouseRepeat);

    private static int NormalizeCount(string text, int max)
    {
        if (!int.TryParse(text?.Trim(), out int v) || v < 0) return 0;
        return Math.Clamp(v, 0, max);
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

    /// <summary>把指定行滚到可见区并聚焦其键名框（校验不通过时用来引导用户）。</summary>
    private void FocusKeyBox(RowControls row)
    {
        int idx = _rows.IndexOf(row);
        if (idx < 0) return;
        int top = idx * RowH;
        if (top < scrollBar.Offset)
            scrollBar.SetOffset(top);
        else if (top + RowH > scrollBar.Offset + pnlRows.Height)
            scrollBar.SetOffset(Math.Min(top + RowH - pnlRows.Height, scrollBar.MaxOffset()));
        row.TxtKey.Inner.Focus();
        row.TxtKey.Inner.SelectAll();
    }

    // ---------- 输入校验 ----------

    /// <summary>键名键入：只放行 A-Z / 0-9；已有一个字符时若无法组成合法两字符键名，用新字符整体替换。</summary>
    private void KeyBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (sender is not TextBox txt) return;
        char c = e.KeyChar;
        if (char.IsControl(c)) return; // 退格等
        if (c >= 'a' && c <= 'z') c = char.ToUpperInvariant(c);

        if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            e.Handled = true; // 符号、空格、中文一律吞掉
            UpdateStatus("键名只能是 A-Z、0-9；小键盘数字用 N0-N9");
            return;
        }

        // 唯一合法两字符形态是「N + 数字」（小键盘），其余情况一律整体替换而非拼接
        string cur = txt.SelectionLength > 0 ? "" : txt.Text;
        bool append = cur.Length == 1 && cur[0] == 'N' && c is >= '0' and <= '9';
        if (append) txt.SelectionStart = txt.TextLength; // 光标可能停在 N 之前，强制追加到末尾
        if (cur.Length > 0 && !append)
        {
            _keyFixing = true;
            txt.Clear();
            _keyFixing = false;
        }
        e.KeyChar = c;
    }

    private void KeyBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox txt) return;
        int code = (int)e.KeyCode;

        // 小键盘数字：直接存成 N0-N9，与主键盘数字（VK 0x30-0x39）区分开
        if (code >= (int)Keys.NumPad0 && code <= (int)Keys.NumPad9)
        {
            _keyFixing = true;
            txt.Text = "N" + (code - (int)Keys.NumPad0);
            txt.SelectionStart = txt.TextLength;
            _keyFixing = false;
            e.Handled = true;
            e.SuppressKeyPress = true;
            KeyBox_TextChanged(txt, EventArgs.Empty); // 上面屏蔽了 TextChanged，这里补跑一次校验与去重
            if (!_loading) SaveConfig();
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            txt.Clear(); // Delete 清空该行键名
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void KeyBox_TextChanged(object? sender, EventArgs e)
    {
        if (_keyFixing || sender is not TextBox txt) return;
        if (txt.Text.Length == 0) return;

        // 规范化为大写（含粘贴进来的小写字符）
        string value = txt.Text.Trim().ToUpperInvariant();
        if (value != txt.Text)
        {
            _keyFixing = true;
            txt.Text = value;
            txt.SelectionStart = value.Length;
            _keyFixing = false;
        }

        // 非法形态（如粘贴 "X5"、"1A"）→ 清空
        if (KeyTextToVk(value) < 0)
        {
            _keyFixing = true;
            txt.Clear();
            _keyFixing = false;
            UpdateStatus("键名不合法，已清空（允许 A-Z、0-9，小键盘用 N0-N9）");
            return;
        }

        if (value == "N") return; // 可能是输入 N5 的中间态，重复检查留到开启时

        // 与其他行键名重复 → 直接置空本行
        int vk = KeyTextToVk(value);
        foreach (var r in _rows)
        {
            if (ReferenceEquals(r.TxtKey.Inner, txt)) continue;
            if (KeyTextToVk(r.TxtKey.Text) == vk)
            {
                _keyFixing = true;
                txt.Clear();
                _keyFixing = false;
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

    /// <summary>页面标题 + 右侧间隔范围说明（按键页与鼠标页共用同一套字号与对齐）。</summary>
    private static void AddPageTitle(Control page, string title, string range)
    {
        var titleSize = RowTextSize(title, 10.5f);
        var rangeSize = RowTextSize(range, 8f);
        page.Controls.Add(new Label
        {
            Text = title,
            Location = new Point(16, 11),
            AutoSize = true,
            ForeColor = AntStyle.TextPri,
            Font = new Font(AntStyle.FontName, 10.5f)
        });
        page.Controls.Add(new Label
        {
            Text = range,
            Location = new Point(16 + titleSize.Width + 12, 11 + (titleSize.Height - rangeSize.Height) / 2),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = new Font(AntStyle.FontName, 8f)
        });
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

    /// <summary>行内文字实测宽度：Label.AutoSize 要挂到父控件后才生效，布局阶段必须自己量。</summary>
    private static int RowTextWidth(string text, bool bold = false) => RowTextWidth(text, 9f, bold);

    /// <summary>指定字号的实测尺寸（标题这类非常规字号用，去掉 MeasureText 自带的左右留白）。</summary>
    private static Size RowTextSize(string text, float size, bool bold = false)
    {
        using var font = bold ? new Font(AntStyle.FontName, size, FontStyle.Bold) : new Font(AntStyle.FontName, size);
        return TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
    }

    private static int RowTextWidth(string text, float size, bool bold = false) => RowTextSize(text, size, bold).Width;

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

    /// <summary>
    /// 键名文本 → 虚拟键码。单字符 A-Z / 0-9 直接取字符码（VK_A=0x41、VK_0=0x30）；
    /// 两字符 N0-N9 表示小键盘数字（VK_NUMPAD0=0x60）。返回 -1 表示非法。
    /// </summary>
    private static int KeyTextToVk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        string s = text.Trim().ToUpperInvariant();
        if (s.Length == 1)
        {
            char c = s[0];
            return c is >= 'A' and <= 'Z' or >= '0' and <= '9' ? c : -1;
        }
        if (s.Length == 2 && s[0] == 'N' && s[1] is >= '0' and <= '9')
            return 0x60 + (s[1] - '0');
        return -1;
    }

    /// <summary>读取配置时规范化键名：大写；非法（旧版本或手改坏的值）一律回落为空，等用户重填。</summary>
    private static string SanitizeKeyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string s = text.Trim().ToUpperInvariant();
        return KeyTextToVk(s) >= 0 ? s : "";
    }

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
            r.SelStop.Enabled = !locked;
            r.NumCount.Enabled = !locked && r.SelStop.SelectedIndex == 1; // 仅自动连按有次数框
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
            r.NumValue.Enabled = !locked && r.SelStop.SelectedIndex == 1; // 仅"自动点击"有数值框
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

    /// <summary>
    /// 可接收焦点的 Tab 头容器。窗体启动/重新激活时 WinForms 会自动把焦点交给第一个可选控件，
    /// 若不接管就会落到第一个「按下 [ ] 键」输入框上（光标闪烁、像是被选中）。
    /// 让 Tab 头成为那个"第一个可选控件"，它不自绘焦点框，视觉上等于没有焦点。
    /// </summary>
    private sealed class TabHeaderPanel : Panel
    {
        public TabHeaderPanel() => SetStyle(ControlStyles.Selectable, true);
    }

    /// <summary>
    /// 页面切换用的白色蒙层：无边框、不在任务栏、不抢焦点、鼠标点击穿透。
    /// 只有顶层窗口能整体半透明（Form.Opacity 走分层窗口），普通子控件做不到。
    /// </summary>
    private sealed class FadeVeil : Form
    {
        public FadeVeil()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Opacity = VeilStartOpacity;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000; // 不抢焦点
                const int WS_EX_TRANSPARENT = 0x00000020; // 点击穿透，不挡下面的控件
                const int WS_EX_TOOLWINDOW = 0x00000080; // 不进 Alt+Tab
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }
}
