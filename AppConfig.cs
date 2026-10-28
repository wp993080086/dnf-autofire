using System.Text.Encodings.Web;
using System.Text.Json;

namespace DnfAutoFire;

public class KeyConfig
{
    public string Key { get; set; } = "";
    public int IntervalMs { get; set; } = 50;
    public bool Enabled { get; set; } = true;
}

public class AppConfig
{
    public List<KeyConfig> Keys { get; set; } = new();
    public string StartHotkey { get; set; } = "F6";
    public string Mode { get; set; } = "B";

    public static AppConfig Default() => new()
    {
        Keys =
        {
            new KeyConfig { Key = "X", IntervalMs = 50, Enabled = true },
            new KeyConfig { Key = "A", IntervalMs = 50, Enabled = false }
        }
    };
}

public static class ConfigManager
{
    private static string ConfigPath => Path.Combine(AppPaths.DataDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions);
                if (cfg != null) return cfg;
            }
        }
        catch (Exception ex)
        {
            // 配置文件损坏时回退到默认配置，并记录日志
            Logger.Error("LoadConfig", ex);
        }
        return AppConfig.Default();
    }

    public static void Save(AppConfig cfg)
    {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOptions));
    }
}
