using System.Windows.Forms;
using Xunit;

namespace KeyMouseFactory.Tests;

public class ChatKeyTests
{
    [Theory]
    [InlineData("enter", 0x0D)]
    [InlineData("ESC", 0x1B)]
    [InlineData("tab", 0x09)]
    [InlineData("a", 0x41)]
    [InlineData("SPC", 0x20)]
    [InlineData("/", 0xBF)]
    public void ChatKeyNamesMapToExpectedVk(string text, int expected) =>
        Assert.Equal(expected, ChatKeyNames.ToVk(text));

    [Theory]
    [InlineData(Keys.Enter, "ENTER")]
    [InlineData(Keys.Escape, "ESC")]
    [InlineData(Keys.Tab, "TAB")]
    [InlineData(Keys.A, "A")]
    public void PhysicalKeysCaptureChatName(Keys key, string expected) =>
        Assert.Equal(expected, ChatKeyNames.FromKeys(key));

    [Theory]
    [InlineData("F5")]   // 功能键是热键专用，聊天键也不收
    [InlineData("SHIFT")]
    [InlineData("LEFT")]
    [InlineData("X9")]
    [InlineData("")]
    public void InvalidChatKeysAreRejected(string text) =>
        Assert.Equal(-1, ChatKeyNames.ToVk(text));

    [Theory]
    [InlineData("", "ENTER")]       // 空回落默认，绝不为空（守卫失效）
    [InlineData("  ", "ENTER")]
    [InlineData("bad", "ENTER")]    // 非法回落默认
    [InlineData(" enter ", "ENTER")]// 规范化大写去空格
    [InlineData("esc", "ESC")]
    [InlineData("n5", "N5")]
    public void ChatKeySanitizeNormalizes(string raw, string expected) =>
        Assert.Equal(expected, ChatKeyNames.Sanitize(raw));

    [Fact]
    public void ChatKeySetIsSupersetOfKeyNames()
    {
        // 聊天键表必须包含全部连按目标键 + Enter/Esc/Tab 三个专用键
        foreach (string name in KeyNames.All())
            Assert.True(ChatKeyNames.ToVk(name) >= 0, $"聊天键表缺了 {name}");
        foreach (string name in new[] { "ENTER", "ESC", "TAB" })
            Assert.True(ChatKeyNames.ToVk(name) >= 0, $"聊天键表缺了 {name}");

        // 但这三个专用键绝不能混进连按目标键表，否则会把打字键拦掉
        foreach (string name in new[] { "ENTER", "ESC", "TAB" })
            Assert.Equal(-1, KeyNames.ToVk(name));
    }

    [Fact]
    public void AllChatNamesRoundTrip()
    {
        foreach (string name in ChatKeyNames.All())
        {
            int vk = ChatKeyNames.ToVk(name);
            Assert.True(vk > 0, $"{name} 解析失败");
            Assert.Equal(name, ChatKeyNames.FromKeys((Keys)vk));
        }
    }
}
