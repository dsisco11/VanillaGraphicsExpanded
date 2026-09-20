using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Feeds voxel-derived tiles through the real world sampling, screen history, gather and debug shaders.</summary>
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests
{
    #region Controlled Room Reproduction
    /// <summary>Characterizes the current across-wall interpolation defect alongside dark controls.</summary>
    [Theory]
    [InlineData(0f, 0.25f, 0f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(1f, 0.25f, 0.125f)]
    public void SealedRoom_VoxelDerivedAtlas_CharacterizesExteriorInterpolation(float exteriorLight, float anchorOffset, float expectedRadiance)
    {
        EnsureShaderTestAvailable();
        var world = WorldProbeRoomScenario.Create(exteriorBlockLight: exteriorLight);
        var atlas = new WorldProbeAtlasData(2, WorldProbeRoomScenario.TileSize);
        // The interior centers are x=.5; the exterior centers are x=2.5. A solid
        // wall occupies [1,2). Sampling x=.75 is still indoors but weights outside
        // probes by (.75-.5)/2 = .125 because current interpolation ignores walls.
        for (int z = 0; z < 2; z++)
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
        {
            var result = WorldProbeRoomScenario.Trace(world, new Vector3d(0.5 + 2 * x, 0.5 + 2 * y, 0.5 + 2 * z), x, y, z);
            Assert.True(result.Success);
            Assert.Equal(256, result.AtlasSamples.Length);
            Assert.All(result.AtlasSamples, sample =>
            {
                Assert.True(sample.AlphaEncodedDistSigned > 0);
                float expected = x == 0 ? 0 : exteriorLight;
                Assert.InRange(sample.RadianceRgb.X, expected - 1e-6f, expected + 1e-6f);
                Assert.InRange(sample.RadianceRgb.Y, expected - 1e-6f, expected + 1e-6f);
                Assert.InRange(sample.RadianceRgb.Z, expected - 1e-6f, expected + 1e-6f);
            });
            atlas.SetProbe(result);
        }
        RunWorldProbeTraceScenario(atlas, spacing: 2, anchorOffsetX: anchorOffset, expectedRadiance: expectedRadiance);
    }
    #endregion
}
