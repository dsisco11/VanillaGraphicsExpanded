using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks the minimum GPU capability contract required for camera exposure dispatch.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CameraExposureShaderTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Admission accepts the complete minimum contract and rejects each missing capability or limit.</summary>
    [Fact]
    public void CapabilityAdmissionRequiresCompleteComputeAndConsumerContract()
    {
        EnsureShaderTestAvailable();
        // Arrange: Supply the minimum compute, storage, and fragment-consumer limits as one supported device.
        var supported = GpuSupport.Graphics with
        {
            MaxComputeWorkGroupInvocations = 256,
            MaxComputeWorkGroupSize = System.Collections.Immutable.ImmutableArray.Create(256, 1, 1),
            MaxComputeWorkGroupCount = System.Collections.Immutable.ImmutableArray.Create(CameraExposureTargets.HistogramGroupCount, 1, 1),
            MaxComputeSharedMemorySize = 2560,
            MaxShaderStorageBufferBindings = 3,
            MaxFragmentShaderStorageBlocks = 1,
            MaxComputeShaderStorageBlocks = 3,
            MaxShaderStorageBlockSize = CameraExposureTargets.HistogramGroupCount * 512
        };
        Assert.True(CameraExposureDispatch.Supports(supported));

        GraphicsCapabilities[] insufficient =
        [
            supported with { SupportsArbComputeShader = false },
            supported with { SupportsArbShaderImageLoadStore = false },
            supported with { SupportsArbShaderStorageBufferObject = false },
            supported with { SupportsArbGlSpirv = false },
            supported with { MaxComputeWorkGroupInvocations = 255 },
            supported with { MaxComputeWorkGroupSize = System.Collections.Immutable.ImmutableArray.Create(255, 1, 1) },
            supported with { MaxComputeWorkGroupCount = System.Collections.Immutable.ImmutableArray.Create(CameraExposureTargets.HistogramGroupCount - 1, 1, 1) },
            supported with { MaxComputeSharedMemorySize = 2559 },
            supported with { MaxShaderStorageBufferBindings = 2 },
            supported with { MaxFragmentShaderStorageBlocks = 0 },
            supported with { MaxComputeShaderStorageBlocks = 2 },
            supported with { MaxShaderStorageBlockSize = CameraExposureTargets.HistogramGroupCount * 512 - 1 }
        ];
        // Act and assert: The complete contract is admitted; removing any one capability or limit rejects it.
        Assert.All(insufficient, value => Assert.False(CameraExposureDispatch.Supports(value)));
    }
    #endregion
}
