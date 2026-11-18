namespace DnfAutoFire;

// 热键槽位：注册注销、跨页互斥置灰、冲突回退、热键下拉构建。
public partial class MainForm : Form
{
    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        var handle = Handle; // 强制创建句柄

        // 四个热键同表处理：同一个键重复配置时只注册一次。
        // 否则后一个 RegisterHotKey 必然失败，会被误报成"被其他程序占用"。
        var defs = new (int Id, string Name, string Key, Action<string> Set)[]
        {
            (HOTKEY_ID_START, "连发", _config.StartHotkey, k => _config.StartHotkey = k),
            (HOTKEY_ID_MOUSE, "连点", _config.MouseStartHotkey, k => _config.MouseStartHotkey = k),
            (HOTKEY_ID_RECORD, "录制", _config.RecordHotkey, k => _config.RecordHotkey = k),
            (HOTKEY_ID_PLAY, "回放", _config.PlaybackHotkey, k => _config.PlaybackHotkey = k)
        };

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var duplicated = new List<string>();
        var occupied = new List<string>();
        var changed = new List<string>();

        // 备用键不能挑别的槽位原本配置的值，否则会把后面的槽位挤掉；
        // busy 记的是被别的程序占用的键，换键后也要避开
        var reserved = new HashSet<string>(
            defs.Select(d => (d.Key ?? "").Trim()).Where(k => k.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, name, key, set) in defs)
        {
            string k = (key ?? "").Trim();
            if (k.Length == 0) continue;

            if (owners.TryGetValue(k, out string? owner))
            {
                duplicated.Add($"{k}（{owner}/{name}）");
                continue;
            }
            if (!NativeMethods.RegisterHotKey(handle, id, 0, (uint)HotkeyStringToVk(k)))
            {
                busy.Add(k);
                string? alt = HotkeyFallback.Pick(reserved.Concat(owners.Keys).Concat(busy));
                if (alt == null || !NativeMethods.RegisterHotKey(handle, id, 0, (uint)HotkeyStringToVk(alt)))
                {
                    occupied.Add(k);
                    continue;
                }
                string from = k;
                k = alt;
                set(k);              // 内存里立刻生效，界面与配置文件稍后一起跟上
                changed.Add($"{name} {from}→{k}");
            }
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
            Logger.Info($"热键注册失败：{names}，且没有空闲的 F 键可换");
            UpdateStatus("警告：热键 " + names + " 注册失败，且没有空闲 F 键可换");
        }
        if (changed.Count > 0)
        {
            string desc = string.Join("、", changed);
            Logger.Info($"热键被占用，已自动改用：{desc}（已写回配置）");
            UpdateStatus("提示：热键被占用，已自动改用 " + desc);
            trayIcon.ShowBalloonTip(3000, "热键已自动调整", desc + "，已写回配置。", ToolTipIcon.Info);
            SyncHotkeyUI(); // 下拉框跟着新键走，其它槽位的置灰也要重算
            SaveConfig();   // 走正常保存链路，把换键结果落到 config.json
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

    /// <summary>
    /// 设置卡片跟随当前 Tab：按键页=连发热键 + 连发模式 + 键盘提示；
    /// 鼠标页=连点热键 + 鼠标提示（无模式概念）；录制页=录制热键 + 录制提示。
    /// </summary>
    private void SyncHotkeyUI()
    {
        bool mouse = _tabIndex == TabMouse;
        bool record = _tabIndex == TabRecord;
        // 录制/回放期间锁热键；当前 Tab 对应的引擎在跑时也要锁——否则切一次 Tab
        // 就把按键/鼠标运行的锁解开了（与 SetKeysUiLocked 的语义打架）
        bool locked = _recorder != null || _player != null
                      || (mouse ? _mouseEngine.Running : _engine.Running);

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

        // 卡片内纵向排布：按键页保持原来位置（下面还有一行说明），录制页两行居中，
        // 鼠标页只有一行也居中——都不带第三行小字
        int topLabel = 12, topCtl = 8, midLabel = 44, midCtl = 40;
        if (record) { topLabel = 20; topCtl = 16; midLabel = 52; midCtl = 48; }
        else if (mouse) { topLabel = 36; topCtl = 32; }
        lblHotkey.Top = topLabel;
        cboStartKey.Top = topCtl;
        lblMode.Top = midLabel;
        cboMode.Top = midCtl;
        cboPlayKey.Top = midCtl;

        // 只有按键页在卡片底部带一行说明（跟着「保留原生 / 拦截原生」变）
        lblModeTip.Visible = !record && !mouse;
        lblModeTip.Text = "";
        if (!record && !mouse) ApplyKeyModeTip(); // 按键页：说明跟着「保留原生 / 拦截原生」走

        RefreshHotkeyAvailability(); // 同一个热键不能配给两个功能：别的槽位用着的键在列表里置灰
    }

    private string HotkeyOf(string slot) => slot switch
    {
        "连点热键" => _config.MouseStartHotkey,
        "录制热键" => _config.RecordHotkey,
        "回放热键" => _config.PlaybackHotkey,
        _ => _config.StartHotkey
    };

    /// <summary>把「别的槽位已经在用」的键在本页的下拉框里置灰，排掉大半误选。</summary>
    private void RefreshHotkeyAvailability()
    {
        if (cboStartKey == null || cboPlayKey == null) return;
        BlockOtherSlots(cboStartKey, StartKeySlot());
        BlockOtherSlots(cboPlayKey, "回放热键");
    }

    private void BlockOtherSlots(AntSelect sel, string ownSlot)
    {
        var slots = HotkeySlots();
        sel.SetBlockedItems(slots.Where(s => s.Name != ownSlot).Select(s => s.Key));
    }

    /// <summary>
    /// 选到别的槽位已经在用的键：提示并退回原值。
    /// 列表里虽然已经置灰，但方向键、滚轮滚动、程序设值都能绕过置灰，这里兜住。
    /// </summary>
    private bool RevertHotkeyIfTaken(AntSelect sel, string slot)
    {
        string picked = (sel.Text ?? "").Trim();
        string mine = HotkeyOf(slot);
        if (picked.Length == 0 || string.Equals(picked, mine, StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var (name, key) in HotkeySlots())
        {
            if (name == slot || !string.Equals((key ?? "").Trim(), picked, StringComparison.OrdinalIgnoreCase)) continue;
            _hotkeySyncing = true;
            sel.Text = mine;
            _hotkeySyncing = false;
            UpdateStatus($"提示：{picked} 已被「{name}」占用，无法选择，已退回 {mine}");
            return true;
        }
        return false;
    }

    private AntSelect MakeHotkeySelect(Point loc)
    {
        var sel = new AntSelect { Location = loc, Width = 120 };
        sel.Inner.DropDownWidth = 120; // 弹出列表与框同宽
        for (int i = 1; i <= 12; i++) sel.Inner.Items.Add("F" + i);
        sel.SelectedIndex = 5; // F6
        return sel;
    }

    private static int HotkeyStringToVk(string s) => HotkeyFallback.ToVk(s);

    /// <summary>四个热键槽位：界面标签名 + 当前生效值（界面上按 Tab 只显示其中一到两个）。</summary>
    private (string Name, string Key)[] HotkeySlots() => new[]
    {
        ("连发热键", _config.StartHotkey),
        ("连点热键", _config.MouseStartHotkey),
        ("录制热键", _config.RecordHotkey),
        ("回放热键", _config.PlaybackHotkey)
    };

    /// <summary>「连发/连点/录制」那个热键下拉框当前对应哪个槽位（跟着 Tab 走）。</summary>
    private string StartKeySlot() => _tabIndex == TabRecord ? "录制热键" : _tabIndex == TabMouse ? "连点热键" : "连发热键";
}
