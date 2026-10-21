using GameDock.Core.Mirrors;
using GameDock.Core.Util;
using Xunit;

namespace GameDock.Core.Tests;

public class MirrorRewriteTests
{
    private readonly BmclapiMirror _bmclapi = new();

    [Fact]
    public void Bmclapi_RewritesMetaAndResources()
    {
        Assert.Equal("https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
            _bmclapi.RewriteUrl("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));

        Assert.Equal("https://bmclapi2.bangbang93.com/assets/ab/abcdef123456",
            _bmclapi.RewriteUrl("https://resources.download.minecraft.net/objects/ab/abcdef123456"));

        Assert.Equal("https://bmclapi2.bangbang93.com/libraries/com/mojang/patchy/1.1/patchy-1.1.jar",
            _bmclapi.RewriteUrl("https://libraries.minecraft.net/com/mojang/patchy/1.1/patchy-1.1.jar"));
    }

    [Fact]
    public void Bmclapi_RewritesMavenAndFabricMeta()
    {
        Assert.Equal("https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar",
            _bmclapi.RewriteUrl("https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar"));

        Assert.Equal("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.20.4",
            _bmclapi.RewriteUrl("https://meta.fabricmc.net/v2/versions/loader/1.20.4"));

        Assert.Equal("https://bmclapi2.bangbang93.com/forge/net/minecraftforge/forge/promotions_slim.json",
            _bmclapi.RewriteUrl("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json"));
    }

    [Fact]
    public void UrlCandidates_PrefersOriginalThenRewrites()
    {
        var mirrors = new IMcMirror[] { new OfficialMirror(), _bmclapi };
        var candidates = UrlCandidates.Build("https://resources.download.minecraft.net/objects/aa/aaaa", mirrors);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("https://resources.download.minecraft.net/objects/aa/aaaa", candidates[0]);
        Assert.Equal("https://bmclapi2.bangbang93.com/assets/aa/aaaa", candidates[1]);
    }

    [Fact]
    public void UrlCandidates_Deduplicates()
    {
        var mirrors = new IMcMirror[] { new OfficialMirror(), new OfficialMirror() };
        var candidates = UrlCandidates.Build("https://example.com/x.jar", mirrors);
        Assert.Single(candidates);
    }

    [Fact]
    public void CustomMirror_KeepsKnownStructure()
    {
        var m = new CustomMirror("my", "自建", "https://mc.example.com/");
        Assert.Equal("https://mc.example.com", m.MetaBase);
        Assert.Equal("https://mc.example.com/assets", m.AssetsBase);
        Assert.Equal("https://mc.example.com/libraries", m.LibrariesBase);
    }
}
