namespace DnfAutoFire;

/// <summary>
/// 配置与日志的数据目录：默认 EXE 同目录（绿色软件习惯）；
/// 若目录无写入权限（如 Program Files），自动回退到 %APPDATA%\DnfAutoFire。
/// </summary>
internal static class AppPaths
{
    public static string DataDir { get; }

    static AppPaths()
    {
        try
        {
            // 探针：实际写一个临时文件验证权限，比检查目录属性更可靠
            string probe = Path.Combine(AppContext.BaseDirectory, ".write_probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            DataDir = AppContext.BaseDirectory;
        }
        catch
        {
            DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DnfAutoFire");
            Directory.CreateDirectory(DataDir);
        }
    }
}
