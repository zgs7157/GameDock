using System.Text.Json.Serialization;

namespace GameDock.Core.Models;

/// <summary>Forge promotions_slim.json 结构。</summary>
public sealed class ForgePromotionMap
{
    [JsonPropertyName("promos")]
    public Dictionary<string, string> Promos { get; set; } = new();

    /// <summary>获取某 MC 版本推荐的 Forge 版本号。</summary>
    public string? GetRecommended(string mcVersion)
        => Promos.TryGetValue($"{mcVersion}-recommended", out var v) ? v : null;

    /// <summary>获取某 MC 版本最新的 Forge 版本号。</summary>
    public string? GetLatest(string mcVersion)
        => Promos.TryGetValue($"{mcVersion}-latest", out var v) ? v : null;
}

/// <summary>Fabric meta API 的 loader / game / installer 版本条目。</summary>
public sealed class FabricMetaVersion
{
    [JsonPropertyName("loader")]
    public FabricVersionInfo? Loader { get; set; }

    [JsonPropertyName("game")]
    public FabricVersionInfo? Game { get; set; }

    [JsonPropertyName("intermediary")]
    public FabricVersionInfo? Intermediary { get; set; }

    [JsonPropertyName("installer")]
    public FabricVersionInfo? Installer { get; set; }
}

public sealed class FabricVersionInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

/// <summary>GET /v2/versions/loader/{game}/{loader} 的包装结构（launcherMeta 为其核心）。</summary>
public sealed class FabricLauncherMetaWrapper
{
    [JsonPropertyName("loader")]
    public FabricVersionInfo? Loader { get; set; }

    [JsonPropertyName("intermediary")]
    public FabricVersionInfo? Intermediary { get; set; }

    [JsonPropertyName("launcherMeta")]
    public FabricLauncherMeta? LauncherMeta { get; set; }
}

public sealed class FabricLauncherMeta
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("min_java_version")]
    public int MinJavaVersion { get; set; }

    [JsonPropertyName("libraries")]
    public FabricLibrarySets? Libraries { get; set; }

    [JsonPropertyName("mainClass")]
    public FabricMainClass? MainClass { get; set; }
}

public sealed class FabricLibrarySets
{
    [JsonPropertyName("common")]
    public List<FabricMavenLib>? Common { get; set; }

    [JsonPropertyName("client")]
    public List<FabricMavenLib>? Client { get; set; }

    [JsonPropertyName("server")]
    public List<FabricMavenLib>? Server { get; set; }

    [JsonPropertyName("development")]
    public List<FabricMavenLib>? Development { get; set; }
}

/// <summary>Maven 坐标风格的库条目：{ name: "g:a:v", url: "https://maven..." }。</summary>
public sealed class FabricMavenLib
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public sealed class FabricMainClass
{
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}

/// <summary>OptiFine（BMCLAPI /optifine/&lt;mc&gt; 端点）条目。</summary>
public sealed class OptifineInfo
{
    [JsonPropertyName("mcversion")]
    public string? McVersion { get; set; }

    [JsonPropertyName("patch")]
    public string? Patch { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }
}
