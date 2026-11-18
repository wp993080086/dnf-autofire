namespace KeyMouseFactory;

// 界面骨架与通用绘制：BuildUi、三个页面构建、Tab 绘制与动画、页内排版工具。
public partial class MainForm : Form
{
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
                lblTarget.Text = "当前控制：连按";
                btnMain.Text = _engine.Running ? "关闭连发" : "开启连发";
                btnMain.Enabled = true;
                btnMain.Danger = _engine.Running;
                break;
        }
        RefreshStatus(); // 状态栏同时展示各引擎状态
        UpdateTrayText();
    }

    // ---------- UI 构建 ----------

    private void BuildUi()
    {
        // 主区域（Tab + 配置页）：三个区域（主区/启动区/设置区）以 2px 深色边框分隔，无留白卡片
        var pnlMain = new AntPanel { Location = new Point(2, 2), Size = new Size(636, 302), BackColor = Color.Transparent };

        // 自定义 Tab 头（AntD 风格：文字 + 蓝色下划线，下边框 1px 样式不变）
        tabHeader = new TabHeaderPanel { Location = Point.Empty, Size = new Size(636, 44), BackColor = Color.Transparent, TabStop = true, TabIndex = 0 };
        tabHeader.Paint += TabHeader_Paint;
        tabHeader.MouseClick += TabHeader_Click;
        tabHeader.MouseMove += TabHeader_Move;
        tabHeader.MouseLeave += (_, _) => { if (_tabHover != -1) { _tabHover = -1; tabHeader.Invalidate(); } };

        // Tab 栏右侧空白处：小号浅灰的版本号与作者，可点击弹出关于窗口
        var lblVersion = new Label
        {
            Name = "lblVersionAbout",
            // ProductVersion 形如 "1.1.0+<commit>"，只取主版本号
            Text = $"v{Application.ProductVersion.Split('+')[0]} · 鹏多多",
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = AntStyle.GetFont(8f),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        // 点版本/作者不误切 Tab：Label 在三个页签矩形之外，HitTab 返回 -1；
        // MouseClick 不冒泡到 tabHeader，这里直接弹关于窗口
        lblVersion.Click += (_, _) =>
        {
            using var about = new AboutForm();
            about.ShowDialog(this);
        };
        tabHeader.Controls.Add(lblVersion);
        lblVersion.Location = new Point(tabHeader.Width - lblVersion.Width - 16, (44 - lblVersion.Height) / 2);

        // 页面容器：各页面叠放在同一位置，只显示当前 Tab 对应的那一页
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i] = new AntPanel
            {
                Location = new Point(0, 44),
                Size = new Size(636, 258),
                BackColor = Color.Transparent,
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

        // 启动区（左下）：按键/鼠标页是一个主按钮；录制页换成「开始录制 / 开始回放」两个等宽按钮。
        // 设置区（右下）内容不多，分隔线右移 100px：启动区 394、设置区 240，按钮能铺开、右侧也不空
        var pnlRun = new AntPanel { Location = new Point(2, 306), Size = new Size(394, 92), BackColor = Color.Transparent };
        const int RunBtnW = 175;              // (366 - 间距 16) / 2，两个按钮均分同一块宽度
        const int RunBtnGap = 16;
        btnMain = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "开启连发",
            Location = new Point(14, 6),
            Size = new Size(366, 40),
            Font = AntStyle.GetFont(10f)
        };
        btnRecord = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "● 开始录制",
            Location = new Point(14, 6),
            Size = new Size(RunBtnW, 40),
            Font = AntStyle.GetFont(10f),
            Visible = false
        };
        btnRecord.Click += (_, _) => ToggleRecording();
        btnPlay = new AntButton
        {
            Kind = AntButton.AntKind.Default,
            Text = "▶ 开始回放",
            Location = new Point(14 + RunBtnW + RunBtnGap, 6),
            Size = new Size(RunBtnW, 40),
            Font = AntStyle.GetFont(10f),
            Visible = false
        };
        btnPlay.Click += (_, _) => TogglePlayback();
        lblTarget = new Label
        {
            Text = "当前控制：连按",
            Location = new Point(14, 50),
            AutoSize = true,
            ForeColor = AntStyle.Primary,
            Font = AntStyle.GetFont(9f, FontStyle.Bold)
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
        // 鼠标页编辑连点热键（默认 F8）；两个热键同时全局生效。
        // 分隔线右移后只剩 240 宽：标签 14 起、下拉 86 起 120 宽，右侧仍留 34 的余量给说明文字
        var pnlSet = new AntPanel { Location = new Point(398, 306), Size = new Size(240, 92), BackColor = Color.Transparent };
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
            if (RevertHotkeyIfTaken(cboStartKey, StartKeySlot())) return;
            SaveConfig();
            RegisterHotkeys();
            RefreshHotkeyAvailability(); // 值变了，另一页的置灰跟着变
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
            if (RevertHotkeyIfTaken(cboPlayKey, "回放热键")) return;
            SaveConfig();
            RegisterHotkeys();
            RefreshHotkeyAvailability();
        };

        // 模式说明（跟着下拉框当前选项变）
        lblModeTip = new Label
        {
            Text = "",
            Location = new Point(14, 70),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = AntStyle.GetFont(8f)
        };

        pnlSet.Controls.AddRange(new Control[] { lblHotkey, lblMode, cboStartKey, cboMode, cboPlayKey, lblModeTip });
        Controls.Add(pnlSet);
        ApplyKeyModeTip();

        // 点击非输入区域时移除输入框焦点样式（面板类控件点击不会自动夺走焦点）
        AttachClearFocus(Controls);
    }

    /// <summary>区域分隔：仅在模块相邻处画 2px 浅灰线，无外框。此处在下半纯白区，沿用较浅的 Split。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var line = new SolidBrush(AntStyle.Split);
        // 主区与底部两区之间的水平分隔（2px）
        g.FillRectangle(line, 0, 304, Width, 2);
        // 启动区与设置区之间的垂直分隔（2px）：比原来右移 100，给左侧启动区更多空间
        g.FillRectangle(line, 396, 304, 2, Height - 304);
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
        // 这条线压在顶部渐变(#E8F2FF→白)上，普通 Split(#F0F0F0)会融进背景看不清，用更深的 SplitStrong
        using (var line = new Pen(AntStyle.SplitStrong))
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
        tabHeader.Invalidate();
        RefreshRunCard(); // 主按钮与"当前控制"标识跟随 Tab 切换
        SyncHotkeyUI();   // 热键下拉框跟随 Tab
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

    private void BuildKeysPage(Control page)
    {
        AddPageTitle(page, "按键配置", $"间隔范围 {MinMs}-{MaxMs}ms，步进 {KeyStepMs}");

        pnlRows = new AntPanel
        {
            Location = new Point(16, 40),
            Size = new Size(RowWidth + 12, 210),
            BackColor = Color.Transparent
        };
        scrollBar = new FlatScrollBar
        {
            Location = new Point(pnlRows.Width - 8, 0),
            Size = new Size(8, pnlRows.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        scrollBar.OffsetChanged += off => pnlContent.Top = -off;

        pnlContent = new AntPanel
        {
            Location = new Point(0, 0),
            Size = new Size(RowWidth, RowH),
            BackColor = Color.Transparent
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

    /// <summary>鼠标连点页：固定左/右/中/X1/X2 五行 + 停止条件下拉（松手停止 / 自动点击，默认松手停止）。</summary>
    private void BuildMousePage(Control page)
    {
        AddPageTitle(page, "鼠标连点", $"间隔范围 {MouseMinMs}-{MouseMaxMs}ms，步进 {MouseStepMs}");

        // 按钮名占固定列宽（取中文名宽度）：X1/X2 是 ASCII 比两个汉字窄，
        // 若按各自文字宽度排，后两行会整体左移、行尾短一截。五行左边缘统一到同一列。
        int btnNameW = Math.Max(RowTextWidth("左键", bold: true), RowTextWidth("X1", bold: true));

        foreach (var (button, label, y) in new[]
                 {
                     ("L", "左键", 44), ("R", "右键", 82), ("M", "滚轮", 120),
                     ("X1", "X1", 158), ("X2", "X2", 196)
                 })
        {
            var row = new MouseRow { Button = button };
            var panel = new AntPanel { Location = new Point(16, y), Size = new Size(RowWidth, RowH), BackColor = Color.Transparent };
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
            int x = 58 + btnNameW + 6;
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

        // 五行配置占满页面，说明压到最下面一行：简短、不折行，避免挤掉 X1/X2 两行
        page.Controls.Add(new Label
        {
            Text = "松手停止：按住键连点，松手即停 ｜ 自动点击：按次数自动，0=无限 ｜ 滚轮=中键，X1/X2=侧键",
            Location = new Point(16, 234),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = AntStyle.GetFont(8f)
        });
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
            Font = AntStyle.GetFont(10.5f)
        });
        page.Controls.Add(new Label
        {
            Text = range,
            Location = new Point(16 + titleSize.Width + 12, 11 + (titleSize.Height - rangeSize.Height) / 2),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = AntStyle.GetFont(8f)
        });
    }

    /// <summary>行内静态文字：保持 AutoSize（不裁切），用 MeasureText 把文字垂直中心对齐到 28px 控件带中线。</summary>
    private static Label MakeRowText(string text, int x, Color? color = null, bool bold = false)
    {
        var font = AntStyle.GetFont(9f, bold ? FontStyle.Bold : FontStyle.Regular);
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

    /// <summary>指定字号的实测尺寸（标题这类非常规字号用，去掉 MeasureText 自带的左右留白）。</summary>
    private static Size RowTextSize(string text, float size, bool bold = false)
    {
        var font = AntStyle.GetFont(size, bold ? FontStyle.Bold : FontStyle.Regular);
        return TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
    }

    private static int Lerp(int a, int b, float t) => a + (int)Math.Round((b - a) * t);

    /// <summary>行内文字实测宽度：Label.AutoSize 要挂到父控件后才生效，布局阶段必须自己量。</summary>
    private static int RowTextWidth(string text, bool bold = false) => RowTextWidth(text, 9f, bold);

    private static int RowTextWidth(string text, float size, bool bold = false) => RowTextSize(text, size, bold).Width;
}
