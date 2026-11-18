namespace DnfAutoFire;

/// <summary>
/// 连发目标键的键名 ↔ 虚拟键码映射。键名是界面与配置文件共用的唯一表示：
/// A-Z、0-9 与符号键直接用字符本身，其余用不超过 5 个字符的短名（SPC / F5 / LEFT / PGUP…）。
/// 修饰键（Shift / Ctrl / Alt）不在此列——连发修饰键没有实际用途，而模式 B 会拦截原生输入，
/// 一开就会把系统的大写、组合键全部打瘫。Delete 也不在此列：它在键名框里用于清空。
/// </summary>
internal static class KeyNames
{
    /// <summary>键名最大字符数（RIGHT、PGUP 这一档），键名输入框按它放宽 MaxLength。</summary>
    public const int MaxLength = 5;

    /// <summary>状态栏与提示统一用这句，避免各处的支持列表写得不一样。</summary>
    public const string Help =
        "支持 A-Z、0-9、空格 SPC、F1-F12、方向键、TAB/ENT/ESC/BS/HOME/END/PGUP/PGDN、" +
        "符号键 ; = , - . / ` [ \\ ] '，小键盘数字用 N0-N9";

    // 短名键与符号键：符号键按 VK 记录，不受键盘布局下实际字符的影响
    private static readonly Dictionary<string, int> Named = new(StringComparer.Ordinal)
    {
        ["SPC"] = 0x20,   // 空格
        ["TAB"] = 0x09,
        ["ENT"] = 0x0D,   // 回车
        ["ESC"] = 0x1B,
        ["BS"] = 0x08,    // 退格
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PGUP"] = 0x21,
        ["PGDN"] = 0x22,
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
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
        for (int i = 1; i <= 12; i++) yield return "F" + i;
        foreach (string name in Named.Keys) yield return name;
    }

    private static Dictionary<int, string> BuildReverse()
    {
        var map = new Dictionary<int, string>();
        for (char c = 'A'; c <= 'Z'; c++) map[c] = c.ToString();
        for (char c = '0'; c <= '9'; c++) map[c] = c.ToString();
        for (int i = 0; i <= 9; i++) map[0x60 + i] = "N" + i;      // 小键盘数字
        for (int i = 1; i <= 12; i++) map[0x6F + i] = "F" + i;     // F1-F12
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

        // F1-F12（VK_F1=0x70）
        if (s.Length >= 2 && s.Length <= 3 && s[0] == 'F' && s[1] != '0'
            && int.TryParse(s.AsSpan(1), out int fn) && fn is >= 1 and <= 12)
            return 0x6F + fn;

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
    /// 是否为扩展键（扫描码需要 E0 前缀）。方向键、Home/End/PgUp/PgDn/Ins/Del 都在此列：
    /// 扫描码注入时系统不会替你补 E0，少了它目标程序会把 0x48 当成小键盘 8。
    /// </summary>
    public static bool IsExtended(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or   // PgUp/PgDn/End/Home/方向
        0x2D or 0x2E or                                                     // Insert/Delete
        0x90 or 0x6F or                                                     // NumLock/小键盘除号
        0x5B or 0x5C or 0x5D or                                             // 两个 Win 键/菜单键
        0xA3 or 0xA5 or 0x2C;                                               // 右 Ctrl/右 Alt/PrintScreen
}
