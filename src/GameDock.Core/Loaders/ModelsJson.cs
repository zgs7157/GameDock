using GameDock.Core.Models;

namespace GameDock.Core.Loaders;

/// <summary>ModelsJson 别名，避免与 LoaderInstallResult 同命名空间冲突。</summary>
internal static class ModelsJson
{
    public static T? Deserialize<T>(string json)
        => System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
}
