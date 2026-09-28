using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Exercises real per-variant reuse across edits, damaged outputs and catalog changes.</summary>
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

    /// <summary>A binding-only contract edit changes emitted source and therefore invalidates the compiled variant.</summary>
    [Fact]
    public void BindingContractChangeInvalidatesEmittedVariantKey()
    {
        const string source = "#version 450 core\nuniform sampler2D image; layout(location=0) out vec4 color; void main(){color=texture(image,vec2(0));}";
        var first = new VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract();
        first.RegisterSamplerUnit("image", 1);
        var second = new VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract();
        second.RegisterSamplerUnit("image", 2);
        var cache = new ShaderBuildTool.Spirv.ShaderVariantCache("unused", "compiler");
        string before = ShaderBuildTool.Spirv.ShaderSourceLayout.Apply(source, "fsh", first);
        string after = ShaderBuildTool.Spirv.ShaderSourceLayout.Apply(source, "fsh", second);
        Assert.NotEqual(cache.Key(before, "fragment", "main"), cache.Key(after, "fragment", "main"));
    }
    #endregion

    #region Incremental publication
    /// <summary>Explicit content verification recompiles an edited include whose metadata was preserved.</summary>
    [Fact]
    public void StrictVerificationDetectsPreservedMetadataEdit()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string include = Path.Combine(fixture.Shaders, "fixture.inc");
        string binary = Path.Combine(fixture.Output, "vanillagraphicsexpanded", "shaders", "fixture.fsh.spv");
        byte[] original = File.ReadAllBytes(binary);
        DateTime timestamp = File.GetLastWriteTimeUtc(include);
        File.WriteAllText(include, File.ReadAllText(include).Replace("0.5", "0.7"));
        File.SetLastWriteTimeUtc(include, timestamp);
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true, verifyContents: true));
        Assert.False(original.SequenceEqual(File.ReadAllBytes(binary)));
    }

    /// <summary>A missing runtime binary is restored from its intact cache without rewriting cache entries or other outputs.</summary>
    [Fact]
    public void MissingPublishedBinaryIsRestoredFromCache()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string binary = Path.Combine(fixture.Output, "vanillagraphicsexpanded", "shaders", "fixture.fsh.spv");
        byte[] expected = File.ReadAllBytes(binary);
        string[] cached = Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "file-hashes.json").ToArray();
        Assert.NotEmpty(cached);
        foreach (string entry in cached) File.SetLastWriteTimeUtc(entry, DateTime.UnixEpoch);
        File.Delete(binary);
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        Assert.Equal(expected, File.ReadAllBytes(binary));
        Assert.All(cached, entry => Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(entry)));
    }

    /// <summary>An isolated source or include edit preserves unrelated compiled output timestamps.</summary>
    [Theory]
    [InlineData("fixture.inc", "#define FACTOR 0.25\n", "fixture.fsh.spv")]
    [InlineData("fixture.csh", "#version 450 core\nlayout(local_size_x=1) in; layout(std430,binding=0) buffer Data{uint value;}; void main(){value=2;}", "fixture.csh.spv")]
    public void IsolatedEditReusesUnaffectedVariants(string source, string replacement, string changed)
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string root = Path.Combine(fixture.Output, "vanillagraphicsexpanded", "shaders");
        var originals = Directory.GetFiles(root, "*.spv").ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        foreach (string binary in Directory.GetFiles(root, "*.spv")) File.SetLastWriteTimeUtc(binary, DateTime.UnixEpoch);
        File.WriteAllText(Path.Combine(fixture.Shaders, source), replacement);
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        foreach (var original in originals)
        {
            string binary = Path.Combine(root, original.Key!);
            if (original.Key == changed)
            {
                Assert.False(original.Value.SequenceEqual(File.ReadAllBytes(binary)));
                Assert.True(File.GetLastWriteTimeUtc(binary) > DateTime.UnixEpoch);
            }
            else
            {
                Assert.Equal(original.Value, File.ReadAllBytes(binary));
                Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(binary));
            }
        }
    }

    /// <summary>Missing or corrupt cached binaries cannot prevent regeneration of damaged published output.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedCacheAndOutputAreRebuilt(bool remove)
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        var expected = fixture.ContentSnapshot();
        string[] cached = Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*.bin", SearchOption.AllDirectories);
        Assert.NotEmpty(cached);
        foreach (string binary in cached)
        {
            if (remove) File.Delete(binary);
            else File.WriteAllBytes(binary, [0]);
        }
        string damaged = Path.Combine(fixture.Output, "vanillagraphicsexpanded", "shaders", "fixture.fsh.spv");
        File.WriteAllBytes(damaged, [0]);
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        Assert.Equal(expected.ToArray(), fixture.ContentSnapshot().ToArray());
    }

    /// <summary>Removing a registered stage prunes its old binary and digest without rebuilding retained stages.</summary>
    [Fact]
    public void RemovedStageIsPrunedFromPublishedCatalog()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string root = Path.Combine(fixture.Output, "vanillagraphicsexpanded", "shaders");
        string retained = Path.Combine(root, "fixture.vsh.spv");
        File.SetLastWriteTimeUtc(retained, DateTime.UnixEpoch);
        File.Delete(Path.Combine(fixture.Shaders, "fixture.csh"));
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true, registry: "build-validation-graphics"));
        Assert.False(File.Exists(Path.Combine(root, "fixture.csh.spv")));
        Assert.DoesNotContain("fixture.csh.spv", File.ReadAllText(Path.Combine(root, ShaderBinaryDigest.FileName)));
        Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(retained));
    }

    /// <summary>Explicit clean overrides an otherwise current incremental receipt and discards cache storage.</summary>
    [Fact]
    public void ExplicitCleanOverridesIncrementalReceipt()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string sentinel = Path.Combine(fixture.Output, "_cache", "clean-sentinel");
        File.WriteAllText(sentinel, "remove on explicit clean");
        string binary = Path.Combine(fixture.Output, "vanillagraphicsexpanded", "shaders", "fixture.vsh.spv");
        File.SetLastWriteTimeUtc(binary, DateTime.UnixEpoch);
        Assert.Equal(0, fixture.Build(2, clean: true, incremental: true));
        Assert.False(File.Exists(sentinel));
        Assert.True(File.GetLastWriteTimeUtc(binary) > DateTime.UnixEpoch);
    }
    #endregion
}
