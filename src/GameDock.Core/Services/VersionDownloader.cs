using GameDock.Core.Mirrors;
using GameDock.Core.Models;
using GameDock.Core.Util;

namespace GameDock.Core.Services;

/// <summary>
/// 版本下载编排：清单 → 版本 JSON → 客户端/服务端/库/资源 → 导入启动器目录。
/// 所有下载都带多镜像候选与 sha1 校验。
/// </summary>
public sealed class VersionDownloader
{
    private const string OfficialManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string OfficialResourcesUrl = "https://resources.download.minecraft.net";

    private readonly McJsonClient _json;
    private readonly DownloadManager _dm;
    private readonly ImportService _import = new();
    private readonly IReadOnlyList<IMcMirror> _mirrors;

    public VersionDownloader(HttpClient http, IReadOnlyList<IMcMirror> mirrors, int threads, int retryCount)
    {
        _mirrors = mirrors;
        _json = new McJsonClient(http, retryCount);
        _dm = new DownloadManager(http, threads, retryCount);
    }

    /// <summary>获取版本清单（自动镜像回退）。</summary>
    public async Task<VersionManifest> GetManifestAsync(CancellationToken ct)
    {
        var manifest = await _json.GetJsonAsync<VersionManifest>(OfficialManifestUrl, _mirrors, ct);
        return manifest ?? throw new IOException("版本清单为空");
    }

    /// <summary>解析目标版本：支持精确 id 或 latest:release / latest:snapshot。</summary>
    public ManifestVersion ResolveVersion(VersionManifest manifest, string idOrLatest)
    {
        var key = idOrLatest.ToLowerInvariant();
        if (key == "latest:release")
        {
            var v = manifest.Latest?.Release;
            return manifest.Versions.FirstOrDefault(x => x.Id == v)
                ?? throw new IOException($"清单中没有最新正式版 {v}");
        }
        if (key == "latest:snapshot")
        {
            var v = manifest.Latest?.Snapshot;
            return manifest.Versions.FirstOrDefault(x => x.Id == v)
                ?? throw new IOException($"清单中没有最新快照版 {v}");
        }
        return manifest.Versions.FirstOrDefault(x => x.Id.Equals(idOrLatest, StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException($"清单中找不到版本 {idOrLatest}（可用 gamedock list 查看）");
    }

    /// <summary>下载并导入一个版本（按清单条目）。</summary>
    public async Task<ImportReport> DownloadVersionAsync(
        ManifestVersion entry,
        DownloadOptions opts,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        // 1) 拉取版本 JSON（原始文本，用于写入启动器目录）。
        log?.Report($"拉取 {entry.Id} 版本元数据…");
        var rawJson = await _json.GetStringAsync(entry.Url, _mirrors, ct);
        var meta = Json.Deserialize<VersionMeta>(rawJson) ?? throw new IOException($"{entry.Id} 版本 JSON 解析失败");
        return await DownloadMetaAsync(entry.Id, rawJson, meta, opts, progress, log, ct);
    }

    /// <summary>
    /// 下载并导入一个版本（直接给定版本 JSON，Fabric/Quilt 等 meta 生成的
    /// 版本 JSON 也走此流程，保证库/资源/导入行为一致）。
    /// </summary>
    public async Task<ImportReport> DownloadMetaAsync(
        string id,
        string rawJson,
        VersionMeta meta,
        DownloadOptions opts,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var mcDir = opts.McDir;

        // 2) 组装下载条目。
        var entries = new List<DownloadEntry>();
        var vdir = Path.Combine(mcDir, "versions", id);
        Directory.CreateDirectory(vdir);

        var clientJarPath = Path.Combine(vdir, id + ".jar");
        if (opts.DownloadClient && meta.Downloads?.Client is { } client)
        {
            entries.Add(new DownloadEntry
            {
                Kind = "client",
                DisplayName = $"{id} 客户端",
                DestPath = clientJarPath,
                Sha1 = client.Sha1,
                Size = client.Size,
                UrlCandidates = BuildCandidates(client.Url),
            });
        }

        if (opts.DownloadServer && meta.Downloads?.Server is { } server)
        {
            var serverDest = Path.Combine(vdir, id + "-server.jar");
            entries.Add(new DownloadEntry
            {
                Kind = "server",
                DisplayName = $"{id} 服务端",
                DestPath = serverDest,
                Sha1 = server.Sha1,
                Size = server.Size,
                UrlCandidates = BuildCandidates(server.Url),
            });
        }

        // 库与 natives。
        var nativesJars = new List<(string JarPath, IReadOnlyList<string>? Exclude)>();
        foreach (var lib in meta.Libraries)
        {
            if (!RuleEvaluator.Allowed(lib.Rules)) continue;
            if (lib.Downloads?.Artifact is { } artifact && !string.IsNullOrEmpty(artifact.Path))
            {
                var dest = _import.LibraryPath(mcDir, artifact.Path);
                entries.Add(new DownloadEntry
                {
                    Kind = "library",
                    DisplayName = $"库 {Path.GetFileName(artifact.Path)}",
                    DestPath = dest,
                    Sha1 = artifact.Sha1,
                    Size = artifact.Size,
                    UrlCandidates = BuildCandidates(artifact.Url),
                });
            }

            var nativesKey = FindNativeClassifier(lib.Downloads?.Classifiers);
            if (nativesKey is not null
                && lib.Downloads!.Classifiers!.TryGetValue(nativesKey, out var nativeArtifact))
            {
                var dest = _import.LibraryPath(mcDir, nativeArtifact.Path ?? ClassifierPathFromName(lib.Name, nativesKey));
                entries.Add(new DownloadEntry
                {
                    Kind = "native",
                    DisplayName = $"natives {Path.GetFileName(nativeArtifact.Url)}",
                    DestPath = dest,
                    Sha1 = nativeArtifact.Sha1,
                    Size = nativeArtifact.Size,
                    UrlCandidates = BuildCandidates(nativeArtifact.Url),
                });
                nativesJars.Add((dest, lib.Extract?.Exclude));
            }
        }

        // 资源索引 + 资源对象。
        string? assetIndexId = null;
        if (opts.DownloadAssets && meta.AssetIndex is { } ai)
        {
            assetIndexId = ai.Id;
            var indexDest = Path.Combine(mcDir, "assets", "indexes", ai.Id + ".json");
            entries.Add(new DownloadEntry
            {
                Kind = "asset-index",
                DisplayName = $"资源索引 {ai.Id}",
                DestPath = indexDest,
                Sha1 = ai.Sha1,
                Size = ai.Size,
                UrlCandidates = BuildCandidates(ai.Url),
            });
        }

        // 日志配置（可选，小文件）。
        if (meta.Logging?.Client?.File is { } logFile && !string.IsNullOrEmpty(logFile.Path))
        {
            var dest = Path.Combine(mcDir, "assets", logFile.Path);
            entries.Add(new DownloadEntry
            {
                Kind = "logconfig",
                DisplayName = $"日志配置 {Path.GetFileName(logFile.Path)}",
                DestPath = dest,
                Sha1 = logFile.Sha1,
                Size = logFile.Size,
                UrlCandidates = BuildCandidates(logFile.Url),
            });
        }

        // 3) 下载（含资源对象，若索引已存在）。
        log?.Report($"开始下载 {id}（共 {entries.Count} 个文件）…");
        await _dm.DownloadAsync(entries, progress, ct);

        // 4) 资源对象：读取索引后批量下载。
        var assetCount = 0;
        if (assetIndexId is not null)
        {
            var indexFile = Path.Combine(mcDir, "assets", "indexes", assetIndexId + ".json");
            var index = Json.Deserialize<AssetIndex>(await File.ReadAllTextAsync(indexFile, ct)) ?? new AssetIndex();
            log?.Report($"资源对象共 {index.Objects.Count} 个，开始下载…");
            var assetEntries = index.Objects.Select(kv =>
            {
                var h = kv.Value.Hash;
                var url = $"{OfficialResourcesUrl}/objects/{h[..2]}/{h}";
                return new DownloadEntry
                {
                    Kind = "asset",
                    DisplayName = $"资源 {h[..8]}",
                    DestPath = ImportServicePath(mcDir, h),
                    Sha1 = h,
                    Size = kv.Value.Size,
                    UrlCandidates = BuildCandidates(url),
                };
            }).ToList();

            await _dm.DownloadAsync(assetEntries, progress, ct);
            assetCount = assetEntries.Count;
        }

        // 5) natives 解压。
        var nativesDir = _import.NativesDir(mcDir, id);
        if (opts.ExtractNatives && nativesJars.Count > 0)
        {
            log?.Report("解压 natives…");
            foreach (var (jar, exclude) in nativesJars)
            {
                _import.Report.NativesExtracted += NativesExtractor.Extract(jar, nativesDir, exclude, log);
            }
        }

        // 6) 写入版本 JSON（导入的核心）。
        await _import.ImportVersionAsync(mcDir, id, rawJson, opts.DownloadClient ? clientJarPath : null, ct);

        // 计数。
        _import.Report.LibrariesWritten = entries.Count(e => e.Kind is "library" or "native");
        _import.Report.AssetsWritten = assetCount;

        return _import.Report;
    }

    /// <summary>镜像候选：原始 URL + 各镜像重写，去重。</summary>
    public IReadOnlyList<string> BuildCandidates(string url)
    {
        var list = new List<string> { url };
        foreach (var mirror in _mirrors)
        {
            var r = mirror.RewriteUrl(url);
            if (!list.Contains(r)) list.Add(r);
        }
        return list;
    }

    private static string? FindNativeClassifier(Dictionary<string, DownloadArtifact>? classifiers)
    {
        if (classifiers is null || classifiers.Count == 0) return null;
        var os = RuleEvaluator.CurrentOsName;
        var arch = RuleEvaluator.CurrentArchAliases;

        // 精确带架构的 key 优先（如 natives-windows-arm64）。
        foreach (var alias in arch)
        {
            var key = $"natives-{os}-{alias}";
            if (classifiers.ContainsKey(key)) return key;
        }
        // 退而求其次：仅 OS（natives-windows）。
        var osKey = $"natives-{os}";
        return classifiers.ContainsKey(osKey) ? osKey : null;
    }

    private static string ClassifierPathFromName(string libName, string classifierKey)
    {
        // 把 com.example:lib:1.0 转成 com/example/lib/1.0/lib-1.0-{classifier}.jar 形状。
        var parts = libName.Split(':');
        if (parts.Length < 3) return $"{libName}-{classifierKey}.jar";
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        return $"{group}/{artifact}/{version}/{artifact}-{version}-{classifierKey}.jar";
    }

    private static string ImportServicePath(string mcDir, string hash)
    {
        if (hash.Length < 2) throw new ArgumentException("hash 过短");
        return Path.Combine(mcDir, "assets", "objects", hash[..2], hash);
    }
}

/// <summary>System.Text.Json 便捷封装。</summary>
public static class Json
{
    private static readonly System.Text.Json.JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static T? Deserialize<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, Opts);
}
