using VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.Water.Support;

using Vintagestory.Client;

namespace VanillaGraphicsExpanded.Tests.Component.Water;

/// <summary>Checks paired diagnostic/filter resets and the owner retirement path used by shallow-wall cases.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterShallowContinuityProgramFixtureTests(HeadlessGLFixture fixture,
    WaterShallowContinuityProgramFixture sharedPrograms)
    : LumOnShaderFunctionalTestBase(fixture), IClassFixture<WaterShallowContinuityProgramFixture>
{
    #region Public API
    /// <summary>Resets the paired shader inputs between distinct cases and retires their class-owned handles.</summary>
    [Fact]
    public void ShallowProgramLeaseResetsPairAndRetiresAfterActiveCase()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        using var colorA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        using var colorB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        RHIUniformSnapshot? diagnosticA = null;
        RHIUniformSnapshot? filterA = null;
        byte[]? diagnosticABytes = null;
        byte[]? filterABytes = null;
        WaterNumericalProgramFixtureOwner<WaterShallowProgramSet>.Lease? retiredLease = null;

        // Arrange and Act: lease A fills both full UBO layouts, frame bindings and paired receiver references.
        // Act: execute the behavior under test and capture its observation.
        using (var lease = sharedPrograms.Acquire())
        {
            retiredLease = lease;
            var set = lease.ProgramSet;
            var diagnostic = (IWaterRefractionDiagnosticBindings)set.Diagnostic;
            var filter = (IWaterReceiverFilterBindings)set.Filter;
            using var camera = new VgeFrameUniformBuffer();
            set.Diagnostic.FrameInputs = camera;
            set.Filter.FrameInputs = camera;
            set.Diagnostic.Scenario = 12;
            set.Diagnostic.Surface = new(2, -3, -5);
            set.Diagnostic.Normal = Vector3.Normalize(new Vector3(.3f, .4f, .8f));
            set.Diagnostic.Budget = 8;
            set.Diagnostic.SelectReceiver = 1;
            set.Diagnostic.Quality = 3;
            set.Diagnostic.Underwater = 1;
            set.Diagnostic.SetMotionBatch([new Vector4(1, 2, 3, 4)]);
            diagnostic.Color = colorA;
            diagnostic.Depth = depthA;
            set.Filter.SampleUv = new(.25f, .75f);
            set.Filter.Surface = new(-2, 3, -8);
            set.Filter.Normal = Vector3.UnitY;
            set.Filter.SetMotionBatch([new Vector4(5, 6, 7, 8)], [new Vector4(.125f, .875f, 0, 0)]);
            filter.Color = colorA;
            filter.Depth = depthA;
            diagnosticA = diagnostic.Inputs.Capture();
            filterA = filter.Inputs.Capture();
            diagnosticABytes = diagnosticA.Bytes.ToArray();
            filterABytes = filterA.Bytes.ToArray();
        // Assert: compare the observed result with the expected contract.
            Assert.Throws<InvalidOperationException>(() => sharedPrograms.Acquire());
            Assert.Throws<InvalidOperationException>(() => sharedPrograms.Dispose());
        }
        Assert.NotNull(retiredLease);
        Assert.Throws<ObjectDisposedException>(() => _ = retiredLease!.ProgramSet);

        // Assert and Act: lease B starts from defaults, then writes a second state before releasing the owner.
        using (var lease = sharedPrograms.Acquire())
        {
            var set = lease.ProgramSet;
            AssertDefaultInputs(set);
            var diagnostic = (IWaterRefractionDiagnosticBindings)set.Diagnostic;
            var filter = (IWaterReceiverFilterBindings)set.Filter;

            using var camera = new VgeFrameUniformBuffer();
            set.Diagnostic.FrameInputs = camera;
            set.Filter.FrameInputs = camera;
            set.Diagnostic.Scenario = 4;
            set.Diagnostic.Surface = new(-9, 8, -6);
            set.Diagnostic.Normal = Vector3.UnitX;
            set.Diagnostic.Budget = 2;
            set.Diagnostic.SelectReceiver = 2;
            set.Diagnostic.Quality = 1;
            set.Diagnostic.Underwater = 0;
            set.Diagnostic.SetMotionBatch([new Vector4(-1, -2, -3, -4)]);
            diagnostic.Color = colorB;
            diagnostic.Depth = depthB;
            set.Filter.SampleUv = new(.875f, .125f);
            set.Filter.Surface = new(6, -7, -3);
            set.Filter.Normal = Vector3.UnitZ;
            set.Filter.SetMotionBatch([new Vector4(-5, -6, -7, -8)], [new Vector4(.75f, .25f, 0, 0)]);
            filter.Color = colorB;
            filter.Depth = depthB;
        }

        // Assert: lease C is clean and neither reset mutates snapshots captured by the earlier A submission.
        using (var lease = sharedPrograms.Acquire())
        {
            AssertDefaultInputs(lease.ProgramSet);
            Assert.NotNull(diagnosticA);
            Assert.NotNull(filterA);
            Assert.NotNull(diagnosticABytes);
            Assert.NotNull(filterABytes);
            Assert.Equal(diagnosticABytes, diagnosticA!.Bytes.ToArray());
            Assert.Equal(filterABytes, filterA!.Bytes.ToArray());
            Assert.NotSame(diagnosticA, ((IWaterRefractionDiagnosticBindings)lease.ProgramSet.Diagnostic).Inputs.Capture());
            Assert.NotSame(filterA, ((IWaterReceiverFilterBindings)lease.ProgramSet.Filter).Inputs.Capture());
        }

        sharedPrograms.Dispose();
        Assert.True(sharedPrograms.ProgramSetIsRetired);
        Assert.Same(sharedPrograms.PreviousPlatform, ScreenManager.Platform);
        Assert.Throws<ObjectDisposedException>(() => sharedPrograms.Acquire());
    }
    #endregion

    #region Private
    /// <summary>Checks every borrowed and packed input for both programs in the shallow case set.</summary>
    private static void AssertDefaultInputs(WaterShallowProgramSet set)
    {
        Assert.Equal(0, set.Diagnostic.Scenario);
        Assert.Equal(Vector3.Zero, set.Diagnostic.Surface);
        Assert.Equal(Vector3.Zero, set.Diagnostic.Normal);
        Assert.Equal(0, set.Diagnostic.Budget);
        Assert.Equal(0, set.Diagnostic.SelectReceiver);
        Assert.Equal(0, set.Diagnostic.Quality);
        Assert.Equal(0, set.Diagnostic.Underwater);
        Assert.Null(set.Diagnostic.FrameInputs);
        Assert.Null(set.Filter.FrameInputs);
        Assert.Equal(Vector2.Zero, set.Filter.SampleUv);
        Assert.Equal(Vector3.Zero, set.Filter.Surface);
        Assert.Equal(Vector3.Zero, set.Filter.Normal);
        Assert.True(((IWaterRefractionDiagnosticBindings)set.Diagnostic).Color.IsEmpty);
        Assert.True(((IWaterRefractionDiagnosticBindings)set.Diagnostic).Depth.IsEmpty);
        Assert.True(((IWaterReceiverFilterBindings)set.Filter).Color.IsEmpty);
        Assert.True(((IWaterReceiverFilterBindings)set.Filter).Depth.IsEmpty);
        WaterNumericalProgramFixtureAssertions.AssertZeroBytes(((IWaterRefractionDiagnosticBindings)set.Diagnostic).Inputs);
        WaterNumericalProgramFixtureAssertions.AssertZeroBytes(((IWaterReceiverFilterBindings)set.Filter).Inputs);
    }
    #endregion
}

