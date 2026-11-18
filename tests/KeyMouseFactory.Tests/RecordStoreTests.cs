using Xunit;

namespace KeyMouseFactory.Tests;

public class RecordStoreTests
{
    [Fact]
    public void NullScreenIsFilledSoReplayCanReadDimensions()
    {
        // 回放直接读 profile.Screen.W：JSON 里显式 "screen":null 会绕过属性初始化器，
        // 过去在 Load 后仍是 null，一点回放就 NullReferenceException。
        var list = RecordStore.SanitizeProfiles(new List<RecordProfile>
        {
            new() { Id = "a", Screen = null! }
        });

        var p = Assert.Single(list);
        Assert.NotNull(p.Screen);
        Assert.Equal(0, p.Screen.W); // 0 表示"录制时未记录尺寸"，回放会跳过分辨率提示
        _ = p.Screen.W + p.Screen.H; // 不再抛
    }

    [Fact]
    public void NullPointsAndEmptyIdAreFilled()
    {
        var list = RecordStore.SanitizeProfiles(new List<RecordProfile>
        {
            new() { Points = null!, Id = "" }
        });

        var p = Assert.Single(list);
        Assert.Empty(p.Points);
        Assert.False(string.IsNullOrEmpty(p.Id));
        Assert.Equal(8, p.Id.Length);
    }

    [Fact]
    public void NullProfileEntriesAreDropped()
    {
        var list = RecordStore.SanitizeProfiles(new List<RecordProfile>
        {
            null!, new() { Id = "keep" }, null!
        });

        var p = Assert.Single(list);
        Assert.Equal("keep", p.Id);
    }

    [Fact]
    public void OverLimitProfilesAreTruncated()
    {
        var source = Enumerable.Range(0, RecordStore.MaxProfiles + 5)
            .Select(i => new RecordProfile { Id = "p" + i }).ToList();

        var list = RecordStore.SanitizeProfiles(source);

        Assert.Equal(RecordStore.MaxProfiles, list.Count);
        Assert.Equal("p0", list[0].Id);
    }

    [Fact]
    public void ValidProfilesAreKeptIntact()
    {
        var screen = new RecordScreen { X = -1920, Y = 0, W = 3840, H = 1080 };
        var source = new List<RecordProfile>
        {
            new() { Id = "x", Name = "方案A", Screen = screen,
                    Points = new List<RecordPoint> { new() { T = 12, K = 1, X = 5, Y = 6 } } }
        };

        var p = Assert.Single(RecordStore.SanitizeProfiles(source));

        Assert.Equal("x", p.Id);
        Assert.Equal("方案A", p.Name);
        Assert.Equal(-1920, p.Screen.X);
        Assert.Equal(3840, p.Screen.W);
        var pt = Assert.Single(p.Points);
        Assert.Equal((12, 1, 5, 6), (pt.T, pt.K, pt.X, pt.Y));
    }
}
