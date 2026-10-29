using System.Text.Encodings.Web;
using System.Text.Json;

namespace DnfAutoFire;

public class KeyConfig
{
    public string Key { get; set; } = "";
    public int IntervalMs { get; set; } = 50;
    public bool Enabled { get; set; } = true;
}

/// <summary>鼠标连点配置行。StopMode: count=点N次 / infinite=松手停止。</summary>
public class MouseClickConfig
{
    public string Button { get; set; } = "L"; // L=左键 R=右键
    public int IntervalMs { get; set; } = 100;
    public string StopMode { get; set; } = "infinite";
    public int StopValue { get; set; } = 50;
    public bool Enabled { get; set; } = false;
}

public class AppConfig
{
    public List<KeyConfig> Keys { get; set; } = new();
    public List<MouseClickConfig> MouseClicks { get; set; } = new();
    public string StartHotkey { get; set; } = "F6";      // 按键连发热键
    public string MouseStartHotkey { get; set; } = "F8"; // 鼠标连点热键
    public string Mode { get; set; } = "B";

    public static AppConfig Default() => new()
    {
        Keys =
        {
            new KeyConfig { Key = "X", IntervalMs = 50, Enabled = true },
            new KeyConfig { Key = "A", IntervalMs = 50, Enabled = false }
        },
        MouseClicks =
        {
            new MouseClickConfig { Button = "L", IntervalMs = 100, StopMode = "infinite", StopValue = 50, Enabled = false },
            new MouseClickConfig { Button = "R", IntervalMs = 100, StopMode = "infinite", StopValue = 50, Enabled = false }
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
