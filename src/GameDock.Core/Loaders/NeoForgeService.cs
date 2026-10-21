using System.Xml.Linq;
using GameDock.Core.Mirrors;
using GameDock.Core.Services;
using GameDock.Core.Util;

namespace GameDock.Core.Loaders;

/// <summary>
/// NeoForge：从 maven-metadata.xml 列出版本，下载安装器，
/// 有 Java 时自动执行 --installClient，否则仅下载。
/// </summary>
public sealed class NeoForgeService
{
    private const string MavenMetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const string MavenBase = "https://maven.neoforged.net/releases";

    private readonly McJsonClient _json;
    private readonly DownloadManager _dm;
    private readonly IReadOnlyList<IMcMirror> _mirrors;

    public NeoForgeService(HttpClient http, IReadOnlyList<IMcMirror> mirrors, int threads, int retryCount)
    {
        _mirrors = mirrors;
        _json = new McJsonClient(http, retryCount);
        _dm = new DownloadManager(http, threads, retryCount);
    }

    /// <summary>获取 NeoForge 全部可用版本（按 maven-metadata.xml）。</summary>
    public async Task<List<string>> GetVersionsAsync(CancellationToken ct)
    {
        var xml = await _json.GetStringAsync(MavenMetadataUrl, _mirrors, ct);
        var doc = XDocument.Parse(xml);
        var versions = doc.Descendants("version").Select(v => v.Value.Trim()).ToList();
        return versions;
    }

    /// <summary>按 MC 版本前缀过滤（如 1.20.4 匹配 20.4.x 与 1.20.4-xx 形态）。</summary>
    public List<string> FilterByMc(string mcVersion, IEnumerable<string> versions)
    {
        var core = mcVersion; // 如 1.20.4
        var compact = mcVersion.Replace(".", ""); // 如 1204（neoforge 的 20.4.x 形态）
        return versions
            .Where(v => v.StartsWith(core + "-", StringComparison.OrdinalIgnoreCase)
                        || v.StartsWith(compact, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public string InstallerUrl(string neoforgeVersion)
        => $"{MavenBase}/net/neoforged/neoforge/{neoforgeVersion}/neoforge-{neoforgeVersion}-installer.jar";

    /// <summary>下载（可选自动安装）NeoForge 安装器。</summary>
    public async Task<LoaderInstallResult> DownloadInstallerAsync(
        string neoforgeVersion,
        string destDir,
        bool autoRun,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var fileName = $"neoforge-{neoforgeVersion}-installer.jar";
        var dest = Path.Combine(destDir, fileName);
        var url = InstallerUrl(neoforgeVersion);

        log?.Report($"下载 NeoForge 安装器：{fileName}…");
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
            result.Message = $"NeoForge 安装器已下载：{dest}（未自动安装）";
            return result;
        }

        var java = JavaLocator.FindCached();
        if (java is null)
        {
            result.Success = false;
            result.Message = $"安装器已下载：{dest}，但未找到 Java，请手动运行：java -jar \"{dest}\" --installClient <启动器目录>";
            return result;
        }

        log?.Report($"用 Java {java.Major} 运行 NeoForge 安装器（--installClient）…");
        var run = await InstallerRunner.RunJavaAsync(java.Path, dest, new[] { "--installClient" }, ct, s => log?.Report(s));
        result.Success = run.Success;
        result.Message = run.Success
            ? $"NeoForge {neoforgeVersion} 已安装到启动器目录"
            : $"NeoForge 安装失败（退出码 {run.ExitCode}），安装器保留在 {dest}，可手动运行安装";
        return result;
    }
}
