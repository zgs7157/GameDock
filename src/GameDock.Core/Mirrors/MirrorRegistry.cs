using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDock.Core.Mirrors;

/// <summary>镜像源注册表：内置官方/BMCLAPI，支持用户新增自定义镜像（持久化到 mirrors.json）。</summary>
public sealed class MirrorRegistry
{
    private readonly List<IMcMirror> _mirrors = new()
    {
        new OfficialMirror(),
        new BmclapiMirror(),
    };

    private readonly string _storePath;

    public MirrorRegistry(string storePath)
    {
        _storePath = storePath;
        Load();
    }

    public IReadOnlyList<IMcMirror> All => _mirrors;

    public IMcMirror? Get(string key) => _mirrors.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>按偏好顺序返回镜像列表（首选在第一位）。</summary>
    public IReadOnlyList<IMcMirror> Ordered(string preferredKey)
    {
        var preferred = Get(preferredKey);
        if (preferred is null) return _mirrors.ToList();
        return new List<IMcMirror> { preferred }.Concat(_mirrors.Where(m => m.Key != preferred.Key)).ToList();
    }

    /// <summary>添加或更新自定义镜像。</summary>
    public void UpsertCustom(string key, string displayName, string baseUrl)
    {
        var existing = _mirrors.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is CustomMirror)
        {
            var idx = _mirrors.IndexOf(existing);
            _mirrors[idx] = new CustomMirror(key, displayName, baseUrl);
        }
        else
        {
            _mirrors.Remove(existing!);
            _mirrors.Add(new CustomMirror(key, displayName, baseUrl));
        }
        Save();
    }

    /// <summary>删除自定义镜像（内置镜像不可删）。</summary>
    public bool RemoveCustom(string key)
    {
        var mirror = _mirrors.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && m is CustomMirror);
        if (mirror is null) return false;
        _mirrors.Remove(mirror);
        Save();
        return true;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            var json = File.ReadAllText(_storePath);
            var doc = JsonSerializer.Deserialize<MirrorStore>(json);
            if (doc?.Custom is null) return;
            foreach (var c in doc.Custom)
            {
                if (!string.IsNullOrWhiteSpace(c.Key) && !string.IsNullOrWhiteSpace(c.BaseUrl))
                    _mirrors.Add(new CustomMirror(c.Key, c.DisplayName ?? c.Key, c.BaseUrl));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameDock] 读取镜像配置失败：{ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var store = new MirrorStore
            {
                Custom = _mirrors.OfType<CustomMirror>()
                    .Select(m => new MirrorEntry { Key = m.Key, DisplayName = m.DisplayName, BaseUrl = m.MetaBase })
                    .ToList(),
            };
            File.WriteAllText(_storePath, JsonSerializer.Serialize(store, JsonOpts));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameDock] 写入镜像配置失败：{ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class MirrorStore
    {
        [JsonPropertyName("custom")]
        public List<MirrorEntry> Custom { get; set; } = new();
    }

    private sealed class MirrorEntry
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = string.Empty;
    }
}
