using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using Vintagestory.API.MathTools;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Checks that the production direct-lighting component consumes a current VSM page publication.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrDirectLightingShadowTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared headless graphics context for production shader and RHI submissions.</summary>
    public PbrDirectLightingShadowTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>A completed current page suppresses sunlight while the same page with far depth admits it.</summary>
    [Fact]
    public void CurrentVsmPageControlsDirectSunlight()
    {
        // Arrange: Render the same receiver twice with independently published pages and the same current sun.
        EnsureShaderTestAvailable();
        double lit = RenderReceiver(1f);
        double occluded = RenderReceiver(0f);

        // Assert: GPU reductions show visible diffuse sunlight in the lit page and no diffuse energy behind depth zero.
        Assert.True(lit > 0.01, $"Expected a lit direct-light contribution, got {lit:R}.");
        Assert.InRange(occluded, 0d, 0.0001d);
    }

    /// <summary>The surface shader keeps a retained page usable when large signed render origins cross chunk boundaries.</summary>
    [Fact]
    public void RetainedSurfaceProjectionSurvivesLargeOriginChunkRebase()
    {
        // Arrange: retain one rendered page around the same absolute receiver as the camera origin moves by whole chunks.
        EnsureShaderTestAvailable();
        var initialOrigin = new Vector3d(1_000_000_031.75, -2_000_000_000.5, 1_500_000_031.75);
        var rebasedOrigin = initialOrigin + new Vector3d(32, -32, 32);
        var initialReceiver = new Vector3d(64, 64, -1);
        var rebasedReceiver = initialReceiver - new Vector3d(32, -32, 32);
        var clearColumnReceiver = new Vector3d(4, 0, 0);
        using var shadows = new PublishedSurfaceShadowFixture(0f, initialOrigin, initialReceiver, shadowedColumns: (94, 96));
        using var rebasedInputs = shadows.CreateRebasedInputs(rebasedOrigin);

        // Act: render a dark texel stripe and an adjacent clear column before and after rebasing the camera/chunk origin.
        double initial = RenderReceiver(shadows.Inputs, initialOrigin, initialReceiver);
        double rebased = RenderReceiver(rebasedInputs, rebasedOrigin, rebasedReceiver);
        double initialLit = RenderReceiver(shadows.Inputs, initialOrigin, initialReceiver + clearColumnReceiver);
        double rebasedLit = RenderReceiver(rebasedInputs, rebasedOrigin, rebasedReceiver + clearColumnReceiver);

        // Assert: both retained coordinates agree with the patterned page, and the same lights remain enabled after rebasing.
        Assert.InRange(initial, 0d, 0.0001d);
        Assert.InRange(rebased, 0d, 0.0001d);
        Assert.True(initialLit > 0.01, $"Expected the adjacent clear column to admit sunlight before rebasing, got {initialLit:R}.");
        Assert.True(rebasedLit > 0.01, $"Expected the adjacent clear column to admit sunlight after rebasing, got {rebasedLit:R}.");
    }

    /// <summary>Demand padding keeps a distant receiver inside its actual GPU-published coverage certificate.</summary>
    [Fact]
    public void DemandCoverageContainsDistantReceiverForSurfaceSampling()
    {
        // Arrange: generate a tight page certificate from the exact distant receiver bounds.
        EnsureShaderTestAvailable();
        var receiver = new Vector3d(1000, 64, -1);
        using var darkShadows = new PublishedSurfaceShadowFixture(0f, receiverRelative: receiver, useDemandCoverage: true);
        using var litShadows = new PublishedSurfaceShadowFixture(1f, receiverRelative: receiver, useDemandCoverage: true);

        // Act: shade the exact same receiver against separately published dark and far-depth demand pages.
        double occluded = RenderReceiver(darkShadows.Inputs, Vector3d.Zero, receiver);
        double lit = RenderReceiver(litShadows.Inputs, Vector3d.Zero, receiver);

        // Assert: the demand certificate admits the exact distant receiver and the shader still compares its depth.
        Assert.InRange(occluded, 0d, 0.0001d);
        Assert.True(lit > 0.01, $"Expected far depth in the published page to admit sunlight, got {lit:R}.");
    }
    #endregion

    #region Private
    /// <summary>Prepares the GPU statistics reducer used to observe direct-light output.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }

    /// <summary>Consumes one real page/readiness publication and observes direct diffuse output on the GPU.</summary>
    private double RenderReceiver(float pageDepth)
    {
        using var shadowPublication = new PublishedSurfaceShadowFixture(pageDepth);
        return RenderReceiver(shadowPublication.Inputs, Vector3d.Zero, new Vector3d(64, 64, -1));
    }

    /// <summary>Draws one deferred receiver whose camera-relative position and render origin are independently controlled.</summary>
    private double RenderReceiver(VanillaGraphicsExpanded.PBR.Shadows.SurfaceShadowInputSet shadows,
        Vector3d renderOrigin, Vector3d receiverRelative)
    {
        // The fixture keeps the receiver and its PCF footprint inside the certified page region.
        using var output = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f, 3);
        using var albedo = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [0.6f, 0.6f, 0.6f, 1f]);
        using var depth = TestFramework.CreateTexture(1, 1, TextureFormat.R32f, [0f]);
        using var normal = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [0.5f, 0.5f, 1f, 1f]);
        using var material = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [0.4f, 0f, 0f, 0f]);
        using var environment = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [0f, 0f, 0f, 1f]);
        using var position = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, new float[4]);
        var program = Programs.Create<PBRDirectLightingShaderProgram>();
        using var layered = LayeredTestTexture.Create(ImageAssertions.Programs, normal, material, environment);
        program.PrimaryScene = albedo;
        program.PrimaryDepth = depth;
        program.GBufferSurface = layered;
        program.GBufferPosition = position;
        program.ShadowInputs = shadows;
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] inverseView = (float[])identity.Clone();
        inverseView[12] = (float)receiverRelative.X;
        inverseView[13] = (float)receiverRelative.Y;
        inverseView[14] = (float)(receiverRelative.Z + 1);
        var chunkOrigin = new VectorInt3(checked((int)Math.Floor(renderOrigin.X / 32)),
            checked((int)Math.Floor(renderOrigin.Y / 32)), checked((int)Math.Floor(renderOrigin.Z / 32)));
        var originRemainder = new Vector3((float)(renderOrigin.X - chunkOrigin.X * 32.0),
            (float)(renderOrigin.Y - chunkOrigin.Y * 32.0), (float)(renderOrigin.Z - chunkOrigin.Z * 32.0));
        using var camera = TestFrameCamera.CreateFromInverseView(identity, inverseView,
            renderOriginChunkCoord: chunkOrigin, renderOriginBlockRemainder: originRemainder);
        program.FrameInputs = camera;
        using var sharedLights = new VgeLightsUniformBuffer();
        sharedLights.Capture(0, [], []);
        program.LightsInputs = sharedLights;
        FramebufferTestSetup.BindForEngineRendering(output);
        TestFramework.RenderQuadTo(program, output);

        // Reduce the rendered attachment on GPU and retain only its compact scalar sum for the CPU assertion.
        var result = ImageAssertions.Statistics.Statistics(output[0], channelMask: 1);
        return result.Sum(0);
    }
    #endregion
}
