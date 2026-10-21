using System.IO.Compression;
using GameDock.Core.Models;

namespace GameDock.Core.Services;

/// <summary>把 natives 分类器 jar 解压到版本目录的 natives 子目录（PCL2/HMCL 约定路径）。</summary>
public static class NativesExtractor
{
    /// <summary>返回成功解压的 natives 文件数。</summary>
    public static int Extract(string nativesJarPath, string nativesDir, IReadOnlyList<string>? excludeEntries, IProgress<string>? progress = null)
    {
        if (!File.Exists(nativesJarPath)) return 0;

        Directory.CreateDirectory(nativesDir);
        var count = 0;
        using var zip = ZipFile.OpenRead(nativesJarPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

            // 排除规则（如 META-INF/、META-INF/MANIFEST.MF）。
            var skip = false;
            if (excludeEntries is not null)
            {
                foreach (var ex in excludeEntries)
                {
                    var norm = ex.TrimEnd('/');
                    if (entry.FullName.Equals(norm + "/" + entry.Name, StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.StartsWith(norm + "/", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.Equals(norm, StringComparison.OrdinalIgnoreCase))
                    {
                        skip = true;
                        break;
                    }
                }
            }
            if (skip) continue;

            var name = Path.GetFileName(entry.FullName.Replace('/', '\\'));
            if (string.IsNullOrEmpty(name)) continue;

            var dest = Path.Combine(nativesDir, name);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                count++;
            }
            catch (Exception ex)
            {
                Util.Log.Warn($"natives 解压失败 {entry.FullName}：{ex.Message}");
            }
        }
        progress?.Report($"natives 解压完成：{count} 个文件 → {nativesDir}");
        return count;
    }

    /// <summary>计算当前平台的 natives 分类器 key，如 natives-windows / natives-windows-arm64。</summary>
    public static string NativeClassifierKey()
    {
        var os = RuleEvaluator.CurrentOsName;
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "64",
            System.Runtime.InteropServices.Architecture.X86 => "32",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => string.Empty,
        };
        return arch.Length == 0 ? $"natives-{os}" : $"natives-{os}-{arch}";
    }
}
