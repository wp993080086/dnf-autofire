namespace DnfAutoFire;

#if DEBUG
// 调试入口：只由 Program.Debug.cs 的调试命令行调用，Release 构建里不参与编译。
public partial class MainForm : Form
{
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

    /// <summary>调试入口：--mouselayout 打印鼠标页各控件实际边界，核对行内排布。</summary>
    internal void DebugDumpMouseLayout()
    {
        foreach (var r in _mouseRows)
            foreach (Control c in r.RowPanel.Controls)
                Console.WriteLine($"{r.Button} {c.GetType().Name} '{c.Text}' loc={c.Location} size={c.Size} right={c.Right}");
    }

    /// <summary>
    /// 调试入口：--hotkeytest 核对热键互斥：别的槽位在用的键是否置灰、
    /// 硬选到那个键是否被退回（方向键/程序设值这条兜底路径）。
    /// </summary>
    internal void DebugHotkeyTest()
    {
        // 先看启动态（按键页，没切过 Tab）——这里是「下拉里没置灰」问题的现场
        Console.WriteLine("[hotkeytest] 启动态（按键页，未切 Tab）：");
        Dump("连发热键", cboStartKey);
        Dump("回放热键", cboPlayKey);

        DebugShowTab(TabRecord);
        Application.DoEvents();

        void Dump(string name, AntSelect sel)
        {
            var blocked = sel.Inner.Items.Cast<object>()
                .Select(o => o?.ToString() ?? "")
                .Where(sel.IsBlocked);
            Console.WriteLine($"[hotkeytest] {name}：当前={sel.Text} 置灰=[{string.Join(",", blocked)}]");
        }

        Console.WriteLine($"[hotkeytest] 配置：连发={_config.StartHotkey} 连点={_config.MouseStartHotkey} "
            + $"录制={_config.RecordHotkey} 回放={_config.PlaybackHotkey}");
        Dump("录制热键", cboStartKey);
        Dump("回放热键", cboPlayKey);

        string TrySelect(AntSelect sel, string key)
        {
            int idx = sel.Inner.Items.IndexOf(key);
            if (idx < 0) return "无此选项";
            sel.SelectedIndex = idx;
            Application.DoEvents();
            return sel.Text;
        }

        string before = cboStartKey.Text;
        Console.WriteLine($"[hotkeytest] 硬选「连发热键」在用的 {_config.StartHotkey} → 结果={TrySelect(cboStartKey, _config.StartHotkey)}（应退回 {before}）");
        Console.WriteLine($"[hotkeytest] 选空闲键 F5 → 结果={TrySelect(cboStartKey, "F5")}（应为 F5，且此时回放热键的 F5 应置灰）");
        Dump("回放热键(选完 F5)", cboPlayKey);
        Console.WriteLine($"[hotkeytest] 选回 {before} → 结果={TrySelect(cboStartKey, before)}");
        Dump("录制热键(复位)", cboStartKey);
    }

    /// <summary>
    /// 调试入口：--cursortest 把光标依次停在各个控件上，回报光标形状：
    /// 可点的按钮/选择器/复选框=手型，输入框=I 型，禁用的一律=禁止，纯文字=箭头。
    /// </summary>
    internal void DebugCursorProbe()
    {
        Activate();
        BringToFront();
        Application.DoEvents();
        var old = Cursor.Position;

        string Probe(Control c, string name, bool disable = false)
        {
            if (!c.Visible)
                return $"{name}: 不可见，跳过";
            bool was = c.Enabled;
            if (disable) c.Enabled = false;
            Application.DoEvents();
            var p = c.PointToScreen(new Point(Math.Min(c.Width / 2, 20), c.Height / 2));
            Cursor.Position = p;
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(20); }
            var h = (Cursor.Current ?? Cursors.Default).Handle;
            string cur = h == Cursors.No.Handle ? "No"
                : h == Cursors.Hand.Handle ? "Hand"
                : h == Cursors.IBeam.Handle ? "IBeam"
                : "Default";
            c.Enabled = was;
            Application.DoEvents();
            return $"{name}{(disable ? "(禁用)" : "")}: → {cur}";
        }

        DebugShowTab(TabKeys);
        Application.DoEvents();
        Console.WriteLine("[cursortest] —— 按键页");
        foreach (var line in new[]
        {
            Probe(btnMain, "主按钮(开启连发)"),
            Probe(_rows[0].Chk, "启用复选框"),
            Probe(_rows[0].TxtKey, "键名输入框"),
            Probe(_rows[0].TxtMs, "间隔数字框(文本区)"),
            Probe(_rows[0].SelStop, "停止条件下拉"),
            Probe(btnAdd, "新增一行"),
            Probe(btnMain, "主按钮", disable: true),
            Probe(_rows[0].TxtKey, "键名输入框", disable: true),
            Probe(_rows[0].SelStop, "停止条件下拉", disable: true)
        })
            Console.WriteLine("[cursortest] " + line);

        // 补满 5 行：第 3 行起才有「删除」，到上限「新增一行」会置灰
        while (_rows.Count < MaxRows) DebugAddEmptyRow();
        Application.DoEvents();
        foreach (var line in new[]
        {
            Probe(_rows[2].LnkDel, "删除按钮(第3行)"),
            Probe(_rows[2].LnkDel, "删除按钮(第3行)", disable: true),
            Probe(btnAdd, "新增一行(已到上限)")
        })
            Console.WriteLine("[cursortest] " + line);

        // 热键下拉展开后逐项：置灰项要「禁止」光标，可选项目手型
        string ProbeItem(AntSelect sel, string label, string item)
        {
            int idx = sel.Inner.Items.IndexOf(item);
            if (idx < 0) return $"{label}[{item}]: 无此项";
            var pt = sel.DebugOpenAndLocateItem(idx);
            if (pt == null) return $"{label}[{item}]: 取不到选项位置";
            Cursor.Position = pt.Value;
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(20); }
            var h = (Cursor.Current ?? Cursors.Default).Handle;
            string cur = h == Cursors.No.Handle ? "No"
                : h == Cursors.Hand.Handle ? "Hand"
                : h == Cursors.IBeam.Handle ? "IBeam"
                : "Default";
            return $"{label}[{item}] {(sel.IsBlocked(item) ? "置灰" : "可选")}: → {cur}";
        }

        Console.WriteLine("[cursortest] —— 按键页：展开「连发热键」下拉");
        foreach (var line in new[]
        {
            ProbeItem(cboStartKey, "连发热键", "F9"),   // 被录制热键占用 → 置灰
            ProbeItem(cboStartKey, "连发热键", "F8"),   // 被连点热键占用 → 置灰
            ProbeItem(cboStartKey, "连发热键", "F1"),   // 空闲 → 手型
            ProbeItem(cboStartKey, "连发热键", "F12")   // 空闲 → 手型
        })
            Console.WriteLine("[cursortest] " + line);
        cboStartKey.Inner.DroppedDown = false;
        Application.DoEvents();

        DebugShowTab(TabMouse);
        Application.DoEvents();
        Console.WriteLine("[cursortest] —— 鼠标页：展开「连点热键」下拉");
        foreach (var line in new[]
        {
            ProbeItem(cboStartKey, "连点热键", "F6"),  // 被连发热键占用 → 置灰
            ProbeItem(cboStartKey, "连点热键", "F2")   // 空闲 → 手型
        })
            Console.WriteLine("[cursortest] " + line);
        cboStartKey.Inner.DroppedDown = false;
        Application.DoEvents();

        DebugShowTab(TabRecord);
        Application.DoEvents();
        Console.WriteLine("[cursortest] —— 录制页");
        foreach (var line in new[]
        {
            Probe(btnPlay, "开始回放(未选方案)"),
            Probe(btnRecord, "开始录制"),
            Probe(cboStartKey, "录制热键下拉"),
            Probe(cboPlayMode, "回放模式下拉", disable: true),
            Probe(numPlayGap, "间隔数字框", disable: true),
            Probe(lblRecStatus, "提示文字")
        })
            Console.WriteLine("[cursortest] " + line);

        Cursor.Position = old;
    }

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

    /// <summary>调试入口：--screenshot 模拟新增行。</summary>
    internal void DebugAddEmptyRow() => AddEmptyRow();

    /// <summary>调试入口：--screenshot … mouse 切到鼠标 Tab 再截图。</summary>
    internal void DebugShowMouseTab() => DebugShowTab(TabMouse);

    /// <summary>调试入口：--screenshot … record 切到录制 Tab 再截图。</summary>
    internal void DebugShowRecordTab() => DebugShowTab(TabRecord);

    /// <summary>
    /// 调试入口：验证配置防抖。
    /// 连改 5 次值后立刻读文件（应仍是旧值，说明没立即写盘），等防抖到期再读（应已更新），
    /// 最后设成 finalGap 并立刻关窗，验证退出兜底没丢最后一次改动。
    /// </summary>
    internal void DebugSaveTest(int finalGap, int waitMs)
    {
        DebugShowTab(TabRecord);
        string CfgPath() => System.IO.Path.Combine(AppPaths.DataDir, "config.json");
        string? ReadGap()
        {
            try
            {
                if (!System.IO.File.Exists(CfgPath())) return "(无文件)";
                var node = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(CfgPath()));
                return node?["PlayGapSec"]?.ToString() ?? "(无字段)";
            }
            catch (Exception ex) { return "(读失败:" + ex.GetType().Name + ")"; }
        }

        Logger.Info($"[savetest] 起始文件值={ReadGap()}");
        for (int i = 1; i <= 5; i++) numPlayGap.SetValue(i);
        Application.DoEvents();
        Logger.Info($"[savetest] 连改 5 次后（应仍是旧值）文件值={ReadGap()} 挂起={_pendingSave != null}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < waitMs)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Logger.Info($"[savetest] 等待 {waitMs}ms 后（防抖应已落盘）文件值={ReadGap()} 挂起={_pendingSave != null}");

        numPlayGap.SetValue(finalGap);
        Logger.Info($"[savetest] 设为 {finalGap} 后立即关窗，挂起={_pendingSave != null}");
        // 交给消息循环关窗：确保 OnFormClosing 的兜底落盘真的跑到
        BeginInvoke(new Action(Close));
    }

    /// <summary>
    /// 调试入口：--keycaptest 逐个喂物理按键给键名框，验证捕获写入的键名是否符合预期。
    /// 单字符 A-Z / 0-9 应交给 KeyPress 路径（KeyDown 不写入也不拦截），其余键在这里整体写入。
    /// </summary>
    internal void DebugKeyCaptureTest()
    {
        bool oldLoading = _loading;
        _loading = true; // 别让测试写 config.json
        int bad = 0;
        try
        {
            var txt = _rows[0].TxtKey.Inner;

            // 应当整体写入的键：空格、小键盘数字、符号键
            var direct = new (Keys Key, string Expect)[]
            {
                (Keys.Space, "SPC"),
                (Keys.NumPad0, "N0"), (Keys.NumPad5, "N5"), (Keys.NumPad9, "N9"),
                (Keys.Oem1, ";"), (Keys.Oemplus, "="), (Keys.Oemcomma, ","), (Keys.OemMinus, "-"),
                (Keys.OemPeriod, "."), (Keys.OemQuestion, "/"), (Keys.Oemtilde, "`"),
                (Keys.OemOpenBrackets, "["), (Keys.OemPipe, "\\"), (Keys.OemCloseBrackets, "]"),
                (Keys.OemQuotes, "'"),
            };
            foreach (var (key, expect) in direct)
            {
                txt.Clear();
                KeyBox_KeyDown(txt, new KeyEventArgs(key));
                string got = txt.Text;
                if (got != expect) { bad++; Console.WriteLine($"[cap] BAD {key,-12} 期望 {expect} 实际「{got}」"); }
                else Console.WriteLine($"[cap] ok  {key,-12} → {got}");
            }

            // 单字符 A-Z / 0-9：KeyDown 不能拦截也不能写入，留给 KeyPress
            foreach (Keys key in new[] { Keys.A, Keys.Z, Keys.D0, Keys.D9 })
            {
                txt.Clear();
                var ev = new KeyEventArgs(key);
                KeyBox_KeyDown(txt, ev);
                if (ev.Handled || txt.Text.Length != 0) { bad++; Console.WriteLine($"[cap] BAD {key} 不该在 KeyDown 里处理"); }
            }

            // 已撤掉的键（F 键 / 方向键 / 编辑键）与不支持的键：不写入、也不拦截，交给文本框默认行为
            var ignored = new[]
            {
                Keys.F1, Keys.F5, Keys.F12, Keys.Up, Keys.Down, Keys.Left, Keys.Right,
                Keys.Tab, Keys.Enter, Keys.Escape, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
                Keys.Insert, Keys.Divide, Keys.Multiply, Keys.CapsLock, Keys.LShiftKey, Keys.ControlKey, Keys.Menu,
                Keys.Back, // 退格必须保留删除语义
            };
            foreach (Keys key in ignored)
            {
                txt.Clear();
                var ev = new KeyEventArgs(key);
                KeyBox_KeyDown(txt, ev);
                if (ev.Handled || txt.Text.Length != 0) { bad++; Console.WriteLine($"[cap] BAD {key} 不该被拦截，实际「{txt.Text}」"); }
            }

            // Delete 仍是清空
            txt.Text = "SPC";
            KeyBox_KeyDown(txt, new KeyEventArgs(Keys.Delete));
            if (txt.Text.Length != 0) { bad++; Console.WriteLine("[cap] BAD Delete 未清空"); }

            Console.WriteLine(bad == 0 ? "[cap] PASS" : $"[cap] FAIL（{bad} 处异常）");
        }
        finally
        {
            _loading = oldLoading;
        }
    }

    /// <summary>调试入口：打印托盘菜单项，确认「打开日志」等出口都在。</summary>
    internal void DebugDumpTrayMenu()
    {
        foreach (ToolStripItem item in trayMenu.Items)
            Console.WriteLine($"[tray] {item.Text}");
    }

    /// <summary>调试入口：模拟注入被拦截，验证事件幂等与状态栏/托盘告警文案。</summary>
    internal void DebugTestBlocked()
    {
        const System.Reflection.BindingFlags bind =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var f = typeof(AutoFireEngine).GetField("_blocked", bind)!;
        var mark = typeof(AutoFireEngine).GetMethod("MarkBlocked", bind)!;
        int fired = 0;
        void Count() => fired++;
        _engine.Blocked += Count;
        mark.Invoke(_engine, null);   // 触发一次：置位 + 发事件 + 走通知链路
        mark.Invoke(_engine, null);   // 第二次应被 _blocked 挡住，不重复弹气泡
        Console.WriteLine($"[blocked] 事件触发={fired}次（应1） 状态={f.GetValue(_engine)}（应True）");
        Application.DoEvents();       // 泵 BeginInvoke，让状态栏/托盘/气泡回调真正执行
        Application.DoEvents();
        Console.WriteLine($"[blocked] 状态栏={lblStatus.Text} 前景={lblStatus.ForeColor}");
        Console.WriteLine($"[blocked] 托盘={trayIcon.Text}");
        f.SetValue(_engine, false);   // 复位，避免影响后续
        _engine.Blocked -= Count;
    }

    /// <summary>调试入口：读 Tab 头实际样式位，确认双缓冲已生效（hover 不闪）。</summary>
    internal void DebugDumpTabStyle()
    {
        var getStyle = typeof(Control).GetMethod("GetStyle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        bool Read(ControlStyles s) => (bool)getStyle.Invoke(tabHeader, new object[] { s })!;
        Console.WriteLine($"[tab] AllPaintingInWmPaint={Read(ControlStyles.AllPaintingInWmPaint)} " +
                          $"OptimizedDoubleBuffer={Read(ControlStyles.OptimizedDoubleBuffer)} " +
                          $"UserPaint={Read(ControlStyles.UserPaint)} " +
                          $"Selectable={Read(ControlStyles.Selectable)}");
    }
}
#endif
