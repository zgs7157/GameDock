using System.Net;
using GameDock.Core.Config;
using GameDock.Core.Loaders;
using GameDock.Core.Mirrors;
using GameDock.Core.Services;
using GameDock.Core.Util;

namespace GameDock.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Log.Message += (_, e) =>
        {
            var color = e.Level switch
            {
                "ERROR" => ConsoleColor.Red,
                "WARN" => ConsoleColor.Yellow,
                "DEBUG" => ConsoleColor.DarkGray,
                _ => ConsoleColor.Gray,
            };
            Console.ForegroundColor = color;
            Console.WriteLine($"[{e.Time:HH:mm:ss}] {e.Message}");
            Console.ResetColor();
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return await DispatchAsync(args, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Info("已取消。");
            return 130;
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            return 1;
        }
    }

    private static async Task<int> DispatchAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return cmd switch
        {
            "list" or "ls" => await ListAsync(rest, ct),
            "download" or "dl" => await DownloadAsync(rest, ct),
            "fabric" => await FabricAsync(rest, ct),
            "forge" => await ForgeAsync(rest, ct),
            "neoforge" => await NeoForgeAsync(rest, ct),
            "optifine" or "of" => await OptifineAsync(rest, ct),
            "mirror" => await MirrorAsync(rest, ct),
            "config" => ConfigAsync(rest),
            "launch-dirs" => LaunchDirsAsync(rest),
            "help" or "-h" or "--help" => PrintHelpAndReturn(),
            _ => Unknown(cmd),
        };
    }

    // ---------- 通用设施 ----------

    private static HttpClient NewHttp() => new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    private static (AppConfig Config, MirrorRegistry Mirrors, HttpClient Http) BuildCore(FlagParser flags, string? mirrorKey)
    {
        var config = ConfigStore.Load(flags.Get("--config"));
        if (mirrorKey is not null) config.Mirror = mirrorKey;
        var mirrors = new MirrorRegistry(Path.Combine(Path.GetDirectoryName(ConfigStore.DefaultPath)!, "mirrors.json"));
        return (config, mirrors, NewHttp());
    }

    private static DownloadOptions MakeOptions(AppConfig config, FlagParser flags)
    {
        var mcDir = flags.Get("--mc-dir") ?? config.GetMcDir();
        return new DownloadOptions
        {
            McDir = mcDir,
            DownloadClient = !flags.Has("--no-client"),
            DownloadServer = flags.Has("--server") || config.DownloadServer,
            DownloadAssets = !flags.Has("--no-assets") && config.DownloadAssets,
            ExtractNatives = !flags.Has("--no-natives"),
        };
    }

    // ---------- list ----------

    private static async Task<int> ListAsync(string[] args, CancellationToken ct)
    {
        var flags = new FlagParser(args);
        var (_, mirrors, http) = BuildCore(flags, flags.Get("--mirror"));
        try
        {
            var vdl = new VersionDownloader(http, mirrors.Ordered(flags.Get("--mirror") ?? "bmclapi"), 4, 3);
            var manifest = await vdl.GetManifestAsync(ct);
            var type = flags.Get("--type") ?? "release";
            var versions = type.ToLowerInvariant() switch
            {
                "release" => manifest.Versions.Where(v => v.Type == "release").ToList(),
                "snapshot" => manifest.Versions.Where(v => v.Type == "snapshot").ToList(),
                "all" => manifest.Versions.ToList(),
                _ => throw new ArgumentException("--type 仅支持 release / snapshot / all"),
            };

            var search = flags.Get("--contains");
            if (!string.IsNullOrWhiteSpace(search))
                versions = versions.Where(v => v.Id.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

            Console.WriteLine($"共 {versions.Count} 个版本（最新正式版 {manifest.Latest?.Release}，快照 {manifest.Latest?.Snapshot}）：");
            foreach (var v in versions.Take(200))
                Console.WriteLine($"  {v.Id,-28} {v.Type,-10} {v.ReleaseTime?[..10]}");
            if (versions.Count > 200)
                Console.WriteLine($"  …（仅显示前 200，可加 --contains 过滤）");
            return 0;
        }
        finally
        {
            http.Dispose();
        }
    }

    // ---------- download ----------

    private static async Task<int> DownloadAsync(string[] args, CancellationToken ct)
    {
        var flags = new FlagParser(args);
        if (flags.Positional.Count == 0)
        {
            Console.WriteLine("用法：gamedock download <版本ID|latest:release|latest:snapshot> [--mc-dir 目录] [--mirror key] [--server] [--no-assets]");
            return 1;
        }

        var (config, mirrors, http) = BuildCore(flags, flags.Get("--mirror"));
        var options = MakeOptions(config, flags);
        options.WriteVersionJson = !flags.Has("--no-import");

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.FilesTotal == 0) return;
            var bar = new string('#', (int)(p.Percent / 2)) + new string('-', 50 - (int)(p.Percent / 2));
            Console.Write($"\r[{bar}] {p.Percent,5:F1}%  {p.FilesDone}/{p.FilesTotal}  {p.CurrentFile,-48}");
        });
        var logProgress = new Progress<string>(s => Console.WriteLine());

        try
        {
            var vdl = new VersionDownloader(http, mirrors.Ordered(config.Mirror), config.Threads, config.RetryCount);
            var manifest = await vdl.GetManifestAsync(ct);
            var entry = vdl.ResolveVersion(manifest, flags.Positional[0]);
            var report = await vdl.DownloadVersionAsync(entry, options, progress, logProgress, ct);
            Console.WriteLine();
            Console.WriteLine($"导入完成 → {report.McDir}");
            Console.WriteLine($"  版本：{report.VersionId}  客户端：{(report.ClientWritten ? "是" : "否")}  服务端：{(report.ServerWritten ? "是" : "否")}");
            Console.WriteLine($"  库：{report.LibrariesWritten}  资源：{report.AssetsWritten}  natives：{report.NativesExtracted}");
            Console.WriteLine("启动 PCL2 / HMCL 即可看到该版本。");
            return 0;
        }
        finally
        {
            http.Dispose();
        }
    }

    // ---------- fabric ----------

    private static async Task<int> FabricAsync(string[] args, CancellationToken ct)
    {
        var flags = new FlagParser(args);
        if (flags.Positional.Count == 0)
        {
            Console.WriteLine("用法：gamedock fabric <MC版本> [--loader 版本] [--mc-dir 目录] [--mirror key]");
            return 1;
        }
        var mc = flags.Positional[0];
        var (config, mirrors, http) = BuildCore(flags, flags.Get("--mirror"));
        var options = MakeOptions(config, flags);
        options.DownloadServer = false;

        try
        {
            var svc = new FabricService(http, mirrors.Ordered(config.Mirror), config.Threads, config.RetryCount);
            var loaders = await svc.GetLoaderVersionsAsync(mc, ct);
            var loader = flags.Get("--loader") ?? loaders.FirstOrDefault()
                ?? throw new IOException($"Fabric 没有 {mc} 的加载器");
            if (flags.Get("--loader") is null)
                Console.WriteLine($"未指定 --loader，使用最新：{loader}");
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.FilesTotal == 0) return;
                Console.Write($"\r{p.Percent,5:F1}%  {p.FilesDone}/{p.FilesTotal}  {p.CurrentFile,-48}");
            });
            var log = new Progress<string>(s => Console.WriteLine());
            var result = await svc.InstallAsync(mc, loader, options, progress, log, ct);
            Console.WriteLine();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        finally
        {
            http.Dispose();
        }
    }

    // ---------- forge ----------

    private static async Task<int> ForgeAsync(string[] args, CancellationToken ct)
    {
        var flags = new FlagParser(args);
        if (flags.Positional.Count == 0)
        {
            Console.WriteLine("用法：gamedock forge <MC版本> [--version Forge版本] [--mc-dir 目录] [--mirror key] [--run]");
            return 1;
        }
        var mc = flags.Positional[0];
        var (config, mirrors, http) = BuildCore(flags, flags.Get("--mirror"));

        try
        {
            var svc = new ForgeService(http, mirrors.Ordered(config.Mirror), config.Threads, config.RetryCount);
            var versions = await svc.GetForgeVersionsAsync(mc, ct);
            if (versions.Count == 0)
            {
                Console.WriteLine($"没有找到 {mc} 的 Forge 推荐/最新版本，请用 --version 指定（如 47.2.0）");
                return 1;
            }
            var forge = flags.Get("--version") ?? versions[0];
            if (flags.Get("--version") is null)
                Console.WriteLine($"未指定 --version，使用推荐版本：{forge}（可选 {string.Join("、", versions)}）");

            var downloadsDir = flags.Get("--downloads-dir") ?? Path.Combine(config.GetMcDir(), "downloads");
            var progress = new Progress<DownloadProgress>(p => { if (p.FilesTotal > 0) Console.Write($"\r{p.Percent,5:F1}%  {p.CurrentFile}"); });
            var log = new Progress<string>(s => Console.WriteLine());
            var result = await svc.DownloadInstallerAsync(mc, forge, downloadsDir, flags.Has("--run") || config.AutoRunInstaller, progress, log, ct);
            Console.WriteLine();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        finally
        {
            http.Dispose();
        }
    }

    // ---------- neoforge ----------

    private static async Task<int> NeoForgeAsync(string[] args, CancellationToken ct)
    {
        var flags = new FlagParser(args);
        var (config, mirrors, http) = BuildCore(flags, flags.Get("--mirror"));

        try
        {
            var svc = new NeoForgeService(http, mirrors.Ordered(config.Mirror), config.Threads, config.RetryCount);
            var versions = await svc.GetVersionsAsync(ct);
            var mc = flags.Get("--mc");
            if (mc is not null)
            {
                var filtered = svc.FilterByMc(mc, versions);
                if (filtered.Count > 0) versions = filtered;
                Console.WriteLine($"NeoForge（{mc}）可用版本（{versions.Count} 个）：");
            }
            else
            {
                Console.WriteLine($"NeoForge 可用版本（{versions.Count} 个，可用 --mc 过滤）：");
            }
            Console.WriteLine(string.Join(" ", versions.Take(20)) + (versions.Count > 20 ? " …" : ""));

            var ver = flags.Get("--version");
            if (ver is null)
            {
                Console.WriteLine("请用 --version 指定（例如 gamedock neoforge --mc 1.20.4 --version 20.4.237）");
                return 1;
            }
            if (!versions.Contains(ver))
                Console.WriteLine($"警告：{ver} 不在 metadata 列表中，仍尝试下载");

            var downloadsDir = flags.Get("--downloads-dir") ?? Path.Combine(config.GetMcDir(), "downloads");
            var progress = new Progress<DownloadProgress>(p => { if (p.FilesTotal > 0) Console.Write($"\r{p.Percent,5:F1}%  {p.CurrentFile}"); });
            var log = new Progress<string>(s => Console.WriteLine());
            var result = await svc.DownloadInstallerAsync(ver, downloadsDir, flags.Has("--run") || config.AutoRunInstaller, progress, log, ct);
            Console.WriteLine();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        finally
        {
            http.Dispose();
        }
    }

    // ---------- optifine ----------

    private static async Task<int> OptifineAsync(string[] args, CancellationToken ct)
    {
        var flags = new FlagParser(args);
        if (flags.Positional.Count == 0)
        {
            Console.WriteLine("用法：gamedock optifine <MC版本> [--index N] [--mc-dir 目录] [--mirror key] [--run]");
            return 1;
        }
        var mc = flags.Positional[0];
        var (config, mirrors, http) = BuildCore(flags, flags.Get("--mirror"));

        try
        {
            var svc = new OptifineService(http, mirrors.Ordered(config.Mirror), config.Threads, config.RetryCount);
            var list = await svc.GetReleasesAsync(mc, ct);
            var idxStr = flags.Get("--index");
            if (idxStr is null)
            {
                Console.WriteLine($"{mc} 的 OptiFine 发布（{list.Count} 个）：");
                for (var i = 0; i < list.Count; i++)
                    Console.WriteLine($"  [{i}] {list[i].Filename}  {list[i].Type}");
                Console.WriteLine("请用 --index N 选择后下载");
                return 1;
            }
            if (!int.TryParse(idxStr, out var idx) || idx < 0 || idx >= list.Count)
            {
                Console.WriteLine($"--index 越界（0..{list.Count - 1}）");
                return 1;
            }

            var downloadsDir = flags.Get("--downloads-dir") ?? Path.Combine(config.GetMcDir(), "downloads");
            var progress = new Progress<DownloadProgress>(p => { if (p.FilesTotal > 0) Console.Write($"\r{p.Percent,5:F1}%  {p.CurrentFile}"); });
            var log = new Progress<string>(s => Console.WriteLine());
            var result = await svc.DownloadInstallerAsync(list[idx], downloadsDir, flags.Has("--run") || config.AutoRunInstaller, progress, log, ct);
            Console.WriteLine();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        finally
        {
            http.Dispose();
        }
    }

    // ---------- mirror ----------

    private static async Task<int> MirrorAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法：gamedock mirror list | add <key> <名称> <BaseUrl> | remove <key> | health");
            return 1;
        }
        var sub = args[0].ToLowerInvariant();
        var flags = new FlagParser(args.Skip(1).ToArray());
        var config = ConfigStore.Load(flags.Get("--config"));
        var registryPath = Path.Combine(Path.GetDirectoryName(ConfigStore.DefaultPath)!, "mirrors.json");
        var registry = new MirrorRegistry(registryPath);

        switch (sub)
        {
            case "list":
                foreach (var m in registry.All)
                    Console.WriteLine($"  {m.Key,-12} {m.DisplayName,-20} {m.MetaBase}" + (m.Key == config.Mirror ? "  ← 当前" : ""));
                return 0;

            case "add":
            {
                var pos = flags.Positional;
                if (pos.Count < 3)
                {
                    Console.WriteLine("用法：gamedock mirror add <key> <名称> <BaseUrl>");
                    return 1;
                }
                registry.UpsertCustom(pos[0], pos[1], pos[2]);
                Console.WriteLine($"已添加镜像 {pos[0]} → {pos[2]}（存于 {registryPath}）");
                return 0;
            }

            case "remove":
            {
                if (flags.Positional.Count == 0)
                {
                    Console.WriteLine("用法：gamedock mirror remove <key>");
                    return 1;
                }
                return registry.RemoveCustom(flags.Positional[0]) ? 0 : 1;
            }

            case "health":
            {
                var health = new MirrorHealth(NewHttp());
                foreach (var m in registry.All)
                {
                    var ms = await health.PingAsync(m, ct);
                    Console.WriteLine($"  {m.Key,-12} {m.DisplayName,-20} {(ms >= 0 ? ms + " ms" : "不可达")}");
                }
                return 0;
            }

            default:
                Console.WriteLine($"未知子命令：{sub}");
                return 1;
        }
    }

    // ---------- config ----------

    private static int ConfigAsync(string[] args)
    {
        var flags = new FlagParser(args);
        var config = ConfigStore.Load(flags.Get("--config"));

        var mcDir = flags.Get("--mc-dir");
        var mirror = flags.Get("--mirror");
        var threads = flags.Get("--threads");

        if (mcDir is null && mirror is null && threads is null)
        {
            Console.WriteLine("当前配置：");
            Console.WriteLine($"  MC 目录   ：{config.GetMcDir()}");
            Console.WriteLine($"  首选镜像  ：{config.Mirror}");
            Console.WriteLine($"  并发线程  ：{config.Threads}");
            Console.WriteLine($"  自动导入  ：{config.AutoImport}");
            Console.WriteLine($"  下载资源  ：{config.DownloadAssets}");
            Console.WriteLine($"  自动运行安装器：{config.AutoRunInstaller}");
            Console.WriteLine("修改：gamedock config --mc-dir <目录> [--mirror <key>] [--threads <N>]");
            return 0;
        }

        if (mcDir is not null) config.McDir = mcDir;
        if (mirror is not null) config.Mirror = mirror;
        if (threads is not null && int.TryParse(threads, out var t)) config.Threads = Math.Clamp(t, 1, 64);
        ConfigStore.Save(config, flags.Get("--config"));
        Console.WriteLine($"配置已保存：MC 目录={config.GetMcDir()}，镜像={config.Mirror}，线程={config.Threads}");
        return 0;
    }

    // ---------- launch-dirs ----------

    private static int LaunchDirsAsync(string[] args)
    {
        _ = new FlagParser(args);
        Console.WriteLine("本机检测到的启动器目录：");
        foreach (var d in LauncherLocator.Detect())
            Console.WriteLine($"  {(d.Exists ? "✔" : "✘")}  {d.Name}  {d.McDir}");
        return 0;
    }

    // ---------- help ----------

    private static int PrintHelpAndReturn()
    {
        PrintHelp();
        return 0;
    }

    private static int Unknown(string cmd)
    {
        Console.WriteLine($"未知命令：{cmd}（gamedock help 查看用法）");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            GameDock —— Minecraft 下载即导入启动器（PCL2 / HMCL / FCL）

            用法：
              gamedock list [--type release|snapshot|all] [--contains 关键字] [--mirror key]
              gamedock download <版本ID|latest:release|latest:snapshot> [--mc-dir 目录] [--mirror key]
                                  [--server] [--no-assets] [--no-client] [--no-natives]
              gamedock fabric <MC版本> [--loader 版本] [--mc-dir 目录]
              gamedock forge <MC版本> [--version Forge版本] [--mc-dir 目录] [--run]
              gamedock neoforge [--mc MC版本] --version <NeoForge版本> [--mc-dir 目录] [--run]
              gamedock optifine <MC版本> --index N [--mc-dir 目录] [--run]
              gamedock mirror list | add <key> <名称> <BaseUrl> | remove <key> | health
              gamedock config [--mc-dir 目录] [--mirror key] [--threads N]
              gamedock launch-dirs

            说明：
              · 下载内容自动写入 .minecraft 的 versions/libraries/assets，启动器启动即识别
              · 镜像被封时自动按列表回退（官方 → BMCLAPI → 自定义），可用 mirror 管理
              · --run 表示下载安装器后自动运行安装（需要本机有 Java 8+）
            """);
    }
}

/// <summary>极简参数解析：--key value 与 --flag 及位置参数。</summary>
internal sealed class FlagParser
{
    public List<string> Positional { get; } = new();

    private readonly Dictionary<string, string> _kv = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public FlagParser(IEnumerable<string> args)
    {
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = a.IndexOf('=');
                if (eq > 0)
                {
                    _kv[a[..eq]] = a[(eq + 1)..];
                    continue;
                }
                // 支持 --key value 双段写法：下一个 token 非 -- 开头且尚未作为位置参数时视为值。
                if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal) && !_kv.ContainsKey(a))
                {
                    _kv[a] = list[i + 1];
                    i++;
                }
                else if (!_flags.Contains(a))
                {
                    _flags.Add(a);
                }
            }
            else
            {
                Positional.Add(a);
            }
        }
    }

    public FlagParser(string[] args) : this((IEnumerable<string>)args)
    {
    }

    /// <summary>获取键值参数；支持 --key value 与 --key=value 两种写法。</summary>
    public string? Get(string key)
    {
        if (_kv.TryGetValue(key, out var v)) return v;
        return null;
    }

    public bool Has(string key) => _flags.Contains(key);
}
