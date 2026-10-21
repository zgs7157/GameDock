using GameDock.Core.Mirrors;
using GameDock.Core.Models;
using GameDock.Core.Services;
using GameDock.Core.Util;

namespace GameDock.Core.Loaders;

/// <summary>
/// Fabric：通过 meta.fabricmc.net 的 launcherMeta 生成带 inheritsFrom 的
/// 版本 JSON，先导入原版基础版本（客户端/库），再下载 Fabric 库并写入版本，
/// 全程无需本机 Java。
/// </summary>
public sealed class FabricService
{
    private const string MetaBase = "https://meta.fabricmc.net";

    private readonly McJsonClient _json;
    private readonly VersionDownloader _vdl;
    private readonly IReadOnlyList<IMcMirror> _mirrors;

    public FabricService(HttpClient http, IReadOnlyList<IMcMirror> mirrors, int threads, int retryCount)
    {
        _mirrors = mirrors;
        _json = new McJsonClient(http, retryCount);
        _vdl = new VersionDownloader(http, mirrors, threads, retryCount);
    }

    /// <summary>获取某 MC 版本可用的 Fabric loader 版本列表。</summary>
    public async Task<List<string>> GetLoaderVersionsAsync(string mcVersion, CancellationToken ct)
    {
        var list = await _json.GetJsonAsync<List<FabricMetaVersion>>($"{MetaBase}/v2/versions/loader/{mcVersion}", _mirrors, ct)
            ?? throw new IOException($"Fabric meta 未返回 {mcVersion} 的加载器列表");
        return list.Where(x => x.Loader is not null).Select(x => x.Loader!.Version).ToList();
    }

    /// <summary>获取 launcherMeta 包装结构。</summary>
    public Task<FabricLauncherMetaWrapper> GetWrapperAsync(string mcVersion, string loaderVersion, CancellationToken ct) =>
        _json.GetJsonAsync<FabricLauncherMetaWrapper>($"{MetaBase}/v2/versions/loader/{mcVersion}/{loaderVersion}", _mirrors, ct)
            ?? throw new IOException($"Fabric meta 未返回 {mcVersion}/{loaderVersion} 的 launcherMeta");

    /// <summary>由 launcherMeta 构造带 inheritsFrom 的启动版本 JSON。</summary>
    public string BuildLauncherMetaJson(FabricLauncherMetaWrapper wrapper, string mcVersion, string loaderVersion)
    {
        var lm = wrapper.LauncherMeta
            ?? throw new IOException("Fabric launcherMeta 缺失");
        var id = $"{mcVersion}-fabric-{loaderVersion}";

        var meta = new VersionMeta
        {
            Id = id,
            Type = "release",
            InheritsFrom = mcVersion,
            MainClass = lm.MainClass?.Client ?? "net.fabricmc.loader.impl.launch.knot.KnotClient",
            Libraries = new List<Library>(),
            JavaVersion = lm.MinJavaVersion > 0
                ? new JavaVersionInfo { Component = "fabric-launcher-meta", MajorVersion = lm.MinJavaVersion }
                : null,
        };

        // common + client 库（Maven 坐标 → 版本 JSON 的 downloads.artifact）。
        var libs = new List<FabricMavenLib>();
        if (lm.Libraries?.Common is not null) libs.AddRange(lm.Libraries.Common);
        if (lm.Libraries?.Client is not null) libs.AddRange(lm.Libraries.Client);

        foreach (var l in libs)
        {
            var (path, artifactUrl) = MavenPath(l);
            meta.Libraries.Add(new Library
            {
                Name = l.Name,
                Downloads = new LibraryDownloads
                {
                    Artifact = new DownloadArtifact
                    {
                        Path = path,
                        Url = artifactUrl,
                        Sha1 = l.Sha1,
                        Size = l.Size,
                    },
                },
            });
        }

        return System.Text.Json.JsonSerializer.Serialize(meta, JsonOpts);
    }

    private static (string Path, string Url) MavenPath(FabricMavenLib lib)
    {
        var parts = lib.Name.Split(':');
        if (parts.Length < 3) return (lib.Name.Replace(':', '-') + ".jar", lib.Url.TrimEnd('/') + "/" + lib.Name.Replace(':', '-') + ".jar");
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var path = $"{group}/{artifact}/{version}/{artifact}-{version}.jar";
        return (path, lib.Url.TrimEnd('/') + "/" + path);
    }

    /// <summary>下载并导入 Fabric 版本（含原版基础版本）。版本 id 形如 1.21.4-fabric-0.20.1。</summary>
    public async Task<LoaderInstallResult> InstallAsync(
        string mcVersion,
        string loaderVersion,
        DownloadOptions opts,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        log?.Report($"生成 Fabric {mcVersion} + loader {loaderVersion} 的启动元数据…");
        var wrapper = await GetWrapperAsync(mcVersion, loaderVersion, ct);
        var rawFabricJson = BuildLauncherMetaJson(wrapper, mcVersion, loaderVersion);
        var fabricMeta = ModelsJson.Deserialize<VersionMeta>(rawFabricJson)
            ?? throw new IOException("Fabric 启动元数据解析失败");
        var fabricId = $"{mcVersion}-fabric-{loaderVersion}";

        // 1) 先导入原版基础版本（客户端 jar / 库 / 资源），Fabric 版本依赖它。
        log?.Report($"先导入原版基础版本 {mcVersion}…");
        var manifest = await _vdl.GetManifestAsync(ct);
        var baseEntry = _vdl.ResolveVersion(manifest, mcVersion);
        await _vdl.DownloadVersionAsync(baseEntry, opts, progress, log, ct);

        // 2) 写入 Fabric 版本 JSON 并下载其专属库（客户端/资源已由基础版本覆盖）。
        var fabOpts = new DownloadOptions
        {
            McDir = opts.McDir,
            DownloadClient = false,
            DownloadServer = false,
            DownloadAssets = false,
            ExtractNatives = opts.ExtractNatives,
        };
        var report = await _vdl.DownloadMetaAsync(fabricId, rawFabricJson, fabricMeta, fabOpts, progress, log, ct);

        return new LoaderInstallResult
        {
            Success = true,
            Message = $"Fabric 版本 {fabricId} 已导入 {report.McDir}（含原版基础版本 {mcVersion}）",
        };
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
