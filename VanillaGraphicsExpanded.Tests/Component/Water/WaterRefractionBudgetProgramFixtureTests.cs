using VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using Vintagestory.Client;


namespace VanillaGraphicsExpanded.Tests.Component.Water;


/// <summary>Verifies case isolation and retained-input lifetime for the numerical water diagnostic owner.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterRefractionBudgetProgramFixtureTests(HeadlessGLFixture fixture,
    WaterRefractionBudgetProgramFixture budgetPrograms)
    : LumOnShaderFunctionalTestBase(fixture), IClassFixture<WaterRefractionBudgetProgramFixture>
{
    #region Public API
    /// <summary>A lease clears all input channels while earlier captured uniform versions remain unchanged.</summary>
    [Fact]
    public void LeaseResetsTypedAndBorrowedInputsWithoutChangingCapturedBytes()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        using var colorA = CreateTexture(TextureFormat.Rgba32f);
        using var depthA = CreateTexture(TextureFormat.R32f);
        using var colorB = CreateTexture(TextureFormat.Rgba32f);
        using var depthB = CreateTexture(TextureFormat.R32f);
        RHIUniformSnapshot? capturedA = null;
        byte[]? capturedABytes = null;
        WaterRefractionBudgetProgramFixture.Lease? retiredLease = null;

        // Arrange and Act: case A publishes non-default scalar, batch, camera and texture inputs.
        // Act: execute the behavior under test and capture its observation.
        using (var lease = budgetPrograms.Acquire())
        {
            retiredLease = lease;
            var program = lease.Program;
            var bindings = (IWaterRefractionDiagnosticBindings)program;
            using var camera = new VgeFrameUniformBuffer();
            program.FrameInputs = camera;
            program.Scenario = 12;
            program.Surface = new(1.25f, -2.5f, -7.75f);
            program.Normal = Vector3.Normalize(new Vector3(.25f, .5f, .75f));
            program.Budget = 8;
            program.SelectReceiver = 2;
            program.Quality = 3;
            program.Underwater = 1;
            program.SetMotionBatch([new Vector4(2, 3, 4, 5), new Vector4(-1, -2, -3, -4)]);
            bindings.Color = colorA;
            bindings.Depth = depthA;
            capturedA = bindings.Inputs.Capture();
            capturedABytes = capturedA.Bytes.ToArray();
        // Assert: compare the observed result with the expected contract.
            Assert.Throws<InvalidOperationException>(() => budgetPrograms.Acquire());
            Assert.Throws<InvalidOperationException>(() => budgetPrograms.Dispose());
        }
        Assert.NotNull(retiredLease);
        Assert.Throws<ObjectDisposedException>(() => _ = retiredLease!.Program);

        // Assert and Act: case B starts from defaults, then replaces A with distinct inputs.
        using (var lease = budgetPrograms.Acquire())
        {
            var program = lease.Program;
            var bindings = (IWaterRefractionDiagnosticBindings)program;
            AssertDefaultInputs(program, bindings);

            using var camera = new VgeFrameUniformBuffer();
            program.FrameInputs = camera;
            program.Scenario = 4;
            program.Surface = new(-9, 8, -6);
            program.Normal = Vector3.UnitY;
            program.Budget = 2;
            program.SelectReceiver = 1;
            program.Quality = 1;
            program.Underwater = 0;
            program.SetMotionBatch([new Vector4(6, 7, 8, 9)]);
            bindings.Color = colorB;
            bindings.Depth = depthB;
        }

        // Assert: the next case sees no residue and A's previously captured UBO bytes stay immutable.
        using (var lease = budgetPrograms.Acquire())
        {
            var program = lease.Program;
            var bindings = (IWaterRefractionDiagnosticBindings)program;
            AssertDefaultInputs(program, bindings);
            Assert.NotNull(capturedA);
            Assert.NotNull(capturedABytes);
            Assert.Equal(capturedABytes, capturedA!.Bytes.ToArray());
            Assert.NotSame(capturedA, bindings.Inputs.Capture());
        }

        // Retire only after all leases finish, then verify native ownership and the outer engine scope are restored.
        budgetPrograms.Dispose();
        Assert.True(budgetPrograms.ProgramIsRetired);
        Assert.Same(budgetPrograms.PreviousPlatform, ScreenManager.Platform);
        Assert.Throws<ObjectDisposedException>(() => budgetPrograms.Acquire());
    }
    #endregion

    #region Private
    /// <summary>Creates one case-owned texture with the format required by the diagnostic sampler.</summary>
    private static DynamicTexture2D CreateTexture(TextureFormat format)
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

    /// <summary>Checks all scalar and borrowed channels, including batch bytes unused by scalar draws.</summary>
    private static void AssertDefaultInputs(WaterRefractionDiagnosticShaderProgram program,
        IWaterRefractionDiagnosticBindings bindings)
    {
        Assert.Equal(0, program.Scenario);
        Assert.Equal(Vector3.Zero, program.Surface);
        Assert.Equal(Vector3.Zero, program.Normal);
        Assert.Equal(0, program.Budget);
        Assert.Equal(0, program.SelectReceiver);
        Assert.Equal(0, program.Quality);
        Assert.Equal(0, program.Underwater);
        Assert.Null(program.FrameInputs);
        Assert.True(bindings.Color.IsEmpty);
        Assert.True(bindings.Depth.IsEmpty);
        Assert.All(bindings.Inputs.Capture().Bytes.ToArray(), value => Assert.Equal((byte)0, value));
    }
    #endregion
}
