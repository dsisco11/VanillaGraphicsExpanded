using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the camera-ray geometry diagnostic against actual published voxel and readiness textures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnNearFieldGeometryDebugFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Construction
    /// <summary>Uses the shared mandatory OpenGL context.</summary>
    public LumOnNearFieldGeometryDebugFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Scene Classification
    /// <summary>Distinct actual geometry/readiness states produce distinct diagnostic colors without lighting inputs.</summary>
    [Theory]
    [InlineData("solid")]
    [InlineData("outside_facing")]
    [InlineData("material_missing")]
    [InlineData("unsupported")]
    [InlineData("unpublished")]
    [InlineData("empty")]
    [InlineData("disabled")]
    [InlineData("outside")]
    public void GeometryView_DistinguishesPublishedStates(string scenario)
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        var world = Plane(0, partial: scenario == "unsupported");
        if (scenario == "unsupported") fixture.PublishCaptured(world);
        else if (scenario == "empty") fixture.Publish(new ControlledVoxelWorld());
        else if (scenario is "solid" or "material_missing" or "outside_facing") fixture.Publish(world, materialIdentity: scenario == "material_missing" ? 0u : 1u);
        var result = RenderGeometry(scenario == "disabled" ? null : fixture.Scene,
            camera: scenario == "outside" ? new Vector3(1000, 0, 0) : scenario == "outside_facing" ? new Vector3(0.5f, 0.5f, 40) : null);
        for (int i = 0; i < result.Length; i += 4)
        {
            if (scenario is "solid" or "outside_facing")
            {
                Assert.True(result[i + 2] > 0.1f);
                Assert.InRange(result[i] / result[i + 2], 0.098f, 0.102f);
                Assert.InRange(result[i + 1] / result[i + 2], 0.848f, 0.852f);
            }
            else if (scenario == "material_missing")
            {
                Assert.True(result[i] > 0.1f);
                Assert.InRange(result[i + 1] / result[i], 0.448f, 0.452f);
                Assert.Equal(0f, result[i + 2]);
            }
            else
            {
                float[] expected = scenario switch
                {
                    "unsupported" => [1, 0, 1], "unpublished" => [0.5f, 0, 1],
                    "disabled" => [0.1f, 0.2f, 0.8f], _ => [0, 0, 0]
                };
                for (int c = 0; c < 3; c++) Assert.InRange(result[i + c], expected[c] - 0.001f, expected[c] + 0.001f);
            }
            Assert.Equal(1f, result[i + 3]);
        }
    }

    /// <summary>Camera translation changes which real cell is visible while large integer origins preserve that result.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(16777216)]
    [InlineData(-16777216)]
    public void GeometryView_CameraMovesAcrossVoxelBoundary_AtLargeOrigins(int offset)
    {
        EnsureShaderTestAvailable();
        var world = Plane(offset);
        // Create a full-height doorway on the camera's positive X side.
        for (int y = -8; y <= 8; y++)
        for (int x = 2; x <= 8; x++) world.SetBlock(offset + x, y, -5, null);
        using var fixture = new NearFieldVoxelFixture(new VectorInt3(offset, 0, 0));
        fixture.Publish(world);
        var blocked = RenderGeometry(fixture.Scene, camera: new Vector3(0.5f, 0.5f, 0), playerOrigin: new Vector3d(offset, 0, 0));
        var clear = RenderGeometry(fixture.Scene, camera: new Vector3(4.5f, 0.5f, 0), playerOrigin: new Vector3d(offset, 0, 0));
        // A different fractional player-origin split must reconstruct the identical absolute camera.
        var splitOrigin = RenderGeometry(fixture.Scene, camera: new Vector3(0.25f, 1.25f, -0.125f),
            playerOrigin: new Vector3d(offset + 0.25, -0.75, 0.125));
        Assert.Equal(blocked, splitOrigin);
        for (int i = 0; i < blocked.Length; i += 4)
        {
            Assert.True(blocked[i + 2] > 0.1f);
            Assert.Equal(0f, clear[i]); Assert.Equal(0f, clear[i + 1]); Assert.Equal(0f, clear[i + 2]);
        }
    }
    #endregion

    #region Entrypoint Parity
    /// <summary>The monolithic debug shader dispatches mode seventy identically to the dedicated family shader.</summary>
    [Fact]
    public void GeometryView_MonolithicEntrypointMatchesDedicatedShader()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(Plane(0));
        Assert.Equal(RenderGeometry(fixture.Scene), RenderGeometry(fixture.Scene, monolithic: true));
    }
    #endregion

    #region Scene And Shader Harness
    /// <summary>Builds a broad finite wall perpendicular to the camera, optionally using actual unsupported partial blocks.</summary>
    private static ControlledVoxelWorld Plane(int offset, bool partial = false)
    {
        var world = new ControlledVoxelWorld();
        var block = new Block { BlockId = 1, CollisionBoxes = partial ? [new Cuboidf(0, 0, 0, 1, 0.5f, 1)] : Block.DefaultCollisionSelectionBoxes };
        for (int y = -8; y <= 8; y++)
        for (int x = -8; x <= 8; x++) world.SetBlock(offset + x, y, -5, block);
        return world;
    }

    /// <summary>Renders only production geometry/readiness inputs with a narrow camera frustum and precise world origin.</summary>
    private float[] RenderGeometry(NearFieldGpuScene? scene, Vector3? camera = null, Vector3d? playerOrigin = null, bool monolithic = false)
    {
        int program = CompileShaderWithDefines("lumon_debug.vsh", monolithic ? "lumon_debug.fsh" : "lumon_debug_worldprobe.fsh", new()
        {
            ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "1",
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = "0"
        });
        try
        {
            // Perspective rays face -Z; a narrow frustum isolates the chosen voxel column.
            float[] inverseProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            inverseProjection[0] *= 0.01f; inverseProjection[5] *= 0.01f;
            var position = camera ?? new Vector3(0.5f, 0.5f, 0);
            float[] inverseView = [1,0,0,0, 0,1,0,0, 0,0,1,0, position.X,position.Y,position.Z,1];
            var origin = playerOrigin ?? new Vector3d();
            var bridge = LumOnFrameWorldSpaceBridge.Compute(origin.X, origin.Y, origin.Z);
            UpdateAndBindLumOnFrameUbo(program, invProjectionMatrix: inverseProjection, invViewMatrix: inverseView,
                matrixSpaceWorldChunkCoordOffset: bridge.ChunkOffset, matrixSpaceWorldBlockOffsetRem: bridge.BlockOffsetRemainder);
            using var parameters = new ObjectParamsUbo("Tests.NearFieldGeometryDebug");
            parameters.UploadAndBind(new LumOnDebugParamsUbo { DebugMode = 70 }.Bytes);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnDebugParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            using var localBuffer = GpuUniformBuffer.Create(debugName: "Tests.NearFieldGeometryDebug.Scene");
            var local = new LumOnNearFieldParamsUbo();
            local.Set(scene?.Origin ?? default, scene?.Resolution ?? 0, cellSize: scene?.CellSize ?? 16);
            localBuffer.UploadOrResize(local.Bytes, growExponentially: false);
            localBuffer.BindBase(LumOnNearFieldParamsUbo.Binding);
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnNearFieldParamsUbo.BlockName, LumOnNearFieldParamsUbo.Binding);
            scene?.Geometry.Bind(34); scene?.Regions.Bind(35);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "nearFieldGeometry"), 34);
            GL.Uniform1(GL.GetUniformLocation(program, "nearFieldRegions"), 35);
            GL.UseProgram(0);
            using var output = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);
            TestFramework.RenderQuadTo(program, output);
            return output[0].ReadPixels();
        }
        finally { GL.DeleteProgram(program); }
    }
    #endregion
}
