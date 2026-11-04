using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks reset and retirement contracts for class-owned numerical water fixture programs.</summary>
internal static class WaterNumericalProgramFixtureAssertions
{
    /// <summary>Creates a fresh GPU image for a fixture boundary control.</summary>
    /// <param name="format">The image format required by the tested sampler slot.</param>
    /// <returns>A case-owned texture whose reference must be cleared before its next lease.</returns>
    internal static DynamicTexture2D CreateTexture(TextureFormat format)
        => RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        {
            Dimension = TextureDimension.Two,
            Format = format,
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Storage = ResourceStorageMode.Mutable,
            Filter = TextureFilterMode.Nearest,
            BaseLevel = 0,
            MaxLevel = 0,
            Label = null
        }).RequireResource();

    /// <summary>Verifies the complete packed uniform buffer has returned to zero, including unused batch storage.</summary>
    /// <param name="inputs">The actual wrapper-owned buffer exposed by its shader binding contract.</param>
    internal static void AssertZeroBytes(CpuUniformBuffer inputs)
        => Assert.All(inputs.Capture().Bytes.ToArray(), value => Assert.Equal((byte)0, value));
}

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

/// <summary>Checks full reset and snapshot behavior for the UV numerical class fixture.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterUvRefractionProgramFixtureTests(HeadlessGLFixture fixture,
    WaterUvRefractionProgramFixture sharedPrograms)
    : LumOnShaderFunctionalTestBase(fixture), IClassFixture<WaterUvRefractionProgramFixture>
{
    #region Public API
    /// <summary>A UV lease clears all packed and borrowed state without changing a previous captured version.</summary>
    [Fact]
    public void UvProgramLeaseResetsEveryInputBetweenCases()
    {
        EnsureShaderTestAvailable();
        using var colorA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        using var colorB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        RHIUniformSnapshot? snapshotA = null;
        byte[]? bytesA = null;

        // Arrange and Act: save a non-default UV input version before releasing case A.
        using (var lease = sharedPrograms.Acquire())
        {
            var program = lease.ProgramSet;
            var bindings = (IWaterUvRefractionBindings)program;
            using var camera = new VgeFrameUniformBuffer();
            program.FrameInputs = camera;
            program.Surface = new(1.5f, -2.5f, -7);
            program.Normal = Vector3.Normalize(new Vector3(.3f, .4f, .8f));
            program.Underwater = 1;
            bindings.Color = colorA;
            bindings.Depth = depthA;
            snapshotA = bindings.Inputs.Capture();
            bytesA = snapshotA.Bytes.ToArray();
        }

        // Assert and Act: the next lease is clean and a second authored case cannot retain case A state.
        using (var lease = sharedPrograms.Acquire())
        {
            var program = lease.ProgramSet;
            var bindings = (IWaterUvRefractionBindings)program;
            AssertDefaultInputs(program, bindings);
            using var camera = new VgeFrameUniformBuffer();
            program.FrameInputs = camera;
            program.Surface = new(-6, 5, -4);
            program.Normal = Vector3.UnitY;
            program.Underwater = 0;
            bindings.Color = colorB;
            bindings.Depth = depthB;
        }

        // Assert: the following case starts from defaults and A's immutable snapshot remains byte-for-byte stable.
        using (var lease = sharedPrograms.Acquire())
        {
            var program = lease.ProgramSet;
            var bindings = (IWaterUvRefractionBindings)program;
            AssertDefaultInputs(program, bindings);
            Assert.NotNull(snapshotA);
            Assert.NotNull(bytesA);
            Assert.Equal(bytesA, snapshotA!.Bytes.ToArray());
            Assert.NotSame(snapshotA, bindings.Inputs.Capture());
        }
    }
    #endregion

    #region Private
    /// <summary>Checks every UV wrapper field, frame reference, sampler and packed byte after fixture reset.</summary>
    private static void AssertDefaultInputs(WaterUvRefractionShaderProgram program, IWaterUvRefractionBindings bindings)
    {
        Assert.Equal(Vector3.Zero, program.Surface);
        Assert.Equal(Vector3.Zero, program.Normal);
        Assert.Equal(0, program.Underwater);
        Assert.Null(program.FrameInputs);
        Assert.True(bindings.Color.IsEmpty);
        Assert.True(bindings.Depth.IsEmpty);
        WaterNumericalProgramFixtureAssertions.AssertZeroBytes(bindings.Inputs);
    }
    #endregion
}

/// <summary>Checks full reset and snapshot behavior for the pixel-normal numerical class fixture.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterPixelNormalRefractionProgramFixtureTests(HeadlessGLFixture fixture,
    WaterPixelNormalRefractionProgramFixture sharedPrograms)
    : LumOnShaderFunctionalTestBase(fixture), IClassFixture<WaterPixelNormalRefractionProgramFixture>
{
    #region Public API
    /// <summary>A pixel-normal lease clears its base normal as well as optical and receiver inputs.</summary>
    [Fact]
    public void PixelNormalProgramLeaseResetsEveryInputBetweenCases()
    {
        EnsureShaderTestAvailable();
        using var colorA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        using var colorB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        RHIUniformSnapshot? snapshotA = null;
        byte[]? bytesA = null;

        // Arrange and Act: retain case A's distinct base normal and all other typed and borrowed inputs.
        using (var lease = sharedPrograms.Acquire())
        {
            var program = lease.ProgramSet;
            var bindings = (IWaterPixelNormalRefractionBindings)program;
            using var camera = new VgeFrameUniformBuffer();
            program.FrameInputs = camera;
            program.Surface = new(1.5f, -2.5f, -7);
            program.Normal = Vector3.Normalize(new Vector3(.3f, .4f, .8f));
            program.Underwater = 1;
            program.BaseNormal = Vector3.Normalize(new Vector3(-.2f, .1f, 1));
            bindings.Color = colorA;
            bindings.Depth = depthA;
            snapshotA = bindings.Inputs.Capture();
            bytesA = snapshotA.Bytes.ToArray();
        }

        // Assert and Act: case B sees defaults, then installs distinct values for case C to clear.
        using (var lease = sharedPrograms.Acquire())
        {
            var program = lease.ProgramSet;
            var bindings = (IWaterPixelNormalRefractionBindings)program;
            AssertDefaultInputs(program, bindings);
            using var camera = new VgeFrameUniformBuffer();
            program.FrameInputs = camera;
            program.Surface = new(-6, 5, -4);
            program.Normal = Vector3.UnitY;
            program.Underwater = 0;
            program.BaseNormal = Vector3.UnitZ;
            bindings.Color = colorB;
            bindings.Depth = depthB;
        }

        // Assert: case C is fully clean and case A's published UBO bytes were not rewritten by reset.
        using (var lease = sharedPrograms.Acquire())
        {
            var program = lease.ProgramSet;
            var bindings = (IWaterPixelNormalRefractionBindings)program;
            AssertDefaultInputs(program, bindings);
            Assert.NotNull(snapshotA);
            Assert.NotNull(bytesA);
            Assert.Equal(bytesA, snapshotA!.Bytes.ToArray());
            Assert.NotSame(snapshotA, bindings.Inputs.Capture());
        }
    }
    #endregion

    #region Private
    /// <summary>Checks every pixel-normal wrapper field, frame reference, sampler and packed byte after reset.</summary>
    private static void AssertDefaultInputs(WaterPixelNormalRefractionShaderProgram program,
        IWaterPixelNormalRefractionBindings bindings)
    {
        Assert.Equal(Vector3.Zero, program.Surface);
        Assert.Equal(Vector3.Zero, program.Normal);
        Assert.Equal(0, program.Underwater);
        Assert.Equal(Vector3.Zero, program.BaseNormal);
        Assert.Null(program.FrameInputs);
        Assert.True(bindings.Color.IsEmpty);
        Assert.True(bindings.Depth.IsEmpty);
        WaterNumericalProgramFixtureAssertions.AssertZeroBytes(bindings.Inputs);
    }
    #endregion
}
