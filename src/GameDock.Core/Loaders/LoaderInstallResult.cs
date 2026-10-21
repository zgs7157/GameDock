namespace GameDock.Core.Loaders;

/// <summary>加载器安装结果。</summary>
public sealed class LoaderInstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ArtifactPath { get; set; }
    public bool ArtifactOnly { get; set; }
}
