using System;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks change-driven geometry and pass preparation independently of requested native rendering effects.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GraphicsPreparationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Unrelated storage and content changes preserve preparation; shrink withdraws it until a legal replacement.</summary>
    [Fact]
    public void GeometryRefreshIsLocalAndFailedBoundsRemainWithdrawn()
    {
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

    /// <summary>Repeated pass requests reuse structural facts while executing each clear and refreshing after attachment resize.</summary>
    [Fact]
    public unsafe void PassReuseNeverSuppressesRequestedClears()
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var texture = commands.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 1, Height = 1,
            Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(texture), Load: AttachmentLoad.Clear,
            Clear: ColorClearValue.Float(1, 0, 0, 1))]);
        var backend = Backend(commands);
        int allocations = 0;
        using var native = new NativeDriverCapture();
        nint create = native.Original("glCreateFramebuffers");
        native.Add("glCreateFramebuffers", new CreateFramebuffers((count, names) =>
        {
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)create)(count, names);
            native.Observe(() => allocations += count);
        }));
        native.Install();
        commands.DescribeRenderTargets(pass);
        Assert.Equal(0, allocations);
        void Execute()
        {
            using var boundary = commands.EnterBoundary(new("Tests.PreparedClear", true, operations: BoundaryOperations.RenderPasses));
            boundary.Run(() => { commands.BeginRenderPass(pass); commands.EndRenderPass(); });
        }
        Execute();
        Assert.Equal(1, allocations);
#if DEBUG
        long prepared = backend.PassPreparations;
#endif
        commands.ClearTexture(texture, new(new(), 0, 0, 0, 1, 1), new(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte), new byte[] { 0, 255, 0, 255 });
        Execute();
        Assert.Equal(1, allocations);
#if DEBUG
        Assert.Equal(prepared, backend.PassPreparations);
#endif
        Assert.Equal(1, texture.ReadPixels()[0]);
        Assert.True(texture.Resize(2, 1));
        Execute();
#if DEBUG
        Assert.Equal(prepared + 1, backend.PassPreparations);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        native.RequireSuccessfulObservation();
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
