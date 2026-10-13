using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the actual compute publication's immutable frame scale and generation identity.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FrameLightingStorageTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Physical lighting admits the frozen frame slot and simultaneous fragment consumer limits.</summary>
    [Fact]
    public void LightingCapabilityAdmissionRequiresCompleteConsumerContract()
    {
        EnsureShaderTestAvailable();
        var supported = GpuSupport.Graphics with { MaxShaderStorageBufferBindings = 5,
            MaxFragmentShaderStorageBlocks = 2, MaxUniformBlockSize = VgeFrameUniformBuffer.PackedSize };
        Assert.True(CameraExposureDispatch.SupportsLighting(supported));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxShaderStorageBufferBindings = 4 }));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxFragmentShaderStorageBlocks = 1 }));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxUniformBlockSize = VgeFrameUniformBuffer.PackedSize - 1 }));
    }

    /// <summary>Automatic exposure is sampled once; later metering cannot modify the frozen publication.</summary>
    [Fact]
    public void FrozenScaleRetainsExposureAndSourceIdentity()
    {
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var previous = ExposureTestStorage.Create(2);
        using var output = GpuShaderStorageBuffer.Create(debugName: "Tests.FrozenLighting");
        output.Allocate(32);
        program.PreviousExposure = previous; program.Output = output;
        program.Capture(0, .5f, true, 42, 7);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        float[] first = ExposureTestStorage.Read<float>(output);
        uint[] identity = ExposureTestStorage.Read<uint>(output);
        Assert.Equal(4, first[0]); Assert.Equal(.25f, first[1]); Assert.Equal(1, first[3]);
        Assert.Equal(42u, identity[4]); Assert.Equal(7u, identity[5]);
        previous.UploadData(new[] { -4f });
        Assert.Equal(first, ExposureTestStorage.Read<float>(output));
        program.Capture(0, .5f, true, 43, 7);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(.0625f, ExposureTestStorage.Read<float>(output)[0]);
        program.Capture(0, 2, false, 44, 7);
        program.PreviousExposure = null;
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(2, ExposureTestStorage.Read<float>(output)[0]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>The Float32 budget remains conservative and unsupported retention never publishes valid lighting.</summary>
    [Fact]
    public void BoundAdmissionAndResetSeedAreConservative()
    {
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var previous = ExposureTestStorage.Create(float.NaN);
        using var output = GpuShaderStorageBuffer.Create(debugName: "Tests.BoundedLighting");
        output.Allocate(32);
        program.PreviousExposure = previous; program.Output = output;
        float bound = LightingStorage.UpperBound(945821327.847);
        program.Capture(bound, 1, true, 1, 1);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        float[] values = ExposureTestStorage.Read<float>(output);
        Assert.Equal(1, values[3]);
        Assert.True((double)values[0] * bound <= 16000);
        Assert.InRange(values[0], LightingStorage.MinimumFrameScale, LightingStorage.FrameScale(1, bound));
        Assert.Equal(bound, values[2]);
        program.Capture(0, .5f, true, 2, 1);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(.5f, ExposureTestStorage.Read<float>(output)[0]);
        float[] retained = ExposureTestStorage.Read<float>(output);
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Capture(float.MaxValue, 1, false, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Capture(0, 1, false, 3, 0));
        Assert.Equal(retained, ExposureTestStorage.Read<float>(output));
    }

    /// <summary>The last admitted bounds retain the packed-light floor despite the GPU's extra predecessor guard.</summary>
    [Fact]
    public void RetentionBoundaryRejectsUnsafeRoundedGuard()
    {
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var output = GpuShaderStorageBuffer.Create(debugName: "Tests.RetentionBoundary");
        output.Allocate(32); program.Output = output;
        float last = (float)((double)LightingStorage.FrameBudget / LightingStorage.MinimumFrameScale);
        // Locate the adjacent representable input accepted by the guard, independently of input packing.
        while (MathF.BitDecrement(LightingStorage.FrameBudget / last) < LightingStorage.MinimumFrameScale)
            last = MathF.BitDecrement(last);
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Capture(MathF.BitIncrement(last), 1, false, 1, 1));
        foreach (float bound in new[] { last, MathF.BitDecrement(last) })
        {
            program.Capture(bound, 1, false, 1, 1);
            Assert.True(CameraExposureDispatch.TryFreeze(program));
            float[] values = ExposureTestStorage.Read<float>(output);
            Assert.Equal(1, values[3]);
            Assert.True(values[0] >= LightingStorage.MinimumFrameScale);
            Assert.True((double)values[0] * bound <= LightingStorage.FrameBudget);
        }
    }
    #endregion
}
