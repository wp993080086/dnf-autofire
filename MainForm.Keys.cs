namespace DnfAutoFire;

// 按键页：连发引擎启停与目标收集、按键配置行的构建与行内编辑。
public partial class MainForm : Form
{
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

    private void AddRow(string key, int ms, bool enabled, string stopMode = "hold", int stopValue = 0)
    {
        var row = new RowControls();
        var panel = new AntPanel { Size = new Size(RowWidth, RowH), BackColor = Color.White };
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
        row.LnkDel = new AntButton
        {
            Kind = AntButton.AntKind.Text,
            Danger = true,
            Text = "删除",
            Location = new Point(RowWidth - 48, 3), // 初始落位（真正位置由 ApplyKeyStopMode 按当前模式算）
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
        // 挂到行里之后 Label 才有实测宽度：此刻再收口一次，顺带把「删除」摆到内容后面
        ApplyKeyStopMode(row, stopMode, stopValue);

        _rows.Add(row);
    }

    /// <summary>
    /// 按停止条件模式调整次数框：auto=显示（0-99999，0 表示无限），hold=隐藏（连「次」一起）。
    /// 「删除」跟着当前内容的尾巴走（auto 跟在「次」后、hold 跟在停止条件后），不固定在行尾。
    /// </summary>
    private void ApplyKeyStopMode(RowControls r, string mode, int value)
    {
        bool auto = mode == "auto";
        r.NumCount.Visible = auto;
        r.LblCount.Visible = auto;
        r.NumCount.Enabled = auto && !_engine.Running;
        r.LnkDel.Left = auto ? r.LblCount.Right + 6 : r.SelStop.Right + 8;
        if (!auto) return;
        r.NumCount.Min = 0;
        r.NumCount.Max = MaxKeyRepeat;
        r.NumCount.Step = 1;
        r.NumCount.SetValue(Math.Clamp(value, 0, MaxKeyRepeat));
    }

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
        AddRow("", MinMs, false);
        RelayoutRows();
        UpdateRowButtons();
        SetKeysUiLocked(_engine.Running);
        RefreshScroll();
        scrollBar.SetOffset(scrollBar.MaxOffset());
        _rows[^1].TxtKey.Inner.Focus();
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

    private static int NormalizeMs(string text)
    {
        if (!int.TryParse(text?.Trim(), out int v) || v <= 0) return MinMs;
        return ClampMs(v); // 允许 1~5000 任意整数（步进按钮仍按 50 步进）
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

    /// <summary>自动连按次数：0 = 无限，非法输入（空/负数）回落 0，上限 99999。</summary>
    private static int NormalizeKeyRepeat(string text) => NormalizeCount(text, MaxKeyRepeat);

    private static int ClampMs(int v) => Math.Clamp(v, MinMs, MaxMs);
}
