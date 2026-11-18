namespace KeyMouseFactory;

/// <summary>
/// 关于窗口：点首页 Tab 栏右侧的版本/作者文案弹出，展示版本、作者、仓库与软件简介。
/// 复用 Ant 配色的小模态框；仓库地址用 LinkLabel，点击走系统默认浏览器打开。
/// </summary>
internal sealed class AboutForm : Form
{
    private const string RepoName = "key-mouse-factory";
    private const string RepoUrl = "https://github.com/wp993080086/key-mouse-factory";

    internal AboutForm()
    {
        Text = "关于 键鼠工厂";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(452, 258);
        BackColor = Color.White;
        Font = AntStyle.GetFont(9f);

        // 顶部标题
        Controls.Add(new Label
        {
            Text = "键鼠工厂",
            Location = new Point(20, 16),
            AutoSize = true,
            Font = AntStyle.GetFont(15f, FontStyle.Bold),
            ForeColor = AntStyle.Primary
        });

        // 版本（动态取程序集版本，与首页文案同源）+ 作者
        string version = Application.ProductVersion.Split('+')[0];
        Controls.Add(new Label
        {
            Text = $"版本 v{version}，作者 鹏多多",
            Location = new Point(20, 56),
            AutoSize = true,
            ForeColor = AntStyle.TextPri
        });

        // GitHub 仓库名
        Controls.Add(new Label
        {
            Text = $"GitHub 仓库名：{RepoName}",
            Location = new Point(20, 86),
            AutoSize = true,
            ForeColor = AntStyle.TextSec
        });

        // 仓库地址：可点击打开浏览器
        var lnkRepo = new LinkLabel
        {
            Text = RepoUrl,
            Location = new Point(20, 110),
            AutoSize = true,
            Font = AntStyle.GetFont(9f),
            LinkColor = AntStyle.Primary,
            ActiveLinkColor = AntStyle.PrimaryHover,
            VisitedLinkColor = AntStyle.Primary
        };
        lnkRepo.LinkClicked += (_, _) => OpenRepo();
        Controls.Add(lnkRepo);

        // 软件描述：手动分行，避免自动折行把最后一个字孤零零甩到第三行
        Controls.Add(new Label
        {
            Text = "按键连点、鼠标连点、鼠标轨迹录制回放，\r\n界面简洁，操作清晰，简单好用。",
            Location = new Point(20, 142),
            Size = new Size(412, 44),
            ForeColor = AntStyle.TextSec
        });

        var btnClose = new AntButton
        {
            Kind = AntButton.AntKind.Primary,
            Text = "关闭",
            Location = new Point(452 - 20 - 88, 258 - 42),
            Size = new Size(88, 30)
        };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        // AntButton 不实现 IButtonControl，不能设为 AcceptButton/CancelButton；
        // 用 KeyPreview 自己处理 Esc / Enter 关闭
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close();
        };
    }

    private static void OpenRepo()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = RepoUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("AboutForm.OpenRepo", ex);
            MessageBox.Show("打开链接失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

#if DEBUG
    /// <summary>调试入口用：不走鼠标直接触发仓库链接动作的可达性检查（不真正开浏览器）。</summary>
    internal static string DebugRepoUrl => RepoUrl;
    internal static string DebugRepoName => RepoName;
#endif
}
