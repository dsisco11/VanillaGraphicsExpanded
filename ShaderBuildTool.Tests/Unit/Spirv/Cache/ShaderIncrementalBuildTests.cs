using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests.Unit.Spirv.Cache;

/// <summary>Checks that shader cache keys include source, stage, entry point, and compiler identity.</summary>
public sealed class ShaderIncrementalBuildTests
{
    #region Compiler identity
    /// <summary>Each compiler-affecting input changes the cache key, including build configuration policy.</summary>
    [Fact]
    public void CacheKeyCoversSourceStageEntryPointAndCompilerIdentity()
    {
        var cache = new ShaderBuildTool.Spirv.ShaderVariantCache("unused", "compiler-debug");
        string key = cache.Key("source", "vertex", "main");
        Assert.Equal(key, cache.Key("source", "vertex", "main"));
        Assert.NotEqual(key, cache.Key("changed source", "vertex", "main"));
        Assert.NotEqual(key, cache.Key("source", "fragment", "main"));
        Assert.NotEqual(key, cache.Key("source", "vertex", "alternate"));
        Assert.NotEqual(key, new ShaderBuildTool.Spirv.ShaderVariantCache("unused", "compiler-release").Key("source", "vertex", "main"));
    }

    #endregion

}
