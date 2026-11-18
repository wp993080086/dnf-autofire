namespace KeyMouseFactory;

internal static partial class Program
{
    internal const string SingleInstanceName = "KeyMouseFactory_SingleInstance";

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

        // 单实例保护：双开会导致热键冲突与双重连发。
        // 第二份实例不再只弹提示，而是广播一条注册消息让已运行的实例把窗口叫到前台。
        using var mutex = new Mutex(true, SingleInstanceName, out bool createdNew);
        if (!createdNew)
        {
            SingleInstanceSignal.Notify();
            return;
        }

        Application.Run(new MainForm());
    }
}
