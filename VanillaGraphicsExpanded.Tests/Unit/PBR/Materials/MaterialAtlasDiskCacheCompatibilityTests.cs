using System;
using System.Globalization;
using System.IO;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Tests.TestSupport;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasDiskCacheCompatibilityTests
{
    [Fact]
    public void ExistingMetaJsonAndPayload_CanBeRead_ByNewCacheImplementation()
    {
        var fs = new InMemoryMaterialAtlasFileSystem();
        string root = $"memcache/{Guid.NewGuid():N}";
        string metaPath = $"{root}/meta.json";

        AtlasCacheKey key = AtlasCacheKey.FromUtf8(1, "compat|normaldepth|tile");

        string stem = string.Format(CultureInfo.InvariantCulture, "v{0}-{1:x16}", key.SchemaVersion, key.Hash64);
        string entryId = stem + ".norm";
        string payloadPath = $"{root}/{entryId}.dds";

        // 1x1 tile, RGBA16_UNORM
        ushort[] rgbaU16 = [
            (ushort)(0.1f * 65535f + 0.5f),
            (ushort)(0.2f * 65535f + 0.5f),
            (ushort)(0.3f * 65535f + 0.5f),
            (ushort)(0.4f * 65535f + 0.5f)
        ];

        byte[] ddsBytes;
        using (var ms = new MemoryStream())
        {
            DdsRgba16UnormCodec.WriteRgba16Unorm(ms, width: 1, height: 1, rgbaU16);
            ddsBytes = ms.ToArray();
        }

        fs.WriteAllBytes(payloadPath, ddsBytes);

        var index = MaterialAtlasDiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
        index.Entries[entryId] = new MaterialAtlasDiskCacheIndex.Entry(
            Kind: "normalDepth",
            SchemaVersion: key.SchemaVersion,
            Hash64: key.Hash64,
            Width: 1,
            Height: 1,
            Channels: 4,
            CreatedUtcTicks: DateTime.UtcNow.Ticks,
            LastAccessUtcTicks: DateTime.UtcNow.Ticks,
            SizeBytes: ddsBytes.Length,
            DdsFileName: null,
            Provenance: null,
            MetadataPresent: null);
        index.RecomputeTotals();
        index.SetSavedNow(DateTime.UtcNow.Ticks);
        index.SaveAtomic(fs, metaPath);

        var cache = new MaterialAtlasDiskCache(root, maxBytes: 64L * 1024 * 1024, fs);

        Assert.True(cache.HasNormalDepthTile(key));
        Assert.True(cache.TryLoadNormalDepthTile(key, out float[] loaded));
        Assert.Equal(4, loaded.Length);

        const float Eps = (1f / 65535f) + 1e-6f;
        Assert.InRange(MathF.Abs(loaded[0] - 0.1f), 0f, Eps);
        Assert.InRange(MathF.Abs(loaded[1] - 0.2f), 0f, Eps);
        Assert.InRange(MathF.Abs(loaded[2] - 0.3f), 0f, Eps);
        Assert.InRange(MathF.Abs(loaded[3] - 0.4f), 0f, Eps);
    }
}
