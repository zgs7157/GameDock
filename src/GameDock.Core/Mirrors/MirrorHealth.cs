using System.Diagnostics;

namespace GameDock.Core.Mirrors;

/// <summary>镜像健康检测：请求元数据端点测延迟，供 GUI/CLI 展示与自动切换参考。</summary>
public sealed class MirrorHealth
{
    private readonly HttpClient _http;

    public MirrorHealth(HttpClient http) => _http = http;

    /// <summary>返回镜像测得的延迟毫秒数；请求失败返回 -1。</summary>
    public async Task<long> PingAsync(IMcMirror mirror, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // version_manifest 很小，适合做探测。
            var url = mirror.MetaBase + "/mc/game/version_manifest_v2.json";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "GameDock/0.1");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            return resp.IsSuccessStatusCode ? sw.ElapsedMilliseconds : -1;
        }
        catch
        {
            return -1;
        }
    }
}
