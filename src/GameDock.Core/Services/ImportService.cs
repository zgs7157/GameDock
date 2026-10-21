using GameDock.Core.Models;

namespace GameDock.Core.Services;

/// <summary>下载参数。</summary>
public sealed class DownloadOptions
{
    public string McDir { get; set; } = string.Empty;
    public bool DownloadClient { get; set; } = true;
    public bool DownloadServer { get; set; }
    public bool DownloadAssets { get; set; } = true;
    public bool ExtractNatives { get; set; } = true;
    public bool WriteVersionJson { get; set; } = true;
    public string? ServerDestDir { get; set; }
}

/// <summary>一次版本导入的报告。</summary>
public sealed class ImportReport
{
    public string VersionId { get; set; } = string.Empty;
    public string McDir { get; set; } = string.Empty;
    public int LibrariesWritten { get; set; }
    public int AssetsWritten { get; set; }
    public int NativesExtracted { get; set; }
    public bool ClientWritten { get; set; }
    public bool ServerWritten { get; set; }
    public bool VersionJsonWritten { get; set; }
    public bool AssetIndexWritten { get; set; }
}

/// <summary>
/// 导入服务：把下载好的文件按启动器（PCL2 / HMCL / FCL）通用的
/// .minecraft 目录布局写入，启动器启动后即可识别该版本。
/// </summary>
public sealed class ImportService
{
    public ImportReport Report { get; } = new();

    public string VersionDir(string mcDir, string id) => Path.Combine(mcDir, "versions", id);

    public string LibraryPath(string mcDir, string libPath) =>
        Path.Combine(mcDir, "libraries", NormalizeLibPath(libPath));

    /// <summary>Maven 风格的库路径（/ 分隔）统一转成平台分隔符。</summary>
    public static string NormalizeLibPath(string libPath)
        => libPath.Replace('/', Path.DirectorySeparatorChar);

    public string AssetObjectPath(string mcDir, string hash)
    {
        if (hash.Length < 2) throw new ArgumentException("hash 过短");
        return Path.Combine(mcDir, "assets", "objects", hash[..2], hash);
    }

    public string AssetIndexPath(string mcDir, string indexId) => Path.Combine(mcDir, "assets", "indexes", indexId + ".json");

    public string NativesDir(string mcDir, string id) =>
        Path.Combine(mcDir, "versions", id, $"natives-{RuleEvaluator.CurrentOsName}-{ArchSuffix()}");

    private static string ArchSuffix() =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => "unknown",
        };

    /// <summary>写入版本 JSON 与客户端 jar（导入的核心步骤）。</summary>
    public async Task ImportVersionAsync(string mcDir, string id, string versionJson, string? clientJarPath, CancellationToken ct)
    {
        var vdir = VersionDir(mcDir, id);
        Directory.CreateDirectory(vdir);
        var jsonDest = Path.Combine(vdir, id + ".json");
        var jarDest = Path.Combine(vdir, id + ".jar");

        await File.WriteAllTextAsync(jsonDest, versionJson, ct);
        Report.VersionJsonWritten = true;

        if (clientJarPath is { Length: > 0 } && File.Exists(clientJarPath)
            && !Path.GetFullPath(clientJarPath).Equals(Path.GetFullPath(jarDest), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(clientJarPath, jarDest, overwrite: true);
            Report.ClientWritten = true;
        }

        Report.VersionId = id;
        Report.McDir = mcDir;
    }

    /// <summary>把服务端 jar 放到版本目录或指定目录。</summary>
    public void ImportServer(string mcDir, string id, string serverJarPath, string? serverDestDir = null)
    {
        if (!File.Exists(serverJarPath)) return;
        var dest = serverDestDir is { Length: > 0 }
            ? Path.Combine(serverDestDir, id + "-server.jar")
            : Path.Combine(VersionDir(mcDir, id), id + "-server.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(serverJarPath, dest, overwrite: true);
        Report.ServerWritten = true;
    }

    /// <summary>写入资源索引文件。</summary>
    public async Task WriteAssetIndexAsync(string mcDir, string indexId, string content, CancellationToken ct)
    {
        var dest = AssetIndexPath(mcDir, indexId);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllTextAsync(dest, content, ct);
        Report.AssetIndexWritten = true;
    }

    /// <summary>登记一个库文件（用于报告计数）。</summary>
    public void RegisterLibrary(string mcDir, string libPath, string sourcePath)
    {
        if (!File.Exists(sourcePath)) return;
        var dest = LibraryPath(mcDir, libPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, dest, overwrite: true);
        Report.LibrariesWritten++;
    }

    /// <summary>登记一个资源对象（用于报告计数）。</summary>
    public void RegisterAsset(string mcDir, string hash, string sourcePath)
    {
        if (!File.Exists(sourcePath)) return;
        var dest = AssetObjectPath(mcDir, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, dest, overwrite: true);
        Report.AssetsWritten++;
    }
}
