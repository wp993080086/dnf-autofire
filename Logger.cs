namespace DnfAutoFire;

/// <summary>
/// 轻量错误日志：写入 exe 同目录 error.log。
/// 追加写入，超过 200 条时整理一次（保留最新的 200 条），不做每次全量重写。
/// </summary>
internal static class Logger
{
    private static readonly object Sync = new();
    private const int MaxEntries = 200;
    private static int _lineCount = -1; // -1 = 还没读过盘；避免每次写都全量读一遍

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
                if (_lineCount < 0)
                    _lineCount = File.Exists(LogPath) ? File.ReadAllLines(LogPath).Length : 0;
                File.AppendAllText(LogPath, entry + Environment.NewLine);
                _lineCount++;
                if (_lineCount > MaxEntries) Trim();
            }
        }
        catch
        {
            // 日志本身失败时静默，不影响主流程
        }
    }

    /// <summary>超过上限时整体整理一次，只留最新 MaxEntries 条。</summary>
    private static void Trim()
    {
        var lines = File.ReadAllLines(LogPath).ToList();
        if (lines.Count <= MaxEntries)
        {
            _lineCount = lines.Count;
            return;
        }
        lines.RemoveRange(0, lines.Count - MaxEntries);
        File.WriteAllLines(LogPath, lines);
        _lineCount = lines.Count;
    }
}
