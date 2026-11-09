using System;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks change-driven geometry and pass preparation independently of requested native rendering effects.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GraphicsPreparationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Unrelated storage and content changes preserve preparation; shrink withdraws it until a legal replacement.</summary>
    [Fact]
    public void GeometryRefreshIsLocalAndFailedBoundsRemainWithdrawn()
    {
        // Arrange: Prepare geometry/pass state, then change unrelated storage before shrinking bounds.
        // Act and assert: Refresh affected state and verify invalid bounds withdraw preparation until a legal replacement.
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var shader = CreateShader();
        var layout = new RHIVertexLayout([new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 8)]);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader, layout, new([new(TextureFormat.Rgba8)]), DynamicPipelineState.Viewport)).RequireResource();
        using var buffer = commands.CreateVertexBuffer(new() { Role = BufferRole.Vertex, Capacity = 24, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
        using var unrelated = commands.CreateVertexBuffer(new() { Role = BufferRole.Vertex, Capacity = 8, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
        var geometry = new RHIGeometryInputs(layout, [new(0, new(buffer, 0, 24), 8)]);
        var backend = Backend(commands);
        geometry.Prepare(commands.Capabilities, backend, pipeline);
#if DEBUG
        long prepared = geometry.Preparations;
#endif
        unrelated.Allocate(16);
        geometry.Prepare(commands.Capabilities, backend, pipeline);
#if DEBUG
        Assert.Equal(prepared, geometry.Preparations);
#endif
        buffer.Allocate(8);
        Assert.Throws<ArgumentOutOfRangeException>(() => geometry.Prepare(commands.Capabilities, backend, pipeline));
#if DEBUG
        long failed = geometry.Preparations;
#endif
        Assert.Throws<ArgumentOutOfRangeException>(() => geometry.Prepare(commands.Capabilities, backend, pipeline));
#if DEBUG
        Assert.Equal(failed, geometry.Preparations);
#endif
        buffer.Allocate(24);
        geometry.Prepare(commands.Capabilities, backend, pipeline);
        Assert.Equal(2, geometry.FetchLimits[0].Last);
    }

    #endregion

    #region Private
    /// <summary>Matches named framebuffer creation without changing its arguments or outputs.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private unsafe delegate void CreateFramebuffers(int count, uint* names);
    /// <summary>Uses an inline executable with one active attribute and no resource bindings.</summary>
    private static RHIShader CreateShader() => RHICommandContext.Current.CreateShader(new(ShaderKind.Graphics,
        [new(ShaderStage.Vertex, "#version 450\nlayout(location=0) in vec2 position; void main(){gl_Position=vec4(position,0,1);}"),
         new(ShaderStage.Fragment, "#version 450\nlayout(location=0) out vec4 color; void main(){color=vec4(1);}")], new())).RequireResource();
    /// <summary>Inspects the existing native owner without adding a production diagnostics API.</summary>
    private static OpenGLBackend Backend(RHICommandContext context) => (OpenGLBackend)typeof(RHICommandContext)
        .GetField("backend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
    #endregion
}
