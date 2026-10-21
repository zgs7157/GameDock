using GameDock.Core.Models;
using GameDock.Core.Services;
using Xunit;

namespace GameDock.Core.Tests;

public class ManifestParsingTests
{
    [Fact]
    public void ParseManifest_FindsVersions()
    {
        const string json = """
        {
          "latest": { "release": "1.21.4", "snapshot": "25w02a" },
          "versions": [
            { "id": "1.21.4", "type": "release", "url": "https://piston-meta.mojang.com/v1/packages/a/b.json", "sha1": "aa", "time": "2024-12-03T12:00:00+00:00", "releaseTime": "2024-12-03T12:00:00+00:00" },
            { "id": "25w02a", "type": "snapshot", "url": "https://piston-meta.mojang.com/v1/packages/c/d.json", "sha1": "bb", "time": "2025-01-08T12:00:00+00:00", "releaseTime": "2025-01-08T12:00:00+00:00" }
          ]
        }
        """;

        var manifest = Json.Deserialize<VersionManifest>(json)!;

        Assert.Equal("1.21.4", manifest.Latest!.Release);
        Assert.Equal(2, manifest.Versions.Count);
        Assert.Equal("release", manifest.Versions[0].Type);
        Assert.Equal("https://piston-meta.mojang.com/v1/packages/a/b.json", manifest.Versions[0].Url);
    }

    [Fact]
    public void ResolveVersion_HandlesLatest()
    {
        const string json = """
        { "latest": { "release": "1.21.4", "snapshot": "25w02a" },
          "versions": [
            { "id": "1.21.4", "type": "release", "url": "u1", "time": "t", "releaseTime": "t" },
            { "id": "25w02a", "type": "snapshot", "url": "u2", "time": "t", "releaseTime": "t" }
          ] }
        """;

        var manifest = Json.Deserialize<VersionManifest>(json)!;
        var vdl = new VersionDownloader(new HttpClient(), Array.Empty<GameDock.Core.Mirrors.IMcMirror>(), 2, 2);

        Assert.Equal("1.21.4", vdl.ResolveVersion(manifest, "latest:release").Id);
        Assert.Equal("25w02a", vdl.ResolveVersion(manifest, "latest:snapshot").Id);
        Assert.Equal("1.21.4", vdl.ResolveVersion(manifest, "1.21.4").Id);
        Assert.Throws<IOException>(() => vdl.ResolveVersion(manifest, "no-such-version"));
    }
}
