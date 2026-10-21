using System.Text.Json.Serialization;

namespace GameDock.Core.Models;

/// <summary>assets 索引文件（{assetIndex}.json）。</summary>
public sealed class AssetIndex
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject> Objects { get; set; } = new();

    /// <summary>是否为 map_to_resources 的旧版资源（1.7 之前）。</summary>
    [JsonPropertyName("map_to_resources")]
    public bool MapToResources { get; set; }
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
