using Xunit;

namespace KeyMouseFactory.Tests;

public class ConfigManagerTests
{
    private static AppConfig Parse(string json) =>
        ConfigManager.ParseAndSanitize(json, out _, out _);

    [Fact]
    public void MissingFieldsAreFilledFromDefaults()
    {
        var cfg = Parse("""{"StartHotkey":"F7","Mode":"A"}""");

        Assert.Equal("F7", cfg.StartHotkey);
        Assert.Equal("F8", cfg.MouseStartHotkey);   // 没写就补默认
        Assert.Equal("A", cfg.Mode);
        Assert.Equal(2, cfg.Keys.Count);
        Assert.Equal(new[] { "L", "R", "M", "X1", "X2" }, cfg.MouseClicks.Select(m => m.Button));
    }

    [Fact]
    public void OutOfRangeValuesAreClampedAndInvalidEnumsFallBack()
    {
        var cfg = Parse("""
        {
          "Keys": [{"Key":"x","IntervalMs":-100,"StopMode":"bad","StopValue":999999,"Enabled":true}],
          "MouseClicks": [{"Button":"L","IntervalMs":999999,"StopMode":"count","StopValue":-3,"Enabled":true}],
          "PlayMode":"duration","PlayValue":999999
        }
        """);

        var key = Assert.Single(cfg.Keys);
        Assert.Equal("X", key.Key);
        Assert.Equal(Limits.MsMin, key.IntervalMs);
        Assert.Equal("hold", key.StopMode);
        Assert.Equal(Limits.KeyCountMax, key.StopValue);

        var mouse = cfg.MouseClicks.First(m => m.Button == "L");
        Assert.Equal(Limits.MouseMsMax, mouse.IntervalMs);
        Assert.Equal("auto", mouse.StopMode);      // 旧 count 并入 auto
        Assert.Equal(0, mouse.StopValue);          // 负数钳到 0
        Assert.Equal(5, cfg.MouseClicks.Count);
        Assert.All(cfg.MouseClicks.Where(m => m.Button is "X1" or "X2"), m => Assert.False(m.Enabled));

        Assert.Equal("duration", cfg.PlayMode);
        Assert.Equal(Limits.PlayMinuteMax, cfg.PlayValue);
    }

    [Theory]
    [InlineData("X9")]
    [InlineData("")]
    [InlineData("SHIFT")]
    [InlineData("F13")]
    public void InvalidHotkeyFallsBackToSlotDefault(string hotkey)
    {
        var cfg = Parse($"{{\"StartHotkey\":\"{hotkey}\"}}");
        Assert.Equal("F6", cfg.StartHotkey);
    }

    [Fact]
    public void LegacyThreeMouseRowsGainDisabledSideButtonRows()
    {
        var cfg = Parse("""
        {"MouseClicks":[
          {"Button":"L","IntervalMs":250,"StopMode":"infinite","StopValue":1,"Enabled":true},
          {"Button":"R","IntervalMs":300,"StopMode":"infinite","StopValue":1,"Enabled":false},
          {"Button":"M","IntervalMs":400,"StopMode":"infinite","StopValue":1,"Enabled":false}
        ]}
        """);

        Assert.Equal(5, cfg.MouseClicks.Count);
        Assert.Equal(250, cfg.MouseClicks.First(m => m.Button == "L").IntervalMs);
        foreach (string button in new[] { "X1", "X2" })
        {
            var row = cfg.MouseClicks.First(m => m.Button == button);
            Assert.False(row.Enabled);
            Assert.Equal(100, row.IntervalMs); // AppConfig.Default() 里鼠标行是 100，与界面新增行的 300 不同
        }
    }

    [Fact]
    public void UnknownButtonAndDuplicateRowsAreDiscarded()
    {
        var cfg = Parse("""
        {"MouseClicks":[
          {"Button":"Z","IntervalMs":777,"StopMode":"infinite","StopValue":1,"Enabled":true},
          {"Button":"L","IntervalMs":222,"StopMode":"infinite","StopValue":1,"Enabled":true},
          {"Button":"L","IntervalMs":333,"StopMode":"infinite","StopValue":1,"Enabled":false}
        ]}
        """);

        Assert.Equal(5, cfg.MouseClicks.Count);
        var left = cfg.MouseClicks.First(m => m.Button == "L");
        Assert.Equal(222, left.IntervalMs); // 同按钮取第一条
        Assert.True(left.Enabled);
    }

    [Fact]
    public void LegacyPlayGapMsIsConvertedToSeconds()
    {
        var cfg = Parse("""{"PlayGapMs":1500}""");
        Assert.Equal(2, cfg.PlayGapSec);
    }

    [Fact]
    public void WrongValueTypeFallsBackToDefault()
    {
        ConfigManager.ParseAndSanitize("""{"Mode":123,"ObsoleteThing":true}""",
            out var stats, out _);
        Assert.True(stats.Fallback >= 1);
        Assert.True(stats.Dropped >= 1);
    }

    [Fact]
    public void NonObjectRootFallsBackToDefault()
    {
        Assert.Equal(2, Parse("[1,2,3]").Keys.Count);
        Assert.Equal(5, Parse("[1,2,3]").MouseClicks.Count);
    }

    [Fact]
    public void OldConfigGetsAntiTypingDefaults()
    {
        // 防打字干扰是新增字段：旧配置没有时用默认值（开 + Enter），且聊天键非法值回落
        var cfg = Parse("""{"StartHotkey":"F6"}""");
        Assert.True(cfg.AntiTyping);
        Assert.Equal("ENTER", cfg.ChatKey);

        var dirty = Parse("""{"AntiTyping":true,"ChatKey":"F5"}""");
        Assert.True(dirty.AntiTyping);
        Assert.Equal("ENTER", dirty.ChatKey); // F5 不是合法聊天键 → 回落默认

        var custom = Parse("""{"AntiTyping":false,"ChatKey":"esc"}""");
        Assert.False(custom.AntiTyping);
        Assert.Equal("ESC", custom.ChatKey);
    }
}
