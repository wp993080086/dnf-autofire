using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DnfAutoFire;

/// <summary>录制点：t=相对开始的毫秒，k=事件类型，x/y=物理屏幕坐标。</summary>
public struct RecordPoint
{
    [JsonPropertyName("t")] public int T { get; set; }
    [JsonPropertyName("k")] public int K { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
}

/// <summary>事件类型取值（与 RecordPoint.K 对应）。滚轮本期不录，预留 7/8。</summary>
public static class RecordKind
{
    public const int Move = 0;
    public const int LeftDown = 1;
    public const int LeftUp = 2;
    public const int RightDown = 3;
    public const int RightUp = 4;
    public const int MiddleDown = 5;
    public const int MiddleUp = 6;
}

/// <summary>录制时的虚拟桌面快照，用于回放前的分辨率变化提示。</summary>
public class RecordScreen
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("w")] public int W { get; set; }
    [JsonPropertyName("h")] public int H { get; set; }
}

/// <summary>一个录制方案。</summary>
public class RecordProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; }
    [JsonPropertyName("durationMs")] public int DurationMs { get; set; }
    [JsonPropertyName("screen")] public RecordScreen Screen { get; set; } = new();
    [JsonPropertyName("points")] public List<RecordPoint> Points { get; set; } = new();

    /// <summary>列表里显示的时长摘要："总时长 12.4s"（采样点数对用户没意义，不展示）。</summary>
    [JsonIgnore]
    public string Summary => $"总时长 {DurationMs / 1000.0:0.0}s";
}

/// <summary>recordings.json 的根结构。<c>version</c> 预留：将来加滚轮/键盘事件时按版本升级。</summary>
public class RecordFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("profiles")] public List<RecordProfile> Profiles { get; set; } = new();
}

/// <summary>
/// 方案存储：独立文件 <c>recordings.json</c>，不塞进 config.json——
/// 方案可能上万点，混在配置里会拖慢每次勾选/热键变更的落盘。
/// 读取失败一律返回空库并记日志，绝不覆盖原文件（避免用户方案被静默清空）。
/// </summary>
internal static class RecordStore
{
    public const int MaxProfiles = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false, // 60k 点约 2MB，缩进只会让文件翻倍
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string FilePath => Path.Combine(AppPaths.DataDir, "recordings.json");

    public static List<RecordProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<RecordProfile>();
            var file = JsonSerializer.Deserialize<RecordFile>(File.ReadAllText(FilePath), JsonOptions);
            return SanitizeProfiles(file?.Profiles);
        }
        catch (Exception ex)
        {
            Logger.Error("RecordStore.Load", ex);
            return new List<RecordProfile>();
        }
    }

    /// <summary>
    /// 纯函数：脏数据钳制，供 Load 与单测共用，不碰磁盘。
    /// JSON 里显式写 null 会绕过属性的初始化器（= new()），所以 Screen/Points 都要在这里兜底；
    /// 回放路径直接读 Screen.W，漏掉就会 NullReferenceException。
    /// </summary>
    internal static List<RecordProfile> SanitizeProfiles(List<RecordProfile>? source)
    {
        var list = (source ?? new List<RecordProfile>()).Where(p => p != null).ToList();
        foreach (var p in list)
        {
            if (string.IsNullOrEmpty(p.Id)) p.Id = NewId();
            p.Points ??= new List<RecordPoint>();
            p.Screen ??= new RecordScreen();
        }
        // 手改文件塞超量方案时按上限截断（只影响内存，不主动改写文件）
        if (list.Count > MaxProfiles)
        {
            Logger.Info($"recordings.json 方案数 {list.Count} 超过上限，仅加载前 {MaxProfiles} 个");
            list.RemoveRange(MaxProfiles, list.Count - MaxProfiles);
        }
        return list;
    }

    /// <summary>写盘失败会抛出，由调用方 catch 后提示（内存中的方案不丢）。</summary>
    public static void Save(List<RecordProfile> profiles)
    {
        var file = new RecordFile { Profiles = profiles };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>自动命名：优先方案A…方案Z，用尽后退到"方案27"这类数字名。与现有方案名去重。</summary>
    public static string NextName(List<RecordProfile> existing)
    {
        var used = new HashSet<string>(existing.Select(p => p.Name));
        for (char c = 'A'; c <= 'Z'; c++)
        {
            string name = "方案" + c;
            if (!used.Contains(name)) return name;
        }
        for (int i = 1; i < 10000; i++)
        {
            string name = "方案" + i;
            if (!used.Contains(name)) return name;
        }
        return "方案" + NewId();
    }
}
