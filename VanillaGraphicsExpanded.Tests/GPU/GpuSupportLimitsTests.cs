using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies shared capability limits against the current native implementation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuSupportLimitsTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Checks resource and dynamic limits using their exact GL query names.</summary>
    [Fact]
    public void SharedLimitsMatchNativeImplementation()
    {
        fixture.MakeCurrent();
        GpuSupport.EnsureCurrentContext();
        int[] viewport = new int[2];
        GL.GetInteger(GetPName.MaxViewportDims, viewport);
        Assert.Equal(viewport[0], GpuSupport.MaxViewportWidth);
        Assert.Equal(viewport[1], GpuSupport.MaxViewportHeight);
        Assert.Equal(GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment), GpuSupport.ShaderStorageBufferOffsetAlignment);
        Assert.Equal(GL.GetInteger(GetPName.MaxDrawBuffers), GpuSupport.MaxDrawBuffers);
        Assert.Equal(GL.GetInteger(GetPName.MaxPatchVertices), GpuSupport.MaxPatchVertices);
        Assert.Equal(GL.GetInteger(GetPName.MaxCombinedTextureImageUnits), GpuSupport.MaxCombinedTextureImageUnits);
        Assert.Equal(GL.GetInteger((GetPName)All.MaxImageUnits), GpuSupport.MaxImageUnits);
        Assert.Equal(GL.GetInteger((GetPName)All.MaxCombinedImageUniforms), GpuSupport.MaxCombinedImageUnits);
        Assert.Equal(GL.GetInteger(GetPName.MaxUniformBufferBindings), GpuSupport.MaxUniformBufferBindings);
        Assert.Equal(GL.GetInteger(GetPName.MaxShaderStorageBufferBindings), GpuSupport.MaxShaderStorageBufferBindings);
        Assert.Equal(GL.GetInteger((GetPName)All.MaxAtomicCounterBufferBindings), GpuSupport.MaxAtomicCounterBufferBindings);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Mutable state invalidation neither recaptures capabilities nor consumes pending GL errors.</summary>
    [Fact]
    public void WarmCapabilitiesSurviveStateInvalidationWithoutNativePolling()
    {
        fixture.MakeCurrent();
        GpuSupport.EnsureCurrentContext();
        long captures = GpuSupport.CaptureCount;
        StateCache.Current.InvalidateAll();
        GL.GetInteger((GetPName)(-1));
        GpuSupport.EnsureCurrentContext();
        Assert.Equal(captures, GpuSupport.CaptureCount);
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
