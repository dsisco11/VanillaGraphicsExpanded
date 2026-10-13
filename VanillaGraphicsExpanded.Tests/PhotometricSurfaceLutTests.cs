using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks material emission admission before compact identifiers become visible.</summary>
[Collection("NearFieldMaterialCapture")]
public sealed class PhotometricSurfaceLutTests
{
    #region Public API
    /// <summary>Emission carries the authored nit reference and persistent multiplier exactly once.</summary>
    [Fact]
    public void PackedEmissionUsesPersistentPhysicalUnits()
    {
        using var material = new ScopedPbrMaterialFixture();
        material.SetReadiness(true, true, emissive: 3);
        var registry = new LumonScenePbrSurfaceLutRegistry();
        ushort id = registry.GetOrAssignSurfaceId(new AssetLocation("vgetest", "textures/block/local_capture_readiness"));
        var data = new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4];
        Assert.True(registry.TryCopyAndClearDirtyLut(data));
        float stored = (float)BitConverter.UInt16BitsToHalf((ushort)(data[id * 4 + 3] >> 16));
        Assert.Equal(3 * LightingCalibration.EmissionReferenceNits * LightingStorage.PersistentScale, stored);
        Assert.InRange(stored / LightingStorage.PersistentScale, 0, registry.MaximumEmissionNits);
        Assert.Equal(stored / LightingStorage.PersistentScale, registry.MaximumEmissionNits);
        registry.Reset();
        Assert.Equal(0, registry.MaximumEmissionNits);
    }

    /// <summary>Unsupported emission leaves the previous payload and identifier allocation untouched.</summary>
    [Theory]
    [InlineData(40000f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void RejectedEmissionDoesNotPublishAnIdentifier(float emission)
    {
        using var material = new ScopedPbrMaterialFixture();
        using var retainedMaterial = new ScopedPbrMaterialFixture("textures/block/retained_emission");
        retainedMaterial.SetReadiness(true, true, emissive: 1.001f);
        var registry = new LumonScenePbrSurfaceLutRegistry();
        registry.GetOrAssignSurfaceId(new AssetLocation("vgetest", "textures/block/retained_emission"));
        float retainedMaximum = registry.MaximumEmissionNits;
        var data = new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4];
        Assert.True(registry.TryCopyAndClearDirtyLut(data));
        float retainedEmission = (float)BitConverter.UInt16BitsToHalf((ushort)(data[7] >> 16)) / LightingStorage.PersistentScale;
        Assert.Equal(retainedEmission, retainedMaximum);
        material.SetReadiness(true, true, emissive: emission);
        var key = new AssetLocation("vgetest", "textures/block/local_capture_readiness");
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.GetOrAssignSurfaceId(key));
        Assert.False(registry.TryCopyAndClearDirtyLut(data));
        Assert.Equal(retainedMaximum, registry.MaximumEmissionNits);
        material.SetReadiness(true, true, emissive: 1);
        Assert.Equal((ushort)2, registry.GetOrAssignSurfaceId(key));
    }
    #endregion
}
