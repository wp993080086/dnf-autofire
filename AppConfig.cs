using System.Text.Encodings.Web;
using System.Text.Json;

namespace DnfAutoFire;

/// <summary>按键连发配置行。StopMode: hold=松手停止 / auto=自动连按（StopValue 0 表示无限）。</summary>
public class KeyConfig
{
    public string Key { get; set; } = "";
    public int IntervalMs { get; set; } = 50;
    public string StopMode { get; set; } = "hold";
    public int StopValue { get; set; } = 0;
    public bool Enabled { get; set; } = true;
}

/// <summary>鼠标连点配置行。StopMode: infinite=松手停止 / auto=自动点击（StopValue 0 表示无限）。</summary>
public class MouseClickConfig
{
    public string Button { get; set; } = "L"; // L=左键 R=右键 M=滚轮按下（中键）
    public int IntervalMs { get; set; } = 300;
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
    public string RecordHotkey { get; set; } = "F9";     // 录制热键
    public string PlaybackHotkey { get; set; } = "F10";  // 回放热键
    public string PlayMode { get; set; } = "infinite";   // 回放模式：count=回放 N 次 / infinite=一直放 / duration=定时 N 分钟
    public int PlayValue { get; set; } = 1;              // 回放次数或分钟数
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
            new MouseClickConfig { Button = "L", IntervalMs = 100, StopMode = "infinite", StopValue = 0, Enabled = false },
            new MouseClickConfig { Button = "R", IntervalMs = 100, StopMode = "infinite", StopValue = 0, Enabled = false },
            new MouseClickConfig { Button = "M", IntervalMs = 100, StopMode = "infinite", StopValue = 0, Enabled = false }
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
