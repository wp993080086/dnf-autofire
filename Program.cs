namespace DnfAutoFire;

internal static partial class Program
{
    [STAThread]
    private static void Main()
    {
        Logger.Hook();
        ApplicationConfiguration.Initialize();

#if DEBUG
        // 调试命令行（--screenshot / --hotkeytest 等）：命中就执行完直接退出，不进正式流程。
        // Release 构建里 TryRunDebugCommand 与方法体都不参与编译，正式版没有任何调试入口。
        if (TryRunDebugCommand(Environment.GetCommandLineArgs())) return;
#endif

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
