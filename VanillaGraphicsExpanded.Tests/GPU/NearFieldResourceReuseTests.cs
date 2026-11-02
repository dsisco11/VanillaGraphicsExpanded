using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies the migrated trace harness actually borrows and retains its supplied branch resources.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NearFieldResourceReuseTests : NearFieldShaderTestBase
{


    /// <summary>Uses the shared GPU context and real component shader owners.</summary>
    public NearFieldResourceReuseTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Trace ownership
    /// <summary>Switching branches, shader variants and suppression cannot contaminate a reused bright trace program.</summary>
    [Fact]
    public void TraceRebindsInputsAfterBranchAndVariantChanges()
    {
        EnsureShaderTestAvailable();
        using var geometry = new NearFieldVoxelFixture();
        geometry.Publish(new VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes.ControlledVoxelWorld());
        using var first = new ShaderLightingResources(Programs.Api);
        using var second = new ShaderLightingResources(Programs.Api);
        var bright = Trace(geometry, resources: first);
        var suppressed = Trace(geometry, resources: second, suppress: true);
        // Interleave a different compiled selection, then return to the original program and branch.
        var disabled = Trace(geometry, resources: second, worldCache: false);
        var restored = Trace(geometry, resources: first);
        Assert.NotSame(first.EnsureScreen(4, 4, 2).ScreenProbeAtlasTraceTex,
            second.EnsureScreen(4, 4, 2).ScreenProbeAtlasTraceTex);
        ImageAssertions.Predicates.Predicate(bright.Radiance, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, restored.Radiance).AssertEqual();
        ImageAssertions.Predicates.Predicate(bright.Meta, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, restored.Meta).AssertEqual();
        ImageAssertions.Predicates.Predicate(bright.Meta, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, suppressed.Meta).AssertEqual();
        ObserveTrace(restored, GeometryPredicates.NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges, suppressed).AssertEqual();
        ObserveTrace(disabled, GeometryPredicates.NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges_2).AssertEqual();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Repeated real draws retain buffers and do not overwrite a simultaneous branch or retire external world resources.</summary>
    [Fact]
    public void TraceReusesSuppliedBranchWithoutTakingOwnershipOfExternalInputs()
    {
        EnsureShaderTestAvailable();
        using var geometry = new NearFieldVoxelFixture();
        using var first = new ShaderLightingResources(Programs.Api);
        using var second = new ShaderLightingResources(Programs.Api);
        using var external = new ShaderLightingResources(Programs.Api);
        var world = external.EnsureWorldProbes(1, 1, 16);
        var screen = first.EnsureScreen(4, 4, 2);
        var other = second.EnsureScreen(4, 4, 2);
        other.ScreenProbeAtlasTraceTex!.UploadDataImmediate(Enumerable.Repeat(.75f, 16 * 16 * 4).ToArray());
        var output = screen.ScreenProbeAtlasTracePass!;
        var history = screen.ScreenProbeAtlasHistoryTex!;
        var radiance = output.Colors[0].Image!.Value.Texture;
        first.Scene.EnsureSize(4, 4);
        var depth = first.Scene.Engine.Depth;
        TraceImages? prior = null;
        for (int frame = 0; frame < 2; frame++)
        {
            var result = Trace(geometry, nearFieldTracing: false, worldCache: false,
                worldResources: world, resources: first,
                consume: traced => Assert.Same(output, traced));
            Assert.Same(screen, first.EnsureScreen(4, 4, 2));
            Assert.Equal(radiance, (GpuTextureRef)screen.ScreenProbeAtlasTraceTex);
            Assert.Same(history, screen.ScreenProbeAtlasHistoryTex);
            Assert.Same(depth, first.Scene.Engine.Depth);
            Assert.True(radiance.IsValid);
            ImageAssertions.Comparisons.CompareConstant(other.ScreenProbeAtlasTraceTex, .75f).AssertEqual();
            if (prior != null) ImageAssertions.Predicates.Predicate(prior.Radiance, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, result.Radiance).AssertEqual();
            prior = result;
        }
        first.Dispose();
        Assert.False(radiance.IsValid);
        Assert.True(world.ProbeRadianceAtlas.IsValid);
        Assert.True(other.ScreenProbeAtlasTraceTex.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-cloud", 0, 0);
        programs.Prepare("predicate-geometry", 0, 0);
    }
    #endregion
}
