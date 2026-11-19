namespace KeyMouseFactory;

/// <summary>
/// 聊天呼出键的键名映射。刻意与 <see cref="KeyNames"/>（连按目标键）分开：
/// 默认 Enter，也支持 ESC / TAB 这类常见的"打开聊天框"键，但这些键绝不能出现在
/// 连按目标键下拉里——否则又会把打字要用的键拦掉。聊天键只用于切换打字暂停态，
/// 按下时放行给游戏（不能吞），否则聊天框根本打不开。
/// </summary>
internal static class ChatKeyNames
{
    /// <summary>默认聊天键：回车。</summary>
    public const string Default = "ENTER";

    /// <summary>键名最大字符数（ENTER 五个字符）。</summary>
    public const int MaxLength = 5;

    public const string Help = "ENTER / ESC / TAB，或 A-Z、0-9、空格 SPC、小键盘 N0-N9 与符号键";

    // 聊天专用键：Enter/Esc/Tab。显示名用全大写，配置里也存这个规范名。
    private static readonly Dictionary<string, int> Extra = new(StringComparer.Ordinal)
    {
        ["ENTER"] = 0x0D,
        ["ESC"] = 0x1B,
        ["TAB"] = 0x09,
    };

    private static readonly Dictionary<int, string> ExtraByVk = new()
    {
        [0x0D] = "ENTER",
        [0x1B] = "ESC",
        [0x09] = "TAB",
    };

    /// <summary>全部合法聊天键名（界面提示/自检用）。</summary>
    public static IEnumerable<string> All()
    {
        foreach (string name in Extra.Keys) yield return name;
        foreach (string name in KeyNames.All()) yield return name;
    }

    /// <summary>键名文本 → 虚拟键码；-1 表示非法。先查聊天专用键，再回落到连按键名表。</summary>
    public static int ToVk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        string s = text.Trim().ToUpperInvariant();
        if (Extra.TryGetValue(s, out int vk)) return vk;
        return KeyNames.ToVk(s);
    }

    /// <summary>物理按键 → 规范聊天键名；null 表示不支持。</summary>
    public static string? FromKeys(Keys key)
    {
        int vk = (int)(key & Keys.KeyCode);
        if (ExtraByVk.TryGetValue(vk, out string? name)) return name;
        return KeyNames.FromKeys(key);
    }

    /// <summary>读配置时规范化：大写；非法值回落到默认 ENTER（绝不留空，否则守卫失效）。</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Default;
        string s = text.Trim().ToUpperInvariant();
        return ToVk(s) >= 0 ? s : Default;
    }
}
