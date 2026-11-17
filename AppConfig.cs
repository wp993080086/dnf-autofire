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
    /// <summary>
    /// 配置结构版本＝写入时的程序版本。缺字段或低于当前版本 → 整份重置为默认（见 ConfigManager.Load）。
    /// 不设初始值：STJ 反序列化时缺失的属性保留字段默认值，若给默认值就永远判不出"没有这个字段"。
    /// </summary>
    public string? ConfigVersion { get; set; }

    public List<KeyConfig> Keys { get; set; } = new();
    public List<MouseClickConfig> MouseClicks { get; set; } = new();
    public string StartHotkey { get; set; } = "F6";      // 按键连发热键
    public string MouseStartHotkey { get; set; } = "F8"; // 鼠标连点热键
    public string RecordHotkey { get; set; } = "F9";     // 录制热键
    public string PlaybackHotkey { get; set; } = "F10";  // 回放热键
    public string PlayMode { get; set; } = "infinite";   // 回放模式：count=回放 N 次 / infinite=一直放 / duration=定时 N 分钟
    public int PlayValue { get; set; } = 1;              // 回放次数或分钟数
    public int PlayGapSec { get; set; } = 1;              // 回放间隔：两轮之间的等待（秒，0=不等待）
    public string Mode { get; set; } = "B";              // 连发模式：A=保留原生（放行物理键，叠加连发）/ B=拦截原生（只发模拟）

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

    /// <summary>程序版本（形如 1.2.0），取自程序集版本，与 Tab 栏显示的版本号同源。</summary>
    public static string AppVersion =>
        typeof(AppConfig).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return AppConfig.Default();

            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions);
            if (cfg == null) return AppConfig.Default();

            // 老配置没有版本字段，或版本低于当前程序 → 整份重置并立刻覆写：
            // 结构变了以后逐字段兼容太容易漏，干脆从头来（配置项就这些，重填成本低）
            if (!VersionMatches(cfg.ConfigVersion))
            {
                Logger.Info($"配置版本 {Describe(cfg.ConfigVersion)} 与当前 {AppVersion} 不符，已整体重置为默认配置");
                var def = AppConfig.Default();
                Save(def);
                return def;
            }
            return cfg;
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
        cfg.ConfigVersion = AppVersion; // 每次写盘都盖上当前版本，下次启动即按此判断
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOptions));
    }

    /// <summary>版本字段必须存在，且不低于当前程序版本（缺字段 / 低于 = 需要重置）。</summary>
    private static bool VersionMatches(string? version) =>
        Version.TryParse(version, out var v) && v >= (Version.TryParse(AppVersion, out var app) ? app : new Version(0, 0, 0));

    private static string Describe(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "(无配置版本字段)" : version;
}
