using System.Drawing;
using Xunit;

namespace DnfAutoFire.Tests;

public class NativeInputTests
{
    [Fact]
    public void ScreenCornersMapToNormalizedExtremes()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        Assert.Equal((0, 0), NativeInput.NormalizePointer(0, 0, screen));
        Assert.Equal((65535, 65535), NativeInput.NormalizePointer(1920, 1080, screen));
    }

    [Fact]
    public void OutOfScreenCoordinatesAreClamped()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        Assert.Equal((0, 0), NativeInput.NormalizePointer(-500, -500, screen));
        Assert.Equal((65535, 65535), NativeInput.NormalizePointer(9999, 9999, screen));
    }

    [Fact]
    public void CenterIsRoughlyMiddle()
    {
        var (x, y) = NativeInput.NormalizePointer(960, 540, new Rectangle(0, 0, 1920, 1080));
        Assert.InRange(x, 32000, 33535);
        Assert.InRange(y, 32000, 33535);
    }

    [Fact]
    public void NegativeVirtualDesktopOriginIsHandled()
    {
        // 副屏在主屏左侧时，虚拟桌面从 -1920 开始
        var desk = new Rectangle(-1920, 0, 3840, 1080);
        Assert.Equal((0, 0), NativeInput.NormalizePointer(-1920, 0, desk));
        var (x, _) = NativeInput.NormalizePointer(0, 0, desk);
        Assert.InRange(x, 32700, 32850);
    }

    [Fact]
    public void DegenerateRectangleDoesNotDivideByZero()
    {
        // 宽高 1 时按最小跨度 2 处理，坐标远超边界应被钳住而不是抛异常
        Assert.Equal((65535, 65535), NativeInput.NormalizePointer(5, 5, new Rectangle(0, 0, 1, 1)));
    }
}
