namespace GameDock.Core.Mirrors;

/// <summary>
/// 镜像源抽象。核心思路：一个镜像 = 一组端点；
/// 下载时若主端点被阻断/失效，可自动回退到其他镜像端点（“封杀”抗性）。
/// </summary>
public interface IMcMirror
{
    /// <summary>镜像唯一标识（配置文件中的 key）。</summary>
    string Key { get; }

    /// <summary>显示名称。</summary>
    string DisplayName { get; }

    /// <summary>版本清单与版本 JSON 的根地址，如 https://piston-meta.mojang.com。</summary>
    string MetaBase { get; }

    /// <summary>资源（assets）根地址，如 https://resources.download.minecraft.net。</summary>
    string AssetsBase { get; }

    /// <summary>库（libraries）根地址，如 https://libraries.minecraft.net。</summary>
    string LibrariesBase { get; }

    /// <summary>把任一已知地址重写到本镜像（用于版本 JSON 内嵌 URL 被封时回退）。</summary>
    string RewriteUrl(string originalUrl);
}

/// <summary>Mojang 官方源。</summary>
public sealed class OfficialMirror : IMcMirror
{
    public string Key => "official";
    public string DisplayName => "Mojang 官方源";
    public string MetaBase => "https://piston-meta.mojang.com";
    public string AssetsBase => "https://resources.download.minecraft.net";
    public string LibrariesBase => "https://libraries.minecraft.net";

    public string RewriteUrl(string originalUrl) => originalUrl;
}

/// <summary>BMCLAPI 镜像（国内常用、兼容官方路径）。</summary>
public sealed class BmclapiMirror : IMcMirror
{
    public string Key => "bmclapi";
    public string DisplayName => "BMCLAPI 镜像";
    public string MetaBase => "https://bmclapi2.bangbang93.com";
    public string AssetsBase => "https://bmclapi2.bangbang93.com/assets";
    public string LibrariesBase => "https://bmclapi2.bangbang93.com/libraries";

    public string RewriteUrl(string originalUrl)
    {
        // 把官方域名的路径整体替换到 BMCLAPI 对应端点。
        if (originalUrl.Contains("piston-meta.mojang.com") || originalUrl.Contains("launchermeta.mojang.com"))
            return MetaBase + UriPath(originalUrl);
        if (originalUrl.Contains("resources.download.minecraft.net"))
            return AssetsBase + UriPath(originalUrl, stripPrefix: true);
        if (originalUrl.Contains("libraries.minecraft.net") || originalUrl.Contains("piston-data.mojang.com"))
            return LibrariesBase + UriPath(originalUrl);
        if (originalUrl.Contains("maven.minecraftforge.net"))
            return MetaBase + "/maven" + UriPath(originalUrl);
        if (originalUrl.Contains("files.minecraftforge.net"))
            return MetaBase + "/forge" + UriPath(originalUrl);
        if (originalUrl.Contains("meta.fabricmc.net"))
            return MetaBase + "/fabric-meta" + UriPath(originalUrl);
        return originalUrl;
    }

    private static string UriPath(string url, bool stripPrefix = false)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath;
        if (stripPrefix)
        {
            var idx = path.IndexOf("/objects", StringComparison.Ordinal);
            if (idx >= 0) path = path[(idx + "/objects".Length)..];
            // objects 端点路径为 /objects/xx/yyyy，直接拼在 assets 根后。
            if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path;
        }
        return path;
    }
}

/// <summary>用户自定义镜像：给一个根地址，按官方路径结构代理。</summary>
public sealed class CustomMirror : IMcMirror
{
    public CustomMirror(string key, string displayName, string baseUrl)
    {
        Key = key;
        DisplayName = displayName;
        var root = baseUrl.TrimEnd('/');
        MetaBase = root;
        AssetsBase = root + "/assets";
        LibrariesBase = root + "/libraries";
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string MetaBase { get; }
    public string AssetsBase { get; }
    public string LibrariesBase { get; }

    public string RewriteUrl(string originalUrl)
    {
        var uri = new Uri(originalUrl);
        var path = uri.AbsolutePath;
        if (originalUrl.Contains("resources.download.minecraft.net"))
        {
            var idx = path.IndexOf("/objects", StringComparison.Ordinal);
            path = idx >= 0 ? path[idx..] : path;
        }
        if (originalUrl.Contains("libraries.minecraft.net") || originalUrl.Contains("piston-data.mojang.com"))
            path = "/libraries" + path;
        return MetaBase + path;
    }
}
