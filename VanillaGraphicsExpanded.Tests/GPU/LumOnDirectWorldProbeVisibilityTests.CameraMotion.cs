using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that camera motion cannot move stationary geometry during direct irradiance visibility.</summary>
public sealed partial class LumOnDirectWorldProbeVisibilityTests
{
    #region Camera Motion
    /// <summary>Renders fixed clear and blocked segments while bob moves the view transform across a voxel boundary.</summary>
    [Theory]
    [InlineData(31, 0)]
    [InlineData(-1, 0)]
    [InlineData(-2, 0)]
    [InlineData(31, 16777216)]
    [InlineData(-1, 16777216)]
    [InlineData(-2, 16777216)]
    [InlineData(31, -16777216)]
    [InlineData(-1, -16777216)]
    [InlineData(-2, -16777216)]
    public void CameraBob_PreservesStationaryClearAndBlockedSegments(int consumer, int offset)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        for (int x = -8; x <= 8; x++)
        for (int z = -8; z <= 8; z++)
            world.SetBlock(offset + x, offset, offset + z, new Block { BlockId = 1 });
        using var local = new NearFieldVoxelFixture(new VectorInt3(offset, offset, offset));
        local.Publish(world);
        // Keep fractions in double precision until subtracting the stable player origin.
        var playerOrigin = new Vector3d(offset + 0.25, offset + 0.375, offset + 0.625);
        var atlas = CreateUniformCache();
        foreach (bool clear in new[] { true, false })
        {
            float height = clear ? 1.125f : 0.875f;
            var probeRelative = new Vector3(0.25f, height - 0.375f, -0.125f);
            var receiverRelative = new Vector3(0.25f, height - 0.375f, -5.625f);
            foreach (float bob in new[] { 0f, 0.25f, -0.25f, 0f })
            {
                var pixels = RenderDirectVisibility(atlas, local.Scene, receiverRelative,
                    probeRelative - new Vector3(8), 16, consumer, size: 1, span: 0,
                    playerOrigin: playerOrigin, cameraBob: bob);
                AssertLighting(pixels, consumer, clear);
            }
        }
    }
    #endregion
}
