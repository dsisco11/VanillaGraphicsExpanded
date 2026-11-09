using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using Vintagestory.Client;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Verifies the migrated trace harness actually borrows and retains its supplied branch resources.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NearFieldResourceReuseTests : NearFieldShaderTestBase, IClassFixture<NearFieldNumericalProgramFixture>
{
    #region Public API
    /// <summary>Uses the shared GPU context and real component shader owners.</summary>
    public NearFieldResourceReuseTests(HeadlessGLFixture fixture, NearFieldNumericalProgramFixture numericalPrograms)
        : base(fixture, numericalPrograms)
    {
        context = fixture;
        this.numericalPrograms = numericalPrograms;
    }

    /// <summary>Switching branches, shader variants and suppression cannot contaminate a reused bright trace program.</summary>
    [Fact]
    public void TraceRebindsInputsAfterBranchAndVariantChanges()
    {
        // Arrange two independent branches and a published geometry source for repeated traces.
        EnsureShaderTestAvailable();
        using var geometry = new NearFieldVoxelFixture();
        geometry.Publish(new VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures.ControlledVoxelWorld());
        using var first = new ShaderLightingResources(Programs.Api);
        using var second = new ShaderLightingResources(Programs.Api);
        // Act with the bright selection, a suppressed branch, a different program variant, then the original selection.
        var bright = Trace(geometry, resources: first);
        Assert.True(numericalPrograms.CaseInputsAreReset);
        var suppressed = Trace(geometry, resources: second, suppress: true);
        Assert.True(numericalPrograms.CaseInputsAreReset);
        // Interleave a different compiled selection, then return to the original program and branch.
        var disabled = Trace(geometry, resources: second, worldCache: false);
        Assert.True(numericalPrograms.CaseInputsAreReset);
        var restored = Trace(geometry, resources: first);
        Assert.True(numericalPrograms.CaseInputsAreReset);
        // Assert the two branches own distinct output resources and equivalent bright reruns preserve observations.
        Assert.NotSame(first.EnsureScreen(4, 4, 2).ScreenProbeAtlasTraceTex,
            second.EnsureScreen(4, 4, 2).ScreenProbeAtlasTraceTex);
        ImageAssertions.Predicates.Predicate(bright.Radiance, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, restored.Radiance).AssertEqual();
        ImageAssertions.Predicates.Predicate(bright.Meta, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, restored.Meta).AssertEqual();
        ImageAssertions.Predicates.Predicate(bright.Meta, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, suppressed.Meta).AssertEqual();
        ObserveTrace(restored, GeometryPredicates.NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges, suppressed).AssertEqual();
        ObserveTrace(disabled, GeometryPredicates.NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges_2).AssertEqual();
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        // Arrange a failure after the draw and readback so this checks lease cleanup on the consumer callback path.
        var injectedFailure = new InvalidOperationException("Injected consumer failure after trace readback.");
        // Act and verify the exact callback exception escapes Trace rather than an unrelated invalid operation.
        var observedFailure = Assert.Throws<InvalidOperationException>(() => Trace(geometry, resources: second,
            consume: _ => throw injectedFailure));
        Assert.Same(injectedFailure, observedFailure);

        // Assert cleanup, then render the same bright selection again to prove the program remains reusable.
        Assert.True(numericalPrograms.CaseInputsAreReset);
        var afterException = Trace(geometry, resources: second);
        Assert.True(numericalPrograms.CaseInputsAreReset);
        ImageAssertions.Predicates.Predicate(bright.Radiance,
            CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5,
            afterException.Radiance).AssertEqual();
        ImageAssertions.Predicates.Predicate(bright.Meta,
            CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5,
            afterException.Meta).AssertEqual();
    }

    /// <summary>Rejects overlapping leases and retires a compiled variant only after its inputs have cleared.</summary>
    [Fact]
    public void NumericalProgramOwnerRejectsActiveRetirementAndReleasesNativeProgram()
    {
        // Arrange an isolated numerical program owner and one active lease.
        EnsureShaderTestAvailable();
        var owner = new NearFieldNumericalProgramFixture(context);
        var key = new NearFieldTraceProgramKey(true, 1, 64, true, 1, 8, 16, 4);
        NearFieldNumericalProgramFixture.Lease? lease = null;
        try
        {
            // Arrange one live owner and acquire its executable; overlapping acquisition and active retirement must fail.
            lease = owner.Acquire(key);
            Assert.Throws<InvalidOperationException>(() => owner.Acquire(key));
            Assert.Throws<InvalidOperationException>(() => owner.Dispose());
            var originalShader = lease.Program.Shader;

            // Act: release and reacquire the same immutable key, then verify the native shader survived the lease gap.
            lease.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _ = lease.Program);
            lease = null;
            using (var repeatedLease = owner.Acquire(key))
            {
                Assert.Equal(originalShader, repeatedLease.Program.Shader);
            }

            // Assert the lease reset contract and complete owner retirement after the native identity check.
            Assert.True(owner.CaseInputsAreReset);
            owner.Dispose();
            Assert.True(owner.ProgramsAreRetired);
            Assert.Same(owner.PreviousPlatform, ScreenManager.Platform);
            Assert.Throws<ObjectDisposedException>(() => owner.Acquire(key));
        }
        finally
        {
            try { lease?.Dispose(); }
            finally { owner.Dispose(); }
        }
    }

    /// <summary>Repeated real draws retain buffers and do not overwrite a simultaneous branch or retire external world resources.</summary>
    [Fact]
    public void TraceReusesSuppliedBranchWithoutTakingOwnershipOfExternalInputs()
    {
        // Arrange two screen branches and a separate caller-owned world resource set.
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
        // Act through repeated traces on the supplied first branch while the second branch and external world stay live.
        TraceImages? prior = null;
        for (int frame = 0; frame < 2; frame++)
        {
            var result = Trace(geometry, nearFieldTracing: false, worldCache: false,
                worldResources: world, resources: first,
                consume: traced => Assert.Same(output, traced));
            // Assert reuse, branch isolation, external input lifetime, and repeatability after each trace.
            Assert.Same(screen, first.EnsureScreen(4, 4, 2));
            Assert.Equal(radiance, (GpuTextureRef)screen.ScreenProbeAtlasTraceTex);
            Assert.Same(history, screen.ScreenProbeAtlasHistoryTex);
            Assert.Same(depth, first.Scene.Engine.Depth);
            Assert.True(radiance.IsValid);
            ImageAssertions.Comparisons.CompareConstant(other.ScreenProbeAtlasTraceTex, .75f).AssertEqual();
            if (prior != null) ImageAssertions.Predicates.Predicate(prior.Radiance, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5, result.Radiance).AssertEqual();
            prior = result;
        }
        // Act by disposing only the supplied screen owner.
        first.Dispose();
        // Assert owned outputs retire while external world and sibling branch resources remain valid.
        Assert.False(radiance.IsValid);
        Assert.True(world.ProbeRadianceAtlas.IsValid);
        Assert.True(other.ScreenProbeAtlasTraceTex.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-cloud", 0, 0);
        programs.Prepare("predicate-geometry", 0, 0);
    }

    #endregion

    #region Private
    private readonly HeadlessGLFixture context;

    private readonly NearFieldNumericalProgramFixture numericalPrograms;

    #endregion

}
