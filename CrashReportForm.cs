using System.Text;

namespace DnfAutoFire;

/// <summary>
/// 未处理异常的提示窗：异常摘要 + 完整详情，并提供打开日志与复制详情两个出口。
/// 全局异常可能发生在任意线程（包括正在终止的线程），所以弹窗放在独立的 STA 线程上。
/// </summary>
internal sealed class CrashReportForm : Form
{
    private readonly string _details;
    private readonly Font _titleFont;

    internal CrashReportForm(string context, Exception? ex, string logPath)
    {
        _details = BuildDetails(context, ex, logPath);
        _titleFont = new Font(Font.FontFamily, 11f, FontStyle.Bold);

        Text = "程序遇到问题";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(560, 320);
        BackColor = Color.White;
        Font = SystemFonts.MessageBoxFont;

        Controls.Add(new Label
        {
            Text = "DNF 连发捕获到一个未处理异常",
            Location = new Point(20, 16),
            AutoSize = true,
            Font = _titleFont,
            ForeColor = Color.FromArgb(24, 32, 48)
        });
        Controls.Add(new Label
        {
            Text = "详情已写入 error.log；程序若退出，请打开日志查看原因。",
            Location = new Point(20, 42),
            Size = new Size(520, 30),
            AutoSize = false,
            ForeColor = Color.FromArgb(96, 102, 112)
        });
        Controls.Add(new TextBox
        {
            Location = new Point(20, 76),
            Size = new Size(520, 184),
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = _details,
            BackColor = Color.White
        });

        var btnLog = new Button
        {
            Text = "打开日志",
            Location = new Point(20, 274),
            Size = new Size(96, 30)
        };
        btnLog.Click += (_, _) =>
        {
            string? error = Logger.OpenLog();
            if (error != null)
                MessageBox.Show(this, "打开日志失败：" + error, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Controls.Add(btnLog);

        var btnCopy = new Button
        {
            Text = "复制详情",
            Location = new Point(126, 274),
            Size = new Size(96, 30)
        };
        btnCopy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_details);
                MessageBox.Show(this, "异常详情已复制到剪贴板。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        Controls.Add(btnCopy);

        var btnClose = new Button
        {
            Text = "关闭",
            Location = new Point(444, 274),
            Size = new Size(96, 30),
            DialogResult = DialogResult.OK
        };
        Controls.Add(btnClose);
        AcceptButton = btnClose;
        CancelButton = btnClose;
        FormClosed += (_, _) => _titleFont.Dispose();
    }

    private static string BuildDetails(string context, Exception? ex, string logPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"来源：{context}");
        sb.AppendLine($"日志：{logPath}");
        sb.AppendLine();
        if (ex == null)
        {
            sb.AppendLine("(non-exception object)");
            return sb.ToString();
        }

        sb.AppendLine($"类型：{ex.GetType().FullName}");
        sb.AppendLine($"消息：{ex.Message}");
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            sb.AppendLine("堆栈：");
            sb.AppendLine(ex.StackTrace);
        }
        for (Exception? inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            sb.AppendLine();
            sb.AppendLine($"内层：{inner.GetType().FullName}: {inner.Message}");
            if (!string.IsNullOrEmpty(inner.StackTrace)) sb.AppendLine(inner.StackTrace);
        }
        return sb.ToString();
    }

    /// <summary>弹窗并返回（异常窗口关闭或 30 秒超时后才返回）。</summary>
    internal static void Show(string context, Exception? ex, string logPath)
    {
        Exception? uiError = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new CrashReportForm(context, ex, logPath);
                form.ShowDialog();
            }
            catch (Exception e) { uiError = e; }
        })
        { IsBackground = true, Name = "CrashReport" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        if (uiError != null) Logger.Error("CrashReport", uiError);
    }
}
