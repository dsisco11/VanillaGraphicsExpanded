using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Protects both surface-cache origin publishers against animated engine camera translations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SurfaceCacheWorldOriginTests : RenderTestBase
{
    /// <summary>Uses the shared rendering context for the terrain uniform buffer.</summary>
    public SurfaceCacheWorldOriginTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Origin publication
    /// <summary>Both production publishers use the engine terrain camera origin independently of matrix translation.</summary>
    [Theory]
    [InlineData(-32.375, -2, 31.625)]
    [InlineData(16777216.25, 524288, .25)]
    [InlineData(-16777216.25, -524289, 31.75)]
    public void PublishersUseCameraOriginIndependentlyOfAnimatedMatrix(double origin, int chunk, double remainder)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        LumOnCameraState camera = new(origin, origin, origin, origin, origin, origin, 0);
        float[] matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var render = RuntimeRenderEvents.Adapt<IRenderAPI>((method, _) => method.Name == "get_CameraMatrixOriginf"
            ? matrix : throw new NotSupportedException(method.Name));
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Event" => events.Api,
            "get_Render" => render,
            _ => method.Invoke(assets.Api, args)
        });
        var config = new VgeConfig();
        config.LumOn.Enabled = config.LumOn.LumonScene.Enabled = true;
        var partitions = new WorldPartitionModSystem();
        try
        {
            using var buffers = new GBufferManager(api);
            using var terrain = new LumOnTerrainBridgeUpdateRenderer(api, config, () => camera);
            using var feedback = new LumonSceneFeedbackUpdateRenderer(api, config, buffers, partitions.GetCoordinator(), () => camera);
            MethodInfo publishFeedback = typeof(LumonSceneFeedbackUpdateRenderer).GetMethod(
                "UpdateWorldCoordUniformState", BindingFlags.Instance | BindingFlags.NonPublic)!;

            // Keep the engine terrain origin fixed while entity position and view-matrix bob change.
            foreach (float bob in new[] { -.3f, 0f, .4f })
            {
                camera = camera with { PositionX = origin + bob, PositionY = origin - 1.6 + bob, PositionZ = origin - bob };
                matrix[12] = bob * 2;
                matrix[13] = -1.4f + bob;
                matrix[14] = -bob;
                LumonSceneWorldCoordUniformState.Disable();
                events.Render(EnumRenderStage.Opaque);
                AssertOrigin(chunk, remainder);
                using (var mapped = LumOnTerrainBridgeUboState.UboOrNull!.MapRange<int>(0, 8, MapBufferAccessMask.MapReadBit))
                {
                    Assert.True(mapped.IsMapped);
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Assert.Equal(chunk, mapped.Span[axis]);
                        Assert.Equal((float)remainder, BitConverter.Int32BitsToSingle(mapped.Span[4 + axis]));
                    }
                }

                // Isolate the feedback publisher from unrelated residency and geometry allocation requirements.
                LumonSceneWorldCoordUniformState.Disable();
                publishFeedback.Invoke(feedback, null);
                AssertOrigin(chunk, remainder);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            partitions.Dispose();
        }
    }

    /// <summary>Checks explicit signed chunk decomposition without reusing the production computation.</summary>
    private static void AssertOrigin(int chunk, double remainder)
    {
        Assert.Equal(new VectorInt3(chunk, chunk, chunk), LumonSceneWorldCoordUniformState.WorldChunkCoordOffset);
        var actual = LumonSceneWorldCoordUniformState.WorldBlockOffsetRem;
        Assert.Equal(remainder, actual.X);
        Assert.Equal(remainder, actual.Y);
        Assert.Equal(remainder, actual.Z);
    }
    #endregion
}
