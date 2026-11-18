namespace DnfAutoFire;

// 录制与回放：方案列表、录制引擎、回放引擎与状态条。
public partial class MainForm : Form
{
    /// <summary>录制页：方案列表（单选 + 行内改名 + 删除）+ 开始/停止录制。</summary>
    private void BuildRecordPage(Control page)
    {
        // 与按键/鼠标页同一套标题样式：10.5pt 标题 + 8pt 灰字说明
        AddPageTitle(page, "鼠标录制回放",
            $"最多 {RecordStore.MaxProfiles} 个方案，单次录制上限 {MacroRecorder.MaxDurationMs / 60000} 分钟");

        // 方案列表：行高 30，正好 4 行，超出滚动（最多 10 个方案）。
        // 行内控件在行顶下 1px，列表放到 41 才能让首行内容顶部落在 42——与按键页
        // （列表 40 + 行内 2）完全一致，标题到内容的间距两页一样。
        pnlRecRows = new AntPanel
        {
            Location = new Point(16, 41),
            Size = new Size(RowWidth + 12, RecListHeight),
            BackColor = Color.Transparent
        };
        recScroll = new FlatScrollBar
        {
            Location = new Point(pnlRecRows.Width - 8, 0),
            Size = new Size(8, pnlRecRows.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        recScroll.OffsetChanged += off => pnlRecContent.Top = -off;
        pnlRecContent = new AntPanel
        {
            Location = new Point(0, 0),
            Size = new Size(RowWidth, RecListHeight),
            BackColor = Color.Transparent
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
            BackColor = Color.Transparent
        };
        pnlRecRows.Controls.Add(lblRecEmpty);
        lblRecEmpty.BringToFront();

        // 录制/回放两个按钮在左下角的启动区里并排（见 BuildUi 的 pnlRun），页面内不再放按钮

        // 回放行：模式 + 数值 + 单位 + 间隔（页面下半段，底部只留一行状态/提示）
        const int PlayRowY = 190;
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
            Max = Limits.PlayCountMax
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

        // 页底只留一行：空闲时是操作提示，录制/回放时变成状态（录制中… / 回放第几次）
        lblRecStatus = new Label
        {
            Location = new Point(16, 226),
            AutoSize = true,
            ForeColor = AntStyle.TextTertiary,
            Font = AntStyle.GetFont(8f)
        };
        page.Controls.Add(lblRecStatus);
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
        numPlayValue.Max = timed ? Limits.PlayMinuteMax : Limits.PlayCountMax;
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
            var row = new AntPanel
            {
                Location = new Point(0, i * RecRowH),
                Size = new Size(RowWidth, RecRowH),
                BackColor = Color.Transparent
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
            : _engine.InjectBlocked || _mouseEngine.InjectBlocked ? "DNF 连发 · 注入被拦截，请管理员运行"
            : _engine.Running || _mouseEngine.Running ? "DNF 连发 · 运行中"
            : "DNF 连发";
        if (text.Length > 60) text = text[..60];
        trayIcon.Text = text;
    }

    /// <summary>当前选中的方案（列表单选，<see cref="_selectedProfileId"/> 为空表示没选）。</summary>
    private RecordProfile? SelectedProfile() =>
        _profiles.FirstOrDefault(p => p.Id == _selectedProfileId);

    private static string FormatDuration(long ms) => PlayText.Duration(ms);

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
}
