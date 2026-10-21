using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDock.Core.Config;

/// <summary>应用配置（持久化到 %APPDATA%\GameDock\config.json）。</summary>
public sealed class AppConfig
{
    /// <summary>MC 根目录（.minecraft），默认 %APPDATA%\.minecraft。</summary>
    public string McDir { get; set; } = string.Empty;

    /// <summary>首选镜像 key。</summary>
    public string Mirror { get; set; } = "bmclapi";

    /// <summary>并发下载线程数。</summary>
    public int Threads { get; set; } = 8;

    /// <summary>下载失败重试次数。</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>是否下载资源文件（assets）。</summary>
    public bool DownloadAssets { get; set; } = true;

    /// <summary>是否下载服务端。</summary>
    public bool DownloadServer { get; set; } = false;

    /// <summary>下载完成后是否自动导入到启动器目录。</summary>
    public bool AutoImport { get; set; } = true;

    /// <summary>加载器安装器下载后是否自动运行安装（需要本机有 Java）。</summary>
    public bool AutoRunInstaller { get; set; } = false;

    /// <summary>下载超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 60;

    public string GetMcDir()
    {
        if (!string.IsNullOrWhiteSpace(McDir)) return McDir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
    }
}

public static class ConfigStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameDock", "config.json");

    public static AppConfig Load(string? path = null)
    {
        var file = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(file)) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"读取配置失败，使用默认值：{ex.Message}");
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config, string? path = null)
    {
        var file = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }));
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"保存配置失败：{ex.Message}");
        }
    }
}
