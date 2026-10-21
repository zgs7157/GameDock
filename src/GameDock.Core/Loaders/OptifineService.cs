using GameDock.Core.Mirrors;
using GameDock.Core.Models;
using GameDock.Core.Services;
using GameDock.Core.Util;

namespace GameDock.Core.Loaders;

/// <summary>
/// OptiFine：通过 BMCLAPI 的 /optifine/&lt;mc&gt; 列表下载安装器。
/// OptiFine 官方未提供 JSON API，因此默认走 BMCLAPI 镜像。
/// </summary>
public sealed class OptifineService
{
    private readonly McJsonClient _json;
    private readonly DownloadManager _dm;
    private readonly IReadOnlyList<IMcMirror> _mirrors;

    public OptifineService(HttpClient http, IReadOnlyList<IMcMirror> mirrors, int threads, int retryCount)
    {
        _mirrors = mirrors;
        _json = new McJsonClient(http, retryCount);
        _dm = new DownloadManager(http, threads, retryCount);
    }

    /// <summary>获取某 MC 版本的 OptiFine 发布列表（BMCLAPI）。</summary>
    public async Task<List<OptifineInfo>> GetReleasesAsync(string mcVersion, CancellationToken ct)
    {
        var bmclapi = _mirrors.FirstOrDefault(m => m.Key.Equals("bmclapi", StringComparison.OrdinalIgnoreCase))
                      ?? new BmclapiMirror();
        var url = $"{bmclapi.MetaBase}/optifine/{mcVersion}";
        var list = await _json.GetJsonAsync<List<OptifineInfo>>(url, _mirrors, ct)
            ?? throw new IOException($"BMCLAPI 未返回 {mcVersion} 的 OptiFine 列表");
        return list;
    }

    /// <summary>下载（可选自动运行）OptiFine 安装器。</summary>
    public async Task<LoaderInstallResult> DownloadInstallerAsync(
        OptifineInfo item,
        string destDir,
        bool autoRun,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, item.Filename);

        log?.Report($"下载 OptiFine 安装器：{item.Filename}…");
        await _dm.DownloadAsync(new[]
        {
            new DownloadEntry
            {
                Kind = "installer",
                DisplayName = item.Filename,
                DestPath = dest,
                UrlCandidates = UrlCandidates.Build(item.Url, _mirrors),
            },
        }, progress, ct);

        var result = new LoaderInstallResult { ArtifactPath = dest };

        if (!autoRun)
        {
            result.Success = true;
            result.ArtifactOnly = true;
            result.Message = $"OptiFine 安装器已下载：{dest}（未自动运行，安装器为图形界面，请手动双击运行）";
            return result;
        }

        var java = JavaLocator.FindCached();
        if (java is null)
        {
            result.Success = false;
            result.Message = $"安装器已下载：{dest}，但未找到 Java，请双击运行安装器";
            return result;
        }

        log?.Report($"用 Java {java.Major} 运行 OptiFine 安装器…");
        var run = await InstallerRunner.RunJavaAsync(java.Path, dest, Array.Empty<string>(), ct, s => log?.Report(s));
        result.Success = run.Success;
        result.Message = run.Success
            ? "OptiFine 安装器已运行（请在弹窗中选择安装目录并点击 Install）"
            : $"OptiFine 安装器运行失败（退出码 {run.ExitCode}），文件保留在 {dest}";
        return result;
    }
}
