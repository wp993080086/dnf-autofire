using Xunit;

namespace KeyMouseFactory.Tests;

public class HotkeyFallbackTests
{
    [Fact]
    public void PickSkipsDefaultFourAndReturnsFirstFreeKey() =>
        Assert.Equal("F1", HotkeyFallback.Pick(new[] { "F6", "F8", "F9", "F10" }));

    [Fact]
    public void PickIsCaseInsensitiveAndTrimsWhitespace() =>
        Assert.Equal("F3", HotkeyFallback.Pick(new[] { " f1 ", "F2" }));

    [Fact]
    public void PickReturnsNullWhenAllTwelveTaken() =>
        Assert.Null(HotkeyFallback.Pick(HotkeyFallback.Candidates));

    [Fact]
    public void PickReturnsF1WhenNothingTaken() =>
        Assert.Equal("F1", HotkeyFallback.Pick(Array.Empty<string>()));

    [Fact]
    public void CandidatesAreF1ThroughF12InOrder() =>
        Assert.Equal(Enumerable.Range(1, 12).Select(i => "F" + i), HotkeyFallback.Candidates);

    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F6", 0x75)]
    [InlineData("f12", 0x7B)]
    [InlineData(" F3 ", 0x72)]
    [InlineData("F13", 0x75)]   // 越界回落 F6
    [InlineData("", 0x75)]
    [InlineData("X9", 0x75)]
    [InlineData("5", 0x35)]     // 单字符沿用旧行为
    public void ToVkMatchesTheHotkeyRegistrationRule(string text, int expected) =>
        Assert.Equal(expected, HotkeyFallback.ToVk(text));
}
