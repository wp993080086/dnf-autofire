namespace DnfAutoFire;

/// <summary>
/// 轻量错误日志：写入 exe 同目录 error.log。
/// 每次写入都是整文件重写（覆盖式更新），最多保留 200 行，自动淘汰最旧的记录。
/// </summary>
internal static class Logger
{
    private static readonly object Sync = new();
    private const int MaxEntries = 200;

    public static string LogPath => Path.Combine(AppPaths.DataDir, "error.log");

    /// <summary>挂接全局异常捕获（UI 线程 + AppDomain），需在 Main 最先调用。</summary>
    public static void Hook()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Error("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Error("AppDomain", e.ExceptionObject as Exception);
    }

    public static void Error(string context, Exception? ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] [").Append(context).Append("] ");
        if (ex == null)
        {
            sb.Append("(unknown non-exception object)");
        }
        else
        {
            sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace)) sb.Append('\n').Append(ex.StackTrace);
            if (ex.InnerException != null)
                sb.Append("\n--> ").Append(ex.InnerException.GetType().Name).Append(": ").Append(ex.InnerException.Message);
        }
        Write(sb.ToString());
    }

    public static void Info(string message) =>
        Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Info] {message}");

    public static void Write(string entry)
    {
        try
        {
            lock (Sync)
            {
                var lines = new List<string>();
                if (File.Exists(LogPath))
                    lines.AddRange(File.ReadAllLines(LogPath));
                lines.Add(entry);
                if (lines.Count > MaxEntries)
                    lines.RemoveRange(0, lines.Count - MaxEntries);
                File.WriteAllLines(LogPath, lines); // 整文件重写 = 覆盖式更新
            }
        }
        catch
        {
            // 日志本身失败时静默，不影响主流程
        }
    }
}
