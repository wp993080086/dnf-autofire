namespace DnfAutoFire;

/// <summary>回放进度文案：从界面里抽出的纯函数，便于单测覆盖边界值。</summary>
internal static class PlayText
{
    /// <summary>毫秒 → mm:ss（分钟不进位到小时）。</summary>
    public static string Duration(long ms) => $"{ms / 60000:00}:{ms % 60000 / 1000:00}";

    /// <summary>循环次数：有总次数显示「第 N/M 次」，无限次只显示「第 N 次」。</summary>
    public static string Loop(int current, int total) =>
        total > 0 ? $"第 {current}/{total} 次" : $"第 {current} 次";
}
