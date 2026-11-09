using VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.Water.Support;

using Vintagestory.Client;

namespace VanillaGraphicsExpanded.Tests.Component.Water;

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
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        using var colorA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthA = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        using var colorB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.Rgba32f);
        using var depthB = WaterNumericalProgramFixtureAssertions.CreateTexture(TextureFormat.R32f);
        RHIUniformSnapshot? snapshotA = null;
        byte[]? bytesA = null;

        // Arrange and Act: save a non-default UV input version before releasing case A.
        // Act: execute the behavior under test and capture its observation.
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
        // Assert: compare the observed result with the expected contract.
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

