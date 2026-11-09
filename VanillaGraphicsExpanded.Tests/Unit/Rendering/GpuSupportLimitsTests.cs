using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies shared capability limits against the current native implementation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuSupportLimitsTests(HeadlessGLFixture fixture)
{
    #region Public API






    /// <summary>Production descriptor construction validates against the shared owner's cached limits.</summary>
    [Fact]
    public void DefaultDescriptorValidationUsesSharedSupport()
    {
        // Arrange: Use the current context and construct a valid pipeline without injecting capability limits.
        fixture.MakeCurrent();
        GpuSupport.EnsureCurrentContext();
#if DEBUG
        long captures = GpuSupport.CaptureCount;
#endif
        var bindings = new GpuBindingContract();
        var contract = new GpuShaderContract("support-test", [
            new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, bindings),
            new ShaderStageContract("test.fsh", "test.fsh", ShaderStageKind.Fragment, bindings)], 1);
        using var shaderOwner = new GraphicsStateShaders();
        var shader = shaderOwner.Get();
        var targets = new RenderTargetSignature([new(TextureFormat.Rgba16f)]);
        var valid = new RHIVertexAttribute(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12);
        var description = new RHIGraphicsPipelineDescription(shader, new([valid]), targets, DynamicPipelineState.Viewport);
        // Act and assert: The valid descriptor is retained; a location at the exclusive native limit is rejected.
        Assert.Equal(targets, description.Targets);
        // A location at the exclusive native limit proves omitted injection uses actual shared support.
        var excessive = new RHIGraphicsPipelineDescription(shader,
            new([valid with { Location = GpuSupport.MaxVertexAttribs }]), targets, DynamicPipelineState.Viewport);
        Assert.Throws<NotSupportedException>(() => RHICommandContext.Current.CreateGraphicsPipeline(excessive).RequireResource());
#if DEBUG
        Assert.Equal(captures, GpuSupport.CaptureCount);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
