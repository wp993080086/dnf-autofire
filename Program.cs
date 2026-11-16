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
                if (args.Length >= 5)
                {
                    if (args[4] == "mouse") form.DebugShowMouseTab();    // 切到鼠标 Tab 再截图
                    else if (args[4] == "record") form.DebugShowRecordTab(); // 切到录制 Tab 再截图
                }
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

        // 调试用：dnf-autofire --enginetest <A|B> <秒数> 自动开启连发引擎，数秒后自动关闭退出（真机验证连发行为）
        if (args.Length >= 4 && args[1] == "--enginetest")
        {
            var form = new MainForm();
            form.Show();
            form.DebugEngineTest(args[2] == "A" ? "A" : "B",
                int.TryParse(args[3], out int sec) ? sec : 5);
            Application.Run(form);
            return;
        }

        // 调试用：dnf-autofire --fadetest <prefix> 切页并按 40ms 连续抓帧，验证白幕淡入
        if (args.Length >= 3 && args[1] == "--fadetest")
        {
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(220, 220) };
            form.Show();
            var rect = form.DebugPageScreenRect();
            // 等窗口出现动画/首帧绘制结束，再切页抓帧，否则抓到的是窗口出现动画
            var warm = new System.Windows.Forms.Timer { Interval = 500 };
            int i = 0;
            var t = new System.Windows.Forms.Timer { Interval = 40 };
            warm.Tick += (_, _) =>
            {
                warm.Stop();
                warm.Dispose();
                form.Activate();
                form.DebugShowTabFade(1);
                t.Start();
            };
            t.Tick += (_, _) =>
            {
                using var bmp = new Bitmap(rect.Width, rect.Height);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
                bmp.Save($"{args[2]}_{i}.png", ImageFormat.Png);
                i++;
                if (i < 10) return;
                t.Stop();
                t.Dispose();
                form.Close();
                Application.Exit();
            };
            warm.Start();
            Application.Run();
            return;
        }

        // 调试用：dnf-autofire --focustest 复现「清焦点 → 最小化 → 还原」后的焦点归属
        if (args.Length >= 2 && args[1] == "--focustest")
        {
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(220, 220) };
            form.Show();
            string Name() => form.ActiveControl?.GetType().Name ?? "null";
            var steps = new List<Action>
            {
                () => Console.WriteLine($"[shown]     {Name()}"),
                () => { form.ActiveControl = null; Console.WriteLine($"[cleared]   {Name()}"); },
                () => { bool ok = form.SelectNextControl(null, true, true, true, true); Console.WriteLine($"[next]      {Name()} ok={ok}"); },
                () => { form.WindowState = FormWindowState.Minimized; Console.WriteLine($"[minimized] {Name()}"); },
                () => { form.WindowState = FormWindowState.Normal; Console.WriteLine($"[restored]  {Name()}"); },
                () => { form.Activate(); Console.WriteLine($"[activated] {Name()}"); },
                () => { form.Hide(); Console.WriteLine($"[hidden]    {Name()}"); },
                () => { form.Show(); Console.WriteLine($"[reshown]   {Name()}"); },
                () => { form.Close(); Application.Exit(); }
            };
            int k = 0;
            var ft = new System.Windows.Forms.Timer { Interval = 400 };
            ft.Tick += (_, _) =>
            {
                if (k >= steps.Count) { ft.Stop(); ft.Dispose(); return; }
                steps[k++]();
            };
            ft.Start();
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
