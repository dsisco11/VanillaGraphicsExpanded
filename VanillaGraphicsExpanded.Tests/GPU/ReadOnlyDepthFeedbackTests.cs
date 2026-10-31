using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks attached-depth sampling with actual RHI draws and native barrier observation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ReadOnlyDepthFeedbackTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Recording preserves explicit feedback intent without issuing native work before submission.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void RecordedBarrierPreservesFramebufferFeedbackIntent(bool framebufferFeedback)
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        int barriers = 0;
        using var native = new NativeDriverCapture();
        nint original = native.Original("glTextureBarrier");
        native.Add("glTextureBarrier", new Barrier(() =>
        {
            ((delegate* unmanaged[Stdcall]<void>)original)();
            native.Observe(() => barriers++);
        }));
        native.Install();
        using var list = commands.CreateCommandList();
        list.Barrier(new(ResourceAccess.None, FramebufferFeedback: framebufferFeedback));
        list.Close();
        // Native ordering belongs to replay; the stored flag must survive recording unchanged.
        Assert.Equal(0, barriers);
        var result = commands.Submit(list);
        Assert.Null(result.Error);
        Assert.Equal(CommandListState.Executed, result.State);
        Assert.Equal(framebufferFeedback ? 1 : 0, barriers);
        native.RequireSuccessfulObservation();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Cloud and water depth states can sample attached depth, but changing to writable depth rejects before drawing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void ReadOnlyDepthUsesExplicitOrderingAndPipelineChangesRevalidate(bool depthTest)
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var depth = new DepthTexture(2, 2, TextureFormat.DepthComponent32f);
        using var color = new GpuFramebufferAttachment(2, 2, TextureFormat.R32f);
        using var depthImage = GpuFramebufferAttachment.FromTexture(depth);
        using var target = DynamicGpuFramebuffer.Create([color], depthImage)!;
        var pass = FramebufferTestSetup.DescribeAttachments(target);
        var layout = new RHIVertexLayout([new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12)]);
        using var vertices = commands.CreateVertexBuffer(new()
        {
            Role = BufferRole.Vertex, Capacity = 36, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw
        }).RequireResource();
        vertices.UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo> { [0] = vertices });
        var signature = commands.DescribeRenderTargets(pass);
        using var readOnly = commands.CreateGraphicsPipeline(new(shader.Shader, layout, signature,
            DynamicPipelineState.Viewport, depthStencil: new() { DepthTest = depthTest, DepthWrite = false })).RequireResource();
        using var writable = commands.CreateGraphicsPipeline(new(shader.Shader, layout, signature,
            DynamicPipelineState.Viewport, depthStencil: new() { DepthTest = true, DepthWrite = true })).RequireResource();
        shader.HzbDepth = depth; shader.SrcMip = 0;
        int barriers = 0;
        using var native = new NativeDriverCapture();
        nint original = native.Original("glTextureBarrier");
        native.Add("glTextureBarrier", new Barrier(() =>
        {
            ((delegate* unmanaged[Stdcall]<void>)original)();
            native.Observe(() => barriers++);
        }));
        native.Install();
        using var boundary = commands.EnterBoundary(new("Tests.ReadOnlyDepth", true,
            graphics: [readOnly, writable], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.SetPipeline(readOnly);
            commands.BindResources(shader.CaptureBindings());
            int before = barriers;
            commands.Draw(geometry.Inputs, new(0, 3));
            Assert.Equal(before, barriers);
            // Reusing bindings must not retain the previous pipeline's read-only permission.
            commands.SetPipeline(writable);
            Assert.Throws<InvalidOperationException>(() => commands.Draw(geometry.Inputs, new(0, 3)));
            Assert.Equal(before, barriers);
            commands.SetPipeline(readOnly);
            commands.Draw(geometry.Inputs, new(0, 3));
            Assert.Equal(before, barriers);
            commands.Barrier(new(ResourceAccess.None, FramebufferFeedback: true));
            Assert.Equal(before + 1, barriers);
            commands.Draw(geometry.Inputs, new(0, 3));
            Assert.Equal(before + 1, barriers);
            commands.EndRenderPass();
        });
        native.RequireSuccessfulObservation();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Matches the native texture-barrier ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void Barrier();
    #endregion
}
