using System.Text.Json;
using GameDock.Core.Mirrors;

namespace GameDock.Core.Services;

/// <summary>带重试与镜像回退的 HTTP 客户端（JSON / 原始文本）。</summary>
public sealed class McJsonClient
{
    private readonly HttpClient _http;
    private readonly int _retryCount;

    public McJsonClient(HttpClient http, int retryCount = 3)
    {
        _http = http;
        _retryCount = Math.Max(1, retryCount);
    }

    /// <summary>GET 并反序列化 JSON；失败时按镜像列表顺序自动切换镜像。</summary>
    public async Task<T?> GetJsonAsync<T>(string url, IReadOnlyList<IMcMirror> mirrors, CancellationToken ct)
    {
        var text = await GetStringAsync(url, mirrors, ct);
        return JsonSerializer.Deserialize<T>(text, JsonOpts);
    }

    /// <summary>GET 原始文本；失败时按镜像列表顺序自动切换镜像。</summary>
    public async Task<string> GetStringAsync(string url, IReadOnlyList<IMcMirror> mirrors, CancellationToken ct)
    {
        var lastEx = (Exception?)null;
        foreach (var mirror in mirrors)
        {
            var attemptUrl = mirror.Key.Equals("official", StringComparison.OrdinalIgnoreCase) ? url : mirror.RewriteUrl(url);
            for (var i = 0; i < _retryCount; i++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, attemptUrl);
                    req.Headers.Add("User-Agent", "GameDock/0.1");
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsStringAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Util.Log.Warn($"GET {attemptUrl} 失败（{ex.Message}），第 {i + 1}/{_retryCount} 次重试或切换镜像…");
                    await Task.Delay(300 * (i + 1), ct);
                }
            }
        }
        throw new IOException($"所有镜像均无法获取 {url}：{lastEx?.Message}");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
