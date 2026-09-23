using System.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks reconstructed surface receivers without relaxing intervening wall occlusion.</summary>
public sealed partial class LumOnDirectWorldProbeVisibilityTests
{
    #region Surface endpoints
    /// <summary>Sub-voxel depth error must not turn a coplanar visibility segment into an immediate wall hit.</summary>
    [Theory]
    [InlineData(-1, .00001f, true)]
    [InlineData(-2, .00001f, true)]
    [InlineData(-1, .002f, false)]
    [InlineData(-2, .002f, false)]
    public void ReconstructedSurfaceEndpointUsesBoundedNormalOffset(int consumer, float inwardError, bool lit)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        // The probe lies exactly on the empty side of the wall's z=-5 face.
        for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
                world.SetBlock(x, y, -6, new Vintagestory.API.Common.Block
                {
                    BlockId = 1,
                    CollisionBoxes = Vintagestory.API.Common.Block.DefaultCollisionSelectionBoxes
                });
        using var local = new NearFieldVoxelFixture();
        local.Publish(world);
        var result = RenderDirectVisibility(CreateUniformCache(), local.Scene,
            new Vector3(.5f, .5f, -5 - inwardError), new Vector3(-7.5f, -7.5f, -13), 16,
            consumer, receiverNormal: Vector3.UnitZ);
        AssertLighting(result, consumer, lit);
    }
    #endregion
}
