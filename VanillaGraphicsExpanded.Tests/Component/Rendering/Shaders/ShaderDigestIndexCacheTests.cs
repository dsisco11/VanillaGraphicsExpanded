using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Checks digest manifest ownership and reload behavior through controlled engine asset providers.</summary>
[Collection("CPU state")]
public sealed class ShaderDigestIndexCacheTests
{
    #region Public API
    #region Manifest lifetime
    /// <summary>Canonicalized asset IDs still select uppercase generated hashes without sharing mutable payloads.</summary>
    [Fact]
    public void VariantAssetsSurviveEngineCaseCanonicalization()
    {
        // Arrange a packaged shader payload and an engine-canonicalized asset identity.
        using var fixture = new BinaryShaderApiFixture();
        string path = TestAssetSnapshot.Built.Paths.First(path => path.StartsWith("shaders/variants/", StringComparison.Ordinal)
            && path.EndsWith(".spv", StringComparison.Ordinal));
        byte[] expected = TestAssetSnapshot.Built.Copy(path);
        var location = Vintagestory.API.Common.AssetLocation.Create(path, "vanillagraphicsexpanded");

        // Assert the engine changes the asset ID before loading it.
        Assert.NotEqual(path, location.Path);

        // Act by loading the asset and corrupting the returned array.
        var asset = fixture.Api.Assets.TryGet(location, loadAsset: true);
        Assert.NotNull(asset);
        Assert.Equal(expected, asset.Data);
        asset.Data[0] ^= byte.MaxValue;

        // Assert a subsequent load remains isolated from that mutation.
        Assert.Equal(expected, fixture.Api.Assets.TryGet(location, loadAsset: true)!.Data);
        Assert.Equal(2, fixture.Reads.Count);
    }

    /// <summary>Owners and domains have independent indexes while repeated lookups retain one parsed object.</summary>
    [Fact]
    public void AssetOwnersAndDomainsAreIsolated()
    {
        // Arrange two independent engine asset owners and acquire one default-domain index.
        using var first = new BinaryShaderApiFixture();
        using var second = new BinaryShaderApiFixture();
        var a = ShaderDigestIndexCache.ForAssets(first.Api.Assets, "vanillagraphicsexpanded");
        Assert.NotNull(a);

        // Act and assert a repeated lookup for the same key reuses the parsed index.
        Assert.Same(a, ShaderDigestIndexCache.ForAssets(first.Api.Assets, "vanillagraphicsexpanded"));

        // Act and assert different domains or asset owners receive independent indexes.
        Assert.NotSame(a, ShaderDigestIndexCache.ForAssets(first.Api.Assets, "otherdomain"));
        Assert.NotSame(a, ShaderDigestIndexCache.ForAssets(second.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Equal(2, first.Reads.Count(path => path == ManifestPath));
        Assert.Single(second.Reads.Where(path => path == ManifestPath));
    }

    /// <summary>Invalid metadata is read once and newly supplied metadata is observed after reload.</summary>
    [Fact]
    public void UnavailableMetadataIsCachedUntilReload()
    {
        // Arrange an asset provider with an initially unavailable manifest.
        using var assets = new BinaryShaderApiFixture();
        assets.Overrides[ManifestPath] = [];

        // Act by looking up the miss, then supply metadata without crossing the reload boundary.
        Assert.Null(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        assets.Overrides.Remove(ManifestPath);
        Assert.Null(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));

        // Assert that the cached miss does not re-read the manifest.
        Assert.Single(assets.Reads.Where(path => path == ManifestPath));

        // Act at the explicit reload boundary and assert the newly available manifest is admitted.
        ShaderDigestReloadHook.Prefix(out _);
        ShaderDigestReloadHook.Finalizer(true);
        Assert.NotNull(ShaderDigestIndexCache.ForAssets(assets.Api.Assets, "vanillagraphicsexpanded"));
        Assert.Equal(2, assets.Reads.Count(path => path == ManifestPath));
    }

    /// <summary>Canonical file paths share parsed metadata and refresh only at the same reload boundary.</summary>
    [Fact]
    public void FileMetadataAndMissingFilesAreCachedUntilReload()
    {
        // Arrange a unique manifest path backed by the controlled cache filesystem.
        string path = Path.Combine(Path.GetTempPath(), "vge-digests-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Act on the missing path, then add metadata without invalidating the cached miss.
            Assert.Null(ShaderDigestIndexCache.ForFile(path, storage));
            storage.WriteAllBytes(path, ShaderBinaryDigest.Encode(new() { ["a.spv"] = new(20, new string('A', 64)) }));
            Assert.Null(ShaderDigestIndexCache.ForFile(path, storage));

            // Act at reload and capture the first parsed index.
            ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
            var first = ShaderDigestIndexCache.ForFile(path, storage);

            // Assert the reload produced a parsed index before changing the manifest again.
            Assert.NotNull(first);

            // Act by changing metadata and reading an equivalent path spelling before reload; assert the cached index is reused.
            storage.WriteAllBytes(path, ShaderBinaryDigest.Encode(new() { ["b.spv"] = new(24, new string('B', 64)) }));
            Assert.Same(first, ShaderDigestIndexCache.ForFile(Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path)), storage));

            // Act at a second reload and assert the updated manifest produces a replacement index.
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
    #endregion

    #region Private
    private const string ManifestPath = "shaders/" + ShaderBinaryDigest.FileName;
    private readonly CountingDiskCacheFileSystem storage = new();
    #endregion
}
