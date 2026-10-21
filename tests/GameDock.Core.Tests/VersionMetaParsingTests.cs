using GameDock.Core.Models;
using GameDock.Core.Services;
using Xunit;

namespace GameDock.Core.Tests;

public class VersionMetaParsingTests
{
    [Fact]
    public void ParseVersionMeta_ExtractsDownloadsAndLibraries()
    {
        const string json = """
        {
          "id": "1.21.4",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "14", "sha1": "ix", "size": 100, "url": "https://piston-meta.mojang.com/v1/packages/ix/14.json" },
          "downloads": {
            "client": { "sha1": "cj", "size": 1000, "url": "https://piston-data.mojang.com/v1/objects/cj/client.jar" },
            "server": { "sha1": "sj", "size": 2000, "url": "https://piston-data.mojang.com/v1/objects/sj/server.jar" }
          },
          "libraries": [
            { "name": "com.mojang:patchy:1.1", "downloads": { "artifact": { "path": "com/mojang/patchy/1.1/patchy-1.1.jar", "sha1": "p", "size": 10, "url": "https://libraries.minecraft.net/com/mojang/patchy/1.1/patchy-1.1.jar" } } },
            { "name": "org.lwjgl:lwjgl:3.3.3", "rules": [ { "action": "disallow", "os": { "name": "windows" } } ], "downloads": { "artifact": { "path": "x.jar", "sha1": "y", "size": 1, "url": "https://libraries.minecraft.net/x.jar" } } },
            { "name": "org.lwjgl:lwjgl:3.3.3", "downloads": { "artifact": { "path": "a.jar", "sha1": "b", "size": 1, "url": "https://libraries.minecraft.net/a.jar" }, "classifiers": { "natives-windows": { "path": "c.jar", "sha1": "d", "size": 2, "url": "https://libraries.minecraft.net/c.jar" } } } }
          ],
          "javaVersion": { "component": "java-runtime-delta", "majorVersion": 21 }
        }
        """;

        var meta = Json.Deserialize<VersionMeta>(json)!;

        Assert.Equal("1.21.4", meta.Id);
        Assert.Equal("net.minecraft.client.main.Main", meta.MainClass);
        Assert.NotNull(meta.AssetIndex);
        Assert.Equal("cj", meta.Downloads!.Client!.Sha1);
        Assert.Equal("sj", meta.Downloads.Server!.Sha1);
        Assert.Equal(3, meta.Libraries.Count);
        Assert.Equal(21, meta.JavaVersion!.MajorVersion);

        // 规则库（windows 上被 disallow）不应入选。
        var lib = meta.Libraries[1];
        Assert.False(RuleEvaluator.Allowed(lib.Rules));
        Assert.True(RuleEvaluator.Allowed(meta.Libraries[2].Rules));
    }

    [Fact]
    public void ParseFabricLauncherMeta_UsesSameSchema()
    {
        // Fabric launcher meta 与原生版本 JSON 同构，可走同一导入流程。
        const string json = """
        {
          "id": "1.20.4-fabric-0.15.11",
          "inheritsFrom": "",
          "time": "t",
          "releaseTime": "t",
          "type": "release",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "arguments": { "game": [ "a" ], "jvm": [ "b" ] },
          "libraries": [ { "name": "net.fabricmc:fabric-loader:0.15.11", "downloads": { "artifact": { "path": "net/fabricmc/fabric-loader/0.15.11/fabric-loader-0.15.11.jar", "sha1": "s", "size": 1, "url": "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.15.11/fabric-loader-0.15.11.jar" } } } ]
        }
        """;

        var meta = Json.Deserialize<VersionMeta>(json)!;
        Assert.Equal("1.20.4-fabric-0.15.11", meta.Id);
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", meta.MainClass);
        Assert.Single(meta.Libraries);
        Assert.Equal("net/fabricmc/fabric-loader/0.15.11/fabric-loader-0.15.11.jar", meta.Libraries[0].Downloads!.Artifact!.Path);
    }
}
