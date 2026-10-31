using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.TestSupport;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks digest source isolation and reload contracts without allocating a graphics context.</summary>
[Collection("CPU state")]
public sealed class ShaderDigestIndexCacheUnitTests
{
    private const string ManifestPath = "shaders/" + ShaderBinaryDigest.FileName;
    private readonly CountingDiskCacheFileSystem storage = new();
    #region Manifest lifetime
    /// <summary>Canonicalized asset IDs still select uppercase generated hashes without sharing mutable payloads.</summary>
    [Fact]
    public void VariantAssetsSurviveEngineCaseCanonicalization()
    {
        using var fixture = new BinaryShaderApiFixture();
        string path = TestAssetSnapshot.Built.Paths.First(path => path.StartsWith("shaders/variants/", StringComparison.Ordinal)
            && path.EndsWith(".spv", StringComparison.Ordinal));
        byte[] expected = TestAssetSnapshot.Built.Copy(path);
        var location = Vintagestory.API.Common.AssetLocation.Create(path, "vanillagraphicsexpanded");
        Assert.NotEqual(path, location.Path);
        var asset = fixture.Api.Assets.TryGet(location, loadAsset: true);
        Assert.NotNull(asset);
        Assert.Equal(expected, asset.Data);
        asset.Data[0] ^= byte.MaxValue;
        Assert.Equal(expected, fixture.Api.Assets.TryGet(location, loadAsset: true)!.Data);
        Assert.Equal(2, fixture.Reads.Count);
    }

    /// <summary>Owners and domains have independent indexes while repeated lookups retain one parsed object.</summary>
    [Fact]
    public void AssetOwnersAndDomainsAreIsolated()
    {
        using var first = new BinaryShaderApiFixture();
        using var second = new BinaryShaderApiFixture();
        var a = ShaderDigestIndexCache.ForAssets(first.Api.Assets, "vanillagraphicsexpanded");
        Assert.NotNull(a);
        Assert.Same(a, ShaderDigestIndexCache.ForAssets(first.Api.Assets, "vanillagraphicsexpanded"));
        Assert.NotSame(a, ShaderDigestIndexCache.ForAssets(first.Api.Assets, "otherdomain"));
        Assert.NotSame(a, ShaderDigestIndexCache.ForAssets(second.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Equal(2, first.Reads.Count(path => path == ManifestPath));
        Assert.Single(second.Reads.Where(path => path == ManifestPath));
    }

    /// <summary>Invalid metadata is read once and newly supplied metadata is observed after reload.</summary>
    [Fact]
    public void UnavailableMetadataIsCachedUntilReload()
    {
        using var assets = new BinaryShaderApiFixture();
        assets.Overrides[ManifestPath] = [];
        Assert.Null(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        assets.Overrides.Remove(ManifestPath);
        Assert.Null(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Single(assets.Reads.Where(path => path == ManifestPath));
        ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
        Assert.NotNull(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Equal(2, assets.Reads.Count(path => path == ManifestPath));
    }

    /// <summary>Canonical file paths share parsed metadata and refresh only at the same reload boundary.</summary>
    [Fact]
    public void FileMetadataAndMissingFilesAreCachedUntilReload()
    {
        string path = Path.Combine(Path.GetTempPath(), "vge-digests-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Null(ShaderDigestIndexCache.ForFile(path, storage));
            storage.WriteAllBytes(path, ShaderBinaryDigest.Encode(new() { ["a.spv"] = new(20, new string('A', 64)) }));
            Assert.Null(ShaderDigestIndexCache.ForFile(path, storage));
            ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
            var first = ShaderDigestIndexCache.ForFile(path, storage);
            Assert.NotNull(first);
            storage.WriteAllBytes(path, ShaderBinaryDigest.Encode(new() { ["b.spv"] = new(24, new string('B', 64)) }));
            Assert.Same(first, ShaderDigestIndexCache.ForFile(Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path)), storage));
            ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
            var replacement = ShaderDigestIndexCache.ForFile(path, storage);
            Assert.NotNull(replacement);
            Assert.NotSame(first, replacement);
            Assert.Contains("b.spv", replacement.Binaries.Keys);
        }
        finally { ShaderDigestIndexCache.Clear(); storage.DeleteFile(path); }
    }
    #endregion

}
