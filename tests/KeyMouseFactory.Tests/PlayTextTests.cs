using Xunit;

namespace KeyMouseFactory.Tests;

public class PlayTextTests
{
    [Theory]
    [InlineData(0L, "00:00")]
    [InlineData(999L, "00:00")]
    [InlineData(1000L, "00:01")]
    [InlineData(59999L, "00:59")]
    [InlineData(60000L, "01:00")]
    [InlineData(3600000L, "60:00")]
    public void DurationFormatsMilliseconds(long ms, string expected) =>
        Assert.Equal(expected, PlayText.Duration(ms));

    [Fact]
    public void LoopTextOmitsTotalForInfinitePlayback() =>
        Assert.Equal("第 3 次", PlayText.Loop(3, 0));

    [Fact]
    public void LoopTextShowsTotalWhenCounted() =>
        Assert.Equal("第 3/10 次", PlayText.Loop(3, 10));
}
