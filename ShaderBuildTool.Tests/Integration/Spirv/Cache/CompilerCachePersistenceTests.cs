using System.Text;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Integration.Spirv.Cache;

/// <summary>Checks compiler cache persistence, canonical filenames, and warm reuse on disk.</summary>
public sealed class CompilerCachePersistenceTests
{
    #region Public API

    /// <summary>Compiler artifacts use Base32 paths while binary integrity remains hexadecimal SHA-256.</summary>
    [Fact]
    public void CompilerCacheUsesCanonicalFilenamesAndWarmReuse()
    {
        using var fixture = new ShaderBuildFixture();
        var cache = new ShaderVariantCache(fixture.Output, "compiler");
        string key = cache.Key("source", "compute", "main", Path.Combine(fixture.Root, "source.glsl"), fixture.Repository);
        Assert.True(ShaderCacheKey.IsValid(key));
        byte[] binary = [1, 2, 3];
        var digest = ShaderVariantCache.Digest(binary);
        Assert.True(ShaderRecordStore.IsDigest(digest.Digest));
        cache.Store(key, binary, digest);
        Assert.True(File.Exists(Path.Combine(fixture.Output, "_cache", key + ".bin")));
        Assert.True(new ShaderVariantCache(fixture.Output, "compiler").TryRead(key, out var restored, out var metadata));
        Assert.Equal(binary, restored);
        Assert.Equal(digest, metadata);
        Assert.False(cache.TryRead(ShaderBuildIdentities.Hash("legacy"), out _, out _));
    }
    #endregion
}
