namespace KeyMouseFactory;

/// <summary>
/// 点窗口 X 时的二选一：最小化到托盘继续运行 / 直接退出。
/// 自绘的小模态框，和主界面同一套 Ant 配色；Esc = 当作取消（保持窗口不动）。
/// </summary>
internal sealed class ClosePromptForm : Form
{
    public enum Choice { None, Minimize, Exit }

    /// <summary>用户的选择；直接关掉对话框（Esc / 标题栏 X）为 <see cref="Choice.None"/>。</summary>
    public Choice Result { get; private set; } = Choice.None;

    public ClosePromptForm(bool busy)
    {
        Text = "关闭 键鼠工厂";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(396, 158);
        BackColor = Color.White;
        Font = AntStyle.GetFont(9f);
        KeyPreview = true;

        var lblTitle = new Label
        {
            Text = "要关闭程序，还是最小化到托盘？",
            Location = new Point(20, 18),
            AutoSize = true,
            ForeColor = AntStyle.TextPri,
            Font = AntStyle.GetFont(10f, FontStyle.Bold)
        };

        var lblBody = new Label
        {
            Text = busy
                ? "当前有任务在运行：退出会立即停止连按、连点或录制回放。\r\n最小化到托盘后程序继续在后台运行，双击托盘图标可再打开。"
                : "退出会关闭程序；最小化到托盘后程序继续在后台运行。\r\n双击托盘图标可随时把窗口打开。",
            Location = new Point(20, 54),
            Size = new Size(356, 46),
            ForeColor = AntStyle.TextSec
        };

        var btnMin = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "最小化到托盘",
            Location = new Point(150, 110),
            Size = new Size(122, 32)
        };
        btnMin.Click += (_, _) => Pick(Choice.Minimize);

        var btnExit = new AntButton
        {
            Kind = AntButton.AntKind.Default,
            Danger = true,
            Text = "退出程序",
            Location = new Point(282, 110),
            Size = new Size(94, 32)
        };
        btnExit.Click += (_, _) => Pick(Choice.Exit);

        Controls.AddRange(new Control[] { lblTitle, lblBody, btnMin, btnExit });

        // 回车 = 最小化（安全动作），Esc = 取消，保持窗口原样
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) Pick(Choice.Minimize);
            else if (e.KeyCode == Keys.Escape) Pick(Choice.None);
        };
    }

    private void Pick(Choice choice)
    {
        Result = choice;
        Close();
    }

#if DEBUG
    /// <summary>调试入口用：不走鼠标直接选一个结果（验证关闭询问的接线）。</summary>
    internal void ChooseForTest(Choice choice) => Pick(choice);
#endif
}
