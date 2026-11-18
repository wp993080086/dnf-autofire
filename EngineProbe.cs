#if DEBUG
using System.Diagnostics;

namespace KeyMouseFactory;

/// <summary>
/// 调试用采样器：DryRun 模式下把每次注入的时间戳记下来，引擎停止时打印间隔统计。
/// 用来对比「改前 / 改后」的调度精度（均值、极值、迟到次数），Release 构建里整段不参与编译。
/// </summary>
internal static class EngineProbe
{
    private const int ChKey = 0;
    private const int ChMouse = 1;
    private const int MaxSamples = 200000;

    private static readonly List<long>[] _down = { new(), new() };
    private static readonly List<long>[] _up = { new(), new() };
    private static readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>记录一次注入事件（down=false 表示抬起）。</summary>
    public static void Record(int channel, bool up)
    {
        var list = up ? _up[channel] : _down[channel];
        if (list.Count >= MaxSamples) return;
        list.Add(_clock.ElapsedTicks);
    }

    public static void Clear()
    {
        foreach (var l in _down) l.Clear();
        foreach (var l in _up) l.Clear();
    }

    /// <summary>打印按下之间的间隔统计（一次点击 = 按下→抬起，所以按下到按下就是实际连按周期）。</summary>
    public static void Report(int channel, string tag)
    {
        var downs = _down[channel];
        if (downs.Count < 2)
        {
            Clear();   // Start 里会先调一次 Stop，此时还没有样本，静默跳过
            return;
        }

        double toMs = 1000.0 / Stopwatch.Frequency;
        double span = (downs[^1] - downs[0]) * toMs;
        double sum = 0, min = double.MaxValue, max = 0;
        int late = 0, late10 = 0;
        double mean = span / (downs.Count - 1);
        for (int i = 1; i < downs.Count; i++)
        {
            double d = (downs[i] - downs[i - 1]) * toMs;
            sum += d;
            if (d < min) min = d;
            if (d > max) max = d;
            if (d > mean + 5) late++;
            if (d > mean + 10) late10++;
        }
        double avg = sum / (downs.Count - 1);
        double var = 0;
        for (int i = 1; i < downs.Count; i++)
        {
            double d = (downs[i] - downs[i - 1]) * toMs - avg;
            var += d * d;
        }
        double sd = Math.Sqrt(var / (downs.Count - 1));

        // 按下保持时长（按下→抬起配对）
        double holdAvg = 0;
        int pairs = Math.Min(downs.Count, _up[channel].Count);
        if (pairs > 0)
        {
            double hs = 0;
            for (int i = 0; i < pairs; i++) hs += (_up[channel][i] - downs[i]) * toMs;
            holdAvg = hs / pairs;
        }

        Console.WriteLine($"[probe] {tag} 次数={downs.Count} 跨度={span:0.0}ms 周期均值={avg:0.00}ms " +
                          $"最小={min:0.0} 最大={max:0.0} 标准差={sd:0.00} " +
                          $"迟到>5ms={late} 迟到>10ms={late10} 保持均值={holdAvg:0.0}ms");
        Clear();
    }
}
#endif
