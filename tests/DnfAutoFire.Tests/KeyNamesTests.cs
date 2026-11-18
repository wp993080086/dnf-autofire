using System.Windows.Forms;
using Xunit;

namespace DnfAutoFire.Tests;

public class KeyNamesTests
{
    [Fact]
    public void AllNamesRoundTripToUniqueVk()
    {
        var names = KeyNames.All().ToList();
        Assert.Equal(58, names.Count); // 26 字母 + 10 数字 + 10 小键盘 + 12 符号/空格

        var seen = new Dictionary<int, string>();
        foreach (string name in names)
        {
            int vk = KeyNames.ToVk(name);
            Assert.True(vk > 0, $"{name} 解析失败");
            Assert.Equal(name, KeyNames.FromKeys((Keys)vk));
            Assert.False(KeyNames.IsExtended(vk), $"{name} 不该标为扩展键");
            Assert.False(seen.ContainsKey(vk), $"{name} 与 {seen.GetValueOrDefault(vk)} 撞 VK");
            seen[vk] = name;
        }
        Assert.Equal(58, seen.Count);
    }

    [Theory]
    [InlineData("a", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData(" n5 ", 0x65)]
    [InlineData("SPC", 0x20)]
    [InlineData(";", 0xBA)]
    [InlineData("/", 0xBF)]
    public void KnownNamesMapToExpectedVk(string text, int expected) =>
        Assert.Equal(expected, KeyNames.ToVk(text));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("F5")]
    [InlineData("LEFT")]
    [InlineData("TAB")]
    [InlineData("BS")]
    [InlineData("PGUP")]
    [InlineData("SHIFT")]
    [InlineData("DEL")]
    [InlineData("X5")]
    [InlineData("NUMPAD5")]
    public void InvalidNamesAreRejected(string text) =>
        Assert.Equal(-1, KeyNames.ToVk(text));

    [Fact]
    public void RemovedKeysAreNotCaptured()
    {
        foreach (Keys key in new[] { Keys.F5, Keys.Up, Keys.Tab, Keys.Home, Keys.Back, Keys.Insert, Keys.LShiftKey })
            Assert.Null(KeyNames.FromKeys(key));
    }

    [Fact]
    public void ExtendedKeysAreFlagged()
    {
        foreach (int vk in new[] { 0x26, 0x24, 0x21, 0x2E })
            Assert.True(KeyNames.IsExtended(vk));
    }

    [Fact]
    public void ModifiersAreRecognized()
    {
        foreach (Keys key in new[] { Keys.LShiftKey, Keys.ControlKey, Keys.Menu, Keys.LWin })
            Assert.True(KeyNames.IsModifier(key));
        Assert.False(KeyNames.IsModifier(Keys.A));
    }
}
