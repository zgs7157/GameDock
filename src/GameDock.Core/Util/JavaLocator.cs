using System.Diagnostics;

namespace GameDock.Core.Util;

/// <summary>定位本机 Java：优先 JAVA_HOME，其次 PATH 中的 java。</summary>
public static class JavaLocator
{
    public sealed record JavaInfo(string Path, string Version, int Major);

    public static JavaInfo? Find()
    {
        var candidates = new List<string>();

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", IsWindows ? "java.exe" : "java"));
            if (!IsWindows) candidates.Add(Path.Combine(javaHome, "bin", "java"));
        }
        candidates.Add(IsWindows ? "java.exe" : "java");

        foreach (var c in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(c, "-version")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                p.WaitForExit(5000);
                var output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                var m = System.Text.RegularExpressions.Regex.Match(output, "\"([0-9]+)(\\.[0-9]+)*\"");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var major))
                {
                    var fullPath = Path.GetFullPath(c);
                    return new JavaInfo(fullPath, m.Groups[0].Value.Trim('"'), major);
                }
            }
            catch
            {
                // 该候选不可用，继续。
            }
        }
        return null;
    }

    private static bool IsWindows =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

    /// <summary>在用户机器上找到的 Java 缓存（单次会话内有效）。</summary>
    private static JavaInfo? _cached;

    public static JavaInfo? FindCached() => _cached ??= Find();
}
