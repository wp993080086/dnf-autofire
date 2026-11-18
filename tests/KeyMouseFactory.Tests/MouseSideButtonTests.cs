using Xunit;

namespace KeyMouseFactory.Tests;

public class MouseSideButtonTests
{
    [Theory]
    [InlineData(0x00010000u, 5)] // XBUTTON1 在 mouseData 高字
    [InlineData(0x00020000u, 6)] // XBUTTON2
    public void XButtonVkReadsHighWord(uint mouseData, int expected) =>
        Assert.Equal(expected, MouseHook.XButtonVk(mouseData));

    [Theory]
    [InlineData(0u)]
    [InlineData(0x00030000u)]
    [InlineData(0x00000001u)] // 滚轮数据在低字，不是侧键
    public void NonSideButtonDataIsIgnored(uint mouseData) =>
        Assert.Null(MouseHook.XButtonVk(mouseData));

    [Fact]
    public void InjectionUsesXdownXupWithSideButtonData()
    {
        Assert.Equal((NativeMethods.MOUSEEVENTF_XDOWN, 1u),
            NativeInput.MouseButtonSpec(NativeMethods.VK_XBUTTON1, false));
        Assert.Equal((NativeMethods.MOUSEEVENTF_XUP, 1u),
            NativeInput.MouseButtonSpec(NativeMethods.VK_XBUTTON1, true));
        Assert.Equal((NativeMethods.MOUSEEVENTF_XDOWN, 2u),
            NativeInput.MouseButtonSpec(NativeMethods.VK_XBUTTON2, false));
        Assert.Equal((NativeMethods.MOUSEEVENTF_XUP, 2u),
            NativeInput.MouseButtonSpec(NativeMethods.VK_XBUTTON2, true));
    }

    [Fact]
    public void OrdinaryButtonsKeepTheirOriginalFlags()
    {
        Assert.Equal((NativeMethods.MOUSEEVENTF_LEFTDOWN, 0u),
            NativeInput.MouseButtonSpec(NativeMethods.VK_LBUTTON, false));
        Assert.Equal((NativeMethods.MOUSEEVENTF_RIGHTUP, 0u),
            NativeInput.MouseButtonSpec(NativeMethods.VK_RBUTTON, true));
        Assert.Equal((NativeMethods.MOUSEEVENTF_MIDDLEDOWN, 0u),
            NativeInput.MouseButtonSpec(NativeMethods.VK_MBUTTON, false));
    }

    [Theory]
    [InlineData("L", 1)]
    [InlineData("R", 2)]
    [InlineData("M", 4)]
    [InlineData("X1", 5)]
    [InlineData("X2", 6)]
    public void ConfigButtonMapsToVk(string button, int expected) =>
        Assert.Equal(expected, MainForm.MouseButtonVk(button));

    [Theory]
    [InlineData(5, "侧键 X1")]
    [InlineData(6, "侧键 X2")]
    public void VkHasReadableName(int vk, string expected) =>
        Assert.Equal(expected, MainForm.MouseButtonName(vk));

    [Fact]
    public void DefaultConfigContainsFiveDisabledMouseRows()
    {
        var rows = AppConfig.Default().MouseClicks;
        Assert.Equal(new[] { "L", "R", "M", "X1", "X2" }, rows.Select(r => r.Button));
        Assert.All(rows, r => Assert.False(r.Enabled));
    }
}
