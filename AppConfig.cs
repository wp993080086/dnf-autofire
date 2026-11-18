using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DnfAutoFire;

/// <summary>界面与配置共用的取值范围：加载配置时按同一套上下限校验，避免手改文件把非法值带进界面。</summary>
public static class Limits
{
    public const int MsMin = 50;             // 按键连发间隔下限
    public const int MsMax = 5000;           // 按键连发间隔上限
    public const int MsStep = 50;            // 按键连发间隔步进

    public const int MouseMsMin = 100;       // 鼠标连点间隔下限
    public const int MouseMsMax = 10000;     // 鼠标连点间隔上限
    public const int MouseMsStep = 100;      // 鼠标连点间隔步进
    public const int MouseMsDefault = 300;   // 鼠标连点间隔默认值

    // 自动连按 / 自动点击次数：0 = 无限，上限统一 99999（按键页与鼠标页一致）
    public const int KeyCountMax = 99999;
    public const int MouseCountMax = 99999;

    public const int KeyRowsMax = 5;         // 按键行上限

    public const int PlayCountMax = 9999;    // 回放「N 次」上限
    public const int PlayMinuteMax = 600;    // 回放「定时 N 分钟」上限
}

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
    public string Button { get; set; } = "L"; // L=左键 R=右键 M=中键 X1=侧键1 X2=侧键2
    public int IntervalMs { get; set; } = 300;
    public string StopMode { get; set; } = "infinite";
    public int StopValue { get; set; } = 1;
    public bool Enabled { get; set; } = false;
}

public class AppConfig
{
    /// <summary>
    /// 配置结构版本＝写入时的程序版本。合并时只作日志与升级依据，不再因为版本不符整份重置（见 ConfigManager.Load）。
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
            new MouseClickConfig { Button = "L", IntervalMs = 100, StopMode = "infinite", StopValue = 1, Enabled = false },
            new MouseClickConfig { Button = "R", IntervalMs = 100, StopMode = "infinite", StopValue = 1, Enabled = false },
            new MouseClickConfig { Button = "M", IntervalMs = 100, StopMode = "infinite", StopValue = 1, Enabled = false },
            new MouseClickConfig { Button = "X1", IntervalMs = 100, StopMode = "infinite", StopValue = 1, Enabled = false },
            new MouseClickConfig { Button = "X2", IntervalMs = 100, StopMode = "infinite", StopValue = 1, Enabled = false }
        }
    };
}

public static class ConfigManager
{
    private static string ConfigPath => Path.Combine(AppPaths.DataDir, "config.json");

    /// <summary>程序版本（形如 x.y.z），取自程序集版本，与 Tab 栏显示的版本号同源。</summary>
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

            var text = File.ReadAllText(ConfigPath);
            if (JsonNode.Parse(text) is not JsonObject file)
            {
                Logger.Info("配置文件根节点不是对象，已回退默认配置");
                var fallback = AppConfig.Default();
                TrySave(fallback);
                return fallback;
            }

            var cfg = ParseAndSanitize(text, out var stats, out string? oldVersion);

            // 统一回写：版本号更新为当前程序，废弃字段当场清掉。
            // 写盘失败（只读目录等）只记日志，绝不能因为回写失败把已经合并好的配置丢掉
            TrySave(cfg);

            if (stats.Dropped + stats.Filled + stats.Fallback > 0)
                Logger.Info($"配置已合并（{Describe(oldVersion)} → {AppVersion}）：移除废弃字段 {stats.Dropped} 个、" +
                            $"补齐缺失字段 {stats.Filled} 个、非法值回落默认 {stats.Fallback} 个");
            else if (!VersionMatches(oldVersion))
                Logger.Info($"配置版本 {Describe(oldVersion)} → {AppVersion}：字段结构一致，仅更新版本号");

            return cfg;
        }
        catch (Exception ex)
        {
            // 配置文件完全不可读（不是合法 JSON 等）时才整份回退，并记录日志
            Logger.Error("LoadConfig", ex);
            Logger.Info("配置文件无法解析，本次以默认配置运行（文件保留原样，下次保存配置时覆盖）");
        }
        return AppConfig.Default();
    }

    /// <summary>
    /// 纯文本 → 合并、迁移、钳制后的配置。不碰磁盘，供 Load 与单测共用。
    /// </summary>
    internal static AppConfig ParseAndSanitize(string text, out MergeStats stats, out string? oldVersion)
    {
        stats = new MergeStats();
        oldVersion = null;
        if (JsonNode.Parse(text) is not JsonObject file) return AppConfig.Default();

        MigrateLegacy(file);

        // 以默认配置为骨架逐字段合并：形状对得上的保留用户值，缺失或类型不符的用默认值；
        // 默认配置里没有的键（已废弃字段）不复制，回写时自然消失
        var shape = JsonSerializer.SerializeToNode(AppConfig.Default(), JsonOptions)!.AsObject();
        var cfg = MergeObject(shape, file, stats).Deserialize<AppConfig>(JsonOptions) ?? AppConfig.Default();

        Sanitize(cfg); // 类型对不代表取值合规：越界/非法的值统一钳到界面允许范围
        oldVersion = cfg.ConfigVersion;
        return cfg;
    }

    /// <summary>加载期间的回写：失败只记日志，不影响本次已合并的配置。</summary>
    private static void TrySave(AppConfig cfg)
    {
        try { Save(cfg); }
        catch (Exception ex) { Logger.Error("SaveConfigOnLoad", ex); }
    }

    public static void Save(AppConfig cfg)
    {
        cfg.ConfigVersion = AppVersion; // 每次写盘都盖上当前版本，下次启动即按此判断
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOptions));
    }

    // ---- 合并 ----

    internal sealed class MergeStats
    {
        public int Dropped;  // 文件里有、默认配置里没有的键（已废弃字段）
        public int Filled;   // 文件里没有、用默认值补上的键
        public int Fallback; // 类型或取值不合法、回落默认值的键
    }

    /// <summary>按默认配置的形状逐键合并：值取文件里的，键集合取默认配置的。</summary>
    private static JsonObject MergeObject(JsonObject shape, JsonObject src, MergeStats stats)
    {
        var result = new JsonObject();
        foreach (var (key, shapeValue) in shape)
        {
            var fallback = shapeValue?.DeepClone();
            if (!src.TryGetPropertyValue(key, out var raw) || raw == null)
            {
                stats.Filled++;
                result[key] = fallback;
                continue;
            }

            if (shapeValue == null)
            {
                // 默认配置里没有默认值的字段（如 ConfigVersion）：文件里有就直接沿用
                result[key] = raw.DeepClone();
                continue;
            }

            var merged = MergeValue(shapeValue, raw, stats);
            if (merged == null)
            {
                stats.Fallback++;
                result[key] = fallback;
            }
            else result[key] = merged;
        }

        stats.Dropped += src.Count(kv => !shape.ContainsKey(kv.Key));
        return result;
    }

    private static JsonNode? MergeValue(JsonNode? shape, JsonNode src, MergeStats stats)
    {
        switch (shape)
        {
            case JsonObject obj:
                return src is JsonObject srcObj ? MergeObject(obj, srcObj, stats) : null;

            case JsonArray arr:
            {
                if (src is not JsonArray srcArr) return null;
                // 元素结构取默认数组的第一项：逐项合并，行数保持用户自己的（不补也不删）
                var template = arr.Count > 0 ? arr[0] : null;
                var result = new JsonArray();
                foreach (var item in srcArr)
                {
                    if (template is JsonObject tObj && item is JsonObject iObj) result.Add(MergeObject(tObj, iObj, stats));
                    else result.Add(item?.DeepClone());
                }
                return result;
            }

            case JsonValue value:
            {
                if (src is not JsonValue srcValue || !KindMatches(value.GetValueKind(), srcValue.GetValueKind()))
                    return null;
                // 数字统一校验成 int 范围：手写的 1.5 / 1e9 / 超 int 会让反序列化直接抛异常
                if (srcValue.GetValueKind() == JsonValueKind.Number &&
                    (!long.TryParse(srcValue.ToJsonString(), out long n) || n < int.MinValue || n > int.MaxValue))
                    return null;
                return srcValue.DeepClone();
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// 类型是否一致。JsonValueKind 把 true / false 拆成 True、False 两个值（不是 Boolean），
    /// 直接比 GetValueKind() 会把合法的布尔值误判成类型不符。
    /// </summary>
    private static bool KindMatches(JsonValueKind shape, JsonValueKind src)
    {
        static bool IsBool(JsonValueKind k) => k is JsonValueKind.True or JsonValueKind.False;
        return shape == src || (IsBool(shape) && IsBool(src));
    }

    /// <summary>旧字段迁移：1.2.0 早期回放间隔按毫秒存（PlayGapMs），之后改为按秒（PlayGapSec）。</summary>
    private static void MigrateLegacy(JsonObject cfg)
    {
        if (cfg.ContainsKey(nameof(AppConfig.PlayGapSec))) return;
        if (!cfg.TryGetPropertyValue("PlayGapMs", out var old) || old is not JsonValue v) return;
        if (!long.TryParse(v.ToJsonString(), out long ms)) return;

        int sec = (int)Math.Clamp((ms + 500) / 1000, 0, MacroPlayer.MaxGapSec);
        cfg[nameof(AppConfig.PlayGapSec)] = sec;
        Logger.Info($"配置迁移：PlayGapMs={ms} → PlayGapSec={sec}");
    }

    /// <summary>值合法化：手改文件、旧版本残留都可能越界，统一钳到界面允许的范围后再交给界面。</summary>
    private static void Sanitize(AppConfig cfg)
    {
        cfg.StartHotkey = HotkeyOr(cfg.StartHotkey, "F6");
        cfg.MouseStartHotkey = HotkeyOr(cfg.MouseStartHotkey, "F8");
        cfg.RecordHotkey = HotkeyOr(cfg.RecordHotkey, "F9");
        cfg.PlaybackHotkey = HotkeyOr(cfg.PlaybackHotkey, "F10");

        cfg.Mode = cfg.Mode == "A" ? "A" : "B";
        cfg.PlayMode = cfg.PlayMode is "count" or "duration" ? cfg.PlayMode : "infinite";
        cfg.PlayValue = Math.Clamp(cfg.PlayValue, 1,
            cfg.PlayMode == "duration" ? Limits.PlayMinuteMax : Limits.PlayCountMax);
        cfg.PlayGapSec = Math.Clamp(cfg.PlayGapSec, 0, MacroPlayer.MaxGapSec);

        var defaults = AppConfig.Default();

        cfg.Keys = (cfg.Keys ?? new List<KeyConfig>())
            .Take(Limits.KeyRowsMax)
            .Select(k => new KeyConfig
            {
                Key = (k.Key ?? "").Trim().ToUpperInvariant(),
                IntervalMs = Math.Clamp(k.IntervalMs, Limits.MsMin, Limits.MsMax),
                StopMode = k.StopMode == "auto" ? "auto" : "hold",
                StopValue = Math.Clamp(k.StopValue, 0, Limits.KeyCountMax),
                Enabled = k.Enabled
            }).ToList();
        if (cfg.Keys.Count == 0) cfg.Keys = defaults.Keys; // 空列表回落到默认两行

        // 鼠标固定左/右/中/X1/X2 五行：按 Button 对齐，缺的补默认值，多余的按钮丢弃；
        // 旧配置只有三行时侧键两行自动补默认（关闭状态），不会被清空；
        // v1.1.x 的 count（按住点 N 次）并入 auto
        var byButton = (cfg.MouseClicks ?? new List<MouseClickConfig>())
            .GroupBy(m => (m.Button ?? "").Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        cfg.MouseClicks = defaults.MouseClicks
            .Select(d => byButton.TryGetValue(d.Button, out var m)
                ? new MouseClickConfig
                {
                    Button = d.Button,
                    IntervalMs = Math.Clamp(m.IntervalMs, Limits.MouseMsMin, Limits.MouseMsMax),
                    StopMode = m.StopMode is "auto" or "count" ? "auto" : "infinite",
                    StopValue = Math.Clamp(m.StopValue, 0, Limits.MouseCountMax),
                    Enabled = m.Enabled
                }
                : d)
            .ToList();
    }

    /// <summary>热键必须是 F1–F12，不认识的就回落到该位的默认值。</summary>
    private static string HotkeyOr(string? value, string fallback)
    {
        string s = (value ?? "").Trim().ToUpperInvariant();
        return s.Length >= 2 && s[0] == 'F' && int.TryParse(s[1..], out int n) && n is >= 1 and <= 12
            ? "F" + n
            : fallback;
    }

    /// <summary>版本字段存在且不低于当前程序版本（仅用于日志描述，不再触发重置）。</summary>
    private static bool VersionMatches(string? version) =>
        Version.TryParse(version, out var v) && v >= (Version.TryParse(AppVersion, out var app) ? app : new Version(0, 0, 0));

    private static string Describe(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "(无配置版本字段)" : version;
}
