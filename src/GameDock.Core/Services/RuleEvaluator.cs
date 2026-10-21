using System.Runtime.InteropServices;
using GameDock.Core.Models;

namespace GameDock.Core.Services;

/// <summary>版本 JSON 中 library.rules 的 OS/架构匹配（决定该库在当前系统是否下载）。</summary>
public static class RuleEvaluator
{
    public static string CurrentOsName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
        : "unknown";

    public static string CurrentOsVersion => Environment.OSVersion.VersionString;

    public static IReadOnlyList<string> CurrentArchAliases
    {
        get
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => new[] { "x64", "amd64" },
                Architecture.X86 => new[] { "x86", "i386" },
                Architecture.Arm64 => new[] { "arm64", "aarch64" },
                _ => new[] { RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() },
            };
        }
    }

    public static bool Allowed(List<LibraryRule>? rules)
    {
        if (rules is null || rules.Count == 0) return true;
        // 规则语义：默认拒绝；遇 allow 规则且条件匹配则放行；遇 disallow 规则且条件匹配则拒绝。
        var allowed = false;
        foreach (var rule in rules)
        {
            var match = RuleMatches(rule);
            if (rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase))
            {
                if (match) allowed = true;
            }
            else if (rule.Action.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                if (match) return false;
            }
        }
        return allowed;
    }

    private static bool RuleMatches(LibraryRule rule)
    {
        // features 条件（如 has_custom_resolution）默认视为不匹配，正常桌面环境不使用。
        if (rule.Features is { Count: > 0 }) return false;

        if (rule.Os is null) return true;

        var os = rule.Os;
        var osMatch = string.IsNullOrEmpty(os.Name) || os.Name.Equals(CurrentOsName, StringComparison.OrdinalIgnoreCase);
        if (!osMatch) return false;

        var archMatch = string.IsNullOrEmpty(os.Arch)
            || CurrentArchAliases.Any(a => a.Equals(os.Arch, StringComparison.OrdinalIgnoreCase));
        if (!archMatch) return false;

        if (!string.IsNullOrEmpty(os.Version))
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(CurrentOsVersion, os.Version)) return false;
            }
            catch
            {
                // 版本正则异常时放行，避免规则误伤。
            }
        }
        return true;
    }
}
