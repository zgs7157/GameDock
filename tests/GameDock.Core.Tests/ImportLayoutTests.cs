using GameDock.Core.Services;
using Xunit;

namespace GameDock.Core.Tests;

public class ImportLayoutTests
{
    [Fact]
    public void Paths_FollowLauncherConvention()
    {
        var imp = new ImportService();
        var mc = @"C:\mc";

        Assert.Equal(@"C:\mc\versions\1.21.4\1.21.4.json", Path.Combine(imp.VersionDir(mc, "1.21.4"), "1.21.4.json"));
        Assert.Equal(@"C:\mc\libraries\com\mojang\patchy\1.1\patchy-1.1.jar",
            imp.LibraryPath(mc, @"com/mojang/patchy/1.1/patchy-1.1.jar"));
        Assert.Equal(@"C:\mc\assets\objects\ab\abcdef", imp.AssetObjectPath(mc, "abcdef"));
        Assert.Equal(@"C:\mc\assets\indexes\14.json", imp.AssetIndexPath(mc, "14"));
    }

    [Fact]
    public void RuleEvaluator_WindowsRules()
    {
        var os = RuleEvaluator.CurrentOsName;
        Assert.Contains(os, new[] { "windows", "osx", "linux" });

        // 空规则 → 允许。
        Assert.True(RuleEvaluator.Allowed(null));
        Assert.True(RuleEvaluator.Allowed(new()));

        // disallow 当前系统 → 拒绝。
        var disallow = new List<GameDock.Core.Models.LibraryRule>
        {
            new() { Action = "disallow", Os = new GameDock.Core.Models.OsMatcher { Name = os } },
        };
        Assert.False(RuleEvaluator.Allowed(disallow));

        // allow 其他系统 → 拒绝（因为默认拒绝 + 无匹配 allow）。
        var allowOther = new List<GameDock.Core.Models.LibraryRule>
        {
            new() { Action = "allow", Os = new GameDock.Core.Models.OsMatcher { Name = os == "windows" ? "linux" : "windows" } },
        };
        Assert.False(RuleEvaluator.Allowed(allowOther));
    }
}
