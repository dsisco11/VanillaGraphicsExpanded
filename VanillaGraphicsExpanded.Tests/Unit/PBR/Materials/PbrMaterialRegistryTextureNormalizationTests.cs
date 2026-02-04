using System.Collections.Generic;
using System.Numerics;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Common;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

public sealed class PbrMaterialRegistryTextureNormalizationTests
{
    [Fact]
    public void TryGetSurface_NormalizesTexturesPrefixAndExtension()
    {
        PbrMaterialRegistry.Instance.Clear();

        try
        {
            // Seed the derived surface map directly (test seam). Use the registry's private normalization
            // so the test stays aligned with the production key shape (textures/ prefix + extensionless).
            AssetLocation normalizedKey = NormalizeTextureKeyForTest(new AssetLocation("game", "block/test.png"));
            var surface = new PbrMaterialSurface(
                Roughness: 0.25f,
                Metallic: 0f,
                Emissive: 0f,
                DiffuseAlbedo: new Vector3(0.1f, 0.2f, 0.3f),
                SpecularF0: new Vector3(0.04f));

            var map = (Dictionary<AssetLocation, PbrMaterialSurface>)PbrMaterialRegistry.Instance.SurfaceByTexture;
            map[normalizedKey] = surface;

            // Query shapes seen around the engine/mod code: missing "textures/" and/or explicit extension.
            Assert.True(PbrMaterialRegistry.Instance.TryGetSurface(new AssetLocation("game", "block/test"), out var s0));
            Assert.Equal(surface, s0);

            Assert.True(PbrMaterialRegistry.Instance.TryGetSurface(new AssetLocation("game", "textures/block/test"), out var s1));
            Assert.Equal(surface, s1);

            // Extension-stripping is part of key normalization (registry keys are extensionless),
            // but runtime AssetLocations are typically already extensionless in VS. Validate normalization directly.
            Assert.Equal(normalizedKey, NormalizeTextureKeyForTest(new AssetLocation("game", "textures/block/test.png")));
            Assert.Equal(normalizedKey, NormalizeTextureKeyForTest(new AssetLocation("game", "textures/block/test.dds")));
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }

    private static AssetLocation NormalizeTextureKeyForTest(AssetLocation input)
    {
        var mi = typeof(PbrMaterialRegistry).GetMethod(
            "NormalizeTextureLocation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(mi);

        object? result = mi!.Invoke(null, [input]);
        Assert.IsType<AssetLocation>(result);
        return (AssetLocation)result!;
    }
}
