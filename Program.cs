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

        // 调试用：dnf-autofire --recordtest <秒> <png> 自动开始录制、抓悬浮条、到时停止并退出
        if (args.Length >= 4 && args[1] == "--recordtest")
        {
            var form = new MainForm();
            form.Show();
            form.DebugRecordTest(int.TryParse(args[2], out int rsec) ? rsec : 2, args[3]);
            Application.Run(form);
            return;
        }

        // 调试用：dnf-autofire --playtest <方案名> <值> [count|infinite|duration] [最长秒数] [截图路径]
        // 按指定模式回放，到点/跑完即退出（配 DNF_DRYRUN=1 只跑时间轴不注入鼠标）
        if (args.Length >= 4 && args[1] == "--playtest")
        {
            var form = new MainForm();
            form.Show();
            form.DebugPlayTest(
                args[2],
                int.TryParse(args[3], out int pv) ? pv : 1,
                args.Length >= 5 ? args[4] : "count",
                args.Length >= 6 && int.TryParse(args[5], out int pg) ? pg : 30,
                args.Length >= 7 ? args[6] : null);
            Application.Run(form);
            return;
        }

        // 调试用：dnf-autofire --movetest <x> <y> 注入一次绝对定位并回报光标实际落点
        // （验证当前环境是否允许注入；结果与虚拟桌面范围一起写日志）
        if (args.Length >= 4 && args[1] == "--movetest")
        {
            int mx = int.TryParse(args[2], out int tx) ? tx : 100;
            int my = int.TryParse(args[3], out int ty) ? ty : 100;
            var before = Cursor.Position;
            bool ok = NativeInput.SendMouseMoveAbsolute(mx, my);
            Thread.Sleep(200);
            var after = Cursor.Position;
            var v = NativeInput.VirtualScreen();
            Logger.Info($"[movetest] 目标={mx},{my} 注入={ok} 前={before.X},{before.Y} 后={after.X},{after.Y} 虚拟桌面={v.X},{v.Y} {v.Width}x{v.Height}");
            return;
        }

        // 调试用：dnf-autofire --mouselayout 打印鼠标页行内控件边界
        if (args.Length >= 2 && args[1] == "--mouselayout")
        {
            using var mf = new MainForm();
            mf.Show();
            Application.DoEvents();
            mf.DebugDumpMouseLayout();
            mf.Close();
            return;
        }

        // 调试用：dnf-autofire --mousetest <秒数> 自动开启鼠标连点，数秒后关闭退出（真机验证连点/自动点击收尾）
        if (args.Length >= 3 && args[1] == "--mousetest")
        {
            var form = new MainForm();
            form.Show();
            form.DebugMouseTest(int.TryParse(args[2], out int msec) ? msec : 5);
            Application.Run(form);
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

        // 调试用：dnf-autofire --keytest 跑一遍「开启连发」前的键名校验并打印结果
        if (args.Length >= 2 && args[1] == "--keytest")
        {
            using var ktForm = new MainForm();
            ktForm.Show();
            Application.DoEvents();
            Console.WriteLine(ktForm.DebugStartCheck());
            ktForm.Close();
            return;
        }

        // 调试用：dnf-autofire --seltest 模拟点击第二个方案，验证单选切换不会无限递归
        if (args.Length >= 2 && args[1] == "--seltest")
        {
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
            form.Show();
            Application.DoEvents();
            form.DebugSelectionTest();
            Logger.Info("[seltest] 方法返回，进程仍在运行");
            form.Close();
            return;
        }

        // 调试用：dnf-autofire --playoverlaytest <png> [第几次] [总次数] 离屏渲染回放状态条并截图
        if (args.Length >= 3 && args[1] == "--playoverlaytest")
        {
            int loop = args.Length >= 4 && int.TryParse(args[3], out int lp) ? lp : 2;
            int total = args.Length >= 5 && int.TryParse(args[4], out int tot) ? tot : 5;
            using var ov = new PlayOverlayForm("方案A", total);
            ov.Show();
            ov.Location = new Point(-3000, -3000); // 摆到屏幕外，别挡住桌面
            ov.UpdateProgress(loop, 125400, false);
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();
            using var bmp = new Bitmap(ov.Width, ov.Height);
            ov.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(args[2], ImageFormat.Png);
            int ex = NativeMethods.GetWindowLong(ov.Handle, NativeMethods.GWL_EXSTYLE);
            Logger.Info($"[playoverlaytest] {ov.Width}x{ov.Height} loop={loop}/{total} "
                + $"ExStyle=0x{ex:X} 穿透={(ex & NativeMethods.WS_EX_TRANSPARENT) != 0} → {args[2]}");
            ov.Close();
            return;
        }

        // 调试用：dnf-autofire --closetest <png> [busy] 渲染「关闭还是最小化」询问框并截图
        if (args.Length >= 3 && args[1] == "--closetest")
        {
            using var dlg = new ClosePromptForm(args.Length >= 4 && args[3] == "busy");
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(-3000, -3000);
            dlg.Show();
            Application.DoEvents();
            Thread.Sleep(250);
            Application.DoEvents();
            using var bmp = new Bitmap(dlg.Width, dlg.Height);
            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(args[2], ImageFormat.Png);
            dlg.Close();
            return;
        }

        // 调试用：dnf-autofire --xclosetest <prefix> 模拟点 X：询问框出现 → 选「最小化」→ 再点 X → 选「退出」
        if (args.Length >= 3 && args[1] == "--xclosetest")
        {
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(220, 220) };
            form.Show();
            Logger.Info("[xclosetest] 窗口已显示，开始计时");

            int step = 0;
            var t = new System.Windows.Forms.Timer { Interval = 500 };
            t.Tick += (_, _) =>
            {
                step++;
                var dlg = Application.OpenForms.OfType<ClosePromptForm>().FirstOrDefault();
                switch (step)
                {
                    case 1:
                        NativeMethods.PostMessage(form.Handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_CLOSE, IntPtr.Zero); // 等于用户点 X
                        return;
                    case 2:
                        Logger.Info($"[xclosetest] 第一次点 X：询问框={(dlg == null ? "未出现" : "已出现")}");
                        if (dlg == null) return;
                        using (var bmp = new Bitmap(dlg.Width, dlg.Height))
                        {
                            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                            bmp.Save(args[2] + "_prompt.png", ImageFormat.Png);
                        }
                        dlg.ChooseForTest(ClosePromptForm.Choice.Minimize);
                        return;
                    case 3:
                        Logger.Info($"[xclosetest] 选最小化后：Visible={form.Visible} Disposed={form.IsDisposed}");
                        form.Show();
                        form.WindowState = FormWindowState.Normal;
                        NativeMethods.PostMessage(form.Handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_CLOSE, IntPtr.Zero); // 等于用户点 X
                        return;
                    case 4:
                        Logger.Info($"[xclosetest] 第二次点 X：询问框={(dlg == null ? "未出现" : "已出现")}");
                        dlg?.ChooseForTest(ClosePromptForm.Choice.Exit);
                        return;
                }
                t.Stop();
                Logger.Info("[xclosetest] 未走到退出分支，强制结束");
                Application.Exit();
            };
            t.Start();

            var guard = new System.Windows.Forms.Timer { Interval = 12000 };
            guard.Tick += (_, _) =>
            {
                guard.Stop();
                Logger.Info("[xclosetest] 超时兜底退出");
                Application.Exit();
            };
            guard.Start();

            Application.Run(form);
            Logger.Info($"[xclosetest] 消息循环结束：Disposed={form.IsDisposed}（true=选「退出」生效）");
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
