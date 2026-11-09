using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks the camera-ray geometry diagnostic against actual published voxel and readiness textures.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnNearFieldGeometryDebugFunctionalTests : LumOnShaderFunctionalTestBase
{


    #region Scenario resources
    // The base fixture owns disposal; each test keeps its own variants and cleared output target.
    private LumOnDebugShaderProgram? geometryProgram;
    private GpuFramebuffer? geometryOutput;
    #endregion

    #region Public API
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
        // Arrange: Build the selected published state; each case varies geometry, material readiness, or scene admission.
        using var fixture = new NearFieldVoxelFixture();
        var world = Plane(0, partial: scenario == "unsupported");
        if (scenario == "unsupported") fixture.PublishCaptured(world);
        else if (scenario == "empty") fixture.Publish(new ControlledVoxelWorld());
        else if (scenario is "solid" or "material_missing" or "outside_facing") fixture.Publish(world, materialIdentity: scenario == "material_missing" ? 0u : 1u);

        // Act: Draw the production geometry view with the matching scene and camera condition.
        using var result = RenderGeometry(scenario == "disabled" ? null : fixture.Scene,
            camera: scenario == "outside" ? new Vector3(1000, 0, 0) : scenario == "outside_facing" ? new Vector3(0.5f, 0.5f, 40) : null);

        // Assert: Compare the readback to the authored state color and require a fully written alpha channel.
            if (scenario is "solid" or "outside_facing")
            {
                ImageAssertions.Predicates.Predicate(result.Texture, SurfacePredicates.LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates).AssertEqual();
            }
            else if (scenario == "material_missing")
            {
                ImageAssertions.Predicates.Predicate(result.Texture, SurfacePredicates.LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates_2).AssertEqual();
            }
            else
            {
                float[] expected = scenario switch
                {
                    "unsupported" => [1, 0, 1], "unpublished" => [0.5f, 0, 1],
                    "disabled" => [0.1f, 0.2f, 0.8f], "outside" => [0, 0.2f, 0.8f], _ => [0, 0, 0]
                };
                ImageAssertions.Predicates.Predicate(result.Texture, SurfacePredicates.LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_DistinguishesPublishedStates_3,
                    parameters:new(expected[0],expected[1],expected[2],0)).AssertEqual();
            }
        ImageAssertions.Comparisons.CompareConstant(result.Texture,1,channelMask:8).AssertEqual();
    }

    /// <summary>Camera translation changes which real cell is visible while large integer origins preserve that result.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(16777216)]
    [InlineData(-16777216)]
    public void GeometryView_CameraMovesAcrossVoxelBoundary_AtLargeOrigins(int offset)
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish a wall with a doorway at each tested integer origin.
        var world = Plane(offset);
        // Create a full-height doorway on the camera's positive X side.
        for (int y = -8; y <= 8; y++)
        for (int x = 2; x <= 8; x++) world.SetBlock(offset + x, y, -5, null);
        using var fixture = new NearFieldVoxelFixture(new VectorInt3(offset, 0, 0));
        fixture.Publish(world);

        // Act: Render blocked, clear, and split-origin camera positions through the real geometry shader.
        using var blocked = RenderGeometry(fixture.Scene, camera: new Vector3(0.5f, 0.5f, 0), playerOrigin: new Vector3d(offset, 0, 0));
        using var clear = RenderGeometry(fixture.Scene, camera: new Vector3(4.5f, 0.5f, 0), playerOrigin: new Vector3d(offset, 0, 0));
        // A different fractional player-origin split must reconstruct the identical absolute camera.
        using var splitOrigin = RenderGeometry(fixture.Scene, camera: new Vector3(0.25f, 1.25f, -0.125f),
            playerOrigin: new Vector3d(offset + 0.25, -0.75, 0.125));

        // Assert: The equivalent split origin matches, the blocked view hits the wall, and the doorway is clear.
        ImageAssertions.Comparisons.CompareHistory(splitOrigin.Texture,blocked).AssertEqual();
        ImageAssertions.Predicates.Predicate(blocked.Texture, SurfacePredicates.LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_CameraMovesAcrossVoxelBoundary_AtLargeOrigins).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(clear.Texture,0,channelMask:7).AssertEqual();
    }
    #endregion

    #region Entrypoint Parity
    /// <summary>Camera motion across a publication interval preserves occupied surfaces and world-zero boundary lines.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(16777216)]
    [InlineData(-16777216)]
    public void GeometryView_PublicationIntervalCameraSweep(int offset)
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish occupied surface cells spanning more than one 16-block region at this integer origin.
        var world = new ControlledVoxelWorld();
        var block = new Block { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        for (int x = -2; x <= 20; x++)
        for (int y = -2; y <= 2; y++) world.SetBlock(offset + x, y, -5, block);
        using var fixture = new NearFieldVoxelFixture(new VectorInt3(offset, 0, 0));
        fixture.Publish(world);
        bool sawPublicationBoundary = false;

        // Act and assert: Sweep 65 camera positions across the interval; each readback must preserve the surface,
        // and at least one must expose a line on the 16-block publication grid.
        for (int step = 0; step <= 64; step++)
        {
            using var pixels = RenderGeometry(fixture.Scene, camera: new Vector3(.5f + step * .25f,
                .5f + .1f * MathF.Sin(step), 0), playerOrigin: new Vector3d(offset, 0, 0));
            var proof=ImageAssertions.Predicates.Predicate(pixels.Texture, SurfacePredicates.LumOnNearFieldGeometryDebugFunctionalTests_GeometryView_PublicationIntervalCameraSweep);
            proof.AssertEqual();sawPublicationBoundary|=proof.MatchingTexels>0;
        }
        Assert.True(sawPublicationBoundary, "The viewer did not expose the 16-block publication grid.");
    }

    /// <summary>The fixed geometry executable ignores the retired runtime mode selector.</summary>
    [Fact]
    public void GeometryView_IgnoresLegacyRuntimeSelector()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish one plane for the fixed near-field geometry program.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(Plane(0));

        // Act: Draw once with the default selector and once with the retired selector value.
        using var original=RenderGeometry(fixture.Scene);
        using var selected=RenderGeometry(fixture.Scene,legacySelector:22);

        // Assert: The executable's output is identical because geometry mode is fixed in the program identity.
        ImageAssertions.Comparisons.CompareHistory(selected.Texture,original).AssertEqual();
    }
    #endregion

    #endregion

    #region Private
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
    private GpuImageSnapshot RenderGeometry(ControlledTraceGpuScene? scene, Vector3? camera = null, Vector3d? playerOrigin = null, int legacySelector = 70)
    {
        var program = geometryProgram;
        if (program is null)
        {
            program = Programs.Create<LumOnDebugShaderProgram>(shader =>
            {
                shader.DirectVisibility = true;
            }, identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.NearFieldGeometry));
            geometryProgram = program;
        }
        {
            // Perspective rays face -Z; a narrow frustum isolates the chosen voxel column.
            float[] inverseProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            inverseProjection[0] *= 0.01f; inverseProjection[5] *= 0.01f;
            var position = camera ?? new Vector3(0.5f, 0.5f, 0);
            float[] inverseView = [1,0,0,0, 0,1,0,0, 0,0,1,0, position.X,position.Y,position.Z,1];
            var origin = playerOrigin ?? new Vector3d();
            var bridge = FrameWorldSpaceBridge.Compute(origin.X, origin.Y, origin.Z);
            UpdateAndBindLumOnFrameUbo(program, invProjectionMatrix: inverseProjection, invViewMatrix: inverseView,
                matrixSpaceWorldChunkCoordOffset: bridge.ChunkOffset, matrixSpaceWorldBlockOffsetRem: bridge.BlockOffsetRemainder);
            program.DebugMode = legacySelector;
            program.NearFieldVisibility.Stage(program, scene?.Backend);
            var output = geometryOutput ??= TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f);
            // RenderQuadTo clears every attachment before drawing, so reuse cannot retain old pixels.
            TestFramework.RenderQuadTo(program, output);
            return new(output[0]);
        }
    }
    #endregion
    #region Observation Setup
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
    #endregion
}
