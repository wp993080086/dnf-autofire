using Xunit;

namespace KeyMouseFactory.Tests;

public class TypingGuardTests
{
    [Theory]
    [InlineData(false, false, false, false)] // 关闭：任何情况都不暂停
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]  // 开启、在游戏、没打字：正常连按
    [InlineData(true, true, false, true)]    // 游戏内聊天态：暂停
    [InlineData(true, false, true, true)]    // 焦点离开游戏：暂停
    [InlineData(true, true, true, true)]     // 两个来源同时成立：暂停
    public void ShouldPauseCombinesTwoSources(bool enabled, bool chatTyping, bool focusLost, bool expected) =>
        Assert.Equal(expected, TypingGuard.ShouldPause(enabled, chatTyping, focusLost));
}
