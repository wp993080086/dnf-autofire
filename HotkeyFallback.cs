using System.Runtime.InteropServices;

namespace KeyMouseFactory;

/// <summary>热键占用时的纯逻辑：在 F1–F12 里挑一个还没被占用的键。</summary>
internal static class HotkeyFallback
{
    /// <summary>候选顺序固定，便于预测与测试。</summary>
    public static readonly string[] Candidates = Enumerable.Range(1, 12).Select(i => "F" + i).ToArray();

    /// <summary>按 F1→F12 返回第一个不在 unavailable 里的键；全占满返回 null。</summary>
    public static string? Pick(IEnumerable<string> unavailable)
    {
        var used = new HashSet<string>(
            unavailable.Select(s => (s ?? "").Trim().ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in Candidates)
            if (!used.Contains(candidate)) return candidate;
        return null;
    }

    /// <summary>热键文本 → VK。F1=0x70；单字符沿用旧行为；无法识别回落到 F6。</summary>
    public static int ToVk(string? s)
    {
        string text = (s ?? "").Trim().ToUpperInvariant();
        if (text.Length >= 2 && text[0] == 'F'
            && int.TryParse(text[1..], out int n) && n is >= 1 and <= 12)
            return 0x70 + n - 1;
        if (text.Length == 1 && char.IsLetterOrDigit(text[0])) return text[0];
        return 0x75; // 兜底 F6
    }
}

/// <summary>单实例互相唤起用的 Win32 消息。</summary>
internal static class SingleInstanceSignal
{
    private const string MessageName = "KeyMouseFactory_ShowWindow_v1";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);

    private static readonly uint MessageId = RegisterWindowMessage(MessageName);

    /// <summary>注册消息在所有进程里拿到同一个 ID；注册失败时为 0，此时不发送。</summary>
    public static int Message => (int)MessageId;

    /// <summary>通知已运行的实例把窗口叫到前台。广播给所有顶层窗口，只有本程序会处理。</summary>
    public static void Notify()
    {
        if (MessageId == 0) return;
        // HWND_BROADCAST = 0xffff
        NativeMethods.PostMessage(new IntPtr(0xffff), (int)MessageId, IntPtr.Zero, IntPtr.Zero);
    }
}
