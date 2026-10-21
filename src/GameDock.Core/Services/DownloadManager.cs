using GameDock.Core.Util;

namespace GameDock.Core.Services;

/// <summary>一次下载任务：多个候选 URL（主镜像 + 回退镜像），落盘校验。</summary>
public sealed class DownloadEntry
{
    public string Kind { get; init; } = "file";
    public string DisplayName { get; init; } = string.Empty;
    public string DestPath { get; init; } = string.Empty;
    public string? Sha1 { get; init; }
    public long Size { get; init; }
    public IReadOnlyList<string> UrlCandidates { get; init; } = Array.Empty<string>();

    public override string ToString() => string.IsNullOrEmpty(DisplayName) ? DestPath : DisplayName;
}

/// <summary>聚合进度。</summary>
public sealed class DownloadProgress
{
    public int FilesDone { get; set; }
    public int FilesTotal { get; set; }
    public long BytesDone { get; set; }
    public long BytesTotal { get; set; }
    public string? CurrentFile { get; set; }

    public double Percent => BytesTotal > 0 ? Math.Min(100, BytesDone * 100.0 / BytesTotal) : 0;
}

/// <summary>
/// 并发下载管理器：跳过已验证文件、失败重试、候选 URL 自动回退、
/// 使用 SemaphoreSlim 控制并发、通过 IProgress 上报进度。
/// </summary>
public sealed class DownloadManager
{
    private readonly HttpClient _http;
    private readonly int _retryCount;
    private readonly SemaphoreSlim _throttle;

    public DownloadManager(HttpClient http, int threads, int retryCount)
    {
        _http = http;
        _retryCount = Math.Max(1, retryCount);
        _throttle = new SemaphoreSlim(Math.Max(1, threads));
    }

    /// <summary>批量下载；任一条目在所有候选与重试后仍失败则抛出异常（先完成的已落盘）。</summary>
    public async Task DownloadAsync(IReadOnlyList<DownloadEntry> entries, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (entries.Count == 0) return;

        long bytesTotal = entries.Where(e => e.Size > 0).Sum(e => e.Size);
        long bytesDone = 0;
        var filesDone = 0;
        var gate = new object();

        async Task RunOne(DownloadEntry entry)
        {
            await _throttle.WaitAsync(ct);
            try
            {
                var result = await DownloadOneAsync(entry, ct);
                lock (gate)
                {
                    filesDone++;
                    bytesDone += result;
                    progress?.Report(new DownloadProgress
                    {
                        FilesDone = filesDone,
                        FilesTotal = entries.Count,
                        BytesDone = bytesDone,
                        BytesTotal = bytesTotal,
                        CurrentFile = entry.ToString(),
                    });
                }
            }
            finally
            {
                _throttle.Release();
            }
        }

        var tasks = entries.Select(RunOne).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task<long> DownloadOneAsync(DownloadEntry entry, CancellationToken ct)
    {
        // 已存在且校验通过 → 跳过。
        if (Hashing.FileValid(entry.DestPath, entry.Size, entry.Sha1))
        {
            Log.Debug($"跳过已存在文件：{entry.DestPath}");
            return entry.Size;
        }

        var dir = Path.GetDirectoryName(entry.DestPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var partPath = entry.DestPath + ".part";
        Exception? lastError = null;

        foreach (var url in entry.UrlCandidates)
        {
            for (var attempt = 0; attempt < _retryCount; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("User-Agent", "GameDock/0.1");
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();

                    await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                    await using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        await src.CopyToAsync(dst, 1 << 20, ct);
                    }

                    // 校验：大小与 sha1（若提供）。
                    if (entry.Size > 0)
                    {
                        var fi = new FileInfo(partPath);
                        if (fi.Length != entry.Size)
                            throw new IOException($"大小不符：期望 {entry.Size}，实际 {fi.Length}（{entry.DisplayName}）");
                    }
                    if (!string.IsNullOrEmpty(entry.Sha1))
                    {
                        var sha = Hashing.Sha1(partPath);
                        if (!sha.Equals(entry.Sha1, StringComparison.OrdinalIgnoreCase))
                            throw new IOException($"SHA1 不符：期望 {entry.Sha1}，实际 {sha}（{entry.DisplayName}）");
                    }

                    File.Move(partPath, entry.DestPath, overwrite: true);
                    Log.Info($"完成：{entry.DisplayName}");
                    return entry.Size;
                }
                catch (OperationCanceledException)
                {
                    TryDelete(partPath);
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Log.Warn($"下载失败 {url}（{ex.Message}），重试 {attempt + 1}/{_retryCount}");
                    await Task.Delay(400 * (attempt + 1), ct);
                }
            }
        }

        TryDelete(partPath);
        throw new IOException($"下载失败：{entry.DisplayName}（{lastError?.Message}）");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 忽略清理失败。
        }
    }
}
