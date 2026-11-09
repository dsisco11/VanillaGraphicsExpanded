using VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.Water.Support;

using Vintagestory.Client;

namespace VanillaGraphicsExpanded.Tests.Component.Water;

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
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        using var colorA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        using var colorB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        RHIUniformSnapshot? snapshotA = null;
        byte[]? bytesA = null;

        // Arrange and Act: retain case A's distinct base normal and all other typed and borrowed inputs.
        // Act: execute the behavior under test and capture its observation.
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
        // Assert: compare the observed result with the expected contract.
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
