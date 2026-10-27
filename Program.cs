using System.Drawing.Imaging;

namespace DnfAutoFire;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Logger.Hook();
        ApplicationConfiguration.Initialize();

        // 调试用：dnf-autofire --screenshot <path.png> [新增行数] 离屏渲染窗口后退出
        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 3 && args[1] == "--screenshot")
        {
            try
            {
                using var form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-3000, -3000);
                form.Show();
                Application.DoEvents();
                if (args.Length >= 4 && int.TryParse(args[3], out int add))
                    for (int i = 0; i < add; i++)
                        form.DebugAddEmptyRow();
                Thread.Sleep(300);
                Application.DoEvents();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(args[2], ImageFormat.Png);
                form.Close();
            }
            catch (Exception ex)
            {
                Logger.Error("Screenshot", ex);
                throw;
            }
            return;
        }

        // 调试用：dnf-autofire --uitest <prefix> 窗口可见状态下切换按钮状态并抓图，验证自绘控件重绘
        if (args.Length >= 3 && args[1] == "--uitest")
        {
            var form = new MainForm();
            form.Show();
            form.RunUiRepaintTest(args[2]);
            Application.Run();
            return;
        }

        // 单实例保护：双开会导致热键冲突与双重连发
        using var mutex = new Mutex(true, "DnfAutoFire_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("DNF 连发工具已经在运行中。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
    }
}
