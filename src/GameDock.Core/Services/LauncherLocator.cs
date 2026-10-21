namespace GameDock.Core.Services;

/// <summary>探测本机常见启动器目录（PCL2 / HMCL / 官方 / 其他），供默认选择。</summary>
public static class LauncherLocator
{
    public sealed record LauncherDir(string Name, string McDir, bool Exists);

    /// <summary>返回候选启动器目录（按常见程度排序）。</summary>
    public static IReadOnlyList<LauncherDir> Detect()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new List<LauncherDir>
        {
            new("PCL2 / HMCL 默认", Path.Combine(appdata, ".minecraft"), true),
            new("官方启动器", Path.Combine(appdata, ".minecraft"), true),
            new("BMCLAPI 默认（便携）", Path.Combine(appdata, ".minecraft"), true),
        };

        // 常见自定义位置（存在才列出）。
        var extra = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ".minecraft"),
            @"D:\MCLDownload",
            @"D:\MC\.minecraft",
        };
        foreach (var dir in extra)
        {
            if (Directory.Exists(dir))
                candidates.Add(new LauncherDir(Path.GetFileName(Path.GetDirectoryName(dir)) ?? dir, dir, true));
        }

        return candidates;
    }
}
