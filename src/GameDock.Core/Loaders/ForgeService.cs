using GameDock.Core.Mirrors;
using GameDock.Core.Models;
using GameDock.Core.Services;
using GameDock.Core.Util;

namespace GameDock.Core.Loaders;

/// <summary>
/// Forge：从 promotions_slim.json 取推荐/最新版本，下载 maven 安装器，
/// 有 Java 时自动执行 --installClient，否则仅下载。
/// </summary>
public sealed class ForgeService
{
    private const string PromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string MavenBase = "https://maven.minecraftforge.net";

    private readonly McJsonClient _json;
    private readonly DownloadManager _dm;
    private readonly IReadOnlyList<IMcMirror> _mirrors;

    public ForgeService(HttpClient http, IReadOnlyList<IMcMirror> mirrors, int threads, int retryCount)
    {
        _mirrors = mirrors;
        _json = new McJsonClient(http, retryCount);
        _dm = new DownloadManager(http, threads, retryCount);
    }

    /// <summary>获取某 MC 版本的 Forge 版本（推荐 + 最新）。</summary>
    public async Task<List<string>> GetForgeVersionsAsync(string mcVersion, CancellationToken ct)
    {
        var map = await _json.GetJsonAsync<ForgePromotionMap>(PromotionsUrl, _mirrors, ct)
            ?? throw new IOException("无法获取 Forge promotions 数据");
        var list = new List<string>();
        var rec = map.GetRecommended(mcVersion);
        var latest = map.GetLatest(mcVersion);
        if (rec is not null) list.Add(rec);
        if (latest is not null && latest != rec) list.Add(latest);
        return list.Distinct().ToList();
    }

    /// <summary>构造安装器下载地址。</summary>
    public string InstallerUrl(string mcVersion, string forgeVersion)
        => $"{MavenBase}/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";

    /// <summary>下载（可选自动安装）Forge 安装器。</summary>
    public async Task<LoaderInstallResult> DownloadInstallerAsync(
        string mcVersion,
        string forgeVersion,
        string destDir,
        bool autoRun,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var fileName = $"forge-{mcVersion}-{forgeVersion}-installer.jar";
        var dest = Path.Combine(destDir, fileName);
        var url = InstallerUrl(mcVersion, forgeVersion);

        log?.Report($"下载 Forge 安装器：{fileName}…");
        await _dm.DownloadAsync(new[]
        {
            new DownloadEntry
            {
                Kind = "installer",
                DisplayName = fileName,
                DestPath = dest,
                UrlCandidates = UrlCandidates.Build(url, _mirrors),
            },
        }, progress, ct);

        var result = new LoaderInstallResult { ArtifactPath = dest };

        if (!autoRun)
        {
            result.Success = true;
            result.ArtifactOnly = true;
            result.Message = $"Forge 安装器已下载：{dest}（未自动安装）";
            return result;
        }

        var java = JavaLocator.FindCached();
        if (java is null)
        {
            result.Success = false;
            result.Message = $"安装器已下载：{dest}，但未找到 Java，请安装 Java 后手动运行：java -jar \"{dest}\" --installClient <启动器目录>";
            return result;
        }

        log?.Report($"用 Java {java.Major} 运行 Forge 安装器（--installClient）…");
        var run = await InstallerRunner.RunJavaAsync(java.Path, dest, new[] { "--installClient" }, ct, s => log?.Report(s));
        result.Success = run.Success;
        result.Message = run.Success
            ? $"Forge {mcVersion}-{forgeVersion} 已安装到启动器目录"
            : $"Forge 安装失败（退出码 {run.ExitCode}），安装器保留在 {dest}，可手动运行安装";
        return result;
    }
}
