using System.Reflection;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Checks native HZB dispatch state and every published hierarchy mip.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnTemporalRendererTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared material-isolated graphics context.</summary>
    public LumOnTemporalRendererTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>The actual HZB consumer routes every mip and restores hostile engine state on repeated submissions.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void RendererHzbSubmissionBuildsEveryMipUnderHostileState()
    {
        // Arrange: warm the registered runtime and capture the HZB owner before hostile state changes.
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false, new SpatialLightingScene());
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        var renderer = (DepthHierarchyRenderer)Assert.Single(runtime.Cache.Events.Registrations.Select(item => item.Renderer).Distinct(),
            item => item is DepthHierarchyRenderer);
        var hzb = DepthHierarchyRenderer.Texture!;
        Assert.True(hzb.MipLevels > 1);
        foreach (float depth in new[] { .375f, .625f })
        {
            runtime.Cache.Terrain.Depth.UploadDataImmediate(Enumerable.Repeat(depth, 16).ToArray());
            using var hostile = new HostileFullscreenState();
            // Act: the pre-render callback must defer HZB work; the opaque callback performs it under hostile state.
            renderer.OnRenderFrame(.016f, Vintagestory.API.Client.EnumRenderStage.Before);
            Assert.Null(DepthHierarchyRenderer.Texture);
            renderer.OnRenderFrame(.016f, Vintagestory.API.Client.EnumRenderStage.Opaque);
            // Assert: native state is restored and the same HZB publishes every mip at the submitted depth.
            hostile.AssertRestored();
            Assert.Same(hzb,DepthHierarchyRenderer.Texture);
            for (int mip = 0; mip < hzb.MipLevels; mip++)
                ImageAssertions.Statistics.Inspect(hzb, depth - .0001f, depth + .0001f, Assertions.GpuImageSnapshot.Mip(hzb, mip)).AssertFiniteRange();
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
    }

    #endregion

}
