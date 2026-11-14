using System.Numerics;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Verifies native light admission and attachment updates reach the immutable shadow source snapshot.</summary>
public sealed class NativeShadowLightSourceCaptureTests
{
    #region Public API
    /// <summary>Moves an admitted carried light after admission and preserves its original world coordinate through publication.</summary>
    [Fact]
    public void AdmittedCarriedLightTransfersUpdatedWorldPositionIntoLightSnapshot()
    {
        // Arrange: provide the real client player accessor and source table used by native collector hooks.
        var camera = new LumOnCameraState(1_000_000_000.25, -2_000_000_000.5, 1_500_000_000.125,
            1_000_000_000.5, -2_000_000_000.25, 1_500_000_000.375, 0);
        var world = new RuntimeCameraWorld(RuntimeEngineServices.CameraPlayer(() => camera));
        ClientMain game = world.World;
        game.shUniforms = new DefaultShaderUniforms { PointLightsCount = 1 };
        var collector = NativeShadowLightSources.For(game);
        collector.Begin(game);
        var owner = collector.Entity(game.EntityPlayer, slot: 1, item: new ItemStack(new Item()));
        var admittedPosition = new Vector3d(1_000_000_032.25, -2_000_000_064.5, 1_500_000_016.125);
        var animatedPosition = new Vector3d(1_000_000_033.125, -2_000_000_062.25, 1_500_000_019.5);

        // Act: record the successfully admitted hand slot, then replace its position with the final animated attachment.
        collector.Admit(game, before: 0, source: owner, worldPosition: admittedPosition);
        collector.UpdatePosition(0, animatedPosition);
        var native = collector.Capture(1);
        using var lights = new VgeLightsUniformBuffer();
        lights.Capture(1, [12.5f, -7.25f, 3.5f], [1, 2, 3], native.Keys.Span, native.Positions.Span);

        // Assert: native identity and final absolute position survive independently from the shader's view coordinates.
        var point = Assert.Single(lights.Lights);
        Assert.True(native.Complete);
        Assert.Equal(0, native.Dimension);
        Assert.Equal(owner, native.Keys.Span[0]);
        Assert.NotEqual(admittedPosition, native.Positions.Span[0]!.Value);
        Assert.Equal(animatedPosition, native.Positions.Span[0]!.Value);
        Assert.Equal(animatedPosition, point.WorldPosition!.Value);
        Assert.Equal(new Vector3(12.5f, -7.25f, 3.5f), point.ViewPosition);
        Assert.Equal(ShadowSourceKind.Hand, point.Source.Kind);
        Assert.NotEqual(point.ViewPosition.X, (float)animatedPosition.X);
    }
    #endregion
}
