namespace KeyMouseFactory;

/// <summary>
/// 连发目标键的键名 ↔ 虚拟键码映射。键名是界面与配置文件共用的唯一表示：
/// A-Z、0-9 与符号键直接用字符本身，空格用 SPC，小键盘数字用 N0-N9。
/// 只收录游戏里真正会当技能键用的键位——功能键、编辑键、方向键都不收录：
/// F 键与热键抢同一批键，方向键与编辑键在游戏里没有连发价值，还会把光标导航一并吃掉。
/// 修饰键同理不收录（连发无意义，且「拦截原生」会把系统 Shift/Ctrl 一起拦掉）。
/// </summary>
internal static class KeyNames
{
    /// <summary>键名最大字符数（SPC 三个字符），键名输入框按它放宽 MaxLength。</summary>
    public const int MaxLength = 3;

    /// <summary>状态栏与提示统一用这句，避免各处的支持列表写得不一样。</summary>
    public const string Help =
        "支持 A-Z、0-9、空格 SPC、小键盘 N0-N9，以及符号键 ; = , - . / ` [ \\ ] '";

    // 符号键按 VK 记录，不受键盘布局下实际字符的影响
    private static readonly Dictionary<string, int> Named = new(StringComparer.Ordinal)
    {
        ["SPC"] = 0x20,   // 空格
        [";"] = 0xBA,
        ["="] = 0xBB,
        [","] = 0xBC,
        ["-"] = 0xBD,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        ["`"] = 0xC0,
        ["["] = 0xDB,
        ["\\"] = 0xDC,
        ["]"] = 0xDD,
        ["'"] = 0xDE,
    };

    private static readonly Dictionary<int, string> ByVk = BuildReverse();

    /// <summary>全部合法键名（自检与文档用）。</summary>
    public static IEnumerable<string> All()
    {
        for (char c = 'A'; c <= 'Z'; c++) yield return c.ToString();
        for (char c = '0'; c <= '9'; c++) yield return c.ToString();
        for (int i = 0; i <= 9; i++) yield return "N" + i;
        foreach (string name in Named.Keys) yield return name;
    }

    private static Dictionary<int, string> BuildReverse()
    {
        var map = new Dictionary<int, string>();
        for (char c = 'A'; c <= 'Z'; c++) map[c] = c.ToString();
        for (char c = '0'; c <= '9'; c++) map[c] = c.ToString();
        for (int i = 0; i <= 9; i++) map[0x60 + i] = "N" + i;      // 小键盘数字
        foreach (var kv in Named) map[kv.Value] = kv.Key;
        return map;
    }

    /// <summary>键名文本 → 虚拟键码；-1 表示非法。</summary>
    public static int ToVk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        string s = text.Trim().ToUpperInvariant();

        if (s.Length == 1)
        {
            char c = s[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        // 两字符 N0-N9 表示小键盘数字（VK_NUMPAD0=0x60），与主键盘数字区分开
        if (s.Length == 2 && s[0] == 'N' && s[1] is >= '0' and <= '9')
            return 0x60 + (s[1] - '0');

        return Named.TryGetValue(s, out int vk) ? vk : -1;
    }

    /// <summary>物理按键 → 规范键名；null 表示这个键不支持作为连发目标。</summary>
    public static string? FromKeys(Keys key)
    {
        int vk = (int)(key & Keys.KeyCode);
        return ByVk.TryGetValue(vk, out string? name) ? name : null;
    }

    /// <summary>修饰键：单独按下时不作为目标键捕获，也不拦截（要留给 Ctrl+V 这类组合）。</summary>
    public static bool IsModifier(Keys key) => (key & Keys.KeyCode) is
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;

    /// <summary>
    /// 是否为扩展键（扫描码需要 E0 前缀）。方向键、Home/End/PgUp/PgDn/Ins/Del 都在此列。
    /// 当前支持集里没有扩展键，这条分支留着是防御性的：按扫描码注入时系统不会替你补 E0，
    /// 一旦以后把方向键之类加回来，少了它目标程序会把 0x48 当成小键盘 8。
    /// </summary>
    public static bool IsExtended(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or   // PgUp/PgDn/End/Home/方向
        0x2D or 0x2E or                                                     // Insert/Delete
        0x90 or 0x6F or                                                     // NumLock/小键盘除号
        0x5B or 0x5C or 0x5D or                                             // 两个 Win 键/菜单键
        0xA3 or 0xA5 or 0x2C;                                               // 右 Ctrl/右 Alt/PrintScreen
}
