using System.Security.Cryptography;

namespace GameDock.Core.Util;

public static class Hashing
{
    /// <summary>计算文件 SHA1（十六进制小写）。</summary>
    public static string Sha1(string path)
    {
        using var sha = SHA1.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    /// <summary>校验文件是否有效（存在、大小一致、可选 sha1 一致）。</summary>
    public static bool FileValid(string path, long expectedSize, string? expectedSha1)
    {
        if (!File.Exists(path)) return false;
        var fi = new FileInfo(path);
        if (fi.Length != expectedSize) return false;
        if (!string.IsNullOrEmpty(expectedSha1))
        {
            try
            {
                return Sha1(path).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
        return true;
    }
}
