using System.Drawing.Imaging;

namespace DnfAutoFire;

#if DEBUG
// 调试入口：只在 Debug 构建里编译，发布版整段代码不进入程序集。
// 每个命令行开关对应一个 Run* 方法，Main 只负责分发。
// Release 构建里整段（含全部开关字符串）不进入程序集。
internal static partial class Program
{
    /// <summary>命中调试命令行则执行并返回 true，调用方直接结束进程；没命中返回 false，继续走正式流程。</summary>
    internal static bool TryRunDebugCommand(string[] args)
    {
        if (args.Length >= 3 && args[1] == "--screenshot") { RunScreenshot(args); return true; }
        if (args.Length >= 4 && args[1] == "--recordtest") { RunRecordTest(args); return true; }
        if (args.Length >= 4 && args[1] == "--playtest") { RunPlayTest(args); return true; }
        if (args.Length >= 4 && args[1] == "--movetest") { RunMoveTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--cursortest") { RunCursorTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--hotkeytest") { RunHotkeyTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--mouselayout") { RunMouseLayout(args); return true; }
        if (args.Length >= 3 && args[1] == "--mousetest") { RunMouseTest(args); return true; }
        if (args.Length >= 3 && args[1] == "--uitest") { RunUiTest(args); return true; }
        if (args.Length >= 4 && args[1] == "--enginetest") { RunEngineTest(args); return true; }
        if (args.Length >= 3 && args[1] == "--fadetest") { RunFadeTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--focustest") { RunFocusTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--keytest") { RunKeyTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--seltest") { RunSelectionTest(args); return true; }
        if (args.Length >= 3 && args[1] == "--playoverlaytest") { RunPlayOverlayTest(args); return true; }
        if (args.Length >= 3 && args[1] == "--closetest") { RunCloseTest(args); return true; }
        if (args.Length >= 3 && args[1] == "--xclosetest") { RunXCloseTest(args); return true; }
        if (args.Length >= 3 && args[1] == "--savetest") { RunSaveTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--keynametest") { RunKeyNameTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--keycaptest") { RunKeyCapTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--xbuttontest") { RunXButtonTest(args); return true; }
        if (args.Length >= 2 && args[1] == "--hotkeyfallbacktest") { RunHotkeyFallbackTest(args); return true; }
        if (args.Length >= 3 && args[1] == "--instancetest") { RunInstanceTest(args); return true; }
        if (args.Length >= 4 && args[1] == "--hotkeyoccupy") { RunHotkeyOccupy(args); return true; }
        return false;
    }

    // 调试用：dnf-autofire --keycaptest 喂物理按键给键名框，验证捕获到的键名
    private static void RunKeyCapTest(string[] args)
    {
        using var form = new MainForm();
        form.Show();
        Application.DoEvents();
        form.DebugKeyCaptureTest();
        form.Close();
    }

    // 调试用：dnf-autofire --keynametest 校验键名映射表的往返一致性、VK 唯一性与扩展键标记
    private static void RunKeyNameTest(string[] args)
    {
        int total = 0, bad = 0;
        var seen = new Dictionary<int, string>();

        foreach (string name in KeyNames.All())
        {
            total++;
            int vk = KeyNames.ToVk(name);
            if (vk <= 0)
            {
                Console.WriteLine($"[name] BAD {name} 解析失败");
                bad++;
                continue;
            }
            string? back = KeyNames.FromKeys((Keys)vk);
            if (back != name)
            {
                Console.WriteLine($"[name] BAD {name} → vk=0x{vk:X2} → {back ?? "(null)"}");
                bad++;
            }
            if (seen.TryGetValue(vk, out string? other))
            {
                Console.WriteLine($"[name] BAD {name} 与 {other} 撞同一个 VK 0x{vk:X2}");
                bad++;
            }
            else seen[vk] = name;
        }
        Console.WriteLine($"[name] 共 {total} 个键名 / {seen.Count} 个不同 VK，异常 {bad} 个");

        // 当前支持集里不该出现扩展键：方向键与编辑键都撤掉了，IsExtended 只是注入层的防御分支
        foreach (string name in KeyNames.All())
            if (KeyNames.IsExtended(KeyNames.ToVk(name))) { Console.WriteLine($"[name] BAD {name} 不该是扩展键"); bad++; }

        // 非法与已撤掉的键：必须拒绝
        foreach (string text in new[] { "", "  ", "X5", "AB", "F5", "F13", "LEFT", "TAB", "BS", "PGUP", "NUMPAD5", "SHIFT", "DEL", "INS" })
            if (KeyNames.ToVk(text) >= 0) { Console.WriteLine($"[name] BAD 「{text}」应判为非法"); bad++; }
        foreach (Keys key in new[] { Keys.F5, Keys.Up, Keys.Tab, Keys.Home, Keys.Back, Keys.Insert, Keys.LShiftKey, Keys.Divide, Keys.CapsLock })
            if (KeyNames.FromKeys(key) != null) { Console.WriteLine($"[name] BAD {key} 应判为不支持"); bad++; }

        Console.WriteLine(bad == 0 ? "[name] PASS" : $"[name] FAIL（{bad} 处异常）");
    }

    // 调试用：dnf-autofire --xbuttontest 校验侧键的消息解析、注入标志与配置映射
    private static void RunXButtonTest(string[] args)
    {
        int bad = 0;
        foreach (var (data, want) in new (uint Data, int? Vk)[]
                 {
                     ((uint)NativeMethods.XBUTTON1 << 16, NativeMethods.VK_XBUTTON1),
                     ((uint)NativeMethods.XBUTTON2 << 16, NativeMethods.VK_XBUTTON2),
                     (0, null), (3u << 16, null)
                 })
        {
            int? got = MouseHook.XButtonVk(data);
            if (got != want)
            {
                Console.WriteLine($"[xb] BAD mouseData=0x{data:X8} 期望 {want} 实际 {got}");
                bad++;
            }
        }

        foreach (var (button, up, wantFlag, wantData) in new (int Button, bool Up, uint Flag, uint Data)[]
                 {
                     (NativeMethods.VK_XBUTTON1, false, NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.XBUTTON1),
                     (NativeMethods.VK_XBUTTON1, true, NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON1),
                     (NativeMethods.VK_XBUTTON2, false, NativeMethods.MOUSEEVENTF_XDOWN, NativeMethods.XBUTTON2),
                     (NativeMethods.VK_XBUTTON2, true, NativeMethods.MOUSEEVENTF_XUP, NativeMethods.XBUTTON2),
                     (NativeMethods.VK_LBUTTON, false, NativeMethods.MOUSEEVENTF_LEFTDOWN, 0)
                 })
        {
            var (flag, data) = NativeInput.MouseButtonSpec(button, up);
            if (flag != wantFlag || data != wantData)
            {
                Console.WriteLine($"[xb] BAD button={button} up={up} 期望 ({wantFlag},{wantData}) 实际 ({flag},{data})");
                bad++;
            }
        }

        foreach (var (name, vk) in new[]
                 {
                     ("L", NativeMethods.VK_LBUTTON), ("R", NativeMethods.VK_RBUTTON),
                     ("M", NativeMethods.VK_MBUTTON),
                     ("X1", NativeMethods.VK_XBUTTON1), ("X2", NativeMethods.VK_XBUTTON2)
                 })
            if (MainForm.MouseButtonVk(name) != vk)
            {
                Console.WriteLine($"[xb] BAD 配置按钮 {name} 应映射到 {vk}");
                bad++;
            }

        var defaults = AppConfig.Default();
        if (defaults.MouseClicks.Count != 5 ||
            !defaults.MouseClicks.Any(m => m.Button == "X1" && !m.Enabled) ||
            !defaults.MouseClicks.Any(m => m.Button == "X2" && !m.Enabled))
        {
            Console.WriteLine("[xb] BAD 默认配置缺少关闭状态的 X1/X2 行");
            bad++;
        }

        Console.WriteLine(bad == 0 ? "[xb] PASS" : $"[xb] FAIL（{bad} 处异常）");
    }

    // 调试用：dnf-autofire --hotkeyfallbacktest 校验备用热键的挑选规则
    private static void RunHotkeyFallbackTest(string[] args)
    {
        int bad = 0;
        void Check(string label, string? got, string? want)
        {
            if (got != want) { Console.WriteLine($"[hkf] BAD {label} 期望 {want ?? "(null)"} 实际 {got ?? "(null)"}"); bad++; }
        }

        Check("默认四键已占", HotkeyFallback.Pick(new[] { "F6", "F8", "F9", "F10" }), "F1");
        Check("大小写与空格", HotkeyFallback.Pick(new[] { " f1 ", "F2" }), "F3");
        Check("全部占满", HotkeyFallback.Pick(HotkeyFallback.Candidates), null);
        Check("无占用", HotkeyFallback.Pick(Array.Empty<string>()), "F1");
        Console.WriteLine(bad == 0 ? "[hkf] PASS" : $"[hkf] FAIL（{bad} 处异常）");
    }

    // 调试用：dnf-autofire --hotkeyoccupy F6 10 强占一个热键若干秒，用来验证本程序的自动换键
    private static void RunHotkeyOccupy(string[] args)
    {
        if (!int.TryParse(args[2].TrimStart('F', 'f'), out int n) || n is < 1 or > 12) return;
        using var form = new Form();
        _ = form.Handle; // 访问 Handle 即强制创建句柄
        bool ok = NativeMethods.RegisterHotKey(form.Handle, 99, 0, (uint)(0x70 + n - 1));
        Console.WriteLine($"[occupy] {args[2]} registered={ok}");
        using var timer = new System.Windows.Forms.Timer
        {
            Interval = int.TryParse(args[3], out int sec) ? Math.Max(1, sec) * 1000 : 10000
        };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
    }

    // 调试用：dnf-autofire --instancetest listen 持锁并隐藏窗口；--instancetest notify 模拟第二份实例
    private static void RunInstanceTest(string[] args)
    {
        if (args[2] == "notify")
        {
            Console.WriteLine($"[inst] notify msg={SingleInstanceSignal.Message}");
            SingleInstanceSignal.Notify();
            return;
        }

        using var mutex = new Mutex(true, Program.SingleInstanceName, out bool createdNew);
        Console.WriteLine(createdNew ? "[inst] mutex acquired" : "[inst] mutex already held");
        using var form = new MainForm();
        form.VisibleChanged += (_, _) => Console.WriteLine($"[inst] visible={form.Visible}");
        form.Show();
        Application.DoEvents();
        form.Hide();
        using var timer = new System.Windows.Forms.Timer { Interval = 8000 };
        timer.Tick += (_, _) => { timer.Stop(); form.Close(); };
        timer.Start();
        Application.Run(form);
    }

    // 调试用：dnf-autofire --savetest <最终间隔秒> [等待毫秒]
    // 连改 5 次值 → 立刻读文件（应仍是旧值）→ 等防抖到期再读（应已更新）→ 设最终值后立即关窗（验证退出兜底）
    private static void RunSaveTest(string[] args)
    {
        var form = new MainForm();
        form.Show();
        Application.DoEvents();
        form.DebugSaveTest(int.TryParse(args[2], out int v) ? v : 7,
                           args.Length >= 4 && int.TryParse(args[3], out int w) ? w : 700);
        Application.Run(form);
    }

    // 调试用：dnf-autofire --screenshot <path.png> [新增行数] 离屏渲染窗口后退出
    private static void RunScreenshot(string[] args)
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
    }

    // 调试用：dnf-autofire --recordtest <秒> <png> 自动开始录制、抓悬浮条、到时停止并退出
    private static void RunRecordTest(string[] args)
    {
        var form = new MainForm();
        form.Show();
        form.DebugRecordTest(int.TryParse(args[2], out int rsec) ? rsec : 2, args[3]);
        Application.Run(form);
    }

    // 调试用：dnf-autofire --playtest <方案名> <值> [count|infinite|duration] [最长秒数] [截图路径]
    // 按指定模式回放，到点/跑完即退出（配 DNF_DRYRUN=1 只跑时间轴不注入鼠标）
    private static void RunPlayTest(string[] args)
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
    }

    // 调试用：dnf-autofire --movetest <x> <y> 注入一次绝对定位并回报光标实际落点
    // （验证当前环境是否允许注入；结果与虚拟桌面范围一起写日志）
    private static void RunMoveTest(string[] args)
    {
        int mx = int.TryParse(args[2], out int tx) ? tx : 100;
        int my = int.TryParse(args[3], out int ty) ? ty : 100;
        var before = Cursor.Position;
        bool ok = NativeInput.SendMouseMoveAbsolute(mx, my);
        Thread.Sleep(200);
        var after = Cursor.Position;
        var v = NativeInput.VirtualScreen();
        Logger.Info($"[movetest] 目标={mx},{my} 注入={ok} 前={before.X},{before.Y} 后={after.X},{after.Y} 虚拟桌面={v.X},{v.Y} {v.Width}x{v.Height}");
    }

    // 调试用：dnf-autofire --cursortest 光标移到几个控件上，回报「禁用=禁止 / 可点=手型」
    private static void RunCursorTest(string[] args)
    {
        var csForm = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(260, 200), TopMost = true };
        csForm.Show();
        Application.DoEvents();
        Thread.Sleep(300);
        csForm.DebugCursorProbe();
        csForm.Close();
    }

    // 调试用：dnf-autofire --hotkeytest 核对热键互斥（置灰 + 硬选时回退）
    private static void RunHotkeyTest(string[] args)
    {
        using var hkForm = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
        hkForm.Show();
        Application.DoEvents();
        hkForm.DebugHotkeyTest();
        hkForm.Close();
    }

    // 调试用：dnf-autofire --mouselayout 打印鼠标页行内控件边界
    private static void RunMouseLayout(string[] args)
    {
        using var mf = new MainForm();
        mf.Show();
        Application.DoEvents();
        mf.DebugDumpMouseLayout();
        mf.Close();
    }

    // 调试用：dnf-autofire --mousetest <秒数> 自动开启鼠标连点，数秒后关闭退出（真机验证连点/自动点击收尾）
    private static void RunMouseTest(string[] args)
    {
        var form = new MainForm();
        form.Show();
        form.DebugMouseTest(int.TryParse(args[2], out int msec) ? msec : 5);
        Application.Run(form);
    }

    // 调试用：dnf-autofire --uitest <prefix> 窗口可见状态下切换按钮状态并抓图，验证自绘控件重绘
    private static void RunUiTest(string[] args)
    {
        var form = new MainForm();
        form.Show();
        form.RunUiRepaintTest(args[2]);
        Application.Run();
    }

    // 调试用：dnf-autofire --enginetest <A|B> <秒数> 自动开启连发引擎，数秒后自动关闭退出（真机验证连发行为）
    private static void RunEngineTest(string[] args)
    {
        var form = new MainForm();
        form.Show();
        form.DebugEngineTest(args[2] == "A" ? "A" : "B",
            int.TryParse(args[3], out int sec) ? sec : 5);
        Application.Run(form);
    }

    // 调试用：dnf-autofire --fadetest <prefix> 切页并按 40ms 连续抓帧，验证白幕淡入
    private static void RunFadeTest(string[] args)
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
    }

    // 调试用：dnf-autofire --focustest 复现「清焦点 → 最小化 → 还原」后的焦点归属
    private static void RunFocusTest(string[] args)
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
    }

    // 调试用：dnf-autofire --keytest 跑一遍「开启连发」前的键名校验并打印结果
    private static void RunKeyTest(string[] args)
    {
        using var ktForm = new MainForm();
        ktForm.Show();
        Application.DoEvents();
        Console.WriteLine(ktForm.DebugStartCheck());
        ktForm.Close();
    }

    // 调试用：dnf-autofire --seltest 模拟点击第二个方案，验证单选切换不会无限递归
    private static void RunSelectionTest(string[] args)
    {
        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
        form.Show();
        Application.DoEvents();
        form.DebugSelectionTest();
        Logger.Info("[seltest] 方法返回，进程仍在运行");
        form.Close();
    }

    // 调试用：dnf-autofire --playoverlaytest <png> [第几次] [总次数] 离屏渲染回放状态条并截图
    private static void RunPlayOverlayTest(string[] args)
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
    }

    // 调试用：dnf-autofire --closetest <png> [busy] 渲染「关闭还是最小化」询问框并截图
    private static void RunCloseTest(string[] args)
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
    }

    // 调试用：dnf-autofire --xclosetest <prefix> 模拟点 X：询问框出现 → 选「最小化」→ 再点 X → 选「退出」
    private static void RunXCloseTest(string[] args)
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
    }
}
#endif
