namespace KeyMouseFactory;

// 鼠标页：连点引擎启停、按/停模式描述、界面锁与数值归一。
public partial class MainForm : Form
{
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

    private static string DescribeStopMode(MouseClickEngine.StopMode mode, int value) => mode switch
    {
        MouseClickEngine.StopMode.Auto => value > 0 ? $"自动点击 {value} 次" : "自动点击 一直点",
        _ => "松手停止"
    };

    /// <summary>配置行按钮标识 → VK 键码（L=左键 R=右键 M=中键 X1/X2=侧键）。</summary>
    internal static int MouseButtonVk(string button) => button switch
    {
        "R" => NativeMethods.VK_RBUTTON,
        "M" => NativeMethods.VK_MBUTTON,
        "X1" => NativeMethods.VK_XBUTTON1,
        "X2" => NativeMethods.VK_XBUTTON2,
        _ => NativeMethods.VK_LBUTTON
    };

    internal static string MouseButtonName(int vk) => vk switch
    {
        NativeMethods.VK_RBUTTON => "右键",
        NativeMethods.VK_MBUTTON => "滚轮",
        NativeMethods.VK_XBUTTON1 => "侧键 X1",
        NativeMethods.VK_XBUTTON2 => "侧键 X2",
        _ => "左键"
    };

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

    /// <summary>鼠标连点间隔钳制：100~10000ms，非法输入回落默认 300。</summary>
    private static int NormalizeMouseMs(string text)
    {
        if (!int.TryParse(text?.Trim(), out int v) || v <= 0) return MouseDefaultMs;
        return Math.Clamp(v, MouseMinMs, MouseMaxMs);
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

    /// <summary>自动点击次数：0 = 无限，非法输入（空/负数）回落 0，上限 99999。</summary>
    private static int NormalizeMouseRepeat(string text) => NormalizeCount(text, MaxMouseRepeat);
}
