using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Unit.Lighting;

/// <summary>Checks the actual compute publication's immutable frame scale and generation identity.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class FrameLightingStorageTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Physical lighting admits the frozen frame slot and simultaneous fragment consumer limits.</summary>
    [Fact]
    public void LightingCapabilityAdmissionRequiresCompleteConsumerContract()
    {
        EnsureShaderTestAvailable();
        // Arrange: Model a device meeting frame-slot, fragment-consumer, and uniform-size requirements.
        var supported = GpuSupport.Graphics with { MaxShaderStorageBufferBindings = 5,
            MaxFragmentShaderStorageBlocks = 2, MaxUniformBlockSize = VgeFrameUniformBuffer.PackedSize };
        // Act and assert: Admission succeeds at the complete boundary and fails when any required limit is short.
        Assert.True(CameraExposureDispatch.SupportsLighting(supported));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxShaderStorageBufferBindings = 4 }));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxFragmentShaderStorageBlocks = 1 }));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxUniformBlockSize = VgeFrameUniformBuffer.PackedSize - 1 }));
    }




    #endregion

}
