using GameDock.Core.Mirrors;

namespace GameDock.Core.Util;

/// <summary>为任意下载 URL 生成“原始 + 各镜像重写”的候选列表（去重、保序）。</summary>
public static class UrlCandidates
{
    public static IReadOnlyList<string> Build(string url, IReadOnlyList<IMcMirror> mirrors)
    {
        var list = new List<string> { url };
        foreach (var mirror in mirrors)
        {
            var r = mirror.RewriteUrl(url);
            if (!list.Contains(r)) list.Add(r);
        }
        return list;
    }
}
