using System.Text.Json.Serialization;

namespace GameDock.Core.Models;

/// <summary>单个版本（version.json）的结构模型，覆盖 Mojang 版本 JSON 的下载所需字段。</summary>
public sealed class VersionMeta
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; set; }

    /// <summary>旧版本继承（1.12 及更早的某些版本），导入时原样保留。</summary>
    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("jar")]
    public string? Jar { get; set; }

    [JsonPropertyName("assetIndex")]
    public AssetIndexInfo? AssetIndex { get; set; }

    [JsonPropertyName("downloads")]
    public VersionDownloads? Downloads { get; set; }

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = new();

    [JsonPropertyName("arguments")]
    public VersionArguments? Arguments { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionInfo? JavaVersion { get; set; }

    [JsonPropertyName("logging")]
    public LoggingInfo? Logging { get; set; }
}

public sealed class AssetIndexInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class VersionDownloads
{
    [JsonPropertyName("client")]
    public DownloadArtifact? Client { get; set; }

    [JsonPropertyName("client_mappings")]
    public DownloadArtifact? ClientMappings { get; set; }

    [JsonPropertyName("server")]
    public DownloadArtifact? Server { get; set; }

    [JsonPropertyName("server_mappings")]
    public DownloadArtifact? ServerMappings { get; set; }
}

public sealed class DownloadArtifact
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>库条目：可能带 os 规则、artifact 与 natives 分类器。</summary>
public sealed class Library
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("rules")]
    public List<LibraryRule>? Rules { get; set; }

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("extract")]
    public ExtractInfo? Extract { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public DownloadArtifact? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, DownloadArtifact>? Classifiers { get; set; }
}

public sealed class LibraryRule
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("os")]
    public OsMatcher? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public sealed class OsMatcher
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class ExtractInfo
{
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }
}

public sealed class VersionArguments
{
    [JsonPropertyName("game")]
    public List<object>? Game { get; set; }

    [JsonPropertyName("jvm")]
    public List<object>? Jvm { get; set; }
}

public sealed class JavaVersionInfo
{
    [JsonPropertyName("component")]
    public string? Component { get; set; }

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

public sealed class LoggingInfo
{
    [JsonPropertyName("client")]
    public LoggingClientInfo? Client { get; set; }
}

public sealed class LoggingClientInfo
{
    [JsonPropertyName("argument")]
    public string? Argument { get; set; }

    [JsonPropertyName("file")]
    public DownloadArtifact? File { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
